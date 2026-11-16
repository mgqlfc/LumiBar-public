using System;
using System.Linq;
using System.Threading.Tasks;
using AntigravityFrontend.Sdk;
using AntigravityFrontend.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AntigravityFrontend.Windows;

public sealed partial class MainWindow : Window
{
    private const double EnterLiteThreshold = 370;
    private const double ExitLiteThreshold = 415;

    private readonly PluginConfig _config;
    private readonly LumiBarBridgeService _bridge;
    private readonly TaskbarCapsuleWindow _realMusicCapsule;
    private readonly TaskbarCapsuleWindow _realWeatherCapsule;
    private readonly TaskbarRendererLeaseService _leaseService;
    private readonly LyricsFlyoutWindow _lyricsFlyout;
    private readonly WeatherFlyoutWindow _weatherFlyout;
    private readonly SettingsWindow _settingsWindow;

    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;
    private bool _isLiteMode;
    private bool _suppressUiEvents;
    private bool _exitRequested;

    public MainWindow()
    {
        App.LogStartup("MainWindow.ctor: PluginConfig.Load");
        _config = PluginConfig.Load();
        App.LogStartup("MainWindow.ctor: LumiBarBridgeService");
        _bridge = new LumiBarBridgeService(DispatcherQueue, _config);

        App.LogStartup("MainWindow.ctor: InitializeComponent");
        InitializeComponent();

        App.LogStartup("MainWindow.ctor: GetAppWindow + Resize");
        var appWin = Win32WindowHelper.GetAppWindow(this);
        appWin.Resize(new global::Windows.Graphics.SizeInt32(980, 560));
        App.LogStartup("MainWindow.ctor: AttachAcrylicBackdrop");
        (_acrylicController, _backdropConfig) = Win32WindowHelper.AttachAcrylicBackdrop(this, true, 0.72);

        App.LogStartup("MainWindow.ctor: new TaskbarCapsuleWindows (hidden)");
        _realMusicCapsule = new TaskbarCapsuleWindow(isMusic: true);
        _realWeatherCapsule = new TaskbarCapsuleWindow(isMusic: false);

        App.LogStartup("MainWindow.ctor: new SettingsWindow");
        _settingsWindow = new SettingsWindow(_bridge, _config);
        App.LogStartup("MainWindow.ctor: new LyricsFlyoutWindow");
        _lyricsFlyout = new LyricsFlyoutWindow(_bridge, _config, () => _settingsWindow.ShowAndFocus());
        App.LogStartup("MainWindow.ctor: new WeatherFlyoutWindow");
        _weatherFlyout = new WeatherFlyoutWindow(_bridge, _config);

        _leaseService = new TaskbarRendererLeaseService(
            DispatcherQueue,
            _config,
            _bridge,
            () => _realMusicCapsule.HwndHex,
            () => _realWeatherCapsule.HwndHex,
            HideAllTaskbarCapsulesAndFlyoutsAsync,
            HideAllTaskbarCapsulesAndFlyoutsImmediate,
            IsManagementUiVisible);
        _settingsWindow.AttachLeaseController(
            _leaseService,
            ShowWorkbenchWindow,
            CheckExitAfterSettingsHidden);

        App.LogStartup("MainWindow.ctor: WireEvents");
        WireEvents();

        _suppressUiEvents = true;
        ModeToggleSwitch.IsOn = _config.UseMockMode;
        SpaceSlider.Maximum = 620;
        SpaceSlider.Minimum = 160;
        SpaceSlider.Value = Math.Clamp(_config.SimulatedAvailableWidth, 160, 620);
        _suppressUiEvents = false;

        UpdateClock();
        var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        clockTimer.Tick += (_, _) => UpdateClock();
        clockTimer.Start();

        App.LogStartup("MainWindow.ctor: _bridge.Start + _leaseService.Start");
        _bridge.Start();
        _leaseService.Start();
        App.LogStartup("MainWindow.ctor: RefreshAllUi");
        RefreshAllUi();
        App.LogStartup("MainWindow.ctor: done");
    }

    private bool IsManagementUiVisible() =>
        Win32WindowHelper.IsWindowVisible(this) || Win32WindowHelper.IsWindowVisible(_settingsWindow);

