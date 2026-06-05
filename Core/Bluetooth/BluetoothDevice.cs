namespace Core.Bluetooth;

public sealed class BluetoothDevice
{
    public string Mac { get; set; } = string.Empty;
    public string Name { get; set; } = "Unknown Device";
    public bool Paired { get; set; }
    public bool Trusted { get; set; }
    public bool Connected { get; set; }
    public int? Rssi { get; set; }
    public int? BatteryLevel { get; set; }
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Mac : Name;

    public BluetoothDevice Clone() => new()
    {
        Mac = Mac,
        Name = Name,
        Paired = Paired,
        Trusted = Trusted,
        Connected = Connected,
        Rssi = Rssi,
        BatteryLevel = BatteryLevel,
        LastSeenUtc = LastSeenUtc
    };
}
