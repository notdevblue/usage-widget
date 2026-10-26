using System.Text.Json;

namespace UsageMonitor.Core;

public sealed class GrokUsageProvider : IUsageProvider
{
    private readonly GrokProcessTransport transport;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private bool versionChecked;
    private int disposed;

    public string Id => "grok";
    public string DisplayName => "Grok";

    public GrokUsageProvider(string executablePath) : this(executablePath, TimeSpan.FromSeconds(30), TimeProvider.System)
    {
    }

    internal GrokUsageProvider(string executablePath, TimeSpan requestTimeout, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        transport = new(executablePath, requestTimeout);
        this.clock = clock;
    }

    public async Task<UsageSnapshot> ReadAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        ct = request.Token;
        await operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!versionChecked)
            {
                await transport.CheckVersionAsync(ct).ConfigureAwait(false);
                versionChecked = true;
            }

            try
            {
                var result = await transport.ReadBillingAsync(ct).ConfigureAwait(false);
                return GrokResponseParser.ReadBilling(result, clock.GetUtcNow());
            }
            catch (GrokAuthenticationRequiredException)
            {
                return SignedOut(clock.GetUtcNow());
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    public Task<LoginAttempt> BeginLoginAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        throw new GrokConnectionException("터미널에서 grok login으로 로그인하면 자동으로 감지합니다.");
    }

    public Task CancelLoginAsync(string loginId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        lifetime.Cancel();
        await operationGate.WaitAsync().ConfigureAwait(false);
        operationGate.Release();
    }

    private UsageSnapshot SignedOut(DateTimeOffset observedAt) => new()
    {
        ProviderId = Id,
        ProviderName = DisplayName,
        ObservedAt = observedAt
    };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
    }
}
