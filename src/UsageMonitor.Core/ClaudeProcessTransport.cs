using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace UsageMonitor.Core;

internal sealed class ClaudeProcessTransport(string executablePath, TimeSpan requestTimeout)
{
    private const int OutputLimit = 1024 * 1024;

    internal async Task<ClaudeProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
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
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var started = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            started = process.Start();
            if (!started)
            {
                throw new ClaudeConnectionException();
            }

            process.StandardInput.Close();
            var output = ReadOutputAsync(process.StandardOutput, request);
            var errors = DrainErrorsAsync(process.StandardError, request.Token);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(request.Token)).ConfigureAwait(false);
            return new(await output.ConfigureAwait(false), process.ExitCode);
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
            request.Cancel();
            if (started && !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception or TimeoutException)
                {
                }
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

    private static async Task DrainErrorsAsync(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false) > 0)
        {
        }
    }
}

internal sealed record ClaudeProcessResult(string Output, int ExitCode);
