using System.Text.RegularExpressions;
using Core.Logging;

namespace Core.Bluetooth;

public static partial class BluetoothParser
{
    public static List<BluetoothDevice> ParseDevices(string output)
    {
        var devices = new Dictionary<string, BluetoothDevice>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in SplitLines(output))
        {
            var line = CleanLine(rawLine);
            var match = DeviceLineRegex().Match(line);
            if (!match.Success)
                continue;

            var mac = match.Groups[1].Value.ToUpperInvariant();
            var name = match.Groups[2].Value.Trim();
            if (!IsValidMac(mac))
            {
                Logger.Warning($"Ignoring malformed bluetoothctl device line: {line}", "BluetoothParser");
                continue;
            }

            if (!devices.TryGetValue(mac, out var device))
            {
                device = new BluetoothDevice { Mac = mac };
                devices.Add(mac, device);
            }

            if (!string.IsNullOrWhiteSpace(name))
                device.Name = name;

            device.LastSeenUtc = DateTime.UtcNow;
        }

        return devices.Values.OrderByDescending(d => d.Connected).ThenBy(d => d.DisplayName).ToList();
    }

    public static BluetoothDevice EnrichFromInfo(BluetoothDevice device, string infoOutput)
    {
        var copy = device.Clone();
        foreach (var rawLine in SplitLines(infoOutput))
        {
            var line = CleanLine(rawLine).Trim();
            if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
                copy.Name = line[5..].Trim();
            else if (line.StartsWith("Paired:", StringComparison.OrdinalIgnoreCase))
                copy.Paired = ParseBoolean(line[7..]);
            else if (line.StartsWith("Trusted:", StringComparison.OrdinalIgnoreCase))
                copy.Trusted = ParseBoolean(line[8..]);
            else if (line.StartsWith("Connected:", StringComparison.OrdinalIgnoreCase))
                copy.Connected = ParseBoolean(line[10..]);
            else if (line.StartsWith("RSSI:", StringComparison.OrdinalIgnoreCase) && int.TryParse(line[5..].Trim(), out var rssi))
                copy.Rssi = rssi;
            else if (line.StartsWith("Battery Percentage:", StringComparison.OrdinalIgnoreCase))
                copy.BatteryLevel = ParseBattery(line);
        }

        return copy;
    }

    public static string CleanResponse(string output)
    {
        var lines = SplitLines(output)
            .Select(CleanLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.TrimEnd().EndsWith("[bluetooth]#", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Contains("Changing power on succeeded", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return string.Join(Environment.NewLine, lines).Trim();
    }

    public static List<BluetoothDevice> MergeDevices(params IEnumerable<BluetoothDevice>[] groups)
    {
        var merged = new Dictionary<string, BluetoothDevice>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in groups.SelectMany(group => group))
        {
            if (string.IsNullOrWhiteSpace(device.Mac) || !IsValidMac(device.Mac))
                continue;

            if (!merged.TryGetValue(device.Mac, out var existing))
            {
                merged[device.Mac] = device.Clone();
                continue;
            }

            if (!string.IsNullOrWhiteSpace(device.Name) && device.Name != "Unknown Device")
                existing.Name = device.Name;
            existing.Paired |= device.Paired;
            existing.Trusted |= device.Trusted;
            existing.Connected |= device.Connected;
            existing.Rssi ??= device.Rssi;
            existing.BatteryLevel ??= device.BatteryLevel;
            existing.LastSeenUtc = device.LastSeenUtc > existing.LastSeenUtc ? device.LastSeenUtc : existing.LastSeenUtc;
        }

        return merged.Values.OrderByDescending(d => d.Connected).ThenByDescending(d => d.Paired).ThenBy(d => d.DisplayName).ToList();
    }

    public static void MarkState(IEnumerable<BluetoothDevice> devices, bool? paired = null, bool? trusted = null, bool? connected = null)
    {
        foreach (var device in devices)
        {
            if (paired.HasValue)
                device.Paired = paired.Value;
            if (trusted.HasValue)
                device.Trusted = trusted.Value;
            if (connected.HasValue)
                device.Connected = connected.Value;
        }
    }

    private static IEnumerable<string> SplitLines(string output) => (output ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

    private static string CleanLine(string line) => AnsiRegex().Replace(line, string.Empty)
        .Replace("[bluetooth]#", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Trim();

    private static bool ParseBoolean(string value) => value.Trim().StartsWith("yes", StringComparison.OrdinalIgnoreCase);

    private static int? ParseBattery(string line)
    {
        var match = Regex.Match(line, @"\((\d+)\)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    private static bool IsValidMac(string mac) => MacRegex().IsMatch(mac);

    [GeneratedRegex(@"^Device\s+([0-9A-Fa-f:]{17})\s*(.*)$", RegexOptions.Compiled)]
    private static partial Regex DeviceLineRegex();

    [GeneratedRegex(@"^[0-9A-Fa-f]{2}(:[0-9A-Fa-f]{2}){5}$", RegexOptions.Compiled)]
    private static partial Regex MacRegex();

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled)]
    private static partial Regex AnsiRegex();
}
