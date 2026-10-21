using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using UsageMonitor.Core;
using UsageMonitor.Presentation;
using UsageMonitor.Services;

namespace UsageMonitor.Layout;

public sealed class UsageLayoutCanvas : Canvas
{
    public UsageLayoutCanvas()
    {
        ClipToBounds = true;
        UseLayoutRounding = true;
    }

    public void Render(LayoutProfile profile, LayoutContext context)
    {
        profile = LayoutProfile.Normalize(profile);
        Children.Clear();
        Width = profile.Width;
        var bottom = 0d;
        foreach (var block in profile.Blocks)
        {
            if (block.Kind is LayoutBlockKind.Usage or LayoutBlockKind.Reset &&
                !TryGetWindow(context, block, out _))
            {
                continue;
            }

            var content = CreateBlock(block, context);
            content.Width = block.Width;
            content.Height = block.Height;
            content.ClipToBounds = true;
            SetLeft(content, block.X);
            SetTop(content, block.Y);
            Children.Add(content);
            bottom = Math.Max(bottom, block.Y + block.Height);
        }

        Height = profile.AutoHeight ? bottom : profile.Height;
    }

    internal static bool TryGetWindow(LayoutContext context, LayoutBlock block, out UsageWindow window)
    {
        window = new();
        var provider = context.ResolveProvider(block.ProviderId);
        if (block.WindowId.Length > 0)
        {
            foreach (var candidate in provider.Windows)
            {
                if (!string.Equals(candidate.Id, block.WindowId, StringComparison.Ordinal))
                    continue;
                window = candidate;
                return true;
            }

            return false;
        }

        var index = block.Source == UsageWindowSource.Second ? 1 : 0;
        if (block.Source is UsageWindowSource.First or UsageWindowSource.Second)
        {
            if (provider.Windows.Count <= index)
                return false;
            window = provider.Windows[index];
            return true;
        }

        foreach (var candidate in provider.Windows)
        {
            if (block.Source == UsageWindowSource.FiveHour && candidate.IsFiveHour ||
                block.Source == UsageWindowSource.Weekly && candidate.WindowMinutes == 10080)
            {
                window = candidate;
                return true;
            }
        }

        return false;
    }

    private FrameworkElement CreateBlock(LayoutBlock block, LayoutContext context)
    {
        var provider = context.ResolveProvider(block.ProviderId);
        if (block.Kind == LayoutBlockKind.Account)
            return CreateAccount(provider);
        if (block.Kind == LayoutBlockKind.Status)
            return CreateStatus(provider);

        TryGetWindow(context, block, out var window);
        var metric = new UsageMetric(window with { Label = $"{provider.ProviderName} · {window.Label}" }, DateTimeOffset.UtcNow,
            ResolveBrush("AccentBrush"), ResolveBrush("WarningBrush"), ResolveBrush("ErrorBrush"));
        var accent = metric.Accent;
        if (block.Kind == LayoutBlockKind.Reset)
            return CreateReset(block, metric);

        var content = block.Visualization switch
        {
            UsageVisualization.Ring => CreateRing(block, metric, accent),
            UsageVisualization.Number => CreateNumber(block, metric, accent),
            _ => CreateBar(block, metric, accent)
        };
        AutomationProperties.SetName(content, $"{metric.Label}, 남은 사용량 {metric.Amount}%");
        return content;
    }

    private static Grid CreateBar(LayoutBlock block, UsageMetric metric, Brush accent)
    {
        var grid = new Grid { Margin = new Thickness(1, 0, 1, 0) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        if (block.ShowLabel && block.Height >= 54)
        {
            var label = Text(metric.Label, 11, "SecondaryBrush");
            grid.Children.Add(label);
        }

        var amount = Text($"{metric.Amount}%", Math.Clamp(Math.Min(block.Height * 0.39, block.Width / 3.4), 14, 45), accent);
        amount.FontWeight = FontWeights.SemiBold;
        amount.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(amount, 1);
        grid.Children.Add(amount);
        if (block.Height >= 45)
        {
            var track = new Grid { Height = 6, Margin = new Thickness(0, 2, 0, 0) };
            track.Children.Add(FilledBorder("TrackBrush", 3));
            var fill = FilledBorder(accent, 3);
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.Width = Math.Max(0, block.Width - 2) * metric.Remaining / 100;
            track.Children.Add(fill);
            Grid.SetRow(track, 2);
            grid.Children.Add(track);
        }

        if (block.ShowReset && block.Height >= 88)
        {
            var reset = Text(metric.Reset, 10, "SecondaryBrush");
            reset.Margin = new Thickness(0, 6, 0, 0);
            reset.ToolTip = metric.ResetDetail;
            Grid.SetRow(reset, 3);
            grid.Children.Add(reset);
        }

        return grid;
    }

    private static Grid CreateNumber(LayoutBlock block, UsageMetric metric, Brush accent)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (block.ShowLabel && block.Height >= 54)
            stack.Children.Add(Text(metric.Label, 11, "SecondaryBrush"));
        var amount = Text($"{metric.Amount}%", Math.Clamp(Math.Min(block.Height * 0.42, block.Width / 3.4), 14, 56), accent);
        amount.FontWeight = FontWeights.SemiBold;
        stack.Children.Add(amount);
        if (block.ShowReset && block.Height >= 92)
        {
            var reset = Text(metric.Reset, 10, "SecondaryBrush");
            reset.ToolTip = metric.ResetDetail;
            stack.Children.Add(reset);
        }

        var grid = new Grid();
        grid.Children.Add(stack);
        return grid;
    }

