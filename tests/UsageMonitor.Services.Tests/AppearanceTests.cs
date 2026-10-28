using System.Text.Json;
using UsageMonitor.Services;

internal static class AppearanceTests
{
    public static void Run(string directory, Action<bool, string> assert)
    {
        TestNormalization(assert);
        TestLegacySettings(directory, assert);
    }

    private static void TestNormalization(Action<bool, string> assert)
    {
        var appearance = AppearanceProfile.Normalize(new()
        {
            Theme = (WidgetTheme)99,
            Scale = double.NaN,
            Opacity = double.PositiveInfinity
        });
        assert(appearance == new AppearanceProfile(), "Unsupported themes and nonfinite dimensions must recover supported defaults.");
        assert(AppearanceProfile.Normalize(new() { Theme = WidgetTheme.Dark, Scale = 0.1, Opacity = 0.1 })
            == new AppearanceProfile { Theme = WidgetTheme.Dark, Scale = 0.8, Opacity = 0.6 }, "Appearance normalization must preserve the selected theme and clamp lower dimension bounds.");
        assert(AppearanceProfile.Normalize(new() { Theme = WidgetTheme.Light, Scale = 2, Opacity = 2 })
            == new AppearanceProfile { Theme = WidgetTheme.Light, Scale = 1.5, Opacity = 1 }, "Appearance normalization must preserve the selected theme and clamp upper dimension bounds.");
    }

    private static void TestLegacySettings(string directory, Action<bool, string> assert)
    {
        var store = new SettingsStore(Path.Combine(directory, "legacy-appearance"));
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath) ?? "");
        var expected = new AppSettings
        {
            AlwaysOnTop = true,
            FiveHourDisplay = FiveHourDisplay.Hidden,
            ShowResetCountdown = true,
            ShowCodex = false,
            ShowClaude = true,
            ShowGrok = false,
            Scale = 1.25,
            Opacity = 0.85,
            HasPosition = true,
            Left = -1100.5,
            Top = 45.25,
            RefreshSeconds = 120,
            CodexPath = "C:\\Tools\\codex.exe",
            ClaudePath = "C:\\Tools\\claude.exe",
            GrokPath = "C:\\Tools\\grok.exe"
        };
        var obsoleteThemes = new[]
        {
            "\"Midnight\"", "\"Mint\"", "\"Lavender\"", "\"Sand\"", "\"Custom\"",
            "3", "4", "5", "6", "7", "\"7\"", "\"unknown\"", "123", "null", "[]", "{}"
        };
        var obsoletePayloads = new[]
        {
            """{"background":"#112233","foreground":"#EEEEDD","accent":"#AABBCC","enabled":true,"width":400,"blocks":[{"kind":"Usage","providerId":"grok","x":20}]}""",
            "null", "17", "\"malformed\"", "[false,{},null]",
            """{"background":{},"foreground":[],"accent":false,"width":"bad","enabled":[],"blocks":{"bad":true}}"""
        };
        foreach (var theme in obsoleteThemes)
        {
            foreach (var payload in obsoletePayloads)
            {
                var json = $$"""
                    {
                        "alwaysOnTop": true,
                        "fiveHourDisplay": "Hidden",
                        "showResetCountdown": true,
                        "showCodex": false,
                        "showClaude": true,
                        "showGrok": false,
                        "theme": {{theme}},
                        "customColors": {{payload}},
                        "layout": {{payload}},
                        "scale": 1.25,
                        "opacity": 0.85,
                        "hasPosition": true,
                        "left": -1100.5,
                        "top": 45.25,
                        "refreshSeconds": 120,
                        "codexPath": "C:\\Tools\\codex.exe",
                        "claudePath": "C:\\Tools\\claude.exe",
                        "grokPath": "C:\\Tools\\grok.exe"
                    }
                    """;
                File.WriteAllText(store.FilePath, json);
                var restored = store.Load();
                assert(restored == expected, $"Legacy theme {theme} and obsolete appearance values must preserve every live preference.");
                assert(store.TrySave(restored), "Migrated settings must save successfully.");
                assert(new SettingsStore(Path.GetDirectoryName(store.FilePath) ?? "").Load() == expected,
                    "Migrated live preferences must survive a restart.");
                using var document = JsonDocument.Parse(File.ReadAllText(store.FilePath));
                assert(!document.RootElement.TryGetProperty("customColors", out _) && !document.RootElement.TryGetProperty("layout", out _),
                    "Saving migrated settings must remove obsolete colors and free layout fields.");
            }
        }

        foreach (var theme in new[] { WidgetTheme.System, WidgetTheme.Light, WidgetTheme.Dark })
        {
            File.WriteAllText(store.FilePath, $$"""{"theme":"{{theme}}","customColors":[],"layout":false,"alwaysOnTop":true}""");
            var restored = store.Load();
            assert(restored.Theme == theme && restored.AlwaysOnTop,
                "Ignoring obsolete fields must preserve supported themes and unrelated preferences.");
        }
    }
}
