using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UsageMonitor.Services;

public sealed class CodexResetNewsService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private readonly HttpClient client;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim readGate = new(1, 1);
    private CodexResetNotice cachedNotice = new();
    private DateTimeOffset nextRequestAt = DateTimeOffset.MinValue;

    public CodexResetNewsService(HttpClient client) : this(client, TimeProvider.System)
    {
    }

    public CodexResetNewsService(HttpClient client, TimeProvider timeProvider)
    {
        this.client = client;
        this.timeProvider = timeProvider;
    }

    public async Task<CodexResetNotice> ReadAsync(CancellationToken cancellationToken)
    {
        await readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (now >= nextRequestAt)
            {
                nextRequestAt = now.Add(RefreshInterval);
                cachedNotice = await ReadNoticeAsync(cancellationToken).ConfigureAwait(false);
            }

            if (cachedNotice.IsVisible(timeProvider.GetUtcNow()))
            {
                return cachedNotice;
            }

            return new();
        }
        finally
        {
            readGate.Release();
        }
    }

    private async Task<CodexResetNotice> ReadNoticeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://didcodexreset.com/openapi/v1/status");
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                ExtendRetry(response);
                return new();
            }

            if (!response.IsSuccessStatusCode)
            {
                return new();
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Parse(content, timeProvider.GetUtcNow());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new();
        }
        catch (HttpRequestException)
        {
            return new();
        }
        catch (IOException)
        {
            return new();
        }
    }

    private void ExtendRetry(HttpResponseMessage response)
    {
        var now = timeProvider.GetUtcNow();
        var retryAt = now.Add(RefreshInterval);
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta.HasValue)
            {
                var delta = retryAfter.Delta.GetValueOrDefault();
                var maximumDelta = DateTimeOffset.MaxValue - now;
                retryAt = now.Add(TimeSpan.FromTicks(Math.Clamp(delta.Ticks, RefreshInterval.Ticks, maximumDelta.Ticks)));
            }
            else if (retryAfter.Date.HasValue && retryAfter.Date.GetValueOrDefault() > retryAt)
            {
                retryAt = retryAfter.Date.GetValueOrDefault();
            }
        }

        if (retryAt > nextRequestAt)
        {
            nextRequestAt = retryAt;
        }
    }

    internal static CodexResetNotice Parse(string content, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (!TryGetProperty(root, "ok", out var ok) || ok.ValueKind != JsonValueKind.True
                || !TryGetProperty(root, "data", out var data) || data.ValueKind != JsonValueKind.Object
                || !TryGetProperty(data, "nextScheduled", out var scheduled) || scheduled.ValueKind != JsonValueKind.Object)
            {
                return new();
            }

            if (GetString(scheduled, "kind") != "reset_scheduled" || GetString(scheduled, "scheduleState") != "pending"
                || GetString(scheduled, "resetType") is not ("global" or "banked" or "global_and_banked")
                || GetString(scheduled, "completedAt").Length > 0 || GetString(scheduled, "completionRecordId").Length > 0)
            {
                return new();
            }

            if (!TryGetProperty(scheduled, "source", out var source) || GetString(source, "origin") != "x"
                || !GetString(source, "handle").Equals("thsottiaux", StringComparison.OrdinalIgnoreCase))
            {
                return new();
            }

            var sourceUrl = GetString(source, "url");
            if (!IsTrustedSourceUrl(sourceUrl))
            {
                return new();
            }

            var exactTime = GetString(scheduled, "schedulePrecision") == "datetime"
                && GetString(scheduled, "scheduleTimingSource") == "explicit"
                && GetString(scheduled, "scheduleBasis") != "contextual_inference"
                && TryGetTimestamp(scheduled, "effectiveAt", out _);
            var expiresAt = GetExpiration(scheduled, exactTime);
            if (now >= expiresAt)
            {
                return new();
            }

            return new()
            {
                Text = GetText(scheduled, exactTime),
                ToolTip = GetToolTip(scheduled, exactTime),
                SourceUrl = sourceUrl,
                ExpiresAt = expiresAt
            };
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static string GetText(JsonElement scheduled, bool exactTime)
    {
        if (exactTime && TryGetTimestamp(scheduled, "effectiveAt", out var effectiveAt))
        {
            var subject = GetCompactSubject(scheduled);
            var localTime = effectiveAt.ToLocalTime().ToString("M/d HH:mm", CultureInfo.InvariantCulture);
            if (GetString(scheduled, "scheduleConstraint") == "deadline")
            {
                return $"{subject} · {localTime}까지 예정";
            }

            return $"{subject} · {localTime} 예정";
        }

        return GetAnnouncementText(scheduled);
    }

    private static string GetCompactSubject(JsonElement scheduled)
    {
        if (GetString(scheduled, "resetType") == "banked")
        {
            return "리셋 티켓";
        }

        if (GetString(scheduled, "scheduleConstraint") == "deadline")
        {
            return "리셋";
        }

        return "Codex 리셋";
    }

    private static string GetAnnouncementText(JsonElement scheduled)
    {
        if (GetString(scheduled, "resetType") == "banked")
        {
            return "리셋 티켓 지급 예고됨";
        }

        return "Codex 리셋 예고됨";
    }

    private static string GetToolTip(JsonElement scheduled, bool exactTime)
    {
        var timing = "정확한 시각 미정";
        if (exactTime && TryGetTimestamp(scheduled, "effectiveAt", out var effectiveAt))
        {
            timing = effectiveAt.ToLocalTime().ToString("yyyy/M/d HH:mm", CultureInfo.InvariantCulture) + " 예정 (PC 현지 시각)";
            if (GetString(scheduled, "scheduleConstraint") == "deadline")
            {
                timing = effectiveAt.ToLocalTime().ToString("yyyy/M/d HH:mm", CultureInfo.InvariantCulture) + "까지 예정 (PC 현지 시각)";
            }
        }
        else if (GetString(scheduled, "schedulePrecision") == "date")
        {
            timing = "날짜 단위 예고 · 정확한 시각 미정";
        }

        return $"{GetToolTipSubject(scheduled)}: {timing}\nTibo X 게시물 · didcodexreset.com";
    }

    private static string GetToolTipSubject(JsonElement scheduled)
    {
        var resetType = GetString(scheduled, "resetType");
        if (resetType == "banked")
        {
            return "Codex 리셋 티켓 지급";
        }

        if (resetType == "global_and_banked")
        {
            return "Codex 전체 리셋 및 리셋 티켓 지급";
        }

        return "Codex 전체 리셋";
    }

    private static DateTimeOffset GetExpiration(JsonElement scheduled, bool exactTime)
    {
        if (TryGetProperty(scheduled, "scheduleWindow", out var window)
            && TryGetTimestamp(window, "startAt", out var startAt)
            && TryGetTimestamp(window, "endAt", out var endAt) && endAt >= startAt)
        {
            return endAt;
        }

        if (exactTime && TryGetTimestamp(scheduled, "effectiveAt", out var effectiveAt))
        {
            return effectiveAt;
        }

        return DateTimeOffset.MaxValue;
    }

    private static bool IsTrustedSourceUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
            && (uri.Host.Equals("x.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("twitter.com", StringComparison.OrdinalIgnoreCase))
            && uri.Query.Length == 0 && uri.Fragment.Length == 0 && !value.Contains('?') && !value.Contains('#')
            && Regex.IsMatch(uri.AbsolutePath, "^/thsottiaux/status/[0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string GetString(JsonElement element, string property)
    {
        if (TryGetProperty(element, property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? "";
        }

        return "";
    }

    private static bool TryGetTimestamp(JsonElement element, string property, out DateTimeOffset timestamp)
    {
        var value = GetString(element, property);
        timestamp = default;
        return value.Contains('T') && value.EndsWith('Z')
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp);
    }

    private static bool TryGetProperty(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out value);
    }
}
