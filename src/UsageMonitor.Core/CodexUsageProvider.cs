using System.Text.Json;

namespace UsageMonitor.Core;

public sealed class CodexUsageProvider : IUsageProvider
{
    private readonly string executablePath;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly object loginGate = new();
    private readonly Dictionary<string, TaskCompletionSource> logins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> earlyLoginResults = new(StringComparer.Ordinal);
    private CodexAppServerTransport transport;
    private long accountRevision;
    private int disposed;

    public string Id => "codex";
    public string DisplayName => "Codex";
    public event EventHandler StateChanged = delegate { };

    public CodexUsageProvider(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        this.executablePath = executablePath;
        transport = CreateTransport();
    }

    public async Task<UsageSnapshot> ReadAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        ct = requestLifetime.Token;
        await operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureTransportAsync().ConfigureAwait(false);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var revision = Interlocked.Read(ref accountRevision);
                var account = await ReadAccountAsync(ct).ConfigureAwait(false);
                if (account.Kind != AccountKind.ChatGpt)
                {
                    return CreateSnapshot(account, []);
                }

                var limits = await transport.RequestAsync("account/rateLimits/read", new
                {
                    excludeResetCreditDetails = true
                }, ct).ConfigureAwait(false);
                var verifiedAccount = await ReadAccountAsync(ct).ConfigureAwait(false);
                if (account == verifiedAccount && revision == Interlocked.Read(ref accountRevision))
                {
                    return CreateSnapshot(account, CodexResponseParser.ReadWindows(limits));
                }
            }

            throw new InvalidOperationException("Codex 계정이 변경 중입니다. 잠시 후 다시 시도해 주세요.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<LoginAttempt> BeginLoginAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        ct = requestLifetime.Token;
        await operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureTransportAsync().ConfigureAwait(false);
            lock (loginGate)
            {
                if (logins.Count > 0)
                {
                    throw new InvalidOperationException("진행 중인 로그인을 완료하거나 취소해 주세요.");
                }

                earlyLoginResults.Clear();
            }

            var result = await transport.RequestAsync("account/login/start", new { type = "chatgpt" }, ct).ConfigureAwait(false);
            var id = CodexResponseParser.ReadString(result, "loginId");
            var authorizationUrl = CodexResponseParser.ReadString(result, "authUrl");
            if (id.Length == 0 || !Uri.TryCreate(authorizationUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("Codex 로그인 주소를 확인하지 못했습니다.");
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (loginGate)
            {
                logins[id] = completion;
                if (earlyLoginResults.Remove(id, out var succeeded))
                {
                    CompleteLogin(id, succeeded);
                }
            }

            if (!transport.IsUsable)
            {
                FailLogins();
            }

            return new()
            {
                Id = id,
                AuthorizationUrl = authorizationUrl,
                Completion = completion.Task
            };
        }
        catch (OperationCanceledException)
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task CancelLoginAsync(string loginId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (loginId.Length == 0)
        {
            return;
        }

        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        ct = requestLifetime.Token;
        await operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            lock (loginGate)
            {
                if (logins.Remove(loginId, out var completion))
                {
                    completion.TrySetCanceled();
                }

                earlyLoginResults.Remove(loginId);
            }

            if (transport.IsUsable)
            {
                await transport.RequestAsync("account/login/cancel", new { loginId }, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task<UsageAccount> ReadAccountAsync(CancellationToken ct)
    {
        var result = await transport.RequestAsync("account/read", new { refreshToken = false }, ct).ConfigureAwait(false);
        return CodexResponseParser.ReadAccount(result);
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

    private CodexAppServerTransport CreateTransport()
    {
        var connection = new CodexAppServerTransport(executablePath);
        connection.NotificationReceived += HandleNotification;
        connection.ConnectionClosed += FailLogins;
        return connection;
    }

    private async Task EnsureTransportAsync()
    {
        ThrowIfDisposed();
        if (!transport.IsUsable)
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            transport = CreateTransport();
        }
    }

    private void HandleNotification(string method, JsonElement parameters)
    {
        if (method == "account/updated")
        {
            Interlocked.Increment(ref accountRevision);
            StateChanged(this, EventArgs.Empty);
            return;
        }

        if (method == "account/rateLimits/updated")
        {
            StateChanged(this, EventArgs.Empty);
            return;
        }

        if (method != "account/login/completed")
        {
            return;
        }

        var id = CodexResponseParser.ReadString(parameters, "loginId");
        if (id.Length > 0)
        {
            CompleteLogin(id, CodexResponseParser.ReadBoolean(parameters, "success"));
        }
    }

    private void CompleteLogin(string id, bool succeeded)
    {
        lock (loginGate)
        {
            if (!logins.Remove(id, out var completion))
            {
                earlyLoginResults[id] = succeeded;
                return;
            }

            if (succeeded)
            {
                completion.TrySetResult();
            }
            else
            {
                completion.TrySetException(new InvalidOperationException("Codex 로그인이 완료되지 않았습니다. 다시 시도해 주세요."));
            }
        }
    }

    private void FailLogins()
    {
        lock (loginGate)
        {
            foreach (var completion in logins.Values)
            {
                completion.TrySetException(new CodexConnectionException());
            }

            logins.Clear();
        }
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
        try
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            lock (loginGate)
            {
                earlyLoginResults.Clear();
            }
        }
        finally
        {
            operationGate.Release();
        }
    }
}
