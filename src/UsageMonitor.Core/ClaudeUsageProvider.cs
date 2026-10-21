namespace UsageMonitor.Core;

public sealed class ClaudeUsageProvider : IUsageProvider
{
    private readonly ClaudeProcessTransport transport;
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        transport = new(executablePath, requestTimeout);
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
                    return CreateSnapshot(authentication.Account, []);
                }

                var result = await transport.RunAsync([
                    "--safe-mode", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}",
                    "--no-session-persistence", "--print", "--output-format", "stream-json", "--verbose", "/usage"
                ], ct).ConfigureAwait(false);
                if (result.ExitCode != 0)
                {
                    throw new ClaudeConnectionException();
                }

                var verified = await ReadAuthenticationAsync(ct).ConfigureAwait(false);
                if (authentication.Identity == verified.Identity)
                {
                    return CreateSnapshot(authentication.Account, ClaudeResponseParser.ReadStream(result.Output));
                }
            }

            throw new ClaudeConnectionException("Claude 계정이 변경 중입니다. 다음 자동 조회에서 다시 확인합니다.");
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
            ObservedAt = DateTimeOffset.UtcNow
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
