using System.Buffers.Binary;
using Core.Logging;

namespace Core.Controllers;

public sealed class LinuxInputReader : IAsyncDisposable
{
    private const ushort EvKey = 0x01;
    private const ushort EvAbs = 0x03;
    private const int EventBufferSize = 24;

    private readonly ControllerDevice _device;
    private readonly Action<ControllerDevice, ControllerState, NavigationAction> _onNavigation;
    private FileStream? _stream;

    public LinuxInputReader(ControllerDevice device, Action<ControllerDevice, ControllerState, NavigationAction> onNavigation)
    {
        _device = device;
        _onNavigation = onNavigation;
        State = new ControllerState(device);
    }

    public ControllerState State { get; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(_device.DevicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, EventBufferSize, FileOptions.Asynchronous);
            _stream = stream;
            Logger.Info($"Controller connected: {_device.Name} at {_device.DevicePath} via {_device.Backend}", "Controllers");

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
            Logger.Warning($"No permission to read {_device.DevicePath}. Add the service user to input,audio,video,render groups. {ex.Message}", "Controllers");
        }
        catch (IOException ex)
        {
            Logger.Warning($"Controller disconnected or input read failed for {_device.DevicePath}: {ex.Message}", "Controllers");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Controller reader failed for {_device.DevicePath}: {ex.Message}", "Controllers");
        }
        finally
        {
            State.MarkDisconnected();
            Logger.Info($"Controller disconnected: {_device.Name} at {_device.DevicePath}", "Controllers");
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

        var action = NavigationAction.None;
        if (type == EvKey)
        {
            var state = value switch
            {
                0 => ButtonState.Released,
                1 => ButtonState.Pressed,
                2 => ButtonState.Held,
                _ => ButtonState.Released
            };
            State.UpdateButton(code, state);
            action = InputMapper.MapButton(code, state);
        }
        else if (type == EvAbs)
        {
            State.UpdateAxis(code, value);
            action = InputMapper.MapAxis(code, value);
        }

        if (action != NavigationAction.None)
            _onNavigation(_device, State, action);
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
            await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