    private static Grid CreateRing(LayoutBlock block, UsageMetric metric, Brush accent)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        if (block.ShowLabel && block.Height >= 70)
        {
            var label = Text(metric.Label, 11, "SecondaryBrush");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 0, 0, 5);
            grid.Children.Add(label);
        }

        var dial = new Grid { Width = 100, Height = 100 };
        var track = new Ellipse { StrokeThickness = 7, Margin = new Thickness(5) };
        track.SetResourceReference(Shape.StrokeProperty, "TrackBrush");
        dial.Children.Add(track);
        if (metric.Remaining >= 100)
        {
            var full = new Ellipse { StrokeThickness = 7, Margin = new Thickness(5) };
            full.Stroke = accent;
            dial.Children.Add(full);
        }
        else if (metric.Remaining > 0)
        {
            var radians = (metric.Remaining * 3.6 - 90) * Math.PI / 180;
            var segment = new ArcSegment
            {
                Point = new Point(50 + 45 * Math.Cos(radians), 50 + 45 * Math.Sin(radians)),
                Size = new Size(45, 45),
                IsLargeArc = metric.Remaining > 50,
                SweepDirection = SweepDirection.Clockwise
            };
            var figure = new PathFigure { StartPoint = new Point(50, 5), IsClosed = false };
            figure.Segments.Add(segment);
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            var arc = new Path
            {
                Data = geometry,
                StrokeThickness = 7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            arc.Stroke = accent;
            dial.Children.Add(arc);
        }

        var amount = Text($"{metric.Amount}%", 23, accent);
        amount.FontWeight = FontWeights.SemiBold;
        amount.HorizontalAlignment = HorizontalAlignment.Center;
        amount.VerticalAlignment = VerticalAlignment.Center;
        dial.Children.Add(amount);
        var view = new Viewbox { Child = dial, Stretch = Stretch.Uniform, Margin = new Thickness(0, 1, 0, 1) };
        Grid.SetRow(view, 1);
        grid.Children.Add(view);
        if (block.ShowReset && block.Height >= 120)
        {
            var reset = Text(metric.Reset, 9, "SecondaryBrush");
            reset.HorizontalAlignment = HorizontalAlignment.Center;
            reset.Margin = new Thickness(0, 5, 0, 0);
            reset.ToolTip = metric.ResetDetail;
            Grid.SetRow(reset, 2);
            grid.Children.Add(reset);
        }

        return grid;
    }

    private static Grid CreateReset(LayoutBlock block, UsageMetric metric)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (block.ShowLabel && block.Height >= 44)
            stack.Children.Add(Text($"{metric.Label} 초기화", 10, "SecondaryBrush"));
        var reset = Text(metric.Reset, 11, "TextBrush");
        reset.ToolTip = metric.ResetDetail;
        stack.Children.Add(reset);
        var grid = new Grid();
        grid.Children.Add(stack);
        return grid;
    }

    private static Grid CreateAccount(LayoutProviderContext context)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Text(context.Account.Length > 0 ? context.Account : "계정 정보 없음", 12, "TextBrush"));
        if (context.Plan.Length > 0)
            stack.Children.Add(Text(context.Plan, 10, "SecondaryBrush"));
        var grid = new Grid();
        grid.Children.Add(stack);
        return grid;
    }

    private static Grid CreateStatus(LayoutProviderContext context)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Ellipse
        {
            Width = 5, Height = 5, Fill = context.StatusBrush,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 7, 0)
        });
        var status = Text(context.Status.Length > 0 ? context.Status : "상태 정보 없음", 10, "SecondaryBrush");
        status.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(status, 1);
        grid.Children.Add(status);
        return grid;
    }

    private Brush ResolveBrush(string key) => TryFindResource(key) is Brush brush ? brush : Brushes.Gray;

    private static TextBlock Text(string value, double size, string brushKey)
    {
        var text = Text(value, size, Brushes.Gray);
        text.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return text;
    }

    private static Border FilledBorder(string brushKey, double radius)
    {
        var border = FilledBorder(Brushes.Transparent, radius);
        border.SetResourceReference(Border.BackgroundProperty, brushKey);
        return border;
    }

    private static TextBlock Text(string value, double size, Brush brush) => new()
    {
        Text = value, FontSize = size, Foreground = brush, TextTrimming = TextTrimming.CharacterEllipsis
    };

    private static Border FilledBorder(Brush brush, double radius) => new()
    {
        CornerRadius = new CornerRadius(radius), Background = brush
    };
}
