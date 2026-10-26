using System.Globalization;
using System.Runtime.InteropServices;
using UsageMonitor.Services;

var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "UsageMonitor.Services.Tests"));
var temporaryDirectory = Path.GetFullPath(Path.Combine(testRoot, Guid.NewGuid().ToString("N")));
if (!temporaryDirectory.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Temporary test directory must stay within the test root.");
}

Directory.CreateDirectory(temporaryDirectory);
var originalPath = Environment.GetEnvironmentVariable("PATH") ?? "";
var assertions = 0;

try
{
    TestSettings();
    TestThemeCatalog();
    AppearanceTests.Run(temporaryDirectory, Assert);
    RefreshScheduleTests.Run(Assert);
    await CodexResetNewsTests.RunAsync(Assert);
    TestLocator();
    ClaudeLocatorTests.Run(temporaryDirectory, Assert);
    GrokLocatorTests.Run(temporaryDirectory, Assert);
    Console.WriteLine($"Passed {assertions} service assertions.");
}
finally
{
    Environment.SetEnvironmentVariable("PATH", originalPath);
    Directory.Delete(temporaryDirectory, true);
}

void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }

    assertions++;
}

void TestSettings()
{
    var settingsDirectory = Path.Combine(temporaryDirectory, "settings");
    var store = new SettingsStore(settingsDirectory);
    Assert(store.Load() == new AppSettings(), "Missing settings must use defaults.");
    Assert(store.FilePath == Path.Combine(settingsDirectory, "settings.json"), "Settings path must use the supplied directory.");

    var expected = new AppSettings
    {
        AlwaysOnTop = true,
        FiveHourDisplay = FiveHourDisplay.Hidden,
        Theme = WidgetTheme.Custom,
        CustomColors = new ThemeColors { Background = "#152535", Foreground = "#F2F4F8", Accent = "#E5B98C" },
        Scale = 1.2,
        Opacity = 0.85,
        HasPosition = true,
        Left = -1100.5,
        Top = 45.25,
        RefreshSeconds = 120,
        CodexPath = "C:\\Tools\\codex.exe"
    };
    Assert(store.TrySave(expected), "Settings must save successfully.");
    Assert(store.Load() == expected, "Settings must round trip.");
    Assert(!Directory.EnumerateFiles(settingsDirectory, "*.tmp").Any(), "Successful save must clean temporary files.");

    File.WriteAllText(store.FilePath, "{\"alwaysOnTop\":true,\"theme\":2,\"refreshSeconds\":300}");
    var legacy = store.Load();
    Assert(legacy.AlwaysOnTop && legacy.Theme == WidgetTheme.Dark && legacy.RefreshSeconds == 300, "Legacy settings must preserve existing values.");
    Assert(legacy.CustomColors == new ThemeColors() && legacy.Scale == 1 && legacy.Opacity == 1, "Legacy settings must receive defaults for new appearance values.");

    File.WriteAllText(store.FilePath, "{\"theme\":\"custom\",\"customColors\":{\"background\":\"invalid\",\"foreground\":null,\"accent\":\" #abc123 \"},\"scale\":1.25,\"opacity\":0.9}");
    var custom = store.Load();
    Assert(custom.Theme == WidgetTheme.Custom && custom.CustomColors.Background == "#F8FAF8" && custom.CustomColors.Foreground == "#202D29", "Invalid custom colors must fall back individually.");
    Assert(custom.CustomColors.Accent == "#ABC123" && custom.Scale == 1.25 && custom.Opacity == 0.9, "Valid custom colors and dimensions must survive normalization.");
    File.WriteAllText(store.FilePath, "{\"customColors\":null}");
    Assert(store.Load().CustomColors == new ThemeColors(), "A null custom palette must recover defaults.");
    File.WriteAllText(store.FilePath, "{\"alwaysOnTop\":true,\"theme\":\"unknown-theme\",\"fiveHourDisplay\":\"unknown-display\",\"scale\":\"NaN\",\"opacity\":\"Infinity\"}");
    var invalidAppearance = store.Load();
    Assert(invalidAppearance.AlwaysOnTop && invalidAppearance.Theme == WidgetTheme.System && invalidAppearance.FiveHourDisplay == FiveHourDisplay.Automatic, "Invalid enum strings must recover individually without discarding other settings.");
    Assert(invalidAppearance.Scale == 1 && invalidAppearance.Opacity == 1, "Nonfinite values from JSON must recover individually.");

    File.WriteAllText(store.FilePath, "{ broken json");
    Assert(store.Load() == new AppSettings(), "Malformed settings must use defaults.");
    File.WriteAllText(store.FilePath, "null");
    Assert(store.Load() == new AppSettings(), "Null settings must use defaults.");
    File.WriteAllText(store.FilePath, "{\"fiveHourDisplay\":123,\"theme\":123,\"refreshSeconds\":1,\"codexPath\":null}");
    var normalized = store.Load();
    Assert(normalized.FiveHourDisplay == FiveHourDisplay.Automatic, "Invalid five hour display must use its default.");
    Assert(normalized.Theme == WidgetTheme.System, "Invalid theme must use its default.");
    Assert(normalized.RefreshSeconds == 30, "Refresh interval must respect its minimum.");
    Assert(normalized.CodexPath == "", "Null executable path must normalize to an empty string.");

    Assert(store.TrySave(expected with { Left = double.NaN, Top = double.PositiveInfinity, RefreshSeconds = 10000, Scale = double.NaN, Opacity = double.NegativeInfinity }), "Nonfinite settings must normalize before serialization.");
    normalized = store.Load();
    Assert(!normalized.HasPosition && normalized.Left == 0 && normalized.Top == 0, "Nonfinite coordinates must discard the saved position.");
    Assert(normalized.RefreshSeconds == 600, "Refresh interval must respect its maximum.");
    Assert(normalized.Scale == 1 && normalized.Opacity == 1, "Nonfinite scale and opacity must recover defaults.");
    Assert(store.TrySave(expected with { Scale = 0.1, Opacity = -100 }), "Appearance values below bounds must save after normalization.");
    normalized = store.Load();
    Assert(normalized.Scale == 0.8 && normalized.Opacity == 0.6, "Scale and opacity must respect lower bounds.");
    Assert(store.TrySave(expected with { Scale = 100, Opacity = 2 }), "Appearance values above bounds must save after normalization.");
    normalized = store.Load();
    Assert(normalized.Scale == 1.5 && normalized.Opacity == 1, "Scale and opacity must respect upper bounds.");

    File.Delete(store.FilePath);
    Directory.CreateDirectory(store.FilePath);
    Assert(!store.TrySave(expected), "An unwritable settings destination must report failure.");
    Assert(!Directory.EnumerateFiles(settingsDirectory, "*.tmp").Any(), "Failed save must clean temporary files.");
    Assert(store.Load() == new AppSettings(), "Unreadable settings must use defaults.");

    var blockedDirectory = Path.Combine(temporaryDirectory, "blocked-settings");
    File.WriteAllText(blockedDirectory, "file");
    Assert(!new SettingsStore(blockedDirectory).TrySave(expected), "An invalid parent directory must report failure.");
}

