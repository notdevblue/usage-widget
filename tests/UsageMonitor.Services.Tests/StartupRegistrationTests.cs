using System.Runtime.Versioning;
using Microsoft.Win32;
using UsageMonitor.Services;

internal static class StartupRegistrationTests
{
    public static void Run(string directory, Action<bool, string> assert)
    {
        if (OperatingSystem.IsWindows())
            RunWindows(directory, assert);
    }

    [SupportedOSPlatform("windows")]
    private static void RunWindows(string directory, Action<bool, string> assert)
    {
        var testDirectory = Path.GetFullPath(directory);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var executableDirectory = Path.GetFullPath(Path.Combine(testDirectory, "startup registration", "spaced app directory"));
        if (!testDirectory.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
            || !executableDirectory.StartsWith(testDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Startup registration fixtures must stay within the temporary test directory.");
        Directory.CreateDirectory(executableDirectory);
        var executablePath = Path.Combine(executableDirectory, "Usage Monitor.exe");
        File.WriteAllBytes(executablePath, []);
        var ownedRoot = @"Software\UsageMonitor.Tests\" + Guid.NewGuid().ToString("N");
        var subKey = ownedRoot + @"\Run";
        VerifyOwnedKey(ownedRoot, subKey);
        try
        {
            var registration = new StartupRegistration(executablePath, subKey);
            assert(!registration.IsEnabled(), "An absent startup value must be reported as disabled.");
            using (var absent = Registry.CurrentUser.OpenSubKey(subKey))
                assert(absent is null, "Constructing and reading startup registration must not create a registry key.");

            assert(registration.TrySetEnabled(true, out var error) && error.Length == 0, "Enabling a valid executable must succeed without feedback errors.");
            using (var key = Registry.CurrentUser.OpenSubKey(subKey, true)
                ?? throw new InvalidOperationException("The startup fixture key was not created."))
            {
                assert(Equals(key.GetValue("UsageMonitor"), '"' + executablePath + '"'), "Startup must store one quoted absolute executable path without arguments.");
                assert(key.GetValueKind("UsageMonitor") == RegistryValueKind.String, "Startup commands must use REG_SZ.");
                key.SetValue("UnrelatedApplication", "unrelated startup command", RegistryValueKind.String);
            }

            assert(registration.IsEnabled() && new StartupRegistration(executablePath, subKey).IsEnabled(), "Startup state must be read back from the OS by new service instances.");
            assert(registration.TrySetEnabled(true, out error) && registration.IsEnabled(), "Enabling startup repeatedly must be idempotent.");
            var otherExecutable = Path.Combine(executableDirectory, "Old Usage Monitor.exe");
            File.WriteAllBytes(otherExecutable, []);
            using (var key = Registry.CurrentUser.OpenSubKey(subKey, true)
                ?? throw new InvalidOperationException("The startup fixture key disappeared."))
            {
                key.SetValue("UsageMonitor", '"' + otherExecutable + '"', RegistryValueKind.String);
                assert(!registration.IsEnabled(), "A stale command pointing at another executable must not count as this installation being enabled.");
                key.SetValue("UsageMonitor", '"' + executablePath + "\" --unexpected", RegistryValueKind.String);
                assert(!registration.IsEnabled(), "Commands with unexpected arguments must not count as the requested startup registration.");
            }

            assert(registration.TrySetEnabled(true, out error) && registration.IsEnabled(), "Enabling startup must repair the app's stale registration.");
            assert(registration.TrySetEnabled(false, out error) && error.Length == 0 && !registration.IsEnabled(), "Disabling startup must remove the app's value.");
            assert(registration.TrySetEnabled(false, out error) && error.Length == 0, "Disabling an already disabled registration must be idempotent.");
            using (var key = Registry.CurrentUser.OpenSubKey(subKey)
                ?? throw new InvalidOperationException("Disabling must preserve unrelated startup values."))
            {
                assert(key.GetValue("UsageMonitor") is null && Equals(key.GetValue("UnrelatedApplication"), "unrelated startup command"),
                    "Disabling startup must remove only UsageMonitor and preserve unrelated applications.");
            }

            TestInvalidExecutables(executableDirectory, subKey, assert);
            var invalidRegistry = new StartupRegistration(executablePath, subKey + @"\" + new string('x', 256));
            assert(!invalidRegistry.IsEnabled(), "An invalid test registry path must read as disabled without throwing.");
            assert(!invalidRegistry.TrySetEnabled(true, out error) && HasKorean(error), "Registry failures must return Korean feedback without escaping normal exceptions.");
            TestLongCommand(executableDirectory, subKey, assert);
        }
        finally
        {
            VerifyOwnedKey(ownedRoot, subKey);
            Registry.CurrentUser.DeleteSubKeyTree(ownedRoot, false);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void TestInvalidExecutables(string directory, string subKey, Action<bool, string> assert)
    {
        var dll = Path.Combine(directory, "UsageMonitor.dll");
        var host = Path.Combine(directory, "dotnet.exe");
        File.WriteAllBytes(dll, []);
        File.WriteAllBytes(host, []);
        foreach (var path in new[] { Path.Combine(directory, "missing.exe"), dll, host, Path.Combine(directory, "invalid\0.exe") })
        {
            var registration = new StartupRegistration(path, subKey);
            assert(!registration.IsEnabled(), "Invalid or missing executable paths must not count as startup registrations.");
            assert(!registration.TrySetEnabled(true, out var error) && HasKorean(error), "Invalid startup executables must fail with Korean feedback.");
        }

        using var key = Registry.CurrentUser.OpenSubKey(subKey)
            ?? throw new InvalidOperationException("Invalid executable checks must preserve the existing fixture key.");
        assert(key.GetValue("UsageMonitor") is null && Equals(key.GetValue("UnrelatedApplication"), "unrelated startup command"),
            "Rejected executable paths must neither create startup commands nor alter unrelated values.");
    }

    [SupportedOSPlatform("windows")]
    private static void TestLongCommand(string directory, string subKey, Action<bool, string> assert)
    {
        var path = Path.Combine(directory, new string('a', 80), new string('b', 80), new string('c', 80), "UsageMonitor.exe");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
            File.WriteAllBytes(path, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var registration = new StartupRegistration(path, subKey);
        assert(!registration.TrySetEnabled(true, out var error) && HasKorean(error), "An existing executable whose quoted command exceeds 260 characters must be rejected with feedback.");
    }

    private static void VerifyOwnedKey(string ownedRoot, string subKey)
    {
        const string prefix = @"Software\UsageMonitor.Tests\";
        if (!ownedRoot.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(ownedRoot[prefix.Length..], "N", out _)
            || subKey != ownedRoot + @"\Run")
            throw new InvalidOperationException("Registry fixtures may only modify their uniquely owned startup test key.");
    }

    private static bool HasKorean(string text) => text.Any(character => character >= '\uAC00' && character <= '\uD7A3');
}
