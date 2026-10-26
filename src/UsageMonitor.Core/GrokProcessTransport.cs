using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace UsageMonitor.Core;

internal sealed class GrokProcessTransport(string executablePath, TimeSpan requestTimeout)
{
    private const int OutputLimit = 1024 * 1024;

    internal async Task CheckVersionAsync(CancellationToken ct)
    {
        var result = await RunAsync(["--version"], ct).ConfigureAwait(false);
        var versionText = System.Text.RegularExpressions.Regex.Match(result.Output, @"\d+\.\d+(?:\.\d+)?").Value;
        if (result.ExitCode != 0 || !Version.TryParse(versionText, out var version) || version < new Version(1, 0))
            throw new GrokConnectionException("Grok Build 1.0 이상이 필요합니다. Grok를 업데이트해 주세요.");
    }

    internal async Task<JsonElement> ReadBillingAsync(CancellationToken ct)
    {
        var directory = Path.Combine(Path.GetTempPath(), "UsageMonitor", "grok-query");
        Directory.CreateDirectory(directory);
        var socket = Path.Combine(directory, "leader-" + Guid.NewGuid().ToString("N") + ".sock");
        return await ExecuteAsync([
            "agent", "--no-leader", "--leader-socket", socket, "stdio"
        ], directory, async (process, token) =>
        {
            var reader = process.StandardOutput;
            await WriteAsync(process.StandardInput, 1, "initialize", new
            {
                protocolVersion = 1,
                clientCapabilities = new { fs = new { readTextFile = false, writeTextFile = false }, terminal = false },
                clientInfo = new { name = "usage_monitor", title = "Usage Monitor", version = "0.1.3" }
            }, token).ConfigureAwait(false);
            await ReadResponseAsync(reader, 1, token).ConfigureAwait(false);
            await WriteAsync(process.StandardInput, 2, "_x.ai/billing", new { }, token).ConfigureAwait(false);
            return await ReadResponseAsync(reader, 2, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task<GrokProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        return await ExecuteAsync(arguments, "", async (process, token) =>
        {
            process.StandardInput.Close();
            var output = await ReadAllAsync(process.StandardOutput, token).ConfigureAwait(false);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return new GrokProcessResult(output, process.ExitCode);
        }, ct).ConfigureAwait(false);
    }

    private async Task<T> ExecuteAsync<T>(IReadOnlyList<string> arguments, string workingDirectory,
        Func<Process, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        request.CancelAfter(requestTimeout);
        using var process = new Process
        {
            StartInfo = new()
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        if (workingDirectory.Length > 0)
            process.StartInfo.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        var started = false;
        var completed = false;
        var errors = Task.CompletedTask;
        try
        {
            ct.ThrowIfCancellationRequested();
            started = process.Start();
            if (!started)
                throw new GrokConnectionException();
            errors = DrainAsync(process.StandardError, request.Token);
            var result = await operation(process, request.Token).ConfigureAwait(false);
            completed = true;
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Grok 응답 시간이 초과되었습니다. 다시 시도해 주세요.");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
        {
            throw new GrokConnectionException();
        }
        finally
        {
            if (started && !process.HasExited)
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch (Exception error) when (error is InvalidOperationException or IOException)
                {
                }

                try
                {
                    if (completed)
                    {
                        try
                        {
                            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                        }
                        catch (TimeoutException)
                        {
                        }
                    }

                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception or TimeoutException or IOException)
                {
                }
            }

            request.Cancel();
            try
            {
                await errors.ConfigureAwait(false);
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException)
            {
            }
        }
    }

    private static async Task WriteAsync(StreamWriter writer, int id, string method, object parameters, CancellationToken ct)
    {
        var message = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });
        await writer.WriteLineAsync(message.AsMemory(), ct).ConfigureAwait(false);
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<JsonElement> ReadResponseAsync(StreamReader reader, int id, CancellationToken ct)
    {
        var total = 0;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false) ?? throw new GrokConnectionException();
            total += line.Length;
            if (total > OutputLimit || line.Length > OutputLimit)
                throw new GrokConnectionException();
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (!TryReadId(root, out var responseId) || responseId != id)
                    continue;
                if (root.TryGetProperty("error", out var error))
                    throw MapError(error);
                if (!root.TryGetProperty("result", out var result))
                    throw new GrokConnectionException();
                return result.Clone();
            }
        }

        throw new GrokConnectionException();
    }

    private static bool TryReadId(JsonElement root, out int id)
    {
        id = 0;
        if (!root.TryGetProperty("id", out var element))
            return false;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out id))
            return true;
        return element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out id);
    }

    private static GrokConnectionException MapError(JsonElement error)
    {
        var message = error.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
            ? messageElement.GetString() ?? ""
            : "";
        if (message.Contains("Authentication required", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("grok login", StringComparison.OrdinalIgnoreCase))
            return new GrokAuthenticationRequiredException();
        var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var value) ? value : 0;
        return code == -32601
            ? new GrokConnectionException("이 Grok 버전은 사용량 조회를 지원하지 않아요. Grok Build를 업데이트해 주세요.")
            : new GrokConnectionException();
    }

    private static async Task<string> ReadAllAsync(StreamReader reader, CancellationToken ct)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
                return builder.ToString();
            if (builder.Length + read > OutputLimit)
                throw new GrokConnectionException();
            builder.Append(buffer, 0, read);
        }
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[1024];
        while (await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false) > 0)
        {
        }
    }

    private readonly record struct GrokProcessResult(string Output, int ExitCode);
}
