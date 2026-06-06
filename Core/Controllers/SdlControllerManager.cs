using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Core.Logging;

namespace Core.Controllers;

public sealed class SdlControllerManager : IDisposable
{
    private const uint SdlInitJoystick = 0x00000200;
    private const uint SdlInitGameController = 0x00002000;
    private const uint SdlInitEvents = 0x00004000;

    private const uint SdlWindowEvent = 0x200;
    private const byte SdlWindowEventFocusLost = 13;
    private const byte SdlWindowEventFocusGained = 12;
    private const uint SdlControllerAxisMotion = 0x650;
    private const uint SdlControllerButtonDown = 0x653;
    private const uint SdlControllerDeviceAdded = 0x655;
    private const uint SdlControllerDeviceRemoved = 0x656;

    private const byte SdlControllerAxisLeftX = 0;
    private const byte SdlControllerAxisLeftY = 1;
    private const short SdlAxisThreshold = 16000;

    private const byte SdlControllerButtonA = 0;
    private const byte SdlControllerButtonB = 1;
    private const byte SdlControllerButtonBack = 4;
    private const byte SdlControllerButtonStart = 6;
    private const byte SdlControllerButtonDpadUp = 11;
    private const byte SdlControllerButtonDpadDown = 12;
    private const byte SdlControllerButtonDpadLeft = 13;
    private const byte SdlControllerButtonDpadRight = 14;

    private static readonly TimeSpan EventPollDelay = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan InputHealthTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RecoveryCooldown = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdleRecoveryCooldown = TimeSpan.FromSeconds(15);

    private readonly Action<ControllerDevice, ControllerState, NavigationAction> _onNavigation;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<int, DateTime> _lastAxisEmitUtc = new();
    private readonly Dictionary<int, OpenController> _controllers = new();
    private bool _initialized;
    private bool _isRecovering;
    private bool _shutdown;
    private Task? _eventLoopTask;
    private Task? _watchdogTask;
    private DateTime _lastInputUtc = DateTime.MinValue;
    private DateTime _lastSdlEventUtc = DateTime.MinValue;
    private DateTime _lastRecoveryUtc = DateTime.MinValue;
    private int _lastJoystickCount = -1;

    public SdlControllerManager(Action<ControllerDevice, ControllerState, NavigationAction> onNavigation)
    {
        _onNavigation = onNavigation;
    }

    public bool IsAvailable { get; private set; }
    public int ConnectedControllerCount
    {
        get
        {
            lock (_sync)
                return _controllers.Count;
        }
    }

    public DateTime LastInputUtc => _lastInputUtc;

    public IReadOnlyList<string> ControllerSummaries
    {
        get
        {
            lock (_sync)
                return _controllers.Values
                    .Select(controller => $"- {controller.Device.Name} ({controller.Device.DevicePath}) [{controller.Device.Backend}] connected={controller.State.Connected} lastInput={controller.State.LastInputUtc:u}")
                    .ToArray();
        }
    }

    public void Initialize()
    {
        lock (_sync)
        {
            if (_initialized)
                return;

            try
            {
                var result = SDL_InitSubSystem(SdlInitJoystick | SdlInitGameController | SdlInitEvents);
                if (result != 0)
                {
                    Logger.Warning($"SDL controller subsystem initialization failed: {GetSdlError()}", "Controllers");
                    return;
                }

                SDL_GameControllerEventState(1);
                _initialized = true;
                IsAvailable = true;
                _lastSdlEventUtc = DateTime.UtcNow;
                ScanControllers();
                _eventLoopTask = Task.Run(() => EventLoopAsync(_cancellation.Token));
                StartWatchdog();
                Logger.Info("SDL GameController subsystem initialized", "Controllers");
            }
            catch (DllNotFoundException ex)
            {
                Logger.Warning($"SDL2 native library is not available; evdev fallback remains active. {ex.Message}", "Controllers");
            }
            catch (EntryPointNotFoundException ex)
            {
                Logger.Warning($"SDL2 GameController API is incomplete on this system; evdev fallback remains active. {ex.Message}", "Controllers");
            }
            catch (Exception ex)
            {
                Logger.Warning($"SDL controller initialization failed; evdev fallback remains active. {ex.Message}", "Controllers");
            }
        }
    }

