using System.Text.Json;
using UsageMonitor.Services;

internal static class AppearanceTests
{
    public static void Run(string directory, Action<bool, string> assert)
    {
        TestLayout(assert);
        TestSettingsLayout(directory, assert);
        TestProfileStore(directory, assert);
    }

    private static void TestLayout(Action<bool, string> assert)
    {
        var layout = SampleLayout();
        var normalized = LayoutProfile.Normalize(layout);
        assert(normalized.Enabled && normalized.Width == 400 && normalized.Height == 360 && !normalized.AutoHeight, "Valid canvas settings must survive normalization.");
        assert(normalized.Blocks.SequenceEqual(layout.Blocks), "Valid block geometry, sources and visibility options must survive normalization.");

        normalized = LayoutProfile.Normalize(new()
        {
            Enabled = true,
            Width = 1,
            Height = 5000,
            Blocks =
            [
                new LayoutBlock { Id = "", Width = 1000, Height = 1, X = double.PositiveInfinity, Y = 9000, Kind = (LayoutBlockKind)99, Visualization = (UsageVisualization)99, Source = (UsageWindowSource)99 },
                new LayoutBlock { Id = "duplicate", Width = double.NaN, Height = double.NegativeInfinity, X = double.NaN, Y = -20 },
                new LayoutBlock { Id = " duplicate ", Width = -1, Height = -1, X = 9000, Y = 9000 }
            ]
        });
        assert(normalized.Width == 200 && normalized.Height == 1000, "Canvas dimensions must respect their bounds.");
        assert(normalized.Blocks[0].Width == 200 && normalized.Blocks[0].Height == 32 && normalized.Blocks[0].X == 0 && normalized.Blocks[0].Y == 968, "Oversized blocks and origins must fit inside the canvas.");
        assert(normalized.Blocks[0].Kind == LayoutBlockKind.Usage && normalized.Blocks[0].Visualization == UsageVisualization.Bar && normalized.Blocks[0].Source == UsageWindowSource.First, "Invalid block enums must use defaults.");
        assert(normalized.Blocks[1].Width == 200 && normalized.Blocks[1].Height == 112 && normalized.Blocks[1].X == 0 && normalized.Blocks[1].Y == 0, "Nonfinite block dimensions and origins must recover finite defaults.");
        assert(normalized.Blocks[2].Width == 60 && normalized.Blocks[2].Height == 32 && normalized.Blocks[2].X == 140 && normalized.Blocks[2].Y == 968, "Undersized blocks must recover minimum dimensions.");
        assert(normalized.Blocks.All(block => !string.IsNullOrWhiteSpace(block.Id)) && normalized.Blocks.Select(block => block.Id).Distinct().Count() == 3, "Empty and duplicate block identifiers must recover unique IDs.");
        assert(LayoutProfile.Normalize(normalized).Blocks.SequenceEqual(normalized.Blocks), "Repeated normalization must preserve recovered block IDs and values.");

        normalized = LayoutProfile.Normalize(new()
        {
            Enabled = true,
            Width = double.NaN,
            Height = double.PositiveInfinity,
            Blocks = Enumerable.Range(0, 40).Select(index => new LayoutBlock { Id = "repeated", X = index, Y = index }).ToArray()
        });
        assert(normalized.Width == 260 && normalized.Height == 250, "Nonfinite canvas dimensions must recover defaults.");
        assert(normalized.Blocks.Count == 32 && normalized.Blocks.Select(block => block.Id).Distinct().Count() == 32, "Layouts must limit block count while preserving unique identifiers.");
        assert(normalized.Blocks.All(block => block.X >= 0 && block.Y >= 0 && block.X + block.Width <= normalized.Width && block.Y + block.Height <= normalized.Height), "Every recovered block must remain inside its canvas.");
        assert(!LayoutProfile.Normalize(new() { Enabled = true }).Enabled, "An enabled empty layout must disable safely.");
        normalized = LayoutProfile.Normalize(new() { Width = 10000, Height = 1, Blocks = [new LayoutBlock()] });
        assert(normalized.Width == 640 && normalized.Height == 80 && normalized.Blocks[0].Height == 80, "Extreme canvas bounds must also constrain block dimensions.");

        var appearance = AppearanceProfile.Normalize(new()
        {
            Version = 99,
            Theme = (WidgetTheme)99,
            Scale = double.NaN,
            Opacity = double.PositiveInfinity,
            CustomColors = new ThemeColors { Background = "bad", Foreground = "bad", Accent = "bad" },
            Layout = new() { Enabled = true }
        });
        assert(appearance.Version == 1 && appearance.Theme == WidgetTheme.System && appearance.Scale == 1 && appearance.Opacity == 1, "Public appearance normalization must produce supported, finite settings.");
        assert(appearance.CustomColors == new ThemeColors() && !appearance.Layout.Enabled, "Public appearance normalization must normalize colors and layout.");
    }

