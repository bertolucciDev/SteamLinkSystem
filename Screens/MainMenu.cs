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
    private readonly ControllerService _controllerService = new();

    public MainMenu(BluetoothService bluetoothService)
    {
        _bluetoothMenu = new BluetoothMenu(bluetoothService);
    }

    public async Task ShowAsync()
    {
        while (true)
        {
            MenuStyles.Header("Main Menu", "embedded Linux gaming shell");
            var selected = AnsiConsole.Prompt(MenuStyles.Prompt("Main Menu").AddChoices("Bluetooth", "Steam Link", "Controllers", "Network", "System", "Settings", "Exit"));

            switch (selected)
            {
                case "Bluetooth":
                    await _bluetoothMenu.ShowAsync().ConfigureAwait(false);
                    break;
                case "Steam Link":
                    await ShowSteamAsync().ConfigureAwait(false);
                    break;
                case "Controllers":
                    ShowControllers();
                    break;
                case "Network":
                    await ShowNetworkAsync().ConfigureAwait(false);
                    break;
                case "System":
                    await ShowSystemAsync().ConfigureAwait(false);
                    break;
                case "Settings":
                    ShowSettings();
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
            MenuStyles.Header("Steam Link", "Flatpak launcher");
            var selected = AnsiConsole.Prompt(MenuStyles.Prompt("Steam Link").AddChoices("Launch Steam Link", "Back"));
            if (selected == "Back")
                return;

            var result = await _steamService.LaunchSteamLinkAsync().ConfigureAwait(false);
            ShowCommandResult(result.CombinedOutput, result.Success ? "Steam Link" : "Steam Link Error");
        }
    }

    private async Task ShowNetworkAsync()
    {
        while (true)
        {
            MenuStyles.Header("Network", "diagnostics foundation");
            var selected = AnsiConsole.Prompt(MenuStyles.Prompt("Network").AddChoices("IP Status", "Connectivity Test", "Back"));
            if (selected == "Back")
                return;

            var result = selected == "IP Status"
                ? await _networkService.GetIpStatusAsync().ConfigureAwait(false)
                : await _networkService.TestConnectivityAsync().ConfigureAwait(false);
            ShowCommandResult(result.CombinedOutput, selected);
        }
    }

    private async Task ShowSystemAsync()
    {
        while (true)
        {
            MenuStyles.Header("System", "power controls");
            var selected = AnsiConsole.Prompt(MenuStyles.Prompt("System").AddChoices("Shutdown", "Reboot", "Back"));
            if (selected == "Back")
                return;

            var confirm = AnsiConsole.Confirm($"Execute [red]{selected}[/] now?", defaultValue: false);
            if (!confirm)
                continue;

            var result = selected == "Shutdown"
                ? await _powerService.PowerOffAsync().ConfigureAwait(false)
                : await _powerService.RebootAsync().ConfigureAwait(false);
            ShowCommandResult(result.CombinedOutput, selected);
        }
    }

    private void ShowControllers()
    {
        MenuStyles.Header("Controllers", "navigation preparation");
        AnsiConsole.Write(new Panel(Markup.Escape(_controllerService.Status)).Border(BoxBorder.Rounded));
        Ui.Pause();
    }

    private static void ShowSettings()
    {
        MenuStyles.Header("Settings", "future configuration");
        AnsiConsole.Write(new Panel("[dim]Settings foundation ready for terminal, controller, and subsystem preferences.[/]").Border(BoxBorder.Rounded));
        Ui.Pause();
    }

    private static void ShowCommandResult(string output, string title)
    {
        AnsiConsole.Write(new Panel(Markup.Escape(string.IsNullOrWhiteSpace(output) ? "Command completed without output." : output)).Header(title).Border(BoxBorder.Rounded));
        Ui.Pause();
    }
}
