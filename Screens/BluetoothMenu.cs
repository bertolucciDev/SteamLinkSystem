using Components;
using Core.Bluetooth;
using Core.Logging;
using Spectre.Console;
using Utils;

namespace Screens;

public sealed class BluetoothMenu
{
    private readonly BluetoothService _bluetooth;

    public BluetoothMenu(BluetoothService bluetooth)
    {
        _bluetooth = bluetooth;
    }

    public async Task ShowAsync()
    {
        await TryInitializeAsync().ConfigureAwait(false);

        while (true)
        {
            MenuStyles.Header("Bluetooth", "persistent BlueZ terminal session");
            var devices = _bluetooth.CachedDevices;
            if (devices.Count == 0)
                AnsiConsole.MarkupLine("[dim]No cached devices yet. Run Scan Devices to discover controllers.[/]\n");

            var choices = new List<string> { "Scan Devices", "Refresh Devices" };
            choices.AddRange(devices.Select(FormatDeviceChoice));
            choices.Add("Back");

            var selected = AnsiConsole.Prompt(MenuStyles.Prompt("Bluetooth").AddChoices(choices));
            if (selected == "Back")
                return;

            if (selected == "Scan Devices")
            {
                await ScanAsync().ConfigureAwait(false);
                continue;
            }

            if (selected == "Refresh Devices")
            {
                await RefreshAsync().ConfigureAwait(false);
                continue;
            }

            var device = devices.FirstOrDefault(d => selected.Contains(d.Mac, StringComparison.OrdinalIgnoreCase));
            if (device != null)
                await ShowDeviceMenuAsync(device).ConfigureAwait(false);
        }
    }

    private async Task TryInitializeAsync()
    {
        try
        {
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync("Initializing Bluetooth adapter...", _ => _bluetooth.InitializeAsync()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error(ex.Message, "BluetoothUI");
            Ui.ShowError("Bluetooth is unavailable. Ensure BlueZ and bluetoothctl are installed and accessible. " + ex.Message);
        }
    }

    private async Task ScanAsync()
    {
        try
        {
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync("Scanning for Bluetooth devices...", _ => _bluetooth.ScanDevicesAsync(TimeSpan.FromSeconds(8))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Ui.ShowError(ex.Message);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync("Refreshing device state...", _ => _bluetooth.ListDevicesAsync(forceRefresh: true)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Ui.ShowError(ex.Message);
        }
    }

    private async Task ShowDeviceMenuAsync(BluetoothDevice device)
    {
        while (true)
        {
            MenuStyles.Header("Device Menu", $"{device.DisplayName} // {device.Mac}");
            AnsiConsole.Write(BuildDevicePanel(device));
            var selected = AnsiConsole.Prompt(MenuStyles.Prompt("Action").AddChoices("Connect", "Disconnect", "Pair", "Trust", "Remove", "Info", "Back"));

            try
            {
                switch (selected)
                {
                    case "Connect":
                        await RunDeviceActionAsync("Connecting...", () => _bluetooth.ConnectDeviceAsync(device)).ConfigureAwait(false);
                        break;
                    case "Disconnect":
                        await RunDeviceActionAsync("Disconnecting...", () => _bluetooth.DisconnectDeviceAsync(device)).ConfigureAwait(false);
                        break;
                    case "Pair":
                        await RunDeviceActionAsync("Pairing...", () => _bluetooth.PairDeviceAsync(device)).ConfigureAwait(false);
                        break;
                    case "Trust":
                        await RunDeviceActionAsync("Trusting...", () => _bluetooth.TrustDeviceAsync(device)).ConfigureAwait(false);
                        break;
                    case "Remove":
                        await RunDeviceActionAsync("Removing...", () => _bluetooth.RemoveDeviceAsync(device)).ConfigureAwait(false);
                        return;
                    case "Info":
                        await ShowInfoAsync(device).ConfigureAwait(false);
                        break;
                    case "Back":
                        return;
                }
            }
            catch (Exception ex)
            {
                Ui.ShowError(ex.Message);
            }

            device = _bluetooth.CachedDevices.FirstOrDefault(d => d.Mac.Equals(device.Mac, StringComparison.OrdinalIgnoreCase)) ?? device;
        }
    }

    private static Panel BuildDevicePanel(BluetoothDevice device)
    {
        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("MAC", Markup.Escape(device.Mac));
        grid.AddRow("Paired", device.Paired ? "[green]yes[/]" : "[dim]no[/]");
        grid.AddRow("Trusted", device.Trusted ? "[green]yes[/]" : "[dim]no[/]");
        grid.AddRow("Connected", device.Connected ? "[green]yes[/]" : "[dim]no[/]");
        grid.AddRow("RSSI", device.Rssi?.ToString() ?? "[dim]unknown[/]");
        grid.AddRow("Battery", device.BatteryLevel is null ? "[dim]unknown[/]" : $"{device.BatteryLevel}%");
        return new Panel(grid).Header(device.DisplayName).Border(BoxBorder.Rounded);
    }

    private async Task RunDeviceActionAsync(string status, Func<Task<string>> action)
    {
        var response = await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync(status, _ => action()).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(response))
        {
            AnsiConsole.Write(new Panel(Markup.Escape(response)).Header("bluetoothctl"));
            Ui.Pause();
        }
    }

    private async Task ShowInfoAsync(BluetoothDevice device)
    {
        var response = await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync("Reading device info...", _ => _bluetooth.GetDeviceInfoAsync(device)).ConfigureAwait(false);
        MenuStyles.Header("Device Info", device.DisplayName);
        AnsiConsole.Write(new Panel(Markup.Escape(string.IsNullOrWhiteSpace(response) ? "No details returned." : response)).Header("bluetoothctl info").Border(BoxBorder.Rounded));
        Ui.Pause();
    }

    private static string FormatDeviceChoice(BluetoothDevice device)
    {
        var tags = new List<string>();
        if (device.Connected)
            tags.Add("[green]CONNECTED[/]");
        if (device.Paired)
            tags.Add("[yellow]PAIRED[/]");
        if (device.Trusted)
            tags.Add("[blue]TRUSTED[/]");
        var prefix = tags.Count == 0 ? "[dim]NEW[/]" : string.Join(" ", tags);
        return $"{prefix} {Markup.Escape(device.DisplayName)} ({device.Mac})";
    }
}
