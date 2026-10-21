using System;
using System.Collections.Generic;

namespace UsageMonitor.Services;

public enum LayoutBlockKind
{
    Usage,
    Reset,
    Account,
    Status
}

public enum UsageVisualization
{
    Bar,
    Ring,
    Number
}

public enum UsageWindowSource
{
    First,
    Second,
    FiveHour,
    Weekly
}

public sealed record LayoutBlock
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ProviderId { get; init; } = "codex";
    public string WindowId { get; init; } = "";
    public LayoutBlockKind Kind { get; init; } = LayoutBlockKind.Usage;
    public UsageVisualization Visualization { get; init; } = UsageVisualization.Bar;
    public UsageWindowSource Source { get; init; } = UsageWindowSource.First;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 260;
    public double Height { get; init; } = 112;
    public bool ShowLabel { get; init; } = true;
    public bool ShowReset { get; init; } = true;
}

public sealed record LayoutProfile
{
    public bool Enabled { get; init; }
    public double Width { get; init; } = 260;
    public double Height { get; init; } = 250;
    public bool AutoHeight { get; init; } = true;
    public IReadOnlyList<LayoutBlock> Blocks { get; init; } = [];

    public static LayoutProfile Normalize(LayoutProfile layout)
    {
        layout ??= new();
        var width = ClampFinite(layout.Width, 260, 200, 640);
        var height = ClampFinite(layout.Height, 250, 80, 1000);
        var blocks = new List<LayoutBlock>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in layout.Blocks ?? [])
        {
            if (blocks.Count == 32)
                break;
            if (block is null)
                continue;
            var blockWidth = ClampFinite(block.Width, 260, 60, width);
            var blockHeight = ClampFinite(block.Height, 112, 32, height);
            var id = string.IsNullOrWhiteSpace(block.Id) ? Guid.NewGuid().ToString("N") : block.Id.Trim();
            var windowId = string.IsNullOrWhiteSpace(block.WindowId) ? "" : block.WindowId.Trim();
            while (!ids.Add(id))
                id = Guid.NewGuid().ToString("N");
            blocks.Add(block with
            {
                Id = id,
                ProviderId = string.Equals(block.ProviderId, "claude", StringComparison.OrdinalIgnoreCase) ? "claude" : "codex",
                WindowId = windowId.Length > 256 ? windowId[..256] : windowId,
                Kind = Enum.IsDefined(block.Kind) ? block.Kind : LayoutBlockKind.Usage,
                Visualization = Enum.IsDefined(block.Visualization) ? block.Visualization : UsageVisualization.Bar,
                Source = Enum.IsDefined(block.Source) ? block.Source : UsageWindowSource.First,
                Width = blockWidth,
                Height = blockHeight,
                X = ClampFinite(block.X, 0, 0, width - blockWidth),
                Y = ClampFinite(block.Y, 0, 0, height - blockHeight)
            });
        }

        return layout with
        {
            Enabled = layout.Enabled && blocks.Count > 0,
            Width = width,
            Height = height,
            Blocks = blocks.Count == 0 ? [] : blocks.AsReadOnly()
        };
    }

    private static double ClampFinite(double value, double fallback, double minimum, double maximum) => Math.Clamp(double.IsFinite(value) ? value : fallback, minimum, maximum);
}
