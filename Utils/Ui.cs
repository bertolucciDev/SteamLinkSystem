using Spectre.Console;

namespace Utils;

public static class Ui
{
    public static void Pause(string message = "Press any key to return...")
    {
        AnsiConsole.MarkupLine($"\n[dim]{message}[/]");
        Console.ReadKey(intercept: true);
    }

    public static void ShowError(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        Pause();
    }
}
