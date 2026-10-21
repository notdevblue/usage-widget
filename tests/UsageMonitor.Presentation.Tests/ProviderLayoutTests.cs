using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using UsageMonitor.Core;
using UsageMonitor.Layout;
using UsageMonitor.Services;

namespace UsageMonitor.Presentation.Tests;

internal static class ProviderLayoutTests
{
    private const string ScopedWindowId = "claude:weekly_scoped:Sonnet:";

    internal static void Run()
    {
        Persistence();
        ScopedWindows();
        ScopedAccountAndStatus();
        MissingProvider();
        EditorSelection();
        SpecificWindow();
        SpecificWindowEditor();
        ProviderPresets();
        Console.WriteLine("PASS provider layout persistence, isolation, editor and presets");
    }

    private static void Persistence()
    {
        var legacy = JsonSerializer.Deserialize<LayoutBlock>("{\"Id\":\"legacy\",\"Source\":3}") ?? new();
        Check(legacy.ProviderId == "codex" && legacy.WindowId.Length == 0, "Legacy layout blocks must keep their Codex provider and source selector.");
        var profile = LayoutProfile.Normalize(new()
        {
            Enabled = true,
            Blocks = [legacy, new() { ProviderId = "CLAUDE", WindowId = " " + ScopedWindowId + " " }, new() { ProviderId = "unknown", WindowId = new string('x', 300) }]
        });
        var restored = JsonSerializer.Deserialize<LayoutProfile>(JsonSerializer.Serialize(profile)) ?? new();
        Check(restored.Blocks.Select(block => block.ProviderId).SequenceEqual(new[] { "codex", "claude", "codex" }), "Provider selections must normalize and survive serialization.");
        Check(restored.Blocks[0].Source == UsageWindowSource.Weekly, "Provider migration must preserve the selected limit.");
        Check(restored.Blocks[1].WindowId == ScopedWindowId && restored.Blocks[2].WindowId.Length == 256, "Specific window IDs must normalize, remain bounded and survive serialization.");
    }

    private static void ScopedWindows()
    {
        var context = Context();
        var sources = new[] { UsageWindowSource.First, UsageWindowSource.Second, UsageWindowSource.FiveHour, UsageWindowSource.Weekly };
        var blocks = new List<LayoutBlock>();
        foreach (var providerId in new[] { "codex", "claude" })
        foreach (var source in sources)
            blocks.Add(new() { ProviderId = providerId, Source = source, Y = blocks.Count * 80, Height = 72 });
        blocks.Add(new() { ProviderId = "claude", Kind = LayoutBlockKind.Reset, Source = UsageWindowSource.Weekly, Y = 640, Height = 60 });
        var canvas = new UsageLayoutCanvas();
        canvas.Render(new() { Enabled = true, Height = 720, Blocks = blocks }, context);
        Check(canvas.Children.Count == 9, "All provider-qualified limit sources must render.");
        var expected = new[] { "40%", "70%", "40%", "70%", "20%", "80%", "20%", "80%" };
        for (var index = 0; index < expected.Length; index++)
        {
            Check(Texts(canvas.Children[index]).Contains(expected[index]), "The selected limit must belong to its block provider.");
            var providerName = index < 4 ? "Codex" : "Claude";
            Check(Texts(canvas.Children[index]).Any(text => text.StartsWith(providerName + " · ", StringComparison.Ordinal)), "Limit labels must identify their provider.");
        }

        var reset = context.ResolveProvider("claude").Windows[1].ResetsAt.ToLocalTime().ToString("yyyy.MM.dd (ddd) HH:mm:ss '초기화'");
        Check(Descendants<TextBlock>(canvas.Children[8]).Any(text => text.ToolTip is string detail && detail == reset), "Reset blocks must use their provider's timestamp.");
    }

