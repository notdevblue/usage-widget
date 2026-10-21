using System;
using System.Threading;
using UsageMonitor.Core;
using UsageMonitor.Presentation;
using UsageMonitor.Services;

namespace UsageMonitor.Providers;

internal sealed class ProviderRuntime(string id, string name)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public IUsageProvider Provider { get; set; } = new UnavailableUsageProvider(id, name);
    public ExecutableLocation Location { get; set; } = new();
    public UsageSnapshot Snapshot { get; set; } = new() { ProviderId = id, ProviderName = name };
    public WidgetViewModel View { get; } = new()
    {
        ProviderId = id, ProviderName = name, EmptyTitle = "연결 중",
        EmptyBody = $"{name} 계정을 확인하고 있어요."
    };
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public LoginAttempt Login { get; set; } = new();
    public EventHandler StateChanged { get; set; } = delegate { };
    public bool LoginPending { get; set; }
    public bool CancelLoginRequested { get; set; }
    public bool HasSnapshot { get; set; }
    public bool Refreshing { get; set; }
    public bool Transitioning { get; set; }
    public int Generation { get; set; }
    public int Failures { get; set; }
    public DateTimeOffset NextRefreshAt { get; set; } = DateTimeOffset.MinValue;
}
