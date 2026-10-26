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
    Dark = 2,
    Midnight = 3,
    Mint = 4,
    Lavender = 5,
    Sand = 6,
    Custom = 7
}

public sealed record ThemeColors
{
    public string Background { get; init; } = "#F8FAF8";
    public string Foreground { get; init; } = "#202D29";
    public string Accent { get; init; } = "#3A9771";
}

public sealed record AppSettings
{
    public bool AlwaysOnTop { get; init; }
    public FiveHourDisplay FiveHourDisplay { get; init; } = FiveHourDisplay.Automatic;
    public bool ShowResetCountdown { get; init; }
    public WidgetTheme Theme { get; init; } = WidgetTheme.System;
    public ThemeColors CustomColors { get; init; } = new();
    public double Scale { get; init; } = 1;
    public double Opacity { get; init; } = 1;
    public LayoutProfile Layout { get; init; } = new();
    public bool HasPosition { get; init; }
    public double Left { get; init; }
    public double Top { get; init; }
    public int RefreshSeconds { get; init; } = 60;
    public string CodexPath { get; init; } = "";
    public string ClaudePath { get; init; } = "";
    public string GrokPath { get; init; } = "";
}