    public void Shutdown()
    {
        lock (_sync)
        {
            if (_shutdown)
                return;

            _shutdown = true;
            _cancellation.Cancel();
            CloseControllers();
            if (_initialized)
                SDL_QuitSubSystem(SdlInitGameController | SdlInitJoystick | SdlInitEvents);
            _initialized = false;
            IsAvailable = false;
            Logger.Info("SDL GameController subsystem shut down", "Controllers");
        }
    }

    public void HandleSdlEvent(SDL_Event e)
    {
        _lastSdlEventUtc = DateTime.UtcNow;
        switch (e.type)
        {
            case SdlWindowEvent when e.window.@event == SdlWindowEventFocusLost:
                Logger.Info("SDL focus lost; controller state will be revalidated on focus gain/watchdog", "Controllers");
                break;
            case SdlWindowEvent when e.window.@event == SdlWindowEventFocusGained:
                Logger.Info("SDL focus gained; revalidating controllers", "Controllers");
                _ = Task.Run(RecoverControllers);
                break;
            case SdlControllerDeviceAdded:
                Logger.Info($"SDL controller device added at joystick index {e.cdevice.which}", "Controllers");
                ScanControllers();
                break;
            case SdlControllerDeviceRemoved:
                Logger.Info($"SDL controller device removed instance {e.cdevice.which}", "Controllers");
                RemoveController(e.cdevice.which);
                ScanControllers();
                break;
            case SdlControllerButtonDown:
                EmitButton(e.cbutton.which, e.cbutton.button);
                break;
            case SdlControllerAxisMotion:
                EmitAxis(e.caxis.which, e.caxis.axis, e.caxis.value);
                break;
        }
    }

    public InputHealthResult DetectInputHealth()
    {
        if (!_initialized || _shutdown)
            return new InputHealthResult(false, "SDL subsystem is not initialized.");

        int joystickCount;
        int openCount;
        var attachedCount = 0;
        lock (_sync)
        {
            joystickCount = SDL_NumJoysticks();
            openCount = _controllers.Count;
            foreach (var controller in _controllers.Values)
            {
                if (SDL_GameControllerGetAttached(controller.Handle))
                    attachedCount++;
            }
        }

        var now = DateTime.UtcNow;
        var noEvents = now - _lastSdlEventUtc > InputHealthTimeout && now - _lastRecoveryUtc > IdleRecoveryCooldown;
        var staleAfterInput = _lastInputUtc != DateTime.MinValue && now - _lastInputUtc > InputHealthTimeout && openCount > 0;
        var previousJoystickCount = _lastJoystickCount;
        var countChanged = previousJoystickCount >= 0 && joystickCount != previousJoystickCount;
        var attachedMismatch = openCount > 0 && attachedCount != openCount;
        _lastJoystickCount = joystickCount;

        if (attachedMismatch)
            return new InputHealthResult(false, $"SDL attached controller mismatch: open={openCount}, attached={attachedCount}, joysticks={joystickCount}.");

        if (countChanged)
            return new InputHealthResult(false, $"SDL joystick count changed and requires rescan: previous={previousJoystickCount}, current={joystickCount}.");

        if (joystickCount > 0 && openCount == 0)
            return new InputHealthResult(false, $"SDL sees {joystickCount} joystick(s) but no GameController handles are open.");

        if (joystickCount > 0 && noEvents)
            return new InputHealthResult(false, $"SDL has not emitted any events for {(now - _lastSdlEventUtc).TotalSeconds:F1}s while joystick(s) are present.");

        if (staleAfterInput)
            return new InputHealthResult(false, $"SDL input stream is stale for {(now - _lastInputUtc).TotalSeconds:F1}s after prior valid input.");

        return new InputHealthResult(true, $"SDL healthy: joysticks={joystickCount}, open={openCount}, attached={attachedCount}.");
    }

