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
using UsageMonitor.Services;

namespace UsageMonitor.Presentation.Tests;

internal static class StartupSettingsTests
{
    private const string StartupLabel = "Windows 로그인 시 자동 실행";
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        StartupControlsStayIndependentOfAppearance();
        PreviewStartupIsUnavailable();
        Console.WriteLine($"PASS {assertions} startup settings presentation assertions");
    }

    private static void StartupControlsStayIndependentOfAppearance()
    {
        var initial = new AppSettings { Theme = WidgetTheme.Light, Scale = 1.2, ShowGrok = false };
        var window = new SettingsWindow(initial);
        try
        {
            var root = (FrameworkElement)window.Content;
            Arrange(root);
            var option = Descendants<CheckBox>(root).Single(control => AutomationProperties.GetName(control) == StartupLabel);
            Check(Equals(option.Content, StartupLabel), "Startup must expose the same descriptive checkbox content and automation name.");
            var startupChanges = new List<bool>();
            var settingsChanges = 0;
            window.StartupChanged += startupChanges.Add;
            window.SettingsChanged += _ => settingsChanges++;
            window.UpdateStartup(false, true);
            Check(option.IsEnabled && option.IsChecked == false && startupChanges.Count == 0 && settingsChanges == 0,
                "Reading disabled OS startup state must synchronize the control without publishing changes.");
            option.IsChecked = true;
            Check(startupChanges.SequenceEqual(new[] { true }) && settingsChanges == 0,
                "Enabling startup must emit exactly one independent OS-state request.");
            option.IsChecked = false;
            Check(startupChanges.SequenceEqual(new[] { true, false }) && settingsChanges == 0,
                "Disabling startup must emit exactly one false request without changing app settings.");
            window.UpdateStartup(true, true);
            Check(option.IsChecked == true && option.IsEnabled && startupChanges.Count == 2,
                "External enabled OS state must update the control without requesting another registry change.");
            window.UpdateSettings(initial with { Theme = WidgetTheme.Dark });
            Check(option.IsChecked == true && option.IsEnabled && startupChanges.Count == 2 && settingsChanges == 0,
                "External application settings updates must preserve startup state without publishing either event.");
            Descendants<ComboBox>(root).Single(control => AutomationProperties.GetName(control) == "화면 테마").SelectedIndex = (int)WidgetTheme.Light;
            Check(settingsChanges == 1 && startupChanges.Count == 2 && option.IsChecked == true,
                "Theme changes must not reset or re-register startup state.");
            Descendants<Button>(root).Single(button => Equals(button.Content, "외형 기본값으로")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(settingsChanges == 2 && startupChanges.Count == 2 && option.IsChecked == true && option.IsEnabled,
                "Appearance reset must preserve the OS-controlled startup checkbox.");
            Render(root);
            window.UpdateStartup(true, false);
            Check(option.IsChecked == true && !option.IsEnabled && startupChanges.Count == 2,
                "Unavailable startup controls must preserve the supplied state and remain disabled without emitting events.");
            window.UpdateStartup(false, false);
            Check(option.IsChecked == false && !option.IsEnabled && startupChanges.Count == 2 && settingsChanges == 2,
                "Disabled preview-style synchronization must not mutate OS or app settings.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void PreviewStartupIsUnavailable()
    {
        var window = new MainWindow("weekly");
        try
        {
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Invoke(window, "OpenSettings");
            var dialogs = GetField<Dictionary<string, SettingsWindow>>(window, "_dialogs");
            var option = Descendants<CheckBox>((DependencyObject)dialogs["settings"].Content)
                .Single(control => AutomationProperties.GetName(control) == StartupLabel);
            Check(!option.IsEnabled && option.IsChecked == false,
                "Preview windows must expose startup as unavailable without reading or writing a live registration.");
        }
        finally
        {
            try
            {
                PumpTask((Task)Invoke(window, "StopAsync"));
            }
            finally
            {
                window.Close();
            }
        }
    }

    private static void Arrange(FrameworkElement element)
    {
        element.Measure(new Size(390, 740));
        element.Arrange(new Rect(0, 0, 390, 740));
        element.UpdateLayout();
    }

    private static void Render(FrameworkElement element)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Arrange(element);
        var bitmap = new RenderTargetBitmap(390, 740, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath("artifacts/layout-smoke");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, "startup-settings.png"));
        encoder.Save(stream);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
            yield return child;
    }

    private static object Invoke(MainWindow target, string name)
    {
        var method = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing startup fixture method: {name}");
        return method.Invoke(target, []) ?? new();
    }

    private static T GetField<T>(MainWindow target, string name) => (T)(typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target)
        ?? throw new InvalidOperationException($"Missing startup fixture field: {name}"));

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

        Check(task.IsCompleted, "Preview startup fixture cleanup must complete within three seconds.");
        task.GetAwaiter().GetResult();
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
