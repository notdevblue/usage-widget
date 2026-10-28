using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UsageMonitor.Core;
using UsageMonitor.Presentation;
using UsageMonitor.Services;

namespace UsageMonitor.Presentation.Tests;

internal static class CompactBarTests
{
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        Palette(WidgetTheme.Light);
        foreach (var scale in new[] { 0.8, 1, 1.5 })
        foreach (var count in new[] { 4, 6 })
            StableRows(scale, count);
        ThemePreviews();
        Console.WriteLine($"PASS {assertions} compact menu geometry assertions");
    }

    private static void StableRows(double scale, int count)
    {
        var model = Model(count);
        var view = new WidgetView { DataContext = model, LayoutTransform = new ScaleTransform(scale, scale) };
        var host = new Grid { Width = 190 * scale };
        host.Children.Add(view);
        Arrange(host);
        Close(190, view.ActualWidth, "Scaling must preserve the compact menu's logical width.");
        var baseline = Geometry(view, Metrics(model));
        Render(host, $"compact-menu-alignment-{count}-{scale * 100:0}.png");

        var remaining = new[] { 0d, 0.5, 1, 9, 64, 99, 100 };
        for (var offset = 0; offset < remaining.Length; offset++)
        {
            var index = 0;
            foreach (var provider in model.Providers)
            {
                for (var window = 0; window < provider.Metrics.Count; window++)
                {
                    var metric = provider.Metrics[window];
                    provider.Metrics[window] = Metric(remaining[(offset + index) % remaining.Length], metric.Label, provider.ProviderName);
                    index++;
                }
            }

            Arrange(host);
            var geometry = Geometry(view, Metrics(model));
            Close(baseline.Track.X, geometry.Track.X, "Updating percentages must preserve the bar's start.");
            Close(baseline.Track.Width, geometry.Track.Width, "Updating percentages must preserve the bar's length.");
            Close(baseline.PercentageRight, geometry.PercentageRight, "Updating percentages must preserve their right edge.");
        }
    }

    private static (Rect Track, double PercentageRight) Geometry(WidgetView view, UsageMetric[] metrics)
    {
        var bars = Descendants<ProgressBar>(view).ToArray();
        Check(bars.Length == metrics.Length, "Every provider quota must render in the compact menu.");
        var tracks = new List<Rect>();
        var labels = new List<Rect>();
        var percentageEdges = new List<double>();
        for (var index = 0; index < bars.Length; index++)
        {
            var bar = bars[index];
            var metric = metrics[index];
            var row = (Grid)VisualTreeHelper.GetParent(bar);
            var label = row.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 0);
            var percentage = row.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 2);
            var track = TemplatePart(bar, "PART_Track");
            var indicator = TemplatePart(bar, "PART_Indicator");
            var bounds = Bounds(track, view);
            var labelBounds = Bounds(label, view);
            var percentageBounds = Bounds(percentage, view);
            tracks.Add(bounds);
            labels.Add(labelBounds);
            percentageEdges.Add(percentageBounds.Right);

            Check(label.Text == metric.Label && percentage.Text == metric.Percentage, "Each row must preserve its period and complete percentage.");
            Check(percentage.TextTrimming == TextTrimming.None, "Percentages must never use ellipsis.");
            Check(percentage.HorizontalAlignment == HorizontalAlignment.Right, "Percentages must align to the right edge.");
            Check(Typography.GetNumeralAlignment(percentage) == FontNumeralAlignment.Tabular, "Percentages must use tabular numerals.");
            Check(TextWidth(label) <= label.ActualWidth + 0.5, $"The common period '{label.Text}' must fit without trimming.");
            var desiredWidth = TextWidth(percentage);
            Check(desiredWidth <= percentage.ActualWidth + 0.5,
                $"Percentage '{percentage.Text}' must fit at {percentage.FontSize:0.##}px: desired {desiredWidth:0.##}px, actual {percentage.ActualWidth:0.##}px.");
            Close(20, row.ActualHeight, "Quota rows must retain their compact height.");
            Close(11.5, percentage.FontSize, "Percentage text must retain its fixed readable size.");
            Check(bounds.Width > 0, "Every compact quota must have a visible track.");
            Check(labelBounds.Right < bounds.Left, "Period labels must not overlap the bar.");
            Check(bounds.Right < percentageBounds.Left, "Bars must not overlap percentages.");
            Close(Bounds(row, view).Right, percentageBounds.Right, "Every percentage must reach its row's right edge.");
            Close(4, bar.ActualHeight, "All bars must share a four-pixel height.");
            Close(4, track.ActualHeight, "The actual track must fill the four-pixel bar.");
            Close(metric.Remaining, bar.Value, "The bar must retain the exact remaining value.");
            Close(track.ActualWidth * metric.Remaining / 100, indicator.ActualWidth, "The fill must update proportionally inside the stable track.", 0.75);
            Check(ReferenceEquals(metric.Accent, bar.Foreground), "Every bar must use its quota's continuous usage color.");
            Check(ReferenceEquals(Application.Current.Resources["TrackBrush"], bar.Background), "Tracks must use the neutral theme brush.");
            Check(AutomationProperties.GetName(bar) == $"{metric.ProviderName} · {metric.Label}", "Assistive technology must receive both provider and period.");
        }

        for (var index = 1; index < tracks.Count; index++)
        {
            Close(tracks[0].X, tracks[index].X, "All provider bars must begin at the same coordinate.");
            Close(tracks[0].Width, tracks[index].Width, "All provider bars must have the same length.");
            Close(labels[0].X, labels[index].X, "All period columns must align.");
            Close(percentageEdges[0], percentageEdges[index], "All percentages must share one right edge.");
        }

        return (tracks[0], percentageEdges[0]);
    }

    private static WidgetViewModel Model(int count)
    {
        var root = new WidgetViewModel();
        var names = new[] { "Codex", "Claude", "Grok" };
        var counts = ProviderQuotaCounts(count);
        var values = new[] { 64d, 63, 90, 82, 99, 100 };
        var valueIndex = 0;
        for (var index = 0; index < names.Length; index++)
        {
            var provider = new WidgetViewModel { ProviderId = names[index].ToLowerInvariant(), ProviderName = names[index] };
            for (var window = 0; window < counts[index]; window++)
            {
                provider.Metrics.Add(Metric(values[valueIndex], WindowLabel(counts[index], window, provider.ProviderName), provider.ProviderName));
                valueIndex++;
            }

            provider.Update();
            root.Providers.Add(provider);
        }

        return root;
    }

    private static int[] ProviderQuotaCounts(int count)
    {
        if (count == 6)
            return [2, 2, 2];
        else
            return [1, 2, 1];
    }

    private static string WindowLabel(int count, int index, string provider)
    {
        if (provider == "Grok" && index == count - 1)
            return "크레딧";
        if (count == 2 && index == 0)
            return "5시간";
        return "주간";
    }

    private static UsageMetric[] Metrics(WidgetViewModel model) => model.Providers.SelectMany(provider => provider.Metrics).ToArray();

    private static UsageMetric Metric(double remaining, string label, string provider) => new(new UsageWindow
    {
        Id = label, Label = label, WindowMinutes = 10080, UsedPercent = 100 - remaining,
        ResetsAt = new DateTimeOffset(2030, 1, 10, 12, 0, 0, TimeSpan.Zero)
    }, DateTimeOffset.UtcNow, (Brush)Application.Current.Resources["UsageBrush"],
        (Brush)Application.Current.Resources["WarningBrush"], (Brush)Application.Current.Resources["ErrorBrush"])
    {
        ProviderName = provider
    };

    private static FrameworkElement TemplatePart(ProgressBar bar, string name)
    {
        if (bar.Template.FindName(name, bar) is FrameworkElement part)
            return part;
        throw new InvalidOperationException($"The compact bar template must contain {name}.");
    }

    private static void ThemePreviews()
    {
        foreach (var theme in new[] { WidgetTheme.Light, WidgetTheme.Dark })
        {
            Palette(theme);
            var model = Model(4);
            var view = new WidgetView { DataContext = model };
            var host = new Grid { Width = 190 };
            host.Children.Add(view);
            Arrange(host);
            Geometry(view, Metrics(model));
            Close(114, host.ActualHeight, "Three providers and four quotas must retain the compact menu height.");
            var surface = (Border)view.FindName("MenuSurface");
            var frame = (Border)view.FindName("MenuFrame");
            Close(0.96, surface.Opacity, "Only the menu surface must be slightly translucent.");
            Close(1, frame.Opacity, "Text and controls must remain opaque.");
            Render(host, $"compact-menu-{theme.ToString().ToLowerInvariant()}.png");
        }

        Palette(WidgetTheme.Light);
    }

    private static void Palette(WidgetTheme theme)
    {
        foreach (var pair in new ThemeCatalog().Create(theme, false))
            Application.Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
    }

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static double TextWidth(TextBlock source)
    {
        var text = new TextBlock
        {
            Text = source.Text, FontFamily = source.FontFamily, FontSize = source.FontSize,
            FontWeight = source.FontWeight, FontStyle = source.FontStyle, FontStretch = source.FontStretch,
            FlowDirection = source.FlowDirection, Language = source.Language,
            LineHeight = source.LineHeight, LineStackingStrategy = source.LineStackingStrategy
        };
        Typography.SetNumeralAlignment(text, Typography.GetNumeralAlignment(source));
        Typography.SetNumeralStyle(text, Typography.GetNumeralStyle(source));
        TextOptions.SetTextFormattingMode(text, TextOptions.GetTextFormattingMode(source));
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return text.DesiredSize.Width;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
            yield return child;
    }

    private static void Arrange(FrameworkElement host)
    {
        host.Measure(new Size(host.Width, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, host.Width, Math.Ceiling(host.DesiredSize.Height)));
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        host.Measure(new Size(host.Width, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, host.Width, Math.Ceiling(host.DesiredSize.Height)));
        host.UpdateLayout();
    }

    private static void Render(FrameworkElement host, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath("artifacts/layout-smoke");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name));
        encoder.Save(stream);
    }

    private static void Close(double expected, double actual, string message, double tolerance = 0.01) =>
        Check(Math.Abs(expected - actual) <= tolerance, $"{message} Expected {expected:0.###}, received {actual:0.###}.");

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (condition == false)
            throw new InvalidOperationException(message);
    }
}
