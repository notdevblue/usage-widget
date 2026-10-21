using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UsageMonitor.Core;

namespace UsageMonitor.Providers;

public sealed class UsageProviderCatalog
{
    private readonly Dictionary<string, Func<string, IUsageProvider>> _factories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["codex"] = path => new CodexUsageProvider(path),
        ["claude"] = path => new ClaudeUsageProvider(path)
    };

    public IUsageProvider Create(string id, string executablePath)
    {
        if (!_factories.TryGetValue(id, out var factory))
            throw new ArgumentException("지원하지 않는 사용량 제공자예요.", nameof(id));
        return factory(executablePath);
    }
}

public sealed class UnavailableUsageProvider(string id = "codex", string name = "Codex") : IUsageProvider
{
    public string Id => id;
    public string DisplayName => name;
    public Task<UsageSnapshot> ReadAsync(CancellationToken ct = default) => Task.FromResult(new UsageSnapshot { ProviderId = id, ProviderName = name });
    public Task<LoginAttempt> BeginLoginAsync(CancellationToken ct = default) => throw new InvalidOperationException($"{name} 실행 파일을 먼저 연결해주세요.");
    public Task CancelLoginAsync(string loginId, CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class PreviewUsageProvider(string scenario, string providerId = "codex") : IUsageProvider
{
    public string Id => providerId;
    public string DisplayName => providerId == "claude" ? "Claude" : "Codex";

    public Task<UsageSnapshot> ReadAsync(CancellationToken ct = default)
    {
        var windows = new List<UsageWindow>();
        if (scenario is "dual" or "low" || providerId == "claude")
            windows.Add(new UsageWindow { Id = "5h", Label = "5시간", UsedPercent = scenario == "low" ? 96 : 28, WindowMinutes = 300, ResetsAt = DateTimeOffset.Now.AddHours(3).AddMinutes(24) });
        if (scenario is not ("signed-out" or "api-key" or "missing"))
            windows.Add(new UsageWindow { Id = "weekly", Label = "주간", UsedPercent = 36, WindowMinutes = 10080, ResetsAt = DateTimeOffset.Now.AddDays(4).AddHours(8) });
        var kind = scenario == "signed-out" ? AccountKind.SignedOut : scenario == "api-key" ? AccountKind.ApiKey : providerId == "claude" ? AccountKind.Claude : AccountKind.ChatGpt;
        return Task.FromResult(new UsageSnapshot
        {
            ProviderId = Id, ProviderName = DisplayName,
            Account = new UsageAccount { Kind = kind, Email = "preview@example.com", Plan = scenario == "dual" ? "plus" : "pro" },
            Windows = windows,
            ObservedAt = DateTimeOffset.Now
        });
    }

    public Task<LoginAttempt> BeginLoginAsync(CancellationToken ct = default) => throw new InvalidOperationException("미리보기에서는 로그인할 수 없어요.");
    public Task CancelLoginAsync(string loginId, CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
