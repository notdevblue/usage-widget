using System;

namespace UsageMonitor.Services;

public sealed record CodexResetNotice
{
    public string Text { get; init; } = "";
    public string ToolTip { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.MaxValue;

    public bool IsVisible(DateTimeOffset now)
    {
        return !string.IsNullOrWhiteSpace(Text) && !string.IsNullOrWhiteSpace(SourceUrl) && now < ExpiresAt;
    }
}