    public void RecoverControllers()
    {
        if (_shutdown || !_initialized)
            return;

        lock (_sync)
        {
            if (_isRecovering)
                return;

            if (DateTime.UtcNow - _lastRecoveryUtc < RecoveryCooldown)
                return;

            _isRecovering = true;
        }

        try
        {
            Logger.Warning("Recovering SDL controllers after focus/input health failure", "Controllers");
            lock (_sync)
            {
                CloseControllers();
                ReinitializeSubsystem();
                ScanControllers();
                _lastInputUtc = DateTime.MinValue;
                _lastSdlEventUtc = DateTime.UtcNow;
                _lastRecoveryUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"SDL controller recovery failed: {ex.Message}", "Controllers");
        }
        finally
        {
            lock (_sync)
                _isRecovering = false;
        }
    }

    public void ReinitializeSubsystem()
    {
        SDL_QuitSubSystem(SdlInitGameController);
        var result = SDL_InitSubSystem(SdlInitGameController);
        if (result != 0)
            Logger.Warning($"SDL GameController subsystem reinitialization failed: {GetSdlError()}", "Controllers");
        else
            SDL_GameControllerEventState(1);
    }

    public void ScanControllers()
    {
        if (!_initialized || _shutdown)
            return;

        lock (_sync)
        {
            var joystickCount = SDL_NumJoysticks();
            _lastJoystickCount = joystickCount;
            for (var index = 0; index < joystickCount; index++)
            {
                if (!SDL_IsGameController(index))
                    continue;

                var controller = SDL_GameControllerOpen(index);
                if (controller == IntPtr.Zero)
                {
                    Logger.Warning($"Unable to open SDL controller at index {index}: {GetSdlError()}", "Controllers");
                    continue;
                }

                var joystick = SDL_GameControllerGetJoystick(controller);
                var instanceId = joystick == IntPtr.Zero ? index : SDL_JoystickInstanceID(joystick);
                if (_controllers.ContainsKey(instanceId))
                {
                    SDL_GameControllerClose(controller);
                    continue;
                }

                var name = GetControllerName(controller, index);
                var device = new ControllerDevice(name, $"sdl:{instanceId}", "sdl2-gamecontroller", true);
                var state = new ControllerState(device);
                _controllers[instanceId] = new OpenController(controller, device, state);
                Logger.Info($"Opened SDL controller {name} instance={instanceId} index={index}", "Controllers");
            }
        }
    }

    public void StartWatchdog()
    {
        if (_watchdogTask is not null)
            return;

        _watchdogTask = Task.Run(() => WatchdogLoopAsync(_cancellation.Token));
    }

    private async Task EventLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var handled = false;
            while (SDL_PollEvent(out var sdlEvent) == 1)
            {
                handled = true;
                HandleSdlEvent(sdlEvent);
            }

