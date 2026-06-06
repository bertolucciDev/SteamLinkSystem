using Core.Controllers;
using Spectre.Console;

namespace Components;

public sealed class MenuRenderer
{
    private readonly ControllerService _controller;

    public MenuRenderer(ControllerService controller)
    {
        _controller = controller;
    }

    public async Task<MenuItem> ShowAsync(
        string title,
        IReadOnlyList<MenuItem> items,
        Action? renderContent = null,
        FocusState? focusState = null,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
            throw new ArgumentException("Menu choices cannot be empty.", nameof(items));

        var focus = focusState ?? new FocusState();
        focus.Clamp(items.Count);

        while (true)
        {
            renderContent?.Invoke();
            Render(title, items, focus.SelectedIndex);

            var action = await ReadNavigationAsync(cancellationToken).ConfigureAwait(false);
            switch (action)
            {
                case NavigationAction.Up:
                    focus.MoveUp(items.Count);
                    break;
                case NavigationAction.Down:
                    focus.MoveDown(items.Count);
                    break;
                case NavigationAction.Select:
                    if (items[focus.SelectedIndex].IsEnabled)
                        return items[focus.SelectedIndex];
                    break;
                case NavigationAction.Back:
                    var backIndex = FindBackChoice(items);
                    if (backIndex >= 0)
                        return items[backIndex];
                    return items[focus.SelectedIndex];
                case NavigationAction.Home:
                    var homeIndex = FindHomeChoice(items);
                    if (homeIndex >= 0)
                        return items[homeIndex];
                    focus = new FocusState();
                    break;
            }
        }
    }

    private static void Render(string title, IReadOnlyList<MenuItem> items, int selectedIndex)
    {
        AnsiConsole.MarkupLine($"[bold green]{Markup.Escape(title.ToUpperInvariant())}[/]");
        AnsiConsole.MarkupLine("[dim]Keyboard: ↑/↓ Enter Esc  •  Controller: D-pad/left stick A/Cross B/Circle Start/Home[/]");
        AnsiConsole.WriteLine();

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var label = Markup.Escape(item.Label);
            var description = string.IsNullOrWhiteSpace(item.Description) ? string.Empty : $" [dim]{Markup.Escape(item.Description)}[/]";

            if (!item.IsEnabled)
            {
                AnsiConsole.MarkupLine($"[dim]   {label}{description}[/]");
                continue;
            }

            if (index == selectedIndex)
                AnsiConsole.MarkupLine($"[black on green] >  {label} [/]{description}");
            else
                AnsiConsole.MarkupLine($"   {label}{description}");
        }
    }

    private async Task<NavigationAction> ReadNavigationAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_controller.TryReadNavigation(out var controllerAction))
                return controllerAction;

            var keyAction = ReadKeyboardNavigation();
            if (keyAction.HasValue)
                return keyAction.Value;

            await Task.Delay(30, cancellationToken).ConfigureAwait(false);
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private static NavigationAction? ReadKeyboardNavigation()
    {
        try
        {
            if (!Console.KeyAvailable)
                return null;

            var key = Console.ReadKey(intercept: true);
            return key.Key switch
            {
                ConsoleKey.UpArrow or ConsoleKey.W or ConsoleKey.K => NavigationAction.Up,
                ConsoleKey.DownArrow or ConsoleKey.S or ConsoleKey.J => NavigationAction.Down,
                ConsoleKey.LeftArrow or ConsoleKey.A or ConsoleKey.H => NavigationAction.Left,
                ConsoleKey.RightArrow or ConsoleKey.D or ConsoleKey.L => NavigationAction.Right,
                ConsoleKey.Enter or ConsoleKey.Spacebar => NavigationAction.Select,
                ConsoleKey.Escape or ConsoleKey.Backspace => NavigationAction.Back,
                ConsoleKey.Home => NavigationAction.Home,
                _ => null
            };
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static int FindBackChoice(IReadOnlyList<MenuItem> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Label.Contains("Back", StringComparison.OrdinalIgnoreCase) || items[index].Label.Contains("Exit", StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private static int FindHomeChoice(IReadOnlyList<MenuItem> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Label.Contains("Home", StringComparison.OrdinalIgnoreCase) || items[index].Label.Contains("Main", StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }
}
