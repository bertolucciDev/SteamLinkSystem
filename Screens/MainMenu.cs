using Spectre.Console;

namespace Screens;

public static class MainMenu
{
    public static void Show()
    {
        AnsiConsole.Clear();

        var option =
            AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]Steam Link System[/]")
                    .AddChoices(
                        "Bluetooth",
                        "Exit"
                    )
            );

        switch (option)
        {
            case "Bluetooth":
                BluetoothMenu.Show();
                break;

            case "Exit":
                Environment.Exit(0);
                break;
        }
    }
}