            if (!handled)
                await Task.Delay(EventPollDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WatchdogLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(WatchdogInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var health = DetectInputHealth();
                if (!health.IsHealthy)
                {
                    Logger.Warning($"SDL controller health check failed: {health.Reason}", "Controllers");
                    RecoverControllers();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void EmitButton(int instanceId, byte button)
    {
        var action = button switch
        {
            SdlControllerButtonA => NavigationAction.Select,
            SdlControllerButtonB or SdlControllerButtonBack => NavigationAction.Back,
            SdlControllerButtonStart => NavigationAction.Home,
            SdlControllerButtonDpadUp => NavigationAction.Up,
            SdlControllerButtonDpadDown => NavigationAction.Down,
            SdlControllerButtonDpadLeft => NavigationAction.Left,
            SdlControllerButtonDpadRight => NavigationAction.Right,
            _ => NavigationAction.None
        };

        if (action == NavigationAction.None)
            return;

        if (TryGetController(instanceId, out var controller))
            controller.State.UpdateButton(button, ButtonState.Pressed);

        Emit(instanceId, action);
    }

    private void EmitAxis(int instanceId, byte axis, short value)
    {
        var action = axis switch
        {
            SdlControllerAxisLeftX when value < -SdlAxisThreshold => NavigationAction.Left,
            SdlControllerAxisLeftX when value > SdlAxisThreshold => NavigationAction.Right,
            SdlControllerAxisLeftY when value < -SdlAxisThreshold => NavigationAction.Up,
            SdlControllerAxisLeftY when value > SdlAxisThreshold => NavigationAction.Down,
            _ => NavigationAction.None
        };

        if (action == NavigationAction.None || !CanEmitAxis(instanceId, axis))
            return;

        if (TryGetController(instanceId, out var controller))
            controller.State.UpdateAxis(axis, value);

        Emit(instanceId, action);
    }

    private void Emit(int instanceId, NavigationAction action)
    {
        if (!TryGetController(instanceId, out var controller))
            return;

        _lastInputUtc = DateTime.UtcNow;
        _onNavigation(controller.Device, controller.State, action);
    }

    private bool TryGetController(int instanceId, out OpenController controller)
    {
        lock (_sync)
            return _controllers.TryGetValue(instanceId, out controller!);
    }

    private bool CanEmitAxis(int instanceId, byte axis)
    {
        var key = HashCode.Combine(instanceId, axis);
        var now = DateTime.UtcNow;
        if (_lastAxisEmitUtc.TryGetValue(key, out var last) && now - last < TimeSpan.FromMilliseconds(160))
            return false;

        _lastAxisEmitUtc[key] = now;
        return true;
    }

    private void RemoveController(int instanceId)
    {
        lock (_sync)
        {
            if (!_controllers.Remove(instanceId, out var controller))
                return;

            controller.State.MarkDisconnected();
            SDL_GameControllerClose(controller.Handle);
            Logger.Info($"Closed SDL controller {controller.Device.Name} instance={instanceId}", "Controllers");
        }
    }

    private void CloseControllers()
    {
        foreach (var controller in _controllers.Values)
        {
            controller.State.MarkDisconnected();
            SDL_GameControllerClose(controller.Handle);
        }

        _controllers.Clear();
        _lastAxisEmitUtc.Clear();
    }

    private static string GetControllerName(IntPtr controller, int index)
    {
        var handleName = SDL_GameControllerName(controller);
        if (handleName != IntPtr.Zero)
            return Marshal.PtrToStringUTF8(handleName) ?? $"SDL Controller {index}";

        var indexName = SDL_GameControllerNameForIndex(index);
        return indexName == IntPtr.Zero ? $"SDL Controller {index}" : Marshal.PtrToStringUTF8(indexName) ?? $"SDL Controller {index}";
    }

    private static string GetSdlError()
    {
        var error = SDL_GetError();
        return error == IntPtr.Zero ? "unknown SDL error" : Marshal.PtrToStringUTF8(error) ?? "unknown SDL error";
    }

    public void Dispose()
    {
        Shutdown();
        _cancellation.Dispose();
    }

    private sealed record OpenController(IntPtr Handle, ControllerDevice Device, ControllerState State);

    public readonly record struct InputHealthResult(bool IsHealthy, string Reason);

    [StructLayout(LayoutKind.Explicit, Size = 56)]
    public struct SDL_Event
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(0)] public SDL_WindowEvent window;
        [FieldOffset(0)] public SDL_ControllerDeviceEvent cdevice;
        [FieldOffset(0)] public SDL_ControllerButtonEvent cbutton;
        [FieldOffset(0)] public SDL_ControllerAxisEvent caxis;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_WindowEvent
    {
        public uint type;
        public uint timestamp;
        public uint windowID;
        public byte @event;
        private byte padding1;
        private byte padding2;
        private byte padding3;
        public int data1;
        public int data2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_ControllerDeviceEvent
    {
        public uint type;
        public uint timestamp;
        public int which;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_ControllerButtonEvent
    {
        public uint type;
        public uint timestamp;
        public int which;
        public byte button;
        public byte state;
        private byte padding1;
        private byte padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_ControllerAxisEvent
    {
        public uint type;
        public uint timestamp;
        public int which;
        public byte axis;
        private byte padding1;
        private byte padding2;
        private byte padding3;
        public short value;
        private ushort padding4;
    }

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_InitSubSystem(uint flags);

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_QuitSubSystem(uint flags);

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_NumJoysticks();

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SDL_IsGameController(int joystickIndex);

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GameControllerOpen(int joystickIndex);

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_GameControllerClose(IntPtr gameController);

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SDL_GameControllerGetAttached(IntPtr gameController);

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GameControllerGetJoystick(IntPtr gameController);

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_JoystickInstanceID(IntPtr joystick);

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GameControllerName(IntPtr gameController);

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GameControllerNameForIndex(int joystickIndex);

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GameControllerEventState(int state);

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_PollEvent(out SDL_Event sdlEvent);

    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetError();
}
