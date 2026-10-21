using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using UsageMonitor.Core;

namespace UsageMonitor.Presentation;

public sealed class WidgetViewModel : DependencyObject
{
    public ObservableCollection<UsageMetric> Metrics { get; } = [];
    public ObservableCollection<WidgetViewModel> Providers { get; } = [];

    public string ProviderId { get; init; } = "codex";
    public string ProviderName { get; init; } = "Codex";
    public string ProviderLabel => $"{ProviderName} ·";

    public static readonly DependencyProperty PlanProperty = DependencyProperty.Register(nameof(Plan), typeof(string), typeof(WidgetViewModel), new PropertyMetadata(""));
    public string Plan
    {
        get => (string)GetValue(PlanProperty);
        set => SetValue(PlanProperty, value);
    }

    public static readonly DependencyProperty AccountProperty = DependencyProperty.Register(nameof(Account), typeof(string), typeof(WidgetViewModel), new PropertyMetadata("계정 확인 중"));
    public string Account
    {
        get => (string)GetValue(AccountProperty);
        set => SetValue(AccountProperty, value);
    }

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(string), typeof(WidgetViewModel), new PropertyMetadata("연결하는 중"));
    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(nameof(Detail), typeof(string), typeof(WidgetViewModel), new PropertyMetadata(""));
    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public static readonly DependencyProperty EmptyTitleProperty = DependencyProperty.Register(nameof(EmptyTitle), typeof(string), typeof(WidgetViewModel), new PropertyMetadata("작은 창으로, 한눈에."));
    public string EmptyTitle
    {
        get => (string)GetValue(EmptyTitleProperty);
        set => SetValue(EmptyTitleProperty, value);
    }

    public static readonly DependencyProperty EmptyBodyProperty = DependencyProperty.Register(nameof(EmptyBody), typeof(string), typeof(WidgetViewModel), new PropertyMetadata("Codex 계정을 연결하면\n남은 사용량을 여기에 보여드려요."));
    public string EmptyBody
    {
        get => (string)GetValue(EmptyBodyProperty);
        set => SetValue(EmptyBodyProperty, value);
    }

    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(WidgetViewModel), new PropertyMetadata("ChatGPT로 로그인"));
    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public static readonly DependencyProperty PinGlyphProperty = DependencyProperty.Register(nameof(PinGlyph), typeof(string), typeof(WidgetViewModel), new PropertyMetadata("pin"));
    public string PinGlyph
    {
        get => (string)GetValue(PinGlyphProperty);
        set => SetValue(PinGlyphProperty, value);
    }

    public static readonly DependencyProperty PinToolTipProperty = DependencyProperty.Register(nameof(PinToolTip), typeof(string), typeof(WidgetViewModel), new PropertyMetadata("항상 위에 표시"));
    public string PinToolTip
    {
        get => (string)GetValue(PinToolTipProperty);
        set => SetValue(PinToolTipProperty, value);
    }

    public static readonly DependencyProperty SettingsOpenProperty = DependencyProperty.Register(nameof(SettingsOpen), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(false));
    public bool SettingsOpen
    {
        get => (bool)GetValue(SettingsOpenProperty);
        set => SetValue(SettingsOpenProperty, value);
    }

    public static readonly DependencyProperty UsageVisibleProperty = DependencyProperty.Register(nameof(UsageVisible), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(true));
    public bool UsageVisible
    {
        get => (bool)GetValue(UsageVisibleProperty);
        set => SetValue(UsageVisibleProperty, value);
    }

    public static readonly DependencyProperty HasPlanProperty = DependencyProperty.Register(nameof(HasPlan), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(false));
    public bool HasPlan
    {
        get => (bool)GetValue(HasPlanProperty);
        set => SetValue(HasPlanProperty, value);
    }

    public static readonly DependencyProperty HasMetricsProperty = DependencyProperty.Register(nameof(HasMetrics), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(false));
    public bool HasMetrics
    {
        get => (bool)GetValue(HasMetricsProperty);
        set => SetValue(HasMetricsProperty, value);
    }

    public static readonly DependencyProperty EmptyVisibleProperty = DependencyProperty.Register(nameof(EmptyVisible), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(true));
    public bool EmptyVisible
    {
        get => (bool)GetValue(EmptyVisibleProperty);
        set => SetValue(EmptyVisibleProperty, value);
    }

    public static readonly DependencyProperty DetailVisibleProperty = DependencyProperty.Register(nameof(DetailVisible), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(false));
    public bool DetailVisible
    {
        get => (bool)GetValue(DetailVisibleProperty);
        set => SetValue(DetailVisibleProperty, value);
    }

    public static readonly DependencyProperty CanActProperty = DependencyProperty.Register(nameof(CanAct), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(false));
    public bool CanAct
    {
        get => (bool)GetValue(CanActProperty);
        set => SetValue(CanActProperty, value);
    }

    public static readonly DependencyProperty RuntimeMissingProperty = DependencyProperty.Register(nameof(RuntimeMissing), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(false));
    public bool RuntimeMissing
    {
        get => (bool)GetValue(RuntimeMissingProperty);
        set => SetValue(RuntimeMissingProperty, value);
    }

    public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(nameof(Preview), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(false));
    public bool Preview
    {
        get => (bool)GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    public static readonly DependencyProperty StaleProperty = DependencyProperty.Register(nameof(Stale), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(false));
    public bool Stale
    {
        get => (bool)GetValue(StaleProperty);
        set => SetValue(StaleProperty, value);
    }

    public static readonly DependencyProperty BusyProperty = DependencyProperty.Register(nameof(Busy), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(false));
    public bool Busy
    {
        get => (bool)GetValue(BusyProperty);
        set => SetValue(BusyProperty, value);
    }

    public static readonly DependencyProperty StatusBrushProperty = DependencyProperty.Register(nameof(StatusBrush), typeof(Brush), typeof(WidgetViewModel), new PropertyMetadata(Brushes.Gray));
    public Brush StatusBrush
    {
        get => (Brush)GetValue(StatusBrushProperty);
        set => SetValue(StatusBrushProperty, value);
    }

    public static readonly DependencyProperty CustomLayoutVisibleProperty = DependencyProperty.Register(nameof(CustomLayoutVisible), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(false));
    public bool CustomLayoutVisible
    {
        get => (bool)GetValue(CustomLayoutVisibleProperty);
        set => SetValue(CustomLayoutVisibleProperty, value);
    }

    public static readonly DependencyProperty AutomaticLayoutVisibleProperty = DependencyProperty.Register(nameof(AutomaticLayoutVisible), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(true));
    public bool AutomaticLayoutVisible
    {
        get => (bool)GetValue(AutomaticLayoutVisibleProperty);
        set => SetValue(AutomaticLayoutVisibleProperty, value);
    }

    public static readonly DependencyProperty ScaleTextProperty = DependencyProperty.Register(nameof(ScaleText), typeof(string), typeof(WidgetViewModel), new PropertyMetadata("100%"));
    public string ScaleText
    {
        get => (string)GetValue(ScaleTextProperty);
        set => SetValue(ScaleTextProperty, value);
    }

    public static readonly DependencyProperty OpacityTextProperty = DependencyProperty.Register(nameof(OpacityText), typeof(string), typeof(WidgetViewModel), new PropertyMetadata("100%"));
    public string OpacityText
    {
        get => (string)GetValue(OpacityTextProperty);
        set => SetValue(OpacityTextProperty, value);
    }

    public static readonly DependencyProperty BackgroundSwatchProperty = DependencyProperty.Register(nameof(BackgroundSwatch), typeof(Brush), typeof(WidgetViewModel), new PropertyMetadata(Brushes.White));
    public Brush BackgroundSwatch
    {
        get => (Brush)GetValue(BackgroundSwatchProperty);
        set => SetValue(BackgroundSwatchProperty, value);
    }

    public static readonly DependencyProperty ForegroundSwatchProperty = DependencyProperty.Register(nameof(ForegroundSwatch), typeof(Brush), typeof(WidgetViewModel), new PropertyMetadata(Brushes.Black));
    public Brush ForegroundSwatch
    {
        get => (Brush)GetValue(ForegroundSwatchProperty);
        set => SetValue(ForegroundSwatchProperty, value);
    }

    public static readonly DependencyProperty AccentSwatchProperty = DependencyProperty.Register(nameof(AccentSwatch), typeof(Brush), typeof(WidgetViewModel), new PropertyMetadata(Brushes.Green));
    public Brush AccentSwatch
    {
        get => (Brush)GetValue(AccentSwatchProperty);
        set => SetValue(AccentSwatchProperty, value);
    }

    public static readonly DependencyProperty HoverTextProperty = DependencyProperty.Register(nameof(HoverText), typeof(string), typeof(WidgetViewModel), new PropertyMetadata("계정 확인 중"));
    public string HoverText
    {
        get => (string)GetValue(HoverTextProperty);
        set => SetValue(HoverTextProperty, value);
    }

    public bool ShowResetCountdown { get; set; }

    public static readonly DependencyProperty ResetSummaryProperty = DependencyProperty.Register(nameof(ResetSummary), typeof(string), typeof(WidgetViewModel), new PropertyMetadata(""));
    public string ResetSummary
    {
        get => (string)GetValue(ResetSummaryProperty);
        set => SetValue(ResetSummaryProperty, value);
    }

    public static readonly DependencyProperty ResetCountdownVisibleProperty = DependencyProperty.Register(nameof(ResetCountdownVisible), typeof(bool), typeof(WidgetViewModel), new PropertyMetadata(false));
    public bool ResetCountdownVisible
    {
        get => (bool)GetValue(ResetCountdownVisibleProperty);
        set => SetValue(ResetCountdownVisibleProperty, value);
    }

    public void Update()
    {
        UsageVisible = !SettingsOpen;
        HasPlan = Plan.Length > 0;
        HasMetrics = Metrics.Count > 0;
        EmptyVisible = !HasMetrics;
        DetailVisible = Detail.Length > 0;
        ResetSummary = HasMetrics ? "초기화 · " + string.Join(" · ", Metrics.Select(metric => $"{metric.Label} {metric.ResetCountdown}")) : "";
        ResetCountdownVisible = ShowResetCountdown && HasMetrics;
    }
}

