using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Core.Bluetooth;

/// <summary>
/// Bluetooth service providing high-level control of Bluetooth subsystem.
/// 
/// This service manages:
/// - Persistent bluetoothctl session (BluetoothTerminal)
/// - Device cache and state management (BluetoothSessionManager)
/// - Discovery flow and device operations
/// 
/// Reference: PRD Sections 3.4, 10 (Stateful Tool Awareness, Bluetooth Requirements)
/// </summary>
public class BluetoothService : IDisposable
{
    private static BluetoothService? _instance;
    private static readonly object _instanceLock = new();

    private readonly BluetoothTerminal _terminal;
    private readonly BluetoothSessionManager _sessionManager;
    private bool _disposed = false;

    public static BluetoothService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new BluetoothService();
                }
            }
            return _instance;
        }
    }

    public bool IsActive => _terminal.IsActive;

    public IReadOnlyList<BluetoothDevice> CachedDevices
        => _sessionManager.CachedDevices;

    private BluetoothService()
    {
        _terminal = new BluetoothTerminal();
        _sessionManager = new BluetoothSessionManager();
    }

    /// <summary>
    /// Initialize the Bluetooth subsystem with required settings.
    /// 
    /// Startup commands per PRD Section 10:
    /// - power on
    /// - agent on
    /// - default-agent
    /// - discoverable on
    /// - pairable on
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            Logger.Info("Initializing Bluetooth subsystem", "BluetoothService");

            _terminal.Initialize();

            // Execute initialization sequence
            await _terminal.SendCommandAsync(BluetoothCommands.Power(true));
            await _terminal.SendCommandAsync(BluetoothCommands.Agent(true));
            await _terminal.SendCommandAsync(BluetoothCommands.DefaultAgent());
            await _terminal.SendCommandAsync(BluetoothCommands.Discoverable(true));
            await _terminal.SendCommandAsync(BluetoothCommands.Pairable(true));

            Logger.Info("Bluetooth subsystem initialized successfully", "BluetoothService");
        }
        catch (Exception ex)
        {
            Logger.Error($"Bluetooth initialization failed: {ex.Message}", "BluetoothService");
            throw;
        }
    }

    /// <summary>
    /// Get all discovered and paired devices with current state.
    /// 
    /// Uses intelligent caching to avoid unnecessary rescans.
    /// </summary>
    public async Task<List<BluetoothDevice>> GetDevicesAsync()
    {
        if (!IsActive)
            throw new InvalidOperationException("Bluetooth service is not active");

        try
        {
            // Return cached devices if valid
            if (_sessionManager.ScanCacheValid && _sessionManager.CachedDevices.Count > 0)
            {
                Logger.Debug("Returning cached device list", "BluetoothService");
                return new List<BluetoothDevice>(_sessionManager.CachedDevices);
            }

            Logger.Debug("Fetching device list from terminal", "BluetoothService");

            var allDevicesOutput = await _terminal.SendCommandAsync(BluetoothCommands.Devices());
            var pairedOutput = await _terminal.SendCommandAsync(BluetoothCommands.DevicesFilter("Paired"));
            var connectedOutput = await _terminal.SendCommandAsync(BluetoothCommands.DevicesFilter("Connected"));
            var trustedOutput = await _terminal.SendCommandAsync(BluetoothCommands.DevicesFilter("Trusted"));

            var allDevices = BluetoothParser.ParseDevices(allDevicesOutput);
            var pairedDevices = BluetoothParser.ParseDevices(pairedOutput);
            var connectedDevices = BluetoothParser.ParseDevices(connectedOutput);
            var trustedDevices = BluetoothParser.ParseDevices(trustedOutput);

            var enrichedDevices = BluetoothParser.EnrichDeviceStates(
                allDevices,
                pairedDevices,
                connectedDevices,
                trustedDevices
            );

            _sessionManager.UpdateDeviceCache(enrichedDevices);

            return enrichedDevices;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get devices: {ex.Message}", "BluetoothService");
            throw;
        }
    }

    /// <summary>
    /// Start Bluetooth device discovery scan.
    /// 
    /// Per PRD Section 10:
    /// - scan on
    /// - wait for asynchronous discovery
    /// - retrieve devices
    /// - scan off
    /// </summary>
    public async Task<List<BluetoothDevice>> ScanAsync(TimeSpan? duration = null)
    {
        if (!IsActive)
            throw new InvalidOperationException("Bluetooth service is not active");

        var scanDuration = duration ?? TimeSpan.FromSeconds(10);

        try
        {
            Logger.Info($"Starting Bluetooth scan for {scanDuration.TotalSeconds}s", "BluetoothService");

            _sessionManager.SetScanningState(true);

            // Start scan
            await _terminal.SendCommandAsync(BluetoothCommands.ScanOn());

            // Wait for discovery
            await Task.Delay(scanDuration);

            // Stop scan
            await _terminal.SendCommandAsync(BluetoothCommands.ScanOff());

            // Fetch discovered devices
            var devices = await GetDevicesAsync();

            _sessionManager.SetScanningState(false);

            Logger.Info($"Scan completed: {devices.Count} devices discovered", "BluetoothService");

            return devices;
        }
        catch (Exception ex)
        {
            _sessionManager.SetScanningState(false);
            Logger.Error($"Scan failed: {ex.Message}", "BluetoothService");
            throw;
        }
    }

    /// <summary>
    /// Pair with a Bluetooth device.
    /// </summary>
    public async Task PairAsync(BluetoothDevice device)
    {
        if (!IsActive)
            throw new InvalidOperationException("Bluetooth service is not active");

        try
        {
            Logger.Info($"Pairing with {device.Name} ({device.Mac})", "BluetoothService");

            await _terminal.SendCommandAsync(BluetoothCommands.Pair(device.Mac));
            await _terminal.SendCommandAsync(BluetoothCommands.Trust(device.Mac));
            await _terminal.SendCommandAsync(BluetoothCommands.Connect(device.Mac));

            _sessionManager.UpdateDeviceState(device.Mac, connected: true, paired: true, trusted: true);

            Logger.Info($"Successfully paired with {device.Name}", "BluetoothService");
        }
        catch (Exception ex)
        {
            Logger.Error($"Pairing failed: {ex.Message}", "BluetoothService");
            throw;
        }
    }

    /// <summary>
    /// Connect to a paired Bluetooth device.
    /// </summary>
    public async Task ConnectAsync(BluetoothDevice device)
    {
        if (!IsActive)
            throw new InvalidOperationException("Bluetooth service is not active");

        try
        {
            Logger.Info($"Connecting to {device.Name} ({device.Mac})", "BluetoothService");

            await _terminal.SendCommandAsync(BluetoothCommands.Connect(device.Mac));

            _sessionManager.UpdateDeviceState(device.Mac, connected: true);

            Logger.Info($"Successfully connected to {device.Name}", "BluetoothService");
        }
        catch (Exception ex)
        {
            Logger.Error($"Connection failed: {ex.Message}", "BluetoothService");
            throw;
        }
    }

    /// <summary>
    /// Disconnect from a Bluetooth device.
    /// </summary>
    public async Task DisconnectAsync(BluetoothDevice device)
    {
        if (!IsActive)
            throw new InvalidOperationException("Bluetooth service is not active");

        try
        {
            Logger.Info($"Disconnecting from {device.Name} ({device.Mac})", "BluetoothService");

            await _terminal.SendCommandAsync(BluetoothCommands.Disconnect(device.Mac));

            _sessionManager.UpdateDeviceState(device.Mac, connected: false);

            Logger.Info($"Successfully disconnected from {device.Name}", "BluetoothService");
        }
        catch (Exception ex)
        {
            Logger.Error($"Disconnection failed: {ex.Message}", "BluetoothService");
            throw;
        }
    }

    /// <summary>
    /// Remove a paired device.
    /// </summary>
    public async Task RemoveAsync(BluetoothDevice device)
    {
        if (!IsActive)
            throw new InvalidOperationException("Bluetooth service is not active");

        try
        {
            Logger.Info($"Removing {device.Name} ({device.Mac})", "BluetoothService");

            await _terminal.SendCommandAsync(BluetoothCommands.Remove(device.Mac));

            Logger.Info($"Successfully removed {device.Name}", "BluetoothService");
        }
        catch (Exception ex)
        {
            Logger.Error($"Remove failed: {ex.Message}", "BluetoothService");
            throw;
        }
    }

    /// <summary>
    /// Get detailed information about a device.
    /// </summary>
    public async Task<string> GetInfoAsync(BluetoothDevice device)
    {
        if (!IsActive)
            throw new InvalidOperationException("Bluetooth service is not active");

        try
        {
            Logger.Debug($"Fetching info for {device.Name} ({device.Mac})", "BluetoothService");

            var output = await _terminal.SendCommandAsync(BluetoothCommands.Info(device.Mac));
            return BluetoothParser.ParseCommandResponse(output);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get info: {ex.Message}", "BluetoothService");
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _terminal.Dispose();
            _sessionManager.Clear();
            Logger.Info("Bluetooth service disposed", "BluetoothService");
        }
        catch (Exception ex)
        {
            Logger.Error($"Error during service disposal: {ex.Message}", "BluetoothService");
        }
        finally
        {
            _disposed = true;
        }
    }
}
    }

    public static void Remove(BluetoothDevice device)
    {
        Shell.Run(
            BluetoothCommands.Remove(device.Mac)
        );
    }

    public static string Info(BluetoothDevice device)
    {
        return Shell.Run(
            BluetoothCommands.Info(device.Mac)
        );
    }
}