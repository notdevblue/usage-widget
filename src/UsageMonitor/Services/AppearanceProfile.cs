using System;

namespace UsageMonitor.Services;

public sealed record AppearanceProfile
{
    public WidgetTheme Theme { get; init; } = WidgetTheme.System;
    public double Scale { get; init; } = 1;
    public double Opacity { get; init; } = 1;

    public static AppearanceProfile Normalize(AppearanceProfile profile)
    {
        profile ??= new();
        var theme = profile.Theme;
        if (!Enum.IsDefined(theme))
            theme = WidgetTheme.System;
        return profile with
        {
            Theme = theme,
            Scale = NormalizeDimension(profile.Scale, 0.8, 1.5),
            Opacity = NormalizeDimension(profile.Opacity, 0.6, 1)
        };
    }

    private static double NormalizeDimension(double value, double minimum, double maximum)
    {
        if (double.IsFinite(value))
            return Math.Clamp(value, minimum, maximum);
        return 1;
    }
}
