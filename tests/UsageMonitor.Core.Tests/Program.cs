using System.Diagnostics;
using System.Text.Json;
using UsageMonitor.Core;

namespace UsageMonitor.Core.Tests;

internal static class Program
{
    private static readonly string ExecutablePath = Environment.ProcessPath ?? "";

    private static async Task<int> Main(string[] args)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USAGE_MONITOR_CLAUDE_SCENARIO")))
        {
            return await FakeClaudeCli.RunAsync(args);
        }

        if (args.Length == 2 && args[0] == "app-server" && args[1] == "--stdio")
        {
            return await FakeAppServer.RunAsync();
        }

        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Weekly primary has no invented five-hour window", WeeklyPrimaryAsync),
            ("Five-hour and weekly windows retain durations", BothWindowsAsync),
            ("Multiple buckets use canonical ordering without duplicated fallback", MultipleBucketsAsync),
            ("Unknown and malformed data remain safe and honest", UnknownDataAsync),
            ("Account kinds handle absent and external authentication", AccountKindsAsync),
            ("Read-only app-server handshake and quota contract", ProviderReadAsync),
            ("Signed-out and API key accounts never request subscription quota", NonSubscriptionReadAsync),
            ("Account change cannot return the old account quota", AccountSwitchAsync),
            ("Account notification invalidates in-flight quota", AccountRevisionAsync),
            ("Login completion arriving before the response is retained", EarlyLoginAsync),
            ("Login completion arriving after the response is retained", NormalLoginAsync),
            ("Login cancel finishes the pending completion", CancelLoginAsync),
            ("Cancellation stays canceled when the server notification wins the race", CancelNotificationAsync),
            ("Canceled request does not poison the next request", RequestCancellationAsync),
            ("Concurrent out-of-order responses route by request id", ConcurrentResponsesAsync),
            ("Process exit faults requests without exposing stderr", ProcessExitAsync),
            ("Disposal terminates the owned child and pending request", ProcessDisposalAsync),
            ("Provider disposal interrupts an unresponsive request", ProviderDisposalAsync)
        };

        var failed = 0;
        var allTests = tests.Concat(ClaudeTests.All).ToArray();
        foreach (var test in allTests)
        {
            try
            {
                await test.Run().WaitAsync(TimeSpan.FromSeconds(15));
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception error)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {test.Name}: {error.GetType().Name}: {error.Message}");
            }
        }

        Console.WriteLine($"{allTests.Length - failed}/{allTests.Length} tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static Task WeeklyPrimaryAsync()
    {
        var windows = ParseWindows("""
            {"rateLimits":{"limitId":"codex","primary":{"usedPercent":13,"windowDurationMins":10080,"resetsAt":1800000000},"secondary":null}}
            """);
        Equal(1, windows.Count);
        Equal("주간", windows[0].Label);
        Equal(10080, windows[0].WindowMinutes);
        True(!windows[0].IsFiveHour);
        Equal(87d, windows[0].RemainingPercent);
        Equal(DateTimeOffset.FromUnixTimeSeconds(1800000000), windows[0].ResetsAt);
        return Task.CompletedTask;
    }

    private static Task BothWindowsAsync()
    {
        var windows = ParseWindows("""
            {"rateLimits":{"primary":{"usedPercent":25,"windowDurationMins":300},"secondary":{"usedPercent":70,"windowDurationMins":10080}}}
            """);
        Equal(2, windows.Count);
        True(windows[0].IsFiveHour);
        Equal("5시간", windows[0].Label);
        True(!windows[1].IsFiveHour);
        return Task.CompletedTask;
    }

    private static Task MultipleBucketsAsync()
    {
        var windows = ParseWindows("""
            {
              "rateLimits":{"limitId":"codex","primary":{"usedPercent":9,"windowDurationMins":300}},
              "rateLimitsByLimitId":{
                "research":{"limitId":"research","limitName":"Research","primary":{"usedPercent":66,"windowDurationMins":1440}},
                "codex":{"limitId":"codex","primary":{"usedPercent":9,"windowDurationMins":300}},
                "duplicate":{"limitId":"codex","primary":{"usedPercent":9,"windowDurationMins":300}},
                "empty":null
              }
            }
            """);
        Equal(2, windows.Count);
        Equal("codex:primary", windows[0].Id);
        Equal("Research · 1일", windows[1].Label);
        return Task.CompletedTask;
    }

    private static Task UnknownDataAsync()
    {
        var windows = ParseWindows("""
            {"rateLimitsByLimitId":{},"rateLimits":{"primary":{"usedPercent":135,"windowDurationMins":null,"resetsAt":999999999999999999},"secondary":{"usedPercent":"unknown","windowDurationMins":300}}}
            """);
        Equal(1, windows.Count);
        Equal("사용량", windows[0].Label);
        Equal(0, windows[0].WindowMinutes);
        Equal(100d, windows[0].UsedPercent);
        Equal(DateTimeOffset.MinValue, windows[0].ResetsAt);
        Equal(0, ParseWindows("{}").Count);
        Equal(0, ParseWindows("null").Count);
        var secondary = ParseWindows("""
            {"rateLimits":{"primary":null,"secondary":{"usedPercent":-5,"windowDurationMins":45}}}
            """);
        Equal(1, secondary.Count);
        Equal(0d, secondary[0].UsedPercent);
        Equal("45분", secondary[0].Label);
        return Task.CompletedTask;
    }

    private static Task AccountKindsAsync()
    {
        Equal(AccountKind.SignedOut, ParseAccount("{\"account\":null}").Kind);
        Equal(AccountKind.SignedOut, ParseAccount("{}").Kind);
        Equal(AccountKind.ApiKey, ParseAccount("{\"account\":{\"type\":\"apiKey\"}}").Kind);
        Equal(AccountKind.Other, ParseAccount("{\"account\":{\"type\":\"amazonBedrock\"}}").Kind);
        var account = ParseAccount("{\"account\":{\"type\":\"chatgpt\",\"email\":null,\"planType\":\"pro\"}}");
        Equal(AccountKind.ChatGpt, account.Kind);
        Equal("", account.Email);
        Equal("pro", account.Plan);
        return Task.CompletedTask;
    }

    private static async Task ProviderReadAsync()
    {
        SetScenario("weekly");
        await using var provider = new CodexUsageProvider(ExecutablePath);
        var snapshot = await provider.ReadAsync();
        Equal("Codex", snapshot.ProviderName);
        Equal(AccountKind.ChatGpt, snapshot.Account.Kind);
        Equal("test@example.invalid", snapshot.Account.Email);
        Equal(1, snapshot.Windows.Count);
        Equal(10080, snapshot.Windows[0].WindowMinutes);
        True(snapshot.ObservedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    private static async Task NonSubscriptionReadAsync()
    {
        foreach (var scenario in new[] { "signedout", "apikey" })
        {
            SetScenario(scenario);
            await using var provider = new CodexUsageProvider(ExecutablePath);
            var snapshot = await provider.ReadAsync();
            Equal(scenario == "signedout" ? AccountKind.SignedOut : AccountKind.ApiKey, snapshot.Account.Kind);
            Equal(0, snapshot.Windows.Count);
        }
    }

    private static async Task AccountSwitchAsync()
    {
        SetScenario("account-switch");
        await using var provider = new CodexUsageProvider(ExecutablePath);
        var snapshot = await provider.ReadAsync();
        Equal("new@example.invalid", snapshot.Account.Email);
        Equal(20d, snapshot.Windows.Single().UsedPercent);
    }

    private static async Task AccountRevisionAsync()
    {
        SetScenario("account-revision");
        await using var provider = new CodexUsageProvider(ExecutablePath);
        var notifications = 0;
        provider.StateChanged += (_, _) => Interlocked.Increment(ref notifications);
        var snapshot = await provider.ReadAsync();
        Equal(20d, snapshot.Windows.Single().UsedPercent);
        Equal(1, notifications);
    }

    private static async Task EarlyLoginAsync()
    {
        SetScenario("login-early");
        await using var provider = new CodexUsageProvider(ExecutablePath);
        var login = await provider.BeginLoginAsync();
        Equal("login-test", login.Id);
        True(login.AuthorizationUrl.StartsWith("https://", StringComparison.Ordinal));
        await login.Completion.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static async Task NormalLoginAsync()
    {
        SetScenario("login-normal");
        await using var provider = new CodexUsageProvider(ExecutablePath);
        var login = await provider.BeginLoginAsync();
        var snapshot = await provider.ReadAsync();
        Equal(1, snapshot.Windows.Count);
        await login.Completion.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static async Task CancelLoginAsync()
    {
        SetScenario("login-cancel");
        await using var provider = new CodexUsageProvider(ExecutablePath);
        var login = await provider.BeginLoginAsync();
        True(!login.Completion.IsCompleted);
        await provider.CancelLoginAsync(login.Id);
        await ThrowsAsync<OperationCanceledException>(async () => await login.Completion);
    }

    private static async Task CancelNotificationAsync()
    {
        SetScenario("login-cancel-notification-first");
        await using var provider = new CodexUsageProvider(ExecutablePath);
        var login = await provider.BeginLoginAsync();
        await provider.CancelLoginAsync(login.Id);
        await ThrowsAsync<OperationCanceledException>(async () => await login.Completion);
    }

    private static async Task RequestCancellationAsync()
    {
        SetScenario("slow-first-usage");
        await using var provider = new CodexUsageProvider(ExecutablePath);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await ThrowsAsync<OperationCanceledException>(() => provider.ReadAsync(cancellation.Token));
        var snapshot = await provider.ReadAsync();
        Equal(20d, snapshot.Windows.Single().UsedPercent);
    }

    private static async Task ConcurrentResponsesAsync()
    {
        SetScenario("weekly");
        await using var transport = new CodexAppServerTransport(ExecutablePath);
        var first = transport.RequestAsync("test/echo", new { value = "first", delay = 150 }, CancellationToken.None);
        var second = transport.RequestAsync("test/echo", new { value = "second", delay = 10 }, CancellationToken.None);
        await Task.WhenAll(first, second);
        Equal("first", first.Result.GetProperty("value").GetString() ?? "");
        Equal("second", second.Result.GetProperty("value").GetString() ?? "");
    }

    private static async Task ProcessExitAsync()
    {
        SetScenario("exit-on-read");
        await using var provider = new CodexUsageProvider(ExecutablePath);
        var error = await ThrowsAsync<CodexConnectionException>(() => provider.ReadAsync());
        True(!error.Message.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task ProcessDisposalAsync()
    {
        SetScenario("hang-on-read");
        var pidFile = Path.Combine(Path.GetTempPath(), $"usage-monitor-test-{Guid.NewGuid():N}.pid");
        Environment.SetEnvironmentVariable("USAGE_MONITOR_TEST_PID_FILE", pidFile);
        var transport = new CodexAppServerTransport(ExecutablePath);
        try
        {
            var request = transport.RequestAsync("account/read", new { refreshToken = false }, CancellationToken.None);
            for (var attempt = 0; attempt < 100 && !File.Exists(pidFile); attempt++)
            {
                await Task.Delay(20);
            }

            var pid = int.Parse(await File.ReadAllTextAsync(pidFile));
            await transport.DisposeAsync();
            await ThrowsAsync<CodexConnectionException>(async () => await request);
            True(!IsProcessRunning(pid));
        }
        finally
        {
            await transport.DisposeAsync();
            Environment.SetEnvironmentVariable("USAGE_MONITOR_TEST_PID_FILE", "");
            File.Delete(pidFile);
        }
    }

    private static async Task ProviderDisposalAsync()
    {
        SetScenario("hang-on-read");
        var provider = new CodexUsageProvider(ExecutablePath);
        var request = provider.ReadAsync();
        await Task.Delay(100);
        await provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        await ThrowsAsync<OperationCanceledException>(async () => await request);
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
        return CodexResponseParser.ReadWindows(document.RootElement);
    }

    private static UsageAccount ParseAccount(string json)
    {
        using var document = JsonDocument.Parse(json);
        return CodexResponseParser.ReadAccount(document.RootElement);
    }

    private static void SetScenario(string scenario)
    {
        Environment.SetEnvironmentVariable("USAGE_MONITOR_TEST_SCENARIO", scenario);
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
}
