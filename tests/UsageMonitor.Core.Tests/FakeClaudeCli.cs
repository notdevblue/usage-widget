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
            "--no-session-persistence", "--print", "--output-format", "stream-json", "--verbose", "/usage"
        ];
        if (!args.SequenceEqual(expected))
        {
            return 9;
        }

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
        Console.WriteLine("{\"type\":\"assistant\",\"usage_report\":" + report + "}");
        Console.WriteLine("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"num_turns\":0}");
        return 0;
    }
}
