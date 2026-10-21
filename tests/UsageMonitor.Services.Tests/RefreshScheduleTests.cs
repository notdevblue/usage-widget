using UsageMonitor.Services;

internal static class RefreshScheduleTests
{
    public static void Run(Action<bool, string> assert)
    {
        var schedule = new RefreshSchedule();
        var now = new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.FromHours(9));
        assert(schedule.NextDelay(true, 0, [], now) == TimeSpan.FromSeconds(60), "Visible widgets must poll normally every minute.");
        assert(schedule.NextDelay(false, 0, [], now) == TimeSpan.FromSeconds(180), "Hidden widgets must poll normally every three minutes.");
        assert(schedule.NextDelay(true, -1, [], now) == TimeSpan.FromSeconds(60), "Negative failure counts must recover normal scheduling.");
        assert(schedule.NextDelay(false, int.MinValue, [], now) == TimeSpan.FromSeconds(180), "Extreme negative failure counts must remain safe.");
        assert(schedule.NextDelay(true, 1, [], now) == TimeSpan.FromSeconds(120), "The first visible failure must double the normal delay.");
        assert(schedule.NextDelay(true, 2, [], now) == TimeSpan.FromSeconds(240), "Consecutive failures must increase the delay exponentially.");
        assert(schedule.NextDelay(true, 3, [], now) == TimeSpan.FromSeconds(300), "Visible failure backoff must cap at five minutes.");
        assert(schedule.NextDelay(false, 1, [], now) == TimeSpan.FromSeconds(300), "Hidden failure backoff must respect the same cap.");
        assert(schedule.NextDelay(true, int.MaxValue, [], now) == TimeSpan.FromSeconds(300), "Extreme failure counts must never overflow.");
        assert(schedule.NextDelay(true, 0, [now.AddSeconds(30)], now) == TimeSpan.FromSeconds(32), "A future reset must schedule a read shortly after it occurs.");
        assert(schedule.NextDelay(true, 0, [now.AddSeconds(1)], now) == TimeSpan.FromSeconds(5), "Near resets must retain a five-second minimum delay.");
        assert(schedule.NextDelay(true, 0, [now.AddSeconds(2.9)], now) == TimeSpan.FromSeconds(5), "Fractional near-reset delays must respect the minimum.");
        assert(schedule.NextDelay(true, 0, [now.AddSeconds(58)], now) == TimeSpan.FromSeconds(60), "A reset at the normal polling boundary must preserve the normal delay.");
        assert(schedule.NextDelay(true, 0, [now.AddHours(5)], now) == TimeSpan.FromSeconds(60), "Distant resets must not postpone normal reads.");
        assert(schedule.NextDelay(false, 0, [now.AddSeconds(30)], now) == TimeSpan.FromSeconds(32), "Hidden widgets must also read soon after a near reset.");
        assert(schedule.NextDelay(true, 0, [DateTimeOffset.MinValue, now.AddDays(-1), now], now) == TimeSpan.FromSeconds(60), "Missing and expired resets must keep normal polling without a busy loop.");
        assert(schedule.NextDelay(true, 0, [now.AddDays(-1), now.AddSeconds(20), now.AddSeconds(1)], now) == TimeSpan.FromSeconds(5), "Multiple reset windows must use the earliest valid read.");
        assert(schedule.NextDelay(true, 1, [now.AddSeconds(1)], now) == TimeSpan.FromSeconds(120), "A near reset must not bypass error backoff.");
        assert(schedule.NextDelay(true, 0, [now.ToUniversalTime().AddSeconds(20)], now) == TimeSpan.FromSeconds(22), "Reset scheduling must compare absolute times across time zones.");
        assert(schedule.NextDelay(true, 0, [DateTimeOffset.MaxValue], DateTimeOffset.MinValue) == TimeSpan.FromSeconds(60), "Extreme reset timestamps must remain bounded by the normal delay.");
    }
}
