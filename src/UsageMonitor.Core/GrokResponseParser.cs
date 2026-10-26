using System.Globalization;
using System.Text.Json;

namespace UsageMonitor.Core;

internal static class GrokResponseParser
{
    internal static UsageSnapshot ReadBilling(JsonElement result, DateTimeOffset observedAt)
    {
        if (result.ValueKind != JsonValueKind.Object)
            throw new GrokConnectionException();

        var plan = ReadPlan(result);
        if (!result.TryGetProperty("config", out var config) || config.ValueKind == JsonValueKind.Null)
        {
            if (plan.Length == 0)
                throw new GrokConnectionException();
            return Snapshot(plan, [], observedAt);
        }

        if (config.ValueKind != JsonValueKind.Object)
            throw new GrokConnectionException();

        var window = ReadWindow(config);
        return Snapshot(plan, window is null ? [] : [window], observedAt);
    }

    private static UsageSnapshot Snapshot(string plan, IReadOnlyList<UsageWindow> windows, DateTimeOffset observedAt) => new()
    {
        ProviderId = "grok",
        ProviderName = "Grok",
        Account = new() { Kind = AccountKind.Grok, Plan = plan },
        Windows = windows,
        ObservedAt = observedAt
    };

    private static UsageWindow? ReadWindow(JsonElement config)
    {
        if (config.TryGetProperty("creditUsagePercent", out var percentElement) && percentElement.ValueKind != JsonValueKind.Null)
        {
            if (!TryReadPercent(percentElement, out var percent))
                throw new GrokConnectionException();
            return CreateWindow(config, percent);
        }

        if (HasCurrentPeriod(config))
            return CreateWindow(config, 0);

        if (TryCent(config, "monthlyLimit", out var limit) && limit > 0 && TryUsed(config, out var used))
            return CreateWindow(config, Math.Min(100, used * 100d / limit));

        return null;
    }

    private static UsageWindow CreateWindow(JsonElement config, double usedPercent)
    {
        var period = DescribePeriod(config);
        return new()
        {
            Id = period.Id,
            Label = period.Label,
            UsedPercent = usedPercent,
            WindowMinutes = period.Minutes,
            ResetsAt = period.Reset
        };
    }

    private static (string Id, string Label, int Minutes, DateTimeOffset Reset) DescribePeriod(JsonElement config)
    {
        var hasPeriod = config.TryGetProperty("currentPeriod", out var period) && period.ValueKind == JsonValueKind.Object;
        var type = hasPeriod && period.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString() ?? ""
            : "";
        var start = ReadTime(hasPeriod ? period : default, "start");
        if (start == DateTimeOffset.MinValue)
            start = ReadTime(config, "billingPeriodStart");
        var end = ReadTime(hasPeriod ? period : default, "end");
        if (end == DateTimeOffset.MinValue)
            end = ReadTime(config, "billingPeriodEnd");
        var measured = start != DateTimeOffset.MinValue && end > start ? (int)Math.Round((end - start).TotalMinutes) : 0;
        if (type.Contains("WEEKLY", StringComparison.OrdinalIgnoreCase))
            return ("grok:weekly", "주간", measured > 0 ? measured : 10080, end);
        if (type.Contains("MONTHLY", StringComparison.OrdinalIgnoreCase))
            return ("grok:monthly", "월간", measured > 0 ? measured : 43200, end);
        if (measured is >= 4 * 1440 and <= 12 * 1440)
            return ("grok:weekly", "주간", measured, end);
        if (measured is >= 20 * 1440 and <= 45 * 1440)
            return ("grok:monthly", "월간", measured, end);
        return ("grok:credits", "크레딧", measured, end);
    }

    private static bool HasCurrentPeriod(JsonElement config) =>
        config.TryGetProperty("currentPeriod", out var period) && period.ValueKind == JsonValueKind.Object &&
        (period.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && (type.GetString()?.Length ?? 0) > 0 ||
         ReadTime(period, "end") != DateTimeOffset.MinValue || ReadTime(period, "start") != DateTimeOffset.MinValue);

    private static bool TryReadPercent(JsonElement element, out double percent)
    {
        percent = 0;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value) || !double.IsFinite(value) || value < 0)
            return false;
        percent = Math.Min(100, value);
        return true;
    }

    private static bool TryUsed(JsonElement config, out double used)
    {
        if (TryCent(config, "used", out used))
            return used >= 0;
        if (config.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object &&
            usage.TryGetProperty("totalUsed", out var total) && total.ValueKind == JsonValueKind.Object &&
            total.TryGetProperty("val", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out used))
            return used >= 0;
        used = 0;
        return false;
    }

    private static bool TryCent(JsonElement config, string name, out double value)
    {
        value = 0;
        return config.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("val", out var raw) && raw.ValueKind == JsonValueKind.Number && raw.TryGetDouble(out value);
    }

    private static DateTimeOffset ReadTime(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return DateTimeOffset.MinValue;
        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    private static string ReadPlan(JsonElement result)
    {
        foreach (var name in new[] { "subscription_tier", "subscriptionTier" })
        {
            if (!result.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
                continue;
            var plan = (value.GetString() ?? "").Trim();
            if (plan.Length > 0)
                return plan.Length > 80 ? plan[..80] : plan;
        }

        return "";
    }
}
