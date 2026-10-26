using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using UsageMonitor.Layout;
using UsageMonitor.Services;
using Forms = System.Windows.Forms;

namespace UsageMonitor;

public sealed class SettingsWindow : Window
{
    private readonly Func<LayoutContext> getContext;
    private readonly CheckBox pinOption = new();
    private readonly ComboBox fiveHourOption = new();
    private readonly CheckBox resetCountdownOption = new();
    private readonly ComboBox themeOption = new();
    private readonly Slider scaleOption = new();
    private readonly Slider opacityOption = new();
    private readonly TextBlock scaleValue = new();
    private readonly TextBlock opacityValue = new();
    private readonly TextBlock layoutSummary = new();
    private readonly TextBlock accountText = new();
    private readonly TextBlock accountHint = new();
    private readonly TextBlock connectionDetail = new();
    private readonly TextBlock feedback = new();
    private readonly Button connectButton = new();
    private readonly Button executableButton = new();
    private readonly TextBlock claudeAccountText = new();
    private readonly TextBlock claudeAccountHint = new();
    private readonly TextBlock claudeConnectionDetail = new();
    private readonly Button claudeConnectButton = new();
    private readonly Button claudeExecutableButton = new();
    private readonly TextBlock grokAccountText = new();
    private readonly TextBlock grokAccountHint = new();
    private readonly TextBlock grokConnectionDetail = new();
    private readonly Button grokConnectButton = new();
    private readonly Button grokExecutableButton = new();
    private readonly Button automaticLayoutButton = new();
    private readonly Dictionary<string, Border> swatches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> colorValues = new(StringComparer.Ordinal);
    private AppSettings settings;
    private bool synchronizing;

    public event Action<AppSettings> SettingsChanged = delegate { };
    public event Action LoginRequested = delegate { };
    public event Action ExecutableRequested = delegate { };
    public event Action ClaudeLoginRequested = delegate { };
    public event Action ClaudeExecutableRequested = delegate { };
    public event Action GrokLoginRequested = delegate { };
    public event Action GrokExecutableRequested = delegate { };
    public event Action QuitRequested = delegate { };

