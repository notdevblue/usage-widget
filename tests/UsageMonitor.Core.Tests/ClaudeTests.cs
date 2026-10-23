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
        ("Claude control usage accepts legacy quotas with stable window ids", LegacyQuotaAsync),
        ("Claude canonical quotas take precedence over legacy quotas", CanonicalQuotaAsync),
        ("Claude control usage rejects absent and malformed quota reports", InvalidUsageReportAsync),
        ("Claude CLI requests usage without submitting user messages", ProviderReadAsync),
        ("Claude control usage ignores startup warnings and unrelated response ids", StartupOutputAsync),
        ("Claude control usage rejects unexpected model inference", UnexpectedInferenceAsync),
        ("Claude control errors hide raw response messages", RejectedControlAsync),
        ("Claude control usage rejects invalid successful payloads", InvalidControlReportAsync),
        ("Claude fresh usage does not wait for a stuck shutdown", SuccessfulShutdownAsync),
        ("Claude natural shutdown drains trailing output", TrailingOutputAsync),
        ("Claude non-subscription credentials skip quota", NonSubscriptionAsync),
        ("Claude account changes discard previous quota", AccountSwitchAsync),
        ("Claude old CLI cannot run usage as a model prompt", OldVersionAsync),
        ("Claude errors hide raw stderr", ProcessExitAsync),
        ("Claude cancellation and disposal terminate owned process", CancellationAsync),
        ("Claude timeout and output bounds terminate owned process", TimeoutAsync),
        ("Claude fresh cache avoids server reads and preserves observation time", FreshCacheAsync),
        ("Claude cache rejects mismatched accounts and invalid timestamps", CacheValidationAsync),
        ("Claude fallback keeps age and persists cooldown across restarts", CachedReportAsync),
        ("Claude failed reads increase the persistent backoff", RefreshBackoffAsync),
        ("Claude new cache is readable during server cooldown", CacheDuringCooldownAsync),
        ("Claude failed state persistence prevents server requests", StateWriteFailureAsync),
        ("Claude restart without cache keeps the server cooldown", RestartWithoutCacheAsync),
        ("Claude successful reads honor the interval after cache deletion", SuccessIntervalAsync),
        ("Claude account switch rejects a fresh cache from the old account", CachedAccountSwitchAsync),
        ("Claude confirmed refresh clears a previous failure", RecoveryAsync),
        ("Claude corrupt cooldown files do not permit immediate server reads", CorruptStateAsync)
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

    private static Task LegacyQuotaAsync()
    {
        var windows = ParseUsageReport("""
            {"rate_limits":{
              "five_hour":{"utilization":35,"resets_at":"2026-10-27T02:00:00Z"},
              "seven_day":{"utilization":20,"resets_at":"2026-10-28T02:00:00Z"},
              "seven_day_opus":{"utilization":8,"resets_at":null},
              "seven_day_sonnet":{"utilization":0},
              "model_scoped":[
                {"display_name":"Fable","utilization":2.5,"resets_at":"invalid"},
                {"display_name":"Invalid","utilization":"unknown"}
              ]
            }}
            """);
        Equal(5, windows.Count);
        Equal("claude:session::", windows[0].Id);
        Equal("5시간", windows[0].Label);
        Equal(300, windows[0].WindowMinutes);
        Equal(35d, windows[0].UsedPercent);
        Equal(DateTimeOffset.Parse("2026-10-27T02:00:00Z"), windows[0].ResetsAt);
        Equal("claude:weekly_all::", windows[1].Id);
        Equal("주간", windows[1].Label);
        Equal(10080, windows[1].WindowMinutes);
        Equal("claude:weekly_scoped:Opus:", windows[2].Id);
        Equal("Opus", windows[2].Label);
        Equal("claude:weekly_scoped:Sonnet:", windows[3].Id);
        Equal(0d, windows[3].UsedPercent);
        Equal("claude:weekly_scoped:Fable:", windows[4].Id);
        Equal(2.5d, windows[4].UsedPercent);
        Equal(DateTimeOffset.MinValue, windows[4].ResetsAt);
        return Task.CompletedTask;
    }

    private static Task CanonicalQuotaAsync()
    {
        var windows = ParseUsageReport("""
            {"rate_limits":{
              "limits":[{"kind":"weekly_all","percent":31}],
              "five_hour":{"utilization":5},
              "seven_day":{"utilization":99}
            }}
            """);
        Equal(1, windows.Count);
        Equal("claude:weekly_all::", windows[0].Id);
        Equal(31d, windows[0].UsedPercent);
        Equal(0, ParseUsageReport("{\"rate_limits\":{\"limits\":[],\"seven_day\":{\"utilization\":99}}}").Count);
        var partial = ParseUsageReport("""
            {"rate_limits":{"five_hour":null,"seven_day":{"utilization":17},"seven_day_opus":{"utilization":"unknown"}}}
            """);
        Equal(1, partial.Count);
        Equal("claude:weekly_all::", partial[0].Id);
        Equal(17d, partial[0].UsedPercent);
        return Task.CompletedTask;
    }

    private static async Task InvalidUsageReportAsync()
    {
        foreach (var json in new[]
                 {
                     "null", "{}", "{\"rate_limits\":null}", "{\"rate_limits\":[]}",
                     "{\"rate_limits\":{}}", "{\"rate_limits\":{\"limits\":null}}",
                     "{\"rate_limits\":{\"limits\":{}}}",
                     "{\"rate_limits\":{\"five_hour\":{\"utilization\":\"unknown\"}}}",
                     "{\"rate_limits\":{\"model_scoped\":{}}}",
                     "{\"rate_limits\":{\"five_hour\":null,\"seven_day\":null}}"
                 })
        {
            await ThrowsAsync<ClaudeConnectionException>(() => Task.FromResult(ParseUsageReport(json)));
        }
    }

    private static async Task ProviderReadAsync()
    {
        using var scenario = new Scenario("weekly");
        await using var provider = scenario.CreateProvider();
        var snapshot = await provider.ReadAsync();
        Equal("claude", snapshot.ProviderId);
        Equal(AccountKind.Claude, snapshot.Account.Kind);
        Equal("test@example.invalid", snapshot.Account.Email);
        Equal(20d, snapshot.Windows.Single().UsedPercent);
        var commands = await File.ReadAllLinesAsync(scenario.Path + ".commands");
        Equal(4, commands.Length);
        True(commands[2].Contains("--input-format stream-json", StringComparison.Ordinal));
        True(!commands.Any(command => command.Contains("/usage", StringComparison.Ordinal)));
        var requests = await File.ReadAllLinesAsync(scenario.Path + ".requests");
        Equal(2, requests.Length);
        using var initialize = JsonDocument.Parse(requests[0]);
        using var usage = JsonDocument.Parse(requests[1]);
        Equal("control_request", initialize.RootElement.GetProperty("type").GetString() ?? "");
        Equal("initialize", initialize.RootElement.GetProperty("request").GetProperty("subtype").GetString() ?? "");
        Equal("control_request", usage.RootElement.GetProperty("type").GetString() ?? "");
        Equal("get_usage", usage.RootElement.GetProperty("request").GetProperty("subtype").GetString() ?? "");
        True(usage.RootElement.GetProperty("request").GetProperty("skip_behaviors").GetBoolean());
    }

    private static async Task StartupOutputAsync()
    {
        foreach (var name in new[] { "noisy", "legacy" })
        {
            using var scenario = new Scenario(name);
            await using var provider = scenario.CreateProvider();
            var snapshot = await provider.ReadAsync();
            Equal(20d, snapshot.Windows.Single().UsedPercent);
            Equal("claude:weekly_all::", snapshot.Windows.Single().Id);
        }
    }

    private static async Task RejectedControlAsync()
    {
        foreach (var name in new[] { "rejected-init", "rejected-usage" })
        {
            using var scenario = new Scenario(name);
            await using var provider = scenario.CreateProvider();
            var error = await ThrowsAsync<ClaudeConnectionException>(() => provider.ReadAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            True(!error.ToString().Contains("secret", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static async Task UnexpectedInferenceAsync()
    {
        foreach (var name in new[] { "assistant-frame", "result-frame" })
        {
            using var scenario = new Scenario(name);
            await using var provider = scenario.CreateProvider();
            var error = await ThrowsAsync<ClaudeConnectionException>(() => provider.ReadAsync());
            True(!error.ToString().Contains("secret", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static async Task InvalidControlReportAsync()
    {
        foreach (var name in new[] { "missing-report", "null-report", "malformed-report" })
        {
            using var scenario = new Scenario(name);
            await using var provider = scenario.CreateProvider();
            await ThrowsAsync<ClaudeConnectionException>(() => provider.ReadAsync());
        }
    }

    private static async Task SuccessfulShutdownAsync()
    {
        using var scenario = new Scenario("hang-after-report");
        await using var provider = scenario.CreateProvider();
        var snapshot = await provider.ReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Equal(20d, snapshot.Windows.Single().UsedPercent);
        True(File.Exists(scenario.Path + ".eof"));
        True(!IsProcessRunning(await scenario.ReadPidAsync()));
    }

    private static async Task TrailingOutputAsync()
    {
        using var scenario = new Scenario("trailing-output");
        await using var provider = scenario.CreateProvider();
        var snapshot = await provider.ReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Equal(20d, snapshot.Windows.Single().UsedPercent);
        True(File.Exists(scenario.Path + ".eof-complete"));
    }

    private static async Task NonSubscriptionAsync()
    {
        foreach (var name in new[] { "signedout", "apikey", "setup-token" })
        {
            using var scenario = new Scenario(name);
            await using var provider = scenario.CreateProvider();
            var snapshot = await provider.ReadAsync();
            Equal(0, snapshot.Windows.Count);
            Equal(name switch
            {
                "signedout" => AccountKind.SignedOut,
                "apikey" => AccountKind.ApiKey,
                _ => AccountKind.Other
            }, snapshot.Account.Kind);
            True(!File.Exists(scenario.Path + ".requests"));
            Equal(2, (await File.ReadAllLinesAsync(scenario.Path + ".commands")).Length);
        }
    }

    private static async Task AccountSwitchAsync()
    {
        using var scenario = new Scenario("switch");
        await using var provider = scenario.CreateProvider();
        Equal(20d, (await provider.ReadAsync()).Windows.Single().UsedPercent);
        Equal("4", await File.ReadAllTextAsync(scenario.Path));
    }

    private static async Task OldVersionAsync()
    {
        using var scenario = new Scenario("old-version");
        await using var provider = scenario.CreateProvider();
        var error = await ThrowsAsync<ClaudeConnectionException>(() => provider.ReadAsync());
        True(error.Message.Contains("2.1.273", StringComparison.Ordinal));
        Equal(1, (await File.ReadAllLinesAsync(scenario.Path + ".commands")).Length);
    }

    private static async Task ProcessExitAsync()
    {
        using var scenario = new Scenario("exit");
        await using var provider = scenario.CreateProvider();
        var error = await ThrowsAsync<ClaudeConnectionException>(() => provider.ReadAsync());
        True(!error.ToString().Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task CancellationAsync()
    {
        foreach (var dispose in new[] { false, true })
        {
            using var scenario = new Scenario("hang");
            var provider = scenario.CreateProvider();
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
            await using var provider = scenario.CreateProvider(TimeSpan.FromSeconds(1));
            var pending = provider.ReadAsync();
            var pid = await scenario.ReadPidAsync();
            await ThrowsAsync<TimeoutException>(async () => await pending);
            True(!IsProcessRunning(pid));
        }

        using (var scenario = new Scenario("overflow"))
        {
            await using var provider = scenario.CreateProvider();
            await ThrowsAsync<ClaudeConnectionException>(() => provider.ReadAsync());
        }
    }

    private static async Task FreshCacheAsync()
    {
        using var scenario = new Scenario("weekly");
        var now = DateTimeOffset.UtcNow;
        scenario.WriteCache(now.AddMinutes(-2));
        await using var provider = scenario.CreateProvider();
        var snapshot = await provider.ReadAsync();
        Equal(17d, snapshot.Windows.Single().UsedPercent);
        Equal(now.AddMinutes(-2).ToUnixTimeMilliseconds(), snapshot.ObservedAt.ToUnixTimeMilliseconds());
        True(snapshot.IsCached && !snapshot.IsStale);
        True(!File.Exists(scenario.Path + ".requests"));
    }

    private static Task CacheValidationAsync()
    {
        using var scenario = new Scenario("weekly");
        var now = DateTimeOffset.UtcNow;
        var authentication = ClaudeResponseParser.ReadAuthentication("""
            {"loggedIn":true,"authMethod":"claude.ai","email":"test@example.invalid","orgId":"old-org","subscriptionType":"team"}
            """);
        var cache = new ClaudeUsageCache(scenario.Path + ".cache");
        foreach (var mutation in new[] { "account", "organization", "email", "future", "timestamp", "malformed" })
        {
            scenario.WriteCache(mutation == "future" ? now.AddHours(1) : now.AddMinutes(-1));
            var path = scenario.Path + ".cache";
            var json = File.ReadAllText(path);
            json = mutation switch
            {
                "account" => json.Replace("\"accountUuid\":\"account-id\",\"fetchedAtMs\"", "\"accountUuid\":\"other-id\",\"fetchedAtMs\""),
                "organization" => json.Replace("old-org", "other-org"),
                "email" => json.Replace("test@example.invalid", "other@example.invalid"),
                "timestamp" => json.Replace(now.AddMinutes(-1).ToUnixTimeMilliseconds().ToString(), "9223372036854775807"),
                "malformed" => "{",
                _ => json
            };
            File.WriteAllText(path, json);
            True(!cache.TryGet(authentication, now, out _));
        }
        return Task.CompletedTask;
    }

    private static async Task CachedReportAsync()
    {
        using var scenario = new Scenario("cached-report");
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var observed = clock.GetUtcNow().AddMinutes(-20);
        scenario.WriteCache(observed);
        await using (var provider = scenario.CreateProvider(clock: clock))
        {
            var first = await provider.ReadAsync();
            True(first.IsStale);
            Equal(observed.ToUnixTimeMilliseconds(), first.ObservedAt.ToUnixTimeMilliseconds());
            Equal(17d, first.Windows.Single().UsedPercent);
            await provider.ReadAsync();
        }
        await using (var restarted = scenario.CreateProvider(clock: clock))
        {
            var snapshot = await restarted.ReadAsync();
            Equal(observed.ToUnixTimeMilliseconds(), snapshot.ObservedAt.ToUnixTimeMilliseconds());
        }
        Equal(2, (await File.ReadAllLinesAsync(scenario.Path + ".requests")).Length);
        clock.Advance(TimeSpan.FromMinutes(31));
        await using (var provider = scenario.CreateProvider(clock: clock))
            await provider.ReadAsync();
        var state = scenario.ReadRefreshState();
        Equal(2, state.Failures);
        Equal(clock.GetUtcNow().AddHours(1), state.NextAttemptAt);
    }

    private static async Task RefreshBackoffAsync()
    {
        using var scenario = new Scenario("rejected-usage");
        var clock = new TestClock(DateTimeOffset.UtcNow);
        scenario.WriteCache(clock.GetUtcNow().AddMinutes(-20));
        await using var provider = scenario.CreateProvider(clock: clock);
        True((await provider.ReadAsync()).IsStale);
        Equal(1, scenario.ReadRefreshState().Failures);
        Equal(clock.GetUtcNow().AddMinutes(30), scenario.ReadRefreshState().NextAttemptAt);
        clock.Advance(TimeSpan.FromMinutes(29));
        await provider.ReadAsync();
        Equal(2, (await File.ReadAllLinesAsync(scenario.Path + ".requests")).Length);
        clock.Advance(TimeSpan.FromMinutes(2));
        await provider.ReadAsync();
        Equal(2, scenario.ReadRefreshState().Failures);
        Equal(clock.GetUtcNow().AddHours(1), scenario.ReadRefreshState().NextAttemptAt);
    }

    private static async Task CacheDuringCooldownAsync()
    {
        using var scenario = new Scenario("cached-report");
        var clock = new TestClock(DateTimeOffset.UtcNow);
        scenario.WriteCache(clock.GetUtcNow().AddMinutes(-20));
        await using var provider = scenario.CreateProvider(clock: clock);
        await provider.ReadAsync();
        clock.Advance(TimeSpan.FromMinutes(2));
        scenario.WriteCache(clock.GetUtcNow());
        var snapshot = await provider.ReadAsync();
        True(!snapshot.IsStale);
        Equal(clock.GetUtcNow().ToUnixTimeMilliseconds(), snapshot.ObservedAt.ToUnixTimeMilliseconds());
        Equal(2, (await File.ReadAllLinesAsync(scenario.Path + ".requests")).Length);
    }

    private static async Task StateWriteFailureAsync()
    {
        using var scenario = new Scenario("weekly");
        await File.WriteAllTextAsync(scenario.Path + ".refresh", "blocked");
        await using var provider = scenario.CreateProvider();
        var snapshot = await provider.ReadAsync();
        True(snapshot.IsStale);
        True(!File.Exists(scenario.Path + ".requests"));
    }

    private static async Task RestartWithoutCacheAsync()
    {
        using var scenario = new Scenario("cached-report");
        await using (var provider = scenario.CreateProvider())
        {
            var snapshot = await provider.ReadAsync();
            True(snapshot.IsStale);
            Equal(DateTimeOffset.MinValue, snapshot.ObservedAt);
        }
        await using (var provider = scenario.CreateProvider())
        {
            var snapshot = await provider.ReadAsync();
            True(snapshot.IsStale);
            Equal(0, snapshot.Windows.Count);
        }
        Equal(2, (await File.ReadAllLinesAsync(scenario.Path + ".requests")).Length);
    }

    private static async Task SuccessIntervalAsync()
    {
        using var scenario = new Scenario("weekly");
        await using var provider = scenario.CreateProvider();
        var first = await provider.ReadAsync();
        True(!first.IsStale);
        Equal(0, scenario.ReadRefreshState().Failures);
        True(scenario.ReadRefreshState().NextAttemptAt > DateTimeOffset.UtcNow.AddMinutes(9));
        File.Delete(scenario.Path + ".cache");
        await provider.ReadAsync();
        Equal(2, (await File.ReadAllLinesAsync(scenario.Path + ".requests")).Length);
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan elapsed) => now += elapsed;
    }

    private static async Task CachedAccountSwitchAsync()
    {
        using var scenario = new Scenario("switch");
        scenario.WriteCache(DateTimeOffset.UtcNow.AddMinutes(-1));
        await using var provider = scenario.CreateProvider();
        Equal(20d, (await provider.ReadAsync()).Windows.Single().UsedPercent);
        Equal(2, (await File.ReadAllLinesAsync(scenario.Path + ".requests")).Length);
    }

    private static async Task RecoveryAsync()
    {
        using var scenario = new Scenario("cached-report");
        scenario.WriteCache(DateTimeOffset.UtcNow.AddMinutes(-20));
        await using var provider = scenario.CreateProvider();
        True((await provider.ReadAsync()).IsStale);
        var path = Directory.GetFiles(scenario.Path + ".refresh").Single();
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(scenario.ReadRefreshState() with { NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1) }));
        Environment.SetEnvironmentVariable("USAGE_MONITOR_CLAUDE_SCENARIO", "weekly");
        True(!(await provider.ReadAsync()).IsStale);
        Equal(0, scenario.ReadRefreshState().Failures);
    }

    private static async Task CorruptStateAsync()
    {
        using var scenario = new Scenario("cached-report");
        await using var provider = scenario.CreateProvider();
        await provider.ReadAsync();
        var path = Directory.GetFiles(scenario.Path + ".refresh").Single();
        foreach (var json in new[] { "{", "null", "{}" })
        {
            await File.WriteAllTextAsync(path, json);
            True((await provider.ReadAsync()).IsStale);
        }
        Equal(2, (await File.ReadAllLinesAsync(scenario.Path + ".requests")).Length);
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

    private static IReadOnlyList<UsageWindow> ParseUsageReport(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ClaudeResponseParser.ReadUsageReport(document.RootElement);
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
            Environment.SetEnvironmentVariable("USAGE_MONITOR_CLAUDE_CACHE", Path + ".cache");
        }

        internal ClaudeUsageProvider CreateProvider(TimeSpan timeout = default) => CreateProvider(TimeProvider.System, timeout);

        internal ClaudeUsageProvider CreateProvider(TimeProvider clock, TimeSpan timeout = default) => new(
            Environment.ProcessPath ?? "", timeout == default ? TimeSpan.FromSeconds(30) : timeout,
            Path + ".cache", Path + ".refresh", clock);

        internal void WriteCache(DateTimeOffset observedAt)
        {
            File.WriteAllText(Path + ".cache", JsonSerializer.Serialize(new
            {
                oauthAccount = new { accountUuid = "account-id", emailAddress = "test@example.invalid", organizationUuid = "old-org" },
                cachedUsageUtilization = new
                {
                    accountUuid = "account-id", fetchedAtMs = observedAt.ToUnixTimeMilliseconds(),
                    utilization = new { seven_day = new { utilization = 17, resets_at = "2026-10-27T02:00:00Z" } }
                }
            }));
        }

        internal ClaudeRefreshState ReadRefreshState() => JsonSerializer.Deserialize<ClaudeRefreshState>(
            File.ReadAllText(Directory.GetFiles(Path + ".refresh").Single())) ?? new();

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
            Environment.SetEnvironmentVariable("USAGE_MONITOR_CLAUDE_CACHE", "");
            if (Directory.Exists(Path + ".refresh"))
            {
                foreach (var file in Directory.GetFiles(Path + ".refresh"))
                    File.Delete(file);
                Directory.Delete(Path + ".refresh");
            }
            else
                File.Delete(Path + ".refresh");
            foreach (var suffix in new[] { "", ".commands", ".requests", ".pid", ".eof", ".eof-complete", ".cache" })
            {
                File.Delete(Path + suffix);
            }
        }
    }
}
