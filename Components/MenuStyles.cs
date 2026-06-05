using Spectre.Console;

namespace Components;

public static class MenuStyles
{
    public static SelectionPrompt<string> Prompt(string title) => new SelectionPrompt<string>()
        .Title($"[bold green]{Markup.Escape(title.ToUpperInvariant())}[/]")
        .HighlightStyle(new Style(foreground: Color.Black, background: Color.Green))
        .PageSize(12)
        .WrapAround();

    public static void Header(string title, string subtitle)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(Align.Center(new FigletText("Steam Link").Color(Color.Green)));
        AnsiConsole.Write(Align.Center(new FigletText(title.ToUpperInvariant()).Color(Color.White)));
        AnsiConsole.Write(Align.Center(new Markup($"[bold green]{Markup.Escape(subtitle.ToUpperInvariant())}[/]")));
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }
}
