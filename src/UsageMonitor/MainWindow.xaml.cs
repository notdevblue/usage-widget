using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using UsageMonitor.Core;
using UsageMonitor.Layout;
using UsageMonitor.Presentation;
using UsageMonitor.Providers;
using UsageMonitor.Services;
using Forms = System.Windows.Forms;

namespace UsageMonitor;

public partial class MainWindow : Window
{
    private readonly WidgetViewModel _view = new();
    private readonly SettingsStore _store = new();
    private readonly CodexExecutableLocator _codexLocator = new();
    private readonly ClaudeExecutableLocator _claudeLocator = new();
    private readonly GrokExecutableLocator _grokLocator = new();
    private readonly UsageProviderCatalog _catalog = new();
    private readonly RefreshSchedule _schedule = new();
    private readonly Dictionary<string, SettingsWindow> _dialogs = new();
    private readonly ProviderRuntime[] _providers = [new("codex", "Codex"), new("claude", "Claude"), new("grok", "Grok")];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly DispatcherTimer _clockTimer = new();
    private readonly DispatcherTimer _resetNewsTimer = new();
    private readonly HttpClient _resetNewsClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly CodexResetNewsService _resetNews;
    private Task _resetNewsTask = Task.CompletedTask;
    private readonly EventHandler _displayChanged;
    private readonly Forms.NotifyIcon _tray;
    private readonly System.Drawing.Icon _trayIcon;
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle _showWait;
    private readonly bool _preview;
    private AppSettings _settings = new();
    private bool _ready;
    private bool _quitting;
    private bool _placed;
    private string _settingsError = "";

