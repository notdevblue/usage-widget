using UsageMonitor.Services;

internal static class GrokLocatorTests
{
    public static void Run(string directory, Action<bool, string> assert)
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var variableName = "USAGE_MONITOR_GROK_LOCATOR_" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(directory, "grok-locator");
        var profile = Path.Combine(root, "profile");
        var preferred = CreateExecutable(Path.Combine(root, "preferred", "grok.exe"));
        var firstPath = Path.Combine(root, "first path");
        var first = CreateExecutable(Path.Combine(firstPath, "grok.exe"));
        var installed = CreateExecutable(Path.Combine(profile, ".grok", "bin", "grok.exe"));
        try
        {
            Environment.SetEnvironmentVariable("PATH", '"' + firstPath + '"');
            Environment.SetEnvironmentVariable(variableName, Path.GetDirectoryName(preferred) ?? "");
            var locator = new GrokExecutableLocator(profile);
            var result = locator.Find(preferred);
            assert(result.Found && result.Path == preferred && result.Source.Length > 0, "Preferred Grok executable must take priority over PATH.");
            assert(locator.Find('"' + preferred + '"').Path == preferred, "Quoted preferred Grok paths must be accepted.");
            assert(locator.Find("%" + variableName + "%\\grok.exe").Path == preferred, "Preferred Grok paths must expand environment variables.");
            assert(locator.Find().Path == first, "Quoted PATH directories with spaces must retain native Grok search order.");
            assert(locator.Find(Path.Combine(root, "missing", "grok.exe")).Path == first, "A missing saved Grok executable must fall back to PATH.");
            var script = Path.Combine(root, "preferred", "grok.cmd");
            File.WriteAllText(script, "unused");
            assert(locator.Find(script).Path == first, "Grok CMD launchers must never be returned as native executables.");
            script = Path.Combine(root, "preferred", "grok.ps1");
            File.WriteAllText(script, "unused");
            assert(locator.Find(script).Path == first, "Grok PowerShell launchers must never be returned as native executables.");
            Environment.SetEnvironmentVariable("PATH", "");
            assert(locator.Find().Path == installed && locator.Find().Source == "Grok Build", "The user Grok installation must be found when PATH has no native executable.");
            using (var locked = new FileStream(installed, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                assert(!locator.Find().Found, "An unreadable installed Grok executable must not be returned.");
            TestSettings(directory, preferred, assert);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable(variableName, "");
        }
    }

    private static void TestSettings(string directory, string executable, Action<bool, string> assert)
    {
        var store = new SettingsStore(Path.Combine(directory, "grok-settings"));
        var expected = new AppSettings { GrokPath = executable, ClaudePath = "C:\\Tools\\claude.exe", CodexPath = "C:\\Tools\\codex.exe", AlwaysOnTop = true };
        assert(store.TrySave(expected), "Grok executable preferences must save successfully.");
        assert(store.Load() == expected, "Grok executable preferences must round trip independently.");
        File.WriteAllText(store.FilePath, "{\"codexPath\":\"C:\\\\Tools\\\\codex.exe\",\"claudePath\":\"C:\\\\Tools\\\\claude.exe\",\"alwaysOnTop\":true}");
        var legacy = store.Load();
        assert(legacy.GrokPath == "" && legacy.CodexPath == expected.CodexPath && legacy.ClaudePath == expected.ClaudePath && legacy.AlwaysOnTop, "Legacy settings must gain an empty Grok path without losing existing preferences.");
        File.WriteAllText(store.FilePath, "{\"grokPath\":null,\"alwaysOnTop\":true}");
        assert(store.Load().GrokPath == "" && store.Load().AlwaysOnTop, "Null Grok paths must normalize without discarding unrelated preferences.");
        var path = Path.Combine(directory, "grok-appearance.json");
        var profiles = new AppearanceProfileStore();
        File.WriteAllText(path, "{\"version\":1,\"theme\":\"Mint\",\"grokPath\":\"C:\\\\private\\\\grok.exe\"}");
        assert(profiles.TryImport(path, out var profile) && profile.Theme == WidgetTheme.Mint, "Appearance imports must ignore machine-specific Grok paths.");
        assert(profiles.TryExport(path, profile) && !File.ReadAllText(path).Contains("grokPath", StringComparison.OrdinalIgnoreCase), "Appearance exports must never include Grok executable preferences.");
    }

    private static string CreateExecutable(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
        File.WriteAllBytes(path, []);
        return path;
    }
}