void TestThemeCatalog()
{
    var catalog = new ThemeCatalog();
    var keys = new[] { "CardBrush", "TextBrush", "SecondaryBrush", "AccentBrush", "AccentSoftBrush", "TrackBrush", "LineBrush", "HoverBrush", "ErrorBrush", "WarningBrush", "ActionTextBrush" };
    Assert(Enum.GetValues<WidgetTheme>().Select(theme => (int)theme).SequenceEqual(Enumerable.Range(0, 8)), "Theme numeric values must remain stable for legacy settings.");

    foreach (var theme in Enum.GetValues<WidgetTheme>())
    {
        var palette = catalog.Create(theme, new(), false);
        Assert(palette.Count == keys.Length && keys.All(palette.ContainsKey), $"{theme} must provide every resource token.");
        Assert(palette.Values.All(IsHex), $"{theme} must return six-digit hex colors.");
        Assert(Contrast(palette["CardBrush"], palette["TextBrush"]) >= 7, $"{theme} foreground must remain readable.");
        Assert(Contrast(palette["CardBrush"], palette["SecondaryBrush"]) >= 4.5, $"{theme} secondary text must remain readable.");
        Assert(palette["ActionTextBrush"] is "#000000" or "#FFFFFF", $"{theme} action text must choose black or white.");
        Assert(Contrast(palette["AccentBrush"], palette["ActionTextBrush"]) >= 4.5, $"{theme} action text must remain readable.");
    }

    var light = catalog.Create(WidgetTheme.Light, new(), false);
    var dark = catalog.Create(WidgetTheme.Dark, new(), false);
    Assert(catalog.Create(WidgetTheme.System, new(), false).SequenceEqual(light), "System theme must follow light mode.");
    Assert(catalog.Create(WidgetTheme.System, new(), true).SequenceEqual(dark), "System theme must follow dark mode.");
    Assert(catalog.Create((WidgetTheme)123, new(), false).SequenceEqual(light), "Unknown themes must use the light palette.");
    var presetBackgrounds = new[] { WidgetTheme.Light, WidgetTheme.Dark, WidgetTheme.Midnight, WidgetTheme.Mint, WidgetTheme.Lavender, WidgetTheme.Sand }
        .Select(theme => catalog.Create(theme, new(), false)["CardBrush"]);
    Assert(presetBackgrounds.Distinct().Count() == 6, "Preset themes must have distinct backgrounds.");

    var custom = catalog.Create(WidgetTheme.Custom, new ThemeColors { Background = "#112233", Foreground = "#EEDDCC", Accent = "#707070" }, false);
    Assert(custom["CardBrush"] == "#112233" && custom["TextBrush"] == "#EEDDCC" && custom["AccentBrush"] == "#707070", "Custom palettes must preserve the selected base colors.");
    Assert(custom["AccentSoftBrush"] != custom["CardBrush"] && custom["TrackBrush"] != custom["CardBrush"] && custom["HoverBrush"] != custom["CardBrush"], "Custom surfaces must derive from the selected base colors.");
    var lowContrast = catalog.Create(WidgetTheme.Custom, new ThemeColors { Background = "#777777", Foreground = "#777777", Accent = "#777777" }, false);
    Assert(Contrast(lowContrast["CardBrush"], lowContrast["SecondaryBrush"]) >= 4.5, "Secondary text must remain readable even for a low contrast custom foreground.");
    Assert(Contrast(lowContrast["AccentBrush"], lowContrast["ActionTextBrush"]) >= 4.5, "Custom action text must choose the higher contrast option.");
    var invalid = catalog.Create(WidgetTheme.Custom, new ThemeColors { Background = "#123", Foreground = "rgba(0,0,0,1)", Accent = "#GGGGGG" }, false);
    Assert(invalid.SequenceEqual(light), "Invalid custom colors must safely use defaults.");
}

