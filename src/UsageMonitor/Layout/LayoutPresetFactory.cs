using System;
using System.Collections.Generic;
using System.Linq;
using UsageMonitor.Services;

namespace UsageMonitor.Layout;

internal static class LayoutPresetFactory
{
    internal static LayoutProfile Create(int preset, LayoutContext context)
    {
        var providers = new[] { "codex", "claude", "grok" }.Select(context.ResolveProvider).Where(provider => provider.Windows.Count > 0).ToArray();
        if (providers.Length == 0)
            providers = [context.ResolveProvider("codex")];
        var blocks = new List<LayoutBlock>();
        var maximumCount = Math.Clamp(providers.Max(provider => provider.Windows.Count), 1, 2);
        var width = preset == 3 ? maximumCount == 2 ? 540d : 300d : 260d;
        var blockHeight = preset switch { 1 => 66d, 2 => 146d, 3 => 106d, _ => 112d };
        var horizontal = preset is 2 or 3;
        var gap = horizontal ? 16d : 12d;
        var top = 0d;
        foreach (var provider in providers)
        {
            var count = Math.Clamp(provider.Windows.Count, 1, 2);
            var blockWidth = horizontal ? (width - gap * (count - 1)) / count : width;
            for (var index = 0; index < count; index++)
            {
                blocks.Add(new()
                {
                    ProviderId = provider.ProviderId,
                    Source = index == 0 ? UsageWindowSource.First : UsageWindowSource.Second,
                    Visualization = preset == 2 ? UsageVisualization.Ring : preset == 1 ? UsageVisualization.Number : UsageVisualization.Bar,
                    X = horizontal ? index * (blockWidth + gap) : 0,
                    Y = top + (horizontal ? 0 : index * (blockHeight + gap)),
                    Width = blockWidth,
                    Height = blockHeight,
                    ShowReset = preset != 1
                });
            }

            top += (horizontal ? blockHeight : count * blockHeight + (count - 1) * gap) + gap;
        }

        return LayoutProfile.Normalize(new()
        {
            Enabled = true,
            Width = width,
            Height = top - gap,
            AutoHeight = true,
            Blocks = blocks
        });
    }
}
