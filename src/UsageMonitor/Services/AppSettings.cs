namespace UsageMonitor.Services;

public enum FiveHourDisplay
{
    Automatic,
    Hidden
}

public enum WidgetTheme
{
    System = 0,
    Light = 1,
    Dark = 2
}

public sealed record AppSettings
{
    public bool AlwaysOnTop { get; init; }
    public FiveHourDisplay FiveHourDisplay { get; init; } = FiveHourDisplay.Automatic;
    public bool ShowResetCountdown { get; init; }
    public bool ShowCodex { get; init; } = true;
    public bool ShowClaude { get; init; } = true;
    public bool ShowGrok { get; init; } = true;
    public WidgetTheme Theme { get; init; } = WidgetTheme.System;
    public double Scale { get; init; } = 1;
    public double Opacity { get; init; } = 1;
    public bool HasPosition { get; init; }
    public double Left { get; init; }
    public double Top { get; init; }
    public int RefreshSeconds { get; init; } = 60;
    public string CodexPath { get; init; } = "";
    public string ClaudePath { get; init; } = "";
    public string GrokPath { get; init; } = "";

    public bool IsProviderVisible(string providerId)
    {
        if (string.Equals(providerId, "codex", System.StringComparison.OrdinalIgnoreCase))
            return ShowCodex;
        if (string.Equals(providerId, "claude", System.StringComparison.OrdinalIgnoreCase))
            return ShowClaude;
        if (string.Equals(providerId, "grok", System.StringComparison.OrdinalIgnoreCase))
            return ShowGrok;
        return true;
    }
}
