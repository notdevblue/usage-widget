using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UsageMonitor.Core;
using UsageMonitor.Services;

namespace UsageMonitor.Presentation.Tests;

internal static class ProviderVisibilityTests
{
    private static readonly string OutputDirectory = Path.GetFullPath("artifacts/layout-smoke");
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        SettingsControlsPublishAndSynchronize();
        ProviderRowsAndSettingsIntegration();
        Console.WriteLine($"PASS {assertions} provider visibility presentation assertions");
    }

    private static void SettingsControlsPublishAndSynchronize()
    {
        var initial = new AppSettings { Theme = WidgetTheme.Light, Scale = 1.2 };
        var window = new SettingsWindow(initial);
        try
        {
            var root = (FrameworkElement)window.Content;
            Arrange(root, 390, 740);
            var codex = Named<CheckBox>(root, "Codex 표시");
            var claude = Named<CheckBox>(root, "Claude 표시");
            var grok = Named<CheckBox>(root, "Grok 표시");
            Check((string)codex.Content == "Codex 표시" && (string)claude.Content == "Claude 표시" && (string)grok.Content == "Grok 표시",
                "Service checkboxes must expose matching visible and automation labels.");
            Check(codex.IsChecked == true && claude.IsChecked == true && grok.IsChecked == true, "The settings window must initially show all services enabled.");
            var captured = initial;
            var changes = 0;
            window.SettingsChanged += settings => { captured = settings; changes++; };
            codex.IsChecked = false;
            Check(changes == 1 && !captured.ShowCodex && captured.ShowClaude && captured.ShowGrok, "A Codex toggle must publish immediately without changing the other services.");
            claude.IsChecked = false;
            Check(changes == 2 && !captured.ShowCodex && !captured.ShowClaude && captured.ShowGrok, "A Claude toggle must preserve the other choices.");
            grok.IsChecked = false;
            Check(changes == 3 && !captured.ShowCodex && !captured.ShowClaude && !captured.ShowGrok, "Users must be able to turn all services off.");
            codex.IsChecked = true;
            Check(changes == 4 && captured.ShowCodex && !captured.ShowClaude && !captured.ShowGrok, "Re-enabling one service must not re-enable the others.");

            window.UpdateSettings(initial with { ShowCodex = false, ShowClaude = true, ShowGrok = false });
            Check(changes == 4 && codex.IsChecked == false && claude.IsChecked == true && grok.IsChecked == false,
                "External settings updates must synchronize all visibility controls without publishing events.");
            Named<ComboBox>(root, "화면 테마").SelectedIndex = (int)WidgetTheme.Dark;
            Check(changes == 5 && !captured.ShowCodex && captured.ShowClaude && !captured.ShowGrok, "Theme selection must preserve visibility preferences.");
            Click(root, "외형 기본값으로");
            Check(changes == 6 && captured.Theme == WidgetTheme.System && captured.Scale == 1
                && !captured.ShowCodex && captured.ShowClaude && !captured.ShowGrok, "Resetting appearance must preserve service visibility preferences.");

            window.UpdateSettings(captured with { ShowCodex = false, ShowClaude = false, ShowGrok = false });
            window.UpdateConnection("codex-hidden@example.invalid", "ChatGPT로 로그인", true, false);
            window.UpdateClaudeConnection("claude-hidden@example.invalid", "Claude에서 로그인", true, false);
            window.UpdateGrokConnection("grok-hidden@example.invalid", "Grok에서 로그인", true, false);
            foreach (var provider in new[] { "Codex", "Claude", "Grok" })
            {
                Check(Descendants<TextBlock>(root).Any(text => text.Text == provider + " 계정"), "Hidden services must retain their account settings section.");
                Check(Descendants<Button>(root).Single(button => Equals(button.Content, provider + " 실행 파일 선택")).IsEnabled,
                    "Hidden services must retain executable selection controls.");
            }

            Render(root, 390, 740, "provider-visibility-settings.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static void ProviderRowsAndSettingsIntegration()
    {
        using var fixture = new Fixture();
        fixture.SetSnapshot("codex", Snapshot("codex") with { IsStale = true, IsCached = true, StatusDetail = "hidden-codex-warning" });
        fixture.SetSnapshot("claude", Snapshot("claude"));
        fixture.SetSnapshot("grok", Snapshot("grok"));
        fixture.Apply(fixture.Settings with { ShowCodex = false, ShowClaude = true, ShowGrok = false });
        Check(fixture.View.Providers.Select(provider => provider.ProviderId).SequenceEqual(new[] { "claude" }), "Provider rows must include only the selected service.");
        Check(fixture.View.Providers.Single().Metrics.Count == 1, "The widget must retain every selected provider metric.");
        Check(!fixture.View.Stale && !fixture.View.NoProvidersVisible, "A hidden stale service must not mark the visible widget stale.");
        Check(fixture.View.HoverText.Contains("Claude", StringComparison.Ordinal) && !fixture.View.HoverText.Contains("Codex", StringComparison.Ordinal)
            && !fixture.View.HoverText.Contains("Grok", StringComparison.Ordinal) && !fixture.View.HoverText.Contains("hidden-codex-warning", StringComparison.Ordinal),
            "Root hover details must omit hidden service identities, quotas, and failure details.");
        Check(fixture.TrayText.Contains("Claude", StringComparison.Ordinal) && !fixture.TrayText.Contains("Codex", StringComparison.Ordinal) && !fixture.TrayText.Contains("Grok", StringComparison.Ordinal),
            "Tray text must summarize only visible services.");
        RenderWidget(fixture.Content, "provider-visibility-claude-only.png");
        var bar = Descendants<ProgressBar>(fixture.Content).Single();
        var provider = fixture.View.Providers.Single();
        Check(ToolTipText(bar) == provider.HoverText && ToolTipText(bar).Contains("Claude", StringComparison.Ordinal)
            && !ToolTipText(bar).Contains("hidden-codex-warning", StringComparison.Ordinal),
            "Hovering a usage bar must inherit its own provider diagnostics without hidden-provider feedback.");
        provider.Detail = "새 연결 오류";
        fixture.Invoke("UpdateView");
        FlushBindings();
        Check(ToolTipText(bar).Contains("새 연결 오류", StringComparison.Ordinal),
            "Updated provider feedback must reach an existing usage bar's tooltip immediately.");

        fixture.Invoke("OpenSettings");
        var settingsWindow = GetField<Dictionary<string, SettingsWindow>>(fixture.Window, "_dialogs")["settings"];
        var settingsRoot = (FrameworkElement)settingsWindow.Content;
        Named<CheckBox>(settingsRoot, "Claude 표시").IsChecked = false;
        Check(!fixture.Settings.ShowCodex && !fixture.Settings.ShowClaude && !fixture.Settings.ShowGrok && fixture.View.NoProvidersVisible,
            "Settings events must update the actual MainWindow visibility immediately.");
        Check(fixture.View.Providers.Count == 0,
            "Turning all services off must remove provider usage rows.");
        FlushBindings();
        Arrange(fixture.Content, 190, 105);
        var empty = Descendants<TextBlock>(fixture.Content).Single(text => text.Text == "표시할 서비스 없음");
        Check(empty.Visibility == Visibility.Visible && empty.TextWrapping == TextWrapping.NoWrap,
            "All-hidden widgets must show the exact single-line empty message.");
        Check(fixture.TrayText == "표시할 서비스 없음" && !fixture.View.Stale && !fixture.View.HoverText.Contains("hidden-codex-warning", StringComparison.Ordinal),
            "The all-hidden tray and hover state must not leak stale hidden provider data.");
        RenderWidget(fixture.Content, "provider-visibility-all-hidden.png");

        fixture.View.ApplyResetNotice(new()
        {
            Text = "리셋 티켓 지급 예고됨", ToolTip = "Tibo X 게시물", SourceUrl = "https://x.com/thsottiaux/status/123"
        }, DateTimeOffset.UtcNow);
        fixture.Invoke("RenderSnapshots");
        Check(fixture.View.ResetNoticeVisible && fixture.View.NoProvidersVisible, "Reset news must remain independent of Codex usage visibility.");
        Check(Descendants<TextBlock>(settingsRoot).Any(text => text.Text == "Codex 계정") && Descendants<TextBlock>(settingsRoot).Any(text => text.Text == "Grok 계정"),
            "Account management sections must remain available when all usage rows are hidden.");
        Named<CheckBox>(settingsRoot, "Grok 표시").IsChecked = true;
        Check(fixture.Settings.ShowGrok && !fixture.View.NoProvidersVisible && fixture.View.Providers.Single().ProviderId == "grok",
            "A hidden service must restore immediately through the same live settings dialog.");
        Check(fixture.View.Providers.Single().Metrics.Count == 1, "Re-enabling a provider must restore its usage bars immediately.");
        fixture.Invoke("ResetAppearance");
        Check(!fixture.Settings.ShowCodex && !fixture.Settings.ShowClaude && fixture.Settings.ShowGrok,
            "MainWindow appearance reset must preserve all visibility flags.");
    }



    private static string ToolTipText(FrameworkElement element)
    {
        var current = element;
        while (true)
        {
            if (current.ToolTip is string text && text.Length > 0)
                return text;
            if (VisualTreeHelper.GetParent(current) is FrameworkElement parent)
                current = parent;
            else
                return "";
        }
    }

    private static UsageSnapshot Snapshot(string providerId) => new()
    {
        ProviderId = providerId, ProviderName = ProviderName(providerId),
        Account = new() { Kind = AccountKind.ChatGpt, Email = providerId + "@example.invalid", Plan = "pro" },
        Windows = [new() { Id = "weekly", Label = "주간", UsedPercent = 36, WindowMinutes = 10080, ResetsAt = DateTimeOffset.UtcNow.AddDays(2) }],
        ObservedAt = DateTimeOffset.UtcNow
    };

    private static string ProviderName(string providerId) => providerId switch
    {
        "claude" => "Claude",
        "grok" => "Grok",
        _ => "Codex"
    };

    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject => Descendants<T>(root).Single(control => AutomationProperties.GetName(control) == name);

    private static void Click(DependencyObject root, string content) => Descendants<Button>(root).Single(button => Equals(button.Content, content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
            yield return child;
    }

    private static void Arrange(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static void FlushBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void RenderWidget(WidgetView view, string name)
    {
        FlushBindings();
        view.Measure(new Size(190, double.PositiveInfinity));
        Render(view, 190, (int)Math.Ceiling(view.DesiredSize.Height), name);
    }

    private static void Render(FrameworkElement element, int width, int height, string name)
    {
        FlushBindings();
        Arrange(element, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(OutputDirectory);
        using var stream = File.Create(Path.Combine(OutputDirectory, name));
        encoder.Save(stream);
    }

    private static object Invoke(object target, string name, params object[] arguments)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing test fixture method: {name}");
        return method.Invoke(target, arguments) ?? new();
    }

    private static T GetField<T>(object target, string name) => (T)(Field(target, name).GetValue(target)
        ?? throw new InvalidOperationException($"Missing test fixture field: {name}"));

    private static void SetField(object target, string name, object value) => Field(target, name).SetValue(target, value);

    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing test fixture field: {name}");

    private static T GetProperty<T>(object target, string name) => (T)(Property(target, name).GetValue(target)
        ?? throw new InvalidOperationException($"Missing test fixture property: {name}"));

    private static void SetProperty(object target, string name, object value) => Property(target, name).SetValue(target, value);

    private static PropertyInfo Property(object target, string name) => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing test fixture property: {name}");

    private static void PumpTask(Task task)
    {
        if (!task.IsCompleted)
        {
            var watch = Stopwatch.StartNew();
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(5) };
            timer.Tick += (_, _) =>
            {
                if (task.IsCompleted || watch.Elapsed >= TimeSpan.FromSeconds(3))
                    frame.Continue = false;
            };
            try
            {
                timer.Start();
                Dispatcher.PushFrame(frame);
            }
            finally
            {
                timer.Stop();
            }
        }

        Check(task.IsCompleted, "Preview fixture work must finish within three seconds.");
        task.GetAwaiter().GetResult();
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        internal MainWindow Window { get; } = new("weekly");
        internal WidgetView Content => GetField<WidgetView>(Window, "WidgetContent");
        internal WidgetViewModel View => GetField<WidgetViewModel>(Window, "_view");
        internal AppSettings Settings => GetField<AppSettings>(Window, "_settings");
        internal string TrayText => GetProperty<string>(GetField<object>(Window, "_tray"), "Text");

        internal Fixture()
        {
            Window.Show();
            FlushBindings();
            foreach (var runtime in GetField<Array>(Window, "_providers").Cast<object>())
                SetProperty(runtime, "NextRefreshAt", DateTimeOffset.MaxValue);
            GetField<DispatcherTimer>(Window, "_refreshTimer").Stop();
            GetField<DispatcherTimer>(Window, "_clockTimer").Stop();
        }

        internal void SetSnapshot(string providerId, UsageSnapshot snapshot)
        {
            var runtime = GetField<Array>(Window, "_providers").Cast<object>().Single(item => GetProperty<string>(item, "Id") == providerId);
            SetProperty(runtime, "Snapshot", snapshot);
            SetProperty(runtime, "HasSnapshot", true);
            var view = GetProperty<WidgetViewModel>(runtime, "View");
            view.Stale = snapshot.IsStale;
            view.Detail = snapshot.StatusDetail;
        }

        internal void Apply(AppSettings settings)
        {
            SetField(Window, "_settings", settings);
            Invoke("ApplyAppearance");
            Invoke("RenderSnapshots");
            FlushBindings();
        }

        internal object Invoke(string name, params object[] arguments) => ProviderVisibilityTests.Invoke(Window, name, arguments);

        public void Dispose()
        {
            try
            {
                PumpTask((Task)Invoke("StopAsync"));
            }
            finally
            {
                Window.Close();
            }
        }
    }
}
