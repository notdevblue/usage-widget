using System.Text.Json;

namespace UsageMonitor.Core;

internal sealed class ClaudeUsageCache(string filePath)
{
    internal static string DefaultPath => Path.Combine(
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } directory
            ? directory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    internal bool TryGet(ClaudeAuthentication authentication, DateTimeOffset now, out UsageSnapshot snapshot)
    {
        snapshot = new();
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 16 * 1024 * 1024)
                return false;
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("oauthAccount", out var account) || account.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("cachedUsageUtilization", out var cache) || cache.ValueKind != JsonValueKind.Object)
                return false;

            var accountId = CodexResponseParser.ReadString(account, "accountUuid");
            if (accountId.Length == 0 || authentication.Account.Email.Length == 0 || authentication.OrganizationId.Length == 0 ||
                accountId != CodexResponseParser.ReadString(cache, "accountUuid") ||
                authentication.Account.Email != CodexResponseParser.ReadString(account, "emailAddress") ||
                authentication.OrganizationId != CodexResponseParser.ReadString(account, "organizationUuid") ||
                !cache.TryGetProperty("fetchedAtMs", out var timestamp) || timestamp.ValueKind != JsonValueKind.Number ||
                !timestamp.TryGetInt64(out var milliseconds) ||
                !cache.TryGetProperty("utilization", out var utilization) || utilization.ValueKind != JsonValueKind.Object)
                return false;

            var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            if (observedAt > now || observedAt < DateTimeOffset.UnixEpoch)
                return false;
            var windows = ClaudeResponseParser.ReadUsageReport(JsonSerializer.SerializeToElement(new { rate_limits = utilization }));
            snapshot = new()
            {
                ProviderId = "claude", ProviderName = "Claude", Account = authentication.Account,
                Windows = windows, ObservedAt = observedAt, IsCached = true
            };
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or JsonException or ArgumentException or ClaudeConnectionException)
        {
            return false;
        }
    }
}
