using System;
using System.Collections.Generic;
using System.Windows.Media;
using UsageMonitor.Core;

namespace UsageMonitor.Layout;

public sealed record LayoutContext
{
    public IReadOnlyList<LayoutProviderContext> Providers { get; init; } = [];
    public IReadOnlyList<UsageWindow> Windows { get; init; } = [];
    public string Account { get; init; } = "";
    public string Plan { get; init; } = "";
    public string Status { get; init; } = "";
    public Brush StatusBrush { get; init; } = Brushes.Gray;

    public LayoutProviderContext ResolveProvider(string providerId)
    {
        foreach (var provider in Providers)
        {
            if (string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                return provider;
        }

        if (!string.Equals(providerId, "codex", StringComparison.OrdinalIgnoreCase))
            return new() { ProviderId = providerId, ProviderName = providerId == "claude" ? "Claude" : providerId };

        return new()
        {
            Windows = Windows, Account = Account, Plan = Plan,
            Status = Status, StatusBrush = StatusBrush
        };
    }
}

public sealed record LayoutProviderContext
{
    public string ProviderId { get; init; } = "codex";
    public string ProviderName { get; init; } = "Codex";
    public IReadOnlyList<UsageWindow> Windows { get; init; } = [];
    public string Account { get; init; } = "";
    public string Plan { get; init; } = "";
    public string Status { get; init; } = "";
    public Brush StatusBrush { get; init; } = Brushes.Gray;
}
