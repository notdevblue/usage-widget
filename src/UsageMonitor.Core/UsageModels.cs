namespace UsageMonitor.Core;

public enum AccountKind
{
    SignedOut,
    ChatGpt,
    Claude,
    Grok,
    ApiKey,
    Other
}

public sealed record UsageAccount
{
    public AccountKind Kind { get; init; } = AccountKind.SignedOut;
    public string Email { get; init; } = "";
    public string Plan { get; init; } = "";
}

public sealed record UsageWindow
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public double UsedPercent { get; init; }
    public int WindowMinutes { get; init; }
    public DateTimeOffset ResetsAt { get; init; } = DateTimeOffset.MinValue;
    public double RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
    public bool IsFiveHour => WindowMinutes == 300;
}

public sealed record UsageSnapshot
{
    public string ProviderId { get; init; } = "codex";
    public string ProviderName { get; init; } = "Codex";
    public UsageAccount Account { get; init; } = new();
    public IReadOnlyList<UsageWindow> Windows { get; init; } = [];
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.MinValue;
    public bool IsCached { get; init; }
    public bool IsStale { get; init; }
    public string StatusDetail { get; init; } = "";
}

public sealed record LoginAttempt
{
    public string Id { get; init; } = "";
    public string AuthorizationUrl { get; init; } = "";
    public Task Completion { get; init; } = Task.CompletedTask;
}
