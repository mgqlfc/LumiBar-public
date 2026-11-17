using System;
using System.Numerics;
using AntigravityFrontend.Rendering;
using AntigravityFrontend.Sdk;
using AntigravityFrontend.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace AntigravityFrontend.Controls;

public sealed partial class MusicCapsuleControl : UserControl
{
    private const string DefaultRetroMonoFont = "Lucida Console, Sarasa Term SC, Microsoft YaHei UI";

    private bool _isHovering;
    private bool _isLite;
    private bool _isDark = true;
    private bool _animationsEnabled = true;
    private bool _g3Enabled = true;
    private double _standardWidth = 248;
    private double? _slotWidthOverride;
    private double? _slotHeightOverride;
    private Color _accent = Color.FromArgb(255, 255, 107, 0);
    private string? _secondaryAccentHex;
    private MediaDto? _currentMedia;
    private readonly DispatcherTimer _liteHoverTimer;
    private readonly DispatcherTimer _synthPulseTimer;
    private double _synthPhase;
    private double _latestSpectrumEnergy = 0.35;

    // Win2D offscreen resources cached between frames for G3 glow + CRT scanlines + pill mask
    private CanvasRenderTarget? _g3RingTarget;
    private CanvasRenderTarget? _g3PillMask;
    private CanvasRenderTarget? _g3ScanMask;
    private float _g3W = -1;
    private float _g3H = -1;
    private float _g3Dpi = -1;

    public event Action? CapsuleClicked;
    public event Action? PreviousRequested;
    public event Action? ToggleRequested;
    public event Action? NextRequested;

    public MusicCapsuleControl()
    {
        InitializeComponent();
        _liteHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _liteHoverTimer.Tick += (_, _) =>
        {
            _liteHoverTimer.Stop();
            if (_isHovering && _isLite && _currentMedia != null && Visibility == Visibility.Visible)
                LiteHoverPopup.IsOpen = true;
        };

        _synthPulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _synthPulseTimer.Tick += (_, _) =>
        {
            if (Visibility != Visibility.Visible || !_animationsEnabled || _currentMedia?.Playing != true)
            {
                _synthPulseTimer.Stop();
                G3Win2dCanvas?.Invalidate();
                return;
            }
            _synthPhase += 0.14;
            G3Win2dCanvas?.Invalidate();
        };
    }

    public void ApplySlotDimensions(double? widthDip, double? heightDip)
    {
        _slotWidthOverride = widthDip;
        _slotHeightOverride = heightDip;
        if (heightDip.HasValue && heightDip.Value >= 24)
        {
            double h = heightDip.Value;
            OuterShell.Height = h;
            OuterShell.CornerRadius = new CornerRadius(h / 2.0);
            ApplyHeightResponsiveMetrics(h);
        }
        if (widthDip.HasValue && widthDip.Value >= 24 && Visibility == Visibility.Visible)
            OuterShell.Width = widthDip.Value;
    }