bool IsHex(string value) => value.Length == 7 && value[0] == '#' && int.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _);

double Contrast(string first, string second)
{
    var firstLuminance = Luminance(first);
    var secondLuminance = Luminance(second);
    return (Math.Max(firstLuminance, secondLuminance) + 0.05) / (Math.Min(firstLuminance, secondLuminance) + 0.05);
}

double Luminance(string color)
{
    var packed = Convert.ToInt32(color[1..], 16);
    var channels = new[] { (packed >> 16) & 255, (packed >> 8) & 255, packed & 255 }
        .Select(channel => channel / 255.0)
        .Select(channel => channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4))
        .ToArray();
    return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
}

void TestLocator()
{
    var locator = new CodexExecutableLocator();
    var preferred = CreateExecutable(Path.Combine(temporaryDirectory, "preferred", "codex.exe"));
    var preferredLocation = locator.Find(preferred);
    Assert(preferredLocation.Found && preferredLocation.Path == preferred, "Explicit executable must take priority.");
    Assert(locator.Find('"' + preferred + '"').Path == preferred, "Quoted explicit executable must be supported.");

    var nativeDirectory = Path.Combine(temporaryDirectory, "path-native");
    var native = CreateExecutable(Path.Combine(nativeDirectory, "codex.exe"));
    var firstPackageDirectory = Path.Combine(temporaryDirectory, "path-package");
    CreateExecutable(NestedPackageExecutable(firstPackageDirectory));
    Environment.SetEnvironmentVariable("PATH", firstPackageDirectory + Path.PathSeparator + nativeDirectory);
    Assert(locator.Find().Path == native, "Native PATH executable must take priority over npm packages.");
    Assert(locator.Find(Path.Combine(temporaryDirectory, "missing.exe")).Path == native, "Missing preferred executable must fall back to PATH.");
    var script = Path.Combine(nativeDirectory, "codex.cmd");
    File.WriteAllText(script, "unused");
    Assert(locator.Find(script).Path == native, "Shell launchers must never be returned as native executables.");

    var nestedDirectory = Path.Combine(temporaryDirectory, "npm-nested");
    var nested = CreateExecutable(NestedPackageExecutable(nestedDirectory));
    Environment.SetEnvironmentVariable("PATH", nestedDirectory);
    Assert(locator.Find().Path == nested, "Nested optional npm package must be discovered.");

    var platform = GetPlatform();
    var hoistedDirectory = Path.Combine(temporaryDirectory, "npm-hoisted");
    var hoisted = CreateExecutable(Path.Combine(hoistedDirectory, "node_modules", "@openai", platform.Package, "vendor", platform.Triple, "bin", "codex.exe"));
    Environment.SetEnvironmentVariable("PATH", hoistedDirectory);
    Assert(locator.Find().Path == hoisted, "Hoisted optional npm package must be discovered.");

    var legacyDirectory = Path.Combine(temporaryDirectory, "npm-legacy");
    var legacy = CreateExecutable(Path.Combine(legacyDirectory, "node_modules", "@openai", "codex", "vendor", platform.Triple, "codex", "codex.exe"));
    Environment.SetEnvironmentVariable("PATH", legacyDirectory);
    Assert(locator.Find().Path == legacy, "Legacy vendor npm package must be discovered.");

    var binDirectory = Path.Combine(hoistedDirectory, "node_modules", ".bin");
    Directory.CreateDirectory(binDirectory);
    Environment.SetEnvironmentVariable("PATH", binDirectory);
    Assert(locator.Find().Path == hoisted, "Local npm .bin directory must discover its hoisted package.");
}

string CreateExecutable(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
    File.WriteAllBytes(path, []);
    return path;
}

string NestedPackageExecutable(string directory)
{
    var platform = GetPlatform();
    return Path.Combine(directory, "node_modules", "@openai", "codex", "node_modules", "@openai", platform.Package, "vendor", platform.Triple, "bin", "codex.exe");
}

(string Package, string Triple) GetPlatform()
{
    return RuntimeInformation.OSArchitecture == Architecture.Arm64
        ? ("codex-win32-arm64", "aarch64-pc-windows-msvc")
        : ("codex-win32-x64", "x86_64-pc-windows-msvc");
}
