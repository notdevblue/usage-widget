using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace UsageMonitor.Services;

public sealed class ThemeCatalog
{
    public IReadOnlyDictionary<string, string> Create(WidgetTheme theme, ThemeColors customColors, bool systemDark)
    {
        var colors = theme switch
        {
            WidgetTheme.System => systemDark ? DarkColors() : new(),
            WidgetTheme.Dark => DarkColors(),
            WidgetTheme.Midnight => new ThemeColors { Background = "#151B2D", Foreground = "#EDF2FF", Accent = "#8AA7F8" },
            WidgetTheme.Mint => new ThemeColors { Background = "#ECF7EF", Foreground = "#223B32", Accent = "#2D8B66" },
            WidgetTheme.Lavender => new ThemeColors { Background = "#F2EFFA", Foreground = "#352C4B", Accent = "#8A65B9" },
            WidgetTheme.Sand => new ThemeColors { Background = "#F6F0E5", Foreground = "#44382A", Accent = "#A06E35" },
            WidgetTheme.Custom => NormalizeColors(customColors ?? new()),
            _ => new ThemeColors()
        };
        var background = Rgb.Parse(colors.Background);
        var foreground = Rgb.Parse(colors.Foreground);
        var accent = Rgb.Parse(colors.Accent);
        var dark = background.Contrast(Rgb.Black) < background.Contrast(Rgb.White);

        return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CardBrush"] = background.Hex,
            ["TextBrush"] = foreground.Hex,
            ["SecondaryBrush"] = SecondaryText(background, foreground).Hex,
            ["AccentBrush"] = accent.Hex,
            ["AccentSoftBrush"] = background.Blend(accent, 0.14).Hex,
            ["TrackBrush"] = background.Blend(foreground, 0.12).Hex,
            ["LineBrush"] = background.Blend(foreground, 0.14).Hex,
            ["HoverBrush"] = background.Blend(accent, 0.10).Hex,
            ["ErrorBrush"] = dark ? "#F49A90" : "#AD443C",
            ["WarningBrush"] = dark ? "#E7C780" : "#926A21",
            ["ActionTextBrush"] = BestText(accent).Hex
        });
    }

    internal static ThemeColors NormalizeColors(ThemeColors colors)
    {
        var defaults = new ThemeColors();
        return colors with
        {
            Background = NormalizeHex(colors.Background, defaults.Background),
            Foreground = NormalizeHex(colors.Foreground, defaults.Foreground),
            Accent = NormalizeHex(colors.Accent, defaults.Accent)
        };
    }

    private static ThemeColors DarkColors() => new() { Background = "#202824", Foreground = "#EDF3EE", Accent = "#83CEAA" };

    private static string NormalizeHex(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        var trimmed = value.Trim();
        return trimmed.Length == 7 && trimmed[0] == '#' && int.TryParse(trimmed.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)
            ? trimmed.ToUpperInvariant()
            : fallback;
    }

    private static Rgb SecondaryText(Rgb background, Rgb foreground)
    {
        var target = background.Contrast(foreground) >= 4.5 ? foreground : BestText(background);
        for (var step = 62; step <= 100; step += 2)
        {
            var candidate = background.Blend(target, step / 100.0);
            if (background.Contrast(candidate) >= 4.5)
                return candidate;
        }

        return target;
    }

    private static Rgb BestText(Rgb background) => background.Contrast(Rgb.Black) >= background.Contrast(Rgb.White) ? Rgb.Black : Rgb.White;

    private readonly record struct Rgb(int Red, int Green, int Blue)
    {
        public static Rgb Black => new(0, 0, 0);
        public static Rgb White => new(255, 255, 255);
        public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}";
        private double Luminance => 0.2126 * Linear(Red) + 0.7152 * Linear(Green) + 0.0722 * Linear(Blue);

        public static Rgb Parse(string hex)
        {
            var value = int.Parse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            return new((value >> 16) & 255, (value >> 8) & 255, value & 255);
        }

        public Rgb Blend(Rgb target, double amount) => new(
            (int)Math.Round(Red + (target.Red - Red) * amount),
            (int)Math.Round(Green + (target.Green - Green) * amount),
            (int)Math.Round(Blue + (target.Blue - Blue) * amount));

        public double Contrast(Rgb other)
        {
            var first = Luminance;
            var second = other.Luminance;
            return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
        }

        private static double Linear(int channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }
}
