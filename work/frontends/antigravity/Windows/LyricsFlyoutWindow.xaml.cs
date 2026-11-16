using System;
using System.Collections.Generic;
using AntigravityFrontend.Sdk;
using AntigravityFrontend.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using Windows.UI;

namespace AntigravityFrontend.Windows;

public sealed partial class LyricsFlyoutWindow : Window
{
    private const string DefaultRetroMonoFont = "Lucida Console, Sarasa Term SC, Microsoft YaHei UI";
    private const int ProgressSegmentsCount = 32;
    private const int SpectrumCols = 14;
    private const int SpectrumRows = 8;

    private sealed class LyricRowVisual
    {
        public double? Seconds { get; init; }
        public required Grid RootGrid { get; init; }
        public required Ellipse Dot { get; init; }
        public required TextBlock ChromaCyan { get; init; }
        public required TextBlock ChromaMagenta { get; init; }
        public required TextBlock Label { get; init; }
        public required Grid VhsGlitchHost { get; init; }
        public required RectangleGeometry VhsBandClip { get; init; }
        public required TranslateTransform VhsBandShift { get; init; }
        public required Border CrtScanOverlay { get; init; }
    }

    private sealed class ReflectStripVisual
    {
        public required Border Strip { get; init; }
        public int RowIndex { get; init; }
        public double BaseWidth { get; init; }
        public double CenterX { get; init; }
        public double BaseAlpha { get; init; }
    }

    private readonly LumiBarBridgeService _bridge;
    private readonly PluginConfig _config;
    private readonly Action _openSettingsAction;

    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _synthTimer;
    private readonly List<LyricRowVisual> _lyricRows = new();
    private readonly List<Border> _progressSegs = new();
    private readonly List<Border> _synthScanlines = new();
    private readonly List<Ellipse> _synthStars = new();
    private readonly List<ReflectStripVisual> _synthReflectStrips = new();
    private Border? _synthSunGlow;

    private string _renderedSynthKey = "";
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

        BuildVfdProgressTrack();

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _tickTimer.Tick += (_, _) => UpdateLiveTimelineAndLyrics();

        _synthTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _synthTimer.Tick += OnSynthTimerTick;

