using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;
using UsageMonitor.Core;
using UsageMonitor.Services;

namespace UsageMonitor.Presentation.Tests;

internal static class Program
{
    private static int assertions;
    private static readonly string OutputDirectory = Path.GetFullPath("artifacts/layout-smoke");

    [STAThread]
    private static int Main()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/UsageMonitor;component/Themes/WidgetStyles.xaml")
        });
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            IconButtons();
            StandaloneSettings();
            CompactWidget();
            CodexResetNoticeTests.Run();
            CompactBarTests.Run();
            UsageColorTests.Run();
            WidgetDragInputTests.Run();
            ProviderRuntimeTests.Run();
            ProviderVisibilityTests.Run();
            StartupSettingsTests.Run();
            Console.WriteLine($"PASS {assertions} presentation assertions; PNGs: {OutputDirectory}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL {error}");
            return 1;
        }
        finally
        {
            application.Shutdown();
        }
    }

    private static void IconButtons()
    {
        var background = (SolidColorBrush)Application.Current.FindResource("CardBrush");
        var strip = new Canvas { Width = 310, Height = 76, Background = background };
        var buttons = new List<Button>();
        var names = new[] { "pin", "unpin", "settings", "hide", "refresh" };
        for (var index = 0; index < names.Length; index++)
        {
            var button = new Button
            {
                Content = names[index], Style = (Style)Application.Current.FindResource("IconButton")
            };
            Canvas.SetLeft(button, 10 + 60 * index);
            Canvas.SetTop(button, 8);
            strip.Children.Add(button);
            buttons.Add(button);
            var label = new TextBlock { Text = names[index], FontSize = 9 };
            Canvas.SetLeft(label, 8 + 60 * index);
            Canvas.SetTop(label, 44);
            strip.Children.Add(label);
        }

        var bitmap = Render(strip, 310, 76, "vector-icons.png");
        var pixels = new byte[310 * 76 * 4];
        bitmap.CopyPixels(pixels, 310 * 4, 0);
        var geometries = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < buttons.Count; index++)
        {
            var path = Descendants<ShapePath>(buttons[index]).Single();
            True(!path.Data.Bounds.IsEmpty);
            True(path.Stroke is Brush);
            geometries.Add(path.Data.ToString());
            var ink = 0;
            for (var y = 8; y < 35; y++)
            for (var x = 10 + 60 * index; x < 37 + 60 * index; x++)
            {
                var offset = (y * 310 + x) * 4;
                if (pixels[offset] != background.Color.B || pixels[offset + 1] != background.Color.G ||
                    pixels[offset + 2] != background.Color.R)
                    ink++;
            }

            True(ink > 8);
        }

        Equal(5, geometries.Count);
    }

    private static void StandaloneSettings()
    {
        var initial = new AppSettings
        {
            CodexPath = @"C:\private\codex.exe", ClaudePath = @"C:\private\claude.exe", HasPosition = true, Left = 87, Top = 143
        };
        var window = new SettingsWindow(initial);
        var captured = new AppSettings();
        var changes = 0;
        var logins = 0;
        var executableRequests = 0;
        var claudeLogins = 0;
        var claudeExecutableRequests = 0;
        var grokLogins = 0;
        var grokExecutableRequests = 0;
        var quits = 0;
        window.SettingsChanged += updated =>
        {
            captured = updated;
            changes++;
            ApplyPalette(updated.Theme);
        };
        window.LoginRequested += () => logins++;
        window.ExecutableRequested += () => executableRequests++;
        window.ClaudeLoginRequested += () => claudeLogins++;
        window.ClaudeExecutableRequested += () => claudeExecutableRequests++;
        window.GrokLoginRequested += () => grokLogins++;
        window.GrokExecutableRequested += () => grokExecutableRequests++;
        window.QuitRequested += () => quits++;
        var root = (FrameworkElement)window.Content;
        Arrange(root, 370, 720);
        Equal(3, ByName<ComboBox>(root, "화면 테마").Items.Count);
        Equal(2, ByName<ComboBox>(root, "5시간 한도 표시").Items.Count);
        True(!Descendants<Button>(root).Any(button => button.Content is string text &&
            (text.Contains("레이아웃", StringComparison.Ordinal) || text.Contains("테마 불러오기", StringComparison.Ordinal) || text.Contains("테마 내보내기", StringComparison.Ordinal))));
        True(!Descendants<Button>(root).Any(button => button.Content is string text &&
            (text.Contains("새로고침", StringComparison.Ordinal) || text.Contains("동기화", StringComparison.Ordinal))));
        ByName<CheckBox>(root, "항상 위에 표시").IsChecked = true;
        ByName<ComboBox>(root, "5시간 한도 표시").SelectedIndex = 1;
        ByName<Slider>(root, "위젯 크기").Value = 145;
        ByName<Slider>(root, "위젯 불투명도").Value = 65;
        True(captured.AlwaysOnTop);
        Equal(FiveHourDisplay.Hidden, captured.FiveHourDisplay);
        Equal(1.45, captured.Scale);
        Equal(0.65, captured.Opacity);
        Equal(1d, window.Opacity);
        True(!window.Topmost);
        Equal(initial.CodexPath, captured.CodexPath);
        Equal(initial.ClaudePath, captured.ClaudePath);
        Equal(initial.Left, captured.Left);
        Equal(initial.Top, captured.Top);
        ByName<ComboBox>(root, "화면 테마").SelectedIndex = (int)WidgetTheme.Dark;
        Equal(WidgetTheme.Dark, captured.Theme);
        Render(root, 370, 720, "settings-dark.png");
        ByName<ComboBox>(root, "화면 테마").SelectedIndex = (int)WidgetTheme.Light;
        Equal(WidgetTheme.Light, captured.Theme);
        Render(root, 370, 720, "settings-light.png");
        window.UpdateConnection("", "Codex 설치 안내", true, true, "Codex 실행 파일을 선택해 주세요.");
        Click(root, "Codex 설치 안내");
        Click(root, "Codex 실행 파일 선택");
        Equal(1, logins);
        Equal(1, executableRequests);
        True(Descendants<TextBlock>(root).Any(text => text.Text == "연결된 계정 없음"));
        window.UpdateConnection("member@example.invalid", "로그인 취소 중", false, false, "브라우저에서 기다리는 중");
        True(!Descendants<Button>(root).Single(button => button.Content is string text && text == "로그인 취소 중").IsEnabled);
        window.UpdateClaudeConnection("claude@example.invalid", "Claude에서 로그인", true, false, "Claude 연결 확인");
        Click(root, "Claude에서 로그인");
        Click(root, "Claude 실행 파일 선택");
        Equal(1, claudeLogins);
        Equal(1, claudeExecutableRequests);
        True(Descendants<TextBlock>(root).Any(text => text.Text == "claude@example.invalid"));
        window.UpdateClaudeConnection("", "Claude 설치 안내", false, true, "Claude를 설치해주세요.");
        True(!Descendants<Button>(root).Single(button => button.Content is string text && text == "Claude 설치 안내").IsEnabled);
        window.UpdateGrokConnection("grok@example.invalid", "Grok에서 로그인", true, false);
        Click(root, "Grok에서 로그인");
        Click(root, "Grok 실행 파일 선택");
        Equal(1, grokLogins);
        Equal(1, grokExecutableRequests);
        window.ShowFeedback("설정 창 안의 피드백");
        True(Descendants<TextBlock>(root).Any(text => text.Text == "설정 창 안의 피드백" && text.Visibility == Visibility.Visible));
        Click(root, "외형 기본값으로");
        True(captured.AlwaysOnTop);
        Equal(FiveHourDisplay.Hidden, captured.FiveHourDisplay);
        Equal(initial.CodexPath, captured.CodexPath);
        Equal(initial.ClaudePath, captured.ClaudePath);
        Equal(WidgetTheme.System, captured.Theme);
        Equal(1d, captured.Scale);
        Equal(1d, captured.Opacity);
        True(changes >= 7);
        var changesBeforeExternalUpdate = changes;
        window.UpdateSettings(captured with { AlwaysOnTop = false });
        Equal(changesBeforeExternalUpdate, changes);
        True(ByName<CheckBox>(root, "항상 위에 표시").IsChecked == false);
        ByName<ComboBox>(root, "화면 테마").SelectedIndex = (int)WidgetTheme.Dark;
        True(!captured.AlwaysOnTop);
        Click(root, "앱 종료");
        Equal(1, quits);
        window.Close();
        ApplyPalette(WidgetTheme.Light);

    }

    private static void CompactWidget()
    {
        ApplyPalette(WidgetTheme.Light);
        var codex = new WidgetViewModel();
        codex.Metrics.Add(WidgetMetric("Codex", 64, 10080));
        codex.Update();
        var rootModel = new WidgetViewModel();
        rootModel.Providers.Add(codex);
        var view = new WidgetView { DataContext = rootModel };
        RenderWidget(view, "compact-weekly.png");
        var weeklyHeight = view.ActualHeight;
        True(weeklyHeight > 0);
        Equal(0, Descendants<Button>(view).Count(button => button.Visibility == Visibility.Visible));
        Equal(1, Descendants<ProgressBar>(view).Count());
        True(Descendants<TextBlock>(view).Any(text => text.Text == "Codex"));
        codex.Metrics.Insert(0, WidgetMetric("Codex", 72, 300));
        codex.Update();
        view = new WidgetView { DataContext = rootModel };
        RenderWidget(view, "compact-dual.png");
        True(view.ActualHeight > weeklyHeight);
        Equal(2, Descendants<ProgressBar>(view).Count());
        ApplyPalette(WidgetTheme.Dark);
        RenderWidget(new WidgetView { DataContext = rootModel }, "compact-dark.png");
        ApplyPalette(WidgetTheme.Light);
        codex.Metrics.RemoveAt(0);
        codex.Update();
        var claude = new WidgetViewModel { ProviderId = "claude", ProviderName = "Claude" };
        claude.Metrics.Add(WidgetMetric("Claude", 63, 300));
        claude.Metrics.Add(WidgetMetric("Claude", 90, 10080));
        claude.Update();
        rootModel.Providers.Add(claude);
        view = new WidgetView { DataContext = rootModel };
        RenderWidget(view, "compact-codex-claude.png");
        var threeLimitHeight = view.ActualHeight;
        True(threeLimitHeight > weeklyHeight);
        Equal(3, Descendants<ProgressBar>(view).Count());
        True(Descendants<TextBlock>(view).Any(text => text.Text == "Claude"));
        var grok = new WidgetViewModel { ProviderId = "grok", ProviderName = "Grok" };
        grok.Metrics.Add(WidgetMetric("Grok", 85, 10080));
        grok.Update();
        rootModel.Providers.Add(grok);
        var complete = new WidgetView { DataContext = rootModel };
        RenderWidget(complete, "compact-all-providers.png");
        True(complete.ActualHeight > threeLimitHeight);
        True(complete.ActualHeight <= 130);
        Equal(4, Descendants<ProgressBar>(complete).Count());
        True(Descendants<TextBlock>(complete).Any(text => text.Text == "Grok"));
        Console.WriteLine($"Full widget: {complete.ActualWidth:0} x {complete.ActualHeight:0} (3 providers, 4 limits)");
        codex.Metrics.Insert(0, WidgetMetric("Codex", 72, 300));
        grok.Metrics.Insert(0, WidgetMetric("Grok", 38, 300));
        var six = new WidgetView { DataContext = rootModel };
        RenderWidget(six, "compact-six-limits.png");
        Equal(6, Descendants<ProgressBar>(six).Count());
        True(six.ActualHeight > complete.ActualHeight);
        True(six.ActualHeight <= 180);
        True(Descendants<ProgressBar>(six).Select(bar => bar.Value).SequenceEqual(rootModel.Providers.SelectMany(provider => provider.Metrics).Select(metric => metric.Remaining)));
        Console.WriteLine($"Full widget: {six.ActualWidth:0} x {six.ActualHeight:0} (3 providers, 6 limits)");
        rootModel.Providers.Remove(grok);
        codex.Metrics.RemoveAt(0);
        claude.Metrics.Clear();
        claude.EmptyTitle = "연결 대기";
        claude.Stale = true;
        claude.Update();
        rootModel.Stale = true;
        view = new WidgetView { DataContext = rootModel };
        RenderWidget(view, "compact-claude-unavailable.png");
        Equal(1, Descendants<ProgressBar>(view).Count());
        True(Descendants<TextBlock>(view).Any(text => text.Text == "연결 대기"));
    }

    private static UsageMetric WidgetMetric(string providerName, double remaining, int minutes) =>
        new(Window(remaining, minutes), DateTimeOffset.UtcNow, Brushes.SeaGreen, Brushes.Goldenrod, Brushes.Firebrick)
        {
            ProviderName = providerName
        };

    private static void RenderWidget(WidgetView view, string name)
    {
        var host = new Grid { Width = 190 };
        host.Children.Add(view);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        host.Measure(new Size(190, double.PositiveInfinity));
        var height = (int)Math.Ceiling(host.DesiredSize.Height);
        Render(host, 190, height, name);
        Equal(190d, host.ActualWidth);
        Equal(190d, view.ActualWidth);
    }

    private static void ApplyPalette(WidgetTheme theme)
    {
        foreach (var pair in new ThemeCatalog().Create(theme, false))
            Application.Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
    }

    private static UsageWindow Window(double remaining, int minutes)
    {
        var label = "주간";
        if (minutes == 300)
            label = "5시간";
        return new()
        {
            Id = minutes.ToString(), Label = label, WindowMinutes = minutes,
            UsedPercent = 100 - remaining, ResetsAt = DateTimeOffset.UtcNow.AddDays(2)
        };
    }

    private static T ByName<T>(DependencyObject root, string name) where T : DependencyObject =>
        Descendants<T>(root).Single(control => AutomationProperties.GetName(control) == name);

    private static void Click(DependencyObject root, string text)
    {
        Descendants<Button>(root).Single(button => button.Content is string content && content == text)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match)
            yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(parent, index)))
                yield return child;
        }
    }

    private static void Arrange(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static RenderTargetBitmap Render(FrameworkElement element, int width, int height, string name)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Arrange(element, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(OutputDirectory, name));
        encoder.Save(stream);
        return bitmap;
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, received {actual}.");
    }

    private static void True(bool value)
    {
        assertions++;
        if (!value)
            throw new InvalidOperationException("Assertion failed.");
    }
}
