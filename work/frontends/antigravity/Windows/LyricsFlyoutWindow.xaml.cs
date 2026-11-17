using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using AntigravityFrontend.Rendering;
using AntigravityFrontend.Sdk;
using AntigravityFrontend.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace AntigravityFrontend.Windows;

public sealed partial class LyricsFlyoutWindow : Window
{
    private const int ProgressSegmentsCount = 32;
    private const int SpectrumCols = 14;
    private const int SpectrumRows = 8;
    private const float LyricRowHeight = 31f;

    private static readonly Regex BilingualTokenRegex = new(@"[A-Za-z0-9\s./:\-]+|[^\x00-\x7F]+", RegexOptions.Compiled);

    private readonly record struct SkyStarSeed(float Nx, float Ny, float Radius, double Speed, double Phase, bool WarmTint);

    private readonly LumiBarBridgeService _bridge;
    private readonly PluginConfig _config;
    private readonly Action _openSettingsAction;

    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _synthTimer;
    private readonly Stopwatch _synthClock = new();
    private double _lastClockSec;
    private readonly Win32WindowHelper.PopupDismissMonitor _dismissMonitor;
    private readonly List<LyricLineDto> _lyricLines = new();
    private readonly SkyStarSeed[] _starSeeds;
    private double _progPos;
    private double _progDur = 1.0;

    private const float CrtMaxShiftDip = 26f;
    private const float CrtBarrelStrength = 0.042f;

    // Win2D Synthwave Background & Shared CRT Curvature Resources
    private CanvasBitmap? _crtDispBitmap;
    private CanvasRenderTarget? _synthCompositeTarget;
    private CanvasRenderTarget? _synthSkySunTarget;
    private CanvasRenderTarget? _synthRoadReflTarget;
    private float _bgW = -1;
    private float _bgH = -1;
    private float _bgDpi = -1;

    // Win2D Zone 3 Spectrum cached resources
    private CanvasRenderTarget? _specCompositeTarget;
    private float _specW = -1;
    private float _specH = -1;
    private float _specDpi = -1;

    // Win2D Deck Reflection cached resources
    private CanvasRenderTarget? _reflAuraTarget;
    private CanvasRenderTarget? _reflBricksTarget;
    private CanvasRenderTarget? _reflFresnelMask;
    private float _reflW = -1;
    private float _reflH = -1;
    private float _reflDpi = -1;

    // Win2D C2 Lyrics cached resources
    private readonly Dictionary<string, CanvasRenderTarget> _lyricSpriteCache = new(StringComparer.Ordinal);
    private CanvasRenderTarget? _lyricsCompositeTarget;
    private CanvasRenderTarget? _lyricScanMask;
    private CanvasRenderTarget? _lyricTempCore;
    private CanvasRenderTarget? _lyricTempCombined;
    private float _lyricsSurfaceW = -1;
    private float _lyricsCanvasW = -1;
    private float _lyricsCanvasH = -1;
    private float _lyricsDpi = -1;
    private double _targetScrollY;
    private double _currentScrollY;
    private DateTime _manualScrollUntilUtc = DateTime.MinValue;

    private double _synthTick;
    private bool _synthPlaying;
    private bool _synthAnimations = true;
    private string? _renderedLyricsTrackKey;
    private int _renderedLineCount = -1;
    private int _activeLineIndex = -1;
    private double _renderedFontSize = -1;

    private bool _isScrubbing;
    private DateTime _seekHoldUntilUtc = DateTime.MinValue;
    private double _optimisticSeekPos;
    private bool _isDark = true;
    private Color _accent = Color.FromArgb(255, 255, 107, 0);
    private string? _secondaryAccentHex;

    public bool IsWindowVisible { get; private set; }

