using System.Globalization;
using System.Text.Json;

namespace UsageMonitor.Core;

internal static class CodexResponseParser
{
    internal static UsageAccount ReadAccount(JsonElement response)
    {
        if (!TryGetObject(response, "account", out var account))
        {
            return new();
        }

        var type = ReadString(account, "type");
        return new()
        {
            Kind = type switch
            {
                "chatgpt" => AccountKind.ChatGpt,
                "apiKey" => AccountKind.ApiKey,
                _ => AccountKind.Other
            },
            Email = ReadString(account, "email"),
            Plan = ReadString(account, "planType")
        };
    }

    internal static IReadOnlyList<UsageWindow> ReadWindows(JsonElement response)
    {
        var windows = new List<UsageWindow>();
        var buckets = new List<(string Key, JsonElement Value)>();
        if (TryGetObject(response, "rateLimitsByLimitId", out var bucketMap))
        {
            buckets.AddRange(bucketMap.EnumerateObject()
                .Where(property => property.Value.ValueKind == JsonValueKind.Object)
                .Select(property => (property.Name, property.Value)));
        }

        if (buckets.Count == 0 && TryGetObject(response, "rateLimits", out var defaultBucket))
        {
            buckets.Add(("codex", defaultBucket));
        }

        var seenBuckets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bucket in buckets.OrderBy(bucket => IsCodexBucket(bucket.Key, bucket.Value) ? 0 : 1)
                     .ThenBy(bucket => bucket.Key, StringComparer.OrdinalIgnoreCase))
        {
            var id = ReadString(bucket.Value, "limitId");
            if (id.Length == 0)
            {
                id = bucket.Key;
            }

            if (!seenBuckets.Add(id))
            {
                continue;
            }

            var isCodex = IsCodexBucket(bucket.Key, bucket.Value);
            var name = ReadString(bucket.Value, "limitName");
            if (name.Length == 0)
            {
                name = id;
            }

            var seenWindows = new HashSet<(double Percent, int Minutes, DateTimeOffset Reset)>();
            foreach (var slot in new[] { "primary", "secondary" })
            {
                if (!TryGetWindow(bucket.Value, slot, out var window) ||
                    !seenWindows.Add((window.UsedPercent, window.WindowMinutes, window.ResetsAt)))
                {
                    continue;
                }

                windows.Add(window with
                {
                    Id = $"{id}:{slot}",
                    Label = isCodex ? window.Label : $"{name} · {window.Label}"
                });
            }
        }

        return windows.AsReadOnly();
    }

    internal static string ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? "";
        }

        return "";
    }

    internal static bool ReadBoolean(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static bool IsCodexBucket(string key, JsonElement value)
    {
        var id = ReadString(value, "limitId");
        return string.Equals(id.Length == 0 ? key : id, "codex", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetWindow(JsonElement bucket, string slot, out UsageWindow window)
    {
        window = new();
        if (!TryGetObject(bucket, slot, out var value) ||
            !value.TryGetProperty("usedPercent", out var used) || used.ValueKind != JsonValueKind.Number ||
            !used.TryGetDouble(out var usedPercent) || !double.IsFinite(usedPercent))
        {
            return false;
        }

        var minutes = 0;
        if (value.TryGetProperty("windowDurationMins", out var duration) &&
            duration.ValueKind == JsonValueKind.Number && duration.TryGetInt32(out var parsedMinutes) && parsedMinutes > 0)
        {
            minutes = parsedMinutes;
        }

        var reset = DateTimeOffset.MinValue;
        if (value.TryGetProperty("resetsAt", out var resetValue) && resetValue.ValueKind == JsonValueKind.Number &&
            resetValue.TryGetInt64(out var unixSeconds) &&
            unixSeconds >= DateTimeOffset.MinValue.ToUnixTimeSeconds() &&
            unixSeconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            reset = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        window = new()
        {
            Label = minutes switch
            {
                300 => "5시간",
                10080 => "주간",
                0 => "사용량",
                _ when minutes % 1440 == 0 => $"{(minutes / 1440).ToString(CultureInfo.InvariantCulture)}일",
                _ when minutes % 60 == 0 => $"{(minutes / 60).ToString(CultureInfo.InvariantCulture)}시간",
                _ => $"{minutes.ToString(CultureInfo.InvariantCulture)}분"
            },
            UsedPercent = Math.Clamp(usedPercent, 0, 100),
            WindowMinutes = minutes,
            ResetsAt = reset
        };
        return true;
    }

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value) &&
               value.ValueKind == JsonValueKind.Object;
    }
}
