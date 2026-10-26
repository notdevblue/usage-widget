using System.Diagnostics;
using System.Text.Json;
using UsageMonitor.Core;

namespace UsageMonitor.Core.Tests;

internal static class GrokTests
{
    internal static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("Grok credits report keeps the weekly pool and plan", CreditsReportAsync),
        ("Grok omitted percentage is zero for an active period", OmittedPercentAsync),
        ("Grok legacy included usage stays a monthly window", LegacyReportAsync),
        ("Grok rejects malformed percentages and empty reports", InvalidReportAsync),
        ("Grok CLI reads billing without a session or shared leader", ProviderReadAsync),
        ("Grok authentication failure is signed out and hides secrets", AuthenticationAsync),
        ("Grok errors hide raw responses", HiddenErrorsAsync),
        ("Grok repeats the billing request on every read", RepeatedReadsAsync),
        ("Grok old CLI is rejected before billing", OldVersionAsync),
        ("Grok cancellation and disposal terminate the owned process", CancellationAsync),
        ("Grok login stays with the official CLI", LoginAsync)
    ];

    private static Task CreditsReportAsync()
    {
        var snapshot = Parse("""
            {"config":{"creditUsagePercent":25.0,"currentPeriod":{"type":"USAGE_PERIOD_TYPE_WEEKLY","start":"2026-10-24T02:24:13.840+00:00","end":"2026-10-31T02:24:13.840+00:00"},"prepaidBalance":{"val":0},"isUnifiedBillingUser":true},"subscription_tier":"SuperGrok"}
            """);
        Equal(AccountKind.Grok, snapshot.Account.Kind);
        Equal("SuperGrok", snapshot.Account.Plan);
        Equal("", snapshot.Account.Email);
        var window = snapshot.Windows.Single();
        Equal("grok:weekly", window.Id);
        Equal("주간", window.Label);
        Equal(25d, window.UsedPercent);
        Equal(75d, window.RemainingPercent);
        Equal(10080, window.WindowMinutes);
        Equal(DateTimeOffset.Parse("2026-10-31T02:24:13.840+00:00"), window.ResetsAt);
        return Task.CompletedTask;
    }

    private static Task OmittedPercentAsync()
    {
        var window = Parse("""
            {"config":{"currentPeriod":{"type":"USAGE_PERIOD_TYPE_MONTHLY","end":"2026-11-01T00:00:00Z"}},"subscriptionTier":"SuperGrok Heavy"}
            """).Windows.Single();
        Equal("grok:monthly", window.Id);
        Equal("월간", window.Label);
        Equal(0d, window.UsedPercent);
        Equal(43200, window.WindowMinutes);
        return Task.CompletedTask;
    }

    private static Task LegacyReportAsync()
    {
        var snapshot = Parse("""
            {"config":{"monthlyLimit":{"val":2000},"usage":{"totalUsed":{"val":500}},"billingPeriodStart":"2026-04-01T00:00:00Z","billingPeriodEnd":"2026-05-01T00:00:00Z"}}
            """);
        var window = snapshot.Windows.Single();
        Equal("grok:monthly", window.Id);
        Equal(25d, window.UsedPercent);
        Equal(43200, window.WindowMinutes);
        var clamped = Parse("""{"config":{"creditUsagePercent":130,"currentPeriod":{"type":"USAGE_PERIOD_TYPE_WEEKLY"}}}""").Windows.Single();
        Equal(100d, clamped.UsedPercent);
        Equal("주간", clamped.Label);
        var tierOnly = Parse("""{"subscription_tier":"SuperGrok","config":{"prepaidBalance":{"val":1446}}}""");
        Equal(0, tierOnly.Windows.Count);
        Equal("SuperGrok", tierOnly.Account.Plan);
        return Task.CompletedTask;
    }

    private static async Task InvalidReportAsync()
    {
        foreach (var json in new[] { "{}", """{"config":{"creditUsagePercent":"25"}}""", """{"config":{"creditUsagePercent":-1}}""", "null", "[]" })
            await ThrowsAsync<GrokConnectionException>(() => Task.FromResult(Parse(json)));
    }

    private static async Task ProviderReadAsync()
    {
        using var scenario = new Scenario("credits");
        await using var provider = scenario.CreateProvider();
        var snapshot = await provider.ReadAsync();
        Equal(25d, snapshot.Windows.Single().UsedPercent);
        Equal("SuperGrok", snapshot.Account.Plan);
        var requests = await File.ReadAllTextAsync(scenario.Path + ".requests");
        True(requests.Contains("\"method\":\"initialize\"", StringComparison.Ordinal));
        True(requests.Contains("\"method\":\"_x.ai/billing\"", StringComparison.Ordinal));
        True(!requests.Contains("session/new", StringComparison.Ordinal));
        True(!requests.Contains("session/prompt", StringComparison.Ordinal));
        var commands = await File.ReadAllLinesAsync(scenario.Path + ".commands");
        var billing = JsonSerializer.Deserialize<string[]>(commands.Single(line => line.Contains("stdio", StringComparison.Ordinal))) ?? [];
        Equal("agent", billing[0]);
        Equal("--no-leader", billing[1]);
        Equal("--leader-socket", billing[2]);
        True(billing[3].Contains("leader-", StringComparison.Ordinal) && billing[3].Contains("grok-query", StringComparison.OrdinalIgnoreCase));
        Equal("stdio", billing[4]);
        var cwd = await File.ReadAllTextAsync(scenario.Path + ".cwd");
        True(cwd.Contains("grok-query", StringComparison.OrdinalIgnoreCase));
        True(!IsProcessRunning(await scenario.ReadPidAsync()));
    }

    private static async Task AuthenticationAsync()
    {
        using var scenario = new Scenario("auth");
        await using var provider = scenario.CreateProvider();
        var snapshot = await provider.ReadAsync();
        Equal(AccountKind.SignedOut, snapshot.Account.Kind);
        Equal(0, snapshot.Windows.Count);
        True(!snapshot.ToString().Contains("secret", StringComparison.OrdinalIgnoreCase));
        var again = await provider.ReadAsync();
        Equal(AccountKind.SignedOut, again.Account.Kind);
        Equal(3, File.ReadAllLines(scenario.Path + ".commands").Length);
    }

    private static async Task HiddenErrorsAsync()
    {
        foreach (var name in new[] { "secret", "missing-method", "malformed" })
        {
            using var scenario = new Scenario(name);
            await using var provider = scenario.CreateProvider();
            var error = await ThrowsAsync<GrokConnectionException>(() => provider.ReadAsync());
            True(!error.ToString().Contains("secret", StringComparison.OrdinalIgnoreCase));
            if (name == "missing-method")
                True(error.Message.Contains("업데이트", StringComparison.Ordinal));
        }
    }

    private static async Task RepeatedReadsAsync()
    {
        using var scenario = new Scenario("credits");
        var clock = new ManualClock(DateTimeOffset.Parse("2026-10-26T00:00:00Z"));
        await using var provider = scenario.CreateProvider(clock);
        var first = await provider.ReadAsync();
        Equal(2, File.ReadAllLines(scenario.Path + ".commands").Length);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var second = await provider.ReadAsync();
        Equal(clock.UtcNow, second.ObservedAt);
        True(second.ObservedAt != first.ObservedAt && !second.IsStale);
        Equal(3, File.ReadAllLines(scenario.Path + ".commands").Length);
        Environment.SetEnvironmentVariable("USAGE_MONITOR_GROK_SCENARIO", "malformed");
        await ThrowsAsync<GrokConnectionException>(() => provider.ReadAsync());
        Equal(4, File.ReadAllLines(scenario.Path + ".commands").Length);
        Environment.SetEnvironmentVariable("USAGE_MONITOR_GROK_SCENARIO", "credits");
        var recovered = await provider.ReadAsync();
        True(!recovered.IsStale && recovered.Windows.Single().UsedPercent == 25);
        Equal(5, File.ReadAllLines(scenario.Path + ".commands").Length);
    }

    private static async Task OldVersionAsync()
    {
        using var scenario = new Scenario("old-version");
        await using var provider = scenario.CreateProvider();
        var error = await ThrowsAsync<GrokConnectionException>(() => provider.ReadAsync());
        True(error.Message.Contains("1.0", StringComparison.Ordinal));
        True(!File.Exists(scenario.Path + ".requests"));
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
                    await provider.DisposeAsync();
                else
                    cancellation.Cancel();
                await ThrowsAsync<OperationCanceledException>(async () => await pending);
                True(!IsProcessRunning(pid));
            }
            finally
            {
                await provider.DisposeAsync();
            }
        }
    }

    private static async Task LoginAsync()
    {
        using var scenario = new Scenario("credits");
        await using var provider = scenario.CreateProvider();
        var error = await ThrowsAsync<GrokConnectionException>(() => provider.BeginLoginAsync());
        True(error.Message.Contains("grok login", StringComparison.Ordinal));
        await provider.CancelLoginAsync("");
    }

    private static UsageSnapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return GrokResponseParser.ReadBilling(document.RootElement, DateTimeOffset.Parse("2026-10-26T00:00:00Z"));
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, received {actual}.");
    }

    private static void True(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Expected condition to hold.");
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

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        internal DateTimeOffset UtcNow { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class Scenario : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "usage-monitor-grok-" + Guid.NewGuid().ToString("N"));

        internal Scenario(string name)
        {
            Environment.SetEnvironmentVariable("USAGE_MONITOR_GROK_SCENARIO", name);
            Environment.SetEnvironmentVariable("USAGE_MONITOR_GROK_STATE", Path);
        }

        internal GrokUsageProvider CreateProvider(TimeProvider? clock = null, TimeSpan timeout = default) => new(
            Environment.ProcessPath ?? "", timeout == default ? TimeSpan.FromSeconds(30) : timeout, clock ?? TimeProvider.System);

        internal async Task<int> ReadPidAsync()
        {
            for (var attempt = 0; attempt < 150 && !File.Exists(Path + ".pid"); attempt++)
                await Task.Delay(20);
            return int.Parse(await File.ReadAllTextAsync(Path + ".pid"));
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("USAGE_MONITOR_GROK_SCENARIO", "");
            Environment.SetEnvironmentVariable("USAGE_MONITOR_GROK_STATE", "");
            foreach (var suffix in new[] { ".commands", ".requests", ".pid", ".cwd" })
                File.Delete(Path + suffix);
        }
    }
}