    public SettingsWindow(AppSettings settings, Func<LayoutContext> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.settings = Normalize(settings);
        getContext = context;
        Title = "Usage Monitor 설정";
        Width = 390;
        MinWidth = 360;
        MaxWidth = 400;
        Height = Math.Min(780, SystemParameters.WorkArea.Height - 40);
        MaxHeight = SystemParameters.WorkArea.Height;
        MinHeight = Math.Min(460, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");
        FontSize = 12;
        SetResourceReference(BackgroundProperty, "CardBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        Content = BuildWindow();
        RefreshControls();
        UpdateConnection("", "ChatGPT로 로그인", true, false);
        UpdateClaudeConnection("", "Claude에서 로그인", true, false);
        UpdateGrokConnection("", "Grok에서 로그인", true, false);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            Close();
            e.Handled = true;
        };
    }

    public void UpdateConnection(string account, string actionText, bool canConnect, bool runtimeMissing, string detail = "")
    {
        accountText.Text = account.Length > 0 ? account : "연결된 계정 없음";
        accountText.ToolTip = accountText.Text;
        connectButton.Content = actionText.Length > 0 ? actionText : "ChatGPT로 로그인";
        connectButton.IsEnabled = canConnect;
        accountHint.Text = runtimeMissing
            ? "Codex를 설치하거나 실행 파일을 선택해 주세요."
            : "이 PC의 Codex 로그인과 공유해요.";
        executableButton.SetResourceReference(Control.BackgroundProperty, runtimeMissing ? "AccentBrush" : "HoverBrush");
        executableButton.SetResourceReference(Control.ForegroundProperty, runtimeMissing ? "ActionTextBrush" : "TextBrush");
        connectionDetail.Text = detail;
        connectionDetail.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowFeedback(string message)
    {
        feedback.Text = message;
        feedback.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void UpdateClaudeConnection(string account, string actionText, bool canConnect, bool runtimeMissing, string detail = "")
    {
        claudeAccountText.Text = account.Length > 0 ? account : "연결된 Claude 계정 없음";
        claudeAccountText.ToolTip = claudeAccountText.Text;
        claudeConnectButton.Content = actionText.Length > 0 ? actionText : "Claude에서 로그인";
        claudeConnectButton.IsEnabled = canConnect;
        claudeAccountHint.Text = runtimeMissing ? "Claude Code를 설치하거나 실행 파일을 선택해 주세요."
            : "이 PC의 Claude Code 로그인과 공유해요. 로그인은 공식 Claude 창에서 진행해요.";
        claudeExecutableButton.SetResourceReference(Control.BackgroundProperty, runtimeMissing ? "AccentBrush" : "HoverBrush");
        claudeExecutableButton.SetResourceReference(Control.ForegroundProperty, runtimeMissing ? "ActionTextBrush" : "TextBrush");
        claudeConnectionDetail.Text = detail;
        claudeConnectionDetail.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void UpdateGrokConnection(string account, string actionText, bool canConnect, bool runtimeMissing, string detail = "")
    {
        grokAccountText.Text = account.Length > 0 ? account : "연결된 Grok 계정 없음";
        grokAccountText.ToolTip = grokAccountText.Text;
        grokConnectButton.Content = actionText.Length > 0 ? actionText : "Grok에서 로그인";
        grokConnectButton.IsEnabled = canConnect;
        grokAccountHint.Text = runtimeMissing ? "Grok Build를 설치하거나 실행 파일을 선택해 주세요."
            : "이 PC의 Grok 로그인과 공유해요. 로그인은 공식 Grok 창에서 진행해요.";
        grokExecutableButton.SetResourceReference(Control.BackgroundProperty, runtimeMissing ? "AccentBrush" : "HoverBrush");
        grokExecutableButton.SetResourceReference(Control.ForegroundProperty, runtimeMissing ? "ActionTextBrush" : "TextBrush");
        grokConnectionDetail.Text = detail;
        grokConnectionDetail.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void UpdateSettings(AppSettings value)
    {
        settings = Normalize(value);
        RefreshControls();
    }

    private Grid BuildWindow()
    {
        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "CardBrush");
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 12) };
        body.Children.Add(Text("위젯 설정", 23, false));
        var intro = Text("변경한 설정은 바로 위젯에 적용돼요.", 11);
        intro.Margin = new Thickness(0, 5, 0, 22);
        body.Children.Add(intro);
        body.Children.Add(BuildDisplay());
        body.Children.Add(BuildAppearance());
        body.Children.Add(BuildLayout());
        body.Children.Add(BuildConnection());
        body.Children.Add(BuildClaudeConnection());
        body.Children.Add(BuildGrokConnection());
        root.Children.Add(new ScrollViewer
        {
            Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        feedback.FontSize = 11;
        feedback.TextWrapping = TextWrapping.Wrap;
        feedback.Margin = new Thickness(22, 5, 22, 0);
        feedback.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        feedback.Visibility = Visibility.Collapsed;
        Grid.SetRow(feedback, 1);
        root.Children.Add(feedback);
        var footer = new Grid { Margin = new Thickness(22, 8, 22, 15) };
        footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var quit = Button("앱 종료", () => QuitRequested());
        quit.HorizontalAlignment = HorizontalAlignment.Left;
        quit.SetResourceReference(Control.ForegroundProperty, "SecondaryBrush");
        footer.Children.Add(quit);
        var close = Button("닫기", Close);
        close.Width = 85;
        close.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
        close.SetResourceReference(Control.ForegroundProperty, "ActionTextBrush");
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private StackPanel BuildDisplay()
    {
        var section = Section("표시");
        pinOption.Content = "항상 위에 표시";
        pinOption.Margin = new Thickness(0, 0, 0, 12);
        pinOption.Checked += (_, _) => ChangePin();
        pinOption.Unchecked += (_, _) => ChangePin();
        AutomationProperties.SetName(pinOption, "항상 위에 표시");
        section.Children.Add(pinOption);
        AddChoices(fiveHourOption, ["자동 표시", "숨기기"], "5시간 한도 표시");
        fiveHourOption.SelectionChanged += (_, _) =>
        {
            if (!synchronizing && fiveHourOption.SelectedIndex >= 0)
                Publish(settings with { FiveHourDisplay = (FiveHourDisplay)fiveHourOption.SelectedIndex });
        };
        section.Children.Add(Row("5시간 한도", fiveHourOption));
        resetCountdownOption.Content = "초기화까지 남은 시간 표시";
        resetCountdownOption.Margin = new Thickness(0, 0, 0, 12);
        resetCountdownOption.Checked += (_, _) => ChangeResetCountdown();
        resetCountdownOption.Unchecked += (_, _) => ChangeResetCountdown();
        AutomationProperties.SetName(resetCountdownOption, "초기화까지 남은 시간 표시");
        section.Children.Add(resetCountdownOption);
        ConfigureSlider(scaleOption, 80, 150, "위젯 크기");
        ConfigureSlider(opacityOption, 60, 100, "위젯 불투명도");
        scaleOption.ValueChanged += (_, e) =>
        {
            if (!synchronizing)
                Publish(settings with { Scale = Math.Round(e.NewValue) / 100 });
        };
        opacityOption.ValueChanged += (_, e) =>
        {
            if (!synchronizing)
                Publish(settings with { Opacity = Math.Round(e.NewValue) / 100 });
        };
        section.Children.Add(SliderRow("크기", scaleOption, scaleValue));
        section.Children.Add(SliderRow("불투명도", opacityOption, opacityValue));
        return section;
    }

    private StackPanel BuildAppearance()
    {
        var section = Section("색상");
        AddChoices(themeOption, ["시스템 설정", "라이트", "다크", "미드나이트", "민트", "라벤더", "샌드", "직접 선택"], "화면 테마");
        themeOption.SelectionChanged += (_, _) =>
        {
            if (!synchronizing && themeOption.SelectedIndex >= 0)
                Publish(settings with { Theme = (WidgetTheme)themeOption.SelectedIndex });
        };
        section.Children.Add(Row("화면 테마", themeOption));
        var colors = new Grid { Margin = new Thickness(-3, 0, -3, 10) };
        for (var index = 0; index < 3; index++)
            colors.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        AddColorButton(colors, "Background", "바탕", 0);
        AddColorButton(colors, "Foreground", "글자", 1);
        AddColorButton(colors, "Accent", "강조", 2);
        section.Children.Add(colors);
        var files = new Grid();
        files.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        files.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var import = Button("테마 불러오기", ImportAppearance);
        import.Margin = new Thickness(0, 0, 4, 0);
        import.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        files.Children.Add(import);
        var export = Button("테마 내보내기", ExportAppearance);
        export.Margin = new Thickness(4, 0, 0, 0);
        export.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        Grid.SetColumn(export, 1);
        files.Children.Add(export);
        section.Children.Add(files);
        var reset = Button("외형 기본값으로", ResetAppearance);
        reset.Margin = new Thickness(0, 5, 0, 0);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.FontSize = 10;
        reset.SetResourceReference(Control.ForegroundProperty, "SecondaryBrush");
        section.Children.Add(reset);
        return section;
    }

    private StackPanel BuildLayout()
    {
        var section = Section("레이아웃");
        layoutSummary.FontSize = 11;
        layoutSummary.Margin = new Thickness(0, 0, 0, 8);
        layoutSummary.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        section.Children.Add(layoutSummary);
        var actions = new Grid();
        actions.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var edit = Button("레이아웃 편집", EditLayout);
        edit.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        actions.Children.Add(edit);
        automaticLayoutButton.Content = "자동 배치로";
        automaticLayoutButton.Margin = new Thickness(7, 0, 0, 0);
        automaticLayoutButton.Click += (_, _) => Publish(settings with { Layout = settings.Layout with { Enabled = false } }, "자동 배치로 전환했어요.");
        Grid.SetColumn(automaticLayoutButton, 1);
        actions.Children.Add(automaticLayoutButton);
        section.Children.Add(actions);
        return section;
    }

    private StackPanel BuildConnection()
    {
        var section = Section("Codex 계정");
        accountText.TextTrimming = TextTrimming.CharacterEllipsis;
        accountText.Margin = new Thickness(0, 0, 0, 4);
        section.Children.Add(accountText);
        accountHint.FontSize = 10;
        accountHint.TextWrapping = TextWrapping.Wrap;
        accountHint.Margin = new Thickness(0, 0, 0, 10);
        accountHint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        section.Children.Add(accountHint);
        connectionDetail.FontSize = 11;
        connectionDetail.TextWrapping = TextWrapping.Wrap;
        connectionDetail.Margin = new Thickness(0, 0, 0, 10);
        connectionDetail.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        section.Children.Add(connectionDetail);
        connectButton.Margin = new Thickness(0, 0, 0, 7);
        connectButton.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        connectButton.Click += (_, _) => LoginRequested();
        section.Children.Add(connectButton);
        executableButton.Content = "Codex 실행 파일 선택";
        executableButton.Click += (_, _) => ExecutableRequested();
        section.Children.Add(executableButton);
        return section;
    }

    private StackPanel BuildClaudeConnection()
    {
        var section = Section("Claude 계정");
        claudeAccountText.TextTrimming = TextTrimming.CharacterEllipsis;
        claudeAccountText.Margin = new Thickness(0, 0, 0, 4);
        section.Children.Add(claudeAccountText);
        claudeAccountHint.FontSize = 10;
        claudeAccountHint.TextWrapping = TextWrapping.Wrap;
        claudeAccountHint.Margin = new Thickness(0, 0, 0, 10);
        claudeAccountHint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        section.Children.Add(claudeAccountHint);
        claudeConnectionDetail.FontSize = 11;
        claudeConnectionDetail.TextWrapping = TextWrapping.Wrap;
        claudeConnectionDetail.Margin = new Thickness(0, 0, 0, 10);
        claudeConnectionDetail.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        section.Children.Add(claudeConnectionDetail);
        claudeConnectButton.Margin = new Thickness(0, 0, 0, 7);
        claudeConnectButton.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        claudeConnectButton.Click += (_, _) => ClaudeLoginRequested();
        section.Children.Add(claudeConnectButton);
        claudeExecutableButton.Content = "Claude 실행 파일 선택";
        claudeExecutableButton.Click += (_, _) => ClaudeExecutableRequested();
        section.Children.Add(claudeExecutableButton);
        return section;
    }

    private StackPanel BuildGrokConnection()
    {
        var section = Section("Grok 계정");
        grokAccountText.TextTrimming = TextTrimming.CharacterEllipsis;
        grokAccountText.Margin = new Thickness(0, 0, 0, 4);
        section.Children.Add(grokAccountText);
        grokAccountHint.FontSize = 10;
        grokAccountHint.TextWrapping = TextWrapping.Wrap;
        grokAccountHint.Margin = new Thickness(0, 0, 0, 10);
        grokAccountHint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        section.Children.Add(grokAccountHint);
        grokConnectionDetail.FontSize = 11;
        grokConnectionDetail.TextWrapping = TextWrapping.Wrap;
        grokConnectionDetail.Margin = new Thickness(0, 0, 0, 10);
        grokConnectionDetail.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        section.Children.Add(grokConnectionDetail);
        grokConnectButton.Margin = new Thickness(0, 0, 0, 7);
        grokConnectButton.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        grokConnectButton.Click += (_, _) => GrokLoginRequested();
        section.Children.Add(grokConnectButton);
        grokExecutableButton.Content = "Grok 실행 파일 선택";
        grokExecutableButton.Click += (_, _) => GrokExecutableRequested();
        section.Children.Add(grokExecutableButton);
        return section;
    }

    private void ChangePin()
    {
        if (!synchronizing)
            Publish(settings with { AlwaysOnTop = pinOption.IsChecked == true });
    }

    private void Publish(AppSettings updated, string message = "")
    {
        settings = Normalize(updated);
        if (message.Length > 0)
            ShowFeedback(message);
        SettingsChanged(settings);
        RefreshControls();
    }

    private void ChangeResetCountdown()
    {
        if (!synchronizing)
            Publish(settings with { ShowResetCountdown = resetCountdownOption.IsChecked == true });
    }

    private void RefreshControls()
    {
        synchronizing = true;
        pinOption.IsChecked = settings.AlwaysOnTop;
        fiveHourOption.SelectedIndex = (int)settings.FiveHourDisplay;
        resetCountdownOption.IsChecked = settings.ShowResetCountdown;
        themeOption.SelectedIndex = (int)settings.Theme;
        scaleOption.Value = settings.Scale * 100;
        opacityOption.Value = settings.Opacity * 100;
        scaleValue.Text = $"{settings.Scale * 100:0}%";
        opacityValue.Text = $"{settings.Opacity * 100:0}%";
        layoutSummary.Text = settings.Layout.Enabled ? $"사용자 배치 · {settings.Layout.Blocks.Count}개 블록" : "자동 배치";
        automaticLayoutButton.IsEnabled = settings.Layout.Enabled;
        var colors = settings.Theme == WidgetTheme.Custom ? settings.CustomColors : CurrentColors();
        UpdateSwatch("Background", colors.Background);
        UpdateSwatch("Foreground", colors.Foreground);
        UpdateSwatch("Accent", colors.Accent);
        synchronizing = false;
    }

    private void AddColorButton(Grid parent, string channel, string title, int column)
    {
        var content = new StackPanel();
        var swatch = new Border { Height = 15, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 5) };
        swatch.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        swatch.BorderThickness = new Thickness(1);
        swatches.Add(channel, swatch);
        content.Children.Add(swatch);
        var name = Text(title, 10, false);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(name);
        var value = Text("", 9);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        colorValues.Add(channel, value);
        content.Children.Add(value);
        var button = Button("", () => ChooseColor(channel));
        button.Content = content;
        button.Padding = new Thickness(8);
        button.Margin = new Thickness(3, 0, 3, 0);
        button.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        AutomationProperties.SetName(button, $"{title} 색상 선택");
        Grid.SetColumn(button, column);
        parent.Children.Add(button);
    }

    private void UpdateSwatch(string channel, string value)
    {
        swatches[channel].Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        colorValues[channel].Text = value;
    }

    private void ChooseColor(string channel)
    {
        var colors = CurrentColors();
        var initial = channel == "Background" ? colors.Background : channel == "Foreground" ? colors.Foreground : colors.Accent;
        using var dialog = new Forms.ColorDialog
        {
            Color = System.Drawing.ColorTranslator.FromHtml(initial), FullOpen = true
        };
        if (dialog.ShowDialog(new NativeDialogOwner(new WindowInteropHelper(this).EnsureHandle())) != Forms.DialogResult.OK)
            return;
        var selected = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        colors = channel == "Background" ? colors with { Background = selected }
            : channel == "Foreground" ? colors with { Foreground = selected } : colors with { Accent = selected };
        Publish(settings with { Theme = WidgetTheme.Custom, CustomColors = colors }, "선택한 색상을 적용했어요.");
    }

    private ThemeColors CurrentColors() => new()
    {
        Background = ColorHex("CardBrush"), Foreground = ColorHex("TextBrush"), Accent = ColorHex("AccentBrush")
    };

    private string ColorHex(string key)
    {
        var color = FindResource(key) is SolidColorBrush brush ? brush.Color : Colors.Gray;
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void EditLayout()
    {
        var editor = new LayoutEditorWindow(settings.Layout, getContext()) { Owner = this };
        if (editor.ShowDialog() == true)
            Publish(settings with { Layout = editor.Result }, "레이아웃을 적용했어요.");
    }

    private void ExportAppearance()
    {
        var dialog = new SaveFileDialog
        {
            Title = "테마 내보내기", Filter = "Usage Monitor 테마 (*.json)|*.json",
            FileName = "My-UsageMonitor.theme.json", AddExtension = true, DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        var profile = new AppearanceProfile
        {
            Theme = settings.Theme, CustomColors = settings.CustomColors, Scale = settings.Scale,
            Opacity = settings.Opacity, Layout = settings.Layout
        };
        ShowFeedback(new AppearanceProfileStore().TryExport(dialog.FileName, profile)
            ? "색상·크기·배치를 테마 파일로 저장했어요." : "테마 파일을 저장하지 못했어요.");
    }

    private void ImportAppearance()
    {
        var dialog = new OpenFileDialog
        {
            Title = "테마 불러오기", Filter = "Usage Monitor 테마 (*.json)|*.json", CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return;
        if (!new AppearanceProfileStore().TryImport(dialog.FileName, out var profile))
        {
            ShowFeedback("이 테마 파일을 읽을 수 없어요. 지원하는 형식인지 확인해 주세요.");
            return;
        }

        Publish(settings with
        {
            Theme = profile.Theme, CustomColors = profile.CustomColors, Scale = profile.Scale,
            Opacity = profile.Opacity, Layout = profile.Layout
        }, "테마를 불러왔어요.");
    }

    private void ResetAppearance() => Publish(settings with
    {
        Theme = WidgetTheme.System, CustomColors = new(), Scale = 1, Opacity = 1, Layout = new()
    }, "외형을 기본값으로 되돌렸어요.");

    private static AppSettings Normalize(AppSettings settings)
    {
        var appearance = AppearanceProfile.Normalize(new()
        {
            Theme = settings.Theme, CustomColors = settings.CustomColors, Scale = settings.Scale,
            Opacity = settings.Opacity, Layout = settings.Layout
        });
        return settings with
        {
            FiveHourDisplay = Enum.IsDefined(settings.FiveHourDisplay) ? settings.FiveHourDisplay : FiveHourDisplay.Automatic,
            Theme = appearance.Theme, CustomColors = appearance.CustomColors, Scale = appearance.Scale,
            Opacity = appearance.Opacity, Layout = appearance.Layout
        };
    }

    private static StackPanel Section(string title)
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 19) };
        var heading = Text(title, 13, false);
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new Thickness(0, 0, 0, 11);
        section.Children.Add(heading);
        return section;
    }

    private static Grid Row(string title, FrameworkElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = Text(title, 12, false);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static StackPanel SliderRow(string title, Slider slider, TextBlock value)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        value.FontSize = 11;
        value.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryBrush");
        var labelRow = Row(title, value);
        labelRow.Margin = new Thickness(0, 0, 0, 5);
        stack.Children.Add(labelRow);
        stack.Children.Add(slider);
        return stack;
    }

    private static void ConfigureSlider(Slider slider, double minimum, double maximum, string name)
    {
        slider.Minimum = minimum;
        slider.Maximum = maximum;
        slider.SmallChange = 1;
        slider.LargeChange = 5;
        slider.TickFrequency = 5;
        slider.IsSnapToTickEnabled = true;
        slider.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        AutomationProperties.SetName(slider, name);
    }

    private static void AddChoices(ComboBox box, string[] values, string name)
    {
        foreach (var value in values)
            box.Items.Add(new ComboBoxItem { Content = value });
        box.MinWidth = 136;
        AutomationProperties.SetName(box, name);
    }

    private static TextBlock Text(string value, double size, bool secondary = true)
    {
        var text = new TextBlock { Text = value, FontSize = size };
        text.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "SecondaryBrush" : "TextBrush");
        return text;
    }

    private static Button Button(string content, Action action)
    {
        var button = new Button { Content = content };
        button.Click += (_, _) => action();
        return button;
    }

    private sealed class NativeDialogOwner(IntPtr handle) : Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
