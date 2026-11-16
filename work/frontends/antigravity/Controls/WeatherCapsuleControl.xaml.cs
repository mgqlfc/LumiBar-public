using System;
using System.Collections.Generic;
using System.Linq;
using AntigravityFrontend.Sdk;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace AntigravityFrontend.Controls;

public sealed partial class WeatherCapsuleControl : UserControl
{
    private enum CapsuleMood { Sunny, ClearNight, Cloudy, Fog, Rain, Snow, Storm }

    private sealed class CapsuleParticle
    {
        public UIElement Visual { get; init; } = null!;
        public double X { get; set; }
        public double Y { get; set; }
        public double Vx { get; set; }
        public double Vy { get; set; }
        public double Phase { get; set; }
        public double BaseOpacity { get; set; }
    }

    private bool _isDark = true;
    private bool _isHovering;
    private bool _animationsEnabled = true;
    private double? _slotWidthOverride;
    private double? _slotHeightOverride;
    private PrecipitationViewModel? _precipModel;
    private bool _isMockMode;
    private DispatcherTimer? _hoverTimer;
    private readonly DispatcherTimer _ambientTimer;
    private readonly List<CapsuleParticle> _particles = new();
    private Ellipse? _ambientGlowOrb;
    private CapsuleMood _currentMood = CapsuleMood.Cloudy;
    private string _lastAmbientKey = "";
    private double _ambientTick;

    public event Action? CapsuleClicked;

    public void ApplySlotDimensions(double? widthDip, double? heightDip)
    {
        _slotWidthOverride = widthDip;
        _slotHeightOverride = heightDip;
        if (heightDip.HasValue && heightDip.Value >= 24)
        {
            double h = heightDip.Value;
            OuterShell.Height = h;
            OuterShell.CornerRadius = new CornerRadius(h / 2.0);
        }
        if (widthDip.HasValue && widthDip.Value >= 24 && Visibility == Visibility.Visible)
        {
            OuterShell.Width = widthDip.Value;
        }
    }

    public void CloseHoverPopup()
    {
        _hoverTimer?.Stop();
        PrecipBubblePopup.IsOpen = false;
    }

