using System.Text.Json;

namespace UsageMonitor.Core.Tests;

internal static class FakeAppServer
{
    private static readonly SemaphoreSlim OutputGate = new(1, 1);
    private static readonly List<Task> BackgroundTasks = [];

    internal static async Task<int> RunAsync()
    {
        var scenario = Environment.GetEnvironmentVariable("USAGE_MONITOR_TEST_SCENARIO") ?? "weekly";
        var pidFile = Environment.GetEnvironmentVariable("USAGE_MONITOR_TEST_PID_FILE") ?? "";
        if (pidFile.Length > 0)
        {
            await File.WriteAllTextAsync($"{pidFile}.tmp", Environment.ProcessId.ToString());
            File.Move($"{pidFile}.tmp", pidFile);
        }

        var initialized = false;
        var accountReads = 0;
        var usageReads = 0;
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString() ?? "";
            if (method == "initialized")
            {
                initialized = true;
                continue;
            }

            var id = root.GetProperty("id").GetInt64();
            var parameters = root.GetProperty("params");
            if (method == "initialize")
            {
                if (parameters.GetProperty("clientInfo").GetProperty("name").GetString() != "usage_monitor")
                {
                    return 5;
                }

                await RespondAsync(id, new { userAgent = "usage-monitor-test" });
                continue;
            }

            if (!initialized)
            {
                return 6;
            }

            switch (method)
            {
                case "account/read":
                    accountReads++;
                    if (scenario == "exit-on-read")
                    {
                        await Console.Error.WriteLineAsync("secret-token-must-never-be-shown");
                        return 4;
                    }

                    if (scenario == "hang-on-read")
                    {
                        continue;
                    }

                    if (parameters.GetProperty("refreshToken").GetBoolean())
                    {
                        return 7;
                    }

                    var email = scenario == "account-switch" && accountReads >= 2
                        ? "new@example.invalid"
                        : "test@example.invalid";
                    var account = scenario switch
                    {
                        "signedout" => JsonSerializer.Deserialize<JsonElement>("null"),
                        "apikey" => JsonSerializer.SerializeToElement(new { type = "apiKey" }),
                        _ => JsonSerializer.SerializeToElement(new { type = "chatgpt", email, planType = "pro" })
                    };
                    await RespondAsync(id, new { account, requiresOpenaiAuth = true });
                    break;
                case "account/rateLimits/read":
                    usageReads++;
                    if (scenario is "signedout" or "apikey" ||
                        !parameters.GetProperty("excludeResetCreditDetails").GetBoolean())
                    {
                        return 8;
                    }

                    if (scenario == "slow-first-usage" && usageReads == 1)
                    {
                        await Task.Delay(450);
                    }

                    if (scenario == "account-revision" && usageReads == 1)
                    {
                        await NotifyAsync("account/updated", new { authMode = "chatgpt", planType = "pro" });
                    }

                    var usedPercent = scenario is "account-switch" or "account-revision" && usageReads == 1 ? 90 : 20;
                    var bucket = new
                    {
                        limitId = "codex",
                        primary = new { usedPercent, windowDurationMins = 10080, resetsAt = 1800000000 }
                    };
                    await RespondAsync(id, new { rateLimits = bucket, rateLimitsByLimitId = new { codex = bucket } });
                    break;
                case "account/login/start":
                    if (parameters.GetProperty("type").GetString() != "chatgpt")
                    {
                        return 9;
                    }

                    if (scenario == "login-early")
                    {
                        await NotifyAsync("account/login/completed", new { loginId = "login-test", success = true });
                    }

                    await RespondAsync(id, new { type = "chatgpt", loginId = "login-test", authUrl = "https://auth.example.invalid/codex" });
                    if (scenario == "login-normal")
                    {
                        BackgroundTasks.Add(Task.Run(async () =>
                        {
                            await Task.Delay(80);
                            await NotifyAsync("account/login/completed", new { loginId = "login-test", success = true });
                        }));
                    }

                    break;
                case "account/login/cancel":
                    if (scenario == "login-cancel-notification-first")
                    {
                        await NotifyAsync("account/login/completed", new { loginId = "login-test", success = false });
                    }

                    await RespondAsync(id, new { status = "canceled" });
                    break;
                case "test/echo":
                    var value = parameters.GetProperty("value").GetString() ?? "";
                    var delay = parameters.GetProperty("delay").GetInt32();
                    BackgroundTasks.Add(Task.Run(async () =>
                    {
                        await Task.Delay(delay);
                        await RespondAsync(id, new { value });
                    }));
                    break;
                default:
                    return 10;
            }
        }

        await Task.WhenAll(BackgroundTasks);
        return 0;
    }

    private static Task RespondAsync(long id, object result)
    {
        return WriteAsync(new { id, result });
    }

    private static Task NotifyAsync(string method, object parameters)
    {
        return WriteAsync(new { method, @params = parameters });
    }

    private static async Task WriteAsync(object message)
    {
        await OutputGate.WaitAsync();
        try
        {
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(message));
            await Console.Out.FlushAsync();
        }
        finally
        {
            OutputGate.Release();
        }
    }
}
