using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UsageMonitor.Core;
using UsageMonitor.Presentation;
using UsageMonitor.Services;

namespace UsageMonitor.Presentation.Tests;

internal static class UsageColorTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly SolidColorBrush Normal = new(Color.FromArgb(240, 40, 200, 120));
    private static readonly SolidColorBrush Warning = new(Color.FromArgb(160, 220, 140, 40));
    private static readonly SolidColorBrush Error = new(Color.FromArgb(80, 180, 40, 60));

    internal static void Run()
    {
        Anchors();
        IntermediateColors();
        RemainingFraction();
        AnchorContinuity();
        ThemedRenderers();
        Console.WriteLine("PASS continuous usage colors, theme anchors and fixed widget rendering");
    }

    private static void Anchors()
    {
        ColorEquals(Normal.Color, Metric(100).Accent, "100% remaining must use the normal accent.");
        ColorEquals(Warning.Color, Metric(25).Accent, "25% remaining must use the warning accent.");
        foreach (var remaining in new[] { 10d, 9.99, 5d, 1d, 0d })
            ColorEquals(Error.Color, Metric(remaining).Accent, $"{remaining}% remaining must saturate at the error accent.");
    }

    private static void IntermediateColors()
    {
        var samples = new[]
        {
            (Remaining: 81.25, Expected: Color.FromArgb(220, 85, 185, 100)),
            (Remaining: 62.5, Expected: Color.FromArgb(200, 130, 170, 80)),
            (Remaining: 43.75, Expected: Color.FromArgb(180, 175, 155, 60)),
            (Remaining: 21.25, Expected: Color.FromArgb(140, 210, 115, 45)),
            (Remaining: 17.5, Expected: Color.FromArgb(120, 200, 90, 50)),
            (Remaining: 13.75, Expected: Color.FromArgb(100, 190, 65, 55))
        };
        var colors = new HashSet<Color>();
        foreach (var sample in samples)
        {
            var accent = Metric(sample.Remaining).Accent;
            ColorEquals(sample.Expected, accent, $"{sample.Remaining}% remaining must interpolate every ARGB channel.");
            colors.Add(ColorOf(accent));
        }

        Check(colors.Count == samples.Length, "Intermediate remaining values must have distinct colors in both gradient segments.");
    }

    private static void RemainingFraction()
    {
        var first = Metric(62.5);
        Check(first.Remaining == 62.5 && first.Amount == "62", "The color input must preserve fractional remaining usage before formatting.");
        ColorEquals(Color.FromArgb(200, 130, 170, 80), first.Accent, "The gradient must use remaining usage rather than used usage.");
        var second = Metric(17.5);
        Check(second.Remaining == 17.5 && second.Amount == "17", "The lower gradient segment must preserve fractional remaining usage.");
        ColorEquals(Color.FromArgb(120, 200, 90, 50), second.Accent, "Fractional usage must not be rounded to the displayed percentage before coloring.");
    }

    private static void AnchorContinuity()
    {
        foreach (var pair in new[] { (100d, 99.99), (25.01, 25d), (25d, 24.99), (10.01, 10d), (10d, 9.99) })
        {
            var first = ColorOf(Metric(pair.Item1).Accent);
            var second = ColorOf(Metric(pair.Item2).Accent);
            Check(Math.Abs(first.A - second.A) <= 1 && Math.Abs(first.R - second.R) <= 1 &&
                Math.Abs(first.G - second.G) <= 1 && Math.Abs(first.B - second.B) <= 1,
                $"The gradient must remain continuous between {pair.Item1}% and {pair.Item2}% remaining.");
        }
    }

    private static void ThemedRenderers()
    {
        var resources = Application.Current.Resources;
        var keys = new ThemeCatalog().Create(WidgetTheme.Light, false).Keys.ToArray();
        var localKeys = resources.Keys.Cast<object>().ToHashSet();
        var originals = keys.Where(localKeys.Contains).ToDictionary(key => key, key => resources[key]);
        var gallery = new StackPanel { Width = 190 };
        var themeAccents = new HashSet<Color>();
        try
        {
            foreach (var theme in new[] { WidgetTheme.Light, WidgetTheme.Dark })
            {
                ApplyPalette(theme);
                var normal = ResourceBrush("UsageBrush");
                var warning = ResourceBrush("WarningBrush");
                var error = ResourceBrush("ErrorBrush");
                themeAccents.Add(ColorOf(normal));
                ColorEquals(ColorOf(normal), ThemeMetric(100).Accent, $"{theme} must retain its theme accent at 100%.");
                ColorEquals(ColorOf(warning), ThemeMetric(25).Accent, $"{theme} must retain its warning color at 25%.");
                ColorEquals(ColorOf(error), ThemeMetric(10).Accent, $"{theme} must retain its error color at 10%.");
                Check(ColorOf(ThemeMetric(60).Accent) != ColorOf(normal) && ColorOf(ThemeMetric(60).Accent) != ColorOf(warning),
                    $"{theme} must blend its accent and warning colors at 60%.");
                Check(ColorOf(ThemeMetric(17).Accent) != ColorOf(warning) && ColorOf(ThemeMetric(17).Accent) != ColorOf(error),
                    $"{theme} must blend its warning and error colors at 17%.");

                var section = new StackPanel { Background = ResourceBrush("CardBrush") };
                foreach (var key in keys)
                    section.Resources[key] = resources[key];
                section.Children.Add(new TextBlock
                {
                    Text = theme.ToString(), FontSize = 18, FontWeight = FontWeights.SemiBold,
                    Foreground = ResourceBrush("TextBrush"), Margin = new Thickness(10, 8, 10, 4)
                });
                foreach (var remaining in new[] { 100d, 60d, 25d, 17d, 10d })
                {
                    var metric = ThemeMetric(remaining);
                    section.Children.Add(CheckCompact(metric, $"{theme}, {remaining}%"));
                }

                gallery.Children.Add(section);
            }

            Check(themeAccents.Count == 2, "Light and Dark must exercise distinct theme accents.");
            SaveGallery(gallery);
        }
        finally
        {
            foreach (var key in keys)
            {
                if (originals.TryGetValue(key, out var original))
                    resources[key] = original;
                else
                    resources.Remove(key);
            }
        }
    }

    private static WidgetView CheckCompact(UsageMetric metric, string context)
    {
        var provider = new WidgetViewModel { Plan = "Pro" };
        provider.Metrics.Add(metric);
        provider.Update();
        var model = new WidgetViewModel();
        model.Providers.Add(provider);
        var view = new WidgetView { DataContext = model, Width = 190 };
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Arrange(view, 190, 66);
        var bar = Descendants<ProgressBar>(view).Single();
        Check(bar.Value == metric.Remaining, $"{context}: compact bar must use remaining usage.");
        ColorEquals(ColorOf(metric.Accent), bar.Foreground, $"{context}: compact bar must use UsageMetric.Accent.");
        ColorEquals(ColorOf(ResourceBrush("TrackBrush")), bar.Background, $"{context}: the bar track must follow the current theme.");
        var indicator = Descendants<Border>(bar).Single(border => border.Name == "PART_Indicator");
        ColorEquals(ColorOf(metric.Accent), indicator.Background, $"{context}: the native bar template must render the metric color.");
        return view;
    }

    private static void ApplyPalette(WidgetTheme theme)
    {
        foreach (var pair in new ThemeCatalog().Create(theme, false))
            Application.Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
    }

    private static UsageMetric Metric(double remaining) => new(Window(remaining), Now, Normal, Warning, Error) { ProviderName = "Codex" };

    private static UsageMetric ThemeMetric(double remaining) => new(Window(remaining), Now,
        ResourceBrush("UsageBrush"), ResourceBrush("WarningBrush"), ResourceBrush("ErrorBrush")) { ProviderName = "Codex" };

    private static Brush ResourceBrush(string key) => (Brush)Application.Current.FindResource(key);

    private static UsageWindow Window(double remaining) => new()
    {
        Id = "color-test", Label = "주간", UsedPercent = 100 - remaining, WindowMinutes = 10080,
        ResetsAt = Now.AddDays(2)
    };

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
            yield return child;
    }

    private static void Arrange(FrameworkElement root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    private static void SaveGallery(FrameworkElement gallery)
    {
        gallery.Measure(new Size(190, double.PositiveInfinity));
        var height = (int)Math.Ceiling(gallery.DesiredSize.Height);
        Arrange(gallery, 190, height);
        var bitmap = new RenderTargetBitmap(190, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(gallery);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath("artifacts/layout-smoke");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, "usage-color-gradient.png"));
        encoder.Save(stream);
    }

    private static Color ColorOf(Brush brush) => brush is SolidColorBrush solid
        ? solid.Color
        : throw new InvalidOperationException("Usage colors must use a solid brush.");

    private static void ColorEquals(Color expected, Brush actual, string message)
    {
        Check(ColorOf(actual) == expected, $"{message} Expected {expected}; actual {ColorOf(actual)}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