    private void ApplyHeightResponsiveMetrics(double h)
    {
        double radius = h / 2.0;
        RevealOverlay.CornerRadius = new CornerRadius(radius);

        if (h >= 36)
        {
            StandardLayout.Padding = new Thickness(7, 2, 7, 2);
            ArtFrame.Width = 26;
            ArtFrame.Height = 26;
            ArtFrame.CornerRadius = new CornerRadius(13);
            TitleText.FontSize = 11.5;
            TitleText.LineHeight = 13.5;
            ArtistText.FontSize = 10;
            ArtistText.LineHeight = 12;
            RightSlotHost.Width = 68;
            RightSlotHost.Height = 24;
            PlayPauseBtn.Width = 20;
            PlayPauseBtn.Height = 18;
            PrevBtn.Width = 18;
            PrevBtn.Height = 18;
            NextBtn.Width = 18;
            NextBtn.Height = 18;
            CenterTextVeil.Margin = new Thickness(36, 5, 110, 5);
            LiteLayout.Padding = new Thickness(11, 8, 11, 8);
        }
        else if (h >= 32)
        {
            StandardLayout.Padding = new Thickness(6, 1, 6, 1);
            ArtFrame.Width = 24;
            ArtFrame.Height = 24;
            ArtFrame.CornerRadius = new CornerRadius(12);
            TitleText.FontSize = 11.0;
            TitleText.LineHeight = 12.5;
            ArtistText.FontSize = 9.5;
            ArtistText.LineHeight = 11;
            RightSlotHost.Width = 62;
            RightSlotHost.Height = 22;
            PlayPauseBtn.Width = 19;
            PlayPauseBtn.Height = 17;
            PrevBtn.Width = 17;
            PrevBtn.Height = 17;
            NextBtn.Width = 17;
            NextBtn.Height = 17;
            CenterTextVeil.Margin = new Thickness(32, 3, 98, 3);
            LiteLayout.Padding = new Thickness(9, 6, 9, 6);
        }
        else
        {
            StandardLayout.Padding = new Thickness(5, 0, 5, 0);
            double art = Math.Clamp(h - 6, 18, 22);
            ArtFrame.Width = art;
            ArtFrame.Height = art;
            ArtFrame.CornerRadius = new CornerRadius(art / 2.0);
            TitleText.FontSize = 10.5;
            TitleText.LineHeight = 12;
            ArtistText.FontSize = 9.0;
            ArtistText.LineHeight = 10.5;
            RightSlotHost.Width = 58;
            RightSlotHost.Height = Math.Max(16, h - 8);
            double btnH = Math.Clamp(h - 10, 14, 17);
            PlayPauseBtn.Width = 18;
            PlayPauseBtn.Height = btnH;
            PrevBtn.Width = 16;
            PrevBtn.Height = btnH;
            NextBtn.Width = 16;
            NextBtn.Height = btnH;
            CenterTextVeil.Margin = new Thickness(art + 6, 2, 74, 2);
            LiteLayout.Padding = new Thickness(7, 5, 7, 5);
        }

        G3Win2dCanvas?.Invalidate();
    }

