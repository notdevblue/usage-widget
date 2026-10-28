using UsageMonitor.Services;

internal static class ProviderVisibilityTests
{
    public static void Run(string directory, Action<bool, string> assert)
    {
        var store = new SettingsStore(Path.Combine(directory, "provider-visibility"));
        var defaults = store.Load();
        assert(defaults.ShowCodex && defaults.ShowClaude && defaults.ShowGrok, "New settings must show all three services.");
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath) ?? "");
        File.WriteAllText(store.FilePath, "{\"theme\":\"Dark\",\"refreshSeconds\":120}");
        var legacy = store.Load();
        assert(legacy.ShowCodex && legacy.ShowClaude && legacy.ShowGrok && legacy.Theme == WidgetTheme.Dark && legacy.RefreshSeconds == 120,
            "Settings saved before visibility options existed must keep all services visible and retain their preferences.");

        foreach (var settings in new[]
        {
            new AppSettings { ShowCodex = false },
            new AppSettings { ShowClaude = false },
            new AppSettings { ShowGrok = false },
            new AppSettings { ShowCodex = false, ShowClaude = false, ShowGrok = false }
        })
        {
            assert(store.TrySave(settings), "Provider visibility settings must save successfully.");
            var restarted = new SettingsStore(Path.GetDirectoryName(store.FilePath) ?? "").Load();
            assert(restarted.ShowCodex == settings.ShowCodex && restarted.ShowClaude == settings.ShowClaude && restarted.ShowGrok == settings.ShowGrok,
                "Independent service visibility choices must survive a fresh settings store.");
            assert(restarted.IsProviderVisible("codex") == settings.ShowCodex && restarted.IsProviderVisible("claude") == settings.ShowClaude
                && restarted.IsProviderVisible("grok") == settings.ShowGrok, "Provider lookup must honor each saved service choice.");
            assert(restarted.IsProviderVisible("future-provider"), "Unknown service IDs must remain visible for compatibility.");
        }

        File.WriteAllText(store.FilePath, "{\"showCodex\":false}");
        var partial = store.Load();
        assert(!partial.ShowCodex && partial.ShowClaude && partial.ShowGrok, "A partially updated settings file must default each missing visibility option independently.");
        var invalidAppearance = partial with { Theme = (WidgetTheme)99, Scale = double.NaN };
        assert(store.TrySave(invalidAppearance) && !store.Load().ShowCodex && store.Load().ShowClaude && store.Load().ShowGrok,
            "Appearance normalization must preserve service visibility preferences.");
    }
}