    private static void ScopedAccountAndStatus()
    {
        var context = Context();
        var canvas = new UsageLayoutCanvas();
        canvas.Render(new()
        {
            Enabled = true, Height = 220,
            Blocks =
            [
                new() { Kind = LayoutBlockKind.Account, Height = 48 },
                new() { ProviderId = "claude", Kind = LayoutBlockKind.Account, Y = 56, Height = 48 },
                new() { Kind = LayoutBlockKind.Status, Y = 112, Height = 38 },
                new() { ProviderId = "claude", Kind = LayoutBlockKind.Status, Y = 158, Height = 38 }
            ]
        }, context);
        Check(Texts(canvas.Children[0]).SequenceEqual(new[] { "codex@example.invalid", "Plus" }), "Codex account blocks must prefer the explicit Codex context.");
        Check(Texts(canvas.Children[1]).SequenceEqual(new[] { "claude@example.invalid", "Max" }), "Claude account blocks must use the Claude account and plan.");
        Check(Texts(canvas.Children[2]).Contains("Codex 동기화"), "Codex status blocks must stay scoped.");
        Check(Texts(canvas.Children[3]).Contains("Claude 연결 지연"), "Claude status blocks must stay scoped.");
        Check(Descendants<Ellipse>(canvas.Children[2]).Single().Fill == Brushes.SeaGreen, "Codex status must retain its own status brush.");
        Check(Descendants<Ellipse>(canvas.Children[3]).Single().Fill == Brushes.Goldenrod, "Claude status must retain its own status brush.");
    }

    private static void MissingProvider()
    {
        var canvas = new UsageLayoutCanvas();
        canvas.Render(new()
        {
            Enabled = true, Height = 250,
            Blocks =
            [
                new() { Height = 72 },
                new() { ProviderId = "claude", Height = 72, Y = 80 },
                new() { ProviderId = "claude", Kind = LayoutBlockKind.Account, Height = 38, Y = 160 },
                new() { ProviderId = "claude", Kind = LayoutBlockKind.Status, Height = 38, Y = 206 }
            ]
        }, new() { Windows = [Window(95, 300)], Account = "legacy-codex@example.invalid", Status = "legacy Codex" });
        Check(canvas.Children.Count == 3, "A missing Claude quota must hide its block without hiding Codex.");
        Check(Texts(canvas.Children[0]).Contains("95%"), "Legacy fields must continue supplying Codex limits.");
        Check(Texts(canvas.Children[1]).SequenceEqual(new[] { "계정 정보 없음" }), "Missing Claude accounts must not inherit Codex account data.");
        Check(Texts(canvas.Children[2]).Contains("상태 정보 없음"), "Missing Claude status must not inherit Codex status data.");
    }

    private static void EditorSelection()
    {
        var editor = new LayoutEditorWindow(new()
        {
            Enabled = true, Height = 250,
            Blocks = [new() { Kind = LayoutBlockKind.Account, Height = 112 }]
        }, Context());
        var root = (FrameworkElement)editor.Content;
        Arrange(root);
        var preview = Descendants<UsageLayoutCanvas>(root).Single();
        var provider = ByName<ComboBox>(root, "제공자");
        Check(provider.Items.Count == 2 && provider.SelectedIndex == 0, "Every legacy block must start with a Codex provider selector.");
        provider.SelectedIndex = 1;
        Check(Texts(preview).Contains("claude@example.invalid"), "Changing an account block's provider must update its account.");
        ByName<ComboBox>(root, "블록").SelectedIndex = (int)LayoutBlockKind.Status;
        Check(provider.IsEnabled && Texts(preview).Contains("Claude 연결 지연"), "Status blocks must retain an enabled provider selector.");
        ByName<ComboBox>(root, "블록").SelectedIndex = (int)LayoutBlockKind.Usage;
        ByName<ComboBox>(root, "한도 선택").SelectedIndex = (int)UsageWindowSource.Weekly;
        Check(Texts(preview).Contains("80%"), "The editor must resolve Claude's weekly quota.");
        provider.SelectedIndex = 0;
        Check(Texts(preview).Contains("70%"), "Changing provider must resolve Codex's weekly quota.");
        Check(Descendants<ListBox>(root).Single().Items.Cast<ListBoxItem>().All(item => item.Content is string label && label.StartsWith("Codex · ", StringComparison.Ordinal)), "The block list must identify the selected provider.");
        editor.Close();
    }

