using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace UsageMonitor.Core;

internal sealed class CodexAppServerTransport : IAsyncDisposable
{
    private readonly string executablePath;
    private readonly Process process = new();
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private TextWriter input = TextWriter.Null;
    private Task outputTask = Task.CompletedTask;
    private Task errorTask = Task.CompletedTask;
    private long requestId;
    private int failed;
    private int disposed;
    private bool started;
    private bool initialized;

    internal event Action<string, JsonElement> NotificationReceived = delegate { };
    internal event Action ConnectionClosed = delegate { };

    internal CodexAppServerTransport(string executablePath)
    {
        this.executablePath = executablePath;
    }

    internal bool IsUsable => Volatile.Read(ref failed) == 0 && Volatile.Read(ref disposed) == 0;

    internal async Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken ct)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        return await SendRequestAsync(method, parameters, ct).ConfigureAwait(false);
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        await connectionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfClosed();
            if (initialized)
            {
                return;
            }

            if (!started)
            {
                process.StartInfo = new()
                {
                    FileName = executablePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                process.StartInfo.ArgumentList.Add("app-server");
                process.StartInfo.ArgumentList.Add("--stdio");
                try
                {
                    started = process.Start();
                    if (!started)
                    {
                        throw new CodexConnectionException();
                    }

                    input = process.StandardInput;
                    outputTask = ReadOutputAsync();
                    errorTask = DrainErrorsAsync();
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
                {
                    FailConnection();
                    throw new CodexConnectionException();
                }
            }

            await SendRequestAsync("initialize", new
            {
                clientInfo = new { name = "usage_monitor", title = "Usage Monitor", version = "0.1.0" },
                capabilities = new { experimentalApi = false, requestAttestation = false }
            }, ct).ConfigureAwait(false);
            await WriteAsync(new { method = "initialized", @params = new { } }, ct).ConfigureAwait(false);
            initialized = true;
        }
        catch
        {
            if (!initialized)
            {
                FailConnection();
            }

            throw;
        }
        finally
        {
            connectionGate.Release();
        }
    }

    private async Task<JsonElement> SendRequestAsync(string method, object parameters, CancellationToken ct)
    {
        ThrowIfClosed();
        var id = Interlocked.Increment(ref requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await WriteAsync(new { id, method, @params = parameters }, timeout.Token).ConfigureAwait(false);
            return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            if (lifetime.IsCancellationRequested)
            {
                throw new CodexConnectionException();
            }

            throw new TimeoutException("Codex 응답 시간이 초과되었습니다. 다시 시도해 주세요.");
        }
        finally
        {
            pending.TryRemove(id, out _);
        }
    }

    private async Task WriteAsync(object message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message);
        await writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfClosed();
            await input.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
            await input.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
        {
            FailConnection();
            throw new CodexConnectionException();
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task ReadOutputAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(lifetime.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                using var document = JsonDocument.Parse(line);
                var message = document.RootElement;
                if (TryGetRequestId(message, out var id) && pending.TryRemove(id, out var completion))
                {
                    if (message.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                    {
                        var code = 0;
                        if (error.TryGetProperty("code", out var codeValue) && codeValue.ValueKind == JsonValueKind.Number)
                        {
                            codeValue.TryGetInt32(out code);
                        }

                        completion.TrySetException(new CodexRequestException(code));
                    }
                    else if (message.TryGetProperty("result", out var result))
                    {
                        completion.TrySetResult(result.Clone());
                    }
                    else
                    {
                        completion.TrySetException(new CodexConnectionException());
                    }

                    continue;
                }

                var method = CodexResponseParser.ReadString(message, "method");
                if (method.Length > 0)
                {
                    var parameters = message.TryGetProperty("params", out var value) ? value.Clone() : default;
                    NotificationReceived(method, parameters);
                }
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or JsonException or InvalidOperationException)
        {
        }
        finally
        {
            FailConnection();
        }
    }

    private async Task DrainErrorsAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested &&
                   await process.StandardError.ReadLineAsync(lifetime.Token).ConfigureAwait(false) is not null)
            {
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or InvalidOperationException)
        {
        }
    }

    private static bool TryGetRequestId(JsonElement message, out long id)
    {
        id = 0;
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("id", out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out id),
            JsonValueKind.String => long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out id),
            _ => false
        };
    }

    private void ThrowIfClosed()
    {
        if (!IsUsable)
        {
            throw new CodexConnectionException();
        }
    }

    private void FailConnection()
    {
        if (Interlocked.Exchange(ref failed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        foreach (var entry in pending)
        {
            if (pending.TryRemove(entry.Key, out var completion))
            {
                completion.TrySetException(new CodexConnectionException());
            }
        }

        ConnectionClosed();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        FailConnection();
        if (started)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }

                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception or TimeoutException)
            {
            }
        }

        try
        {
            await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }

        process.Dispose();
    }
}
