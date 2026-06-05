using Spectre.Console;

namespace Components;

public static class MenuStyles
{
    public static SelectionPrompt<string> Prompt(string title) => new SelectionPrompt<string>()
        .Title($"[bold green]{title}[/]")
        .HighlightStyle(new Style(foreground: Color.Black, background: Color.Green))
        .PageSize(12)
        .WrapAround();

    public static void Header(string title, string subtitle)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText("Steam Link").Color(Color.Green));
        AnsiConsole.MarkupLine($"[bold]{title}[/] [dim]// {subtitle}[/]");
        AnsiConsole.WriteLine();
    }
}
