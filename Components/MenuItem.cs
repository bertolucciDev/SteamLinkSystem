namespace Components;

public sealed record MenuItem(string Label, string? Description = null, bool IsEnabled = true);
