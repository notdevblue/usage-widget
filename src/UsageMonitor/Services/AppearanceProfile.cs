using System;

namespace UsageMonitor.Services;

public sealed record AppearanceProfile
{
    public int Version { get; init; } = 1;
    public WidgetTheme Theme { get; init; } = WidgetTheme.System;
    public ThemeColors CustomColors { get; init; } = new();
    public double Scale { get; init; } = 1;
    public double Opacity { get; init; } = 1;
    public LayoutProfile Layout { get; init; } = new();

    public static AppearanceProfile Normalize(AppearanceProfile profile)
    {
        profile ??= new();
        return profile with
        {
            Version = 1,
            Theme = Enum.IsDefined(profile.Theme) ? profile.Theme : WidgetTheme.System,
            CustomColors = ThemeCatalog.NormalizeColors(profile.CustomColors ?? new()),
            Scale = double.IsFinite(profile.Scale) ? Math.Clamp(profile.Scale, 0.8, 1.5) : 1,
            Opacity = double.IsFinite(profile.Opacity) ? Math.Clamp(profile.Opacity, 0.6, 1) : 1,
            Layout = LayoutProfile.Normalize(profile.Layout ?? new())
        };
    }
}