public sealed class UsageMetric
{
    public string Label { get; }
    public string Amount { get; }
    public string Percentage => $"{Amount}%";
    public double Remaining { get; }
    public string Reset { get; }
    public string ResetCountdown { get; }
    public string ResetDetail { get; }
    public Brush Accent { get; }

    public UsageMetric(UsageWindow window, DateTimeOffset now, Brush normal, Brush warning, Brush error)
    {
        Label = window.Label;
        Remaining = window.RemainingPercent;
        Amount = Remaining <= 0 ? "0" : Remaining < 1 ? "<1" : Math.Floor(Remaining).ToString("0");
        Accent = CreateAccent(Remaining, normal, warning, error);
        ResetDetail = window.ResetsAt == DateTimeOffset.MinValue
            ? "초기화 시간이 제공되지 않았어요."
            : window.ResetsAt.ToLocalTime().ToString("yyyy.MM.dd (ddd) HH:mm:ss '초기화'");
        Reset = FormatReset(window.ResetsAt, now);
        ResetCountdown = FormatReset(window.ResetsAt, now, true);
    }

    private static Brush CreateAccent(double remaining, Brush normal, Brush warning, Brush error)
    {
        if (remaining <= 10)
            return error;
        if (remaining >= 100)
            return normal;
        var start = remaining > 25 ? normal : warning;
        var end = remaining > 25 ? warning : error;
        var amount = remaining > 25 ? (100 - remaining) / 75 : (25 - remaining) / 15;
        if (start is not SolidColorBrush first || end is not SolidColorBrush second)
            return start;
        var color = Color.FromArgb(
            BlendChannel(first.Color.A, second.Color.A, amount),
            BlendChannel(first.Color.R, second.Color.R, amount),
            BlendChannel(first.Color.G, second.Color.G, amount),
            BlendChannel(first.Color.B, second.Color.B, amount));
        var accent = new SolidColorBrush(color) { Opacity = first.Opacity + (second.Opacity - first.Opacity) * amount };
        accent.Freeze();
        return accent;
    }

    private static byte BlendChannel(byte first, byte second, double amount) => (byte)Math.Round(first + (second - first) * amount);

    private static string FormatReset(DateTimeOffset resetsAt, DateTimeOffset now, bool compact = false)
    {
        if (resetsAt == DateTimeOffset.MinValue)
            return compact ? "미제공" : "초기화 시간 미제공";
        var remaining = resetsAt - now;
        if (remaining <= TimeSpan.Zero)
            return compact ? "확인 중" : "초기화 확인 중";
        if (remaining.TotalDays >= 1)
            return compact ? $"{(int)remaining.TotalDays}d {remaining.Hours}h" : $"{(int)remaining.TotalDays}일 {remaining.Hours}시간 뒤 초기화";
        if (remaining.TotalHours >= 1)
            return compact ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m" : $"{(int)remaining.TotalHours}시간 {remaining.Minutes}분 뒤 초기화";
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return compact ? $"{minutes}m" : $"{minutes}분 뒤 초기화";
    }
}
