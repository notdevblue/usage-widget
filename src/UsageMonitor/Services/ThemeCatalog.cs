using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace UsageMonitor.Services;

public sealed class ThemeCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Light = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CardBrush"] = "#F5F5F7",
        ["SurfaceBrush"] = "#FFFFFF",
        ["TextBrush"] = "#1D1D1F",
        ["SecondaryBrush"] = "#68686D",
        ["AccentBrush"] = "#007AFF",
        ["AccentSoftBrush"] = "#E1EEFF",
        ["UsageBrush"] = "#248A3D",
        ["TrackBrush"] = "#E1E1E6",
        ["LineBrush"] = "#D2D2D7",
        ["HoverBrush"] = "#E8E8ED",
        ["ErrorBrush"] = "#B42318",
        ["WarningBrush"] = "#9A6700",
        ["ActionTextBrush"] = "#000000"
    });

    private static readonly IReadOnlyDictionary<string, string> Dark = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CardBrush"] = "#242426",
        ["SurfaceBrush"] = "#303033",
        ["TextBrush"] = "#F5F5F7",
        ["SecondaryBrush"] = "#ADADB3",
        ["AccentBrush"] = "#0A84FF",
        ["AccentSoftBrush"] = "#263B56",
        ["UsageBrush"] = "#30D158",
        ["TrackBrush"] = "#3A3A3C",
        ["LineBrush"] = "#48484A",
        ["HoverBrush"] = "#343436",
        ["ErrorBrush"] = "#FF6961",
        ["WarningBrush"] = "#FFD60A",
        ["ActionTextBrush"] = "#000000"
    });

    public IReadOnlyDictionary<string, string> Create(WidgetTheme theme, bool systemDark)
    {
        if (theme == WidgetTheme.Light)
            return Light;
        if (theme == WidgetTheme.Dark)
            return Dark;
        if (systemDark)
            return Dark;
        return Light;
    }
}
