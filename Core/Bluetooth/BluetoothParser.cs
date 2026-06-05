using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Bluetooth;

/// <summary>
/// Parses bluetoothctl output into strongly-typed models.
/// 
/// Handles various output formats and safely manages malformed data.
/// 
/// Reference: PRD Section 10 (BluetoothParser)
/// </summary>
public static class BluetoothParser
{
    public static List<BluetoothDevice> ParseDevices(string output)
    {
        var devices = new List<BluetoothDevice>();

        if (string.IsNullOrWhiteSpace(output))
            return devices;

        var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            if (!line.StartsWith("Device", StringComparison.OrdinalIgnoreCase))
                continue;

            var device = ParseDeviceLine(line);
            if (device != null)
                devices.Add(device);
        }

        return devices;
    }

    private static BluetoothDevice? ParseDeviceLine(string line)
    {
        try
        {
            // Format: "Device 00:1B:66:12:34:56 Xbox Controller"
            var parts = line.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3)
            {
                Logger.Warning($"Failed to parse device line: {line}", "BluetoothParser");
                return null;
            }

            var mac = parts[1].Trim();
            var name = parts[2].Trim();

            if (!IsValidMacAddress(mac))
            {
                Logger.Warning($"Invalid MAC address: {mac}", "BluetoothParser");
                return null;
            }

            return new BluetoothDevice
            {
                Mac = mac,
                Name = name,
                DiscoveredAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception parsing device line: {ex.Message}", "BluetoothParser");
            return null;
        }
    }

    public static List<BluetoothDevice> EnrichDeviceStates(
        List<BluetoothDevice> allDevices,
        List<BluetoothDevice> pairedDevices,
        List<BluetoothDevice> connectedDevices,
        List<BluetoothDevice> trustedDevices
    )
    {
        var pairedMacs = pairedDevices.Select(d => d.Mac).ToHashSet();
        var connectedMacs = connectedDevices.Select(d => d.Mac).ToHashSet();
        var trustedMacs = trustedDevices.Select(d => d.Mac).ToHashSet();

        foreach (var device in allDevices)
        {
            device.Paired = pairedMacs.Contains(device.Mac);
            device.Connected = connectedMacs.Contains(device.Mac);
            device.Trusted = trustedMacs.Contains(device.Mac);
        }

        return allDevices;
    }

    public static string ParseCommandResponse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "";

        // Remove common bluetoothctl prompts and clean up output
        var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
            .Where(line => !line.EndsWith("[bluetooth]#", StringComparison.OrdinalIgnoreCase))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        return string.Join("\n", lines).Trim();
    }

    private static bool IsValidMacAddress(string mac)
    {
        if (string.IsNullOrEmpty(mac))
            return false;

        var parts = mac.Split(':');
        if (parts.Length != 6)
            return false;

        return parts.All(part =>
            part.Length == 2 &&
            byte.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out _)
        );
    }
}