    private static void ProviderPresets()
    {
        var editor = new LayoutEditorWindow(new(), Context());
        var root = (FrameworkElement)editor.Content;
        Arrange(root);
        var preview = Descendants<UsageLayoutCanvas>(root).Single();
        var preset = ByName<ComboBox>(root, "레이아웃 프리셋");
        for (var index = 1; index <= 4; index++)
        {
            preset.SelectedIndex = index;
            Check(preview.Children.Count == 4, "Each preset must include both available providers' limits.");
            Check(Texts(preview).Count(text => text.StartsWith("Codex · ", StringComparison.Ordinal)) == 2, "Preset Codex blocks must identify their provider.");
            Check(Texts(preview).Count(text => text.StartsWith("Claude · ", StringComparison.Ordinal)) == 2, "Preset Claude blocks must identify their provider.");
            foreach (FrameworkElement block in preview.Children)
                Check(Canvas.GetLeft(block) + block.Width <= preview.Width && Canvas.GetTop(block) + block.Height <= preview.Height, "Provider presets must fit inside the canvas.");
            Render(preview, (int)Math.Ceiling(preview.Width), (int)Math.Ceiling(preview.Height), $"provider-preset-{index}.png");
        }

        editor.Close();
        var claudeOnly = new LayoutEditorWindow(new(), new() { Providers = [Context().ResolveProvider("claude")] });
        Arrange((FrameworkElement)claudeOnly.Content);
        var claudePreview = Descendants<UsageLayoutCanvas>((FrameworkElement)claudeOnly.Content).Single();
        Check(claudePreview.Children.Count == 2 && !Texts(claudePreview).Any(text => text.StartsWith("Codex · ", StringComparison.Ordinal)), "Claude-only presets must not invent a Codex quota.");
        claudeOnly.Close();
    }

    private static void SpecificWindow()
    {
        var context = SpecificContext();
        var profile = new LayoutProfile
        {
            Enabled = true, Height = 200,
            Blocks =
            [
                new() { ProviderId = "claude", WindowId = ScopedWindowId, Height = 112 },
                new() { ProviderId = "claude", WindowId = ScopedWindowId, Kind = LayoutBlockKind.Reset, Y = 124, Height = 60 }
            ]
        };
        profile = JsonSerializer.Deserialize<LayoutProfile>(JsonSerializer.Serialize(profile)) ?? new();
        var canvas = new UsageLayoutCanvas();
        canvas.Render(profile, context);
        Check(canvas.Children.Count == 2 && Texts(canvas.Children[0]).Contains("12%"), "A specific third quota must render independently of its legacy source.");
        var claude = context.ResolveProvider("claude");
        var reordered = context with { Providers = [context.ResolveProvider("codex"), claude with { Windows = [claude.Windows[1], claude.Windows[2], claude.Windows[0]] }] };
        canvas.Render(profile, reordered);
        Check(canvas.Children.Count == 2 && Texts(canvas.Children[0]).Contains("12%"), "A persisted window ID must keep selecting the same quota after order changes.");
        var reset = claude.Windows[2].ResetsAt.ToLocalTime().ToString("yyyy.MM.dd (ddd) HH:mm:ss '초기화'");
        Check(Descendants<TextBlock>(canvas.Children[1]).Any(text => text.ToolTip is string detail && detail == reset), "Specific reset blocks must use the selected model quota.");
        canvas.Render(profile, context with
        {
            Providers = [context.ResolveProvider("codex"), claude with { Windows = claude.Windows.Take(2).ToArray() }]
        });
        Check(canvas.Children.Count == 0, "A missing specific quota must hide without falling back to a legacy source or another provider's identical ID.");
    }

