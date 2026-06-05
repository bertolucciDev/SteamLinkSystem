namespace Core.Bluetooth;

/// <summary>
/// Represents a Bluetooth device discovered or paired by the system.
/// 
/// Reference: PRD Section 10 (BluetoothDevice Model)
/// </summary>
public class BluetoothDevice
{
    public string Mac { get; set; } = "";
    public string Name { get; set; } = "";

    public bool Connected { get; set; }
    public bool Paired { get; set; }
    public bool Trusted { get; set; }

    // Future: RSSI and battery level
    public int? Rssi { get; set; }
    public int? BatteryLevel { get; set; }

    // Discovery timestamp for caching purposes
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    public override string ToString()
    {
        return $"{Name} ({Mac})";
    }

    public string GetStatusString()
    {
        var status = "";
        if (Connected)
            status += "[green][CONNECTED][/] ";
        if (Paired)
            status += "[yellow][PAIRED][/] ";
        if (Trusted)
            status += "[blue][TRUSTED][/] ";

        return status.Length > 0 ? status : "[dim][NEW][/] ";
    }
}