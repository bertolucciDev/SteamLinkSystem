using Core.Controllers;
using Spectre.Console;

namespace Utils;

public static class Ui
{
    public static async Task PauseAsync(ControllerService controller, string message = "Press any key or controller confirm to return...")
    {
        AnsiConsole.MarkupLine($"\n[dim]{message}[/]");
        while (true)
        {
            if (controller.TryReadNavigation(out var action) && action is NavigationAction.Select or NavigationAction.Back)
                return;

            try
            {
                if (Console.KeyAvailable)
                {
                    Console.ReadKey(intercept: true);
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                return;
            }

            await Task.Delay(35).ConfigureAwait(false);
        }
    }

    public static async Task ShowErrorAsync(ControllerService controller, string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        await PauseAsync(controller).ConfigureAwait(false);
    }
}
