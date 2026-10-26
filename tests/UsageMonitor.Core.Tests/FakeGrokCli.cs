using System.Text.Json;

namespace UsageMonitor.Core.Tests;

internal static class FakeGrokCli
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var scenario = Environment.GetEnvironmentVariable("USAGE_MONITOR_GROK_SCENARIO") ?? "";
        var statePath = Environment.GetEnvironmentVariable("USAGE_MONITOR_GROK_STATE") ?? "";
        await File.AppendAllTextAsync(statePath + ".commands", JsonSerializer.Serialize(args) + Environment.NewLine);
        if (args.SequenceEqual(["--version"]))
        {
            Console.WriteLine(scenario == "old-version" ? "grok 0.9.0" : "grok 1.0.70 (test)");
            return scenario == "version-fail" ? 1 : 0;
        }

        if (args.Length != 5 || args[0] != "agent" || args[1] != "--no-leader" || args[2] != "--leader-socket" || args[4] != "stdio" ||
            !args[3].Contains("grok-query", StringComparison.OrdinalIgnoreCase) || !args[3].Contains("leader-", StringComparison.Ordinal))
        {
            return 9;
        }

        await File.WriteAllTextAsync(statePath + ".pid", Environment.ProcessId.ToString());
        await File.WriteAllTextAsync(statePath + ".cwd", Environment.CurrentDirectory);
        if (scenario == "hang")
        {
            await Task.Delay(Timeout.Infinite);
            return 0;
        }

        var initialize = await ReadRequestAsync(statePath);
        if (initialize.Method is "session/new" or "session/prompt")
            return 12;
        if (scenario == "notify")
            await Console.Out.WriteLineAsync("not json");
        await Console.Out.WriteLineAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{initialize.Id},\"result\":{{}}}}");
        if (scenario == "notify")
            await Console.Out.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"_x.ai/mcp/servers_updated\",\"params\":{}}");
        var billing = await ReadRequestAsync(statePath);
        if (billing.Method != "_x.ai/billing")
            return 14;
        var response = Response(scenario).Replace("\"id\":2", $"\"id\":{billing.Id}", StringComparison.Ordinal);
        await Console.Out.WriteLineAsync(response);
        return 0;
    }

    private static async Task<(string Method, string Id)> ReadRequestAsync(string statePath)
    {
        var line = await Console.In.ReadLineAsync() ?? "";
        await File.AppendAllTextAsync(statePath + ".requests", line + Environment.NewLine);
        using var document = JsonDocument.Parse(line);
        return (document.RootElement.GetProperty("method").GetString() ?? "", document.RootElement.GetProperty("id").GetRawText());
    }

    private static string Response(string scenario) => scenario switch
    {
        "auth" => """{"jsonrpc":"2.0","id":2,"error":{"code":-32000,"message":"Authentication required to fetch billing data. secret-token"}}""",
        "secret" => """{"jsonrpc":"2.0","id":2,"error":{"code":1,"message":"secret-token exploded"}}""",
        "missing-method" => """{"jsonrpc":"2.0","id":2,"error":{"code":-32601,"message":"secret-token Method not found"}}""",
        "malformed" => """{"jsonrpc":"2.0","id":2,"result":{}}""",
        "legacy" => """{"jsonrpc":"2.0","id":2,"result":{"config":{"monthlyLimit":{"val":2000},"used":{"val":1234},"billingPeriodStart":"2026-04-01T00:00:00Z","billingPeriodEnd":"2026-05-01T00:00:00Z"},"subscriptionTier":"SuperGrok"}}""",
        "omitted" => """{"jsonrpc":"2.0","id":2,"result":{"config":{"currentPeriod":{"type":"USAGE_PERIOD_TYPE_WEEKLY","start":"2026-10-24T02:24:13.840+00:00","end":"2026-10-31T02:24:13.840+00:00"}},"subscription_tier":"SuperGrok"}}""",
        _ => """{"jsonrpc":"2.0","id":2,"result":{"config":{"creditUsagePercent":25.0,"currentPeriod":{"type":"USAGE_PERIOD_TYPE_WEEKLY","start":"2026-10-24T02:24:13.840+00:00","end":"2026-10-31T02:24:13.840+00:00"}},"subscription_tier":"SuperGrok"}}"""
    };
}