        SyncContextMenuState();
        App.LogStartup("LyricsFlyoutWindow: done");
    }

    private void BuildVfdProgressTrack()
    {
        VfdProgressGrid.ColumnDefinitions.Clear();
        VfdProgressGrid.Children.Clear();
        _progressSegs.Clear();

        for (int i = 0; i < ProgressSegmentsCount; i++)
        {
            VfdProgressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var seg = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(1),
                Background = new SolidColorBrush(Color.FromArgb(36, 148, 163, 184))
            };
            Grid.SetColumn(seg, i);
            VfdProgressGrid.Children.Add(seg);
            _progressSegs.Add(seg);
        }
    }

    public void ShowAtAnchor(int leftPx, int topPx, int rightPx, int bottomPx)
    {
        Win32WindowHelper.PositionPopupAboveOrBelowAnchor(
            this, leftPx, topPx, rightPx, bottomPx,
            _config.LyricsFlyoutWidth, _config.LyricsFlyoutHeight, _config.PopupDirection);

        IsWindowVisible = true;
        ApplyState(_bridge.CurrentState, _bridge.CurrentArtwork);
        _tickTimer.Start();
        if (_synthAnimations && _synthPlaying)
            _synthTimer.Start();
        Activate();
        Win32WindowHelper.ApplyBorderlessDwmChrome(this);
    }

    public void HidePopup()
    {
        IsWindowVisible = false;
        _isScrubbing = false;
        _tickTimer.Stop();
        _synthTimer.Stop();
        Win32WindowHelper.GetAppWindow(this).Hide();
    }

    public void ApplyState(StateDto state, BitmapImage? artwork)
    {
        _isDark = Win32WindowHelper.ResolveIsDark(state.Appearance, state.Settings, isTaskbar: false);
        RootFrame.RequestedTheme = _isDark ? ElementTheme.Dark : ElementTheme.Light;
        string activeFont = !string.IsNullOrWhiteSpace(state.Appearance.FontFamily)
            ? state.Appearance.FontFamily
            : (!string.IsNullOrWhiteSpace(state.Settings.FontFamily) ? state.Settings.FontFamily : DefaultRetroMonoFont);
        Win32WindowHelper.ApplyFontFamily(FontHost, activeFont);
        if (_backdropConfig != null)
            _backdropConfig.Theme = _isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;

        var media = state.Media;
        _accent = WeatherFormatting.ParseHexColor(media?.Accent, Color.FromArgb(255, 255, 107, 0));
        var neon = SynthwavePaletteHelper.DeriveAlbumNeonPalette(_accent);
        Win32WindowHelper.UpdateAcrylicTint(
            _acrylicController, _isDark, state.Appearance.GlassOpacity,
            state.Appearance.CoverGlow ? neon.Primary : null);

        CoverGlowMenuItem.IsChecked = state.Settings.CoverGlow;
        SyncContextMenuState();
        ApplySurfaceBrushes(state, neon);

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
            DeckReflectionCanvas.Children.Clear();
            LyricsContainer.Children.Clear();
            _lyricRows.Clear();
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

        PlayPauseGlyph.Glyph = media.Playing ? "\uE769" : "\uE768";
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

        RebuildSynthwaveStageIfNeeded(state);
        UpdateDeckReflection();
        if (IsWindowVisible && _synthAnimations && _synthPlaying)
        {
            if (!_synthTimer.IsEnabled)
                _synthTimer.Start();
        }
        else
        {
            _synthTimer.Stop();
        }

        RebuildLyricsIfNeeded(state);
        UpdateLiveTimelineAndLyrics();
    }

    public void ApplySpectrum(SpectrumDto spectrum)
    {
        if (!IsWindowVisible) return;
        SpectrumBackdrop.UpdateBands(spectrum.Bands);
        UpdateDeckReflection();
    }

    private void ApplySurfaceBrushes(StateDto state, SynthwavePaletteHelper.NeonAlbumPalette neon)
    {
        _ = state;
        // 1:1 .grad-rim across RootFrame and ControlDeckCard
        var gradRim = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0),
            EndPoint = new global::Windows.Foundation.Point(1, 1)
        };
        gradRim.GradientStops.Add(new GradientStop { Color = Color.FromArgb(148, 0, 245, 212), Offset = 0.0 });
        gradRim.GradientStops.Add(new GradientStop { Color = Color.FromArgb(108, 139, 92, 246), Offset = 0.5 });
        gradRim.GradientStops.Add(new GradientStop { Color = Color.FromArgb(148, 255, 42, 133), Offset = 1.0 });

        RootFrame.Background = new SolidColorBrush(_isDark ? Color.FromArgb(245, 9, 8, 20) : Color.FromArgb(242, 246, 244, 255));
        RootFrame.BorderBrush = gradRim;

        SourceBadge.Background = new SolidColorBrush(_isDark ? Color.FromArgb(184, 16, 58, 70) : Color.FromArgb(42, 14, 116, 144));
        SourceBadge.BorderBrush = gradRim;
        SourceText.Foreground = new SolidColorBrush(_isDark ? Color.FromArgb(255, 103, 232, 249) : Color.FromArgb(255, 14, 116, 144));
        LyricsStatusText.Foreground = new SolidColorBrush(_isDark ? Color.FromArgb(204, 253, 230, 138) : Color.FromArgb(215, 180, 83, 9));
        ElapsedHeaderBadge.Foreground = new SolidColorBrush(_isDark ? Color.FromArgb(204, 103, 232, 249) : Color.FromArgb(215, 14, 116, 144));

        // Zone 4 Smoked-Acrylic Console matching rgba(11,15,28,0.80) -> rgba(9,10,22,0.90) -> rgba(7,8,18,0.96)
        var deckBg = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0.5, 0),
            EndPoint = new global::Windows.Foundation.Point(0.5, 1)
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
            StartPoint = new global::Windows.Foundation.Point(0.5, 0),
            EndPoint = new global::Windows.Foundation.Point(0.5, 1)
        };
        trayBg.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 5, 7, 14), Offset = 0.0 });
        trayBg.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 11, 16, 29), Offset = 1.0 });
        HardwareTray.Background = trayBg;
        HardwareTray.BorderBrush = new SolidColorBrush(Color.FromArgb(242, 15, 23, 42));
        VfdProgressTray.Background = trayBg;
        VfdProgressTray.BorderBrush = new SolidColorBrush(Color.FromArgb(242, 15, 23, 42));

        var coverBg = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0),
            EndPoint = new global::Windows.Foundation.Point(1, 1)
        };
        coverBg.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Primary, 210), Offset = 0.0 });
        coverBg.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Secondary, 210), Offset = 1.0 });
        DockCoverBorder.Background = coverBg;
        DockCoverBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255));

        TrackTitleText.Foreground = new SolidColorBrush(_isDark ? Color.FromArgb(255, 241, 245, 249) : Color.FromArgb(255, 24, 24, 48));
        TrackArtistText.Foreground = new SolidColorBrush(_isDark ? Color.FromArgb(204, 253, 230, 138) : Color.FromArgb(255, 124, 58, 237));
        ImportDrawer.Background = new SolidColorBrush(_isDark ? Color.FromArgb(244, 10, 12, 24) : Color.FromArgb(246, 246, 248, 255));
        ImportDrawer.BorderBrush = gradRim;

        // Exact 3 B1 Hardware Keys inside Zone 4 tray
        SynthwavePaletteHelper.ApplyB1KeyStyle(PrevBtn, isPrimary: false, isDark: _isDark);
        SynthwavePaletteHelper.ApplyB1KeyStyle(PlayPauseBtn, isPrimary: true, isDark: _isDark);
        SynthwavePaletteHelper.ApplyB1KeyStyle(NextBtn, isPrimary: false, isDark: _isDark);
    }

    private void OnSynthwaveBackdropSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SynthwaveBackdropHost.Clip = new RectangleGeometry
        {
            Rect = new global::Windows.Foundation.Rect(0, 0, Math.Max(0, e.NewSize.Width), Math.Max(0, e.NewSize.Height))
        };
        _renderedSynthKey = "";
        RebuildSynthwaveStageIfNeeded(_bridge.CurrentState);
        UpdateDeckReflection();
    }

    private void RebuildSynthwaveStageIfNeeded(StateDto state)
    {
        double w = SynthwaveBackdropHost.ActualWidth > 40 ? SynthwaveBackdropHost.ActualWidth : _config.LyricsFlyoutWidth;
        double h = SynthwaveBackdropHost.ActualHeight > 60 ? SynthwaveBackdropHost.ActualHeight : _config.LyricsFlyoutHeight;
        bool show = state.Appearance.CoverGlow;
        string key = $"{_isDark}|{show}|{Math.Round(w)}|{Math.Round(h)}|{_accent.R},{_accent.G},{_accent.B}";
        if (key == _renderedSynthKey) return;
        _renderedSynthKey = key;

        SynthwaveStageCanvas.Children.Clear();
        _synthScanlines.Clear();
        _synthStars.Clear();
        _synthReflectStrips.Clear();
        _synthSunGlow = null;
        if (!show) return;

        var neon = SynthwavePaletteHelper.DeriveAlbumNeonPalette(_accent);
        // 1:1 horizonY proportion from 174 / 376 = 0.4628
        double horizonY = Math.Round(h * 0.463);
        double sunCx = w * 0.5;
        double sunR = Math.Clamp(Math.Round(w * 0.128), 48, 58);
        double sunCy = horizonY - sunR * 0.34;

        // 1. Sky backdrop (0 .. horizonY)
        var skyRect = new Border
        {
            Width = w,
            Height = horizonY,
            Background = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0.5, 0),
                EndPoint = new global::Windows.Foundation.Point(0.5, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 8, 9, 23), Offset = 0.0 },
                    new GradientStop { Color = Color.FromArgb(255, 21, 16, 43), Offset = 0.65 },
                    new GradientStop { Color = Color.FromArgb(255, 35, 16, 51), Offset = 1.0 }
                }
            }
        };
        Canvas.SetLeft(skyRect, 0);
        Canvas.SetTop(skyRect, 0);
        SynthwaveStageCanvas.Children.Add(skyRect);

        // Ground backdrop (horizonY .. h)
        var groundRect = new Border
        {
            Width = w,
            Height = Math.Max(0, h - horizonY),
            Background = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0.5, 0),
                EndPoint = new global::Windows.Foundation.Point(0.5, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 18, 12, 36), Offset = 0.0 },
                    new GradientStop { Color = Color.FromArgb(255, 11, 13, 27), Offset = 0.35 },
                    new GradientStop { Color = Color.FromArgb(255, 7, 8, 18), Offset = 1.0 }
                }
            }
        };
        Canvas.SetLeft(groundRect, 0);
        Canvas.SetTop(groundRect, horizonY);
        SynthwaveStageCanvas.Children.Add(groundRect);

        // 2. Sky Canvas strictly clipped above horizonY
        var skyCanvas = new Canvas
        {
            Width = w,
            Height = horizonY,
            Clip = new RectangleGeometry { Rect = new global::Windows.Foundation.Rect(0, 0, Math.Max(0, w), Math.Max(0, horizonY)) }
        };

        // 16 cyber stars
        double spanW = Math.Max(40, w - 20);
        double spanSky = Math.Max(20, horizonY - 26);
        for (int i = 0; i < 16; i++)
        {
            double sx = ((i * 83 + 17) % spanW) + 10;
            double sy = ((i * 29 + 11) % spanSky) + 8;
            var star = new Ellipse
            {
                Width = 1.4,
                Height = 1.4,
                Fill = new SolidColorBrush(Color.FromArgb(255, 165, 243, 252)),
                Opacity = 0.28
            };
            Canvas.SetLeft(star, sx);
            Canvas.SetTop(star, sy);
            skyCanvas.Children.Add(star);
            _synthStars.Add(star);
        }

        // Album-sampled neon Outrun setting sun orb occluded behind horizonY
        var sunGrad = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0.5, 0),
            EndPoint = new global::Windows.Foundation.Point(0.5, 1)
        };
        sunGrad.GradientStops.Add(new GradientStop { Color = neon.Highlight, Offset = 0.0 });
        sunGrad.GradientStops.Add(new GradientStop { Color = neon.Primary, Offset = 0.68 });
        sunGrad.GradientStops.Add(new GradientStop { Color = neon.Secondary, Offset = 1.0 });

        var sunOrb = new Border
        {
            Width = sunR * 2,
            Height = sunR * 2,
            CornerRadius = new CornerRadius(sunR),
            Background = sunGrad,
            Opacity = 0.40
        };
        _synthSunGlow = sunOrb;
        Canvas.SetLeft(sunOrb, sunCx - sunR);
        Canvas.SetTop(sunOrb, sunCy - sunR);
        skyCanvas.Children.Add(sunOrb);

        // 3 dark scan-cutouts matching #1c102e at horizonY - 25 + s * 8
        var cutBrush = new SolidColorBrush(Color.FromArgb(255, 28, 16, 46));
        for (int s = 0; s < 3; s++)
        {
            var stripe = new Border
            {
                Width = sunR * 2 + 4,
                Height = 1.8 + s * 0.7,
                Background = cutBrush
            };
            Canvas.SetLeft(stripe, sunCx - sunR - 2);
            Canvas.SetTop(stripe, horizonY - 25 + s * 8);
            skyCanvas.Children.Add(stripe);
        }
        SynthwaveStageCanvas.Children.Add(skyCanvas);

        // 3. 20 shimmering album-neon sun reflection rows below horizonY
        double reflTopY = horizonY + 2;
        double reflDepth = Math.Max(40, h - horizonY - 12);
        for (int row = 0; row < 20; row++)
        {
            double frac = row / 20.0;
            double ry = reflTopY + frac * frac * reflDepth;
            double baseRw = (sunR * 1.42) * (1 - frac * 0.45);
            double baseAlpha = (1 - frac) * 0.24;
            Color stripColor = frac < 0.45 ? neon.Secondary : neon.Highlight;

            var strip = new Border
            {
                Width = baseRw,
                Height = 2.0 + frac * 1.6,
                Opacity = baseAlpha,
                Background = new LinearGradientBrush
                {
                    StartPoint = new global::Windows.Foundation.Point(0, 0.5),
                    EndPoint = new global::Windows.Foundation.Point(1, 0.5),
                    GradientStops =
                    {
                        new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(stripColor, 0), Offset = 0.0 },
                        new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(stripColor, 255), Offset = 0.5 },
                        new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(stripColor, 0), Offset = 1.0 }
                    }
                }
            };
            Canvas.SetLeft(strip, sunCx - baseRw / 2.0);
            Canvas.SetTop(strip, ry);
            SynthwaveStageCanvas.Children.Add(strip);
            _synthReflectStrips.Add(new ReflectStripVisual
            {
                Strip = strip,
                RowIndex = row,
                BaseWidth = baseRw,
                CenterX = sunCx,
                BaseAlpha = baseAlpha
            });
        }

        // 4. Perspective rays (r = -5..5, rgba(168, 85, 247, 0.20)) + 6 scrolling pink scanlines
        var rayBrush = new SolidColorBrush(Color.FromArgb(51, 168, 85, 247));
        for (int r = -5; r <= 5; r++)
        {
            SynthwaveStageCanvas.Children.Add(new Line
            {
                X1 = sunCx + r * 20,
                Y1 = horizonY,
                X2 = sunCx + r * 92,
                Y2 = h,
                Stroke = rayBrush,
                StrokeThickness = 1.0
            });
        }

        for (int s = 0; s < 6; s++)
        {
            var hl = new Border
            {
                Width = w,
                Height = 1.0,
                Background = new SolidColorBrush(Color.FromArgb(255, 255, 42, 133)),
                Opacity = 0.14
            };
            Canvas.SetLeft(hl, 0);
            Canvas.SetTop(hl, horizonY);
            SynthwaveStageCanvas.Children.Add(hl);
            _synthScanlines.Add(hl);
        }
        PositionSynthScanlines(horizonY, h);

        // 5. 1.5px Horizon laser seam: transparent -> cyan 0.48 -> #ff7a00 0.72 -> cyan 0.48 -> transparent
        var seam = new Border
        {
            Width = w,
            Height = 1.5,
            Background = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0, 0.5),
                EndPoint = new global::Windows.Foundation.Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop { Color = Colors.Transparent, Offset = 0.0 },
                    new GradientStop { Color = Color.FromArgb(122, 0, 245, 212), Offset = 0.25 },
                    new GradientStop { Color = Color.FromArgb(184, 255, 122, 0), Offset = 0.50 },
                    new GradientStop { Color = Color.FromArgb(122, 0, 245, 212), Offset = 0.75 },
                    new GradientStop { Color = Colors.Transparent, Offset = 1.0 }
                }
            }
        };
        Canvas.SetLeft(seam, 0);
        Canvas.SetTop(seam, horizonY - 0.8);
        SynthwaveStageCanvas.Children.Add(seam);
    }

    private void UpdateDeckReflection()
    {
        var state = _bridge.CurrentState;
        DeckReflectionCanvas.Children.Clear();
        if (!state.Settings.Spectrum) return;

        double cardW = ControlDeckCard.ActualWidth > 40 ? ControlDeckCard.ActualWidth : _config.LyricsFlyoutWidth - 24;
        double h = 56;
        DeckReflectionCanvas.Clip = new RectangleGeometry
        {
            Rect = new global::Windows.Foundation.Rect(0, 0, Math.Max(0, cardW), h)
        };

        var levels = SpectrumBackdrop.CurrentLevels;
        if (levels.Count == 0) return;

        double x0 = 6;
        double specW = Math.Max(40, cardW - 12);
        double colGap = 4.5;
        double colW = (specW - colGap * (SpectrumCols - 1)) / SpectrumCols;

        // Pass 1 + Pass 2 + Pass 3 (Fresnel decay): wide soft aura ellipses + mirrored bottom 1-3 VFD bricks
        for (int i = 0; i < SpectrumCols && i < levels.Count; i++)
        {
            double val = _synthPlaying ? Math.Clamp(levels[i], 0.0, 1.0) : 0.05;
            double cx = x0 + i * (colW + colGap) + colW / 2.0;
            double x = x0 + i * (colW + colGap);

            // Pass 1: Blurred wide radial glow
            Color glowCol = val > 0.72
                ? Color.FromArgb(255, 255, 165, 38)
                : Color.FromArgb(255, 0, 245, 212);
            byte glowA = (byte)Math.Clamp((val > 0.72 ? (0.20 + val * 0.32) : (0.16 + val * 0.34)) * 255, 12, 138);
            double ew = Math.Max(22, colW * 2.2);
            double eh = (18 + val * 16) * 2.1;

            var auraBrush = new RadialGradientBrush
            {
                Center = new global::Windows.Foundation.Point(0.5, 0.16),
                GradientOrigin = new global::Windows.Foundation.Point(0.5, 0.16),
                RadiusX = 0.5,
                RadiusY = 0.84
            };
            auraBrush.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(glowCol, glowA), Offset = 0.0 });
            auraBrush.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(glowCol, (byte)(glowA * 0.42)), Offset = 0.45 });
            auraBrush.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(glowCol, 0), Offset = 1.0 });

            var auraEllipse = new Ellipse { Width = ew, Height = eh, Fill = auraBrush };
            Canvas.SetLeft(auraEllipse, cx - ew / 2.0);
            Canvas.SetTop(auraEllipse, -eh * 0.14);
            DeckReflectionCanvas.Children.Add(auraEllipse);

            // Pass 2: Mirrored bottom 1-3 lit bricks with Fresnel vertical attenuation
            int litRows = (int)Math.Round(val * SpectrumRows);
            for (int r = 0; r < Math.Min(3, litRows); r++)
            {
                double mirrorY = 2 + r * 11;
                double fresnel = mirrorY <= 16 ? 0.80 : (mirrorY <= 38 ? 0.34 : 0.12);
                double fade = (_synthPlaying ? (r == 0 ? 0.42 : (r == 1 ? 0.22 : 0.10)) : 0.06) * fresnel;
                byte brickA = (byte)Math.Clamp(fade * 255, 0, 120);
                if (brickA < 4) continue;

                Color bCol = r < 2 ? Color.FromArgb(255, 0, 245, 212) : Color.FromArgb(255, 255, 183, 3);
                var brickBrush = new RadialGradientBrush
                {
                    Center = new global::Windows.Foundation.Point(0.5, 0.5),
                    GradientOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
                    RadiusX = 0.52,
                    RadiusY = 0.58
                };
                brickBrush.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(bCol, brickA), Offset = 0.0 });
                brickBrush.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(bCol, (byte)(brickA * 0.45)), Offset = 0.55 });
                brickBrush.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(bCol, 0), Offset = 1.0 });

                var brickRect = new Rectangle
                {
                    Width = colW + 4,
                    Height = 10,
                    RadiusX = 3,
                    RadiusY = 3,
                    Fill = brickBrush
                };
                Canvas.SetLeft(brickRect, x - 2);
                Canvas.SetTop(brickRect, mirrorY - 1.5);
                DeckReflectionCanvas.Children.Add(brickRect);
            }
        }

        // Pass 4: 1.2px dynamic specular rim highlight strip across top edge of console
        var rimGrad = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0.5),
            EndPoint = new global::Windows.Foundation.Point(1, 0.5)
        };
        for (int i = 0; i < SpectrumCols && i < levels.Count; i++)
        {
            double t = Math.Clamp((x0 + i * (colW + colGap) + colW / 2.0) / Math.Max(1, cardW), 0.02, 0.98);
            double val = _synthPlaying ? Math.Clamp(levels[i], 0.0, 1.0) : 0.04;
            byte a = (byte)Math.Clamp((0.12 + val * 0.55) * 255, 20, 192);
            Color rc = val > 0.78
                ? Color.FromArgb(a, 255, 198, 109)
                : Color.FromArgb(a, 103, 232, 249);
            rimGrad.GradientStops.Add(new GradientStop { Color = rc, Offset = t });
        }
        var rimStrip = new Rectangle
        {
            Width = Math.Max(0, cardW - 8),
            Height = 1.2,
            Fill = rimGrad
        };
        Canvas.SetLeft(rimStrip, 4);
        Canvas.SetTop(rimStrip, 0);
        DeckReflectionCanvas.Children.Add(rimStrip);
    }

    private void OnSynthTimerTick(object? sender, object e)
    {
        if (!IsWindowVisible || !_synthAnimations || !_synthPlaying)
        {
            _synthTimer.Stop();
            return;
        }

        _synthTick += 0.024;
        double h = SynthwaveBackdropHost.ActualHeight > 60 ? SynthwaveBackdropHost.ActualHeight : _config.LyricsFlyoutHeight;
        double horizonY = Math.Round(h * 0.463);
        PositionSynthScanlines(horizonY, h);

        for (int i = 0; i < _synthStars.Count; i++)
            _synthStars[i].Opacity = 0.20 + 0.22 * Math.Abs(Math.Sin(_synthTick * 0.7 + i));

        for (int i = 0; i < _synthReflectStrips.Count; i++)
        {
            var r = _synthReflectStrips[i];
            double rw = r.BaseWidth + Math.Sin(_synthTick * 2.0 + r.RowIndex * 0.9) * 4.0;
            r.Strip.Width = Math.Max(8, rw);
            r.Strip.Opacity = r.BaseAlpha * (0.8 + 0.2 * Math.Sin(_synthTick * 1.5 + r.RowIndex));
            Canvas.SetLeft(r.Strip, r.CenterX - rw / 2.0);
        }

        // C2 rolling 7px VHS horizontal slice tear on the active lyric row
        if (_activeLineIndex >= 0 && _activeLineIndex < _lyricRows.Count)
        {
            var row = _lyricRows[_activeLineIndex];
            double rowW = Math.Max(40, row.VhsGlitchHost.ActualWidth);
            double rowH = Math.Max(20, row.VhsGlitchHost.ActualHeight);
            double bandProgress = (_synthTick * 0.55) % 1.0;
            double bandH = 7.0;
            double bandTop = bandProgress * Math.Max(1.0, rowH - bandH);
            double glitchShift = Math.Sin(_synthTick * 17.0) > 0.78
                ? Math.Sin(_synthTick * 31.0) * 2.2
                : Math.Sin(_synthTick * 4.0) * 0.8;
            row.VhsBandClip.Rect = new global::Windows.Foundation.Rect(0, bandTop, rowW, bandH);
            row.VhsBandShift.X = glitchShift;
        }
    }

    private void PositionSynthScanlines(double horizonY, double bottomY)
    {
        if (_synthScanlines.Count == 0) return;
        double span = Math.Max(40, bottomY - horizonY);
        double scroll = (_synthTick * 0.14) % 1.0;
        int n = _synthScanlines.Count;
        for (int i = 0; i < n; i++)
        {
            double frac = ((double)i / n + scroll) % 1.0;
            double y = horizonY + frac * frac * span;
            _synthScanlines[i].Opacity = Math.Clamp(0.08 + frac * 0.15, 0.06, 0.25);
            Canvas.SetTop(_synthScanlines[i], y);
        }
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
        _lyricRows.Clear();
        LyricsContainer.Children.Clear();

        if (lyrics.Lines.Count == 0)
        {
            LyricsEmptyTitle.Text = "暂无匹配歌词";
            LyricsEmptyDesc.Text = string.IsNullOrWhiteSpace(lyrics.Status)
                ? "右键浮窗可匹配歌词或导入本地 LRC 文本"
                : lyrics.Status;
            LyricsEmptyPanel.Visibility = Visibility.Visible;
            return;
        }

        LyricsEmptyPanel.Visibility = Visibility.Collapsed;
        var cyanGhostBrush = new SolidColorBrush(Color.FromArgb(255, 0, 245, 212));
        var magentaGhostBrush = new SolidColorBrush(Color.FromArgb(255, 255, 42, 133));
        var crtBrush = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0),
            EndPoint = new global::Windows.Foundation.Point(0, 1)
        };
        for (int s = 0; s < 6; s++)
        {
            double o0 = s / 6.0;
            double o1 = (s + 0.5) / 6.0;
            crtBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 8, 9, 23), Offset = o0 });
            crtBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(88, 8, 9, 23), Offset = o1 });
        }

        foreach (var line in lyrics.Lines)
        {
            var rowGrid = new Grid { Padding = new Thickness(2, 2, 2, 2) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dot = new Ellipse
            {
                Width = 3.0,
                Height = 3.0,
                Margin = new Thickness(4, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(Color.FromArgb(72, 148, 163, 184))
            };
            Grid.SetColumn(dot, 0);
            rowGrid.Children.Add(dot);

            var textHost = new Grid();
            Grid.SetColumn(textHost, 1);

            var chromaCyan = new TextBlock
            {
                Text = line.Text,
                FontSize = fontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = fontSize * 1.32,
                CharacterSpacing = 24,
                Foreground = cyanGhostBrush,
                Opacity = 0.0,
                RenderTransform = new TranslateTransform { X = -1.4 },
                IsHitTestVisible = false
            };

            var chromaMagenta = new TextBlock
            {
                Text = line.Text,
                FontSize = fontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = fontSize * 1.32,
                CharacterSpacing = 24,
                Foreground = magentaGhostBrush,
                Opacity = 0.0,
                RenderTransform = new TranslateTransform { X = 1.4 },
                IsHitTestVisible = false
            };

            var tb = new TextBlock
            {
                Text = line.Text,
                FontSize = fontSize,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.42,
                LineHeight = fontSize * 1.32,
                CharacterSpacing = 20
            };

            // Rolling 7px VHS slice overlay (clipped to 7px band when active & playing)
            var vhsBandClip = new RectangleGeometry { Rect = new global::Windows.Foundation.Rect(0, 0, 0, 0) };
            var vhsBandShift = new TranslateTransform();
            var vhsGlitchHost = new Grid
            {
                Clip = vhsBandClip,
                RenderTransform = vhsBandShift,
                Opacity = 0.0,
                IsHitTestVisible = false,
                Background = new SolidColorBrush(Color.FromArgb(200, 16, 12, 34))
            };
            vhsGlitchHost.Children.Add(new TextBlock
            {
                Text = line.Text,
                FontSize = fontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = fontSize * 1.32,
                CharacterSpacing = 24,
                Foreground = new SolidColorBrush(Color.FromArgb(245, 103, 232, 249))
            });

            var crtOverlay = new Border
            {
                Background = crtBrush,
                Opacity = 0.0,
                IsHitTestVisible = false
            };

            textHost.Children.Add(chromaCyan);
            textHost.Children.Add(chromaMagenta);
            textHost.Children.Add(tb);
            textHost.Children.Add(vhsGlitchHost);
            textHost.Children.Add(crtOverlay);
            rowGrid.Children.Add(textHost);

            if (line.Seconds.HasValue && (state.Media?.Controls?.Seek ?? false))
            {
                double sec = line.Seconds.Value;
                rowGrid.Tapped += async (_, _) =>
                {
                    await _bridge.SendMediaCommandAsync("media.seek", new { seconds = Math.Max(0, sec - lyrics.OffsetSeconds) });
                };
            }

            _lyricRows.Add(new LyricRowVisual
            {
                Seconds = line.Seconds,
                RootGrid = rowGrid,
                Dot = dot,
                ChromaCyan = chromaCyan,
                ChromaMagenta = chromaMagenta,
                Label = tb,
                VhsGlitchHost = vhsGlitchHost,
                VhsBandClip = vhsBandClip,
                VhsBandShift = vhsBandShift,
                CrtScanOverlay = crtOverlay
            });
            LyricsContainer.Children.Add(rowGrid);
        }
    }

    private void SyncVfdProgressSegments(double pos, double dur)
    {
        double frac = dur > 0 ? Math.Clamp(pos / dur, 0.0, 1.0) : 0.0;
        int lit = (int)Math.Round(frac * ProgressSegmentsCount);
        var headBrush = new SolidColorBrush(Color.FromArgb(255, 255, 122, 0));
        var amberBrush = new SolidColorBrush(Color.FromArgb(224, 255, 183, 3));
        var cyanBrush = new SolidColorBrush(Color.FromArgb(224, 0, 245, 212));
        var dimBrush = new SolidColorBrush(Color.FromArgb(36, 148, 163, 184));

        for (int i = 0; i < _progressSegs.Count; i++)
        {
            if (i < lit)
            {
                bool isHead = i == lit - 1;
                _progressSegs[i].Background = isHead ? headBrush : (i > ProgressSegmentsCount * 0.66 ? amberBrush : cyanBrush);
            }
            else
            {
                _progressSegs[i].Background = dimBrush;
            }
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

        if (_lyricRows.Count == 0) return;

        double lyricPos = pos + state.Lyrics.OffsetSeconds;
        int nextActive = -1;
        for (int i = 0; i < _lyricRows.Count; i++)
        {
            var s = _lyricRows[i].Seconds;
            if (s.HasValue && lyricPos >= s.Value)
                nextActive = i;
        }

        if (nextActive != _activeLineIndex)
        {
            _activeLineIndex = nextActive;
            var activeGrad = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0, 0.5),
                EndPoint = new global::Windows.Foundation.Point(1, 0.5)
            };
            if (_isDark)
            {
                activeGrad.GradientStops.Add(new GradientStop { Color = Colors.White, Offset = 0.0 });
                activeGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 103, 232, 249), Offset = 0.52 });
                activeGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 253, 230, 138), Offset = 1.0 });
            }
            else
            {
                activeGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 190, 24, 93), Offset = 0.0 });
                activeGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 124, 58, 237), Offset = 1.0 });
            }

            var dimTextBrush = new SolidColorBrush(_isDark ? Color.FromArgb(255, 203, 213, 225) : Color.FromArgb(255, 51, 65, 85));
            var orangeDotBrush = new SolidColorBrush(Color.FromArgb(255, 255, 122, 0));
            var dimDotBrush = new SolidColorBrush(Color.FromArgb(72, 148, 163, 184));

            for (int i = 0; i < _lyricRows.Count; i++)
            {
                bool active = i == _activeLineIndex;
                int dist = _activeLineIndex >= 0 ? Math.Abs(i - _activeLineIndex) : 2;
                var row = _lyricRows[i];

                row.Label.Opacity = active ? 1.0 : (dist == 1 ? 0.42 : 0.22);
                row.Label.FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
                row.Label.Foreground = active ? activeGrad : dimTextBrush;

                row.Dot.Width = active ? 7.2 : 3.0;
                row.Dot.Height = active ? 7.2 : 3.0;
                row.Dot.Margin = active ? new Thickness(2, 0, 8, 0) : new Thickness(4, 0, 10, 0);
                row.Dot.Fill = active ? orangeDotBrush : dimDotBrush;

                row.ChromaCyan.Opacity = active && _isDark ? 0.52 : 0.0;
                row.ChromaMagenta.Opacity = active && _isDark ? 0.52 : 0.0;
                row.CrtScanOverlay.Opacity = active && _isDark ? 0.30 : 0.0;
                row.VhsGlitchHost.Opacity = active && _synthPlaying && _isDark ? 0.88 : 0.0;
                if (!active)
                {
                    row.VhsBandClip.Rect = new global::Windows.Foundation.Rect(0, 0, 0, 0);
                    row.VhsBandShift.X = 0;
                }
            }

            if (_activeLineIndex >= 0 && _activeLineIndex < _lyricRows.Count)
            {
                var target = _lyricRows[_activeLineIndex].RootGrid;
                target.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = state.Appearance.Animations,
                    VerticalAlignmentRatio = 0.46
                });
            }
        }
    }

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
