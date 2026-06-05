using Spectre.Console;
using Core.Bluetooth;

namespace Screens;

public static class BluetoothMenu
{
    public static void Show()
    {
        BluetoothService.Initialize();

        while (true)
        {
            AnsiConsole.Clear();

            var devices =
                BluetoothService.GetDevices();

            var prompt =
                new SelectionPrompt<string>()
                    .Title("[blue]Bluetooth Devices[/]");

            prompt.AddChoice("Scan Devices");

            foreach (var device in devices)
            {
                var status = "";

                if (device.Connected)
                    status += "[green][CONNECTED][/ ] ";

                if (device.Paired)
                    status += "[yellow][PAIRED][/ ] ";

                prompt.AddChoice(
                    $"{status}{device.Name} ({device.Mac})"
                );
            }

            prompt.AddChoice("Back");

            var selected =
                AnsiConsole.Prompt(prompt);

            if (selected == "Back")
                return;

            if (selected == "Scan Devices")
            {
                Scan();
                continue;
            }

            var selectedDevice =
                devices.First(
                    x => selected.Contains(x.Mac)
                );

            DeviceMenu(selectedDevice);
        }
    }

    static void Scan()
    {
        AnsiConsole.Clear();

        AnsiConsole.Status()
            .Start(
                "Scanning bluetooth devices...",
                ctx =>
                {
                    BluetoothService.Scan();
                }
            );
    }

    static void DeviceMenu(
        BluetoothDevice device
    )
    {
        while (true)
        {
            AnsiConsole.Clear();

            var option =
                AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title(
                            $"[green]{device.Name}[/]"
                        )
                        .AddChoices(
                            "Connect",
                            "Disconnect",
                            "Pair",
                            "Info",
                            "Remove",
                            "Back"
                        )
                );

            switch (option)
            {
                case "Connect":
                    BluetoothService.Connect(device);
                    break;

                case "Disconnect":
                    BluetoothService.Disconnect(device);
                    break;

                case "Pair":
                    BluetoothService.Pair(device);
                    break;

                case "Info":
                    ShowInfo(device);
                    break;

                case "Remove":
                    BluetoothService.Remove(device);
                    return;

                case "Back":
                    return;
            }
        }
    }

    static void ShowInfo(
        BluetoothDevice device
    )
    {
        AnsiConsole.Clear();

        var info =
            BluetoothService.Info(device);

        AnsiConsole.Write(
            new Panel(info)
                .Header("Device Info")
        );

        Console.ReadKey();
    }
}