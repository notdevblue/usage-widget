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

namespace UsageMonitor.Presentation.Tests;

internal static class CompactBarTests
{
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        foreach (var scale in new[] { 0.8, 1, 1.5 })
            StableRows(scale);
        Console.WriteLine($"PASS {assertions} compact bar geometry assertions");
    }

    private static void StableRows(double scale)
    {
        var codex = new WidgetViewModel();
        codex.Metrics.Add(Metric(100, 10080));
        codex.Update();
        var claude = new WidgetViewModel { ProviderId = "claude", ProviderName = "Claude" };
        claude.Metrics.Add(Metric(9, 300));
        claude.Metrics.Add(Metric(0.5, 10080));
        claude.Update();
        var rootModel = new WidgetViewModel();
        rootModel.Providers.Add(codex);
        rootModel.Providers.Add(claude);
        var view = new WidgetView
        {
            DataContext = rootModel,
            LayoutTransform = new ScaleTransform(scale, scale)
        };
        var host = new Grid { Width = 190 * scale };
        host.Children.Add(view);
        Arrange(host);
        Close(190, view.ActualWidth, "Scaling must preserve the compact widget's logical width.");
        var baseline = Geometry(view, [100, 9, 0.5], ["100%", "9%", "<1%"]);
        Render(host, $"compact-bar-alignment-{scale * 100:0}.png");

        var remaining = new[] { 0d, 1, 99, 100 };
        for (var index = 0; index < remaining.Length; index++)
        {
            var values = new[]
            {
                remaining[index], remaining[(index + 1) % remaining.Length], remaining[(index + 2) % remaining.Length]
            };
            codex.Metrics[0] = Metric(values[0], 10080);
            claude.Metrics[0] = Metric(values[1], 300);
            claude.Metrics[1] = Metric(values[2], 10080);
            Arrange(host);
            var geometry = Geometry(view, values, values.Select(value => $"{value:0}%").ToArray());
            Close(baseline.Track.X, geometry.Track.X, "Updating percentages must preserve the bar's start.");
            Close(baseline.Track.Width, geometry.Track.Width, "Updating percentages must preserve the bar's length.");
            Close(baseline.PercentageRight, geometry.PercentageRight, "Updating percentages must preserve their right edge.");
        }
    }

    private static (Rect Track, double PercentageRight) Geometry(WidgetView view, double[] remaining, string[] percentages)
    {
        var bars = Descendants<ProgressBar>(view).ToArray();
        Check(bars.Length == 3, "Codex weekly and Claude's two windows must all render.");
        var tracks = new List<Rect>();
        var percentageEdges = new List<double>();
        for (var index = 0; index < bars.Length; index++)
        {
            var bar = bars[index];
            var row = (Grid)VisualTreeHelper.GetParent(bar);
            var label = row.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 0);
            var percentage = row.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 2);
            var track = TemplatePart(bar, "PART_Track");
            var indicator = TemplatePart(bar, "PART_Indicator");
            var bounds = Bounds(track, view);
            var percentageBounds = Bounds(percentage, view);
            tracks.Add(bounds);
            percentageEdges.Add(percentageBounds.Right);

            Check(label.Text == (index == 1 ? "5시간" : "주간"), "The geometry sample must include both label lengths.");
            Check(percentage.Text == percentages[index], "Each percentage must update to its window's remaining amount.");
            Check(percentage.TextTrimming == TextTrimming.None, "The full percentage must render without ellipsis.");
            Check(percentage.HorizontalAlignment == HorizontalAlignment.Right, "Percentages must align to the right edge.");
            Check(TextWidth(percentage) <= percentage.ActualWidth + 0.5, "One-, two-, three-digit and less-than-one percentages must fit fully.");
            Check(bounds.Width > 0, "Every compact bar must have a visible track.");
            Check(Bounds(label, view).Right < bounds.Left, "Window labels must not overlap the bar.");
            Check(bounds.Right < percentageBounds.Left, "Bars must not overlap percentages.");
            Close(Bounds(row, view).Right, percentageBounds.Right, "Each percentage must reach its row's right edge.");
            Close(4, bar.ActualHeight, "All compact bars must share a four-pixel height.");
            Close(4, track.ActualHeight, "The actual track must fill the four-pixel bar height.");
            Close(remaining[index], bar.Value, "The progress bar must retain the selected window's value.");
            Close(track.ActualWidth * remaining[index] / 100, indicator.ActualWidth, "The fill must update inside the stable track.", 0.75);
        }

        foreach (var track in tracks.Skip(1))
        {
            Close(tracks[0].X, track.X, "Five-hour and weekly bars must begin at the same widget coordinate.");
            Close(tracks[0].Width, track.Width, "Five-hour and weekly bars must have the same track length.");
        }

        foreach (var edge in percentageEdges.Skip(1))
            Close(percentageEdges[0], edge, "All provider rows must share the percentage's right edge.");
        return (tracks[0], percentageEdges[0]);
    }

    private static UsageMetric Metric(double remaining, int minutes) => new(new UsageWindow
    {
        Id = minutes.ToString(), Label = minutes == 300 ? "5시간" : "주간",
        WindowMinutes = minutes, UsedPercent = 100 - remaining,
        ResetsAt = new DateTimeOffset(2030, 1, 10, 12, 0, 0, TimeSpan.Zero)
    }, DateTimeOffset.UtcNow, Brushes.SeaGreen, Brushes.Goldenrod, Brushes.Firebrick);

    private static FrameworkElement TemplatePart(ProgressBar bar, string name) =>
        bar.Template.FindName(name, bar) is FrameworkElement part
            ? part
            : throw new InvalidOperationException($"The compact bar template must contain {name}.");

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static double TextWidth(TextBlock source)
    {
        var text = new TextBlock
        {
            Text = source.Text, FontFamily = source.FontFamily, FontSize = source.FontSize,
            FontWeight = source.FontWeight, FontStyle = source.FontStyle, FontStretch = source.FontStretch,
            FlowDirection = source.FlowDirection, TextWrapping = TextWrapping.NoWrap
        };
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
        var width = (int)Math.Ceiling(host.ActualWidth);
        var height = (int)Math.Ceiling(host.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath("artifacts/layout-smoke");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name));
        encoder.Save(stream);
    }

    private static void Close(double expected, double actual, string message, double tolerance = 0.01) =>
        Check(Math.Abs(expected - actual) < tolerance, $"{message} Expected {expected:0.###}, received {actual:0.###}.");

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
