using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UsageMonitor.Core;

internal sealed record ClaudeRefreshState
{
    public DateTimeOffset NextAttemptAt { get; init; } = DateTimeOffset.MinValue;
    public int Failures { get; init; }
}

internal sealed class ClaudeRefreshStateStore(string directory)
{
    internal static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageMonitor", "claude-refresh");

    internal ClaudeRefreshState Load(string identity, DateTimeOffset now)
    {
        try
        {
            using var stream = File.OpenRead(GetPath(identity));
            if (stream.Length > 4096)
                return new() { NextAttemptAt = now.AddMinutes(10) };
            var state = JsonSerializer.Deserialize<ClaudeRefreshState>(stream) ?? new();
            if (state.NextAttemptAt == DateTimeOffset.MinValue)
                return new() { NextAttemptAt = now.AddMinutes(10) };
            return state with
            {
                Failures = Math.Clamp(state.Failures, 0, 2),
                NextAttemptAt = state.NextAttemptAt > now.AddHours(1) ? now.AddHours(1) : state.NextAttemptAt
            };
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException)
        {
            return new() { NextAttemptAt = now.AddMinutes(10) };
        }
    }

    internal bool TrySave(string identity, ClaudeRefreshState state)
    {
        var path = GetPath(identity);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state);
                stream.Flush(true);
            }
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
    }

    private string GetPath(string identity) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".json");
}
