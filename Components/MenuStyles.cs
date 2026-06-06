using Core.Controllers;
using Spectre.Console;

namespace Components;

public static class MenuStyles
{
    public static void Header(string title, string subtitle)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(Align.Center(new FigletText("Steam Link").Color(Color.Green)));
        AnsiConsole.Write(Align.Center(new FigletText(title.ToUpperInvariant()).Color(Color.White)));
        AnsiConsole.Write(Align.Center(new Markup($"[bold green]{Markup.Escape(subtitle.ToUpperInvariant())}[/]")));
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    public static async Task<string> SelectAsync(
        string title,
        IReadOnlyList<string> choices,
        ControllerService controller,
        Action? renderContent = null,
        CancellationToken cancellationToken = default)
    {
        var renderer = new MenuRenderer(controller);
        var selected = await renderer.ShowAsync(
            title,
            choices.Select(choice => new MenuItem(choice)).ToArray(),
            renderContent,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return selected.Label;
    }
}
