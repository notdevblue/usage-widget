namespace UsageMonitor.Core;

public sealed class ClaudeUsageProvider : IUsageProvider
{
    private readonly ClaudeProcessTransport transport;
    private readonly ClaudeUsageCache cache;
    private readonly ClaudeRefreshStateStore refreshStates;
    private readonly TimeProvider clock;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);
    private UsageSnapshot lastSnapshot = new();
    private string lastIdentity = "";
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private bool versionChecked;
    private int disposed;

    public string Id => "claude";
    public string DisplayName => "Claude";

    public ClaudeUsageProvider(string executablePath) : this(executablePath, TimeSpan.FromSeconds(30))
    {
    }

    internal ClaudeUsageProvider(string executablePath, TimeSpan requestTimeout)
        : this(executablePath, requestTimeout, ClaudeUsageCache.DefaultPath, ClaudeRefreshStateStore.DefaultDirectory, TimeProvider.System)
    {
    }

    internal ClaudeUsageProvider(string executablePath, TimeSpan requestTimeout, string cachePath, string stateDirectory, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        transport = new(executablePath, requestTimeout);
        cache = new(cachePath);
        refreshStates = new(stateDirectory);
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
                await CheckVersionAsync(ct).ConfigureAwait(false);
                versionChecked = true;
            }

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var authentication = await ReadAuthenticationAsync(ct).ConfigureAwait(false);
                if (authentication.Account.Kind != AccountKind.Claude)
                {
                    lastSnapshot = new();
                    lastIdentity = "";
                    return CreateSnapshot(authentication.Account, []);
                }

                var snapshot = await ReadAccountUsageAsync(authentication, ct).ConfigureAwait(false);

                var verified = await ReadAuthenticationAsync(ct).ConfigureAwait(false);
                if (authentication.Identity == verified.Identity)
                {
                    lastIdentity = authentication.Identity;
                    lastSnapshot = snapshot;
                    return snapshot;
                }
            }

            throw new ClaudeConnectionException("Claude 계정이 변경 중입니다. 다음 자동 조회에서 다시 확인합니다.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task<UsageSnapshot> ReadAccountUsageAsync(ClaudeAuthentication authentication, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hasCache = cache.TryGet(authentication, now, out var cached);
        if (hasCache && now - cached.ObservedAt < RefreshInterval)
            return cached;

        if (!hasCache && lastIdentity == authentication.Identity)
            cached = lastSnapshot;
        var state = refreshStates.Load(authentication.Identity, now);
        if (now < state.NextAttemptAt)
            return WaitingSnapshot(authentication.Account, cached, state.NextAttemptAt);

        state = new()
        {
            Failures = Math.Min(state.Failures + 1, 2),
            NextAttemptAt = now.AddMinutes(state.Failures == 0 ? 30 : 60)
        };
        if (!refreshStates.TrySave(authentication.Identity, state))
        {
            return WaitingSnapshot(authentication.Account, cached, state.NextAttemptAt) with
            {
                StatusDetail = "조회 대기 상태를 저장하지 못해 서버 조회를 보류했어요."
            };
        }

        try
        {
            var report = await transport.ReadUsageAsync(ct).ConfigureAwait(false);
            var windows = ClaudeResponseParser.ReadUsageReport(report);
            now = clock.GetUtcNow();
            if (cache.TryGet(authentication, now, out var refreshed) &&
                refreshed.ObservedAt > cached.ObservedAt && now - refreshed.ObservedAt < RefreshInterval)
            {
                refreshStates.TrySave(authentication.Identity, new() { NextAttemptAt = now + RefreshInterval });
                return refreshed;
            }

            if (cached.ObservedAt == DateTimeOffset.MinValue && cached.Windows.Count == 0)
                cached = CreateSnapshot(authentication.Account, windows) with { ObservedAt = DateTimeOffset.MinValue };
            return WaitingSnapshot(authentication.Account, cached, state.NextAttemptAt);
        }
        catch (Exception error) when (error is ClaudeConnectionException or TimeoutException)
        {
            if (cached.Windows.Count == 0)
                throw;
            return WaitingSnapshot(authentication.Account, cached, state.NextAttemptAt);
        }
    }

    private UsageSnapshot WaitingSnapshot(UsageAccount account, UsageSnapshot cached, DateTimeOffset nextAttemptAt)
    {
        return cached with
        {
            ProviderId = Id, ProviderName = DisplayName, Account = account, IsStale = true,
            StatusDetail = $"최신 갱신을 확인하지 못했어요. 서버 재조회: {nextAttemptAt.ToLocalTime():HH:mm} 이후"
        };
    }

    public Task<LoginAttempt> BeginLoginAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        throw new ClaudeConnectionException("터미널에서 claude auth login으로 로그인하면 자동으로 감지합니다.");
    }

    public Task CancelLoginAsync(string loginId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private async Task CheckVersionAsync(CancellationToken ct)
    {
        var result = await transport.RunAsync(["--version"], ct).ConfigureAwait(false);
        var versionText = result.Output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (result.ExitCode != 0 || !Version.TryParse(versionText, out var version) || version < new Version(2, 1, 273))
        {
            throw new ClaudeConnectionException("Claude Code 2.1.273 이상이 필요합니다. Claude Code를 업데이트해 주세요.");
        }
    }

    private async Task<ClaudeAuthentication> ReadAuthenticationAsync(CancellationToken ct)
    {
        var result = await transport.RunAsync(["auth", "status", "--json"], ct).ConfigureAwait(false);
        if (result.ExitCode is not (0 or 1))
        {
            throw new ClaudeConnectionException();
        }

        var authentication = ClaudeResponseParser.ReadAuthentication(result.Output);
        if (result.ExitCode == 1 && authentication.Account.Kind != AccountKind.SignedOut)
        {
            throw new ClaudeConnectionException();
        }

        return authentication;
    }

    private UsageSnapshot CreateSnapshot(UsageAccount account, IReadOnlyList<UsageWindow> windows)
    {
        return new()
        {
            ProviderId = Id,
            ProviderName = DisplayName,
            Account = account,
            Windows = windows,
            ObservedAt = clock.GetUtcNow()
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        await operationGate.WaitAsync().ConfigureAwait(false);
        operationGate.Release();
    }
}