    private static void TestSettingsLayout(string directory, Action<bool, string> assert)
    {
        var store = new SettingsStore(Path.Combine(directory, "layout-settings"));
        var settings = new AppSettings
        {
            AlwaysOnTop = true,
            Theme = WidgetTheme.Midnight,
            CodexPath = "C:\\Tools\\codex.exe",
            Layout = SampleLayout()
        };
        assert(store.TrySave(settings), "Settings must persist layout alongside machine preferences.");
        var restored = store.Load();
        assert(restored.AlwaysOnTop && restored.Theme == WidgetTheme.Midnight && restored.CodexPath == settings.CodexPath, "Layout persistence must preserve existing machine preferences.");
        assert(restored.Layout.Enabled && restored.Layout.Width == 400 && restored.Layout.Height == 360 && !restored.Layout.AutoHeight && restored.Layout.Blocks.SequenceEqual(settings.Layout.Blocks), "Settings must round trip the full layout.");
        File.WriteAllText(store.FilePath, "{\"alwaysOnTop\":true,\"layout\":{\"enabled\":true,\"width\":\"NaN\",\"height\":80,\"blocks\":[null,{\"id\":null,\"kind\":\"bad\",\"visualization\":99,\"source\":\"bad\",\"width\":9000,\"height\":9000,\"x\":9000,\"y\":9000}]}}");
        restored = store.Load();
        assert(restored.AlwaysOnTop && restored.Layout.Enabled && restored.Layout.Width == 260 && restored.Layout.Height == 80 && restored.Layout.Blocks.Count == 1, "Malformed layout fields must normalize without discarding valid machine preferences.");
        assert(restored.Layout.Blocks[0].Width == 260 && restored.Layout.Blocks[0].Height == 80 && restored.Layout.Blocks[0].X == 0 && restored.Layout.Blocks[0].Y == 0 && restored.Layout.Blocks[0].Id.Length > 0, "Null JSON blocks and invalid geometry must recover safely.");
        File.WriteAllText(store.FilePath, "{\"layout\":null}");
        assert(store.Load().Layout == new LayoutProfile(), "Null layouts in settings must recover defaults.");
    }

    private static void TestProfileStore(string directory, Action<bool, string> assert)
    {
        var store = new AppearanceProfileStore();
        var path = Path.Combine(directory, "profiles", "shared-theme.json");
        var expected = new AppearanceProfile
        {
            Theme = WidgetTheme.Lavender,
            CustomColors = new ThemeColors { Background = "#112233", Foreground = "#EEEEDD", Accent = "#AABBCC" },
            Scale = 1.1,
            Opacity = 0.88,
            Layout = SampleLayout()
        };
        assert(store.TryExport(path, expected), "Appearance export must create the requested profile.");
        assert(store.TryImport(path, out var imported), "Exported appearance must be importable.");
        assert((imported with { Layout = expected.Layout }) == expected && imported.Layout.Blocks.SequenceEqual(expected.Layout.Blocks), "Appearance profiles must round trip colors, scale, opacity and blocks.");
        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();
            assert(names.SetEquals(["version", "theme", "customColors", "scale", "opacity", "layout"]), "Exports must contain only appearance fields.");
        }
        assert(!ContainsMachineFields(File.ReadAllText(path)), "Exported profiles must omit executable paths, account data and machine preferences.");
        assert(!Directory.EnumerateFiles(Path.GetDirectoryName(path) ?? "", "*.tmp").Any(), "Successful export must clean temporary files.");

