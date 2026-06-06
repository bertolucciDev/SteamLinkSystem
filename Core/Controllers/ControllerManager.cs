using System.Threading.Channels;
using Core.Logging;

namespace Core.Controllers;

public sealed class ControllerManager : IDisposable
{
    private static readonly TimeSpan HotplugScanInterval = TimeSpan.FromSeconds(5);

    private readonly Channel<NavigationAction> _navigationEvents = Channel.CreateUnbounded<NavigationAction>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false
    });
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Dictionary<string, LinuxInputReader> _readers = new(StringComparer.Ordinal);
    private readonly List<Task> _readerTasks = new();
    private readonly Dictionary<NavigationAction, DateTime> _lastActionUtc = new();
    private readonly object _sync = new();
    private Task? _hotplugTask;
    private bool _started;

    public event Action<NavigationAction>? NavigationReceived;

    public IReadOnlyList<ControllerState> States
    {
        get
        {
            lock (_sync)
                return _readers.Values.Select(reader => reader.State).ToArray();
        }
    }

    public int ConnectedDeviceCount => States.Count(state => state.Connected);
    public ChannelReader<NavigationAction> NavigationEvents => _navigationEvents.Reader;

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        ScanForControllers();
        _hotplugTask = Task.Run(() => HotplugLoopAsync(_cancellation.Token));
        Logger.Info($"Controller manager started with {ConnectedDeviceCount} active device reader(s)", "Controllers");
    }

    public bool TryReadNavigation(out NavigationAction action)
    {
        Start();
        return _navigationEvents.Reader.TryRead(out action);
    }

    public async Task<NavigationAction> ReadNavigationAsync(CancellationToken cancellationToken = default)
    {
        Start();
        return await _navigationEvents.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task HotplugLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HotplugScanInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                ScanForControllers();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ScanForControllers()
    {
        foreach (var device in FindEventDevices())
        {
            lock (_sync)
            {
                if (_readers.ContainsKey(device.DevicePath))
                    continue;

                var reader = new LinuxInputReader(device, EmitNavigation);
                _readers[device.DevicePath] = reader;
                _readerTasks.Add(Task.Run(() => reader.RunAsync(_cancellation.Token)));
                Logger.Info($"Detected controller candidate: {device.Name} ({device.DevicePath})", "Controllers");
            }
        }
    }

    private static IEnumerable<ControllerDevice> FindEventDevices()
    {
        var devices = new SortedDictionary<string, ControllerDevice>(StringComparer.Ordinal);
        AddDevicesFromDirectory(devices, "/dev/input/by-id", "*event-joystick", "evdev/by-id");
        AddDevicesFromDirectory(devices, "/dev/input/by-id", "*event-gamepad", "evdev/by-id");
        AddDevicesFromDirectory(devices, "/dev/input/by-path", "*event-joystick", "evdev/by-path");
        AddDevicesFromDirectory(devices, "/dev/input/by-path", "*event-gamepad", "evdev/by-path");
        AddDevicesFromDirectory(devices, "/dev/input", "event*", "evdev");
        return devices.Values;
    }

    private static void AddDevicesFromDirectory(IDictionary<string, ControllerDevice> devices, string directory, string pattern, string backend)
    {
        try
        {
            if (!Directory.Exists(directory))
                return;

            foreach (var path in Directory.EnumerateFiles(directory, pattern))
            {
                var resolvedPath = ResolveDevicePath(path);
                var name = BuildDeviceName(path);
                var likelyController = IsLikelyControllerName(name) || pattern.Contains("joystick", StringComparison.OrdinalIgnoreCase) || pattern.Contains("gamepad", StringComparison.OrdinalIgnoreCase);
                devices.TryAdd(resolvedPath, new ControllerDevice(name, resolvedPath, backend, likelyController));
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Unable to enumerate {directory}: {ex.Message}", "Controllers");
        }
    }

    private static string ResolveDevicePath(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            return fileInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
        catch
        {
            return path;
        }
    }

    private static string BuildDeviceName(string path)
    {
        var fileName = Path.GetFileName(path);
        var name = (string.IsNullOrWhiteSpace(fileName) ? path : fileName).Replace("-event-joystick", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-event-gamepad", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace('_', ' ')
            .Replace('-', ' ');
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static bool IsLikelyControllerName(string name) =>
        name.Contains("xbox", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("microsoft", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("dualshock", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("dualsense", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("playstation", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("sony", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("gamepad", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("joystick", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("controller", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("steam", StringComparison.OrdinalIgnoreCase);

    private void EmitNavigation(ControllerDevice device, ControllerState state, NavigationAction action)
    {
        if (!CanEmit(action))
            return;

        Logger.Debug($"Navigation event {action} from {device.Name} ({state.DevicePath})", "Controllers");
        NavigationReceived?.Invoke(action);
        _navigationEvents.Writer.TryWrite(action);
    }

    private bool CanEmit(NavigationAction action)
    {
        if (action == NavigationAction.None)
            return false;

        var now = DateTime.UtcNow;
        var guard = action is NavigationAction.Up or NavigationAction.Down or NavigationAction.Left or NavigationAction.Right
            ? TimeSpan.FromMilliseconds(160)
            : TimeSpan.FromMilliseconds(250);

        lock (_lastActionUtc)
        {
            if (_lastActionUtc.TryGetValue(action, out var last) && now - last < guard)
                return false;

            _lastActionUtc[action] = now;
            return true;
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        lock (_sync)
        {
            foreach (var reader in _readers.Values)
                reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _readers.Clear();
        }
        _navigationEvents.Writer.TryComplete();
        _cancellation.Dispose();
    }
}
