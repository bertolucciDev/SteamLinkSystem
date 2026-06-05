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
        if (choices.Count == 0)
            throw new ArgumentException("Menu choices cannot be empty.", nameof(choices));

        var selectedIndex = 0;
        while (true)
        {
            renderContent?.Invoke();
            RenderChoices(title, choices, selectedIndex);

            var action = await ReadNavigationAsync(controller, cancellationToken).ConfigureAwait(false);
            switch (action)
            {
                case ControllerNavigationAction.Up:
                    selectedIndex = selectedIndex == 0 ? choices.Count - 1 : selectedIndex - 1;
                    break;
                case ControllerNavigationAction.Down:
                    selectedIndex = (selectedIndex + 1) % choices.Count;
                    break;
                case ControllerNavigationAction.Select:
                    return choices[selectedIndex];
                case ControllerNavigationAction.Back:
                    var backIndex = FindBackChoice(choices);
                    return backIndex >= 0 ? choices[backIndex] : choices[selectedIndex];
            }
        }
    }

    private static void RenderChoices(string title, IReadOnlyList<string> choices, int selectedIndex)
    {
        AnsiConsole.MarkupLine($"[bold green]{Markup.Escape(title.ToUpperInvariant())}[/]");
        AnsiConsole.MarkupLine("[dim]Keyboard: ↑/↓ Enter Esc  •  Controller: D-pad/left stick A/Cross B/Circle[/]");
        AnsiConsole.WriteLine();

        for (var index = 0; index < choices.Count; index++)
        {
            var cursor = index == selectedIndex ? "[black on green] > [/]" : "[dim]   [/]";
            var choice = choices[index];
            var line = index == selectedIndex ? $"[black on green] {choice} [/]" : $" {choice}";
            AnsiConsole.MarkupLine($"{cursor}{line}");
        }
    }

    private static async Task<ControllerNavigationAction> ReadNavigationAsync(ControllerService controller, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (controller.TryReadNavigation(out var controllerAction))
                return controllerAction;

            var keyAction = ReadKeyboardNavigation();
            if (keyAction.HasValue)
                return keyAction.Value;

            await Task.Delay(35, cancellationToken).ConfigureAwait(false);
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private static ControllerNavigationAction? ReadKeyboardNavigation()
    {
        try
        {
            if (!Console.KeyAvailable)
                return null;

            var key = Console.ReadKey(intercept: true);
            return key.Key switch
            {
                ConsoleKey.UpArrow or ConsoleKey.W or ConsoleKey.K => ControllerNavigationAction.Up,
                ConsoleKey.DownArrow or ConsoleKey.S or ConsoleKey.J => ControllerNavigationAction.Down,
                ConsoleKey.Enter or ConsoleKey.Spacebar => ControllerNavigationAction.Select,
                ConsoleKey.Escape or ConsoleKey.Backspace => ControllerNavigationAction.Back,
                _ => null
            };
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static int FindBackChoice(IReadOnlyList<string> choices)
    {
        for (var index = 0; index < choices.Count; index++)
        {
            if (choices[index].Contains("Back", StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }
}