    public void ShowWorkbenchWindow()
    {
        try
        {
            Win32WindowHelper.GetAppWindow(this).Show();
            Activate();
        }
        catch { }
    }

    public void RequestCleanExit(string reason)
    {
        if (_exitRequested) return;
        _exitRequested = true;
        App.LogStartup($"MainWindow: RequestCleanExit ({reason})");
        try { Close(); }
        catch
        {
            ((App)Application.Current).CompleteShutdown();
        }
    }

    private void CheckExitAfterSettingsHidden()
    {
        if (PluginConfig.IsTaskbarLaunchMode && _leaseService.IsPausedByUserRelease && !IsManagementUiVisible())
            RequestCleanExit("settings_closed_after_release");
    }

    private void HideAllTaskbarCapsulesAndFlyoutsImmediate()
    {
        try
        {
            _realMusicCapsule.HideImmediate();
            _realWeatherCapsule.HideImmediate();
            if (_lyricsFlyout.IsWindowVisible) _lyricsFlyout.HidePopup();
            if (_weatherFlyout.IsWindowVisible) _weatherFlyout.HidePopup();
            _bridge.SetPopupVisibility(false);
        }
        catch { }
    }

    private Task HideAllTaskbarCapsulesAndFlyoutsAsync()
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            HideAllTaskbarCapsulesAndFlyoutsImmediate();
            return Task.CompletedTask;
        }
        var tcs = new TaskCompletionSource();
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try { HideAllTaskbarCapsulesAndFlyoutsImmediate(); }
            finally { tcs.TrySetResult(); }
        }))
        {
            tcs.TrySetResult();
        }
        return tcs.Task;
    }

    private void WireEvents()
    {
        _bridge.StateUpdated += RefreshAllUi;
        _bridge.ArtworkUpdated += RefreshAllUi;
        _bridge.ConnectionChanged += UpdateConnectionBadge;
        _leaseService.StatusOrSlotsChanged += OnLeaseStatusOrSlotsChanged;
        _leaseService.AutoExitRequested += reason => DispatcherQueue.TryEnqueue(() => RequestCleanExit(reason));
        _bridge.SpectrumUpdated += () =>
        {
            TaskbarMusicCapsule.UpdateSpectrum(_bridge.CurrentSpectrum);
            _realMusicCapsule.UpdateSpectrum(_bridge.CurrentSpectrum);
            _lyricsFlyout.ApplySpectrum(_bridge.CurrentSpectrum);
            _settingsWindow.UpdatePreviewSpectrum(_bridge.CurrentSpectrum);
        };

        TaskbarMusicCapsule.CapsuleClicked += OnOpenLyricsFromPreviewCapsule;
        TaskbarMusicCapsule.PreviousRequested += async () => await _bridge.SendMediaCommandAsync("media.previous");
        TaskbarMusicCapsule.ToggleRequested += async () => await _bridge.SendMediaCommandAsync("media.toggle");
        TaskbarMusicCapsule.NextRequested += async () => await _bridge.SendMediaCommandAsync("media.next");
        TaskbarWeatherCapsule.CapsuleClicked += OnOpenWeatherFromPreviewCapsule;

        _realMusicCapsule.MusicCapsule.CapsuleClicked += OnOpenLyricsFromRealCapsule;
        _realMusicCapsule.MusicCapsule.PreviousRequested += async () => await _bridge.SendMediaCommandAsync("media.previous");
        _realMusicCapsule.MusicCapsule.ToggleRequested += async () => await _bridge.SendMediaCommandAsync("media.toggle");
        _realMusicCapsule.MusicCapsule.NextRequested += async () => await _bridge.SendMediaCommandAsync("media.next");
        _realWeatherCapsule.WeatherCapsule.CapsuleClicked += OnOpenWeatherFromRealCapsule;

        Closed += (_, _) =>
        {
            App.LogStartup("MainWindow.Closed: releasing lease and closing child windows");
            _leaseService.ReleaseOnUiClose();
            _leaseService.Dispose();
            _realMusicCapsule.Close();
            _realWeatherCapsule.Close();
            _lyricsFlyout.Close();
            _weatherFlyout.Close();
            _settingsWindow.ForceClose();
            _bridge.Dispose();
            ((App)Application.Current).CompleteShutdown();
        };
    }

    private void OnLeaseStatusOrSlotsChanged()
    {
        SyncRealTaskbarCapsules();
        UpdateTakeoverBanner();
    }

    private void RefreshAllUi()
    {
        var state = _bridge.CurrentState;
        bool popupDark = Win32WindowHelper.ResolveIsDark(state.Appearance, state.Settings, isTaskbar: false);
        bool taskbarDark = Win32WindowHelper.ResolveIsDark(state.Appearance, state.Settings, isTaskbar: true);

        RootGrid.RequestedTheme = popupDark ? ElementTheme.Dark : ElementTheme.Light;
        Win32WindowHelper.ApplyFontFamily(FontHost, state.Appearance.FontFamily ?? state.Settings.FontFamily);
        if (_backdropConfig != null)
            _backdropConfig.Theme = popupDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        Win32WindowHelper.UpdateAcrylicTint(_acrylicController, popupDark, state.Appearance.GlassOpacity);

        TaskbarSurface.RequestedTheme = taskbarDark ? ElementTheme.Dark : ElementTheme.Light;
        TaskbarSurface.Background = new SolidColorBrush(taskbarDark
            ? Color.FromArgb(205, 22, 24, 32)
            : Color.FromArgb(215, 240, 244, 250));
        TaskbarSurface.BorderBrush = new SolidColorBrush(taskbarDark
            ? Color.FromArgb(35, 255, 255, 255)
            : Color.FromArgb(40, 15, 23, 42));

        CapsulesAnchorPanel.Spacing = Math.Clamp(state.Settings.Gap, 4, 24);

        EvaluateSpaceAndHysteresis(SpaceSlider.Value);

        TaskbarMusicCapsule.UpdateState(state, _bridge.CurrentArtwork, _isLiteMode);
        TaskbarWeatherCapsule.UpdateState(
            state,
            _bridge.GetOneHourPrecipitation(),
            _bridge.IsMockMode,
            _isLiteMode,
            suppressHoverBubble: _weatherFlyout.IsWindowVisible);

        if (_lyricsFlyout.IsWindowVisible)
            _lyricsFlyout.ApplyState(state, _bridge.CurrentArtwork);
        if (_weatherFlyout.IsWindowVisible)
            _weatherFlyout.ApplyState(state);

        SyncRealTaskbarCapsules();
        UpdateTakeoverBanner();
        _settingsWindow.SyncFromState(state, _bridge.CurrentArtwork);
        UpdateConnectionBadge();
        MockLifecycleCard.Opacity = _bridge.IsMockMode ? 1.0 : 0.55;
    }

    private void SyncRealTaskbarCapsules()
    {
        bool allow = _leaseService.CanShowTaskbarCapsules;
        var slots = _leaseService.LatestSlots;
        var state = _bridge.CurrentState;
        var precip = _bridge.GetOneHourPrecipitation();

        _realMusicCapsule.ApplySlot(
            slots?.MusicSlot,
            slots?.State,
            allow,
            state,
            _bridge.CurrentArtwork,
            precip,
            _bridge.IsMockMode,
            suppressWeatherHover: true);

        _realWeatherCapsule.ApplySlot(
            slots?.WeatherSlot,
            slots?.State,
            allow,
            state,
            _bridge.CurrentArtwork,
            precip,
            _bridge.IsMockMode,
            suppressWeatherHover: _weatherFlyout.IsWindowVisible);
    }

    private void UpdateTakeoverBanner()
    {
        TakeoverPhaseText.Text = _leaseService.Phase switch
        {
            TaskbarTakeoverPhase.Active => "Active · 外部接管中",
            TaskbarTakeoverPhase.PendingActivation => "Pending · 等待占位就绪",
            TaskbarTakeoverPhase.Registering => "Registering",
            TaskbarTakeoverPhase.PreviewOnly => "Preview Only",
            TaskbarTakeoverPhase.BusyByOtherRenderer => "Busy · 已被占用",
            TaskbarTakeoverPhase.ReservationUnavailable => "Reservation Unavailable",
            TaskbarTakeoverPhase.PausedByUserRelease => "Released · 已退回内置",
            TaskbarTakeoverPhase.Disabled => "Disabled",
            TaskbarTakeoverPhase.Unsupported => "Unsupported",
            TaskbarTakeoverPhase.Error => "Error",
            _ => "Waiting"
        };

        var (fg, bg) = _leaseService.Phase switch
        {
            TaskbarTakeoverPhase.Active => (Color.FromArgb(255, 74, 222, 128), Color.FromArgb(38, 34, 197, 94)),
            TaskbarTakeoverPhase.PendingActivation or TaskbarTakeoverPhase.Registering => (Color.FromArgb(255, 96, 205, 255), Color.FromArgb(38, 96, 205, 255)),
            TaskbarTakeoverPhase.PreviewOnly or TaskbarTakeoverPhase.Disabled or TaskbarTakeoverPhase.PausedByUserRelease => (Color.FromArgb(255, 148, 163, 184), Color.FromArgb(34, 148, 163, 184)),
            _ => (Color.FromArgb(255, 251, 191, 36), Color.FromArgb(38, 245, 158, 11))
        };
        TakeoverPhaseText.Foreground = new SolidColorBrush(fg);
        TakeoverPhaseBadge.Background = new SolidColorBrush(bg);

        string reason = string.IsNullOrWhiteSpace(_leaseService.LastReleaseReason)
            ? ""
            : $" ｜ 上次释放原因：{_leaseService.LastReleaseReason}";
        TakeoverDetailText.Text = $"{_leaseService.StatusText}{reason}";
    }

    private void EvaluateSpaceAndHysteresis(double availableDip)
    {
        // Hysteresis buffer prevents oscillation between Full and Dual Lite
        if (!_isLiteMode && availableDip <= EnterLiteThreshold)
            _isLiteMode = true;
        else if (_isLiteMode && availableDip >= ExitLiteThreshold)
            _isLiteMode = false;

        // Simulate Windows app icons collapsing into overflow '...' to preserve Dual Lite capsules
        bool crowded = availableDip <= 320;
        bool ultraTight = availableDip <= 230;
        AppIconPlayer.Visibility = crowded ? Visibility.Collapsed : Visibility.Visible;
        AppIconCode.Visibility = crowded ? Visibility.Collapsed : Visibility.Visible;
        AppIconEdge.Visibility = ultraTight ? Visibility.Collapsed : Visibility.Visible;
        OverflowPill.Visibility = crowded ? Visibility.Visible : Visibility.Collapsed;

        string modeDesc = _isLiteMode
            ? "双胶囊 Lite (48 + 64 DIP) · 程序图标让位至溢出"
            : "标准双胶囊（音乐左 238 / 天气右 104 DIP）";
        if (availableDip > EnterLiteThreshold && availableDip < ExitLiteThreshold)
            modeDesc += " [滞回缓冲保持]";

        string backendHint = "";
        if (_bridge.IsConnected && _bridge.CurrentHints is { } hints)
        {
            string ownerText = string.IsNullOrWhiteSpace(hints.RenderingOwner) ? "" : $"渲染归属：{hints.RenderingOwner}";
            string layoutText = string.IsNullOrWhiteSpace(hints.LayoutState) ? "" : $"布局：{hints.LayoutState}";
            string resText = "";
            if (_bridge.CurrentReservation?.Snapshot is { } snap && !string.IsNullOrWhiteSpace(snap.Status))
                resText = $"原生占位：{snap.Status} ({Math.Round(snap.WidthDip)} DIP)";
            else if (hints.NativeReservation is { } nativeSnap && !string.IsNullOrWhiteSpace(nativeSnap.Status))
                resText = $"原生占位：{nativeSnap.Status} ({Math.Round(nativeSnap.WidthDip)} DIP)";

            var parts = new[] { ownerText, layoutText, resText }.Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (parts.Length > 0)
                backendHint = " ｜ " + string.Join(" · ", parts);
        }

        LayoutTelemetryText.Text = $"可用宽度：{Math.Round(availableDip)} DIP ｜ 当前状态：{modeDesc}{backendHint}";
    }

    private void UpdateConnectionBadge()
    {
        if (_bridge.IsMockMode)
        {
            ConnStatusText.Text = "示例数据模式";
            ConnStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 56, 189, 248));
            ConnBadge.Background = new SolidColorBrush(Color.FromArgb(38, 56, 189, 248));
            ConnDetailText.Text = "当前展示 clearly-labeled 示例数据；关闭右上角开关即可连接 LumiBar Named Pipe 实况后端。";
        }
        else if (_bridge.IsConnected)
        {
            ConnStatusText.Text = "已连接实况后端";
            ConnStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 74, 222, 128));
            ConnBadge.Background = new SolidColorBrush(Color.FromArgb(38, 34, 197, 94));
            var hello = _bridge.CurrentHello;
            string ver = string.IsNullOrWhiteSpace(hello?.BackendVersion) ? "API v1" : $"API v1 · {hello.BackendVersion}";
            string ownerNote = _leaseService.CanShowTaskbarCapsules
                ? " · 任务栏由 Antigravity 正式渲染"
                : (_bridge.CurrentHints?.RenderingOwner == "builtin"
                    ? " · 当前由 LumiBar 内置渲染任务栏胶囊"
                    : "");
            ConnDetailText.Text = $"已连接 {_bridge.EffectiveEndpointPath}（{ver}{ownerNote}）";
        }
        else
        {
            ConnStatusText.Text = "后端离线（重连中）";
            ConnStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 251, 191, 36));
            ConnBadge.Background = new SolidColorBrush(Color.FromArgb(38, 245, 158, 11));
            ConnDetailText.Text = string.IsNullOrWhiteSpace(_bridge.LastErrorMessage)
                ? "请先运行 start-backend.ps1，或开启“示例数据”预览 UI。"
                : _bridge.LastErrorMessage;
        }
    }

    private (int Left, int Top, int Right, int Bottom) GetCapsuleScreenRect(FrameworkElement capsule)
    {
        var (winLeft, winTop, _, _) = Win32WindowHelper.GetWindowPhysicalRect(this);
        var scale = Win32WindowHelper.GetWindowScale(this);
        var transform = capsule.TransformToVisual(RootGrid);
        var topLeftDip = transform.TransformPoint(new global::Windows.Foundation.Point(0, 0));

        // Add subtle titlebar/frame offset approximation via Content.XamlRoot / RootGrid
        var rootTransform = RootGrid.TransformToVisual(null);
        var contentPt = rootTransform.TransformPoint(new global::Windows.Foundation.Point(0, 0));
        double totalX = topLeftDip.X + contentPt.X;
        double totalY = topLeftDip.Y + contentPt.Y;

        int left = winLeft + (int)Math.Round((totalX + 8) * scale);
        int top = winTop + (int)Math.Round((totalY + 30) * scale);
        int right = left + (int)Math.Round(Math.Max(48, capsule.ActualWidth) * scale);
        int bottom = top + (int)Math.Round(Math.Max(40, capsule.ActualHeight) * scale);
        return (left, top, right, bottom);
    }

    private void OnOpenLyricsFromPreviewCapsule()
    {
        ToggleLyricsAtAnchor(() => GetCapsuleScreenRect(TaskbarMusicCapsule));
    }

    private void OnOpenLyricsFromRealCapsule()
    {
        ToggleLyricsAtAnchor(() => _realMusicCapsule.GetAnchorRect());
    }

    private void ToggleLyricsAtAnchor(Func<(int Left, int Top, int Right, int Bottom)> anchorProvider)
    {
        if (_lyricsFlyout.IsWindowVisible)
        {
            _lyricsFlyout.HidePopup();
        }
        else
        {
            if (!_config.PinWeatherFlyout) _weatherFlyout.HidePopup();
            var (l, t, r, b) = anchorProvider();
            _lyricsFlyout.ShowAtAnchor(l, t, r, b);
        }
        _bridge.SetPopupVisibility(_lyricsFlyout.IsWindowVisible || _weatherFlyout.IsWindowVisible);
    }

    private void OnOpenWeatherFromPreviewCapsule()
    {
        ToggleWeatherAtAnchor(() => GetCapsuleScreenRect(TaskbarWeatherCapsule));
    }

    private void OnOpenWeatherFromRealCapsule()
    {
        ToggleWeatherAtAnchor(() => _realWeatherCapsule.GetAnchorRect());
    }

    private void ToggleWeatherAtAnchor(Func<(int Left, int Top, int Right, int Bottom)> anchorProvider)
    {
        if (_weatherFlyout.IsWindowVisible)
        {
            _weatherFlyout.HidePopup();
        }
        else
        {
            if (!_config.PinLyricsFlyout) _lyricsFlyout.HidePopup();
            var (l, t, r, b) = anchorProvider();
            _weatherFlyout.ShowAtAnchor(l, t, r, b);
        }
        _bridge.SetPopupVisibility(_lyricsFlyout.IsWindowVisible || _weatherFlyout.IsWindowVisible);
    }

    private void OnOpenLyricsClick(object sender, RoutedEventArgs e)
        => ToggleLyricsAtAnchor(() => _realMusicCapsule.IsCapsuleVisible ? _realMusicCapsule.GetAnchorRect() : GetCapsuleScreenRect(TaskbarMusicCapsule));
    private void OnOpenWeatherClick(object sender, RoutedEventArgs e)
        => ToggleWeatherAtAnchor(() => _realWeatherCapsule.IsCapsuleVisible ? _realWeatherCapsule.GetAnchorRect() : GetCapsuleScreenRect(TaskbarWeatherCapsule));
    private void OnOpenSettingsClick(object sender, RoutedEventArgs e) => _settingsWindow.ShowAndFocus();

    private void OnSwitchToOfficialClick(object sender, RoutedEventArgs e)
    {
        _config.EndpointPath = PluginConfig.OfficialEndpointPath;
        _config.UseMockMode = false;
        _config.Save();
        _suppressUiEvents = true;
        ModeToggleSwitch.IsOn = false;
        _suppressUiEvents = false;
        _bridge.ReconnectEndpoint();
        _leaseService.RequestManualRetry();
    }

    private void OnRetryTakeoverClick(object sender, RoutedEventArgs e) => _leaseService.RequestManualRetry();
    private async void OnReleaseTakeoverClick(object sender, RoutedEventArgs e) => await _leaseService.ReleaseByUserAsync();

    private void OnDataModeToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressUiEvents || !RootGrid.IsLoaded || !Win32WindowHelper.IsWindowVisible(this)) return;
        if (ModeToggleSwitch.IsOn == _config.UseMockMode) return;
        _bridge.SetMockMode(ModeToggleSwitch.IsOn);
    }

    private void OnSpaceSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressUiEvents || !RootGrid.IsLoaded || !Win32WindowHelper.IsWindowVisible(this)) return;
        int nextWidth = (int)Math.Round(e.NewValue);
        if (nextWidth == _config.SimulatedAvailableWidth) return;
        _config.SimulatedAvailableWidth = nextWidth;
        _config.Save();
        RefreshAllUi();
    }

    private void OnPresetFullClick(object sender, RoutedEventArgs e) => SpaceSlider.Value = 520;
    private void OnPresetBufferClick(object sender, RoutedEventArgs e) => SpaceSlider.Value = 390;
    private void OnPresetLiteClick(object sender, RoutedEventArgs e) => SpaceSlider.Value = 280;
    private void OnPresetRecoverClick(object sender, RoutedEventArgs e) => SpaceSlider.Value = 440;

    private void OnMockPlayClick(object sender, RoutedEventArgs e) => _bridge.ToggleMockSession(true, true);
    private void OnMockPauseClick(object sender, RoutedEventArgs e) => _bridge.ToggleMockSession(true, false);
    private void OnMockStopMediaClick(object sender, RoutedEventArgs e) => _bridge.ToggleMockSession(false, false);

    private void UpdateClock()
    {
        TrayTimeText.Text = DateTime.Now.ToString("HH:mm");
        TrayDateText.Text = DateTime.Now.ToString("yyyy/MM/dd");
    }
}