    private static void SpecificWindowEditor()
    {
        var context = SpecificContext();
        var profile = new LayoutProfile
        {
            Enabled = true, Height = 250,
            Blocks = [new() { ProviderId = "claude", Height = 112 }]
        };
        var editor = new LayoutEditorWindow(profile, context);
        var root = (FrameworkElement)editor.Content;
        Arrange(root);
        var preview = Descendants<UsageLayoutCanvas>(root).Single();
        var source = ByName<ComboBox>(root, "한도 선택");
        Check(source.Items.Count == 7, "The source selector must include all three actual Claude quotas alongside the legacy choices.");
        source.SelectedItem = source.Items.Cast<ComboBoxItem>().Single(item => item.Tag is string id && id == ScopedWindowId);
        Check(Texts(preview).Contains("12%") && Texts(preview).Contains("Claude · Sonnet"), "The editor must make the model quota directly selectable by its stable ID.");
        Render(root, 1020, 760, "provider-specific-window-editor.png");
        ByName<ComboBox>(root, "제공자").SelectedIndex = 0;
        Check(source.SelectedIndex == (int)UsageWindowSource.First && Texts(preview).Contains("40%"), "Changing provider must clear the previous provider's specific quota selection.");
        Check(!source.Items.Cast<ComboBoxItem>().Any(item => item.Content is string label && label == "직접 선택 · Sonnet"), "The quota selector must contain only the selected provider's labeled windows.");
        editor.Close();
        var missing = new LayoutEditorWindow(profile with
        {
            Blocks = [profile.Blocks[0] with { WindowId = ScopedWindowId }]
        }, context with
        {
            Providers = [context.ResolveProvider("codex"), context.ResolveProvider("claude") with { Windows = context.ResolveProvider("claude").Windows.Take(2).ToArray() }]
        });
        var missingRoot = (FrameworkElement)missing.Content;
        Arrange(missingRoot);
        var missingSource = ByName<ComboBox>(missingRoot, "한도 선택");
        Check(missingSource.SelectedItem is ComboBoxItem { Tag: ScopedWindowId }, "Reopening a missing specific quota must preserve the saved selection.");
        Check(Descendants<UsageLayoutCanvas>(missingRoot).Single().Children.Count == 0, "The editor must not substitute another quota for a missing saved ID.");
        missingSource.SelectedIndex = (int)UsageWindowSource.Weekly;
        Check(Texts(Descendants<UsageLayoutCanvas>(missingRoot).Single()).Contains("80%"), "Selecting a legacy source must clear a missing specific quota ID.");
        missing.Close();
    }

    private static LayoutContext SpecificContext()
    {
        var context = Context();
        var codex = context.ResolveProvider("codex");
        var claude = context.ResolveProvider("claude");
        return context with
        {
            Providers =
            [
                codex with
                {
                    Windows = [codex.Windows[0] with { Id = "codex:primary" }, codex.Windows[1] with { Id = "codex:secondary" }, Window(96, 10080) with { Id = ScopedWindowId, Label = "Codex scope" }]
                },
                claude with
                {
                    Windows = [claude.Windows[0] with { Id = "claude:session::" }, claude.Windows[1] with { Id = "claude:weekly_all::" }, Window(12, 10080) with { Id = ScopedWindowId, Label = "Sonnet", ResetsAt = new DateTimeOffset(2030, 1, 10, 12, 0, 0, TimeSpan.Zero) }]
                }
            ]
        };
    }

    private static LayoutContext Context() => new()
    {
        Windows = [Window(99, 300)], Account = "legacy@example.invalid", Plan = "Legacy", Status = "legacy",
        Providers =
        [
            new()
            {
                ProviderId = "codex", ProviderName = "Codex", Windows = [Window(40, 300), Window(70, 10080)],
                Account = "codex@example.invalid", Plan = "Plus", Status = "Codex 동기화", StatusBrush = Brushes.SeaGreen
            },
            new()
            {
                ProviderId = "claude", ProviderName = "Claude", Windows = [Window(20, 300), Window(80, 10080)],
                Account = "claude@example.invalid", Plan = "Max", Status = "Claude 연결 지연", StatusBrush = Brushes.Goldenrod
            }
        ]
    };

    private static UsageWindow Window(double remaining, int minutes) => new()
    {
        Label = minutes == 300 ? "5시간" : "주간", UsedPercent = 100 - remaining, WindowMinutes = minutes,
        ResetsAt = new DateTimeOffset(2030, 1, minutes == 300 ? 2 : 8, 12, 0, 0, TimeSpan.Zero)
    };

    private static string[] Texts(DependencyObject root) => Descendants<TextBlock>(root).Select(text => text.Text).ToArray();

    private static T ByName<T>(DependencyObject root, string name) where T : DependencyObject => Descendants<T>(root).Single(value => AutomationProperties.GetName(value) == name);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
            yield return child;
    }

    private static void Arrange(FrameworkElement root)
    {
        root.Measure(new Size(1020, 760));
        root.Arrange(new Rect(0, 0, 1020, 760));
        root.UpdateLayout();
    }

    private static void Render(FrameworkElement root, int width, int height, string name)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new Border { Background = (Brush)Application.Current.FindResource("CardBrush") };
        background.Measure(new Size(width, height));
        background.Arrange(new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath("artifacts/layout-smoke");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name));
        encoder.Save(stream);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
