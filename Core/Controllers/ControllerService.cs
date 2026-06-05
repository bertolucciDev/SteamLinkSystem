using System.Buffers.Binary;
using System.Threading.Channels;
using Core.Logging;

namespace Core.Controllers;

public enum ControllerNavigationAction
{
    Up,
    Down,
    Left,
    Right,
    Select,
    Back
}

public sealed class ControllerService : IDisposable
{
    private const ushort EvKey = 0x01;
    private const ushort EvAbs = 0x03;
    private const int AnalogThreshold = 20000;
    private static readonly TimeSpan RepeatGuard = TimeSpan.FromMilliseconds(180);

    private readonly Channel<ControllerNavigationAction> _events = Channel.CreateUnbounded<ControllerNavigationAction>();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<Task> _readers = new();
    private readonly List<FileStream> _openStreams = new();
    private readonly Dictionary<ControllerNavigationAction, DateTime> _lastActionUtc = new();
    private bool _started;
    private int _deviceCount;

    public string Status => $"Controller navigation active for Xbox, PlayStation, and generic Linux gamepads. Devices detected: {_deviceCount}. Supported inputs: D-pad, left stick, A/Cross/Start to select, B/Circle/Select to go back.";

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        foreach (var devicePath in FindEventDevices())
        {
            var path = devicePath;
            var task = Task.Run(() => ReadDeviceAsync(path, _cancellation.Token));
            _readers.Add(task);
        }

        Logger.Info($"Controller navigation started with {_deviceCount} input device readers", "Controllers");
    }

    public bool TryReadNavigation(out ControllerNavigationAction action)
    {
        Start();
        return _events.Reader.TryRead(out action);
    }

    public async Task<ControllerNavigationAction> ReadNavigationAsync(CancellationToken cancellationToken = default)
    {
        Start();
        return await _events.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    private IEnumerable<string> FindEventDevices()
    {
        var devices = new SortedSet<string>(StringComparer.Ordinal);
        AddDevicesFromDirectory(devices, "/dev/input/by-id", "*event-joystick");
        AddDevicesFromDirectory(devices, "/dev/input/by-id", "*event-gamepad");
        AddDevicesFromDirectory(devices, "/dev/input/by-path", "*event-joystick");
        AddDevicesFromDirectory(devices, "/dev/input", "event*");
        return devices;
    }

    private static void AddDevicesFromDirectory(ISet<string> devices, string directory, string pattern)
    {
        try
        {
            if (!Directory.Exists(directory))
                return;

            foreach (var path in Directory.EnumerateFiles(directory, pattern))
                devices.Add(path);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Unable to enumerate {directory}: {ex.Message}", "Controllers");
        }
    }

    private async Task ReadDeviceAsync(string path, CancellationToken cancellationToken)
    {
        var counted = false;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 24, FileOptions.Asynchronous);
            lock (_openStreams)
                _openStreams.Add(stream);
            Interlocked.Increment(ref _deviceCount);
            counted = true;
            Logger.Info($"Reading controller input from {path}", "Controllers");

            var eventSize = IntPtr.Size == 8 ? 24 : 16;
            var buffer = new byte[eventSize];
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await ReadExactAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
                if (!read)
                    break;

                DecodeInputEvent(buffer);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warning($"No permission to read controller device {path}: {ex.Message}", "Controllers");
        }
        catch (IOException ex)
        {
            Logger.Debug($"Controller device {path} closed: {ex.Message}", "Controllers");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Controller reader failed for {path}: {ex.Message}", "Controllers");
        }
        finally
        {
            if (counted)
                Interlocked.Decrement(ref _deviceCount);
        }
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return false;
            offset += read;
        }

        return true;
    }

    private void DecodeInputEvent(byte[] buffer)
    {
        var offset = IntPtr.Size == 8 ? 16 : 8;
        var type = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2));
        var code = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 2, 2));
        var value = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset + 4, 4));

        if (type == EvKey && value is 1 or 2)
            Emit(MapButton(code));
        else if (type == EvAbs)
            Emit(MapAxis(code, value));
    }

    private static ControllerNavigationAction? MapButton(ushort code) => code switch
    {
        256 or 288 or 304 or 315 => ControllerNavigationAction.Select, // Generic primary, Xbox A, PlayStation Cross, Start
        257 or 289 or 305 or 314 => ControllerNavigationAction.Back,   // Generic secondary, Xbox B, PlayStation Circle, Select
        544 => ControllerNavigationAction.Up,
        545 => ControllerNavigationAction.Down,
        546 => ControllerNavigationAction.Left,
        547 => ControllerNavigationAction.Right,
        _ => null
    };

    private static ControllerNavigationAction? MapAxis(ushort code, int value) => code switch
    {
        16 when value < 0 => ControllerNavigationAction.Left,
        16 when value > 0 => ControllerNavigationAction.Right,
        17 when value < 0 => ControllerNavigationAction.Up,
        17 when value > 0 => ControllerNavigationAction.Down,
        0 when value < -AnalogThreshold => ControllerNavigationAction.Left,
        0 when value > AnalogThreshold => ControllerNavigationAction.Right,
        1 when value < -AnalogThreshold => ControllerNavigationAction.Up,
        1 when value > AnalogThreshold => ControllerNavigationAction.Down,
        _ => null
    };

    private void Emit(ControllerNavigationAction? action)
    {
        if (action is null || !CanEmit(action.Value))
            return;

        _events.Writer.TryWrite(action.Value);
    }

    private bool CanEmit(ControllerNavigationAction action)
    {
        var now = DateTime.UtcNow;
        lock (_lastActionUtc)
        {
            if (_lastActionUtc.TryGetValue(action, out var last) && now - last < RepeatGuard)
                return false;

            _lastActionUtc[action] = now;
            return true;
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        lock (_openStreams)
        {
            foreach (var stream in _openStreams)
                stream.Dispose();
            _openStreams.Clear();
        }
        _cancellation.Dispose();
    }
}
