using Components;
using Core.Bluetooth;
using Core.Controllers;
using Core.Network;
using Core.Power;
using Core.Steam;
using Spectre.Console;
using Utils;

namespace Screens;

public sealed class MainMenu
{
    private readonly BluetoothMenu _bluetoothMenu;
    private readonly SteamService _steamService = new();
    private readonly NetworkService _networkService = new();
    private readonly PowerService _powerService = new();
    private readonly ControllerService _controllerService;

    public MainMenu(BluetoothService bluetoothService, ControllerService controllerService)
    {
        _controllerService = controllerService;
        _controllerService.Start();
        _bluetoothMenu = new BluetoothMenu(bluetoothService, controllerService);
    }

    public async Task ShowAsync()
    {
        while (true)
        {
            var selected = await MenuStyles.SelectAsync(
                "Main Menu",
                new[] { "Bluetooth", "Steam Link", "Controllers", "Network", "System", "Settings", "Exit" },
                _controllerService,
                () => MenuStyles.Header("Main Menu", "embedded Linux gaming shell"));

            switch (selected)
            {
                case "Bluetooth":
                    await _bluetoothMenu.ShowAsync().ConfigureAwait(false);
                    break;
                case "Steam Link":
                    await ShowSteamAsync().ConfigureAwait(false);
                    break;
                case "Controllers":
                    await ShowControllersAsync().ConfigureAwait(false);
                    break;
                case "Network":
                    await ShowNetworkAsync().ConfigureAwait(false);
                    break;
                case "System":
                    await ShowSystemAsync().ConfigureAwait(false);
                    break;
                case "Settings":
                    await ShowSettingsAsync().ConfigureAwait(false);
                    break;
                case "Exit":
                    return;
            }
        }
    }

    private async Task ShowSteamAsync()
    {
        while (true)
        {
            var selected = await MenuStyles.SelectAsync(
                "Steam Link",
                new[] { "Launch Steam Link", "Back" },
                _controllerService,
                () => MenuStyles.Header("Steam Link", "Flatpak launcher"));
            if (selected == "Back")
                return;

            var result = await _steamService.LaunchSteamLinkAsync().ConfigureAwait(false);
            await ShowCommandResultAsync(result.CombinedOutput, result.Success ? "Steam Link" : "Steam Link Error").ConfigureAwait(false);
        }
    }

    private async Task ShowNetworkAsync()
    {
        while (true)
        {
            var selected = await MenuStyles.SelectAsync(
                "Network",
                new[] { "IP Status", "Connectivity Test", "Back" },
                _controllerService,
                () => MenuStyles.Header("Network", "diagnostics foundation"));
            if (selected == "Back")
                return;

            var result = selected == "IP Status"
                ? await _networkService.GetIpStatusAsync().ConfigureAwait(false)
                : await _networkService.TestConnectivityAsync().ConfigureAwait(false);
            await ShowCommandResultAsync(result.CombinedOutput, selected).ConfigureAwait(false);
        }
    }

    private async Task ShowSystemAsync()
    {
        while (true)
        {
            var selected = await MenuStyles.SelectAsync(
                "System",
                new[] { "Shutdown", "Reboot", "Back" },
                _controllerService,
                () => MenuStyles.Header("System", "power controls"));
            if (selected == "Back")
                return;

            var confirm = await MenuStyles.SelectAsync(
                "Confirm",
                new[] { "No", "Yes" },
                _controllerService,
                () =>
                {
                    MenuStyles.Header("Confirm", selected);
                    AnsiConsole.MarkupLine($"[red]Execute {Markup.Escape(selected)} now?[/]");
                    AnsiConsole.WriteLine();
                });
            if (confirm != "Yes")
                continue;

            var result = selected == "Shutdown"
                ? await _powerService.PowerOffAsync().ConfigureAwait(false)
                : await _powerService.RebootAsync().ConfigureAwait(false);
            await ShowCommandResultAsync(result.CombinedOutput, selected).ConfigureAwait(false);
        }
    }

    private async Task ShowControllersAsync()
    {
        MenuStyles.Header("Controllers", "universal gamepad navigation");
        AnsiConsole.Write(new Panel(Markup.Escape(_controllerService.Status)).Border(BoxBorder.Rounded));
        await Ui.PauseAsync(_controllerService).ConfigureAwait(false);
    }

    private async Task ShowSettingsAsync()
    {
        MenuStyles.Header("Settings", "future configuration");
        AnsiConsole.Write(new Panel("[dim]Settings foundation ready for terminal, controller, and subsystem preferences.[/]").Border(BoxBorder.Rounded));
        await Ui.PauseAsync(_controllerService).ConfigureAwait(false);
    }

    private async Task ShowCommandResultAsync(string output, string title)
    {
        AnsiConsole.Write(new Panel(Markup.Escape(string.IsNullOrWhiteSpace(output) ? "Command completed without output." : output)).Header(title).Border(BoxBorder.Rounded));
        await Ui.PauseAsync(_controllerService).ConfigureAwait(false);
    }
}
