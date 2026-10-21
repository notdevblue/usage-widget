using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace UsageMonitor.Core;

internal sealed class ClaudeProcessTransport(string executablePath, TimeSpan requestTimeout)
{
    private const int OutputLimit = 1024 * 1024;

    internal Task<ClaudeProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        return ExecuteAsync(arguments, async (process, request) =>
        {
            process.StandardInput.Close();
            var output = ReadOutputAsync(process.StandardOutput, request);
            await Task.WhenAll(output, process.WaitForExitAsync(request.Token)).ConfigureAwait(false);
            return new ClaudeProcessResult(await output.ConfigureAwait(false), process.ExitCode);
        }, ct);
    }

    internal Task<JsonElement> ReadUsageAsync(CancellationToken ct)
    {
        return ExecuteAsync<JsonElement>([
            "--safe-mode", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}",
            "--no-session-persistence", "--print", "--output-format", "stream-json", "--verbose",
            "--input-format", "stream-json"
        ], async (process, request) =>
        {
            var reader = new ControlOutputReader(process.StandardOutput);
            await WriteRequestAsync(process.StandardInput, "initialize", new { subtype = "initialize" }, request.Token).ConfigureAwait(false);
            await ReadResponseAsync(reader, "initialize", request.Token).ConfigureAwait(false);
            await WriteRequestAsync(process.StandardInput, "usage", new { subtype = "get_usage", skip_behaviors = true }, request.Token).ConfigureAwait(false);
            return await ReadResponseAsync(reader, "usage", request.Token).ConfigureAwait(false);
        }, ct);
    }

    private async Task<T> ExecuteAsync<T>(IReadOnlyList<string> arguments,
        Func<Process, CancellationTokenSource, Task<T>> operation, CancellationToken ct)
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
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var started = false;
        var completed = false;
        var errors = Task.CompletedTask;
        var trailingOutput = Task.CompletedTask;
        try
        {
            ct.ThrowIfCancellationRequested();
            started = process.Start();
            if (!started)
            {
                throw new ClaudeConnectionException();
            }

            errors = DrainAsync(process.StandardError, request.Token);
            var result = await operation(process, request).ConfigureAwait(false);
            completed = true;
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Claude 응답 시간이 초과되었습니다. 다시 시도해 주세요.");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
        {
            throw new ClaudeConnectionException();
        }
        finally
        {
            if (!completed)
            {
                request.Cancel();
            }

            if (started && !process.HasExited)
            {
                if (completed)
                {
                    trailingOutput = DrainAsync(process.StandardOutput, request.Token);
                }

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
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception or TimeoutException or IOException)
                {
                }
            }

            request.Cancel();
            try
            {
                await Task.WhenAll(errors, trailingOutput).ConfigureAwait(false);
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException)
            {
            }
        }
    }

    private static async Task WriteRequestAsync(StreamWriter writer, string id, object request, CancellationToken ct)
    {
        var message = JsonSerializer.Serialize(new { type = "control_request", request_id = id, request });
        await writer.WriteLineAsync(message.AsMemory(), ct).ConfigureAwait(false);
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<JsonElement> ReadResponseAsync(ControlOutputReader reader, string id, CancellationToken ct)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
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
                var message = document.RootElement;
                if (message.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var type = CodexResponseParser.ReadString(message, "type");
                if (type == "assistant" || type == "result" &&
                    message.TryGetProperty("num_turns", out var turns) &&
                    (turns.ValueKind != JsonValueKind.Number || !turns.TryGetInt32(out var count) || count != 0))
                {
                    throw new ClaudeConnectionException();
                }

                if (type != "control_response" || !message.TryGetProperty("response", out var response) ||
                    response.ValueKind != JsonValueKind.Object || CodexResponseParser.ReadString(response, "request_id") != id)
                {
                    continue;
                }

                if (CodexResponseParser.ReadString(response, "subtype") != "success" ||
                    !response.TryGetProperty("response", out var result) || result.ValueKind != JsonValueKind.Object)
                {
                    throw new ClaudeConnectionException("Claude 사용량 조회가 거절되었습니다. Claude Code 연결 상태를 확인해 주세요.");
                }

                return result.Clone();
            }
        }
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, CancellationTokenSource request)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), request.Token).ConfigureAwait(false) is var count && count > 0)
        {
            if (output.Length + count > OutputLimit)
            {
                request.Cancel();
                throw new ClaudeConnectionException();
            }

            output.Append(buffer, 0, count);
        }

        return output.ToString();
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false) > 0)
        {
        }
    }

    private sealed class ControlOutputReader(StreamReader reader)
    {
        private readonly char[] buffer = new char[4096];
        private int position;
        private int count;
        private int total;

        internal async Task<string> ReadLineAsync(CancellationToken ct)
        {
            var line = new StringBuilder();
            while (true)
            {
                if (position == count)
                {
                    count = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                    position = 0;
                    total += count;
                    if (count == 0 || total > OutputLimit)
                    {
                        throw new ClaudeConnectionException();
                    }
                }

                var end = Array.IndexOf(buffer, '\n', position, count - position);
                if (end >= 0)
                {
                    line.Append(buffer, position, end - position);
                    position = end + 1;
                    return line.ToString().TrimEnd('\r');
                }

                line.Append(buffer, position, count - position);
                position = count;
            }
        }
    }
}

internal sealed record ClaudeProcessResult(string Output, int ExitCode);