    public MainWindow(string preview)
    {
        _preview = preview.Length > 0;
        if (_preview)
            _settings = new AppSettings { Theme = WidgetTheme.Light };
        else
            _settings = _store.Load();
        _displayChanged = (sender, args) => Dispatcher.BeginInvoke(ClampToScreen);
        InitializeComponent();
        WidgetDragInput.Attach(WidgetContent, BeginWidgetDrag);
        ShowInTaskbar = _preview;
        DataContext = _view;
        _view.Preview = _preview;
        _resetNews = new(_resetNewsClient);
        ConfigureResetNews(preview);
        ApplyTheme();
        ApplyAppearance();
        Topmost = _settings.AlwaysOnTop;
        if (_settings.HasPosition) { Left = _settings.Left; Top = _settings.Top; }
        foreach (var runtime in _providers)
        {
            runtime.StateChanged = (sender, args) => Dispatcher.BeginInvoke(() => _ = RefreshProviderAsync(runtime));
            if (_preview)
            {
                runtime.Location = new() { Found = preview != "missing", Source = "미리보기" };
                AttachProvider(runtime, new PreviewUsageProvider(preview, runtime.Id));
            }
            else
            {
                runtime.Location = Locate(runtime.Id);
                if (runtime.Location.Found)
                    AttachProvider(runtime, _catalog.Create(runtime.Id, runtime.Location.Path));
            }
        }

        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/UsageMonitor;component/Assets/widget.ico"))
            ?? throw new InvalidOperationException("위젯 아이콘을 불러오지 못했어요.");
        using (resource.Stream)
            _trayIcon = new System.Drawing.Icon(resource.Stream);
        _tray = new Forms.NotifyIcon { Icon = _trayIcon, Text = "Usage Monitor", Visible = true };
        ConfigureTray();
        var showEventName = "UsageMonitor.Show";
        if (_preview)
            showEventName = $"UsageMonitor.Show.Preview.{Environment.ProcessId}";
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (state, timedOut) => Dispatcher.BeginInvoke(ShowWidget), new object(), Timeout.Infinite, false);
        _refreshTimer.Tick += (sender, args) => { _refreshTimer.Stop(); _ = RefreshAsync(false); };
        _clockTimer.Interval = TimeSpan.FromSeconds(30);
        _clockTimer.Tick += (sender, args) => RenderSnapshots();
        Loaded += async (sender, args) =>
        {
            PlaceWindow();
            _placed = true;
            _ready = true;
            RenderSnapshots();
            await RefreshAsync();
            _clockTimer.Start();
        };
        SizeChanged += (sender, args) => { if (_placed) ClampToScreen(); };
        IsVisibleChanged += (sender, args) => { if (_ready) RescheduleProviders(); };
        Closing += (sender, args) => { if (!_quitting) { args.Cancel = true; Hide(); } };
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
        SystemEvents.PowerModeChanged += PowerModeChanged;
        SystemEvents.DisplaySettingsChanged += _displayChanged;
    }

    private void ConfigureResetNews(string preview)
    {
        WidgetContent.ResetNoticeRequested += OpenResetNotice;
        if (_preview)
        {
            ApplyPreviewResetNotice(preview);
            return;
        }

        _resetNewsTimer.Interval = TimeSpan.FromSeconds(30);
        _resetNewsTimer.Tick += (sender, args) => RefreshResetNews();
        Loaded += (sender, args) =>
        {
            RefreshResetNews();
            _resetNewsTimer.Start();
        };
    }

    private void ApplyPreviewResetNotice(string preview)
    {
        var text = "";
        if (preview == "reset-scheduled")
            text = $"Codex 리셋 · {DateTimeOffset.Now.AddHours(2):M/d HH:mm} 예정";
        else if (preview == "reset-announced")
            text = "Codex 리셋 예고됨";
        _view.ApplyResetNotice(new()
        {
            Text = text,
            ToolTip = "미리보기 · 실제 리셋 공지가 아니에요.",
            SourceUrl = "https://x.com/thsottiaux/status/2114588174249071102"
        }, DateTimeOffset.Now);
    }

    private void RefreshResetNews()
    {
        if (_quitting || _preview || !_resetNewsTask.IsCompleted)
            return;
        _resetNewsTask = RefreshResetNewsAsync();
    }

    private async Task RefreshResetNewsAsync()
    {
        try
        {
            var notice = await _resetNews.ReadAsync(_lifetime.Token);
            if (_quitting == false)
                _view.ApplyResetNotice(notice, DateTimeOffset.Now);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private void OpenResetNotice(string sourceUrl)
    {
        try
        {
            Process.Start(new ProcessStartInfo(sourceUrl) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            _view.ResetNoticeToolTip = "기본 브라우저를 열지 못했어요. 브라우저 설정을 확인해주세요.";
        }
    }

    private void ConfigureTray()
    {
        var menu = new Forms.ContextMenuStrip();
        var visibility = new Forms.ToolStripMenuItem("위젯 숨기기");
        visibility.Click += (sender, args) => Dispatcher.Invoke(() => { if (IsVisible) Hide(); else ShowWidget(); });
        menu.Items.Add(visibility);
        menu.Items.Add("설정", image: default, (sender, args) => Dispatcher.Invoke(OpenSettings));
        var pin = new Forms.ToolStripMenuItem("항상 위에 표시");
        pin.Click += (sender, args) => Dispatcher.Invoke(() =>
        {
            _settings = _settings with { AlwaysOnTop = !_settings.AlwaysOnTop };
            Topmost = _settings.AlwaysOnTop;
            SaveSettings();
            foreach (var dialog in _dialogs.Values)
                dialog.UpdateSettings(_settings);
        });
        menu.Items.Add(pin);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", image: default, (sender, args) => Dispatcher.BeginInvoke(() => _ = QuitAsync()));
        menu.Opening += (sender, args) =>
        {
            visibility.Text = IsVisible ? "위젯 숨기기" : "위젯 표시";
            pin.Checked = _settings.AlwaysOnTop;
        };
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (sender, args) => Dispatcher.Invoke(ShowWidget);
    }

    private void OpenSettings()
    {
        if (_dialogs.TryGetValue("settings", out var existing)) { existing.Activate(); return; }
        var window = new SettingsWindow(_settings, BuildLayoutContext) { Owner = this };
        _dialogs["settings"] = window;
        window.SettingsChanged += value =>
        {
            _settings = _settings with
            {
                AlwaysOnTop = value.AlwaysOnTop, FiveHourDisplay = value.FiveHourDisplay,
                ShowResetCountdown = value.ShowResetCountdown,
                Theme = value.Theme, CustomColors = value.CustomColors, Scale = value.Scale,
                Opacity = value.Opacity, Layout = value.Layout
            };
            Topmost = _settings.AlwaysOnTop;
            ApplyTheme();
            ApplyAppearance();
            SaveSettings();
            RenderSnapshots();
        };
        window.LoginRequested += () => _ = ConnectAsync(_providers[0]);
        window.ExecutableRequested += () => _ = ChooseExecutableAsync(_providers[0]);
        window.ClaudeLoginRequested += () => OpenClaudeLogin(Runtime("claude"));
        window.ClaudeExecutableRequested += () => _ = ChooseExecutableAsync(Runtime("claude"));
        window.GrokLoginRequested += () => OpenGrokLogin(Runtime("grok"));
        window.GrokExecutableRequested += () => _ = ChooseExecutableAsync(Runtime("grok"));
        window.QuitRequested += () => _ = QuitAsync();
        window.Closed += (sender, args) => _dialogs.Remove("settings");
        UpdateView();
        window.Show();
    }

    private ProviderRuntime Runtime(string id) => _providers.Single(runtime => runtime.Id == id);

    private ExecutableLocation Locate(string id, string preferredPath = "") => id switch
    {
        "claude" => _claudeLocator.Find(preferredPath.Length > 0 ? preferredPath : _settings.ClaudePath),
        "grok" => _grokLocator.Find(preferredPath.Length > 0 ? preferredPath : _settings.GrokPath),
        _ => _codexLocator.Find(preferredPath.Length > 0 ? preferredPath : _settings.CodexPath)
    };

    private static void AttachProvider(ProviderRuntime runtime, IUsageProvider provider)
    {
        runtime.Provider = provider;
        if (provider is CodexUsageProvider codex)
            codex.StateChanged += runtime.StateChanged;
    }

    private async Task RefreshAsync(bool force = true)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            await Task.WhenAll(_providers.Where(runtime => force || runtime.NextRefreshAt <= now).Select(RefreshProviderAsync));
        }
        finally
        {
            ScheduleNext();
        }
    }

    private async Task RefreshProviderAsync(ProviderRuntime runtime)
    {
        if (_quitting || runtime.LoginPending || runtime.Transitioning || !await runtime.Gate.WaitAsync(0))
            return;
        runtime.Refreshing = true;
        var generation = runtime.Generation;
        ScheduleNext();
        try
        {
            runtime.View.Busy = true;
            if (!runtime.HasSnapshot)
                runtime.View.Status = "연결 확인 중";
            UpdateView();
            if (!runtime.Location.Found)
            {
                if (!_preview)
                {
                    runtime.Location = Locate(runtime.Id);
                    if (runtime.Location.Found)
                        AttachProvider(runtime, _catalog.Create(runtime.Id, runtime.Location.Path));
                }
                if (!runtime.Location.Found)
                {
                    runtime.Failures = Math.Min(runtime.Failures + 1, 5);
                    RenderMissingRuntime(runtime);
                    RenderSnapshots();
                    return;
                }
            }
            var snapshot = await runtime.Provider.ReadAsync(_lifetime.Token);
            if (runtime.LoginPending || _quitting || generation != runtime.Generation)
                return;
            runtime.Snapshot = snapshot;
            runtime.HasSnapshot = true;
            runtime.Failures = 0;
            runtime.View.Stale = snapshot.IsStale;
            runtime.View.Detail = snapshot.StatusDetail;
            RenderSnapshots();
        }
        catch (Exception exception)
        {
            if (runtime.LoginPending || _quitting || generation != runtime.Generation)
                return;
            runtime.Failures = Math.Min(runtime.Failures + 1, 5);
            runtime.View.Stale = runtime.HasSnapshot;
            runtime.View.Detail = FriendlyError(exception, runtime.Name);
            if (!runtime.HasSnapshot)
            {
                runtime.View.EmptyTitle = "연결 대기";
                runtime.View.EmptyBody = $"트레이 아이콘 → 설정에서 {runtime.Name} 연결을 확인해주세요.";
            }
            RenderSnapshots();
            runtime.View.Status = runtime.HasSnapshot ? "연결 지연 · 마지막 수신 값" : "연결 대기 중";
            UpdateView();
        }
        finally
        {
            runtime.View.Busy = false;
            runtime.Refreshing = false;
            runtime.Gate.Release();
            runtime.NextRefreshAt = DateTimeOffset.UtcNow + (generation == runtime.Generation ? NextDelay(runtime) : TimeSpan.FromSeconds(5));
            ScheduleNext();
        }
    }

    private TimeSpan NextDelay(ProviderRuntime runtime) => _schedule.NextDelay(IsVisible, runtime.Failures,
        runtime.Snapshot.Windows.Select(window => window.ResetsAt).ToArray(), DateTimeOffset.UtcNow, _settings.RefreshSeconds);

    private void RescheduleProviders()
    {
        foreach (var runtime in _providers.Where(runtime => !runtime.Refreshing))
            runtime.NextRefreshAt = DateTimeOffset.UtcNow + NextDelay(runtime);
        ScheduleNext();
    }

    private void ScheduleNext()
    {
        if (!_ready || _quitting)
            return;
        _refreshTimer.Stop();
        var available = _providers.Where(runtime => !runtime.LoginPending && !runtime.Refreshing && !runtime.Transitioning).ToArray();
        if (available.Length == 0)
            return;
        var delay = available.Min(runtime => runtime.NextRefreshAt) - DateTimeOffset.UtcNow;
        _refreshTimer.Interval = delay < TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : delay;
        _refreshTimer.Start();
    }

    private UsageWindow[] VisibleWindows(ProviderRuntime runtime) => runtime.Snapshot.Windows
        .Where(window => _settings.FiveHourDisplay != FiveHourDisplay.Hidden || !window.IsFiveHour).ToArray();

    private UsageWindow[] DisplayWindows(ProviderRuntime runtime)
    {
        var windows = VisibleWindows(runtime);
        if (runtime.Id != "claude")
            return windows;
        var common = windows.Where(window => window.IsFiveHour || window.Id.StartsWith("claude:weekly_all:", StringComparison.Ordinal) || window.Id == "weekly").ToArray();
        return common.Length > 0 ? common : windows;
    }

    private void RenderSnapshots()
    {
        if (_quitting)
            return;
        foreach (var runtime in _providers)
        {
            if (!runtime.LoginPending && runtime.HasSnapshot)
                RenderProvider(runtime);
            else if (!runtime.Location.Found)
                RenderMissingRuntime(runtime);
        }
        var custom = _settings.Layout.Enabled && _providers.Any(runtime => VisibleWindows(runtime).Length > 0);
        _view.CustomLayoutVisible = custom;
        var context = BuildLayoutContext();
        var covered = custom ? _settings.Layout.Blocks
            .Where(block => block.Kind is LayoutBlockKind.Account or LayoutBlockKind.Status || UsageLayoutCanvas.TryGetWindow(context, block, out _))
            .Select(block => block.ProviderId).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        _view.Providers.Clear();
        foreach (var runtime in _providers.Where(runtime => !covered.Contains(runtime.Id)))
            _view.Providers.Add(runtime.View);
        _view.AutomaticLayoutVisible = _view.Providers.Count > 0;
        if (custom)
            WidgetContent.LayoutCanvas.Render(_settings.Layout, context);
        var trayText = string.Join(" · ", _providers.Select(runtime => runtime.View.Metrics.Count > 0
            ? $"{runtime.Name} {runtime.View.Metrics[0].Label} {runtime.View.Metrics[0].Amount}%"
            : $"{runtime.Name} {runtime.View.EmptyTitle}"));
        _tray.Text = trayText.Length > 63 ? trayText[..63] : trayText;
        UpdateView();
    }

    private void RenderProvider(ProviderRuntime runtime)
    {
        var view = runtime.View;
        var account = runtime.Snapshot.Account;
        var connected = account.Kind is AccountKind.ChatGpt or AccountKind.Claude or AccountKind.Grok;
        view.Metrics.Clear();
        foreach (var window in DisplayWindows(runtime))
            view.Metrics.Add(new UsageMetric(window, DateTimeOffset.Now, BrushResource("AccentBrush"), BrushResource("WarningBrush"), BrushResource("ErrorBrush")));
        view.Plan = FormatPlan(account.Plan);
        view.Account = account.Email.Length > 0 ? account.Email : connected ? $"연결된 {runtime.Name} 계정" : "연결된 계정 없음";
        view.RuntimeMissing = false;
        view.ActionText = runtime.Id switch
        {
            "claude" => "Claude에서 로그인",
            "grok" => "Grok에서 로그인",
            _ => connected ? "다른 계정으로 로그인" : "ChatGPT로 로그인"
        };
        view.EmptyTitle = connected && runtime.Snapshot.IsStale && runtime.Snapshot.Windows.Count == 0 ? "사용량 조회 대기" : account.Kind switch
        {
            AccountKind.SignedOut => "로그인 필요",
            AccountKind.ApiKey or AccountKind.Other => "구독 로그인 필요",
            _ => "한도 미제공"
        };
        view.EmptyBody = connected && runtime.Snapshot.IsStale && runtime.Snapshot.Windows.Count == 0 ? runtime.Snapshot.StatusDetail
            : connected && runtime.Snapshot.Windows.Count > 0 ? "설정에서 5시간 한도를 켜주세요."
            : connected ? "계정에서 한도를 제공하지 않았어요."
            : $"트레이 아이콘 → 설정에서 {runtime.Name} 구독 계정을 연결해주세요.";
        view.Status = view.Stale ? $"{FormatAge(runtime.Snapshot)} · 마지막 값 / 조회 대기"
            : _preview ? "미리보기 · 실제 사용량이 아니에요"
            : runtime.Snapshot.IsCached ? $"{FormatAge(runtime.Snapshot)} 갱신 · 로컬 캐시"
            : connected ? $"{FormatAge(runtime.Snapshot)} 동기화" : "계정 연결 대기 중";
        view.StatusBrush = BrushResource(view.Stale ? "WarningBrush" : "AccentBrush");
    }

    private static void RenderMissingRuntime(ProviderRuntime runtime)
    {
        var view = runtime.View;
        view.Metrics.Clear();
        view.Plan = "";
        view.Account = "연결된 계정 없음";
        view.EmptyTitle = "설치 필요";
        view.EmptyBody = $"트레이 아이콘 → 설정에서 {runtime.Name}를 설치하거나 연결해주세요.";
        view.Detail = "";
        view.RuntimeMissing = true;
        view.ActionText = $"{runtime.Name} 설치 안내";
        view.Status = $"{runtime.Name} 실행 파일을 찾고 있어요";
    }

    private void UpdateView()
    {
        foreach (var runtime in _providers)
        {
            var view = runtime.View;
            view.ShowResetCountdown = _settings.ShowResetCountdown;
            var allMetrics = VisibleWindows(runtime).Select(window => new UsageMetric(window, DateTimeOffset.Now,
                BrushResource("AccentBrush"), BrushResource("WarningBrush"), BrushResource("ErrorBrush"))).ToArray();
            var limits = allMetrics.Length > 0
                ? string.Join("\n", allMetrics.Select(metric => $"{metric.Label}: {metric.Amount}% 남음 · {metric.Reset}"))
                : view.EmptyBody;
            view.HoverText = $"{runtime.Name} {view.Plan}\n{limits}\n{view.Status}";
            if (view.Detail.Length > 0)
                view.HoverText += $"\n{view.Detail}";
            view.Update();
        }
        _view.Stale = _providers.Any(runtime => runtime.View.Stale);
        _view.HoverText = string.Join("\n\n", _providers.Select(runtime => runtime.View.HoverText));
        if (_settingsError.Length > 0)
            _view.HoverText += $"\n{_settingsError}";
        foreach (var dialog in _dialogs.Values)
        {
            var codex = _providers[0].View;
            var claude = _providers[1].View;
            dialog.UpdateConnection(codex.Stale ? $"{codex.Account} (마지막 조회)" : codex.Account, codex.ActionText, !_preview && !_providers[0].Transitioning, codex.RuntimeMissing, codex.Detail);
            dialog.UpdateClaudeConnection(claude.Stale ? $"{claude.Account} (마지막 조회)" : claude.Account, claude.ActionText,
                !_preview && !_providers[1].Transitioning && !_providers[1].LoginPending, claude.RuntimeMissing, claude.Detail);
            var grokRuntime = Runtime("grok");
            var grok = grokRuntime.View;
            dialog.UpdateGrokConnection(grok.Stale ? $"{grok.Account} (마지막 조회)" : grok.Account, grok.ActionText,
                !_preview && !grokRuntime.Transitioning && !grokRuntime.LoginPending, grok.RuntimeMissing, grok.Detail);
        }
    }

    private async Task ConnectAsync(ProviderRuntime runtime)
    {
        if (_preview || _quitting || runtime.Transitioning)
            return;
        if (!runtime.Location.Found) { OpenUrl("https://developers.openai.com/codex/cli", runtime); return; }
        if (runtime.LoginPending)
        {
            runtime.CancelLoginRequested = true;
            runtime.View.ActionText = "로그인 취소 중";
            UpdateView();
            try { if (runtime.Login.Id.Length > 0) await runtime.Provider.CancelLoginAsync(runtime.Login.Id, _lifetime.Token); }
            catch (Exception exception) { runtime.View.Detail = FriendlyError(exception, runtime.Name); UpdateView(); }
            return;
        }
        runtime.LoginPending = true;
        runtime.CancelLoginRequested = false;
        runtime.View.ActionText = "로그인 취소";
        runtime.View.Detail = "브라우저에서 로그인을 마치면 자동으로 연결해요.";
        runtime.View.Status = "로그인 기다리는 중";
        UpdateView();
        ScheduleNext();
        try
        {
            runtime.Login = await runtime.Provider.BeginLoginAsync(_lifetime.Token);
            if (runtime.CancelLoginRequested)
            {
                await runtime.Provider.CancelLoginAsync(runtime.Login.Id, _lifetime.Token);
                return;
            }
            if (!Uri.TryCreate(runtime.Login.AuthorizationUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !(uri.Host == "chatgpt.com" || uri.Host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Codex에서 올바른 로그인 주소를 받지 못했어요.");
            if (!OpenUrl(uri.AbsoluteUri, runtime))
            {
                await runtime.Provider.CancelLoginAsync(runtime.Login.Id, _lifetime.Token);
                return;
            }
            await runtime.Login.Completion.WaitAsync(_lifetime.Token);
            ResetConnectionPresentation(runtime);
            runtime.View.Detail = "";
        }
        catch (OperationCanceledException) { runtime.View.Detail = "로그인을 취소했어요."; }
        catch (Exception exception) { runtime.View.Detail = FriendlyError(exception, runtime.Name); }
        finally
        {
            runtime.LoginPending = false;
            runtime.Login = new();
            if (!_quitting) { await RefreshProviderAsync(runtime); UpdateView(); }
        }
    }

    private void OpenClaudeLogin(ProviderRuntime runtime)
    {
        if (_preview || _quitting || runtime.Transitioning || runtime.LoginPending)
            return;
        if (!runtime.Location.Found) { OpenUrl("https://code.claude.com/docs/en/setup", runtime); return; }
        try
        {
            var start = new ProcessStartInfo(runtime.Location.Path) { UseShellExecute = true };
            start.ArgumentList.Add("auth");
            start.ArgumentList.Add("login");
            if (Process.Start(start) is not Process process)
                throw new InvalidOperationException();
            runtime.LoginPending = true;
            ResetConnectionPresentation(runtime);
            runtime.View.EmptyTitle = "로그인 중";
            runtime.View.ActionText = "Claude 로그인 중";
            runtime.View.Detail = "열린 Claude 로그인 창에서 연결해주세요. 완료 후 자동으로 확인해요.";
            RenderSnapshots();
            ScheduleNext();
            _ = ObserveClaudeLoginAsync(runtime, process);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            runtime.View.Detail = "Claude 로그인 창을 열지 못했어요. 터미널에서 claude auth login을 실행해주세요.";
            UpdateView();
        }
    }

    private void OpenGrokLogin(ProviderRuntime runtime)
    {
        if (_preview || _quitting || runtime.Transitioning || runtime.LoginPending)
            return;
        if (!runtime.Location.Found) { OpenUrl("https://x.ai/cli", runtime); return; }
        try
        {
            var start = new ProcessStartInfo(runtime.Location.Path) { UseShellExecute = true };
            start.ArgumentList.Add("login");
            if (Process.Start(start) is not Process process)
                throw new InvalidOperationException();
            runtime.LoginPending = true;
            ResetConnectionPresentation(runtime);
            runtime.View.EmptyTitle = "로그인 중";
            runtime.View.ActionText = "Grok 로그인 중";
            runtime.View.Detail = "열린 Grok 로그인 창에서 연결해주세요. 완료 후 자동으로 확인해요.";
            RenderSnapshots();
            ScheduleNext();
            _ = ObserveGrokLoginAsync(runtime, process);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            runtime.View.Detail = "Grok 로그인 창을 열지 못했어요. 터미널에서 grok login을 실행해주세요.";
            UpdateView();
        }
    }

    private async Task ObserveGrokLoginAsync(ProviderRuntime runtime, Process process)
    {
        try { await process.WaitForExitAsync(_lifetime.Token); }
        catch (OperationCanceledException) when (_quitting) { }
        catch (InvalidOperationException) { runtime.View.Detail = "Grok 로그인 상태를 확인해주세요."; }
        finally
        {
            process.Dispose();
            runtime.LoginPending = false;
            if (!_quitting)
                await RefreshProviderAsync(runtime);
        }
    }

    private async Task ObserveClaudeLoginAsync(ProviderRuntime runtime, Process process)
    {
        try { await process.WaitForExitAsync(_lifetime.Token); }
        catch (OperationCanceledException) when (_quitting) { }
        catch (InvalidOperationException) { runtime.View.Detail = "Claude 로그인 상태를 확인해주세요."; }
        finally
        {
            process.Dispose();
            runtime.LoginPending = false;
            if (!_quitting)
                await RefreshProviderAsync(runtime);
        }
    }

    private async Task ChooseExecutableAsync(ProviderRuntime runtime)
    {
        if (_preview || runtime.LoginPending || runtime.Transitioning || _quitting)
            return;
        var fileName = $"{runtime.Id}.exe";
        var dialog = new OpenFileDialog { Title = $"{runtime.Name} 실행 파일 선택", Filter = $"{runtime.Name} 실행 파일 ({fileName})|{fileName}", CheckFileExists = true };
        var owner = _dialogs.TryGetValue("settings", out var settingsDialog) ? (Window)settingsDialog : this;
        if (dialog.ShowDialog(owner) != true)
            return;
        if (runtime.LoginPending || runtime.Transitioning || _quitting)
            return;
        var location = Locate(runtime.Id, dialog.FileName);
        if (!location.Found || !string.Equals(location.Path, Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))
        {
            runtime.View.Detail = $"{fileName} 실행 파일을 선택해주세요.";
            UpdateView();
            return;
        }
        var acquired = false;
        runtime.Transitioning = true;
        UpdateView();
        ScheduleNext();
        try
        {
            await runtime.Gate.WaitAsync(_lifetime.Token);
            acquired = true;
            if (_quitting || runtime.LoginPending)
                return;
            DetachProvider(runtime);
            await runtime.Provider.DisposeAsync();
            runtime.Location = location;
            AttachProvider(runtime, _catalog.Create(runtime.Id, location.Path));
            _settings = runtime.Id switch
            {
                "claude" => _settings with { ClaudePath = location.Path },
                "grok" => _settings with { GrokPath = location.Path },
                _ => _settings with { CodexPath = location.Path }
            };
            ResetConnectionPresentation(runtime);
            SaveSettings();
        }
        catch (OperationCanceledException) when (_quitting) { return; }
        catch (Exception exception) { runtime.View.Detail = FriendlyError(exception, runtime.Name); UpdateView(); }
        finally
        {
            if (acquired) runtime.Gate.Release();
            runtime.Transitioning = false;
            UpdateView();
            ScheduleNext();
        }
        await RefreshProviderAsync(runtime);
    }

    private void ApplyTheme()
    {
        var systemDark = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0;
        foreach (var token in new ThemeCatalog().Create(_settings.Theme, _settings.CustomColors, systemDark))
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(token.Value));
            brush.Freeze();
            Application.Current.Resources[token.Key] = brush;
        }
        foreach (var editor in Application.Current.Windows.OfType<LayoutEditorWindow>())
            editor.RefreshTheme();
    }

    private void ApplyAppearance()
    {
        Width = (_settings.Layout.Enabled ? _settings.Layout.Width + 50 : 190) * _settings.Scale;
        WidgetContent.LayoutTransform = new ScaleTransform(_settings.Scale, _settings.Scale);
        WidgetContent.Opacity = _settings.Opacity;
    }

    private Brush BrushResource(string name) => (Brush)FindResource(name);

    private LayoutContext BuildLayoutContext() => new()
    {
        Providers = _providers.Select(runtime => new LayoutProviderContext
        {
            ProviderId = runtime.Id, ProviderName = runtime.Name, Windows = VisibleWindows(runtime),
            Account = runtime.View.Account, Plan = runtime.View.Plan, Status = runtime.View.Status,
            StatusBrush = runtime.View.StatusBrush
        }).ToArray()
    };

    private static void ResetConnectionPresentation(ProviderRuntime runtime)
    {
        runtime.Generation++;
        runtime.Snapshot = new() { ProviderId = runtime.Id, ProviderName = runtime.Name };
        runtime.HasSnapshot = false;
        runtime.View.Metrics.Clear();
        runtime.View.Plan = "";
        runtime.View.Account = "계정 확인 중";
        runtime.View.EmptyTitle = "연결 중";
        runtime.View.EmptyBody = $"{runtime.Name} 계정을 확인하고 있어요.";
        runtime.View.Stale = false;
        runtime.View.RuntimeMissing = false;
        runtime.Failures = 0;
    }

    private void ResetAppearance()
    {
        _settings = _settings with { Theme = WidgetTheme.System, CustomColors = new(), Scale = 1, Opacity = 1, Layout = new() };
        ApplyTheme();
        ApplyAppearance();
        SaveSettings();
        RenderSnapshots();
        foreach (var dialog in _dialogs.Values)
            dialog.UpdateSettings(_settings);
    }

    private void BeginWidgetDrag()
    {
        DragMove();
        ClampToScreen();
        SavePosition();
    }

    private void Widget_MouseRightButtonUp(object sender, MouseButtonEventArgs e) => _tray.ContextMenuStrip?.Show(Forms.Cursor.Position);

    private void PlaceWindow()
    {
        if (!_settings.HasPosition)
        {
            var area = Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = area.Right / dpi.DpiScaleX - ActualWidth - 24;
            Top = area.Top / dpi.DpiScaleY + 24;
        }
        ClampToScreen();
        SavePosition();
    }

    private void ClampToScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
            return;
        var area = Forms.Screen.FromHandle(handle).WorkingArea;
        var x = Math.Clamp(rect.Left, area.Left, Math.Max(area.Left, area.Right - (rect.Right - rect.Left)));
        var y = Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - (rect.Bottom - rect.Top)));
        if (x != rect.Left || y != rect.Top)
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0015);
        MaxHeight = Math.Max(180, area.Height / VisualTreeHelper.GetDpi(this).DpiScaleY - 20);
    }

    private void SavePosition()
    {
        _settings = _settings with { HasPosition = true, Left = Left, Top = Top };
        SaveSettings();
    }

    private void SaveSettings()
    {
        if (!_preview)
            _settingsError = _store.TrySave(_settings) ? "" : "설정을 저장하지 못했어요. 현재 실행에는 적용돼요.";
    }

    private void ShowWidget()
    {
        if (_quitting)
            return;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        ClampToScreen();
        _ = RefreshAsync(false);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        else if (e.Key == Key.R && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ResetAppearance();
            e.Handled = true;
        }
    }

    private async Task QuitAsync()
    {
        if (_quitting)
            return;
        await StopAsync();
        Application.Current.Shutdown();
    }

    private async Task StopAsync()
    {
        if (_quitting)
            return;
        _quitting = true;
        _refreshTimer.Stop();
        _clockTimer.Stop();
        _resetNewsTimer.Stop();
        SavePosition();
        SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        SystemEvents.PowerModeChanged -= PowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= _displayChanged;
        _showWait.Unregister(default);
        _showEvent.Dispose();
        _lifetime.Cancel();
        await _resetNewsTask;
        _resetNewsClient.Dispose();
        await Task.WhenAll(_providers.Select(DisposeProviderAsync));
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _trayIcon.Dispose();
    }

    private static async Task DisposeProviderAsync(ProviderRuntime runtime)
    {
        await runtime.Gate.WaitAsync();
        try { DetachProvider(runtime); await runtime.Provider.DisposeAsync(); }
        finally { runtime.Gate.Release(); }
    }

    private static void DetachProvider(ProviderRuntime runtime)
    {
        if (runtime.Provider is CodexUsageProvider codex)
            codex.StateChanged -= runtime.StateChanged;
    }

    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_settings.Theme == WidgetTheme.System) { ApplyTheme(); RenderSnapshots(); }
    });

    private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Dispatcher.BeginInvoke(() => _ = RefreshAsync(false));
    }

    private bool OpenUrl(string url, ProviderRuntime runtime)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return true; }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            runtime.View.Detail = "기본 브라우저를 열지 못했어요. 브라우저 설정을 확인해주세요.";
            UpdateView();
            return false;
        }
    }

    private static string FormatAge(UsageSnapshot snapshot)
    {
        if (snapshot.ObservedAt == DateTimeOffset.MinValue)
            return "갱신 시각 미확인";
        var age = DateTimeOffset.Now - snapshot.ObservedAt;
        return age.TotalMinutes < 1 ? "방금" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}분 전" : $"{(int)age.TotalHours}시간 전";
    }

    private static string FormatPlan(string plan) => plan.ToLowerInvariant() switch
    {
        "pro" => "Pro", "plus" => "Plus", "free" => "Free", "max" => "Max", "team" => "Team", "business" => "Business", "enterprise" => "Enterprise", "edu" => "Edu", "" => "", _ => plan
    };

    private static string FriendlyError(Exception exception, string name) => exception switch
    {
        CodexConnectionException codex => codex.Message,
        CodexRequestException request => request.Message,
        ClaudeConnectionException claude => claude.Message,
        GrokConnectionException grok => grok.Message,
        TimeoutException => $"{name} 응답이 늦어지고 있어요. 잠시 후 자동으로 다시 연결해요.",
        OperationCanceledException => "요청을 취소했어요. 자동으로 다시 연결해요.",
        _ => $"{name}에 연결하지 못했어요. 실행 파일과 로그인, 네트워크를 확인해주세요."
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
