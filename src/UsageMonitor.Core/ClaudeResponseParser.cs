using System.Globalization;
using System.Text.Json;

namespace UsageMonitor.Core;

internal static class ClaudeResponseParser
{
    internal static ClaudeAuthentication ReadAuthentication(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("loggedIn", out var loggedIn) ||
                loggedIn.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new ClaudeConnectionException();
            }

            if (!loggedIn.GetBoolean())
            {
                return new(new(), "");
            }

            var method = CodexResponseParser.ReadString(root, "authMethod");
            var email = CodexResponseParser.ReadString(root, "email");
            var plan = CodexResponseParser.ReadString(root, "subscriptionType");
            var account = new UsageAccount
            {
                Kind = method switch
                {
                    "claude.ai" => AccountKind.Claude,
                    "api_key" or "api_key_helper" => AccountKind.ApiKey,
                    _ => AccountKind.Other
                },
                Email = email,
                Plan = plan
            };
            var identity = JsonSerializer.Serialize(new[]
            {
                method, email, plan, CodexResponseParser.ReadString(root, "orgId")
            });
            return new(account, identity);
        }
        catch (JsonException)
        {
            throw new ClaudeConnectionException();
        }
    }

    internal static IReadOnlyList<UsageWindow> ReadStream(string output)
    {
        var report = new JsonElement();
        var foundReport = false;
        var foundResult = false;
        try
        {
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var type = CodexResponseParser.ReadString(root, "type");
                if (type == "assistant" && root.TryGetProperty("usage_report", out var value) &&
                    value.ValueKind == JsonValueKind.Object)
                {
                    report = value.Clone();
                    foundReport = true;
                }

                if (type == "result")
                {
                    foundResult = CodexResponseParser.ReadString(root, "subtype") == "success" &&
                                  !CodexResponseParser.ReadBoolean(root, "is_error") &&
                                  root.TryGetProperty("num_turns", out var turns) &&
                                  turns.ValueKind == JsonValueKind.Number && turns.TryGetInt32(out var count) && count == 0;
                }
            }
        }
        catch (JsonException)
        {
            throw new ClaudeConnectionException();
        }

        if (!foundReport || !foundResult)
        {
            throw new ClaudeConnectionException();
        }

        return ReadWindows(report);
    }

    internal static IReadOnlyList<UsageWindow> ReadUsageReport(JsonElement report)
    {
        if (report.ValueKind != JsonValueKind.Object || !report.TryGetProperty("rate_limits", out var limits) ||
            limits.ValueKind != JsonValueKind.Object)
        {
            throw new ClaudeConnectionException();
        }

        if (limits.TryGetProperty("limits", out _))
        {
            return ReadWindows(report);
        }

        var rows = new List<object>();
        foreach (var (name, kind, model) in new[]
                 {
                     ("five_hour", "session", ""), ("seven_day", "weekly_all", ""),
                     ("seven_day_opus", "weekly_scoped", "Opus"), ("seven_day_sonnet", "weekly_scoped", "Sonnet")
                 })
        {
            if (limits.TryGetProperty(name, out var meter) && meter.ValueKind != JsonValueKind.Null)
            {
                AddLegacyMeter(rows, meter, kind, model);
            }
        }

        if (limits.TryGetProperty("model_scoped", out var scoped) && scoped.ValueKind != JsonValueKind.Null)
        {
            if (scoped.ValueKind != JsonValueKind.Array)
            {
                throw new ClaudeConnectionException();
            }

            foreach (var meter in scoped.EnumerateArray())
            {
                if (meter.ValueKind != JsonValueKind.Object || CodexResponseParser.ReadString(meter, "display_name").Length == 0)
                {
                    continue;
                }

                AddLegacyMeter(rows, meter, "weekly_scoped", CodexResponseParser.ReadString(meter, "display_name"));
            }
        }

        if (rows.Count == 0)
        {
            throw new ClaudeConnectionException();
        }

        return ReadWindows(JsonSerializer.SerializeToElement(new { rate_limits = new { limits = rows } }));
    }

    private static void AddLegacyMeter(List<object> rows, JsonElement meter, string kind, string model)
    {
        if (meter.ValueKind != JsonValueKind.Object || !meter.TryGetProperty("utilization", out var utilization) ||
            utilization.ValueKind != JsonValueKind.Number || !utilization.TryGetDouble(out var used) || !double.IsFinite(used))
        {
            return;
        }

        rows.Add(new
        {
            kind,
            percent = used,
            resets_at = CodexResponseParser.ReadString(meter, "resets_at"),
            scope = new { model = new { display_name = model } }
        });
    }

    internal static IReadOnlyList<UsageWindow> ReadWindows(JsonElement report)
    {
        if (report.ValueKind != JsonValueKind.Object ||
            !report.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object ||
            !limits.TryGetProperty("limits", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            throw new ClaudeConnectionException();
        }

        var windows = new List<UsageWindow>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("percent", out var percent) ||
                percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out var used) || !double.IsFinite(used))
            {
                continue;
            }

            var kind = CodexResponseParser.ReadString(row, "kind");
            var model = ReadScope(row, "model");
            var surface = ReadScope(row, "surface");
            var id = $"claude:{kind}:{model}:{surface}";
            if (!ids.Add(id))
            {
                continue;
            }

            var minutes = kind switch
            {
                "session" => 300,
                "weekly_all" or "weekly_scoped" => 10080,
                _ => 0
            };
            var label = kind switch
            {
                "session" => "5시간",
                "weekly_all" => "주간",
                _ when model.Length > 0 && surface.Length > 0 => $"{model} · {surface}",
                _ when model.Length > 0 => model,
                _ when surface.Length > 0 => surface,
                _ => "사용량"
            };
            var reset = DateTimeOffset.MinValue;
            if (DateTimeOffset.TryParse(CodexResponseParser.ReadString(row, "resets_at"),
                    CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedReset))
            {
                reset = parsedReset;
            }

            windows.Add(new()
            {
                Id = id,
                Label = label,
                UsedPercent = Math.Clamp(used, 0, 100),
                WindowMinutes = minutes,
                ResetsAt = reset
            });
        }

        return windows.AsReadOnly();
    }

    private static string ReadScope(JsonElement row, string name)
    {
        if (row.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object &&
            scope.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
        {
            return CodexResponseParser.ReadString(value, "display_name");
        }

        return "";
    }
}

internal sealed record ClaudeAuthentication(UsageAccount Account, string Identity);
