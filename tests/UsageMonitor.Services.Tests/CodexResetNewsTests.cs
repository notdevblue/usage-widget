using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using UsageMonitor.Services;

internal static class CodexResetNewsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EffectiveAt = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Pending = """
        {
          "ok": true,
          "data": {
            "currentTime": "2026-10-26T00:00:00.000Z",
            "latestGlobal": null,
            "latestBanked": null,
            "nextScheduled": {
              "kind": "reset_scheduled",
              "resetType": "global",
              "announcedAt": "2026-10-25T12:00:00.000Z",
              "effectiveAt": "2026-10-27T09:00:00.000Z",
              "schedulePrecision": "datetime",
              "scheduleTimingSource": "explicit",
              "scheduleBasis": "explicit",
              "scheduleConstraint": null,
              "scheduleState": "pending",
              "scheduleWindow": {
                "startAt": "2026-10-27T09:00:00.000Z",
                "endAt": "2026-10-27T09:00:00.000Z"
              },
              "source": {
                "origin": "x",
                "handle": "thsottiaux",
                "postId": "1234567890",
                "url": "https://x.com/thsottiaux/status/1234567890"
              }
            }
          }
        }
        """;

    public static async Task RunAsync(Action<bool, string> assert)
    {
        TestNotices(assert);
        TestParser(assert);
        await TestCachingAsync(assert);
        await TestFailuresAsync(assert);
        await TestSingleFlightAsync(assert);
        await TestCancellationAsync(assert);
        await TestRetryAsync("1200", TimeSpan.FromMinutes(20), assert);
        await TestRetryAsync(Now.AddMinutes(12).ToString("R", CultureInfo.InvariantCulture), TimeSpan.FromMinutes(12), assert);
        await TestRetryAsync("30", TimeSpan.FromMinutes(5), assert);
        await TestRetryAsync(Now.AddMinutes(-1).ToString("R", CultureInfo.InvariantCulture), TimeSpan.FromMinutes(5), assert);
    }

    private static void TestNotices(Action<bool, string> assert)
    {
        assert(!new CodexResetNotice().IsVisible(Now), "Empty reset notices must stay hidden.");
        assert(!new CodexResetNotice { Text = "reset" }.IsVisible(Now), "Reset notices without a source must stay hidden.");
        assert(!new CodexResetNotice { SourceUrl = "https://x.com/thsottiaux/status/1" }.IsVisible(Now), "Reset notices without text must stay hidden.");
        var notice = new CodexResetNotice { Text = "reset", SourceUrl = "https://x.com/thsottiaux/status/1", ExpiresAt = Now.AddSeconds(1) };
        assert(notice.IsVisible(Now) && !notice.IsVisible(Now.AddSeconds(1)), "Reset notices must disappear at their expiration boundary.");
    }

    private static void TestParser(Action<bool, string> assert)
    {
        var exact = CodexResetNewsService.Parse(Pending, Now);
        var localTime = EffectiveAt.ToLocalTime().ToString("M/d HH:mm", CultureInfo.InvariantCulture);
        assert(exact.Text == $"Codex 리셋 · {localTime} 예정", "Explicit reset times must use the PC local time.");
        assert(exact.SourceUrl == "https://x.com/thsottiaux/status/1234567890" && exact.ExpiresAt == EffectiveAt, "Trusted reset notices must retain their post link and exact expiry.");
        assert(exact.ToolTip.Contains("Tibo X") && exact.ToolTip.Contains("didcodexreset.com") && exact.ToolTip.Contains("PC 현지 시각"), "Reset tooltips must identify the original post, aggregator, and local time.");
        assert(CodexResetNewsService.Parse(Change("scheduleConstraint", "\"deadline\""), Now).Text == $"리셋 · {localTime}까지 예정", "Deadline announcements must retain the compact upper-bound wording.");
        var combined = CodexResetNewsService.Parse(Change("resetType", "\"global_and_banked\""), Now);
        assert(combined.IsVisible(Now) && combined.Text == exact.Text && combined.ToolTip.Contains("전체 리셋 및 리셋 티켓 지급"), "Combined announcements must keep the compact reset label and explain both reset types in the tooltip.");
        var bankedPayload = Change("resetType", "\"banked\"");
        var banked = CodexResetNewsService.Parse(bankedPayload, Now);
        assert(banked.IsVisible(Now) && banked.Text == $"리셋 티켓 · {localTime} 예정", "Pending banked resets must display the local ticket grant time.");
        assert(banked.ToolTip.Contains("Codex 리셋 티켓 지급"), "Banked tooltips must identify the ticket grant.");
        assert(CodexResetNewsService.Parse(Change("scheduleConstraint", "\"deadline\"", bankedPayload), Now).Text == $"리셋 티켓 · {localTime}까지 예정", "Banked deadlines must retain their upper-bound wording.");
        assert(CodexResetNewsService.Parse(Change("schedulePrecision", "\"date\"", bankedPayload), Now).Text == "리셋 티켓 지급 예고됨", "Date-only ticket grants must use a general ticket announcement label.");
        assert(CodexResetNewsService.Parse(Change("scheduleTimingSource", "\"fallback\"", bankedPayload), Now).Text == "리셋 티켓 지급 예고됨", "Inferred ticket grant times must not appear as exact times.");
        assert(!CodexResetNewsService.Parse(Change("kind", "\"reset_completed\"", bankedPayload), Now).IsVisible(Now), "Completed ticket grants must stay hidden.");
        assert(!CodexResetNewsService.Parse(Change("scheduleState", "\"fulfilled\"", bankedPayload), Now).IsVisible(Now), "Fulfilled ticket grant schedules must stay hidden.");
        assert(!CodexResetNewsService.Parse(Change("resetType", "\"unknown\""), Now).IsVisible(Now), "Unknown reset types must remain hidden.");

        var date = Change("schedulePrecision", "\"date\"");
        date = Change("scheduleWindow", """{"startAt":"2026-10-27T07:00:00.000Z","endAt":"2026-10-28T07:00:00.000Z"}""", date);
        var dateNotice = CodexResetNewsService.Parse(date, Now);
        assert(dateNotice.Text == "Codex 리셋 예고됨" && dateNotice.ToolTip.Contains("날짜 단위"), "Date precision must never turn midnight into an exact reset time.");
        assert(dateNotice.ExpiresAt == new DateTimeOffset(2026, 10, 28, 7, 0, 0, TimeSpan.Zero), "Date precision must retain the entire announced day window.");
        assert(!CodexResetNewsService.Parse(date, dateNotice.ExpiresAt).IsVisible(dateNotice.ExpiresAt), "Date notices must expire at the end of their full window.");

        foreach (var payload in new[]
        {
            Change("scheduleTimingSource", "\"fallback\""),
            Change("scheduleTimingSource", "null"),
            Change("scheduleBasis", "\"contextual_inference\""),
            Change("schedulePrecision", "null"),
            Change("effectiveAt", "null"),
            Change("effectiveAt", "\"2026-10-27\""),
            Change("effectiveAt", "\"not a timestamp\"")
        })
        {
            assert(CodexResetNewsService.Parse(payload, Now).Text == "Codex 리셋 예고됨", "Unstated or inferred times must use a general announcement label.");
        }

        var unknown = Change("effectiveAt", "null");
        unknown = Change("scheduleWindow", "null", unknown);
        var unknownNotice = CodexResetNewsService.Parse(unknown, Now);
        assert(unknownNotice.IsVisible(Now) && unknownNotice.ExpiresAt == DateTimeOffset.MaxValue, "An unknown reset time must not reuse the announcement timestamp or invent an expiry.");
        var exactWithoutWindow = CodexResetNewsService.Parse(Change("scheduleWindow", "null"), Now);
        assert(exactWithoutWindow.ExpiresAt == EffectiveAt, "An explicit exact time must expire even without a separate schedule window.");
        assert(!CodexResetNewsService.Parse(Pending, EffectiveAt).IsVisible(EffectiveAt), "Elapsed exact schedules must disappear without claiming completion.");

        foreach (var state in new[] { "elapsed", "fulfilled", "unknown", "" })
        {
            assert(!CodexResetNewsService.Parse(Change("scheduleState", System.Text.Json.JsonSerializer.Serialize(state)), Now).IsVisible(Now), "Only pending schedules may appear.");
        }

        foreach (var payload in new[]
        {
            Change("kind", "\"reset_completed\""),
            Change("completedAt", "\"2026-10-25T12:00:00.000Z\""),
            Change("completionRecordId", "\"completed-record\""),
            Change("source", "null"),
            Change("source", """{"origin":"operator","handle":"thsottiaux","url":"https://x.com/thsottiaux/status/1234567890"}"""),
            Change("source", """{"origin":"x","handle":"someone","url":"https://x.com/thsottiaux/status/1234567890"}""")
        })
        {
            assert(!CodexResetNewsService.Parse(payload, Now).IsVisible(Now), "Completed schedules and untrusted authors must remain hidden.");
        }

        foreach (var url in new[]
        {
            "http://x.com/thsottiaux/status/123", "https://evil.example/thsottiaux/status/123",
            "https://x.com.evil.example/thsottiaux/status/123", "https://user@x.com/thsottiaux/status/123",
            "https://x.com:444/thsottiaux/status/123", "https://x.com/other/status/123",
            "https://x.com/thsottiaux/status/abc", "https://x.com/thsottiaux/status/123/extra",
            "https://x.com/thsottiaux/status/123?redirect=evil", "https://x.com/thsottiaux/status/123#fragment",
            "javascript:alert(1)", ""
        })
        {
            var source = new JsonObject { ["origin"] = "x", ["handle"] = "thsottiaux", ["url"] = url };
            assert(!CodexResetNewsService.Parse(Change("source", source.ToJsonString()), Now).IsVisible(Now), "Reset sources must be canonical HTTPS posts from the expected Tibo account.");
        }

        var twitter = Pending.Replace("https://x.com/", "https://twitter.com/", StringComparison.Ordinal);
        assert(CodexResetNewsService.Parse(twitter, Now).IsVisible(Now), "The canonical legacy Twitter host must also be accepted.");
        foreach (var payload in new[] { "{", "null", "[]", "{}", "{\"ok\":false,\"data\":{}}", "{\"ok\":true}", "{\"ok\":true,\"data\":null}", "{\"ok\":true,\"data\":{}}", "{\"ok\":true,\"data\":{\"nextScheduled\":null}}", "{\"ok\":true,\"data\":{\"nextScheduled\":[]}}" })
        {
            assert(CodexResetNewsService.Parse(payload, Now) == new CodexResetNotice(), "Invalid JSON and missing status envelopes must safely return an empty notice.");
        }
    }

    private static async Task TestCachingAsync(Action<bool, string> assert)
    {
        var clock = new TestClock(Now);
        using var handler = new TestHandler((request, _) =>
        {
            assert(request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri == "https://didcodexreset.com/openapi/v1/status", "Reset news must use the documented public GET endpoint.");
            assert(request.Headers.Authorization is null, "Public reset requests must not attach credentials.");
            return Task.FromResult(JsonResponse(Pending));
        });
        using var client = new HttpClient(handler);
        var service = new CodexResetNewsService(client, clock);
        var first = await service.ReadAsync(CancellationToken.None);
        assert(first.IsVisible(Now) && handler.RequestCount == 1, "The first reset read must request immediately.");
        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
        assert(await service.ReadAsync(CancellationToken.None) == first && handler.RequestCount == 1, "Reset notices must remain cached before five minutes.");
        handler.Respond = (_, _) => Task.FromResult(JsonResponse("{\"ok\":true,\"data\":{\"nextScheduled\":null}}"));
        clock.Advance(TimeSpan.FromTicks(1));
        assert(await service.ReadAsync(CancellationToken.None) == new CodexResetNotice() && handler.RequestCount == 2, "A refreshed empty schedule must clear the prior announcement at five minutes.");
        assert(await service.ReadAsync(CancellationToken.None) == new CodexResetNotice() && handler.RequestCount == 2, "Empty responses must also be cached.");

        var nearExpiry = Change("effectiveAt", "\"2026-10-26T00:01:00.000Z\"");
        nearExpiry = Change("scheduleWindow", "null", nearExpiry);
        using var expiringHandler = new TestHandler((_, _) => Task.FromResult(JsonResponse(nearExpiry)));
        using var expiringClient = new HttpClient(expiringHandler);
        var expiringClock = new TestClock(Now);
        var expiringService = new CodexResetNewsService(expiringClient, expiringClock);
        assert((await expiringService.ReadAsync(CancellationToken.None)).IsVisible(Now), "Future exact announcements must be visible before expiry.");
        expiringClock.Advance(TimeSpan.FromMinutes(1));
        assert(await expiringService.ReadAsync(CancellationToken.None) == new CodexResetNotice() && expiringHandler.RequestCount == 1, "Cached announcements must hide at expiry without an early HTTP request.");
    }

    private static async Task TestFailuresAsync(Action<bool, string> assert)
    {
        var clock = new TestClock(Now);
        using var handler = new TestHandler((_, _) => Task.FromResult(JsonResponse(Pending)));
        using var client = new HttpClient(handler);
        var service = new CodexResetNewsService(client, clock);
        await service.ReadAsync(CancellationToken.None);
        handler.Respond = (_, _) => throw new HttpRequestException("offline");
        clock.Advance(TimeSpan.FromMinutes(5));
        assert(await service.ReadAsync(CancellationToken.None) == new CodexResetNotice(), "A network failure must quietly clear a prior announcement.");
        await service.ReadAsync(CancellationToken.None);
        assert(handler.RequestCount == 2, "A failed request must preserve the minimum request interval.");
        foreach (var respond in new Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[]
        {
            (_, _) => Task.FromResult(JsonResponse("{")),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            (_, _) => throw new TaskCanceledException("timeout"),
            (_, _) => throw new IOException("read failure")
        })
        {
            handler.Respond = respond;
            clock.Advance(TimeSpan.FromMinutes(5));
            assert(await service.ReadAsync(CancellationToken.None) == new CodexResetNotice(), "Malformed JSON, non-success status, timeout, and I/O failures must quietly return empty notices.");
        }
    }

    private static async Task TestSingleFlightAsync(Action<bool, string> assert)
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new TestHandler((_, _) => response.Task);
        using var client = new HttpClient(handler);
        var service = new CodexResetNewsService(client, new TestClock(Now));
        var reads = Enumerable.Range(0, 8).Select(_ => service.ReadAsync(CancellationToken.None)).ToArray();
        assert(handler.RequestCount == 1, "Concurrent reset reads must share one in-flight request.");
        response.SetResult(JsonResponse(Pending));
        var notices = await Task.WhenAll(reads);
        assert(handler.RequestCount == 1 && notices.All(notice => notice.IsVisible(Now)), "Concurrent readers must receive the same cached announcement.");
    }

    private static async Task TestCancellationAsync(Action<bool, string> assert)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new TestHandler(async (_, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(Pending);
        });
        using var client = new HttpClient(handler);
        var service = new CodexResetNewsService(client, new TestClock(Now));
        using var activeCancellation = new CancellationTokenSource();
        var active = service.ReadAsync(activeCancellation.Token);
        await started.Task;
        using var waitingCancellation = new CancellationTokenSource();
        var waiting = service.ReadAsync(waitingCancellation.Token);
        waitingCancellation.Cancel();
        await AssertCanceledAsync(waiting, assert);
        activeCancellation.Cancel();
        await AssertCanceledAsync(active, assert);
        assert(handler.RequestCount == 1, "Canceling a waiting reader must not issue another HTTP request.");
    }

    private static async Task TestRetryAsync(string retryAfter, TimeSpan delay, Action<bool, string> assert)
    {
        var clock = new TestClock(Now);
        using var handler = new TestHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var service = new CodexResetNewsService(client, clock);
        assert(await service.ReadAsync(CancellationToken.None) == new CodexResetNotice(), "Rate limits must quietly produce no announcement.");
        handler.Respond = (_, _) => Task.FromResult(JsonResponse(Pending));
        clock.Advance(delay - TimeSpan.FromTicks(1));
        await service.ReadAsync(CancellationToken.None);
        assert(handler.RequestCount == 1, "Retry-After must delay requests by at least the normal five-minute interval.");
        clock.Advance(TimeSpan.FromTicks(1));
        assert((await service.ReadAsync(CancellationToken.None)).IsVisible(clock.GetUtcNow()) && handler.RequestCount == 2, "Requests must resume at the Retry-After boundary for delta and HTTP-date values.");
    }

    private static async Task AssertCanceledAsync(Task<CodexResetNotice> read, Action<bool, string> assert)
    {
        var canceled = false;
        try
        {
            await read;
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        assert(canceled, "Caller cancellation must propagate to reset news readers.");
    }

    private static string Change(string property, string jsonValue, string payload = Pending)
    {
        var root = JsonNode.Parse(payload)!.AsObject();
        root["data"]!["nextScheduled"]![property] = JsonNode.Parse(jsonValue);
        return root.ToJsonString();
    }

    private static HttpResponseMessage JsonResponse(string payload)
    {
        return new(HttpStatusCode.OK) { Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json") };
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan delay)
        {
            current = current.Add(delay);
        }
    }

    private sealed class TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } = respond;
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Respond(request, cancellationToken);
        }
    }
}
