using System;
using AntigravityFrontend.Sdk;
using AntigravityFrontend.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace AntigravityFrontend.Controls;

public sealed partial class MusicCapsuleControl : UserControl
{
    private const string DefaultRetroMonoFont = "Lucida Console, Sarasa Term SC, Microsoft YaHei UI";

    private bool _isHovering;
    private bool _isLite;
    private bool _isDark = true;
    private bool _animationsEnabled = true;
    private double _standardWidth = 248;
    private double? _slotWidthOverride;
    private double? _slotHeightOverride;
    private Color _accent = Color.FromArgb(255, 255, 107, 0);
    private MediaDto? _currentMedia;
    private readonly DispatcherTimer _liteHoverTimer;
    private readonly DispatcherTimer _synthPulseTimer;
    private double _synthPhase;
    private double _lastCrtW = -1;
    private double _lastCrtH = -1;

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
                AmbientTintLayer.Opacity = 0.72;
                InnerNeonRim.Opacity = 0.78;
                return;
            }
            _synthPhase += 0.11;
            double beat = 0.56 + 0.44 * Math.Sin(_synthPhase);
            AmbientTintLayer.Opacity = 0.48 + beat * 0.46;
            InnerNeonRim.Opacity = 0.54 + beat * 0.42;
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
        {
            OuterShell.Width = widthDip.Value;
        }
    }

    private void ApplyHeightResponsiveMetrics(double h)
    {
        double radius = h / 2.0;
        AmbientTintLayer.CornerRadius = new CornerRadius(radius);
        InnerNeonRim.CornerRadius = new CornerRadius(Math.Max(4, radius - 1));
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
            StandardLayout.Padding = new Thickness(5, 1, 7, 1);
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
            CenterTextVeil.Margin = new Thickness(32, 3, 72, 3);
            LiteLayout.Padding = new Thickness(8, 5, 8, 5);
        }
        else
        {
            StandardLayout.Padding = new Thickness(4, 0, 6, 0);
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
            CenterTextVeil.Margin = new Thickness(art + 6, 2, 66, 2);
            LiteLayout.Padding = new Thickness(6, 4, 6, 4);
        }

        RebuildCrtScanTexture();
    }

    private void OnInnerCrtSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RebuildCrtScanTexture();
    }

    private void RebuildCrtScanTexture()
    {
        double w = InnerCrtScanCanvas.ActualWidth > 4 ? InnerCrtScanCanvas.ActualWidth : OuterShell.Width;
        double h = InnerCrtScanCanvas.ActualHeight > 4 ? InnerCrtScanCanvas.ActualHeight : OuterShell.Height;
        if (w <= 4 || h <= 4) return;
        if (Math.Abs(w - _lastCrtW) < 1 && Math.Abs(h - _lastCrtH) < 1) return;

        _lastCrtW = w;
        _lastCrtH = h;
        InnerCrtScanCanvas.Clip = new RectangleGeometry
        {
            Rect = new global::Windows.Foundation.Rect(0, 0, w, h)
        };
        InnerCrtScanCanvas.Children.Clear();

        // G3 CRT inner scanliner: subtle 1px horizontal dark raster lines every 3px over inner neon bezel
        var scanBrush = new SolidColorBrush(Color.FromArgb(_isDark ? (byte)76 : (byte)32, 4, 6, 14));
        for (double y = 1.5; y < h - 1; y += 3.0)
        {
            var strip = new Rectangle { Width = w, Height = 1.0, Fill = scanBrush };
            Canvas.SetLeft(strip, 0);
            Canvas.SetTop(strip, y);
            InnerCrtScanCanvas.Children.Add(strip);
        }
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
        var neon = SynthwavePaletteHelper.DeriveAlbumNeonPalette(_accent);

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

        string glyph = media.Playing ? "\uE769" : "\uE768";
        PlayPauseGlyph.Glyph = glyph;

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
            AmbientTintLayer.Opacity = 0.72;
            InnerNeonRim.Opacity = 0.78;
        }

        UpdateHoverState();
    }

    public void UpdateSpectrum(SpectrumDto spectrum)
    {
        if (Visibility != Visibility.Visible) return;
        if (_isLite)
            LiteSpectrumBackdrop.UpdateBands(spectrum.Bands);
        else
            SpectrumBackdrop.UpdateBands(spectrum.Bands);
    }

    private void ApplyGlassStyling(StateDto state, SynthwavePaletteHelper.NeonAlbumPalette neon)
    {
        // High-contrast smoked obsidian pill base (matches prototype #070A14)
        byte glassAlpha = (byte)Math.Clamp(state.Appearance.GlassOpacity * 245, 198, 246);
        OuterShell.Background = new SolidColorBrush(_isDark
            ? Color.FromArgb(glassAlpha, 7, 10, 20)
            : Color.FromArgb(Math.Min((byte)245, glassAlpha), 246, 242, 255));

        var outerRim = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0),
            EndPoint = new global::Windows.Foundation.Point(1, 1)
        };
        byte rimA = (byte)(_isDark ? 148 : 112);
        outerRim.GradientStops.Add(new GradientStop { Color = Color.FromArgb(rimA, 0, 245, 212), Offset = 0.0 });
        outerRim.GradientStops.Add(new GradientStop { Color = Color.FromArgb((byte)(rimA * 0.75), 139, 92, 246), Offset = 0.5 });
        outerRim.GradientStops.Add(new GradientStop { Color = Color.FromArgb(rimA, 255, 42, 133), Offset = 1.0 });
        OuterShell.BorderBrush = outerRim;

        var titleFg = new SolidColorBrush(_isDark ? Color.FromArgb(255, 248, 250, 252) : Color.FromArgb(255, 24, 16, 48));
        var artistFg = new SolidColorBrush(_isDark ? neon.Primary : Color.FromArgb(255, 91, 33, 182));
        TitleText.Foreground = titleFg;
        ArtistText.Foreground = artistFg;

        // Dark readable center lane mask so G3 inner rim never distracts behind song title/artist
        var veilBrush = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0.5, 0),
            EndPoint = new global::Windows.Foundation.Point(0.5, 1)
        };
        if (_isDark)
        {
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 7, 10, 20), Offset = 0.0 });
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(204, 7, 10, 20), Offset = 0.25 });
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(204, 7, 10, 20), Offset = 0.75 });
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 7, 10, 20), Offset = 1.0 });
        }
        else
        {
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 246, 242, 255), Offset = 0.0 });
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(190, 246, 242, 255), Offset = 0.5 });
            veilBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 246, 242, 255), Offset = 1.0 });
        }
        CenterTextVeil.Background = veilBrush;

        // G3 Album-Sampled Neon Inner Perimeter Ring
        if (state.Appearance.CoverGlow)
        {
            byte auraA = (byte)Math.Clamp(Math.Max(0.55, state.Appearance.Glow) * 168, 72, 165);
            var ringBorder = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0, 0.5),
                EndPoint = new global::Windows.Foundation.Point(1, 0.5)
            };
            ringBorder.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Primary, auraA), Offset = 0.0 });
            ringBorder.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Secondary, (byte)(auraA * 0.78)), Offset = 0.5 });
            ringBorder.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Primary, auraA), Offset = 1.0 });

            AmbientTintLayer.BorderBrush = ringBorder;
            InnerNeonRim.BorderBrush = new SolidColorBrush(SynthwavePaletteHelper.WithAlpha(neon.Primary, 115));

            var edgeWash = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0.5, 0),
                EndPoint = new global::Windows.Foundation.Point(0.5, 1)
            };
            edgeWash.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Primary, 44), Offset = 0.0 });
            edgeWash.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 0.34 });
            edgeWash.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 0.66 });
            edgeWash.GradientStops.Add(new GradientStop { Color = SynthwavePaletteHelper.WithAlpha(neon.Secondary, 42), Offset = 1.0 });
            AmbientTintLayer.Background = edgeWash;
        }
        else
        {
            AmbientTintLayer.BorderBrush = new SolidColorBrush(Colors.Transparent);
            AmbientTintLayer.Background = new SolidColorBrush(Colors.Transparent);
            InnerNeonRim.BorderBrush = new SolidColorBrush(Colors.Transparent);
        }

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
        LiteHoverBorder.BorderBrush = outerRim;
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
        TextInfoPanel.Opacity = 1.0; // Fixed Slot 3 buttons never obscure or dim Slot 2 text
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
        var neon = SynthwavePaletteHelper.DeriveAlbumNeonPalette(_accent);
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
