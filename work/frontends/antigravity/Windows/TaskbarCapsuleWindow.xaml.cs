using System;
using AntigravityFrontend.Controls;
using AntigravityFrontend.Sdk;
using AntigravityFrontend.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AntigravityFrontend.Windows;

public sealed partial class TaskbarCapsuleWindow : Window
{
    private readonly bool _isMusic;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;

    private int _lastLeft = int.MinValue;
    private int _lastTop = int.MinValue;
    private int _lastWidth = int.MinValue;
    private int _lastHeight = int.MinValue;
    private bool _lastShown;

    public MusicCapsuleControl MusicCapsule => MusicControl;
    public WeatherCapsuleControl WeatherCapsule => WeatherControl;
    public string HwndHex => Win32WindowHelper.GetHwndHex(this);
    public bool IsCapsuleVisible => Win32WindowHelper.IsWindowVisible(this);

    public TaskbarCapsuleWindow(bool isMusic)
    {
        _isMusic = isMusic;
        InitializeComponent();

        MusicControl.Visibility = isMusic ? Visibility.Visible : Visibility.Collapsed;
        WeatherControl.Visibility = isMusic ? Visibility.Collapsed : Visibility.Visible;

        Win32WindowHelper.ConfigureTaskbarCapsuleWindow(this);
        (_acrylicController, _backdropConfig) = Win32WindowHelper.AttachAcrylicBackdrop(this, true, 0.56);

        Closed += (_, _) =>
        {
            MusicControl.CloseHoverPopup();
            WeatherControl.CloseHoverPopup();
            _acrylicController?.Dispose();
            _acrylicController = null;
        };
    }

    public void HideImmediate()
    {
        bool wasShown = _lastShown || Win32WindowHelper.IsWindowVisible(this);
        MusicControl.CloseHoverPopup();
        WeatherControl.CloseHoverPopup();
        _lastShown = false;
        _lastWidth = 0;
        _lastHeight = 0;
        Win32WindowHelper.HideWindowImmediate(this);
        if (wasShown)
            App.LogStartup($"TaskbarCapsuleWindow({(_isMusic ? "Music" : "Weather")}): HideImmediate");
    }

    public void UpdateTheme(StateDto state)
    {
        bool taskbarDark = Win32WindowHelper.ResolveIsDark(state.Appearance, state.Settings, isTaskbar: true);
        RootGrid.RequestedTheme = taskbarDark ? ElementTheme.Dark : ElementTheme.Light;
        if (_backdropConfig != null)
            _backdropConfig.Theme = taskbarDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        Win32WindowHelper.UpdateAcrylicTint(_acrylicController, taskbarDark, Math.Min(0.82, state.Appearance.GlassOpacity));
    }

    public void UpdateSpectrum(SpectrumDto spectrum)
    {
        if (_isMusic && _lastShown)
            MusicControl.UpdateSpectrum(spectrum);
    }

    public void ApplySlot(
        CapsuleSlotDto? slot,
        string? overallState,
        bool allowShow,
        StateDto state,
        BitmapImage? artwork,
        PrecipitationViewModel precipModel,
        bool isMockMode,
        bool suppressWeatherHover)
    {
        UpdateTheme(state);

        bool featureEnabled = _isMusic
            ? (state.Settings.ShowMusic && state.Media != null)
            : state.Settings.ShowWeather;
        bool overallActive = string.Equals(overallState, "full", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(overallState, "lite", StringComparison.OrdinalIgnoreCase);
        bool modeActive = slot != null &&
                          (string.Equals(slot.Mode, "full", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(slot.Mode, "lite", StringComparison.OrdinalIgnoreCase));
        int widthPx = slot?.Rect != null ? slot.Rect.Right - slot.Rect.Left : 0;
        int heightPx = slot?.Rect != null ? slot.Rect.Bottom - slot.Rect.Top : 0;

        if (!allowShow ||
            !featureEnabled ||
            !overallActive ||
            !modeActive ||
            slot?.Rect == null ||
            widthPx <= 0 ||
            heightPx <= 0)
        {
            HideImmediate();
            return;
        }

        // Move/resize first (still hidden if not yet shown) so monitor DPI matches target monitor before computing DIPs.
        bool geometryChanged = !_lastShown ||
                               _lastLeft != slot.Rect.Left ||
                               _lastTop != slot.Rect.Top ||
                               _lastWidth != widthPx ||
                               _lastHeight != heightPx;

        if (geometryChanged && !_lastShown)
            Win32WindowHelper.PlaceTaskbarCapsule(this, slot.Rect.Left, slot.Rect.Top, widthPx, heightPx, show: false);

        double scale = Math.Max(1.0, Win32WindowHelper.GetWindowScale(this));
        double wDip = widthPx / scale;
        double hDip = heightPx / scale;
        bool isLite = string.Equals(slot.Mode, "lite", StringComparison.OrdinalIgnoreCase);

        if (_isMusic)
        {
            MusicControl.ApplySlotDimensions(wDip, hDip);
            MusicControl.UpdateState(state, artwork, isLite);
        }
        else
        {
            WeatherControl.ApplySlotDimensions(wDip, hDip);
            WeatherControl.UpdateState(state, precipModel, isMockMode, isLite, suppressWeatherHover);
        }

        if (geometryChanged || !Win32WindowHelper.IsWindowVisible(this) || !Win32WindowHelper.IsAppWindowVisible(this))
        {
            bool wasShown = _lastShown;
            Win32WindowHelper.PlaceTaskbarCapsule(this, slot.Rect.Left, slot.Rect.Top, widthPx, heightPx, show: true);
            RootGrid.UpdateLayout();
            _lastLeft = slot.Rect.Left;
            _lastTop = slot.Rect.Top;
            _lastWidth = widthPx;
            _lastHeight = heightPx;
            _lastShown = true;
            if (!wasShown || geometryChanged)
            {
                App.LogStartup(
                    $"TaskbarCapsuleWindow({(_isMusic ? "Music" : "Weather")}): shown rect=({slot.Rect.Left},{slot.Rect.Top},{widthPx}x{heightPx}) scale={scale:F2} appVis={Win32WindowHelper.IsAppWindowVisible(this)} winVis={Win32WindowHelper.IsWindowVisible(this)} loaded={RootGrid.IsLoaded} rootDip={RootGrid.ActualWidth:F0}x{RootGrid.ActualHeight:F0} fg={Win32WindowHelper.GetForegroundHwndHex()}");
            }
        }
    }

    public (int Left, int Top, int Right, int Bottom) GetAnchorRect()
    {
        return Win32WindowHelper.GetWindowPhysicalRect(this);
    }
}