        var originalJson = File.ReadAllText(path);
        var huge = expected with { Layout = expected.Layout with { Blocks = [new LayoutBlock { Id = new string('x', 256 * 1024) }] } };
        assert(!store.TryExport(path, huge) && File.ReadAllText(path) == originalJson, "Oversized exports must preserve an existing destination.");

        File.WriteAllText(path, "{\"version\":1,\"theme\":\"Mint\",\"codexPath\":\"C:\\\\untrusted.exe\",\"alwaysOnTop\":true,\"email\":\"private@example.com\",\"accessToken\":\"secret\",\"left\":9000,\"top\":9000}");
        assert(store.TryImport(path, out imported) && imported.Theme == WidgetTheme.Mint, "Unknown machine fields must not prevent importing appearance values.");
        assert(store.TryExport(path, imported) && !ContainsMachineFields(File.ReadAllText(path)), "Imported machine fields must never survive a profile export.");

        File.WriteAllText(path, "{\"version\":1,\"theme\":\"invalid\",\"scale\":\"NaN\",\"opacity\":100,\"customColors\":null,\"layout\":{\"enabled\":true,\"blocks\":null}}");
        assert(store.TryImport(path, out imported), "Sensible malformed appearance values must sanitize on import.");
        assert(imported.Theme == WidgetTheme.System && imported.Scale == 1 && imported.Opacity == 1 && imported.CustomColors == new ThemeColors() && !imported.Layout.Enabled, "Imported appearance values must use the same normalization as saved settings.");

        foreach (var invalid in new[] { "{ broken", "null", "[]", "", "{\"version\":0}", "{\"version\":2}", "{\"version\":\"invalid\"}" })
        {
            File.WriteAllText(path, invalid);
            assert(!store.TryImport(path, out imported) && imported == new AppearanceProfile(), "Malformed and unsupported profiles must fail with a default output.");
        }

        File.WriteAllText(path, "{\"version\":1}".PadRight(256 * 1024));
        assert(store.TryImport(path, out _), "Profiles exactly at the size limit must remain importable.");
        File.AppendAllText(path, " ");
        assert(!store.TryImport(path, out imported) && imported == new AppearanceProfile(), "Profiles above 256 KiB must be rejected.");
        assert(!store.TryImport(Path.Combine(directory, "absent-profile.json"), out imported) && imported == new AppearanceProfile(), "Missing profiles must fail with a default output.");
        assert(!store.TryImport("\0", out _) && !store.TryExport("\0", expected), "Invalid profile paths must report failure.");
        var directoryDestination = Path.Combine(directory, "profile-directory");
        Directory.CreateDirectory(directoryDestination);
        assert(!store.TryExport(directoryDestination, expected), "Exporting over a directory must report failure.");
        assert(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Failed export must clean temporary files.");
    }

    private static bool ContainsMachineFields(string json) => new[] { "codexPath", "alwaysOnTop", "refreshSeconds", "hasPosition", "\"left\"", "\"top\"", "email", "accessToken", "refreshToken" }
        .Any(field => json.Contains(field, StringComparison.OrdinalIgnoreCase));

    private static LayoutProfile SampleLayout() => new()
    {
        Enabled = true,
        Width = 400,
        Height = 360,
        AutoHeight = false,
        Blocks =
        [
            new LayoutBlock { Id = "usage-one", Kind = LayoutBlockKind.Usage, Visualization = UsageVisualization.Ring, Source = UsageWindowSource.FiveHour, X = 10, Y = 20, Width = 180, Height = 140, ShowLabel = false, ShowReset = false },
            new LayoutBlock { Id = "usage-two", Kind = LayoutBlockKind.Usage, Visualization = UsageVisualization.Number, Source = UsageWindowSource.Second, X = 210, Y = 20, Width = 180, Height = 140 },
            new LayoutBlock { Id = "reset", Kind = LayoutBlockKind.Reset, Source = UsageWindowSource.Weekly, X = 10, Y = 175, Width = 380, Height = 40 },
            new LayoutBlock { Id = "account", Kind = LayoutBlockKind.Account, X = 10, Y = 230, Width = 380, Height = 40 },
            new LayoutBlock { Id = "status", Kind = LayoutBlockKind.Status, X = 10, Y = 285, Width = 380, Height = 40 }
        ]
    };
}
