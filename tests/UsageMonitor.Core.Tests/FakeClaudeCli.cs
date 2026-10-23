using System.Text.Json;

namespace UsageMonitor.Core.Tests;

internal static class FakeClaudeCli
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var scenario = Environment.GetEnvironmentVariable("USAGE_MONITOR_CLAUDE_SCENARIO") ?? "weekly";
        var statePath = Environment.GetEnvironmentVariable("USAGE_MONITOR_CLAUDE_STATE") ?? "";
        await File.AppendAllTextAsync(statePath + ".commands", string.Join(' ', args) + Environment.NewLine);
        if (args.SequenceEqual(["--version"]))
        {
            Console.WriteLine(scenario == "old-version" ? "2.1.200 (Claude Code)" : "2.1.304 (Claude Code)");
            return 0;
        }

        if (args.SequenceEqual(["auth", "status", "--json"]))
        {
            var reads = File.Exists(statePath) ? int.Parse(await File.ReadAllTextAsync(statePath)) + 1 : 1;
            await File.WriteAllTextAsync(statePath, reads.ToString());
            if (scenario == "signedout")
            {
                Console.WriteLine("{\"loggedIn\":false,\"authMethod\":\"none\"}");
                return 1;
            }

            var authMethod = scenario switch
            {
                "apikey" => "api_key",
                "setup-token" => "oauth_token",
                _ => "claude.ai"
            };
            var orgId = scenario == "switch" && reads >= 2 ? "new-org" : "old-org";
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                loggedIn = true, authMethod, email = "test@example.invalid", orgId, subscriptionType = "team"
            }));
            return 0;
        }

        string[] expected = [
            "--safe-mode", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}",
            "--no-session-persistence", "--print", "--output-format", "stream-json", "--verbose",
            "--input-format", "stream-json"
        ];
        if (!args.SequenceEqual(expected))
        {
            return 9;
        }

        var initialize = await ReadRequestAsync(statePath, "initialize");
        var initializeId = initialize.GetProperty("request_id");
        if (scenario == "noisy")
        {
            await Console.Out.WriteLineAsync("Harmless startup warning");
            await WriteAsync(new
            {
                type = "control_response",
                response = new { subtype = "error", request_id = "unrelated-init", error = "secret-unrelated-error" }
            });
        }

        if (scenario == "rejected-init")
        {
            await RejectAsync(initializeId);
            return 0;
        }

        await RespondAsync(initializeId, new { });
        var usage = await ReadRequestAsync(statePath, "get_usage");
        if (!usage.GetProperty("request").TryGetProperty("skip_behaviors", out var skipBehaviors) ||
            skipBehaviors.ValueKind != JsonValueKind.True)
        {
            return 10;
        }

        var usageId = usage.GetProperty("request_id");
        if (scenario is "hang" or "exit")
        {
            await File.WriteAllTextAsync(statePath + ".pid", Environment.ProcessId.ToString());
            if (scenario == "hang")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan);
            }

            await Console.Error.WriteLineAsync("secret-token-must-not-escape");
            return 4;
        }

        if (scenario == "overflow")
        {
            Console.WriteLine(new string('x', 2 * 1024 * 1024));
            return 0;
        }

        var count = int.Parse(await File.ReadAllTextAsync(statePath));
        var percent = scenario == "switch" && count == 1 ? 55 : 20;
        var report = "{\"rate_limits\":{\"limits\":[{\"kind\":\"weekly_all\",\"percent\":" + percent + ",\"resets_at\":\"2026-10-27T02:00:00Z\"}]}}";
        if (scenario == "rejected-usage")
        {
            await RejectAsync(usageId);
            return 0;
        }

        if (scenario == "noisy")
        {
            await Console.Out.WriteLineAsync("Another harmless warning");
            await WriteAsync(new { type = "system", subtype = "init", session_id = "ignored" });
            await RespondAsync(JsonSerializer.SerializeToElement("unrelated-usage"), new
            {
                rate_limits = new { limits = new[] { new { kind = "weekly_all", percent = 99 } } }
            });
        }

        if (scenario == "assistant-frame")
        {
            await WriteAsync(new { type = "assistant", message = new { content = "secret-unexpected-response" } });
        }

        if (scenario == "result-frame")
        {
            await WriteAsync(new { type = "result", subtype = "success", num_turns = 1 });
        }

        report = scenario switch
        {
            "legacy" => "{\"rate_limits\":{\"seven_day\":{\"utilization\":20,\"resets_at\":\"2026-10-27T02:00:00Z\"}}}",
            "missing-report" => "{}",
            "null-report" => "null",
            "malformed-report" => "{\"rate_limits\":{\"limits\":null}}",
            _ => report
        };
        if (scenario == "hang-after-report")
        {
            await File.WriteAllTextAsync(statePath + ".pid", Environment.ProcessId.ToString());
        }

        using var reportDocument = JsonDocument.Parse(report);
        if (scenario != "cached-report" && reportDocument.RootElement.ValueKind == JsonValueKind.Object &&
            reportDocument.RootElement.TryGetProperty("rate_limits", out var limits) && limits.ValueKind == JsonValueKind.Object)
        {
            var orgId = scenario == "switch" && count >= 2 ? "new-org" : "old-org";
            await File.WriteAllTextAsync(Environment.GetEnvironmentVariable("USAGE_MONITOR_CLAUDE_CACHE") ?? "", JsonSerializer.Serialize(new
            {
                oauthAccount = new { accountUuid = "account-id", emailAddress = "test@example.invalid", organizationUuid = orgId },
                cachedUsageUtilization = new { accountUuid = "account-id", fetchedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), utilization = limits }
            }));
        }
        await RespondAsync(usageId, reportDocument.RootElement);
        while (await Console.In.ReadLineAsync() is { } line)
        {
            await File.AppendAllTextAsync(statePath + ".requests", line + Environment.NewLine);
            return 11;
        }

        if (scenario == "trailing-output")
        {
            await Console.Out.WriteLineAsync(new string('x', 128 * 1024));
            await Console.Out.FlushAsync();
            await File.WriteAllTextAsync(statePath + ".eof-complete", "closed");
        }

        if (scenario == "hang-after-report")
        {
            await File.WriteAllTextAsync(statePath + ".eof", "closed");
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }

        return 0;
    }

    private static async Task<JsonElement> ReadRequestAsync(string statePath, string subtype)
    {
        var line = await Console.In.ReadLineAsync() ?? "";
        await File.AppendAllTextAsync(statePath + ".requests", line + Environment.NewLine);
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.GetProperty("type").GetString() != "control_request" ||
            root.GetProperty("request").GetProperty("subtype").GetString() != subtype ||
            !root.TryGetProperty("request_id", out var requestId) || requestId.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(requestId.GetString()))
        {
            throw new InvalidOperationException("Unexpected control request.");
        }

        return root.Clone();
    }

    private static Task RespondAsync(JsonElement requestId, object response)
    {
        return WriteAsync(new { type = "control_response", response = new { subtype = "success", request_id = requestId, response } });
    }

    private static Task RejectAsync(JsonElement requestId)
    {
        return WriteAsync(new
        {
            type = "control_response",
            response = new { subtype = "error", request_id = requestId, error = "secret-control-error-must-not-escape" }
        });
    }

    private static async Task WriteAsync(object message)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(message));
        await Console.Out.FlushAsync();
    }
}
