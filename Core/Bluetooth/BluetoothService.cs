using Core.Logging;

namespace Core.Bluetooth;

public sealed class BluetoothService : IAsyncDisposable, IDisposable
{
    private readonly BluetoothTerminal _terminal = new();
    private readonly BluetoothSessionManager _session = new();
    private bool _initialized;
    private bool _disposed;

    public bool IsActive => _terminal.IsActive;
    public List<BluetoothDevice> CachedDevices => _session.Snapshot();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        try
        {
            _terminal.Start();
            Logger.Info("Initializing adapter state", "Bluetooth");
            await _terminal.SendCommand(BluetoothCommands.Power(true), TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(false);
            await _terminal.SendCommand(BluetoothCommands.Agent(true), TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(false);
            await _terminal.SendCommand(BluetoothCommands.DefaultAgent(), TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(false);
            await _terminal.SendCommand(BluetoothCommands.Discoverable(true), TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(false);
            await _terminal.SendCommand(BluetoothCommands.Pairable(true), TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(false);
            _initialized = true;
            Logger.Info("Bluetooth adapter initialization complete", "Bluetooth");
        }
        catch (Exception ex)
        {
            Logger.Error($"Bluetooth initialization failed: {ex.Message}", "Bluetooth");
            throw;
        }
    }

    public async Task<List<BluetoothDevice>> ScanDevicesAsync(TimeSpan? duration = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var scanDuration = duration ?? TimeSpan.FromSeconds(8);
        _session.SetScanning(true);

        try
        {
            Logger.Info($"Starting scan for {scanDuration.TotalSeconds:0}s", "Bluetooth");
            await _terminal.SendCommand(BluetoothCommands.Scan(true), TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
            await Task.Delay(scanDuration, cancellationToken).ConfigureAwait(false);
            var devicesOutput = await _terminal.SendCommand(BluetoothCommands.Devices(), TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
            await _terminal.SendCommand(BluetoothCommands.Scan(false), TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);

            var discovered = BluetoothParser.ParseDevices(devicesOutput);
            _session.Merge(discovered);
            await RefreshDeviceStatesAsync(cancellationToken).ConfigureAwait(false);
            Logger.Info($"Scan completed with {_session.Snapshot().Count} cached devices", "Bluetooth");
            return _session.Snapshot();
        }
        catch (Exception ex)
        {
            Logger.Error($"Bluetooth scan failed: {ex.Message}", "Bluetooth");
            try
            {
                await _terminal.SendCommand(BluetoothCommands.Scan(false), TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception stopEx)
            {
                Logger.Warning($"Unable to stop scan after failure: {stopEx.Message}", "Bluetooth");
            }
            throw;
        }
        finally
        {
            _session.SetScanning(false);
        }
    }

    public async Task<List<BluetoothDevice>> ListDevicesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (!forceRefresh && _session.CacheIsFresh && _session.Snapshot().Count > 0)
            return _session.Snapshot();

        var output = await _terminal.SendCommand(BluetoothCommands.Devices(), TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
        _session.Merge(BluetoothParser.ParseDevices(output));
        await RefreshDeviceStatesAsync(cancellationToken).ConfigureAwait(false);
        return _session.Snapshot();
    }

    public async Task<List<BluetoothDevice>> ListPairedDevicesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var devices = BluetoothParser.ParseDevices(await _terminal.SendCommand(BluetoothCommands.PairedDevices(), TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false));
        BluetoothParser.MarkState(devices, paired: true);
        _session.Merge(devices);
        return devices;
    }

    public async Task<List<BluetoothDevice>> ListTrustedDevicesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var devices = BluetoothParser.ParseDevices(await _terminal.SendCommand(BluetoothCommands.TrustedDevices(), TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false));
        BluetoothParser.MarkState(devices, trusted: true);
        _session.Merge(devices);
        return devices;
    }

    public async Task<List<BluetoothDevice>> ListConnectedDevicesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var devices = BluetoothParser.ParseDevices(await _terminal.SendCommand(BluetoothCommands.ConnectedDevices(), TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false));
        BluetoothParser.MarkState(devices, connected: true);
        _session.Merge(devices);
        return devices;
    }

    public async Task<string> PairDeviceAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        Logger.Info($"Pairing {device.DisplayName} ({device.Mac})", "Bluetooth");
        var response = await _terminal.SendCommand(BluetoothCommands.Pair(device.Mac), TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        _session.UpdateState(device.Mac, paired: true);
        return BluetoothParser.CleanResponse(response);
    }

    public async Task<string> TrustDeviceAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        Logger.Info($"Trusting {device.DisplayName} ({device.Mac})", "Bluetooth");
        var response = await _terminal.SendCommand(BluetoothCommands.Trust(device.Mac), TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        _session.UpdateState(device.Mac, trusted: true);
        return BluetoothParser.CleanResponse(response);
    }

    public async Task<string> ConnectDeviceAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        Logger.Info($"Connecting {device.DisplayName} ({device.Mac})", "Bluetooth");
        var response = await _terminal.SendCommand(BluetoothCommands.Connect(device.Mac), TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        _session.UpdateState(device.Mac, connected: true);
        return BluetoothParser.CleanResponse(response);
    }

    public async Task<string> DisconnectDeviceAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        Logger.Info($"Disconnecting {device.DisplayName} ({device.Mac})", "Bluetooth");
        var response = await _terminal.SendCommand(BluetoothCommands.Disconnect(device.Mac), TimeSpan.FromSeconds(12), cancellationToken).ConfigureAwait(false);
        _session.UpdateState(device.Mac, connected: false);
        return BluetoothParser.CleanResponse(response);
    }

    public async Task<string> RemoveDeviceAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        Logger.Info($"Removing {device.DisplayName} ({device.Mac})", "Bluetooth");
        var response = await _terminal.SendCommand(BluetoothCommands.Remove(device.Mac), TimeSpan.FromSeconds(12), cancellationToken).ConfigureAwait(false);
        _session.Remove(device.Mac);
        return BluetoothParser.CleanResponse(response);
    }

    public async Task<string> GetDeviceInfoAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var response = await _terminal.SendCommand(BluetoothCommands.Info(device.Mac), TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
        var enriched = BluetoothParser.EnrichFromInfo(device, response);
        _session.Merge(new[] { enriched }, refreshTimestamp: false);
        return BluetoothParser.CleanResponse(response);
    }

    private async Task RefreshDeviceStatesAsync(CancellationToken cancellationToken)
    {
        var paired = await ListPairedDevicesAsync(cancellationToken).ConfigureAwait(false);
        var trusted = await ListTrustedDevicesAsync(cancellationToken).ConfigureAwait(false);
        var connected = await ListConnectedDevicesAsync(cancellationToken).ConfigureAwait(false);
        _session.Merge(BluetoothParser.MergeDevices(paired, trusted, connected));
        _session.ReplaceKnownStates(paired, trusted, connected);
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken) => _initialized ? Task.CompletedTask : InitializeAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _terminal.Dispose();
        _session.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _terminal.DisposeAsync().ConfigureAwait(false);
        _session.Clear();
    }
}
