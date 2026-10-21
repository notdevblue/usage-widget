using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using UsageMonitor.Core;
using UsageMonitor.Layout;
using UsageMonitor.Services;

namespace UsageMonitor.Presentation.Tests;

internal static class ProviderRuntimeTests
{
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        SlowProviderDoesNotDelayAnother();
        EarlyRefreshTimerTickKeepsClaudePolling();
        EmptyRefreshBatchKeepsClaudePolling();
        FailurePreservesOtherProvider();
        RetiredGenerationCannotPublish(false);
        RetiredGenerationCannotPublish(true);
        MissingCustomQuotaKeepsStatusRow();
        ClaudeSummaryKeepsModelDetails();
        Console.WriteLine($"PASS {assertions} provider runtime assertions");
    }

    private static void SlowProviderDoesNotDelayAnother()
    {
        using var fixture = new Fixture();
        var release = new TaskCompletionSource<UsageSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var codex = new ControlledProvider("codex", ct => release.Task.WaitAsync(ct));
        var claude = new ControlledProvider("claude", _ => Task.FromResult(Snapshot("claude", 42)));
        fixture.Install("codex", codex);
        fixture.Install("claude", claude);
        SetProperty(fixture.Runtime("codex"), "NextRefreshAt", DateTimeOffset.UtcNow.AddSeconds(-1));
        SetProperty(fixture.Runtime("claude"), "NextRefreshAt", DateTimeOffset.UtcNow.AddMilliseconds(150));
        var firstRead = fixture.InvokeTask("RefreshAsync", false);
        Check(codex.ReadCount == 1 && claude.ReadCount == 0, "Only the due Codex request should start immediately.");
        PumpUntil(() => claude.ReadCount == 1, TimeSpan.FromSeconds(2), "Claude must refresh on its own timer while Codex remains pending.");
        Check(!firstRead.IsCompleted, "The slow Codex request must still be outstanding when Claude refreshes.");
        Check(GetProperty<bool>(fixture.Runtime("claude"), "HasSnapshot"), "Claude must publish its result without waiting for Codex.");
        release.SetResult(Snapshot("codex", 73));
        PumpTask(firstRead);
        Check(GetProperty<bool>(fixture.Runtime("codex"), "HasSnapshot"), "Codex must still publish when its delayed request completes.");
    }

    private static void EarlyRefreshTimerTickKeepsClaudePolling()
    {
        using var fixture = new Fixture();
        var codex = new ControlledProvider("codex", _ => Task.FromResult(Snapshot("codex", 73)));
        var claude = new ControlledProvider("claude", _ => Task.FromResult(Snapshot("claude", 42)));
        fixture.Install("codex", codex);
        fixture.Install("claude", claude);
        var claudeRuntime = fixture.Runtime("claude");
        SetProperty(claudeRuntime, "NextRefreshAt", DateTimeOffset.UtcNow.AddMilliseconds(250));
        var timer = GetField<DispatcherTimer>(fixture.Window, "_refreshTimer");
        var earlyTickObserved = false;
        var tickWasEarly = false;
        var timerWasRearmed = false;
        var readsBeforeDue = -1;
        timer.Tick += (_, _) =>
        {
            if (earlyTickObserved)
                return;
            earlyTickObserved = true;
            tickWasEarly = GetProperty<DateTimeOffset>(claudeRuntime, "NextRefreshAt") > DateTimeOffset.UtcNow;
            timerWasRearmed = timer.IsEnabled;
            readsBeforeDue = codex.ReadCount + claude.ReadCount;
        };
        timer.Interval = TimeSpan.FromMilliseconds(30);
        timer.Start();
        PumpUntil(() => earlyTickObserved, TimeSpan.FromSeconds(2), "The refresh timer must execute its early tick.");
        Check(tickWasEarly && readsBeforeDue == 0, "An early timer tick must leave both providers unread until their deadlines.");
        Check(timerWasRearmed, "An early timer tick with no due providers must rearm the refresh timer.");
        PumpUntil(() => claude.ReadCount == 1, TimeSpan.FromSeconds(2), "Claude must refresh automatically after an early empty timer tick.");
        Check(View(claudeRuntime).Metrics.Single().Amount == "42", "Claude's timer-driven refresh must publish the updated quota.");
        Check(codex.ReadCount == 0, "Claude's deadline must not trigger an early Codex read.");
    }

    private static void EmptyRefreshBatchKeepsClaudePolling()
    {
        using var fixture = new Fixture();
        var remaining = 64d;
        var codex = new ControlledProvider("codex", _ => Task.FromResult(Snapshot("codex", 73)));
        var claude = new ControlledProvider("claude", _ => Task.FromResult(Snapshot("claude", remaining)));
        fixture.Install("codex", codex);
        fixture.Install("claude", claude);
        var claudeRuntime = fixture.Runtime("claude");
        SetProperty(claudeRuntime, "NextRefreshAt", DateTimeOffset.UtcNow.AddMilliseconds(250));
        PumpTask(fixture.InvokeTask("RefreshAsync", false));
        Check(codex.ReadCount == 0 && claude.ReadCount == 0, "A refresh batch with future deadlines must not read either provider.");
        Check(GetField<DispatcherTimer>(fixture.Window, "_refreshTimer").IsEnabled, "An empty refresh batch must keep a timer armed for the next deadline.");
        PumpUntil(() => claude.ReadCount == 1, TimeSpan.FromSeconds(2), "Claude must refresh automatically after an empty refresh batch.");
        Check(View(claudeRuntime).Metrics.Single().Amount == "64", "The first timer-driven Claude result must update the displayed quota.");
        remaining = 31;
        SetProperty(claudeRuntime, "NextRefreshAt", DateTimeOffset.UtcNow.AddMilliseconds(150));
        fixture.Invoke("ScheduleNext");
        PumpUntil(() => claude.ReadCount == 2, TimeSpan.FromSeconds(2), "Claude must continue refreshing on later timer deadlines.");
        Check(View(claudeRuntime).Metrics.Single().Amount == "31", "A later timer-driven Claude result must replace the displayed quota.");
        Check(codex.ReadCount == 0, "Repeated Claude refreshes must leave a future Codex deadline unread.");
        Check(GetField<DispatcherTimer>(fixture.Window, "_refreshTimer").IsEnabled, "Repeated timer refreshes must keep the next refresh scheduled.");
    }

    private static void FailurePreservesOtherProvider()
    {
        using var fixture = new Fixture();
        var failClaude = false;
        var codex = new ControlledProvider("codex", _ => Task.FromResult(Snapshot("codex", 81)));
        var claude = new ControlledProvider("claude", _ => failClaude
            ? Task.FromException<UsageSnapshot>(new InvalidOperationException("controlled Claude failure"))
            : Task.FromResult(Snapshot("claude", 34)));
        fixture.Install("codex", codex);
        fixture.Install("claude", claude);
        PumpTask(fixture.InvokeTask("RefreshAsync", true));
        var codexRuntime = fixture.Runtime("codex");
        var claudeRuntime = fixture.Runtime("claude");
        var codexSnapshot = GetProperty<UsageSnapshot>(codexRuntime, "Snapshot");
        var claudeSnapshot = GetProperty<UsageSnapshot>(claudeRuntime, "Snapshot");
        failClaude = true;
        PumpTask(fixture.InvokeTask("RefreshProviderAsync", claudeRuntime));
        Check(codex.ReadCount == 1 && claude.ReadCount == 2, "A Claude retry must remain provider-scoped.");
        Check(ReferenceEquals(codexSnapshot, GetProperty<UsageSnapshot>(codexRuntime, "Snapshot")), "Claude failure must preserve the Codex snapshot.");
        Check(!View(codexRuntime).Stale && View(codexRuntime).Metrics.Single().Amount == "81", "Claude failure must leave Codex metrics fresh and unchanged.");
        Check(ReferenceEquals(claudeSnapshot, GetProperty<UsageSnapshot>(claudeRuntime, "Snapshot")), "Claude failure must retain its own last snapshot.");
        Check(View(claudeRuntime).Stale && View(claudeRuntime).Metrics.Single().Amount == "34", "Only Claude's retained metrics should become stale.");
        Check(GetProperty<int>(claudeRuntime, "Failures") == 1 && View(claudeRuntime).Detail.Length > 0, "Claude's failure must schedule its own backoff and feedback.");
    }

    private static void RetiredGenerationCannotPublish(bool fail)
    {
        using var fixture = new Fixture();
        var release = new TaskCompletionSource<UsageSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ControlledProvider("codex", ct => release.Task.WaitAsync(ct));
        fixture.Install("codex", provider);
        var runtime = fixture.Runtime("codex");
        var generation = GetProperty<int>(runtime, "Generation");
        var pending = fixture.InvokeTask("RefreshProviderAsync", runtime);
        Check(provider.ReadCount == 1 && !pending.IsCompleted, "A controlled request must be in flight before the account switch.");
        fixture.Invoke("ResetConnectionPresentation", runtime);
        Check(GetProperty<int>(runtime, "Generation") == generation + 1, "Resetting an account must retire its in-flight generation.");
        if (fail)
            release.SetException(new InvalidOperationException("retired account failure"));
        else
            release.SetResult(Snapshot("codex", 91) with { Account = new() { Kind = AccountKind.ChatGpt, Email = "retired@example.invalid", Plan = "plus" } });
        PumpTask(pending);
        Check(!GetProperty<bool>(runtime, "HasSnapshot"), "A retired request must not restore the previous account snapshot.");
        Check(GetProperty<UsageSnapshot>(runtime, "Snapshot").Account.Email.Length == 0 && View(runtime).Metrics.Count == 0, "Retired results must not repopulate the previous account or quota.");
        Check(!View(runtime).Stale && GetProperty<int>(runtime, "Failures") == 0 && View(runtime).Detail.Length == 0, "Retired request failures must not mark the new account stale or start backoff.");
    }

    private static void MissingCustomQuotaKeepsStatusRow()
    {
        using var fixture = new Fixture();
        fixture.Install("codex", new("codex", _ => Task.FromResult(Snapshot("codex", 64))));
        fixture.Install("claude", new("claude", _ => Task.FromResult(Snapshot("claude", 0) with { Windows = [] })));
        var settings = GetField<AppSettings>(fixture.Window, "_settings");
        SetField(fixture.Window, "_settings", settings with
        {
            Layout = new()
            {
                Enabled = true,
                Blocks = [new() { ProviderId = "claude", Kind = LayoutBlockKind.Usage, Source = UsageWindowSource.Weekly, Height = 100 }]
            }
        });
        PumpTask(fixture.InvokeTask("RefreshAsync", true));
        fixture.Invoke("RenderSnapshots");
        var view = GetField<WidgetViewModel>(fixture.Window, "_view");
        Check(view.Providers.Any(provider => provider.ProviderId == "claude"), "An unrenderable custom Claude quota must retain Claude's automatic status row.");
        Check(view.Providers.Any(provider => provider.ProviderId == "codex" && provider.Metrics.Count == 1), "A custom Claude-only layout must leave Codex metrics visible.");
        Check(view.AutomaticLayoutVisible, "Missing custom quota data must not suppress the automatic provider rows.");
    }

    private static void ClaudeSummaryKeepsModelDetails()
    {
        using var fixture = new Fixture();
        fixture.Install("codex", new("codex", _ => Task.FromResult(Snapshot("codex", 64))));
        fixture.Install("claude", new("claude", _ => Task.FromResult(Snapshot("claude", 0) with
        {
            Windows =
            [
                new() { Id = "claude:five_hour:primary", Label = "5시간", WindowMinutes = 300, UsedPercent = 14 },
                new() { Id = "claude:weekly_all:primary", Label = "주간", WindowMinutes = 10080, UsedPercent = 28 },
                new() { Id = "claude:weekly_scoped:sonnet", Label = "Sonnet 주간", WindowMinutes = 10080, UsedPercent = 51 }
            ]
        })));
        PumpTask(fixture.InvokeTask("RefreshAsync", true));
        var view = View(fixture.Runtime("claude"));
        Check(view.Metrics.Count == 2 && view.Metrics.Select(metric => metric.Label).SequenceEqual(new[] { "5시간", "주간" }), "Claude's compact summary must show the shared five-hour and weekly limits only.");
        var context = (LayoutContext)fixture.Invoke("BuildLayoutContext");
        Check(context.ResolveProvider("claude").Windows.Count == 3, "Custom layouts must retain every Claude quota window.");
        Check(view.HoverText.Contains("Sonnet 주간", StringComparison.Ordinal) && view.HoverText.Contains("49%", StringComparison.Ordinal), "Claude's tooltip must retain the model-scoped quota and its remaining amount.");
    }

    private static WidgetViewModel View(object runtime) => GetProperty<WidgetViewModel>(runtime, "View");

    private static UsageSnapshot Snapshot(string id, double remaining) => new()
    {
        ProviderId = id,
        ProviderName = id == "claude" ? "Claude" : "Codex",
        Account = new() { Kind = id == "claude" ? AccountKind.Claude : AccountKind.ChatGpt, Email = id + "@example.invalid", Plan = id == "claude" ? "max" : "plus" },
        Windows = [new() { Id = "weekly", Label = "주간", UsedPercent = 100 - remaining, WindowMinutes = 10080, ResetsAt = DateTimeOffset.UtcNow.AddDays(2) }],
        ObservedAt = DateTimeOffset.UtcNow
    };

    private static void PumpTask(Task task)
    {
        PumpUntil(() => task.IsCompleted, TimeSpan.FromSeconds(3), "Provider operation did not finish within its bounded test timeout.");
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout, string message)
    {
        if (condition())
            return;
        var elapsed = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(5) };
        timer.Tick += (_, _) =>
        {
            if (condition() || elapsed.Elapsed >= timeout)
                frame.Continue = false;
        };
        try
        {
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timer.Stop();
        }
        Check(condition(), message);
    }

    private static T GetProperty<T>(object target, string name) => (T)(Property(target, name).GetValue(target)
        ?? throw new InvalidOperationException($"Missing runtime property value: {name}"));

    private static void SetProperty(object target, string name, object value) => Property(target, name).SetValue(target, value);

    private static PropertyInfo Property(object target, string name) => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing runtime property: {name}");

    private static T GetField<T>(MainWindow window, string name) => (T)(Field(name).GetValue(window)
        ?? throw new InvalidOperationException($"Missing window field value: {name}"));

    private static void SetField(MainWindow window, string name, object value) => Field(name).SetValue(window, value);

    private static FieldInfo Field(string name) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing window field: {name}");

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly object[] runtimes;

        internal MainWindow Window { get; } = new("weekly");

        internal Fixture()
        {
            runtimes = GetField<Array>(Window, "_providers").Cast<object>().ToArray();
            foreach (var runtime in runtimes)
                SetProperty(runtime, "NextRefreshAt", DateTimeOffset.UtcNow.AddMinutes(10));
            SetField(Window, "_ready", true);
        }

        internal object Runtime(string id) => runtimes.Single(runtime => GetProperty<string>(runtime, "Id") == id);

        internal void Install(string id, ControlledProvider provider)
        {
            var runtime = Runtime(id);
            GetProperty<IUsageProvider>(runtime, "Provider").DisposeAsync().GetAwaiter().GetResult();
            SetProperty(runtime, "Provider", provider);
            SetProperty(runtime, "Location", new ExecutableLocation { Found = true, Source = "Controlled test provider" });
        }

        internal object Invoke(string name, params object[] arguments)
        {
            var method = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Missing window method: {name}");
            return method.Invoke(Window, arguments) ?? new();
        }

        internal Task InvokeTask(string name, params object[] arguments) => (Task)Invoke(name, arguments);

        public void Dispose()
        {
            try
            {
                PumpTask(InvokeTask("StopAsync"));
            }
            finally
            {
                Window.Close();
            }
        }
    }

    private sealed class ControlledProvider(string id, Func<CancellationToken, Task<UsageSnapshot>> read) : IUsageProvider
    {
        public string Id { get; } = id;
        public string DisplayName => Id == "claude" ? "Claude" : "Codex";
        public int ReadCount { get; private set; }

        public Task<UsageSnapshot> ReadAsync(CancellationToken ct = default)
        {
            ReadCount++;
            return read(ct);
        }

        public Task<LoginAttempt> BeginLoginAsync(CancellationToken ct = default) => Task.FromResult(new LoginAttempt());
        public Task CancelLoginAsync(string loginId, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
