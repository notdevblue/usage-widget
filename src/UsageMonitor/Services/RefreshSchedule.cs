using System;
using System.Collections.Generic;

namespace UsageMonitor.Services;

public sealed class RefreshSchedule
{
    public TimeSpan NextDelay(bool visible, int consecutiveFailures, IReadOnlyList<DateTimeOffset> resets, DateTimeOffset now)
    {
        var normalSeconds = visible ? 60 : 180;
        var failures = Math.Clamp(consecutiveFailures, 0, 3);
        if (failures > 0)
            return TimeSpan.FromSeconds(Math.Min(300, normalSeconds * (1 << failures)));

        var delay = TimeSpan.FromSeconds(normalSeconds);
        foreach (var reset in resets ?? [])
        {
            if (reset <= now)
                continue;
            var afterReset = reset - now + TimeSpan.FromSeconds(2);
            var candidate = afterReset < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : afterReset;
            if (candidate < delay)
                delay = candidate;
        }

        return delay;
    }
}
