using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UsageMonitor.Core;
using UsageMonitor.Layout;
using UsageMonitor.Services;
using ShapePath = System.Windows.Shapes.Path;

namespace UsageMonitor.Presentation.Tests;

internal static class CodexResetNoticeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(9));
    private static readonly string OutputDirectory = Path.GetFullPath("artifacts/layout-smoke");
    private const string ScheduledText = "Codex 리셋 · 10/27 03:00 예정";
    private const string AnnouncedText = "Codex 리셋 예고됨";
    private const string TicketScheduledText = "리셋 티켓 · 10/27 03:00 예정";
    private const string TicketAnnouncedText = "리셋 티켓 지급 예고됨";
    private const string DeadlineText = "리셋 · 10/27 03:00까지 예정";
    private const string TicketDeadlineText = "리셋 티켓 · 10/27 03:00까지 예정";
    private const string SourceUrl = "https://example.invalid/tibo/status/reset-announcement";
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        PendingNoticeAndExpiry();
        RevealOnlyOnVisibilityChange();
        CompactThemes();
        CustomLayoutKeepsNotice();
        ClickKeepsDragInput();
        ApplyPalette(WidgetTheme.Light);
        Console.WriteLine($"PASS {assertions} Codex reset notice presentation assertions");
    }

    private static void PendingNoticeAndExpiry()
    {
        var model = CreateModel();
        var view = CreateView(model);
        var initialHeight = Measure(view);
        var row = (Grid)view.FindName("ResetNoticeRow");
        var button = (Button)view.FindName("ResetNoticeButton");
        Check(row.Visibility == Visibility.Collapsed && row.Height == 0, "An absent notice must occupy no layout height.");
        Check(button.Visibility == Visibility.Collapsed, "The absent notice action must be collapsed.");
        model.ApplyResetNotice(Notice(ScheduledText), Now);
        Settle();
        Check(model.ResetNoticeText == ScheduledText, "The scheduled summary must preserve the service's exact text.");
        Check((string)button.ToolTip == "Tibo · 공지 원문", "The notice tooltip must preserve the service detail.");
        Check(Math.Abs(Measure(view) - initialHeight - 20) <= 1, "The notice must add only one slim 20px row.");
        model.ApplyResetNotice(Notice(ScheduledText), Now.AddHours(1));
        FlushBindings();
        Check(!model.ResetNoticeVisible && model.ResetNoticeText.Length == 0 && model.ResetNoticeSourceUrl.Length == 0,
            "A completed schedule must clear its text and link immediately.");
        Check(row.Visibility == Visibility.Collapsed && row.Height == 0 && row.Opacity == 0,
            "Hiding a notice must remove its height and animation immediately.");
        Check(Measure(view) == initialHeight, "Hiding a notice must restore the exact original widget height.");
        Render(view, "reset-news-hidden.png");
        model.ApplyResetNotice(Notice(AnnouncedText), Now);
        Settle();
        Check(model.ResetNoticeVisible && model.ResetNoticeText == AnnouncedText, "An announcement without a time must remain visible.");
        model.ApplyResetNotice(new(), Now);
        FlushBindings();
        Check(row.Visibility == Visibility.Collapsed && Measure(view) == initialHeight, "An empty service result must hide without a gap.");
    }

    private static void RevealOnlyOnVisibilityChange()
    {
        var model = CreateModel();
        var view = CreateView(model);
        var row = (Grid)view.FindName("ResetNoticeRow");
        model.ApplyResetNotice(Notice(ScheduledText), Now);
        FlushBindings();
        if (SystemParameters.ClientAreaAnimation)
            Check(row.Height < 20, "A newly visible notice must begin its short height reveal.");
        Settle();
        Check(row.Height == 20 && row.Opacity == 1, "The reveal must finish at its stable size and opacity.");
        model.ApplyResetNotice(Notice(ScheduledText), Now.AddMinutes(5));
        FlushBindings();
        Check(row.Height == 20 && row.Opacity == 1, "Refreshing the same announcement must not replay the reveal.");
        model.ApplyResetNotice(Notice(AnnouncedText), Now.AddMinutes(6));
        FlushBindings();
        Check(row.Height == 20 && row.Opacity == 1, "Updating visible notice text must not replay the reveal.");
        model.ApplyResetNotice(new(), Now);
        FlushBindings();
        model.ApplyResetNotice(Notice(AnnouncedText), Now);
        FlushBindings();
        if (SystemParameters.ClientAreaAnimation)
            Check(row.Height < 20, "A later transition from hidden to visible may reveal once again.");
        model.ApplyResetNotice(new(), Now);
        FlushBindings();
        Check(row.Height == 0 && row.Visibility == Visibility.Collapsed, "A notice hidden during its reveal must leave no animated height behind.");
    }

    private static void CompactThemes()
    {
        foreach (var theme in new[] { WidgetTheme.Light, WidgetTheme.Dark })
        foreach (var text in new[] { ScheduledText, AnnouncedText, TicketScheduledText, TicketAnnouncedText, DeadlineText, TicketDeadlineText })
        {
            ApplyPalette(theme);
            var model = CreateModel();
            var other = new WidgetViewModel { ProviderId = "claude", ProviderName = "Claude" };
            other.Metrics.Add(new UsageMetric(Window(), Now, Brushes.SeaGreen, Brushes.Goldenrod, Brushes.Firebrick));
            other.Update();
            model.Providers.Add(other);
            var view = CreateView(model);
            model.ApplyResetNotice(Notice(text), Now);
            Settle();
            Arrange(view);
            var button = (Button)view.FindName("ResetNoticeButton");
            var label = Descendants<TextBlock>(button).Single();
            Check(label.Text == text && label.TextWrapping == TextWrapping.NoWrap, "The notice must show the exact single-line summary.");
            var formatted = new FormattedText(label.Text, System.Globalization.CultureInfo.CurrentCulture,
                label.FlowDirection, new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch),
                label.FontSize, label.Foreground, new NumberSubstitution(), TextOptions.GetTextFormattingMode(label),
                VisualTreeHelper.GetDpi(label).PixelsPerDip);
            Check(formatted.WidthIncludingTrailingWhitespace <= label.ActualWidth + 1,
                $"The complete notice must fit within the 190px widget: '{text}', required {formatted.WidthIncludingTrailingWhitespace:0.##}px, available {label.ActualWidth:0.##}px.");
            var icon = Descendants<ShapePath>(button).Single();
            Check(!icon.Data.Bounds.IsEmpty && icon.Stroke == (Brush)Application.Current.Resources["SecondaryBrush"],
                "The outline bell must use the current theme's secondary brush.");
            Check(button.BorderThickness == new Thickness(0) && button.Height == 20, "The notice must remain a slim action without card chrome.");
            var automatic = Descendants<ItemsControl>(view).First(control => ReferenceEquals(control.ItemsSource, model.Providers));
            var noticeTop = button.TranslatePoint(new Point(), view).Y;
            var usageBottom = automatic.TranslatePoint(new Point(0, automatic.ActualHeight), view).Y;
            Check(noticeTop >= usageBottom, "The notice must follow the entire automatic provider region.");
            Render(view, $"reset-news-{Scenario(text)}-{theme.ToString().ToLowerInvariant()}.png");
        }
    }

    private static string Scenario(string text)
    {
        if (text == ScheduledText)
            return "scheduled";
        if (text == TicketScheduledText)
            return "ticket-scheduled";
        if (text == TicketAnnouncedText)
            return "ticket-announced";
        if (text == DeadlineText)
            return "deadline";
        if (text == TicketDeadlineText)
            return "ticket-deadline";
        return "announced";
    }

    private static void CustomLayoutKeepsNotice()
    {
        ApplyPalette(WidgetTheme.Light);
        var model = CreateModel();
        model.CustomLayoutVisible = true;
        model.AutomaticLayoutVisible = false;
        var view = CreateView(model);
        view.LayoutCanvas.Render(new()
        {
            Enabled = true, Width = 158, Height = 42,
            Blocks = [new() { Width = 158, Height = 42 }]
        }, new() { Windows = [Window()] });
        model.ApplyResetNotice(Notice(ScheduledText), Now);
        Settle();
        Arrange(view);
        var button = (Button)view.FindName("ResetNoticeButton");
        var noticeTop = button.TranslatePoint(new Point(), view).Y;
        var usageBottom = view.LayoutCanvas.TranslatePoint(new Point(0, view.LayoutCanvas.ActualHeight), view).Y;
        Check(view.LayoutCanvas.Children.Count == 1, "Showing a notice must retain the custom usage block.");
        Check(button.Visibility == Visibility.Visible && noticeTop >= usageBottom, "The notice must follow the whole custom canvas.");
        Render(view, "reset-news-custom.png");
    }

    private static void ClickKeepsDragInput()
    {
        var model = CreateModel();
        var view = CreateView(model);
        model.ApplyResetNotice(Notice(ScheduledText), Now);
        Settle();
        Arrange(view);
        var button = (Button)view.FindName("ResetNoticeButton");
        var label = Descendants<TextBlock>(button).Single();
        var dragCalls = 0;
        var clickedUrl = "";
        WidgetDragInput.Attach(view, () => dragCalls++);
        view.ResetNoticeRequested += url => clickedUrl = url;
        var input = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        };
        label.RaiseEvent(input);
        Check(dragCalls == 0 && !input.Handled, "Notice label input must reach its button without starting a widget drag.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(clickedUrl == SourceUrl, "The notice event must expose its matching source URL without launching a browser in tests.");
        var providerLabel = Descendants<TextBlock>(view).Single(text => text.Text == "Codex ·");
        providerLabel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        });
        Check(dragCalls == 1, "Ordinary usage content must keep its existing drag behavior.");
    }

    private static CodexResetNotice Notice(string text) => new()
    {
        Text = text, ToolTip = "Tibo · 공지 원문", SourceUrl = SourceUrl, ExpiresAt = Now.AddHours(1)
    };

    private static UsageWindow Window() => new()
    {
        Id = "weekly", Label = "주간", WindowMinutes = 10080, UsedPercent = 36, ResetsAt = Now.AddDays(2)
    };

    private static WidgetViewModel CreateModel()
    {
        var provider = new WidgetViewModel();
        provider.Metrics.Add(new UsageMetric(Window(), Now, Brushes.SeaGreen, Brushes.Goldenrod, Brushes.Firebrick));
        provider.Update();
        var model = new WidgetViewModel();
        model.Providers.Add(provider);
        return model;
    }

    private static WidgetView CreateView(WidgetViewModel model)
    {
        var view = new WidgetView { DataContext = model };
        FlushBindings();
        Arrange(view);
        return view;
    }

    private static void FlushBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void Settle()
    {
        FlushBindings();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        timer.Tick += (sender, args) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static double Measure(FrameworkElement element)
    {
        element.Measure(new Size(190, double.PositiveInfinity));
        return element.DesiredSize.Height;
    }

    private static void Arrange(FrameworkElement element)
    {
        var height = Measure(element);
        element.Arrange(new Rect(0, 0, 190, height));
        element.UpdateLayout();
    }

    private static void Render(FrameworkElement element, string name)
    {
        Arrange(element);
        var bitmap = new RenderTargetBitmap(190, (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(OutputDirectory);
        using var stream = File.Create(Path.Combine(OutputDirectory, name));
        encoder.Save(stream);
    }

    private static void ApplyPalette(WidgetTheme theme)
    {
        foreach (var pair in new ThemeCatalog().Create(theme, new(), false))
            Application.Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match)
            yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(parent, index)))
            yield return child;
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (condition == false)
            throw new InvalidOperationException(message);
    }
}
