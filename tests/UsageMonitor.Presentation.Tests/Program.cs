using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;
using UsageMonitor.Core;
using UsageMonitor.Layout;
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
            MissingWindows();
            FixedAndInvalidGeometry();
            RenderVisualizations();
            IconButtons();
            EditorInteractions();
            EmptyAccountEditor();
            StandaloneSettings();
            CompactWidget();
            CodexResetNoticeTests.Run();
            CompactBarTests.Run();
            UsageColorTests.Run();
            WidgetDragInputTests.Run();
            ProviderLayoutTests.Run();
            ProviderRuntimeTests.Run();
            ProviderVisibilityTests.Run();
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

    private static void MissingWindows()
    {
        var canvas = new UsageLayoutCanvas();
        var profile = new LayoutProfile
        {
            Enabled = true, Width = 260, Height = 300,
            Blocks =
            [
                new() { Source = UsageWindowSource.Weekly, Height = 100 },
                new() { Source = UsageWindowSource.FiveHour, Y = 140, Height = 100 },
                new() { Kind = LayoutBlockKind.Reset, Source = UsageWindowSource.Second, Y = 250, Height = 32 }
            ]
        };
        canvas.Render(profile, Context(43));
        Equal(1, canvas.Children.Count);
        Equal(100d, canvas.Height);
        canvas.Render(profile, new());
        Equal(0, canvas.Children.Count);
        Equal(0d, canvas.Height);
        canvas.Render(profile, Context(43) with
        {
            Windows = [Window(43, 300), Window(62, 10080)]
        });
        Equal(3, canvas.Children.Count);
        Equal(282d, canvas.Height);
    }

    private static void FixedAndInvalidGeometry()
    {
        var canvas = new UsageLayoutCanvas();
        canvas.Render(new()
        {
            Enabled = true, Width = double.NaN, Height = double.PositiveInfinity, AutoHeight = false,
            Blocks = [new() { X = double.NaN, Y = -100, Width = 99999, Height = -10 }]
        }, Context(50));
        Equal(260d, canvas.Width);
        Equal(250d, canvas.Height);
        var content = (FrameworkElement)canvas.Children[0];
        Equal(0d, Canvas.GetLeft(content));
        Equal(0d, Canvas.GetTop(content));
        Equal(260d, content.Width);
        Equal(32d, content.Height);
        Render(canvas, 260, 250, "bounded-layout.png");
    }

    private static void RenderVisualizations()
    {
        var stack = new StackPanel { Width = 620, Background = Brushes.White };
        foreach (var remaining in new[] { 0d, 0.4, 37d, 100d })
        {
            var canvas = new UsageLayoutCanvas { Margin = new Thickness(12, 10, 12, 10) };
            var profile = new LayoutProfile
            {
                Enabled = true, Width = 596, Height = 150,
                Blocks =
                [
                    new() { Visualization = UsageVisualization.Bar, Width = 180, Height = 146 },
                    new() { Visualization = UsageVisualization.Ring, X = 200, Width = 180, Height = 146 },
                    new() { Visualization = UsageVisualization.Number, X = 400, Width = 180, Height = 146 }
                ]
            };
            canvas.Render(profile, Context(remaining));
            Equal(3, canvas.Children.Count);
            stack.Children.Add(canvas);
        }

        Render(stack, 620, 664, "visualizations.png");
        var compact = new UsageLayoutCanvas();
        compact.Render(new()
        {
            Enabled = true, Width = 200, Height = 80,
            Blocks =
            [
                new() { Width = 60, Height = 32 },
                new() { Visualization = UsageVisualization.Ring, X = 70, Width = 60, Height = 32 },
                new() { Visualization = UsageVisualization.Number, X = 140, Width = 60, Height = 32 }
            ]
        }, Context(100));
        Equal(3, compact.Children.Count);
        Render(compact, 200, 32, "compact-metrics.png");
    }

    private static void EditorInteractions()
    {
        var editor = new LayoutEditorWindow(new()
        {
            Enabled = true, Width = 260, Height = 300,
            Blocks = [new() { Width = 160, Height = 100 }]
        }, Context(64));
        var root = (FrameworkElement)editor.Content;
        Arrange(root, 1020, 650);
        var preview = Descendants<UsageLayoutCanvas>(root).Single();
        Equal(1, preview.Children.Count);
        Click(root, "+ 계정");
        Equal(2, preview.Children.Count);
        var list = Descendants<ListBox>(root).Single();
        Equal(2, list.Items.Count);
        list.SelectedIndex = 0;
        SetNumber(root, "X", "30");
        SetNumber(root, "Y", "40");
        SetNumber(root, "너비", "120");
        SetNumber(root, "높이", "90");
        Equal(30d, Canvas.GetLeft(preview.Children[0]));
        Equal(40d, Canvas.GetTop(preview.Children[0]));
        var activeResize = Descendants<Thumb>(root).Single(thumb => thumb.Cursor == Cursors.SizeNWSE && thumb.Visibility == Visibility.Visible);
        var drag = Descendants<Thumb>(VisualTreeHelper.GetParent(activeResize)).Single(thumb => thumb.Cursor == Cursors.SizeAll);
        drag.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        drag.RaiseEvent(new DragDeltaEventArgs(17, 23) { RoutedEvent = Thumb.DragDeltaEvent });
        Equal(47d, Canvas.GetLeft(preview.Children[0]));
        Equal(63d, Canvas.GetTop(preview.Children[0]));
        var resize = Descendants<Thumb>(root).First(thumb => thumb.Cursor == Cursors.SizeNWSE && thumb.Visibility == Visibility.Visible);
        resize.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        resize.RaiseEvent(new DragDeltaEventArgs(-500, -500) { RoutedEvent = Thumb.DragDeltaEvent });
        Equal(60d, ((FrameworkElement)preview.Children[0]).Width);
        Equal(32d, ((FrameworkElement)preview.Children[0]).Height);
        var source = ByName<ComboBox>(root, "한도 선택");
        source.SelectedIndex = (int)UsageWindowSource.FiveHour;
        Equal(1, preview.Children.Count);
        source.SelectedIndex = (int)UsageWindowSource.First;
        Equal(2, preview.Children.Count);
        Click(root, "선택 블록 삭제");
        Equal(1, preview.Children.Count);
        Equal(1, list.Items.Count);
        var preset = ByName<ComboBox>(root, "레이아웃 프리셋");
        preset.SelectedIndex = 3;
        Equal(1, preview.Children.Count);
        Equal(1, list.Items.Count);
        Equal(260d, preview.Width);
        Click(root, "+ 계정");
        SetNumber(root, "Y", "0");
        Equal(2, preview.Children.Count);
        Click(root, "뒤로");
        True(Descendants<TextBlock>(preview.Children[0]).Any(text => text.Text == "member@example.invalid"));
        Equal(0d, Canvas.GetTop(preview.Children[0]));
        Click(root, "앞으로");
        True(Descendants<TextBlock>(preview.Children[1]).Any(text => text.Text == "member@example.invalid"));
        Equal(0d, Canvas.GetTop(preview.Children[1]));
        Click(root, "선택 블록 삭제");
        Render(root, 1020, 650, "layout-editor.png");
        editor.Close();
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

    private static void EmptyAccountEditor()
    {
        var editor = new LayoutEditorWindow(new(), new());
        var root = (FrameworkElement)editor.Content;
        Arrange(root, 1020, 650);
        var preview = Descendants<UsageLayoutCanvas>(root).Single();
        Equal(0, preview.Children.Count);
        True(Descendants<TextBlock>(root).Any(text => text.Text.Contains("현재 데이터 없음", StringComparison.Ordinal)));
        Render(root, 1020, 650, "layout-editor-no-account.png");
        editor.Close();
    }

    private static void StandaloneSettings()
    {
        var initial = new AppSettings
        {
            CodexPath = @"C:\private\codex.exe", ClaudePath = @"C:\private\claude.exe", HasPosition = true, Left = 87, Top = 143,
            Layout = new() { Enabled = true, Blocks = [new() { Height = 100 }] }
        };
        var window = new SettingsWindow(initial, () => new());
        var captured = new AppSettings();
        var changes = 0;
        var logins = 0;
        var executableRequests = 0;
        var claudeLogins = 0;
        var claudeExecutableRequests = 0;
        var quits = 0;
        window.SettingsChanged += updated =>
        {
            captured = updated;
            changes++;
            ApplyPalette(updated.Theme, updated.CustomColors);
        };
        window.LoginRequested += () => logins++;
        window.ExecutableRequested += () => executableRequests++;
        window.ClaudeLoginRequested += () => claudeLogins++;
        window.ClaudeExecutableRequested += () => claudeExecutableRequests++;
        window.QuitRequested += () => quits++;
        var root = (FrameworkElement)window.Content;
        Arrange(root, 370, 720);
        Equal(8, ByName<ComboBox>(root, "화면 테마").Items.Count);
        Equal(2, ByName<ComboBox>(root, "5시간 한도 표시").Items.Count);
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
        True(captured.Layout.Enabled);
        Equal(initial.CodexPath, captured.CodexPath);
        Equal(initial.ClaudePath, captured.ClaudePath);
        Equal(initial.Left, captured.Left);
        Equal(initial.Top, captured.Top);
        ByName<ComboBox>(root, "화면 테마").SelectedIndex = (int)WidgetTheme.Dark;
        Equal(WidgetTheme.Dark, captured.Theme);
        Render(root, 370, 720, "settings-dark.png");
        ByName<ComboBox>(root, "화면 테마").SelectedIndex = (int)WidgetTheme.Lavender;
        Equal(WidgetTheme.Lavender, captured.Theme);
        Render(root, 370, 720, "settings-lavender.png");
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
        window.ShowFeedback("설정 창 안의 피드백");
        True(Descendants<TextBlock>(root).Any(text => text.Text == "설정 창 안의 피드백" && text.Visibility == Visibility.Visible));
        Click(root, "자동 배치로");
        True(!captured.Layout.Enabled);
        Equal(1, captured.Layout.Blocks.Count);
        Click(root, "외형 기본값으로");
        True(captured.AlwaysOnTop);
        Equal(FiveHourDisplay.Hidden, captured.FiveHourDisplay);
        Equal(initial.CodexPath, captured.CodexPath);
        Equal(initial.ClaudePath, captured.ClaudePath);
        Equal(WidgetTheme.System, captured.Theme);
        Equal(1d, captured.Scale);
        Equal(1d, captured.Opacity);
        Equal(0, captured.Layout.Blocks.Count);
        True(changes >= 8);
        var changesBeforeExternalUpdate = changes;
        window.UpdateSettings(captured with { AlwaysOnTop = false });
        Equal(changesBeforeExternalUpdate, changes);
        True(ByName<CheckBox>(root, "항상 위에 표시").IsChecked == false);
        ByName<ComboBox>(root, "화면 테마").SelectedIndex = (int)WidgetTheme.Dark;
        True(!captured.AlwaysOnTop);
        Click(root, "앱 종료");
        Equal(1, quits);
        window.Close();
        ApplyPalette(WidgetTheme.Light, new());

    }

    private static void CompactWidget()
    {
        ApplyPalette(WidgetTheme.Light, new());
        var model = new WidgetViewModel { Plan = "Pro" };
        var rootModel = new WidgetViewModel();
        rootModel.Providers.Add(model);
        var view = new WidgetView { DataContext = rootModel };
        model.Metrics.Add(new UsageMetric(Window(64, 10080), DateTimeOffset.UtcNow, Brushes.SeaGreen, Brushes.Goldenrod, Brushes.Firebrick));
        model.Update();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        view.Measure(new Size(190, double.PositiveInfinity));
        var weeklyHeight = view.DesiredSize.Height;
        True(weeklyHeight <= 45);
        Render(view, 190, (int)Math.Ceiling(weeklyHeight), "compact-weekly.png");
        Equal(0, Descendants<Button>(view).Count(button => button.Visibility == Visibility.Visible));
        True(Descendants<TextBlock>(view).Any(text => text.Text == "Codex ·"));
        Equal(1, Descendants<ProgressBar>(view).Count());
        model.Metrics.Insert(0, new UsageMetric(Window(72, 300), DateTimeOffset.UtcNow, Brushes.SeaGreen, Brushes.Goldenrod, Brushes.Firebrick));
        model.Update();
        view = new WidgetView { DataContext = rootModel };
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        view.Measure(new Size(190, double.PositiveInfinity));
        True(view.DesiredSize.Height > weeklyHeight);
        True(view.DesiredSize.Height <= 65);
        Render(view, 190, (int)Math.Ceiling(view.DesiredSize.Height), "compact-dual.png");
        Equal(2, Descendants<ProgressBar>(view).Count());
        Console.WriteLine($"Compact widget: 190 x {weeklyHeight:0} (weekly), 190 x {view.DesiredSize.Height:0} (dual)");
        var dualHeight = (int)Math.Ceiling(view.DesiredSize.Height);
        ApplyPalette(WidgetTheme.Dark, new());
        view = new WidgetView { DataContext = rootModel };
        Render(view, 190, dualHeight, "compact-dark.png");
        ApplyPalette(WidgetTheme.Light, new());
        model.Metrics.RemoveAt(0);
        model.Update();
        var claude = new WidgetViewModel { ProviderId = "claude", ProviderName = "Claude", Plan = "Max" };
        claude.Metrics.Add(new UsageMetric(Window(63, 300), DateTimeOffset.UtcNow, Brushes.SeaGreen, Brushes.Goldenrod, Brushes.Firebrick));
        claude.Metrics.Add(new UsageMetric(Window(90, 10080), DateTimeOffset.UtcNow, Brushes.SeaGreen, Brushes.Goldenrod, Brushes.Firebrick));
        claude.Update();
        rootModel.Providers.Add(claude);
        view = new WidgetView { DataContext = rootModel };
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        view.Measure(new Size(190, double.PositiveInfinity));
        True(view.DesiredSize.Height <= 80);
        Render(view, 190, (int)Math.Ceiling(view.DesiredSize.Height), "compact-codex-claude.png");
        Equal(3, Descendants<ProgressBar>(view).Count());
        Equal(0, Descendants<Button>(view).Count(button => button.Visibility == Visibility.Visible));
        True(Descendants<TextBlock>(view).Any(text => text.Text == "Codex ·"));
        True(Descendants<TextBlock>(view).Any(text => text.Text == "Claude ·"));
        Console.WriteLine($"Codex + Claude widget: 190 x {view.DesiredSize.Height:0}");
        claude.Metrics.Clear();
        claude.EmptyTitle = "연결 대기";
        claude.Stale = true;
        claude.Update();
        rootModel.Stale = true;
        view = new WidgetView { DataContext = rootModel };
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        view.Measure(new Size(190, double.PositiveInfinity));
        Render(view, 190, (int)Math.Ceiling(view.DesiredSize.Height), "compact-claude-unavailable.png");
        Equal(1, Descendants<ProgressBar>(view).Count());
        True(Descendants<TextBlock>(view).Any(text => text.Text == "연결 대기"));
    }

    private static void ApplyPalette(WidgetTheme theme, ThemeColors custom)
    {
        foreach (var pair in new ThemeCatalog().Create(theme, custom, false))
            Application.Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
    }

    private static LayoutContext Context(double remaining) => new()
    {
        Windows = [Window(remaining, 10080)], Account = "member@example.invalid", Plan = "PRO",
        Status = "방금 업데이트", StatusBrush = Brushes.SeaGreen
    };

    private static UsageWindow Window(double remaining, int minutes) => new()
    {
        Id = minutes.ToString(), Label = minutes == 300 ? "5시간" : "주간",
        WindowMinutes = minutes, UsedPercent = 100 - remaining,
        ResetsAt = DateTimeOffset.UtcNow.AddDays(2)
    };

    private static void SetNumber(FrameworkElement root, string name, string text)
    {
        var box = ByName<TextBox>(root, name);
        box.Text = text;
        box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, box, root)
        {
            RoutedEvent = Keyboard.LostKeyboardFocusEvent
        });
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
