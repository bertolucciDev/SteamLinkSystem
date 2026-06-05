namespace Core.Bluetooth;

public static class BluetoothCommands
{
    public static string Power(bool enabled) => enabled ? "power on" : "power off";
    public static string Agent(bool enabled) => enabled ? "agent on" : "agent off";
    public static string DefaultAgent() => "default-agent";
    public static string Discoverable(bool enabled) => enabled ? "discoverable on" : "discoverable off";
    public static string Pairable(bool enabled) => enabled ? "pairable on" : "pairable off";
    public static string Scan(bool enabled) => enabled ? "scan on" : "scan off";
    public static string Devices() => "devices";
    public static string PairedDevices() => "devices Paired";
    public static string TrustedDevices() => "devices Trusted";
    public static string ConnectedDevices() => "devices Connected";
    public static string Pair(string mac) => $"pair {mac}";
    public static string Trust(string mac) => $"trust {mac}";
    public static string Connect(string mac) => $"connect {mac}";
    public static string Disconnect(string mac) => $"disconnect {mac}";
    public static string Remove(string mac) => $"remove {mac}";
    public static string Info(string mac) => $"info {mac}";
    public static string Show() => "show";
    public static string Quit() => "quit";
}
