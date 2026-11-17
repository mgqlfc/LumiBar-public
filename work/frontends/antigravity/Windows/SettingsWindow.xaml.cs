using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AntigravityFrontend.Sdk;
using AntigravityFrontend.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AntigravityFrontend.Windows;

public sealed partial class SettingsWindow : Window
{
    private readonly LumiBarBridgeService _bridge;
    private readonly PluginConfig _config;
    private TaskbarRendererLeaseService? _leaseService;
    private Action? _showWorkbenchAction;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;
    private int _suppressDepth;
    private bool _hasInitialSync;
    private bool _allowClose;
    private Action? _hiddenByUserAction;

    private readonly DispatcherTimer _fontSearchTimer;
    private readonly List<SystemFontItemDto> _fontItems = [];
    private readonly HashSet<string> _installedFontSet = new(StringComparer.OrdinalIgnoreCase);
    private bool _fontListLoaded;
    private bool _fontCatalogHasUnfilteredSet;
    private bool _isUpdatingFontSelection;
    private string? _previewFontFamily;

    public SettingsWindow(LumiBarBridgeService bridge, PluginConfig config)
    {
        _bridge = bridge;
        _config = config;

        BeginSyncSuppress();
        try
        {
            InitializeComponent();

            MusicWidthSlider.Maximum = 340;
            MusicWidthSlider.Minimum = 180;
            LyricsWidthSlider.Maximum = 640;
            LyricsWidthSlider.Minimum = 320;
            LyricsHeightSlider.Maximum = 680;
            LyricsHeightSlider.Minimum = 340;
            WeatherWidthSlider.Maximum = 540;
            WeatherWidthSlider.Minimum = 360;
            WeatherHeightSlider.Maximum = 960;
            WeatherHeightSlider.Minimum = 480;
        }
        finally
        {
            EndSyncSuppressDeferred();
        }

        var appWin = Win32WindowHelper.GetAppWindow(this);
        appWin.Resize(new global::Windows.Graphics.SizeInt32(780, 580));
        if (appWin.TitleBar is { } tb)
            tb.ExtendsContentIntoTitleBar = false;

        (_acrylicController, _backdropConfig) = Win32WindowHelper.AttachAcrylicBackdrop(this, true, 0.65);

        _fontSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _fontSearchTimer.Tick += async (_, _) =>
        {
            _fontSearchTimer.Stop();
            await LoadFontsAsync(FontSearchBox.Text ?? "", refresh: false);
        };

        appWin.Closing += (_, e) =>
        {
            if (_allowClose)
                return;
            e.Cancel = true;
            appWin.Hide();
            _hiddenByUserAction?.Invoke();
        };

        SyncFromState(_bridge.CurrentState, _bridge.CurrentArtwork, force: true);
    }

    public void ForceClose()
    {
        _allowClose = true;
        try { Close(); } catch { }
    }

    private void BeginSyncSuppress() => _suppressDepth++;

