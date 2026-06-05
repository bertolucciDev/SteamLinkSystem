using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Bluetooth;

/// <summary>
/// Manages internal Bluetooth session state and device cache.
/// 
/// Responsibilities:
/// - Maintain current device cache
/// - Track scanning state
/// - Prevent unnecessary rescans
/// - Manage session lifecycle
/// 
/// Reference: PRD Section 18 (State Management)
/// </summary>
public class BluetoothSessionManager
{
    private List<BluetoothDevice> _deviceCache = new();
    private bool _isScanning = false;
    private DateTime _lastScanTime = DateTime.MinValue;
    private TimeSpan _scanCacheExpiry = TimeSpan.FromSeconds(30);
    private readonly object _lock = new();

    public IReadOnlyList<BluetoothDevice> CachedDevices
    {
        get
        {
            lock (_lock)
            {
                return _deviceCache.AsReadOnly();
            }
        }
    }

    public bool IsScanning
    {
        get
        {
            lock (_lock)
            {
                return _isScanning;
            }
        }
    }

    public bool ScanCacheValid
    {
        get
        {
            lock (_lock)
            {
                return DateTime.UtcNow - _lastScanTime < _scanCacheExpiry;
            }
        }
    }

    public void UpdateDeviceCache(List<BluetoothDevice> devices)
    {
        lock (_lock)
        {
            _deviceCache = devices;
            _lastScanTime = DateTime.UtcNow;

            Logger.Debug(
                $"Device cache updated: {devices.Count} devices",
                "BluetoothSessionManager"
            );
        }
    }

    public void SetScanningState(bool isScanning)
    {
        lock (_lock)
        {
            _isScanning = isScanning;
            Logger.Debug($"Scanning state: {isScanning}", "BluetoothSessionManager");
        }
    }

    public BluetoothDevice? FindDevice(string mac)
    {
        lock (_lock)
        {
            return _deviceCache.FirstOrDefault(d => d.Mac == mac);
        }
    }

    public void UpdateDeviceState(
        string mac,
        bool? connected = null,
        bool? paired = null,
        bool? trusted = null
    )
    {
        lock (_lock)
        {
            var device = _deviceCache.FirstOrDefault(d => d.Mac == mac);
            if (device == null)
                return;

            if (connected.HasValue)
                device.Connected = connected.Value;
            if (paired.HasValue)
                device.Paired = paired.Value;
            if (trusted.HasValue)
                device.Trusted = trusted.Value;

            Logger.Debug(
                $"Device {device.Name} state updated: C={device.Connected} P={device.Paired} T={device.Trusted}",
                "BluetoothSessionManager"
            );
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _deviceCache.Clear();
            _isScanning = false;
            _lastScanTime = DateTime.MinValue;

            Logger.Debug("Session state cleared", "BluetoothSessionManager");
        }
    }
}
