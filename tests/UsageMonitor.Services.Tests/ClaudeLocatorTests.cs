using UsageMonitor.Services;

internal static class ClaudeLocatorTests
{
    public static void Run(string directory, Action<bool, string> assert)
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var variableName = "USAGE_MONITOR_CLAUDE_LOCATOR_" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(directory, "claude-locator");
        var preferred = CreateExecutable(Path.Combine(root, "preferred", "claude.exe"));
        var firstPath = Path.Combine(root, "first path");
        var first = CreateExecutable(Path.Combine(firstPath, "claude.exe"));
        var secondPath = Path.Combine(root, "second-path");
        var second = CreateExecutable(Path.Combine(secondPath, "claude.exe"));
        try
        {
            Environment.SetEnvironmentVariable("PATH", '"' + firstPath + '"' + Path.PathSeparator + secondPath);
            Environment.SetEnvironmentVariable(variableName, Path.GetDirectoryName(preferred) ?? "");
            var locator = new ClaudeExecutableLocator();
            var result = locator.Find(preferred);
            assert(result.Found && result.Path == preferred && result.Source.Length > 0, "Preferred Claude executable must take priority over PATH.");
            assert(locator.Find('"' + preferred + '"').Path == preferred, "Quoted preferred Claude paths must be accepted.");
            assert(locator.Find("%" + variableName + "%\\claude.exe").Path == preferred, "Preferred Claude paths must expand environment variables.");
            assert(locator.Find().Path == first, "Quoted PATH directories with spaces must retain native search order.");
            assert(locator.Find(Path.Combine(root, "missing", "claude.exe")).Path == first, "A missing saved Claude executable must fall back to PATH.");
            assert(locator.Find("\0").Path == first, "Invalid saved Claude paths must fall back safely.");
            assert(locator.Find(Path.GetDirectoryName(preferred) ?? "").Path == first, "Directory paths must not be returned as Claude executables.");
            var script = Path.Combine(root, "preferred", "claude.cmd");
            File.WriteAllText(script, "unused");
            assert(locator.Find(script).Path == first, "Claude CMD launchers must never be returned as native executables.");
            script = Path.Combine(root, "preferred", "claude.ps1");
            File.WriteAllText(script, "unused");
            assert(locator.Find(script).Path == first, "Claude PowerShell launchers must never be returned as native executables.");
            var other = CreateExecutable(Path.Combine(root, "preferred", "other.exe"));
            assert(locator.Find(other).Path == first, "Unrelated executable names must not be accepted as Claude.");
            using (var locked = new FileStream(preferred, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                assert(locator.Find(preferred).Path == first, "Unreadable preferred Claude executables must fall back safely.");
            using (var locked = new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                assert(locator.Find().Path == second, "Unreadable PATH executables must not prevent searching later native candidates.");
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
        var store = new SettingsStore(Path.Combine(directory, "claude-settings"));
        var expected = new AppSettings { ClaudePath = executable, CodexPath = "C:\\Tools\\codex.exe", AlwaysOnTop = true };
        assert(store.TrySave(expected), "Claude executable preferences must save successfully.");
        assert(store.Load() == expected, "Claude and Codex executable preferences must round trip independently.");
        File.WriteAllText(store.FilePath, "{\"codexPath\":\"C:\\\\Tools\\\\codex.exe\",\"alwaysOnTop\":true}");
        var legacy = store.Load();
        assert(legacy.ClaudePath == "" && legacy.CodexPath == expected.CodexPath && legacy.AlwaysOnTop, "Legacy settings must gain an empty Claude path without losing existing preferences.");
        File.WriteAllText(store.FilePath, "{\"claudePath\":null,\"alwaysOnTop\":true}");
        assert(store.Load().ClaudePath == "" && store.Load().AlwaysOnTop, "Null Claude paths must normalize without discarding unrelated preferences.");
        var path = Path.Combine(directory, "claude-appearance.json");
        var profiles = new AppearanceProfileStore();
        File.WriteAllText(path, "{\"version\":1,\"theme\":\"Mint\",\"claudePath\":\"C:\\\\private\\\\claude.exe\"}");
        assert(profiles.TryImport(path, out var profile) && profile.Theme == WidgetTheme.Mint, "Appearance imports must ignore machine-specific Claude paths.");
        assert(profiles.TryExport(path, profile) && !File.ReadAllText(path).Contains("claudePath", StringComparison.OrdinalIgnoreCase), "Appearance exports must never include Claude executable preferences.");
    }

    private static string CreateExecutable(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
        File.WriteAllBytes(path, []);
        return path;
    }
}
