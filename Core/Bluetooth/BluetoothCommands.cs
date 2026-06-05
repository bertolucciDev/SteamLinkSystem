namespace Core.Bluetooth;

/// <summary>
/// Bluetooth control commands for persistent terminal execution.
/// 
/// These commands are designed for use with BluetoothTerminal,
/// not for isolated shell execution.
/// 
/// Reference: PRD Section 10 (Bluetooth Functional Requirements)
/// </summary>
public static class BluetoothCommands
{
    // Initialization commands
    public static string Power(bool on)
        => on ? "power on" : "power off";

    public static string Agent(bool enable)
        => enable ? "agent on" : "agent off";

    public static string DefaultAgent()
        => "default-agent";

    public static string Discoverable(bool enable)
        => enable ? "discoverable on" : "discoverable off";

    public static string Pairable(bool enable)
        => enable ? "pairable on" : "pairable off";

    // Device discovery
    public static string Devices()
        => "devices";

    public static string DevicesFilter(string filter)
        => $"devices {filter}";

    public static string ScanOn()
        => "scan on";

    public static string ScanOff()
        => "scan off";

    // Device operations
    public static string Pair(string mac)
        => $"pair {mac}";

    public static string Trust(string mac)
        => $"trust {mac}";

    public static string Connect(string mac)
        => $"connect {mac}";

    public static string Disconnect(string mac)
        => $"disconnect {mac}";

    public static string Remove(string mac)
        => $"remove {mac}";

    public static string Info(string mac)
        => $"info {mac}";

    public static string Show()
        => "show";

    public static string Quit()
        => "quit";
}