    public WeatherCapsuleControl()
    {
        InitializeComponent();
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_isHovering && Visibility == Visibility.Visible)
            {
                PrecipBubblePopup.IsOpen = true;
                RepositionHoverBubble();
            }
        };

        _ambientTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _ambientTimer.Tick += OnAmbientTick;
    }

    private void OnCapsuleHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = Math.Max(0, e.NewSize.Width);
        double h = Math.Max(0, e.NewSize.Height);
        double r = h / 2.0;
        WeatherTintLayer.CornerRadius = new CornerRadius(r);
        RevealOverlay.CornerRadius = new CornerRadius(r);
        CapsuleInnerHost.Clip = new RectangleGeometry
        {
            Rect = new global::Windows.Foundation.Rect(0, 0, w, h)
        };
    }

    public void UpdateState(StateDto state, PrecipitationViewModel precipModel, bool isMockMode, bool isLite, bool suppressHoverBubble)
    {
        _isDark = Services.Win32WindowHelper.ResolveIsDark(state.Appearance, state.Settings, isTaskbar: true);
        _animationsEnabled = state.Settings.Animations;
        _precipModel = precipModel;
        _isMockMode = isMockMode;
        var activeFont = state.Appearance.FontFamily ?? state.Settings.FontFamily;
        Services.Win32WindowHelper.ApplyFontFamily(this, activeFont);
        Services.Win32WindowHelper.ApplyFontFamily(BubbleFontHost, activeFont);

        if (!state.Settings.ShowWeather)
        {
            Visibility = Visibility.Collapsed;
            _ambientTimer.Stop();
            PrecipBubblePopup.IsOpen = false;
            return;
        }

        Visibility = Visibility.Visible;
        OuterShell.Width = _slotWidthOverride is > 0 ? _slotWidthOverride.Value : (isLite ? 64 : 104);
        if (_slotHeightOverride is >= 24)
        {
            OuterShell.Height = _slotHeightOverride.Value;
            OuterShell.CornerRadius = new CornerRadius(_slotHeightOverride.Value / 2.0);
        }
        DescText.Visibility = isLite ? Visibility.Collapsed : Visibility.Visible;

        var forecast = state.Weather.Forecast;
        var city = state.Weather.City;
        bool isNight = forecast?.IsDay.HasValue == true
            ? !forecast.IsDay.Value
            : WeatherFormatting.IsCityNightNow(city?.TimeZone);

        if (forecast == null)
        {
            WeatherMistIconHost.Visibility = Visibility.Collapsed;
            WeatherIconText.Text = "☁️";
            WeatherIconText.Visibility = Visibility.Visible;
            TempText.Text = "--°";
            DescText.Text = "离线";
            ToolTipService.SetToolTip(OuterShell, $"{city?.Name ?? "未选择城市"}：{state.Weather.Status}");
        }
        else
        {
            if (forecast.Code is 45 or 48)
            {
                WeatherIconText.Visibility = Visibility.Collapsed;
                WeatherMistIconHost.Children.Clear();
                WeatherMistIconHost.Children.Add(Services.Win32WindowHelper.CreateMistIcon(15, _isDark));
                WeatherMistIconHost.Visibility = Visibility.Visible;
            }
            else
            {
                WeatherMistIconHost.Visibility = Visibility.Collapsed;
                WeatherIconText.Text = WeatherFormatting.EmojiForWmo(forecast.Code, isNight);
                WeatherIconText.Visibility = Visibility.Visible;
            }
            TempText.Text = WeatherFormatting.FormatTemp(forecast.Temperature, state.Settings.Fahrenheit);
            DescText.Text = WeatherFormatting.DescribeWmo(forecast.Code);
            ToolTipService.SetToolTip(OuterShell, $"{city?.Name ?? ""} · {DescText.Text} {TempText.Text}");
        }

        UpdateHoverBubbleContent(state, precipModel, isMockMode);

        if (suppressHoverBubble)
            PrecipBubblePopup.IsOpen = false;

        ApplyGlassStyling(state, forecast?.Code ?? 0, isNight);
    }

    private void UpdateHoverBubbleContent(StateDto state, PrecipitationViewModel? precipModel, bool isMockMode)
    {
        var city = state.Weather.City;
        var forecast = state.Weather.Forecast;

        BubbleLocationText.Text = string.IsNullOrWhiteSpace(city?.Name) ? "未选择地点" : city.Name;

        if (forecast == null)
        {
            BubbleFeelsText.Text = "体感 --°";
            BubbleWindText.Text = "风速 --";
            BubbleHumidityText.Text = "湿度 --";
        }
        else
        {
            string feelsStr = WeatherFormatting.FormatTemp(forecast.FeelsLike, state.Settings.Fahrenheit);
            BubbleFeelsText.Text = $"{(forecast.EstimatedFeelsLike ? "估算体感" : "体感")} {feelsStr}";
            BubbleWindText.Text = WeatherFormatting.FormatWind(forecast.Wind, forecast.WindDirection, state.Settings.WindMetersPerSecond);
            BubbleHumidityText.Text = $"湿度 {Math.Round(forecast.Humidity)}%";
        }

        var slots = precipModel?.Slots;
        var validMm = slots?.Where(s => !s.IsGap && s.Millimeters.HasValue).Select(s => s.Millimeters!.Value).ToList();
        if (validMm == null || validMm.Count == 0)
        {
            BubblePrecipSummaryText.Text = isMockMode ? "未来 1h 降水示例暂缺" : "未来 1h 降水暂不可用";
            BubblePrecipDot.Fill = new SolidColorBrush(Color.FromArgb(200, 148, 163, 184));
        }
        else
        {
            double maxMm = validMm.Max();
            if (maxMm > 0.01)
            {
                BubblePrecipSummaryText.Text = $"未来 1h 有降水 · 峰值 {maxMm:0.0} mm";
                BubblePrecipDot.Fill = new SolidColorBrush(Color.FromArgb(245, 56, 189, 248));
            }
            else
            {
                BubblePrecipSummaryText.Text = "未来 1h 无明显降水";
                BubblePrecipDot.Fill = new SolidColorBrush(Color.FromArgb(225, 52, 211, 153));
            }
        }

        BubblePrecipBadge.Visibility = isMockMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBubbleSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RepositionHoverBubble();
    }

    private void RepositionHoverBubble()
    {
        if (BubbleBorder.ActualHeight > 10)
            PrecipBubblePopup.VerticalOffset = -(BubbleBorder.ActualHeight + 10);
        if (BubbleBorder.ActualWidth > 10 && OuterShell.ActualWidth > 10)
            PrecipBubblePopup.HorizontalOffset = -Math.Max(0, (BubbleBorder.ActualWidth - OuterShell.ActualWidth) / 2.0);
    }

    private void ApplyGlassStyling(StateDto state, int wmoCode, bool isNight)
    {
        byte glassAlpha = (byte)Math.Clamp(state.Appearance.GlassOpacity * 220, 75, 225);
        OuterShell.Background = new SolidColorBrush(_isDark
            ? Color.FromArgb(glassAlpha, 26, 28, 36)
            : Color.FromArgb((byte)Math.Min(240, glassAlpha + 35), 248, 250, 253));

        OuterShell.BorderBrush = new SolidColorBrush(_isDark
            ? Color.FromArgb(34, 255, 255, 255)
            : Color.FromArgb(40, 15, 23, 42));

        var fg = new SolidColorBrush(_isDark ? Colors.White : Color.FromArgb(255, 15, 23, 42));
        TempText.Foreground = fg;
        DescText.Foreground = fg;

        var (primary, secondary, mood) = ResolveCapsuleAtmosphere(wmoCode, isNight);
        _currentMood = mood;
        double glowStrength = Math.Clamp(state.Appearance.Glow, 0.25, 1.0);
        byte a1 = (byte)Math.Clamp((_isDark ? 92 : 64) * glowStrength, 24, 118);
        byte a2 = (byte)Math.Clamp((_isDark ? 44 : 28) * glowStrength, 10, 60);

        var grad = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0),
            EndPoint = new global::Windows.Foundation.Point(1, 0.9)
        };
        if (mood == CapsuleMood.ClearNight)
        {
            grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb((byte)Math.Clamp(a1 + 36, 52, 148), 11, 19, 43), Offset = 0.0 });
            grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb((byte)Math.Clamp(a2 + 16, 26, 86), 79, 70, 229), Offset = 0.52 });
            grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(24, primary.R, primary.G, primary.B), Offset = 1.0 });
        }
        else
        {
            grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(a1, primary.R, primary.G, primary.B), Offset = 0.0 });
            grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(a2, secondary.R, secondary.G, secondary.B), Offset = 0.55 });
            grad.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 1.0 });
        }
        WeatherTintLayer.Background = grad;

        string key = $"{mood}|{_isDark}|{Math.Round(OuterShell.Width)}|{Math.Round(glowStrength, 1)}";
        if (key != _lastAmbientKey)
        {
            _lastAmbientKey = key;
            RebuildCapsuleAmbient(primary, secondary, mood, glowStrength);
        }

        if (_animationsEnabled && Visibility == Visibility.Visible)
        {
            if (!_ambientTimer.IsEnabled)
                _ambientTimer.Start();
        }
        else
        {
            _ambientTimer.Stop();
        }

        BubbleBorder.Background = new SolidColorBrush(_isDark
            ? Color.FromArgb(240, 24, 26, 34)
            : Color.FromArgb(245, 248, 250, 253));
        BubbleBorder.BorderBrush = Services.Win32WindowHelper.CreateGlassBorderBrush(_isDark, 50, 14);

        var bubbleFg = new SolidColorBrush(_isDark ? Colors.White : Color.FromArgb(255, 15, 23, 42));
        BubbleLocationText.Foreground = bubbleFg;
        BubbleFeelsText.Foreground = bubbleFg;
        BubbleWindText.Foreground = bubbleFg;
        BubbleHumidityText.Foreground = bubbleFg;
        BubblePrecipSummaryText.Foreground = bubbleFg;
        BubbleDivider.Background = bubbleFg;
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isHovering = true;
        RevealOverlay.Opacity = 1;
        _hoverTimer?.Start();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isHovering = false;
        RevealOverlay.Opacity = 0;
        _hoverTimer?.Stop();
        PrecipBubblePopup.IsOpen = false;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(OuterShell).Position;
        double nx = OuterShell.ActualWidth > 0 ? Math.Clamp(pt.X / OuterShell.ActualWidth, 0, 1) : 0.5;
        var reveal = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(Math.Max(0, nx - 0.35), 0),
            EndPoint = new global::Windows.Foundation.Point(Math.Min(1, nx + 0.35), 1)
        };
        reveal.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 0 });
        reveal.GradientStops.Add(new GradientStop { Color = Color.FromArgb((byte)(_isDark ? 36 : 75), 255, 255, 255), Offset = 0.5 });
        reveal.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 1 });
        RevealOverlay.Background = reveal;
    }

    private static (Color Primary, Color Secondary, CapsuleMood Mood) ResolveCapsuleAtmosphere(int wmoCode, bool isNight)
    {
        if (wmoCode is >= 95 and <= 99)
            return (Color.FromArgb(255, 139, 92, 246), Color.FromArgb(255, 56, 189, 248), CapsuleMood.Storm);
        if (wmoCode is (>= 71 and <= 77) or 85 or 86)
            return (Color.FromArgb(255, 186, 230, 253), Color.FromArgb(255, 56, 189, 248), CapsuleMood.Snow);
        if (wmoCode is (>= 51 and <= 67) or (>= 80 and <= 82))
            return (Color.FromArgb(255, 14, 165, 233), Color.FromArgb(255, 59, 130, 246), CapsuleMood.Rain);
        if (wmoCode is 45 or 48)
            return (Color.FromArgb(255, 148, 163, 184), Color.FromArgb(255, 100, 116, 139), CapsuleMood.Fog);
        if (wmoCode <= 1 && !isNight)
            return (Color.FromArgb(255, 251, 191, 36), Color.FromArgb(255, 251, 146, 60), CapsuleMood.Sunny);
        if (wmoCode <= 1 && isNight)
            return (Color.FromArgb(255, 56, 189, 248), Color.FromArgb(255, 99, 102, 241), CapsuleMood.ClearNight);
        return (Color.FromArgb(255, 96, 165, 250), Color.FromArgb(255, 148, 163, 184), CapsuleMood.Cloudy);
    }

    private void RebuildCapsuleAmbient(Color primary, Color secondary, CapsuleMood mood, double glowStrength)
    {
        WeatherAmbientCanvas.Children.Clear();
        _particles.Clear();

        double w = Math.Max(64, OuterShell.Width);
        double h = Math.Max(26, OuterShell.Height);
        var orbBrush = new RadialGradientBrush
        {
            Center = new global::Windows.Foundation.Point(0.5, 0.5),
            GradientOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        byte oc = (byte)Math.Clamp((_isDark ? 96 : 68) * glowStrength, 24, 116);
        orbBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(oc, primary.R, primary.G, primary.B), Offset = 0.0 });
        orbBrush.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 1.0 });

        _ambientGlowOrb = new Ellipse { Width = 54, Height = 54, Fill = orbBrush };
        Canvas.SetLeft(_ambientGlowOrb, -6);
        Canvas.SetTop(_ambientGlowOrb, -12);
        WeatherAmbientCanvas.Children.Add(_ambientGlowOrb);

        var rng = new Random((int)mood * 313 + 7);
        int count = mood switch
        {
            CapsuleMood.ClearNight => 8,
            CapsuleMood.Rain or CapsuleMood.Storm => 8,
            CapsuleMood.Snow => 7,
            CapsuleMood.Sunny => 5,
            _ => 4
        };

        for (int i = 0; i < count; i++)
        {
            double x = 4 + rng.NextDouble() * (w - 8);
            double y = 2 + rng.NextDouble() * (h - 6);
            FrameworkElement vis;
            double vx = 0, vy = 0, baseOp = 0.55;

            if (mood == CapsuleMood.ClearNight)
            {
                double sz = 1.4 + rng.NextDouble() * 1.4;
                vis = new Ellipse
                {
                    Width = sz,
                    Height = sz,
                    Fill = new SolidColorBrush(i % 2 == 0 ? Color.FromArgb(255, 224, 242, 254) : Colors.White)
                };
                baseOp = 0.48 + rng.NextDouble() * 0.36;
            }
            else if (mood is CapsuleMood.Rain or CapsuleMood.Storm)
            {
                if (i % 3 == 0)
                {
                    double sz = 3.2 + rng.NextDouble() * 1.8;
                    vis = new Ellipse
                    {
                        Width = sz,
                        Height = sz,
                        Fill = new SolidColorBrush(Color.FromArgb(145, 224, 242, 254))
                    };
                    vy = 0.25 + rng.NextDouble() * 0.3;
                    baseOp = 0.55;
                }
                else
                {
                    vis = new Border
                    {
                        Width = 1.2,
                        Height = 7,
                        CornerRadius = new CornerRadius(1),
                        Background = new SolidColorBrush(Color.FromArgb(215, 186, 230, 253)),
                        RenderTransform = new RotateTransform { Angle = 14 }
                    };
                    vx = -0.65;
                    vy = 2.1 + rng.NextDouble() * 1.2;
                    baseOp = 0.44;
                }
            }
            else if (mood == CapsuleMood.Snow)
            {
                double sz = 2.0 + rng.NextDouble() * 1.8;
                vis = new Ellipse
                {
                    Width = sz,
                    Height = sz,
                    Fill = new SolidColorBrush(_isDark ? Colors.White : Color.FromArgb(255, 186, 230, 253))
                };
                vx = -0.1 + rng.NextDouble() * 0.2;
                vy = 0.32 + rng.NextDouble() * 0.28;
                baseOp = 0.52;
            }
            else
            {
                double sz = mood == CapsuleMood.Sunny ? 8 + rng.NextDouble() * 7 : 28 + rng.NextDouble() * 16;
                double ph = mood == CapsuleMood.Sunny ? sz : 14;
                vis = new Ellipse
                {
                    Width = sz,
                    Height = ph,
                    Fill = new SolidColorBrush(Color.FromArgb(48, primary.R, primary.G, primary.B))
                };
                vx = 0.16 + rng.NextDouble() * 0.14;
                vy = mood == CapsuleMood.Sunny ? -0.06 : 0;
                baseOp = 0.48;
            }

            vis.Opacity = baseOp;
            Canvas.SetLeft(vis, x);
            Canvas.SetTop(vis, y);
            WeatherAmbientCanvas.Children.Add(vis);
            _particles.Add(new CapsuleParticle
            {
                Visual = vis,
                X = x,
                Y = y,
                Vx = vx,
                Vy = vy,
                Phase = rng.NextDouble() * Math.PI * 2,
                BaseOpacity = baseOp
            });
        }
    }

    private void OnAmbientTick(object? sender, object e)
    {
        if (Visibility != Visibility.Visible || !_animationsEnabled)
        {
            _ambientTimer.Stop();
            return;
        }

        _ambientTick += 0.045;
        double w = Math.Max(64, OuterShell.Width);
        double h = Math.Max(26, OuterShell.Height);

        if (_ambientGlowOrb != null)
        {
            Canvas.SetLeft(_ambientGlowOrb, -6 + Math.Sin(_ambientTick * 0.8) * 14);
            Canvas.SetTop(_ambientGlowOrb, -12 + Math.Cos(_ambientTick * 0.6) * 5);
            if (_currentMood == CapsuleMood.Storm)
            {
                double cyc = _ambientTick % 5.8;
                _ambientGlowOrb.Opacity = cyc is > 4.2 and < 4.45 ? 1.0 : 0.65;
            }
            else
            {
                _ambientGlowOrb.Opacity = 0.72 + Math.Sin(_ambientTick * 1.4) * 0.22;
            }
        }

        foreach (var p in _particles)
        {
            if (_currentMood == CapsuleMood.ClearNight)
            {
                p.Visual.Opacity = Math.Clamp(p.BaseOpacity * (0.35 + 0.65 * Math.Sin(_ambientTick * 2.2 + p.Phase)), 0.08, 0.92);
                continue;
            }

            p.X += p.Vx + (_currentMood == CapsuleMood.Snow ? Math.Sin(_ambientTick * 2.0 + p.Phase) * 0.18 : 0);
            p.Y += p.Vy;
            if (p.X < -18) p.X = w + 6;
            if (p.X > w + 18) p.X = -12;
            if (p.Y > h + 4) { p.Y = -6; p.X = (p.X + w * 0.31) % w; }
            if (p.Y < -14) p.Y = h + 2;

            Canvas.SetLeft(p.Visual, p.X);
            Canvas.SetTop(p.Visual, p.Y);
        }
    }

    private void OnCapsuleTapped(object sender, TappedRoutedEventArgs e)
    {
        _hoverTimer?.Stop();
        PrecipBubblePopup.IsOpen = false;
        CapsuleClicked?.Invoke();
    }
}
