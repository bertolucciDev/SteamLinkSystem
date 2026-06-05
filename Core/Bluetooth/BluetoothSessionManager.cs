using Core.Logging;

namespace Core.Bluetooth;

public sealed class BluetoothSessionManager
{
    private readonly object _lock = new();
    private readonly Dictionary<string, BluetoothDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(30);
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    public bool IsScanning { get; private set; }
    public bool CacheIsFresh => DateTime.UtcNow - _lastRefreshUtc < _cacheDuration;

    public List<BluetoothDevice> Snapshot()
    {
        lock (_lock)
            return _devices.Values.Select(device => device.Clone()).OrderByDescending(d => d.Connected).ThenByDescending(d => d.Paired).ThenBy(d => d.DisplayName).ToList();
    }

    public void Merge(IEnumerable<BluetoothDevice> devices, bool refreshTimestamp = true)
    {
        lock (_lock)
        {
            foreach (var device in devices)
                UpsertLocked(device);

            if (refreshTimestamp)
                _lastRefreshUtc = DateTime.UtcNow;

            Logger.Debug($"Bluetooth cache contains {_devices.Count} devices", "BluetoothSession");
        }
    }

    public void UpdateState(string mac, bool? paired = null, bool? trusted = null, bool? connected = null)
    {
        lock (_lock)
        {
            if (!_devices.TryGetValue(mac, out var device))
                return;

            if (paired.HasValue)
                device.Paired = paired.Value;
            if (trusted.HasValue)
                device.Trusted = trusted.Value;
            if (connected.HasValue)
                device.Connected = connected.Value;
        }
    }


    public void ReplaceKnownStates(IEnumerable<BluetoothDevice> paired, IEnumerable<BluetoothDevice> trusted, IEnumerable<BluetoothDevice> connected)
    {
        var pairedMacs = paired.Select(d => d.Mac).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trustedMacs = trusted.Select(d => d.Mac).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var connectedMacs = connected.Select(d => d.Mac).ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            foreach (var device in _devices.Values)
            {
                device.Paired = pairedMacs.Contains(device.Mac);
                device.Trusted = trustedMacs.Contains(device.Mac);
                device.Connected = connectedMacs.Contains(device.Mac);
            }
        }
    }

    public void Remove(string mac)
    {
        lock (_lock)
            _devices.Remove(mac);
    }

    public void SetScanning(bool scanning)
    {
        lock (_lock)
            IsScanning = scanning;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _devices.Clear();
            IsScanning = false;
            _lastRefreshUtc = DateTime.MinValue;
        }
    }

    private void UpsertLocked(BluetoothDevice incoming)
    {
        if (!_devices.TryGetValue(incoming.Mac, out var existing))
        {
            _devices[incoming.Mac] = incoming.Clone();
            return;
        }

        if (!string.IsNullOrWhiteSpace(incoming.Name) && incoming.Name != "Unknown Device")
            existing.Name = incoming.Name;
        existing.Paired = incoming.Paired || existing.Paired;
        existing.Trusted = incoming.Trusted || existing.Trusted;
        existing.Connected = incoming.Connected || existing.Connected;
        existing.Rssi = incoming.Rssi ?? existing.Rssi;
        existing.BatteryLevel = incoming.BatteryLevel ?? existing.BatteryLevel;
        existing.LastSeenUtc = incoming.LastSeenUtc > existing.LastSeenUtc ? incoming.LastSeenUtc : existing.LastSeenUtc;
    }
}