    private void EndSyncSuppressDeferred()
    {
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_suppressDepth > 0)
                    _suppressDepth--;
            }))
        {
            if (_suppressDepth > 0)
                _suppressDepth--;
        }
    }

    private bool CanSendUserChange() =>
        _suppressDepth == 0 && _hasInitialSync && RootGrid.IsLoaded && Win32WindowHelper.IsWindowVisible(this);

    public void AttachLeaseController(TaskbarRendererLeaseService leaseService, Action showWorkbenchAction, Action? hiddenByUserAction = null)
    {
        _leaseService = leaseService;
        _showWorkbenchAction = showWorkbenchAction;
        _hiddenByUserAction = hiddenByUserAction;
        _leaseService.StatusOrSlotsChanged += UpdateTakeoverStatusUi;
        UpdateTakeoverStatusUi();
    }

    private void UpdateTakeoverStatusUi()
    {
        if (_leaseService == null) return;
        BeginSyncSuppress();
        try
        {
            if (TaskbarTakeoverSwitch.IsOn != _config.EnableTaskbarTakeover)
                TaskbarTakeoverSwitch.IsOn = _config.EnableTaskbarTakeover;
        }
        finally
        {
            EndSyncSuppressDeferred();
        }

        string reason = string.IsNullOrWhiteSpace(_leaseService.LastReleaseReason)
            ? ""
            : $" · 上次释放原因：{_leaseService.LastReleaseReason}";
        TakeoverStatusText.Text = $"阶段：{_leaseService.Phase} ｜ {_leaseService.StatusText}{reason}";
    }

    public void ShowAndFocus()
    {
        SyncFromState(_bridge.CurrentState, _bridge.CurrentArtwork, force: true);
        Win32WindowHelper.GetAppWindow(this).Show();
        Activate();
        if (!_fontListLoaded)
            _ = LoadFontsAsync(FontSearchBox.Text ?? "", refresh: false);
    }

    public void SyncFromState(StateDto state, BitmapImage? artwork, bool force = false)
    {
        if (!force && !Win32WindowHelper.IsWindowVisible(this))
            return;

        BeginSyncSuppress();
        try
        {
            bool isDark = Win32WindowHelper.ResolveIsDark(state.Appearance, state.Settings, isTaskbar: false);
            RootGrid.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
            string? activeFont = !string.IsNullOrWhiteSpace(state.Appearance.FontFamily)
                ? state.Appearance.FontFamily
                : state.Settings.FontFamily;
            Win32WindowHelper.ApplyFontFamily(FontHost, activeFont);
            UpdateFontStatusBanner(activeFont);
            if (_backdropConfig != null)
                _backdropConfig.Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
            Win32WindowHelper.UpdateAcrylicTint(_acrylicController, isDark, state.Appearance.GlassOpacity);

            var s = state.Settings;
            MockSwitch.IsOn = _config.UseMockMode;
            EndpointPathBox.Text = _config.EndpointPath;
            TaskbarTakeoverSwitch.IsOn = _config.EnableTaskbarTakeover;
            var envOverride = PluginConfig.EnvEndpointOverride;
            if (!string.IsNullOrWhiteSpace(envOverride))
            {
                EnvOverrideNoteText.Text = $"环境变量 LUMIBAR_ENDPOINT_FILE 正在生效：{envOverride}";
                EnvOverrideNoteText.Visibility = Visibility.Visible;
            }
            else
            {
                EnvOverrideNoteText.Visibility = Visibility.Collapsed;
            }
            UpdateTakeoverStatusUi();

            ThemeSystemRadio.IsChecked = s.Theme == "System";
            ThemeLightRadio.IsChecked = s.Theme == "Light";
            ThemeDarkRadio.IsChecked = s.Theme == "Dark";

            GlassOpacitySlider.Value = Math.Round(s.GlassOpacity * 100);
            CoverGlowSwitch.IsOn = s.CoverGlow;
            GlowIntensitySlider.Value = Math.Round(s.Glow * 100);
            AnimationsSwitch.IsOn = s.Animations;

            ShowMusicSwitch.IsOn = s.ShowMusic;
            ShowWeatherSwitch.IsOn = s.ShowWeather;
            MusicWidthSlider.Value = s.MusicWidth;
            GapSlider.Value = s.Gap;

            HideFullscreenSwitch.IsOn = s.HideInFullscreen;
            foreach (ComboBoxItem item in RefreshIntervalCombo.Items)
            {
                if (int.TryParse(item.Tag?.ToString(), out int m) && m == s.WeatherRefreshMinutes)
                {
                    RefreshIntervalCombo.SelectedItem = item;
                    break;
                }
            }

            FahrenheitSwitch.IsOn = s.Fahrenheit;
            WindMpsSwitch.IsOn = s.WindMetersPerSecond;

            SpectrumSwitch.IsOn = s.Spectrum;
            SensitivitySlider.Value = Math.Round(s.Sensitivity * 10);
            AutoLyricsSwitch.IsOn = s.AutoLyrics;
            LyricsFontSlider.Value = s.LyricsFontSize;
            LyricsOffsetSlider.Value = s.LyricsOffset;

            PopupDirectionCombo.SelectedIndex = string.Equals(_config.PopupDirection, "Down", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            CloseOnDeactivateSwitch.IsOn = _config.CloseOnDeactivate;
            LyricsWidthSlider.Value = _config.LyricsFlyoutWidth;
            LyricsHeightSlider.Value = _config.LyricsFlyoutHeight;
            WeatherWidthSlider.Value = _config.WeatherFlyoutWidth;
            WeatherHeightSlider.Value = _config.WeatherFlyoutHeight;

            PreviewMusicCapsule.UpdateState(state, artwork, isLite: false);
            PreviewWeatherCapsule.UpdateState(state, _bridge.GetOneHourPrecipitation(), _bridge.IsMockMode, isLite: false, suppressHoverBubble: true);
            _hasInitialSync = true;
        }
        finally
        {
            EndSyncSuppressDeferred();
        }
    }

    public void UpdatePreviewSpectrum(SpectrumDto spectrum)
    {
        PreviewMusicCapsule.UpdateSpectrum(spectrum);
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "appearance";
        PageGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        PageAppearance.Visibility = tag == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        PageTaskbar.Visibility = tag == "taskbar" ? Visibility.Visible : Visibility.Collapsed;
        PageWeather.Visibility = tag == "weather" ? Visibility.Visible : Visibility.Collapsed;
        PageMusic.Visibility = tag == "music" ? Visibility.Visible : Visibility.Collapsed;
        PagePlugin.Visibility = tag == "plugin" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMockToggled(object sender, RoutedEventArgs e)
    {
        if (!CanSendUserChange() || MockSwitch.IsOn == _config.UseMockMode) return;
        _bridge.SetMockMode(MockSwitch.IsOn);
    }

    private void OnSaveEndpointClick(object sender, RoutedEventArgs e)
    {
        _config.EndpointPath = EndpointPathBox.Text.Trim();
        _config.Save();
        _bridge.ReconnectEndpoint();
        if (_config.UseMockMode)
            _bridge.SetMockMode(false);
    }

    private void OnPresetOfficialEndpointClick(object sender, RoutedEventArgs e)
    {
        EndpointPathBox.Text = PluginConfig.OfficialEndpointPath;
        _config.EndpointPath = PluginConfig.OfficialEndpointPath;
        _config.UseMockMode = false;
        _config.Save();
        _bridge.ReconnectEndpoint();
    }

    private void OnPresetPreviewEndpointClick(object sender, RoutedEventArgs e)
    {
        EndpointPathBox.Text = PluginConfig.PreviewEndpointPath;
        _config.EndpointPath = PluginConfig.PreviewEndpointPath;
        _config.UseMockMode = false;
        _config.Save();
        _bridge.ReconnectEndpoint();
    }

    private async void OnTaskbarTakeoverToggled(object sender, RoutedEventArgs e)
    {
        if (!CanSendUserChange() || _leaseService == null) return;
        if (TaskbarTakeoverSwitch.IsOn == _config.EnableTaskbarTakeover) return;
        await _leaseService.SetTakeoverEnabledAsync(TaskbarTakeoverSwitch.IsOn);
    }

    private void OnRetryTakeoverClick(object sender, RoutedEventArgs e) => _leaseService?.RequestManualRetry();

    private async void OnReleaseTakeoverClick(object sender, RoutedEventArgs e)
    {
        if (_leaseService != null)
            await _leaseService.ReleaseByUserAsync();
    }

    private void OnShowWorkbenchClick(object sender, RoutedEventArgs e) => _showWorkbenchAction?.Invoke();

    private async void OnThemeRadioChecked(object sender, RoutedEventArgs e)
    {
        if (!CanSendUserChange() || sender is not RadioButton rb || rb.Tag is not string theme) return;
        if (string.Equals(_bridge.CurrentState.Settings.Theme, theme, StringComparison.OrdinalIgnoreCase)) return;
        await _bridge.PatchSettingsAsync(new { theme });
    }

    private async void OnGlassOpacityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!CanSendUserChange()) return;
        double next = Math.Round(e.NewValue / 100.0, 2);
        if (Math.Abs(next - Math.Round(_bridge.CurrentState.Settings.GlassOpacity, 2)) < 0.005) return;
        await _bridge.PatchSettingsAsync(new { glassOpacity = next });
    }

    private async void OnGlowChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!CanSendUserChange()) return;
        double next = Math.Round(e.NewValue / 100.0, 2);
        if (Math.Abs(next - Math.Round(_bridge.CurrentState.Settings.Glow, 2)) < 0.005) return;
        await _bridge.PatchSettingsAsync(new { glow = next });
    }

    private async void OnSettingToggleChanged(object sender, RoutedEventArgs e)
    {
        if (!CanSendUserChange() || sender is not ToggleSwitch ts || ts.Tag is not string key) return;
        bool val = ts.IsOn;
        var s = _bridge.CurrentState.Settings;
        switch (key)
        {
            case "coverGlow": if (val != s.CoverGlow) await _bridge.PatchSettingsAsync(new { coverGlow = val }); break;
            case "animations": if (val != s.Animations) await _bridge.PatchSettingsAsync(new { animations = val }); break;
            case "showMusic": if (val != s.ShowMusic) await _bridge.PatchSettingsAsync(new { showMusic = val }); break;
            case "showWeather": if (val != s.ShowWeather) await _bridge.PatchSettingsAsync(new { showWeather = val }); break;
            case "hideInFullscreen": if (val != s.HideInFullscreen) await _bridge.PatchSettingsAsync(new { hideInFullscreen = val }); break;
            case "fahrenheit": if (val != s.Fahrenheit) await _bridge.PatchSettingsAsync(new { fahrenheit = val }); break;
            case "windMetersPerSecond": if (val != s.WindMetersPerSecond) await _bridge.PatchSettingsAsync(new { windMetersPerSecond = val }); break;
            case "spectrum": if (val != s.Spectrum) await _bridge.PatchSettingsAsync(new { spectrum = val }); break;
            case "autoLyrics": if (val != s.AutoLyrics) await _bridge.PatchSettingsAsync(new { autoLyrics = val }); break;
        }
    }

    private async void OnRefreshComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!CanSendUserChange() || RefreshIntervalCombo.SelectedItem is not ComboBoxItem item) return;
        if (int.TryParse(item.Tag?.ToString(), out int minutes) && minutes != _bridge.CurrentState.Settings.WeatherRefreshMinutes)
            await _bridge.PatchSettingsAsync(new { weatherRefreshMinutes = minutes });
    }

    private async void OnMusicWidthChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!CanSendUserChange()) return;
        double next = Math.Round(e.NewValue);
        if (Math.Abs(next - Math.Round(_bridge.CurrentState.Settings.MusicWidth)) < 0.5) return;
        await _bridge.PatchSettingsAsync(new { musicWidth = next });
        _leaseService?.NotifyPreferredSizesChanged();
    }

    private async void OnGapChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!CanSendUserChange()) return;
        double next = Math.Round(e.NewValue);
        if (Math.Abs(next - Math.Round(_bridge.CurrentState.Settings.Gap)) < 0.5) return;
        await _bridge.PatchSettingsAsync(new { gap = next });
        _leaseService?.NotifyPreferredSizesChanged();
    }

    private async void OnSensitivityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!CanSendUserChange()) return;
        double next = Math.Round(e.NewValue / 10.0, 1);
        if (Math.Abs(next - Math.Round(_bridge.CurrentState.Settings.Sensitivity, 1)) < 0.05) return;
        await _bridge.PatchSettingsAsync(new { sensitivity = next });
    }

    private async void OnLyricsFontChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!CanSendUserChange()) return;
        double next = Math.Round(e.NewValue);
        if (Math.Abs(next - Math.Round(_bridge.CurrentState.Settings.LyricsFontSize)) < 0.5) return;
        await _bridge.PatchSettingsAsync(new { lyricsFontSize = next });
    }

    private async void OnLyricsOffsetChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!CanSendUserChange()) return;
        double next = Math.Round(e.NewValue, 1);
        if (Math.Abs(next - Math.Round(_bridge.CurrentState.Settings.LyricsOffset, 1)) < 0.05) return;
        await _bridge.PatchSettingsAsync(new { lyricsOffset = next });
    }

    private void OnPopupDirectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!CanSendUserChange() || PopupDirectionCombo.SelectedItem is not ComboBoxItem item) return;
        string dir = item.Tag?.ToString() ?? "Up";
        if (string.Equals(_config.PopupDirection, dir, StringComparison.OrdinalIgnoreCase)) return;
        _config.PopupDirection = dir;
        _config.Save();
    }

    private void OnCloseOnDeactivateToggled(object sender, RoutedEventArgs e)
    {
        if (!CanSendUserChange() || CloseOnDeactivateSwitch.IsOn == _config.CloseOnDeactivate) return;
        _config.CloseOnDeactivate = CloseOnDeactivateSwitch.IsOn;
        _config.Save();
    }

    private void OnFlyoutSizeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!CanSendUserChange()) return;
        double lw = Math.Round(LyricsWidthSlider.Value);
        double lh = Math.Round(LyricsHeightSlider.Value);
        double ww = Math.Round(WeatherWidthSlider.Value);
        double wh = Math.Round(WeatherHeightSlider.Value);
        if (Math.Abs(lw - _config.LyricsFlyoutWidth) < 0.5 &&
            Math.Abs(lh - _config.LyricsFlyoutHeight) < 0.5 &&
            Math.Abs(ww - _config.WeatherFlyoutWidth) < 0.5 &&
            Math.Abs(wh - _config.WeatherFlyoutHeight) < 0.5)
            return;
        _config.LyricsFlyoutWidth = lw;
        _config.LyricsFlyoutHeight = lh;
        _config.WeatherFlyoutWidth = ww;
        _config.WeatherFlyoutHeight = wh;
        _config.Save();
    }

    private void UpdateFontStatusBanner(string? activeFont)
    {
        ActiveFontText.Text = string.IsNullOrWhiteSpace(activeFont) ? "当前：系统默认" : $"当前：{activeFont}";
        bool missing = !string.IsNullOrWhiteSpace(activeFont) &&
                       _fontCatalogHasUnfilteredSet &&
                       !_installedFontSet.Contains(activeFont);
        FontWarningText.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(_previewFontFamily))
        {
            if (string.IsNullOrWhiteSpace(activeFont))
                FontPreviewText.ClearValue(TextBlock.FontFamilyProperty);
            else
                FontPreviewText.FontFamily = new FontFamily(activeFont);
        }
    }

    private async Task LoadFontsAsync(string query, bool refresh)
    {
        try
        {
            RefreshFontsBtn.IsEnabled = false;
            var res = await _bridge.ListSystemFontsAsync(query, offset: 0, limit: 250, refresh: refresh);
            _fontListLoaded = true;

            if (string.IsNullOrWhiteSpace(query) || refresh)
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    _installedFontSet.Clear();
                    foreach (var item in res.Items)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Family))
                            _installedFontSet.Add(item.Family);
                        foreach (var alias in item.Aliases)
                        {
                            if (!string.IsNullOrWhiteSpace(alias))
                                _installedFontSet.Add(alias);
                        }
                    }
                    _fontCatalogHasUnfilteredSet = res.Items.Count > 0;
                }
            }

            _fontItems.Clear();
            _fontItems.AddRange(res.Items);
            RenderFontList();

            string? activeFont = !string.IsNullOrWhiteSpace(_bridge.CurrentState.Appearance.FontFamily)
                ? _bridge.CurrentState.Appearance.FontFamily
                : _bridge.CurrentState.Settings.FontFamily;
            UpdateFontStatusBanner(activeFont);
            FontActionStatusText.Text = $"已加载 {res.Items.Count} / {res.Total} 个系统字体（来源：{res.Source}）";
        }
        catch (Exception ex)
        {
            FontActionStatusText.Text = $"加载字体失败：{ex.Message}";
        }
        finally
        {
            RefreshFontsBtn.IsEnabled = true;
        }
    }

    private void RenderFontList()
    {
        _isUpdatingFontSelection = true;
        try
        {
            FontsListView.Items.Clear();
            string? activeFont = !string.IsNullOrWhiteSpace(_bridge.CurrentState.Appearance.FontFamily)
                ? _bridge.CurrentState.Appearance.FontFamily
                : _bridge.CurrentState.Settings.FontFamily;
            string? targetFamily = !string.IsNullOrWhiteSpace(_previewFontFamily) ? _previewFontFamily : activeFont;
            ListViewItem? selectedItem = null;

            foreach (var font in _fontItems)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(4, 3, 4, 3) };
                var primary = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(font.DisplayName) ? font.Family : font.DisplayName,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(primary);

                if (!string.Equals(font.DisplayName, font.Family, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(font.Family))
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = font.Family,
                        FontSize = 11,
                        Opacity = 0.62,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                var lvi = new ListViewItem { Content = panel, Tag = font };
                FontsListView.Items.Add(lvi);

                if (selectedItem == null && !string.IsNullOrWhiteSpace(targetFamily) &&
                    (string.Equals(font.Family, targetFamily, StringComparison.OrdinalIgnoreCase) ||
                     font.Aliases.Any(a => string.Equals(a, targetFamily, StringComparison.OrdinalIgnoreCase))))
                {
                    selectedItem = lvi;
                }
            }

            if (selectedItem != null)
                FontsListView.SelectedItem = selectedItem;
        }
        finally
        {
            _isUpdatingFontSelection = false;
        }
    }

    private void OnFontSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _fontSearchTimer.Stop();
        _fontSearchTimer.Start();
    }

    private async void OnRefreshFontsClick(object sender, RoutedEventArgs e)
    {
        await LoadFontsAsync(FontSearchBox.Text ?? "", refresh: true);
    }

    private void OnFontListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingFontSelection) return;
        if (FontsListView.SelectedItem is ListViewItem lvi && lvi.Tag is SystemFontItemDto font &&
            !string.IsNullOrWhiteSpace(font.Family))
        {
            _previewFontFamily = font.Family;
            FontPreviewText.FontFamily = new FontFamily(font.Family);
            ApplyFontBtn.IsEnabled = true;
            FontActionStatusText.Text = $"已预览：{font.DisplayName}（点击“应用所选字体”生效）";
        }
    }

    private async void OnApplyFontClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_previewFontFamily)) return;
        ApplyFontBtn.IsEnabled = false;
        try
        {
            var (ok, message) = await _bridge.SetFontFamilyAsync(_previewFontFamily);
            FontActionStatusText.Text = message;
            if (!ok)
                ApplyFontBtn.IsEnabled = true;
        }
        finally
        {
            ApplyFontBtn.IsEnabled = !string.IsNullOrWhiteSpace(_previewFontFamily);
        }
    }

    private async void OnResetFontClick(object sender, RoutedEventArgs e)
    {
        _previewFontFamily = null;
        FontPreviewText.ClearValue(TextBlock.FontFamilyProperty);
        _isUpdatingFontSelection = true;
        try { FontsListView.SelectedIndex = -1; }
        finally { _isUpdatingFontSelection = false; }
        ApplyFontBtn.IsEnabled = false;

        var (_, message) = await _bridge.SetFontFamilyAsync(null);
        FontActionStatusText.Text = message;
    }
}