    public LyricsFlyoutWindow(LumiBarBridgeService bridge, PluginConfig config, Action openSettingsAction)
    {
        _bridge = bridge;
        _config = config;
        _openSettingsAction = openSettingsAction;

        App.LogStartup("LyricsFlyoutWindow: InitializeComponent");
        InitializeComponent();
        App.LogStartup("LyricsFlyoutWindow: ConfigureBorderlessPopup");
        Win32WindowHelper.ConfigureBorderlessPopup(this, hideFromAltTab: true, alwaysOnTop: true);
        App.LogStartup("LyricsFlyoutWindow: AttachAcrylicBackdrop");
        (_acrylicController, _backdropConfig) = Win32WindowHelper.AttachAcrylicBackdrop(this, true, 0.52);

        _dismissMonitor = new Win32WindowHelper.PopupDismissMonitor(
            this,
            () => IsWindowVisible && _config.CloseOnDeactivate && !_config.PinLyricsFlyout,
            HidePopup);

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated &&
                _config.CloseOnDeactivate &&
                !_config.PinLyricsFlyout)
            {
                HidePopup();
            }
        };

        RootFrame.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape)
                HidePopup();
        };

        SpectrumBackdrop.SuppressVisuals = true;
        _starSeeds = BuildStarField(16);

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _tickTimer.Tick += (_, _) => UpdateLiveTimelineAndLyrics();

        _synthTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _synthTimer.Tick += OnSynthTimerTick;

        SyncContextMenuState();
        App.LogStartup("LyricsFlyoutWindow: done");
    }

    private static SkyStarSeed[] BuildStarField(int count)
    {
        var rng = new Random(198407);
        var stars = new SkyStarSeed[count];
        for (int i = 0; i < count; i++)
        {
            stars[i] = new SkyStarSeed(
                Nx: (float)(0.04 + rng.NextDouble() * 0.92),
                Ny: (float)(0.05 + rng.NextDouble() * 0.82),
                Radius: (float)(0.55 + rng.NextDouble() * 0.30),
                Speed: 0.65 + rng.NextDouble() * 0.70,
                Phase: rng.NextDouble() * Math.PI * 2.0,
                WarmTint: false);
        }
        return stars;
    }

    public void ShowAtAnchor(int leftPx, int topPx, int rightPx, int bottomPx)
    {
        Win32WindowHelper.PositionPopupAboveOrBelowAnchor(
            this, leftPx, topPx, rightPx, bottomPx,
            _config.LyricsFlyoutWidth, _config.LyricsFlyoutHeight, _config.PopupDirection);

        IsWindowVisible = true;
        _synthClock.Restart();
        _lastClockSec = 0;
        ApplyState(_bridge.CurrentState, _bridge.CurrentArtwork);
        _tickTimer.Start();
        if (_synthAnimations && _synthPlaying)
            _synthTimer.Start();
        Win32WindowHelper.ActivatePopupSafely(this);
        Win32WindowHelper.ApplyBorderlessDwmChrome(this);
        _dismissMonitor.Start();
    }

    public void HidePopup()
    {
        IsWindowVisible = false;
        _isScrubbing = false;
        _dismissMonitor.Stop();
        _tickTimer.Stop();
        _synthTimer.Stop();
        _synthClock.Stop();
        Win32WindowHelper.GetAppWindow(this).Hide();
    }

    public void ApplyState(StateDto state, BitmapImage? artwork)
    {
        bool prevDark = _isDark;
        _isDark = Win32WindowHelper.ResolveIsDark(state.Appearance, state.Settings, isTaskbar: false);
        if (prevDark != _isDark)
            ClearLyricSpriteCache();

        RootFrame.RequestedTheme = _isDark ? ElementTheme.Dark : ElementTheme.Light;
        string activeFont = !string.IsNullOrWhiteSpace(state.Appearance.FontFamily)
            ? state.Appearance.FontFamily
            : (!string.IsNullOrWhiteSpace(state.Settings.FontFamily) ? state.Settings.FontFamily : "Lucida Console, Sarasa Term SC, Microsoft YaHei UI");
        Win32WindowHelper.ApplyFontFamily(FontHost, activeFont);
        if (_backdropConfig != null)
            _backdropConfig.Theme = _isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;

        var media = state.Media;
        _accent = WeatherFormatting.ParseHexColor(media?.Accent, Color.FromArgb(255, 255, 107, 0));
        _secondaryAccentHex = media?.SecondaryAccent;
        var neon = SynthwavePaletteHelper.DeriveAlbumNeonPalette(_accent, _secondaryAccentHex);
        Win32WindowHelper.UpdateAcrylicTint(
            _acrylicController, _isDark, state.Appearance.GlassOpacity,
            state.Appearance.CoverGlow ? neon.Primary : null);

        CoverGlowMenuItem.IsChecked = state.Settings.CoverGlow;
        SyncContextMenuState();
        ApplySurfaceBrushes(neon);

        if (media == null)
        {
            SourceText.Text = "no-session";
            LyricsStatusText.Text = "DMC-12 // STANDBY";
            TrackTitleText.Text = "当前没有正在播放的媒体";
            TrackArtistText.Text = "播放音乐后将自动同步歌名、频谱与歌词";
            DockCoverImage.Visibility = Visibility.Collapsed;
            DockCoverFallback.Visibility = Visibility.Visible;
            PrevBtn.IsEnabled = false;
            PlayPauseBtn.IsEnabled = false;
            NextBtn.IsEnabled = false;
            _lyricLines.Clear();
            ClearLyricSpriteCache();
            LyricsWin2dCanvas?.Invalidate();
            DeckReflectionCanvas?.Invalidate();
            LyricsEmptyTitle.Text = "等待媒体播放";
            LyricsEmptyDesc.Text = "开启播放器后，桌面音乐会话会自动出现在这里";
            LyricsEmptyPanel.Visibility = Visibility.Visible;
            SyncVfdProgressSegments(0, 1);
            return;
        }

        SourceText.Text = string.IsNullOrWhiteSpace(media.Source) ? "system.media" : media.Source;
        LyricsStatusText.Text = "DMC-12 // LUCIDA MONO";
        TrackTitleText.Text = string.IsNullOrWhiteSpace(media.Title) ? "未知曲目" : media.Title;
        TrackArtistText.Text = string.IsNullOrWhiteSpace(media.Album)
            ? media.Artist
            : $"{media.Artist} · {media.Album}";

        if (artwork != null)
        {
            DockCoverImage.Source = artwork;
            DockCoverImage.Visibility = Visibility.Visible;
            DockCoverFallback.Visibility = Visibility.Collapsed;
        }
        else
        {
            DockCoverImage.Visibility = Visibility.Collapsed;
            DockCoverFallback.Visibility = Visibility.Visible;
        }

        PauseIconPath.Visibility = media.Playing ? Visibility.Visible : Visibility.Collapsed;
        PlayIconPath.Visibility = media.Playing ? Visibility.Collapsed : Visibility.Visible;
        bool canToggle = media.Controls?.Toggle ?? false;
        bool canPrev = media.Controls?.Previous ?? false;
        bool canNext = media.Controls?.Next ?? false;

        PlayPauseBtn.IsEnabled = canToggle;
        PrevBtn.IsEnabled = canPrev;
        NextBtn.IsEnabled = canNext;
        ToolTipService.SetToolTip(PlayPauseBtn, canToggle ? "播放 / 暂停" : "当前播放器未开放播放/暂停控制");
        ToolTipService.SetToolTip(PrevBtn, canPrev ? "上一首" : "当前播放器未开放上一首");
        ToolTipService.SetToolTip(NextBtn, canNext ? "下一首" : "当前播放器未开放下一首");

        _synthPlaying = media.Playing;
        _synthAnimations = state.Settings.Animations;

        SpectrumBackdrop.Configure(
            neon.Primary,
            media.Playing,
            state.Settings.Spectrum,
            state.Settings.Sensitivity,
            0.94,
            compactMode: false);

        SynthwaveBgWin2dCanvas?.Invalidate();
        SpectrumWin2dCanvas?.Invalidate();
        DeckReflectionCanvas?.Invalidate();
        CrtTubeOverlayCanvas?.Invalidate();
        if (IsWindowVisible && _synthAnimations && _synthPlaying)
        {
            if (!_synthClock.IsRunning)
            {
                _synthClock.Restart();
                _lastClockSec = 0;
            }
            if (!_synthTimer.IsEnabled)
                _synthTimer.Start();
        }
        else
        {
            _synthTimer.Stop();
            _synthClock.Stop();
        }

        RebuildLyricsIfNeeded(state);
        UpdateLiveTimelineAndLyrics();
    }

    public void ApplySpectrum(SpectrumDto spectrum)
    {
        if (!IsWindowVisible) return;
        SpectrumBackdrop.UpdateBands(spectrum.Bands);
        SpectrumWin2dCanvas?.Invalidate();
        DeckReflectionCanvas?.Invalidate();
    }

    private void ApplySurfaceBrushes(SynthwavePaletteHelper.NeonAlbumPalette neon)
    {
        var gradRim = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        gradRim.GradientStops.Add(new GradientStop { Color = Color.FromArgb(148, 0, 245, 212), Offset = 0.0 });
        gradRim.GradientStops.Add(new GradientStop { Color = Color.FromArgb(108, 139, 92, 246), Offset = 0.5 });
        gradRim.GradientStops.Add(new GradientStop { Color = Color.FromArgb(148, 255, 42, 133), Offset = 1.0 });

        RootFrame.Background = new SolidColorBrush(_isDark ? Color.FromArgb(245, 9, 8, 20) : Color.FromArgb(242, 246, 244, 255));
        RootFrame.BorderBrush = new SolidColorBrush(Colors.Transparent);

        SourceBadge.Background = new SolidColorBrush(_isDark ? Color.FromArgb(184, 16, 58, 70) : Color.FromArgb(42, 14, 116, 144));
        SourceBadge.BorderBrush = gradRim;
        SourceText.Foreground = new SolidColorBrush(_isDark ? Color.FromArgb(255, 103, 232, 249) : Color.FromArgb(255, 14, 116, 144));
        LyricsStatusText.Foreground = new SolidColorBrush(_isDark ? Color.FromArgb(204, 253, 230, 138) : Color.FromArgb(215, 180, 83, 9));
        ElapsedHeaderBadge.Foreground = new SolidColorBrush(_isDark ? Color.FromArgb(204, 103, 232, 249) : Color.FromArgb(215, 14, 116, 144));

        var deckBg = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        if (_isDark)
        {
            deckBg.GradientStops.Add(new GradientStop { Color = Color.FromArgb(204, 11, 15, 28), Offset = 0.0 });
            deckBg.GradientStops.Add(new GradientStop { Color = Color.FromArgb(230, 9, 10, 22), Offset = 0.48 });
            deckBg.GradientStops.Add(new GradientStop { Color = Color.FromArgb(245, 7, 8, 18), Offset = 1.0 });
        }
        else
        {
            deckBg.GradientStops.Add(new GradientStop { Color = Color.FromArgb(210, 244, 245, 252), Offset = 0.0 });
            deckBg.GradientStops.Add(new GradientStop { Color = Color.FromArgb(235, 234, 238, 250), Offset = 1.0 });
        }
        ControlDeckCard.Background = deckBg;
        ControlDeckCard.BorderBrush = gradRim;

        var trayBg = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        trayBg.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 5, 7, 14), Offset = 0.0 });
        trayBg.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 11, 16, 29), Offset = 1.0 });
        HardwareTray.Background = trayBg;
        HardwareTray.BorderBrush = new SolidColorBrush(Color.FromArgb(242, 15, 23, 42));
        VfdProgressTray.Background = trayBg;
        VfdProgressTray.BorderBrush = new SolidColorBrush(Color.FromArgb(242, 15, 23, 42));

        var coverBg = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        coverBg.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Primary, 210), Offset = 0.0 });
        coverBg.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Secondary, 210), Offset = 1.0 });
        DockCoverBorder.Background = coverBg;
        DockCoverBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255));

        TrackTitleText.Foreground = new SolidColorBrush(_isDark ? Color.FromArgb(255, 241, 245, 249) : Color.FromArgb(255, 24, 24, 48));
        TrackArtistText.Foreground = new SolidColorBrush(_isDark ? Color.FromArgb(204, 253, 230, 138) : Color.FromArgb(255, 124, 58, 237));
        ImportDrawer.Background = new SolidColorBrush(_isDark ? Color.FromArgb(244, 10, 12, 24) : Color.FromArgb(246, 246, 248, 255));
        ImportDrawer.BorderBrush = gradRim;

        SynthwavePaletteHelper.ApplyB1KeyStyle(PrevBtn, isPrimary: false, isDark: _isDark);
        SynthwavePaletteHelper.ApplyB1KeyStyle(PlayPauseBtn, isPrimary: true, isDark: _isDark);
        SynthwavePaletteHelper.ApplyB1KeyStyle(NextBtn, isPrimary: false, isDark: _isDark);
    }

    // ==================== Win2D Synthwave Sky + Sunset Halo + Ground Reflection ====================

    private void OnSynthBgCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        EnsureSynthBgResources(sender);
    }

    private void OnSynthBgSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is CanvasControl c)
        {
            EnsureSynthBgResources(c);
            c.Invalidate();
        }
        DeckReflectionCanvas?.Invalidate();
    }

    private void OnSynthBgUnloaded(object sender, RoutedEventArgs e)
    {
        DisposeSynthBgResources();
        try { SynthwaveBgWin2dCanvas?.RemoveFromVisualTree(); } catch { }
    }

    private void DisposeSynthBgResources()
    {
        _crtDispBitmap?.Dispose();
        _crtDispBitmap = null;
        _synthCompositeTarget?.Dispose();
        _synthCompositeTarget = null;
        _synthSkySunTarget?.Dispose();
        _synthSkySunTarget = null;
        _synthRoadReflTarget?.Dispose();
        _synthRoadReflTarget = null;
        _bgW = -1;
        _bgH = -1;
        _bgDpi = -1;
    }

    private void EnsureSynthBgResources(CanvasControl canvas)
    {
        float w = (float)canvas.ActualWidth;
        float h = (float)canvas.ActualHeight;
        float dpi = canvas.Dpi;
        if (w < 40 || h < 60 || !canvas.ReadyToDraw) return;

        if (_synthCompositeTarget != null &&
            _crtDispBitmap != null &&
            Math.Abs(_bgW - w) < 0.5f &&
            Math.Abs(_bgH - h) < 0.5f &&
            Math.Abs(_bgDpi - dpi) < 0.5f)
        {
            return;
        }

        DisposeSynthBgResources();
        _bgW = w;
        _bgH = h;
        _bgDpi = dpi;
        _crtDispBitmap = SynthwaveEffects.CreateCrtDisplacementBitmap(
            canvas.Device, Math.Max(4, (int)MathF.Round(w)), Math.Max(4, (int)MathF.Round(h)), CrtMaxShiftDip, CrtBarrelStrength);
        _synthCompositeTarget = new CanvasRenderTarget(canvas.Device, w, h, dpi);
        _synthSkySunTarget = new CanvasRenderTarget(canvas.Device, w, h, dpi);
        _synthRoadReflTarget = new CanvasRenderTarget(canvas.Device, w, h, dpi);
        LyricsWin2dCanvas?.Invalidate();
        SpectrumWin2dCanvas?.Invalidate();
        CrtTubeOverlayCanvas?.Invalidate();
    }

    private void OnSynthBgDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        EnsureSynthBgResources(sender);
        if (_synthCompositeTarget == null || _synthSkySunTarget == null || _synthRoadReflTarget == null || _crtDispBitmap == null) return;

        float w = _bgW;
        float h = _bgH;
        float horizonY = MathF.Round(h * 0.463f);
        float sunCx = w * 0.5f;
        float sunR = Math.Clamp(MathF.Round(w * 0.120f), 50f, 64f);
        float sunCy = horizonY - sunR * 0.34f;
        var device = sender.Device;
        bool show = _bridge.CurrentState.Appearance.CoverGlow;
        var neon = SynthwavePaletteHelper.DeriveAlbumNeonPalette(_accent, _secondaryAccentHex);

        using (var ds = _synthCompositeTarget.CreateDrawingSession())
        {
            ds.Clear(Color.FromArgb(255, 8, 9, 23));

            // 1. Outrun twilight sky gradient (padded horizontally/top for corner CRT warp)
            using (var skyBrush = new CanvasLinearGradientBrush(device,
            [
                new CanvasGradientStop { Position = 0.0f, Color = Color.FromArgb(255, 8, 9, 23) },
                new CanvasGradientStop { Position = 0.65f, Color = Color.FromArgb(255, 21, 16, 43) },
                new CanvasGradientStop { Position = 1.0f, Color = Color.FromArgb(255, 35, 16, 51) }
            ])
            { StartPoint = Vector2.Zero, EndPoint = new Vector2(0, horizonY) })
            {
                ds.FillRectangle(-24, -20, w + 48, horizonY + 20, skyBrush);
            }

            // 2. Ground dark laser road gradient
            using (var groundBrush = new CanvasLinearGradientBrush(device,
            [
                new CanvasGradientStop { Position = 0.0f, Color = Color.FromArgb(255, 18, 12, 36) },
                new CanvasGradientStop { Position = 0.35f, Color = Color.FromArgb(255, 11, 13, 27) },
                new CanvasGradientStop { Position = 1.0f, Color = Color.FromArgb(255, 7, 8, 18) }
            ])
            { StartPoint = new Vector2(0, horizonY), EndPoint = new Vector2(0, h) })
            {
                ds.FillRectangle(-24, horizonY, w + 48, Math.Max(0, h - horizonY) + 20, groundBrush);
            }

            // 3. Calm micro-stars matching concept density (16 tiny pale-cyan pinpoints)
            for (int i = 0; i < _starSeeds.Length; i++)
            {
                var star = _starSeeds[i];
                float sx = star.Nx * w;
                float sy = star.Ny * Math.Max(20f, horizonY - 14f);
                double wave = 0.5 + 0.5 * Math.Sin(_synthTick * star.Speed + star.Phase);
                double alpha = 0.14 + 0.24 * wave;
                byte aStar = (byte)Math.Clamp(alpha * 255, 24, 108);
                ds.FillCircle(sx, sy, star.Radius, Color.FromArgb(aStar, 165, 243, 252));
            }

            if (show)
            {
                // 4a. Subtle atmospheric solar corona behind sun (clipped above horizonY)
                using (ds.CreateLayer(1.0f, new Rect(0, 0, w, horizonY)))
                {
                    using var corona = new CanvasRadialGradientBrush(device,
                    [
                        new CanvasGradientStop { Position = 0.0f, Color = SynthwavePaletteHelper.WithAlpha(neon.Highlight, 36) },
                        new CanvasGradientStop { Position = 0.45f, Color = SynthwavePaletteHelper.WithAlpha(neon.Primary, 22) },
                        new CanvasGradientStop { Position = 1.0f, Color = Colors.Transparent }
                    ])
                    {
                        Center = new Vector2(sunCx, sunCy),
                        RadiusX = sunR * 2.15f,
                        RadiusY = sunR * 1.65f
                    };
                    ds.FillEllipse(sunCx, sunCy, sunR * 2.15f, sunR * 1.65f, corona);
                }

                // 4b. Album-sampled sunset orb with TRUE transparent DestinationOut cutouts + subtle CRT distortion
                using var cutMask = new CanvasCommandList(device);
                using (var mds = cutMask.CreateDrawingSession())
                {
                    for (int s = 0; s < 3; s++)
                        mds.FillRectangle(sunCx - sunR - 4f, horizonY - 25f + s * 8f, sunR * 2f + 8f, 1.8f + s * 0.7f, Colors.White);

                    Color crtWhite = Color.FromArgb(42, 255, 255, 255);
                    for (float sy = sunCy - 4f; sy < horizonY; sy += 3.0f)
                        mds.FillRectangle(sunCx - sunR - 2f, sy, sunR * 2f + 4f, 1.0f, crtWhite);
                }

                using (var sds = _synthSkySunTarget.CreateDrawingSession())
                {
                    sds.Clear(Colors.Transparent);
                    using var orbRaw = new CanvasCommandList(device);
                    using (var ods = orbRaw.CreateDrawingSession())
                    {
                        using var sunGrad = new CanvasLinearGradientBrush(device,
                        [
                            new CanvasGradientStop { Position = 0.0f, Color = neon.Highlight },
                            new CanvasGradientStop { Position = 0.65f, Color = neon.Primary },
                            new CanvasGradientStop { Position = 1.0f, Color = neon.Secondary }
                        ])
                        {
                            StartPoint = new Vector2(0, sunCy - sunR),
                            EndPoint = new Vector2(0, horizonY)
                        };
                        ods.FillCircle(sunCx, sunCy, sunR, sunGrad);
                    }

                    using var punchedSun = SynthwaveEffects.PunchOut(orbRaw, cutMask);
                    using (sds.CreateLayer(1.0f, new Rect(0, 0, w, horizonY)))
                    {
                        if (_synthPlaying && _synthAnimations)
                        {
                            float bandY = sunCy + sunR * 0.10f + (float)((Math.Sin(_synthTick * 1.6) * 0.5 + 0.5) * Math.Max(4f, horizonY - sunCy - 8f));
                            float shiftPx = (float)(Math.Sin(_synthTick * 9.0) * 1.1);
                            using var distortedSun = SynthwaveEffects.TearSlice(punchedSun, w, horizonY, Math.Clamp(bandY, 2f, horizonY - 6f), shiftPx, 4.5f, sender.Dpi);
                            sds.DrawImage(distortedSun);
                        }
                        else
                        {
                            sds.DrawImage(punchedSun);
                        }
                    }
                }

                using (var sunHalo = SynthwaveEffects.Glow(_synthSkySunTarget, 6.5f))
                using (var haloOpacity = new OpacityEffect { Source = sunHalo, Opacity = 0.28f })
                {
                    ds.DrawImage(haloOpacity);
                }
                using (var orbOpacity = new OpacityEffect { Source = _synthSkySunTarget, Opacity = 0.44f })
                {
                    ds.DrawImage(orbOpacity);
                }

                // 4c. Subtle horizon scatter warmth
                using (var horizScatter = new CanvasRadialGradientBrush(device,
                [
                    new CanvasGradientStop { Position = 0.0f, Color = SynthwavePaletteHelper.WithAlpha(neon.Highlight, 30) },
                    new CanvasGradientStop { Position = 0.50f, Color = SynthwavePaletteHelper.WithAlpha(neon.Primary, 14) },
                    new CanvasGradientStop { Position = 1.0f, Color = Colors.Transparent }
                ])
                {
                    Center = new Vector2(sunCx, horizonY),
                    RadiusX = sunR * 1.9f,
                    RadiusY = 18f
                })
                {
                    ds.FillEllipse(sunCx, horizonY, sunR * 1.9f, 18f, horizScatter);
                }

                // 5a. Glossy ground reflection sheen beneath horizon
                using (var roadSheen = new CanvasRadialGradientBrush(device,
                [
                    new CanvasGradientStop { Position = 0.0f, Color = SynthwavePaletteHelper.WithAlpha(neon.Primary, 60) },
                    new CanvasGradientStop { Position = 0.48f, Color = SynthwavePaletteHelper.WithAlpha(neon.Secondary, 30) },
                    new CanvasGradientStop { Position = 1.0f, Color = Colors.Transparent }
                ])
                {
                    Center = new Vector2(sunCx, horizonY + 14f),
                    RadiusX = w * 0.44f,
                    RadiusY = Math.Max(40f, (h - horizonY) * 0.72f)
                })
                {
                    ds.FillEllipse(sunCx, horizonY + 14f, w * 0.44f, Math.Max(40f, (h - horizonY) * 0.72f), roadSheen);
                }

                // 5b. 24 shimmering perspective reflection ripples -> Glow(4.5)
                using (var rds = _synthRoadReflTarget.CreateDrawingSession())
                {
                    rds.Clear(Colors.Transparent);
                    float reflTopY = horizonY + 2f;
                    float reflDepth = Math.Max(40f, h - horizonY - 10f);
                    for (int row = 0; row < 24; row++)
                    {
                        float frac = row / 24f;
                        float ry = reflTopY + frac * frac * reflDepth;
                        float rw = (sunR * 1.65f) * (1f - frac * 0.40f) + (float)Math.Sin(_synthTick * 2.4 + row * 0.85) * 5.5f;
                        double alpha = (1.0 - frac * 0.88) * 0.34 * (0.76 + 0.24 * Math.Sin(_synthTick * 1.8 + row));
                        byte ba = (byte)Math.Clamp(alpha * 255, 8, 120);
                        Color stripColor = frac < 0.42f ? neon.Secondary : (frac < 0.75f ? neon.Primary : neon.Highlight);

                        using var rGrad = new CanvasLinearGradientBrush(device,
                        [
                            new CanvasGradientStop { Position = 0.0f, Color = Colors.Transparent },
                            new CanvasGradientStop { Position = 0.5f, Color = SynthwavePaletteHelper.WithAlpha(stripColor, ba) },
                            new CanvasGradientStop { Position = 1.0f, Color = Colors.Transparent }
                        ])
                        {
                            StartPoint = new Vector2(sunCx - rw / 2f, ry),
                            EndPoint = new Vector2(sunCx + rw / 2f, ry)
                        };
                        rds.FillRectangle(sunCx - rw / 2f, ry, Math.Max(4f, rw), 2.2f + frac * 1.8f, rGrad);
                    }
                }

                using (var reflBlur = SynthwaveEffects.Glow(_synthRoadReflTarget, 4.5f))
                {
                    ds.DrawImage(reflBlur);
                }
                ds.DrawImage(_synthRoadReflTarget);
            }

            // 6. Wide Outrun Perspective Laser Grid + Fast Concept-Speed Forward Scanlines
            Color rayColor = Color.FromArgb(62, 168, 85, 247);
            for (int r = -5; r <= 5; r++)
            {
                ds.DrawLine(sunCx + r * 22f, horizonY, sunCx + r * 116f, h + 18f,
                    Math.Abs(r) <= 1 ? Color.FromArgb(52, 0, 245, 212) : rayColor, 1.0f);
            }

            double scroll = (_synthTick * 0.14) % 1.0;
            float span = Math.Max(40f, h - horizonY);
            const int scanCount = 7;
            for (int s = 0; s < scanCount; s++)
            {
                float frac = (float)(((double)s / scanCount + scroll) % 1.0);
                float y = horizonY + frac * frac * span;
                byte sa = (byte)Math.Clamp((0.10 + frac * 0.24) * 255, 18, 96);
                ds.DrawLine(-20f, y, w + 20f, y, Color.FromArgb(sa, 255, 42, 133), 1.0f + frac * 0.35f);
            }

            // 7. Concept-exact crisp 1.5px horizon laser seam (cyan 0.48 -> #ff7a00 0.72 -> cyan 0.48)
            using var seamBrush = new CanvasLinearGradientBrush(device,
            [
                new CanvasGradientStop { Position = 0.0f, Color = Colors.Transparent },
                new CanvasGradientStop { Position = 0.25f, Color = Color.FromArgb(122, 0, 245, 212) },
                new CanvasGradientStop { Position = 0.50f, Color = Color.FromArgb(184, 255, 122, 0) },
                new CanvasGradientStop { Position = 0.75f, Color = Color.FromArgb(122, 0, 245, 212) },
                new CanvasGradientStop { Position = 1.0f, Color = Colors.Transparent }
            ])
            { StartPoint = new Vector2(-12f, 0), EndPoint = new Vector2(w + 12f, 0) };
            ds.FillRectangle(-12f, horizonY - 0.8f, w + 24f, 1.5f, seamBrush);
        }

        // Apply convex CRT screen barrel curvature across entire background stage
        using var crtWarped = SynthwaveEffects.WarpCrt(_synthCompositeTarget, _crtDispBitmap, 0f, 0f, CrtMaxShiftDip);
        args.DrawingSession.DrawImage(crtWarped);
    }

    // ==================== Win2D Zone 4 Deck Optical Reflection ====================

    private void OnDeckReflCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        EnsureDeckReflResources(sender);
    }

    private void OnDeckReflSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is CanvasControl c)
        {
            EnsureDeckReflResources(c);
            c.Invalidate();
        }
    }

    private void OnDeckReflUnloaded(object sender, RoutedEventArgs e)
    {
        DisposeDeckReflResources();
        try { DeckReflectionCanvas?.RemoveFromVisualTree(); } catch { }
    }

    private void DisposeDeckReflResources()
    {
        _reflAuraTarget?.Dispose();
        _reflAuraTarget = null;
        _reflBricksTarget?.Dispose();
        _reflBricksTarget = null;
        _reflFresnelMask?.Dispose();
        _reflFresnelMask = null;
        _reflW = -1;
        _reflH = -1;
        _reflDpi = -1;
    }

    private void EnsureDeckReflResources(CanvasControl canvas)
    {
        float w = (float)canvas.ActualWidth;
        float h = (float)canvas.ActualHeight;
        float dpi = canvas.Dpi;
        if (w < 16 || h < 8 || !canvas.ReadyToDraw) return;

        if (_reflAuraTarget != null &&
            Math.Abs(_reflW - w) < 0.5f &&
            Math.Abs(_reflH - h) < 0.5f &&
            Math.Abs(_reflDpi - dpi) < 0.5f)
        {
            return;
        }

        DisposeDeckReflResources();
        var device = canvas.Device;
        _reflW = w;
        _reflH = h;
        _reflDpi = dpi;

        _reflAuraTarget = new CanvasRenderTarget(device, w, h, dpi);
        _reflBricksTarget = new CanvasRenderTarget(device, w, h, dpi);
        _reflFresnelMask = new CanvasRenderTarget(device, w, h, dpi);

        using var ds = _reflFresnelMask.CreateDrawingSession();
        ds.Clear(Colors.Transparent);
        using var fresnel = new CanvasLinearGradientBrush(device,
        [
            new CanvasGradientStop { Position = 0.0f, Color = Color.FromArgb(235, 255, 255, 255) },
            new CanvasGradientStop { Position = 0.28f, Color = Color.FromArgb(122, 255, 255, 255) },
            new CanvasGradientStop { Position = 0.68f, Color = Color.FromArgb(31, 255, 255, 255) },
            new CanvasGradientStop { Position = 1.0f, Color = Colors.Transparent }
        ])
        {
            StartPoint = Vector2.Zero,
            EndPoint = new Vector2(0, h)
        };
        ds.FillRectangle(0, 0, w, h, fresnel);
    }

    private void OnDeckReflDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (!_bridge.CurrentState.Settings.Spectrum) return;
        EnsureDeckReflResources(sender);
        if (_reflAuraTarget == null || _reflBricksTarget == null || _reflFresnelMask == null) return;

        float w = _reflW;
        var levels = SpectrumBackdrop.CurrentLevels;
        if (levels.Count == 0) return;

        float x0 = 6f;
        float specW = Math.Max(40f, w - 12f);
        float colGap = 4.5f;
        float colW = (specW - colGap * (SpectrumCols - 1)) / SpectrumCols;

        // Pass 1: Wide column aura blobs -> GaussianBlur(11) (desaturated vintage VFD reflection)
        using (var ds = _reflAuraTarget.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            for (int i = 0; i < SpectrumCols && i < levels.Count; i++)
            {
                double val = _synthPlaying ? Math.Clamp(levels[i], 0.0, 1.0) : 0.05;
                float cx = x0 + i * (colW + colGap) + colW / 2f;
                byte a = (byte)Math.Clamp((val > 0.72 ? 0.18 + val * 0.28 : 0.14 + val * 0.30) * 255, 8, 132);
                Color c = val > 0.72 ? Color.FromArgb(a, 199, 142, 78) : Color.FromArgb(a, 90, 168, 160);
                ds.FillEllipse(cx, 6f, colW * 0.95f, (float)(18.0 + val * 16.0), c);
            }
        }

        // Pass 2: Mirrored bottom 1-3 lit VFD brick rectangles -> GaussianBlur(3.2)
        using (var ds = _reflBricksTarget.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            for (int i = 0; i < SpectrumCols && i < levels.Count; i++)
            {
                double val = _synthPlaying ? Math.Clamp(levels[i], 0.0, 1.0) : 0.05;
                int litRows = (int)Math.Round(val * SpectrumRows);
                float x = x0 + i * (colW + colGap);
                for (int r = 0; r < Math.Min(3, litRows); r++)
                {
                    float mirrorY = 2f + r * 11f;
                    double alpha = _synthPlaying ? (r == 0 ? 0.36 : (r == 1 ? 0.19 : 0.09)) : 0.05;
                    byte ba = (byte)Math.Clamp(alpha * 255, 6, 112);
                    Color bc = r < 2 ? Color.FromArgb(ba, 90, 168, 160) : Color.FromArgb(ba, 199, 142, 78);
                    ds.FillRectangle(x - 1f, mirrorY, colW + 2f, 7f, bc);
                }
            }
        }

        // Compose Pass 1 (blur 11) + Pass 2 (blur 3.2) via Screen, then Pass 3 Mask(Fresnel)
        using var auraBlur = SynthwaveEffects.Glow(_reflAuraTarget, 11f);
        using var bricksBlur = SynthwaveEffects.Glow(_reflBricksTarget, 3.2f);
        using var combined = SynthwaveEffects.Screen(auraBlur, bricksBlur);
        using var masked = SynthwaveEffects.Mask(combined, _reflFresnelMask);
        args.DrawingSession.DrawImage(masked);

        // Pass 4: 1.2px dynamic specular highlight strip along y=0
        var rimStops = new CanvasGradientStop[SpectrumCols];
        for (int i = 0; i < SpectrumCols && i < levels.Count; i++)
        {
            float t = Math.Clamp((x0 + i * (colW + colGap) + colW / 2f) / Math.Max(1f, w), 0.02f, 0.98f);
            double val = _synthPlaying ? Math.Clamp(levels[i], 0.0, 1.0) : 0.04;
            byte a = (byte)Math.Clamp((0.10 + val * 0.48) * 255, 16, 172);
            Color rc = val > 0.78 ? Color.FromArgb(a, 214, 168, 112) : Color.FromArgb(a, 128, 198, 192);
            rimStops[i] = new CanvasGradientStop { Position = t, Color = rc };
        }
        using var rimBrush = new CanvasLinearGradientBrush(sender.Device, rimStops)
        {
            StartPoint = Vector2.Zero,
            EndPoint = new Vector2(w, 0)
        };
        args.DrawingSession.FillRectangle(4f, 0f, Math.Max(0f, w - 8f), 1.2f, rimBrush);
    }

    // ==================== Win2D Zone 2 C2 Lyrics Canvas ====================

    private void OnLyricsCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        ClearLyricSpriteCache();
        DisposeLyricsStaging();
        EnsureLyricsStaging(sender);
    }

    private void OnLyricsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is CanvasControl c)
        {
            ClearLyricSpriteCache();
            EnsureLyricsStaging(c);
            c.Invalidate();
        }
    }

    private void OnLyricsUnloaded(object sender, RoutedEventArgs e)
    {
        ClearLyricSpriteCache();
        DisposeLyricsStaging();
        try { LyricsWin2dCanvas?.RemoveFromVisualTree(); } catch { }
    }

    private void ClearLyricSpriteCache()
    {
        foreach (var rt in _lyricSpriteCache.Values)
            rt.Dispose();
        _lyricSpriteCache.Clear();
    }

    private void DisposeLyricsStaging()
    {
        _lyricsCompositeTarget?.Dispose();
        _lyricsCompositeTarget = null;
        _lyricScanMask?.Dispose();
        _lyricScanMask = null;
        _lyricTempCore?.Dispose();
        _lyricTempCore = null;
        _lyricTempCombined?.Dispose();
        _lyricTempCombined = null;
        _lyricsSurfaceW = -1;
        _lyricsCanvasW = -1;
        _lyricsCanvasH = -1;
        _lyricsDpi = -1;
    }

    private void EnsureLyricsStaging(CanvasControl canvas)
    {
        float w = (float)canvas.ActualWidth;
        float h = (float)canvas.ActualHeight;
        float dpi = canvas.Dpi;
        if (w < 40 || h < 20 || !canvas.ReadyToDraw) return;
        float spriteW = Math.Max(40f, w - 16f);

        if (_lyricScanMask != null &&
            _lyricsCompositeTarget != null &&
            Math.Abs(_lyricsSurfaceW - spriteW) < 0.5f &&
            Math.Abs(_lyricsCanvasW - w) < 0.5f &&
            Math.Abs(_lyricsCanvasH - h) < 0.5f &&
            Math.Abs(_lyricsDpi - dpi) < 0.5f)
        {
            return;
        }

        DisposeLyricsStaging();
        ClearLyricSpriteCache();
        _lyricsSurfaceW = spriteW;
        _lyricsCanvasW = w;
        _lyricsCanvasH = h;
        _lyricsDpi = dpi;

        var device = canvas.Device;
        _lyricsCompositeTarget = new CanvasRenderTarget(device, w, h, dpi);
        _lyricScanMask = SynthwaveEffects.ScanlineMask(device, spriteW, LyricRowHeight, dpi);
        _lyricTempCore = new CanvasRenderTarget(device, spriteW, LyricRowHeight, dpi);
        _lyricTempCombined = new CanvasRenderTarget(device, spriteW, LyricRowHeight, dpi);
    }

    private CanvasTextLayout BuildBilingualLayout(CanvasDevice device, string text, bool active, float maxW)
    {
        double baseSize = _renderedFontSize > 0 ? _renderedFontSize : 14.5;
        float enSize = active ? (float)Math.Clamp(baseSize, 13.5, 17.5) : (float)Math.Clamp(baseSize * 0.79, 11.0, 13.5);
        float cjkSize = active ? (float)Math.Clamp(baseSize * 1.10, 14.5, 19.0) : (float)Math.Clamp(baseSize * 0.86, 12.0, 14.5);

        string cjkFont = IsSystemFontInstalled("Sarasa Term SC") ? "Sarasa Term SC" : "Microsoft YaHei UI";
        string enFont = IsSystemFontInstalled("Lucida Console") ? "Lucida Console" : "Consolas";

        using var format = new CanvasTextFormat
        {
            FontFamily = cjkFont,
            FontSize = cjkSize,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            WordWrapping = CanvasWordWrapping.NoWrap,
            TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            VerticalAlignment = CanvasVerticalAlignment.Center
        };

        var layout = new CanvasTextLayout(device, text, format, maxW, LyricRowHeight);
        foreach (Match m in BilingualTokenRegex.Matches(text))
        {
            bool isAscii = true;
            foreach (char ch in m.Value)
            {
                if (ch > 127) { isAscii = false; break; }
            }
            if (isAscii)
            {
                layout.SetFontFamily(m.Index, m.Length, enFont);
                layout.SetFontSize(m.Index, m.Length, enSize);
            }
            else
            {
                layout.SetFontFamily(m.Index, m.Length, cjkFont);
                layout.SetFontSize(m.Index, m.Length, cjkSize);
            }
        }
        return layout;
    }

    private static HashSet<string>? _systemFontsCache;
    private static bool IsSystemFontInstalled(string family)
    {
        _systemFontsCache ??= new HashSet<string>(CanvasTextFormat.GetSystemFontFamilies(), StringComparer.OrdinalIgnoreCase);
        return _systemFontsCache.Contains(family);
    }

    private CanvasRenderTarget? GetOrBuildLyricSprite(CanvasDevice device, string text, bool active, float spriteW, float dpi)
    {
        if (_lyricScanMask == null || _lyricTempCore == null || _lyricTempCombined == null) return null;

        string key = $"{text}|{(active ? 1 : 0)}|{Math.Round(spriteW)}|{Math.Round(dpi)}|{(_isDark ? 1 : 0)}";
        if (_lyricSpriteCache.TryGetValue(key, out var cached))
            return cached;

        if (_lyricSpriteCache.Count > 120)
            ClearLyricSpriteCache();

        using var layout = BuildBilingualLayout(device, text, active, spriteW - 8f);

        using (var ds = _lyricTempCore.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            if (active && _isDark)
            {
                using var gradBrush = new CanvasLinearGradientBrush(device,
                [
                    new CanvasGradientStop { Position = 0.0f, Color = Colors.White },
                    new CanvasGradientStop { Position = 0.52f, Color = Color.FromArgb(255, 0x67, 0xE8, 0xF9) },
                    new CanvasGradientStop { Position = 1.0f, Color = Color.FromArgb(255, 0xFD, 0xE6, 0x8A) }
                ])
                {
                    StartPoint = new Vector2(4f, 0f),
                    EndPoint = new Vector2(Math.Max(60f, (float)layout.LayoutBounds.Width + 12f), 0f)
                };
                ds.DrawTextLayout(layout, new Vector2(4f, 0f), gradBrush);
            }
            else
            {
                Color c = active
                    ? Color.FromArgb(255, 190, 24, 93)
                    : (_isDark ? Color.FromArgb(255, 203, 213, 225) : Color.FromArgb(255, 51, 65, 85));
                ds.DrawTextLayout(layout, new Vector2(4f, 0f), c);
            }
        }

        var finalSprite = new CanvasRenderTarget(device, spriteW, LyricRowHeight, dpi);
        using (var ds = _lyricTempCombined.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            if (active && _isDark)
            {
                ds.DrawTextLayout(layout, new Vector2(4f - 1.4f, 0f), Color.FromArgb(140, 0, 245, 212));
                ds.DrawTextLayout(layout, new Vector2(4f + 1.4f, 0f), Color.FromArgb(140, 255, 42, 133));
            }
            ds.DrawImage(_lyricTempCore);
        }

        using (var ds = finalSprite.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            if (active && _isDark)
            {
                using var scanOpacity = new OpacityEffect { Source = _lyricScanMask, Opacity = 0.30f };
                using var punched = SynthwaveEffects.PunchOut(_lyricTempCombined, scanOpacity);
                ds.DrawImage(punched);
            }
            else
            {
                ds.DrawImage(_lyricTempCombined);
            }
        }

        _lyricSpriteCache[key] = finalSprite;
        return finalSprite;
    }

    private float ComputeLyricsBaseTop(float viewportH)
    {
        if (_lyricLines.Count <= 4)
            return MathF.Round((viewportH - _lyricLines.Count * LyricRowHeight) / 2f);
        return MathF.Round(viewportH * 0.5f - LyricRowHeight * 0.5f);
    }

    private void UpdateLyricsTargetScroll(bool snapImmediate)
    {
        float h = (float)(LyricsWin2dCanvas.ActualHeight > 20 ? LyricsWin2dCanvas.ActualHeight : 134);
        if (_lyricLines.Count <= 4 || _activeLineIndex < 0)
        {
            _targetScrollY = 0;
            if (snapImmediate) _currentScrollY = 0;
            return;
        }

        float totalH = _lyricLines.Count * LyricRowHeight;
        float maxScroll = Math.Max(0f, totalH - h * 0.65f);
        double desired = Math.Clamp(_activeLineIndex * LyricRowHeight, 0.0, maxScroll);
        _targetScrollY = desired;
        if (snapImmediate)
            _currentScrollY = desired;
    }

    private (float X, float Y) GetOriginRelativeToRoot(UIElement el, float fallbackX, float fallbackY)
    {
        try
        {
            var pt = el.TransformToVisual(RootFrame).TransformPoint(new Point(0, 0));
            return ((float)pt.X, (float)pt.Y);
        }
        catch
        {
            return (fallbackX, fallbackY);
        }
    }

    private void OnLyricsDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_lyricLines.Count == 0) return;
        EnsureLyricsStaging(sender);
        if (_lyricScanMask == null || _lyricsCompositeTarget == null) return;

        float h = _lyricsCanvasH;
        float spriteW = _lyricsSurfaceW;
        float startY = ComputeLyricsBaseTop(h);

        using (var ds = _lyricsCompositeTarget.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            for (int i = 0; i < _lyricLines.Count; i++)
            {
                float cy = startY + i * LyricRowHeight + LyricRowHeight / 2f - (float)_currentScrollY;
                if (cy < -LyricRowHeight || cy > h + LyricRowHeight) continue;

                bool active = i == _activeLineIndex;
                int dist = _activeLineIndex >= 0 ? Math.Abs(i - _activeLineIndex) : 2;

                if (active)
                {
                    ds.FillCircle(8f, cy, 6.2f, Color.FromArgb(68, 255, 122, 0));
                    ds.FillCircle(8f, cy, 3.6f, Color.FromArgb(255, 255, 122, 0));
                }
                else
                {
                    ds.FillCircle(8f, cy, 1.5f, Color.FromArgb(72, 148, 163, 184));
                }

                var sprite = GetOrBuildLyricSprite(sender.Device, _lyricLines[i].Text, active, spriteW, sender.Dpi);
                if (sprite == null) continue;

                float destX = 16f;
                float destY = MathF.Round(cy - LyricRowHeight / 2f);

                if (active)
                {
                    if (_synthPlaying && _synthAnimations)
                    {
                        float bandProgress = (float)((_synthTick * 0.55) % 1.0);
                        float sliceHeightDip = 7f * 96f / sender.Dpi;
                        float bandTop = Math.Clamp(bandProgress * Math.Max(2f, LyricRowHeight - sliceHeightDip - 2f), 1f, Math.Max(1f, LyricRowHeight - sliceHeightDip - 1f));
                        float glitchShiftPx = (float)(Math.Sin(_synthTick * 17.0) > 0.78
                            ? Math.Sin(_synthTick * 31.0) * 2.4
                            : Math.Sin(_synthTick * 4.0) * 0.9);
                        using var torn = SynthwaveEffects.TearSlice(sprite, spriteW, LyricRowHeight, bandTop, glitchShiftPx, 7f, sender.Dpi);
                        ds.DrawImage(torn, new Vector2(destX, destY));
                    }
                    else
                    {
                        ds.DrawImage(sprite, new Vector2(destX, destY));
                    }
                }
                else
                {
                    float rowAlpha = dist == 1 ? 0.42f : 0.22f;
                    ds.DrawImage(sprite, new Rect(destX, destY, spriteW, LyricRowHeight), new Rect(0, 0, spriteW, LyricRowHeight), rowAlpha);
                }
            }
        }

        if (_crtDispBitmap != null)
        {
            var (ox, oy) = GetOriginRelativeToRoot(sender, 14f, 34f);
            using var warped = SynthwaveEffects.WarpCrt(_lyricsCompositeTarget, _crtDispBitmap, ox, oy, CrtMaxShiftDip);
            args.DrawingSession.DrawImage(warped);
        }
        else
        {
            args.DrawingSession.DrawImage(_lyricsCompositeTarget);
        }
    }

    // ==================== Win2D Zone 3 CRT-Curved Spectrum + Global Tube Overlay ====================

    private void OnSpectrumWin2dCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        EnsureSpectrumResources(sender);
    }

    private void OnSpectrumWin2dSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is CanvasControl c)
        {
            EnsureSpectrumResources(c);
            c.Invalidate();
        }
    }

    private void OnSpectrumWin2dUnloaded(object sender, RoutedEventArgs e)
    {
        _specCompositeTarget?.Dispose();
        _specCompositeTarget = null;
        _specW = -1;
        _specH = -1;
        _specDpi = -1;
        try { SpectrumWin2dCanvas?.RemoveFromVisualTree(); } catch { }
    }

    private void EnsureSpectrumResources(CanvasControl canvas)
    {
        float w = (float)canvas.ActualWidth;
        float h = (float)canvas.ActualHeight;
        float dpi = canvas.Dpi;
        if (w < 16 || h < 16 || !canvas.ReadyToDraw) return;

        if (_specCompositeTarget != null &&
            Math.Abs(_specW - w) < 0.5f &&
            Math.Abs(_specH - h) < 0.5f &&
            Math.Abs(_specDpi - dpi) < 0.5f)
        {
            return;
        }

        _specCompositeTarget?.Dispose();
        _specW = w;
        _specH = h;
        _specDpi = dpi;
        _specCompositeTarget = new CanvasRenderTarget(canvas.Device, w, h, dpi);
    }

    private void OnSpectrumWin2dDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (!_bridge.CurrentState.Settings.Spectrum) return;
        EnsureSpectrumResources(sender);
        if (_specCompositeTarget == null) return;

        float w = _specW;
        float h = _specH;
        var levels = SpectrumBackdrop.CurrentLevels;
        var peaks = SpectrumBackdrop.CurrentPeaks;
        if (levels.Count == 0) return;

        // Desaturated vintage 1984 VFD phosphor colors with subtle tube wear
        Color cyan = Color.FromArgb(255, 90, 168, 160);
        Color amber = Color.FromArgb(255, 199, 142, 78);
        Color magenta = Color.FromArgb(255, 188, 91, 126);
        Color dim = Color.FromArgb(20, 125, 138, 153);
        Color bracket = Color.FromArgb(52, 90, 168, 160);

        using (var ds = _specCompositeTarget.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);

            float insetX = 3f;
            float usableW = Math.Max(4f, w - insetX * 2f);
            float colGap = 4.5f;
            float rowGap = 2.6f;
            float colW = Math.Max(2f, (usableW - colGap * (SpectrumCols - 1)) / SpectrumCols);
            float segH = Math.Max(1.6f, (h - rowGap * (SpectrumRows - 1)) / SpectrumRows);

            ds.FillRectangle(0f, 0f, 10f, 1f, bracket);
            ds.FillRectangle(0f, 0f, 1f, 6f, bracket);
            ds.FillRectangle(Math.Max(0f, w - 10f), 0f, 10f, 1f, bracket);
            ds.FillRectangle(Math.Max(0f, w - 1f), 0f, 1f, 6f, bracket);

            using var litCmd = new CanvasCommandList(sender);
            using (var lds = litCmd.CreateDrawingSession())
            {
                for (int c = 0; c < SpectrumCols && c < levels.Count; c++)
                {
                    float x = insetX + c * (colW + colGap);
                    int litRows = (int)Math.Round(levels[c] * SpectrumRows);
                    int peakRow = Math.Clamp((int)Math.Round(peaks[c] * SpectrumRows) - 1, 0, SpectrumRows - 1);

                    for (int r = 0; r < SpectrumRows; r++)
                    {
                        float y = Math.Max(0f, h - (r + 1) * segH - r * rowGap);
                        bool isLit = r < litRows;
                        bool isPeakCap = _synthPlaying && peaks[c] > 0.14 && r == peakRow;
                        double frac = (double)(r + 1) / SpectrumRows;

                        if (isLit || isPeakCap)
                        {
                            double wear = 0.78 + 0.045 * ((c * 5 + r * 9) % 5);
                            Color baseC = frac > 0.78 ? magenta : (frac > 0.45 ? amber : cyan);
                            byte a = (byte)Math.Clamp((isPeakCap && !isLit ? 215 : 185) * wear, 95, 225);
                            lds.FillRectangle(x, y, colW, segH, SynthwavePaletteHelper.WithAlpha(baseC, a));
                        }
                        else
                        {
                            ds.FillRectangle(x, y, colW, segH, dim);
                        }
                    }
                }
            }

            // Soft vintage phosphor filament bleed behind aged bricks + 3px aged mesh patina
            using (var phosphorBleed = SynthwaveEffects.Glow(litCmd, 1.5f))
            using (var bleedOpacity = new OpacityEffect { Source = phosphorBleed, Opacity = 0.22f })
            {
                ds.DrawImage(bleedOpacity);
            }
            ds.DrawImage(litCmd);

            Color meshDark = Color.FromArgb(44, 6, 8, 16);
            for (float my = 1.5f; my < h; my += 3.0f)
                ds.FillRectangle(0f, my, w, 0.9f, meshDark);
        }

        if (_crtDispBitmap != null)
        {
            var (ox, oy) = GetOriginRelativeToRoot(sender, 15f, 162f);
            using var warped = SynthwaveEffects.WarpCrt(_specCompositeTarget, _crtDispBitmap, ox, oy, CrtMaxShiftDip);
            args.DrawingSession.DrawImage(warped);
        }
        else
        {
            args.DrawingSession.DrawImage(_specCompositeTarget);
        }
    }

    private void OnCrtOverlayUnloaded(object sender, RoutedEventArgs e)
    {
        try { CrtTubeOverlayCanvas?.RemoveFromVisualTree(); } catch { }
    }

    private void OnCrtOverlayDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        float w = (float)sender.ActualWidth;
        float h = (float)sender.ActualHeight;
        if (w < 40 || h < 40) return;

        var ds = args.DrawingSession;
        var device = sender.Device;

        // 1. Curved CRT Convex Tube Edge Vignette (darkens corners cleanly like physical curved bezel glass)
        using (var vignette = new CanvasRadialGradientBrush(device,
        [
            new CanvasGradientStop { Position = 0.00f, Color = Colors.Transparent },
            new CanvasGradientStop { Position = 0.62f, Color = Colors.Transparent },
            new CanvasGradientStop { Position = 0.86f, Color = Color.FromArgb(72, 2, 4, 12) },
            new CanvasGradientStop { Position = 1.00f, Color = Color.FromArgb(172, 2, 3, 9) }
        ])
        {
            Center = new Vector2(w * 0.5f, h * 0.5f),
            RadiusX = w * 0.56f,
            RadiusY = h * 0.58f
        })
        {
            ds.FillRectangle(0, 0, w, h, vignette);
        }

        // 2. Curved CRT Glass Bezel Inner Frame (rounds corner silhouette gently)
        ds.DrawRoundedRectangle(0, 0, w, h, 24f, 24f, Color.FromArgb(145, 2, 4, 10), 8f);
        ds.DrawRoundedRectangle(1.5f, 1.5f, Math.Max(2, w - 3f), Math.Max(2, h - 3f), 14f, 14f, Color.FromArgb(32, 165, 243, 252), 1.0f);

        // 3. Domed Convex CRT Glass Specular Sheen (soft diagonal glass glare in upper-left quadrant)
        using (var glassGlare = new CanvasRadialGradientBrush(device,
        [
            new CanvasGradientStop { Position = 0.0f, Color = Color.FromArgb(16, 224, 242, 254) },
            new CanvasGradientStop { Position = 0.55f, Color = Color.FromArgb(6, 165, 243, 252) },
            new CanvasGradientStop { Position = 1.0f, Color = Colors.Transparent }
        ])
        {
            Center = new Vector2(w * 0.28f, h * 0.20f),
            RadiusX = w * 0.42f,
            RadiusY = h * 0.25f
        })
        {
            ds.FillEllipse(w * 0.28f, h * 0.20f, w * 0.42f, h * 0.25f, glassGlare);
        }

        // 4. Subtle global CRT horizontal scanlines over glass surface
        Color scanDark = Color.FromArgb(15, 0, 0, 0);
        for (float y = 1.5f; y < h; y += 3.0f)
            ds.FillRectangle(0, y, w, 1.0f, scanDark);
    }

    private void OnLyricsCanvasTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_lyricLines.Count == 0 || !(_bridge.CurrentState.Media?.Controls?.Seek ?? false)) return;
        float h = (float)(LyricsWin2dCanvas.ActualHeight > 20 ? LyricsWin2dCanvas.ActualHeight : 134);
        double y = e.GetPosition(LyricsWin2dCanvas).Y + _currentScrollY;
        float startY = ComputeLyricsBaseTop(h);
        int idx = (int)Math.Floor((y - startY) / LyricRowHeight);
        if (idx < 0 || idx >= _lyricLines.Count) return;

        var sec = _lyricLines[idx].Seconds;
        if (!sec.HasValue) return;
        _ = _bridge.SendMediaCommandAsync("media.seek", new { seconds = Math.Max(0, sec.Value - _bridge.CurrentState.Lyrics.OffsetSeconds) });
    }

    private void OnLyricsPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_lyricLines.Count <= 4) return;
        int delta = e.GetCurrentPoint(LyricsWin2dCanvas).Properties.MouseWheelDelta;
        float h = (float)(LyricsWin2dCanvas.ActualHeight > 20 ? LyricsWin2dCanvas.ActualHeight : 134);
        float maxScroll = Math.Max(0f, _lyricLines.Count * LyricRowHeight - h * 0.55f);
        _targetScrollY = Math.Clamp(_targetScrollY - Math.Sign(delta) * LyricRowHeight * 1.4, 0, maxScroll);
        _currentScrollY = _targetScrollY;
        _manualScrollUntilUtc = DateTime.UtcNow.AddSeconds(3.5);
        LyricsWin2dCanvas?.Invalidate();
    }

    // ==================== Animation Tick & Lyrics Timeline Sync ====================

    private void OnSynthTimerTick(object? sender, object e)
    {
        if (!IsWindowVisible || !_synthAnimations || !_synthPlaying)
        {
            _synthTimer.Stop();
            _synthClock.Stop();
            return;
        }

        double nowSec = _synthClock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(nowSec - _lastClockSec, 0.005, 0.08);
        _lastClockSec = nowSec;
        // Exact 60Hz * 0.024 = 1.44 units/sec wall-clock forward speed matching concept prototype
        _synthTick += dt * 1.44;

        if (DateTime.UtcNow >= _manualScrollUntilUtc && Math.Abs(_currentScrollY - _targetScrollY) > 0.4)
            _currentScrollY += (_targetScrollY - _currentScrollY) * 0.18;

        SynthwaveBgWin2dCanvas?.Invalidate();
        LyricsWin2dCanvas?.Invalidate();
    }

    private void RebuildLyricsIfNeeded(StateDto state)
    {
        var lyrics = state.Lyrics;
        double fontSize = Math.Clamp(Math.Round(state.Settings.LyricsFontSize * 0.62, 1), 12.5, 18.0);

        if (_renderedLyricsTrackKey == lyrics.TrackKey &&
            _renderedLineCount == lyrics.Lines.Count &&
            Math.Abs(_renderedFontSize - fontSize) < 0.1)
        {
            return;
        }

        _renderedLyricsTrackKey = lyrics.TrackKey;
        _renderedLineCount = lyrics.Lines.Count;
        _renderedFontSize = fontSize;
        _activeLineIndex = -1;
        _targetScrollY = 0;
        _currentScrollY = 0;
        _lyricLines.Clear();
        ClearLyricSpriteCache();

        if (lyrics.Lines.Count == 0)
        {
            LyricsEmptyTitle.Text = "暂无匹配歌词";
            LyricsEmptyDesc.Text = string.IsNullOrWhiteSpace(lyrics.Status)
                ? "右键浮窗可匹配歌词或导入本地 LRC 文本"
                : lyrics.Status;
            LyricsEmptyPanel.Visibility = Visibility.Visible;
            LyricsWin2dCanvas?.Invalidate();
            return;
        }

        LyricsEmptyPanel.Visibility = Visibility.Collapsed;
        _lyricLines.AddRange(lyrics.Lines);
        LyricsWin2dCanvas?.Invalidate();
    }

    private void SyncVfdProgressSegments(double pos, double dur)
    {
        _progPos = pos;
        _progDur = dur > 0 ? dur : 1.0;
        VfdProgressWin2dCanvas?.Invalidate();
    }

    private void OnVfdProgressUnloaded(object sender, RoutedEventArgs e)
    {
        try { VfdProgressWin2dCanvas?.RemoveFromVisualTree(); } catch { }
    }

    private void OnVfdProgressDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        float w = (float)sender.ActualWidth;
        float h = (float)sender.ActualHeight;
        if (w < 20 || h < 6) return;

        double frac = _progDur > 0 ? Math.Clamp(_progPos / _progDur, 0.0, 1.0) : 0.0;
        int lit = (int)Math.Round(frac * ProgressSegmentsCount);
        float gap = 2.0f;
        float segW = Math.Max(1.5f, (w - gap * (ProgressSegmentsCount - 1)) / ProgressSegmentsCount);
        float segH = 6.0f;
        float y = MathF.Round((h - segH) / 2f);

        Color headColor = Color.FromArgb(255, 255, 122, 0);
        Color amberColor = Color.FromArgb(224, 255, 183, 3);
        Color cyanColor = Color.FromArgb(224, 0, 245, 212);
        Color dimColor = Color.FromArgb(36, 148, 163, 184);

        var ds = args.DrawingSession;
        if (lit > 0 && lit <= ProgressSegmentsCount)
        {
            float hx = (lit - 1) * (segW + gap);
            using var headCmd = new CanvasCommandList(sender);
            using (var cds = headCmd.CreateDrawingSession())
            {
                cds.FillRoundedRectangle(hx, y, segW, segH, 1f, 1f, headColor);
            }
            using var headGlow = SynthwaveEffects.Glow(headCmd, 4.5f);
            ds.DrawImage(headGlow);
        }

        for (int i = 0; i < ProgressSegmentsCount; i++)
        {
            float sx = i * (segW + gap);
            Color sc = i < lit
                ? (i == lit - 1 ? headColor : (i > ProgressSegmentsCount * 0.66 ? amberColor : cyanColor))
                : dimColor;
            ds.FillRoundedRectangle(sx, y, segW, segH, 1f, 1f, sc);
        }
    }

    private void UpdateLiveTimelineAndLyrics()
    {
        var state = _bridge.CurrentState;
        var media = state.Media;
        if (media == null) return;

        double dur = media.Duration;
        double livePos = _bridge.GetInterpolatedMediaPosition();
        bool holdingOptimisticSeek = DateTime.UtcNow < _seekHoldUntilUtc;
        double pos = holdingOptimisticSeek
            ? Math.Clamp(_optimisticSeekPos, 0, Math.Max(0, dur > 0 ? dur : _optimisticSeekPos))
            : livePos;

        if (!_isScrubbing)
        {
            string ts = WeatherFormatting.FormatTimeSpan(pos);
            ElapsedText.Text = ts;
            ElapsedHeaderBadge.Text = $"CH-A · {ts}";
            SyncVfdProgressSegments(pos, dur);
        }
        DurationText.Text = dur > 0 ? WeatherFormatting.FormatTimeSpan(dur) : "--:--";

        if (_lyricLines.Count == 0) return;

        double lyricPos = pos + state.Lyrics.OffsetSeconds;
        int nextActive = -1;
        for (int i = 0; i < _lyricLines.Count; i++)
        {
            var s = _lyricLines[i].Seconds;
            if (s.HasValue && lyricPos >= s.Value)
                nextActive = i;
        }

        if (nextActive != _activeLineIndex)
        {
            _activeLineIndex = nextActive;
            if (DateTime.UtcNow >= _manualScrollUntilUtc)
                UpdateLyricsTargetScroll(snapImmediate: !state.Appearance.Animations || _currentScrollY == 0);
            LyricsWin2dCanvas?.Invalidate();
        }
    }

    // ==================== Context Menu & Controls ====================

    private void SyncContextMenuState()
    {
        PinMenuItem.Text = _config.PinLyricsFlyout ? "取消固定浮窗" : "固定浮窗（失焦不自动收起）";
        Win32WindowHelper.SetAlwaysOnTop(this, true);
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        _config.PinLyricsFlyout = !_config.PinLyricsFlyout;
        _config.Save();
        SyncContextMenuState();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _openSettingsAction();
    private void OnCloseClick(object sender, RoutedEventArgs e) => HidePopup();

    private void OnVfdProgressPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var media = _bridge.CurrentState.Media;
        if (media == null || media.Duration <= 0 || !(media.Controls?.Seek ?? false)) return;
        _isScrubbing = true;
        VfdProgressTray.CapturePointer(e.Pointer);
        ScrubToPointerX(e.GetCurrentPoint(VfdProgressTray).Position.X);
    }

    private void OnVfdProgressPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isScrubbing) return;
        ScrubToPointerX(e.GetCurrentPoint(VfdProgressTray).Position.X);
    }

    private async void OnVfdProgressPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isScrubbing) return;
        _isScrubbing = false;
        VfdProgressTray.ReleasePointerCapture(e.Pointer);
        double target = ScrubToPointerX(e.GetCurrentPoint(VfdProgressTray).Position.X);
        await CommitSeekAsync(target);
    }

    private void OnVfdProgressPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isScrubbing = false;
    }

    private double ScrubToPointerX(double x)
    {
        double w = Math.Max(1, VfdProgressTray.ActualWidth);
        double frac = Math.Clamp(x / w, 0.0, 1.0);
        double dur = _bridge.CurrentState.Media?.Duration ?? 0;
        double target = frac * Math.Max(0, dur);
        string ts = WeatherFormatting.FormatTimeSpan(target);
        ElapsedText.Text = ts;
        ElapsedHeaderBadge.Text = $"CH-A · {ts}";
        SyncVfdProgressSegments(target, dur > 0 ? dur : 1);
        return target;
    }

    private async System.Threading.Tasks.Task CommitSeekAsync(double targetSeconds)
    {
        var media = _bridge.CurrentState.Media;
        if (media == null) return;
        double dur = media.Duration;
        double clamped = Math.Clamp(targetSeconds, 0, Math.Max(0, dur));
        _optimisticSeekPos = clamped;
        _seekHoldUntilUtc = DateTime.UtcNow.AddMilliseconds(900);
        string ts = WeatherFormatting.FormatTimeSpan(clamped);
        ElapsedText.Text = ts;
        ElapsedHeaderBadge.Text = $"CH-A · {ts}";
        SyncVfdProgressSegments(clamped, dur);
        await _bridge.SendMediaCommandAsync("media.seek", new { seconds = clamped });
    }

    private async void OnPrevClick(object sender, RoutedEventArgs e) => await _bridge.SendMediaCommandAsync("media.previous");
    private async void OnToggleClick(object sender, RoutedEventArgs e) => await _bridge.SendMediaCommandAsync("media.toggle");
    private async void OnNextClick(object sender, RoutedEventArgs e) => await _bridge.SendMediaCommandAsync("media.next");

    private async void OnRefreshLyricsClick(object sender, RoutedEventArgs e) => await _bridge.RefreshLyricsAsync();

    private void OnOpenImportClick(object sender, RoutedEventArgs e)
    {
        ImportDrawer.Visibility = ImportDrawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCancelImportClick(object sender, RoutedEventArgs e) => ImportDrawer.Visibility = Visibility.Collapsed;

    private async void OnConfirmImportClick(object sender, RoutedEventArgs e)
    {
        var trackKey = _bridge.CurrentState.Media?.Key;
        if (string.IsNullOrEmpty(trackKey)) return;
        var (ok, msg) = await _bridge.ImportLyricsAsync(trackKey, LrcInputBox.Text);
        LyricsStatusText.Text = msg;
        if (ok) ImportDrawer.Visibility = Visibility.Collapsed;
    }

    private async void OnCoverGlowToggleClick(object sender, RoutedEventArgs e)
    {
        bool val = CoverGlowMenuItem.IsChecked;
        await _bridge.PatchSettingsAsync(new { coverGlow = val });
    }
}
