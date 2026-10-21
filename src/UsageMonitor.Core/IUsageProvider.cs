namespace UsageMonitor.Core;

public interface IUsageProvider : IAsyncDisposable
{
    string Id { get; }
    string DisplayName { get; }
    Task<UsageSnapshot> ReadAsync(CancellationToken ct = default);
    Task<LoginAttempt> BeginLoginAsync(CancellationToken ct = default);
    Task CancelLoginAsync(string loginId, CancellationToken ct = default);
}
