using System.Diagnostics;
using System.Text.Json;
using UsageMonitor.Core;

namespace UsageMonitor.Core.Tests;

internal static class ClaudeTests
{
    internal static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("Claude server meters retain optional windows and model scope", ServerMetersAsync),
        ("Claude null and missing quota reports differ from explicit empty meters", MissingQuotaAsync),
        ("Claude stream rejects model inference and absent reports", StreamValidationAsync),
        ("Claude CLI uses only auth status and zero-turn usage", ProviderReadAsync),
        ("Claude non-subscription credentials skip quota", NonSubscriptionAsync),
        ("Claude account changes discard previous quota", AccountSwitchAsync),
        ("Claude old CLI cannot run usage as a model prompt", OldVersionAsync),
        ("Claude errors hide raw stderr", ProcessExitAsync),
        ("Claude cancellation and disposal terminate owned process", CancellationAsync),
        ("Claude timeout and output bounds terminate owned process", TimeoutAsync)
    ];

    private static Task ServerMetersAsync()
    {
        var windows = ParseWindows("""
            {"rate_limits":{"limits":[
              {"kind":"weekly_all","percent":10,"resets_at":"2026-10-27T02:00:00.405437+00:00"},
              {"kind":"weekly_scoped","percent":0,"scope":{"model":{"display_name":"Fable"}}},
              {"kind":"future_meter","percent":130,"scope":{"surface":{"display_name":"Other"}},"resets_at":"invalid"},
              {"kind":"session","percent":"missing"}
            ]}}
            """);
        Equal(3, windows.Count);
        Equal("주간", windows[0].Label);
        Equal(10080, windows[0].WindowMinutes);
        Equal("Fable", windows[1].Label);
        Equal(0d, windows[1].UsedPercent);
        Equal(0, windows[2].WindowMinutes);
        Equal(100d, windows[2].UsedPercent);
        Equal(DateTimeOffset.MinValue, windows[2].ResetsAt);
        True(windows.All(window => !window.IsFiveHour));
        Equal(0, ParseWindows("{\"rate_limits\":{\"limits\":[]}}").Count);
        var session = ParseWindows("{\"rate_limits\":{\"limits\":[{\"kind\":\"session\",\"percent\":35}]}}").Single();
        Equal("5시간", session.Label);
        True(session.IsFiveHour);
        return Task.CompletedTask;
    }

    private static async Task MissingQuotaAsync()
    {
        Equal(0, ParseWindows("{\"rate_limits\":{\"limits\":[]}}").Count);
        foreach (var json in new[]
                 {
                     "{}", "{\"rate_limits\":null}", "{\"rate_limits\":{\"limits\":null}}",
                     "{\"rate_limits\":{}}", "{\"rate_limits\":[]}", "{\"rate_limits\":{\"limits\":{}}}"
                 })
        {
            await ThrowsAsync<ClaudeConnectionException>(() => Task.FromResult(ParseWindows(json)));
        }
    }

    private static async Task StreamValidationAsync()
    {
        var report = "{\"type\":\"assistant\",\"usage_report\":{\"rate_limits\":{\"limits\":[]}}}";
        Equal(0, ClaudeResponseParser.ReadStream(report + "\n{\"type\":\"result\",\"subtype\":\"success\",\"num_turns\":0}").Count);
        await ThrowsAsync<ClaudeConnectionException>(() => Task.FromResult(ClaudeResponseParser.ReadStream(report)));
        await ThrowsAsync<ClaudeConnectionException>(() => Task.FromResult(ClaudeResponseParser.ReadStream(
            report + "\n{\"type\":\"result\",\"subtype\":\"success\",\"num_turns\":1}")));
        await ThrowsAsync<ClaudeConnectionException>(() => Task.FromResult(ClaudeResponseParser.ReadStream("secret-invalid-json")));
        var auth = ClaudeResponseParser.ReadAuthentication("{\"loggedIn\":true,\"authMethod\":\"claude.ai\",\"email\":null}");
        Equal(AccountKind.Claude, auth.Account.Kind);
        Equal("", auth.Account.Email);
    }

    private static async Task ProviderReadAsync()
    {
        using var scenario = new Scenario("weekly");
        await using var provider = new ClaudeUsageProvider(Environment.ProcessPath ?? "");
        var snapshot = await provider.ReadAsync();
        Equal("claude", snapshot.ProviderId);
        Equal(AccountKind.Claude, snapshot.Account.Kind);
        Equal("test@example.invalid", snapshot.Account.Email);
        Equal(20d, snapshot.Windows.Single().UsedPercent);
        Equal(4, (await File.ReadAllLinesAsync(scenario.Path + ".commands")).Length);
    }

    private static async Task NonSubscriptionAsync()
    {
        foreach (var name in new[] { "signedout", "apikey", "setup-token" })
        {
            using var scenario = new Scenario(name);
            await using var provider = new ClaudeUsageProvider(Environment.ProcessPath ?? "");
            var snapshot = await provider.ReadAsync();
            Equal(0, snapshot.Windows.Count);
            Equal(name switch
            {
                "signedout" => AccountKind.SignedOut,
                "apikey" => AccountKind.ApiKey,
                _ => AccountKind.Other
            }, snapshot.Account.Kind);
            True(!(await File.ReadAllTextAsync(scenario.Path + ".commands")).Contains("/usage", StringComparison.Ordinal));
        }
    }

    private static async Task AccountSwitchAsync()
    {
        using var scenario = new Scenario("switch");
        await using var provider = new ClaudeUsageProvider(Environment.ProcessPath ?? "");
        Equal(20d, (await provider.ReadAsync()).Windows.Single().UsedPercent);
        Equal("4", await File.ReadAllTextAsync(scenario.Path));
    }

    private static async Task OldVersionAsync()
    {
        using var scenario = new Scenario("old-version");
        await using var provider = new ClaudeUsageProvider(Environment.ProcessPath ?? "");
        var error = await ThrowsAsync<ClaudeConnectionException>(() => provider.ReadAsync());
        True(error.Message.Contains("2.1.273", StringComparison.Ordinal));
        Equal(1, (await File.ReadAllLinesAsync(scenario.Path + ".commands")).Length);
    }

    private static async Task ProcessExitAsync()
    {
        using var scenario = new Scenario("exit");
        await using var provider = new ClaudeUsageProvider(Environment.ProcessPath ?? "");
        var error = await ThrowsAsync<ClaudeConnectionException>(() => provider.ReadAsync());
        True(!error.ToString().Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task CancellationAsync()
    {
        foreach (var dispose in new[] { false, true })
        {
            using var scenario = new Scenario("hang");
            var provider = new ClaudeUsageProvider(Environment.ProcessPath ?? "");
            using var cancellation = new CancellationTokenSource();
            try
            {
                var pending = provider.ReadAsync(cancellation.Token);
                var pid = await scenario.ReadPidAsync();
                if (dispose)
                {
                    await provider.DisposeAsync();
                }
                else
                {
                    cancellation.Cancel();
                }

                await ThrowsAsync<OperationCanceledException>(async () => await pending);
                True(!IsProcessRunning(pid));
            }
            finally
            {
                await provider.DisposeAsync();
            }
        }
    }

    private static async Task TimeoutAsync()
    {
        using (var scenario = new Scenario("hang"))
        {
            await using var provider = new ClaudeUsageProvider(Environment.ProcessPath ?? "", TimeSpan.FromSeconds(1));
            var pending = provider.ReadAsync();
            var pid = await scenario.ReadPidAsync();
            await ThrowsAsync<TimeoutException>(async () => await pending);
            True(!IsProcessRunning(pid));
        }

        using (var scenario = new Scenario("overflow"))
        {
            await using var provider = new ClaudeUsageProvider(Environment.ProcessPath ?? "");
            await ThrowsAsync<ClaudeConnectionException>(() => provider.ReadAsync());
        }
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IReadOnlyList<UsageWindow> ParseWindows(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ClaudeResponseParser.ReadWindows(document.RootElement);
    }

    private static void True(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed.");
        }
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, received {actual}.");
        }
    }

    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T error)
        {
            return error;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class Scenario : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"usage-monitor-claude-{Guid.NewGuid():N}");

        internal Scenario(string name)
        {
            Environment.SetEnvironmentVariable("USAGE_MONITOR_CLAUDE_SCENARIO", name);
            Environment.SetEnvironmentVariable("USAGE_MONITOR_CLAUDE_STATE", Path);
        }

        internal async Task<int> ReadPidAsync()
        {
            for (var attempt = 0; attempt < 150 && !File.Exists(Path + ".pid"); attempt++)
            {
                await Task.Delay(20);
            }

            return int.Parse(await File.ReadAllTextAsync(Path + ".pid"));
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("USAGE_MONITOR_CLAUDE_SCENARIO", "");
            Environment.SetEnvironmentVariable("USAGE_MONITOR_CLAUDE_STATE", "");
            foreach (var suffix in new[] { "", ".commands", ".pid" })
            {
                File.Delete(Path + suffix);
            }
        }
    }
}