    private void OnG3CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        EnsureG3Resources(sender);
    }

    private void OnG3SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is CanvasControl canvas)
        {
            EnsureG3Resources(canvas);
            canvas.Invalidate();
        }
    }

    private void OnG3Unloaded(object sender, RoutedEventArgs e)
    {
        DisposeG3Resources();
        try { G3Win2dCanvas?.RemoveFromVisualTree(); } catch { }
    }

    private void DisposeG3Resources()
    {
        _g3RingTarget?.Dispose();
        _g3RingTarget = null;
        _g3PillMask?.Dispose();
        _g3PillMask = null;
        _g3ScanMask?.Dispose();
        _g3ScanMask = null;
        _g3W = -1;
        _g3H = -1;
        _g3Dpi = -1;
    }

    private void EnsureG3Resources(CanvasControl canvas)
    {
        float w = (float)canvas.ActualWidth;
        float h = (float)canvas.ActualHeight;
        float dpi = canvas.Dpi;
        if (w < 8 || h < 8 || !canvas.ReadyToDraw) return;

        if (_g3RingTarget != null &&
            Math.Abs(_g3W - w) < 0.5f &&
            Math.Abs(_g3H - h) < 0.5f &&
            Math.Abs(_g3Dpi - dpi) < 0.5f)
        {
            return;
        }

        DisposeG3Resources();
        var device = canvas.Device;
        _g3W = w;
        _g3H = h;
        _g3Dpi = dpi;

        _g3RingTarget = new CanvasRenderTarget(device, w, h, dpi);
        _g3PillMask = new CanvasRenderTarget(device, w, h, dpi);
        using (var ds = _g3PillMask.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            float radius = h / 2f;
            ds.FillRoundedRectangle(0, 0, w, h, radius, radius, Colors.White);
        }
        _g3ScanMask = SynthwaveEffects.ScanlineMask(device, w, h, dpi);
    }

    private void OnG3Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (!_g3Enabled || Visibility != Visibility.Visible) return;
        EnsureG3Resources(sender);
        if (_g3RingTarget == null || _g3PillMask == null || _g3ScanMask == null) return;

        float w = _g3W;
        float h = _g3H;
        float radius = h / 2f;
        var neon = SynthwavePaletteHelper.DeriveAlbumNeonPalette(_accent, _secondaryAccentHex);
        bool playing = _currentMedia?.Playing == true;

        double wavePulse = playing && _animationsEnabled
            ? 0.08 * Math.Sin(_synthPhase)
            : 0.0;
        double energy = playing ? Math.Clamp(_latestSpectrumEnergy + wavePulse, 0.12, 1.0) : 0.16;
        double pulse = 0.44 + energy * 0.56;

        byte aMain = (byte)Math.Clamp(0.80 * pulse * 255, 42, 215);
        byte aMid = (byte)Math.Clamp(0.68 * pulse * 255, 36, 195);
        Color cA = SynthwavePaletteHelper.WithAlpha(neon.Primary, aMain);
        Color cB = SynthwavePaletteHelper.WithAlpha(neon.Secondary, aMid);

        using (var ds = _g3RingTarget.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            using var ringGrad = new CanvasLinearGradientBrush(sender.Device,
            [
                new CanvasGradientStop { Position = 0.0f, Color = cA },
                new CanvasGradientStop { Position = 0.48f, Color = cB },
                new CanvasGradientStop { Position = 1.0f, Color = cA }
            ])
            {
                StartPoint = Vector2.Zero,
                EndPoint = new Vector2(w, 0)
            };

            // Concept-matched G3 soft feathered breathing perimeter stroke
            float strokeWidth = _isLite ? 6.4f : 7.4f;
            ds.DrawRoundedRectangle(0.8f, 0.8f, Math.Max(2, w - 1.6f), Math.Max(2, h - 1.6f),
                Math.Max(2, radius - 0.8f), Math.Max(2, radius - 0.8f), ringGrad, strokeWidth);
        }

        using var haloBlur = SynthwaveEffects.Glow(_g3RingTarget, 3.8f);
        using var scanOpacity = new OpacityEffect { Source = _g3ScanMask, Opacity = _isDark ? 0.30f : 0.16f };
        using var scanPunched = SynthwaveEffects.PunchOut(haloBlur, scanOpacity);
        using var clipped = SynthwaveEffects.Mask(scanPunched, _g3PillMask);
        args.DrawingSession.DrawImage(clipped);
    }

    public void CloseHoverPopup()
    {
        _liteHoverTimer.Stop();
        LiteHoverPopup.IsOpen = false;
    }

    public void UpdateState(StateDto state, BitmapImage? artwork, bool isLite)
    {
        _isLite = isLite;
        _isDark = Win32WindowHelper.ResolveIsDark(state.Appearance, state.Settings, isTaskbar: true);
        _animationsEnabled = state.Settings.Animations;
        _g3Enabled = state.Appearance.CoverGlow;
        _standardWidth = Math.Clamp(state.Settings.MusicWidth, 180, 340);

        var activeFont = !string.IsNullOrWhiteSpace(state.Appearance.FontFamily)
            ? state.Appearance.FontFamily
            : (!string.IsNullOrWhiteSpace(state.Settings.FontFamily) ? state.Settings.FontFamily : DefaultRetroMonoFont);
        Win32WindowHelper.ApplyFontFamily(this, activeFont);
        Win32WindowHelper.ApplyFontFamily(LiteHoverFontHost, activeFont);

        var media = state.Media;
        _currentMedia = media;

        if (!state.Settings.ShowMusic || media == null)
        {
            LiteHoverPopup.IsOpen = false;
            _liteHoverTimer.Stop();
            _synthPulseTimer.Stop();
            OuterShell.Opacity = 0;
            OuterShell.IsHitTestVisible = false;
            OuterShell.Width = 0;
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        if (!isLite)
        {
            LiteHoverPopup.IsOpen = false;
            _liteHoverTimer.Stop();
        }
        OuterShell.Opacity = 1;
        OuterShell.IsHitTestVisible = true;
        OuterShell.Width = _slotWidthOverride is > 0 ? _slotWidthOverride.Value : (isLite ? 72 : _standardWidth);
        if (_slotHeightOverride is >= 24)
        {
            OuterShell.Height = _slotHeightOverride.Value;
            OuterShell.CornerRadius = new CornerRadius(_slotHeightOverride.Value / 2.0);
            ApplyHeightResponsiveMetrics(_slotHeightOverride.Value);
        }

        StandardLayout.Visibility = isLite ? Visibility.Collapsed : Visibility.Visible;
        LiteLayout.Visibility = isLite ? Visibility.Visible : Visibility.Collapsed;
        CenterTextVeil.Visibility = isLite ? Visibility.Collapsed : Visibility.Visible;

        _accent = WeatherFormatting.ParseHexColor(media.Accent, Color.FromArgb(255, 255, 107, 0));
        _secondaryAccentHex = media.SecondaryAccent;
        var neon = SynthwavePaletteHelper.DeriveAlbumNeonPalette(_accent, _secondaryAccentHex);

        TitleText.Text = string.IsNullOrWhiteSpace(media.Title) ? "未知曲目" : media.Title;
        ArtistText.Text = string.IsNullOrWhiteSpace(media.Artist) ? "未知歌手" : media.Artist;
        LiteHoverTitleText.Text = TitleText.Text;
        LiteHoverArtistText.Text = ArtistText.Text;
        LiteHoverStatusText.Text = media.Playing ? "PLAYING" : "PAUSED";
        LiteHoverStatusBadge.Background = new SolidColorBrush(media.Playing
            ? SynthwavePaletteHelper.WithAlpha(neon.Primary, 46)
            : Color.FromArgb(35, 148, 163, 184));
        LiteHoverStatusText.Foreground = new SolidColorBrush(media.Playing
            ? neon.Primary
            : Color.FromArgb(220, 148, 163, 184));
        ToolTipService.SetToolTip(OuterShell, isLite ? null : $"{TitleText.Text} · {ArtistText.Text}" + (media.Playing ? "" : "（已暂停）"));

        PauseIconPath.Visibility = media.Playing ? Visibility.Visible : Visibility.Collapsed;
        PlayIconPath.Visibility = media.Playing ? Visibility.Collapsed : Visibility.Visible;

        bool canToggle = media.Controls?.Toggle ?? true;
        bool canPrev = media.Controls?.Previous ?? true;
        bool canNext = media.Controls?.Next ?? true;
        PlayPauseBtn.IsEnabled = canToggle;
        PrevBtn.IsEnabled = canPrev;
        NextBtn.IsEnabled = canNext;

        if (artwork != null)
        {
            ArtImage.Source = artwork;
            ArtImage.Visibility = Visibility.Visible;
            ArtFallbackGlyph.Visibility = Visibility.Collapsed;
            LiteHoverArtImage.Source = artwork;
            LiteHoverArtImage.Visibility = Visibility.Visible;
            LiteHoverArtFallback.Visibility = Visibility.Collapsed;
        }
        else
        {
            ArtImage.Visibility = Visibility.Collapsed;
            ArtFallbackGlyph.Visibility = Visibility.Visible;
            LiteHoverArtImage.Visibility = Visibility.Collapsed;
            LiteHoverArtFallback.Visibility = Visibility.Visible;
        }

        ApplyGlassStyling(state, neon);

        SpectrumBackdrop.Configure(
            neon.Primary,
            media.Playing,
            state.Settings.Spectrum && !isLite,
            state.Settings.Sensitivity,
            0.94,
            compactMode: true);
        LiteSpectrumBackdrop.Configure(
            neon.Primary,
            media.Playing,
            state.Settings.Spectrum && isLite,
            state.Settings.Sensitivity,
            0.98,
            compactMode: true);

        if (media.Playing && _animationsEnabled && Visibility == Visibility.Visible)
        {
            if (!_synthPulseTimer.IsEnabled)
                _synthPulseTimer.Start();
        }
        else
        {
            _synthPulseTimer.Stop();
            _latestSpectrumEnergy = 0.16;
        }

        G3Win2dCanvas?.Invalidate();
        UpdateHoverState();
    }

    public void UpdateSpectrum(SpectrumDto spectrum)
    {
        if (Visibility != Visibility.Visible) return;
        if (_isLite)
            LiteSpectrumBackdrop.UpdateBands(spectrum.Bands);
        else
            SpectrumBackdrop.UpdateBands(spectrum.Bands);

        if (spectrum.Bands != null && spectrum.Bands.Length > 0)
        {
            double sum = 0;
            double max = 0;
            for (int i = 0; i < spectrum.Bands.Length; i++)
            {
                double v = spectrum.Bands[i];
                sum += v;
                if (v > max) max = v;
            }
            double targetEnergy = Math.Clamp((sum / spectrum.Bands.Length) * 0.55 + max * 0.55, 0.08, 1.0);
            double blend = targetEnergy > _latestSpectrumEnergy ? 0.62 : 0.30;
            _latestSpectrumEnergy += (targetEnergy - _latestSpectrumEnergy) * blend;
        }
        else
        {
            _latestSpectrumEnergy = 0.16;
        }

        G3Win2dCanvas?.Invalidate();
    }

    private void ApplyGlassStyling(StateDto state, SynthwavePaletteHelper.NeonAlbumPalette neon)
    {
        // High-contrast smoked obsidian pill base (#070A14)
        byte glassAlpha = (byte)Math.Clamp(state.Appearance.GlassOpacity * 245, 198, 246);
        OuterShell.Background = new SolidColorBrush(_isDark
            ? Color.FromArgb(glassAlpha, 7, 10, 20)
            : Color.FromArgb(Math.Min((byte)245, glassAlpha), 246, 242, 255));

        OuterShell.BorderBrush = new SolidColorBrush(Colors.Transparent);

        var titleFg = new SolidColorBrush(_isDark ? Color.FromArgb(255, 248, 250, 252) : Color.FromArgb(255, 24, 16, 48));
        var artistFg = new SolidColorBrush(_isDark ? neon.Primary : Color.FromArgb(255, 91, 33, 182));
        TitleText.Foreground = titleFg;
        ArtistText.Foreground = artistFg;

        // Dark readable center lane mask matching rgba(7,10,20,0.68) in prototype
        var veilBrush = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0.5, 0),
            EndPoint = new global::Windows.Foundation.Point(0.5, 1)
        };
        if (_isDark)
        {
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 7, 10, 20), Offset = 0.0 });
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(174, 7, 10, 20), Offset = 0.28 });
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(174, 7, 10, 20), Offset = 0.72 });
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 7, 10, 20), Offset = 1.0 });
        }
        else
        {
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 246, 242, 255), Offset = 0.0 });
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(170, 246, 242, 255), Offset = 0.5 });
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 246, 242, 255), Offset = 1.0 });
        }
        CenterTextVeil.Background = veilBrush;

        // Cover Frame & B1 Hardware Hover Keys
        var artBorder = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0),
            EndPoint = new global::Windows.Foundation.Point(1, 1)
        };
        artBorder.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Primary, 210), Offset = 0.0 });
        artBorder.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Secondary, 210), Offset = 1.0 });
        ArtFrame.BorderBrush = artBorder;

        var artBg = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0),
            EndPoint = new global::Windows.Foundation.Point(1, 1)
        };
        artBg.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Primary, 160), Offset = 0.0 });
        artBg.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Secondary, 160), Offset = 1.0 });
        ArtFrame.Background = artBg;

        SynthwavePaletteHelper.ApplyB1KeyStyle(PrevBtn, isPrimary: false, _isDark);
        SynthwavePaletteHelper.ApplyB1KeyStyle(PlayPauseBtn, isPrimary: true, _isDark);
        SynthwavePaletteHelper.ApplyB1KeyStyle(NextBtn, isPrimary: false, _isDark);

        LiteHoverBorder.Background = new SolidColorBrush(_isDark
            ? Color.FromArgb(242, 10, 12, 24)
            : Color.FromArgb(245, 246, 242, 255));
        LiteHoverBorder.BorderBrush = new SolidColorBrush(_isDark
            ? Color.FromArgb(32, 255, 255, 255)
            : Color.FromArgb(36, 15, 23, 42));
        LiteHoverArtFrame.BorderBrush = artBorder;
        LiteHoverArtFrame.Background = artBg;
        LiteHoverTitleText.Foreground = titleFg;
        LiteHoverArtistText.Foreground = artistFg;
    }

    private void UpdateHoverState()
    {
        bool showInline = _isHovering && !_isLite && _currentMedia != null;
        HoverControlsPanel.Opacity = showInline ? 1 : 0;
        HoverControlsPanel.IsHitTestVisible = showInline;
        SpectrumBackdrop.Opacity = showInline ? 0 : (_currentMedia != null && !_isLite ? 0.94 : 0);
        TextInfoPanel.Opacity = 1.0;
        RevealOverlay.Opacity = _isHovering ? 1 : 0;

        if (!_isHovering || !_isLite || _currentMedia == null)
        {
            _liteHoverTimer.Stop();
            LiteHoverPopup.IsOpen = false;
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isHovering = true;
        UpdateHoverState();
        if (_isLite && _currentMedia != null)
            _liteHoverTimer.Start();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isHovering = false;
        _liteHoverTimer.Stop();
        LiteHoverPopup.IsOpen = false;
        UpdateHoverState();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(OuterShell).Position;
        double nx = OuterShell.ActualWidth > 0 ? Math.Clamp(pt.X / OuterShell.ActualWidth, 0, 1) : 0.5;
        var neon = SynthwavePaletteHelper.DeriveAlbumNeonPalette(_accent, _secondaryAccentHex);
        var reveal = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(Math.Max(0, nx - 0.35), 0),
            EndPoint = new global::Windows.Foundation.Point(Math.Min(1, nx + 0.35), 1)
        };
        byte alpha = (byte)(_isDark ? 36 : 64);
        reveal.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 0 });
        reveal.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Primary, alpha), Offset = 0.35 });
        reveal.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Secondary, alpha), Offset = 0.65 });
        reveal.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 1 });
        RevealOverlay.Background = reveal;
    }

    private void OnCapsuleTapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src && IsChildOfControls(src))
            return;
        _liteHoverTimer.Stop();
        LiteHoverPopup.IsOpen = false;
        CapsuleClicked?.Invoke();
    }

    private bool IsChildOfControls(DependencyObject obj)
    {
        DependencyObject? cur = obj;
        while (cur != null)
        {
            if (ReferenceEquals(cur, HoverControlsPanel)) return true;
            cur = VisualTreeHelper.GetParent(cur);
        }
        return false;
    }

    private void OnPrevClick(object sender, RoutedEventArgs e) => PreviousRequested?.Invoke();
    private void OnToggleClick(object sender, RoutedEventArgs e) => ToggleRequested?.Invoke();
    private void OnNextClick(object sender, RoutedEventArgs e) => NextRequested?.Invoke();
}
