using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AntigravityFrontend.Sdk;
using AntigravityFrontend.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using Windows.UI;

namespace AntigravityFrontend.Windows;

public sealed partial class WeatherFlyoutWindow : Window
{
    private readonly LumiBarBridgeService _bridge;
    private readonly PluginConfig _config;

    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;
    private bool _isDark = true;
    private (int Left, int Top, int Right, int Bottom) _lastAnchor;
    private double _lastFittedHeightDip = -1;
    private int _refitAttempts;
    private readonly DispatcherTimer _refitTimer;

    private enum WeatherMood { Sunny, ClearNight, Cloudy, Fog, Rain, Snow, Storm }
    private enum AmbientKind { Star, Meteor, RainStreak, GlassDroplet, GlassSplash, Snow, Sunbeam, SunDust, CloudMist }

    private sealed class AmbientParticle
    {
        public AmbientKind Kind { get; init; }
        public UIElement Visual { get; init; } = null!;
        public FrameworkElement? TrailVisual { get; init; }
        public ScaleTransform? Scale { get; init; }
        public RotateTransform? Rotate { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Vx { get; set; }
        public double Vy { get; set; }
        public double Size { get; set; }
        public double Phase { get; set; }
        public double Speed { get; set; }
        public double BaseOpacity { get; set; }
        public double Period { get; set; }
        public double Timer { get; set; }
        public double TrailHeight { get; set; }
        public int State { get; set; }
    }

    private readonly DispatcherTimer _ambientTimer;
    private readonly List<AmbientParticle> _particles = [];
    private Ellipse? _orbPrimary;
    private Ellipse? _orbSecondary;
    private WeatherMood _currentMood = WeatherMood.Cloudy;
    private string _lastAtmosphereKey = "";
    private double _ambientTick;
    private double _glowStrength = 0.65;
    private bool _animationsEnabled = true;

    public bool IsWindowVisible { get; private set; }

    public WeatherFlyoutWindow(LumiBarBridgeService bridge, PluginConfig config)
    {
        _bridge = bridge;
        _config = config;

        InitializeComponent();
        Win32WindowHelper.ConfigureBorderlessPopup(this, hideFromAltTab: true, alwaysOnTop: true);
        (_acrylicController, _backdropConfig) = Win32WindowHelper.AttachAcrylicBackdrop(this, true, 0.62);

        _refitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _refitTimer.Tick += (_, _) =>
        {
            _refitTimer.Stop();
            RefitUsingScrollMetrics();
        };

        _ambientTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _ambientTimer.Tick += OnAmbientTick;

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated &&
                _config.CloseOnDeactivate &&
                !_config.PinWeatherFlyout)
            {
                HidePopup();
            }
        };

        RootFrame.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape)
                HidePopup();
        };

        ContentStack.SizeChanged += (_, e) =>
        {
            if (IsWindowVisible && e.NewSize.Height > 40)
                _refitTimer.Start();
        };

        SyncPinIcon();
    }

    public void ShowAtAnchor(int leftPx, int topPx, int rightPx, int bottomPx)
    {
        _lastAnchor = (leftPx, topPx, rightPx, bottomPx);
        _lastFittedHeightDip = -1;
        _refitAttempts = 0;
        IsWindowVisible = true;
        ApplyState(_bridge.CurrentState);

        ContentStack.InvalidateMeasure();
        ContentStack.Measure(new global::Windows.Foundation.Size(Math.Max(320, _config.WeatherFlyoutWidth - 40), 2400));
        ApplyTargetHeight(ContentStack.DesiredSize.Height + 48);

        Activate();
        Win32WindowHelper.ApplyBorderlessDwmChrome(this);
        _refitTimer.Start();
        if (_animationsEnabled)
            _ambientTimer.Start();
    }

    private void RefitUsingScrollMetrics()
    {
        if (!IsWindowVisible) return;
        MainScrollViewer.UpdateLayout();

        double scale = Win32WindowHelper.GetWindowScale(this);
        var appWin = Win32WindowHelper.GetAppWindow(this);
        double curWindowDip = appWin.Size.Height / scale;
        if (curWindowDip <= 60) return;

        bool changed = false;
        // Directly eliminate any non-zero ScrollableHeight caused by DPI rounding or inner scrollbars
        if (MainScrollViewer.ScrollableHeight > 0.5)
        {
            changed = ApplyTargetHeight(curWindowDip + MainScrollViewer.ScrollableHeight + 4);
        }
        else if (MainScrollViewer.ViewportHeight > MainScrollViewer.ExtentHeight + 6 && MainScrollViewer.ExtentHeight > 60)
        {
            double excess = MainScrollViewer.ViewportHeight - MainScrollViewer.ExtentHeight;
            changed = ApplyTargetHeight(curWindowDip - excess + 3);
        }

        if (changed && _refitAttempts < 4)
        {
            _refitAttempts++;
            _refitTimer.Start();
        }
        else
        {
            Win32WindowHelper.ApplyBorderlessDwmChrome(this);
        }
    }

    private bool ApplyTargetHeight(double desiredTotalDip)
    {
        if (desiredTotalDip <= 60) return false;
        double cap = Math.Max(480, _config.WeatherFlyoutHeight);
        double targetHeight = Math.Clamp(Math.Ceiling(desiredTotalDip), 260, cap);

        if (Math.Abs(targetHeight - _lastFittedHeightDip) < 2)
            return false;

        _lastFittedHeightDip = targetHeight;
        Win32WindowHelper.PositionPopupAboveOrBelowAnchor(
            this, _lastAnchor.Left, _lastAnchor.Top, _lastAnchor.Right, _lastAnchor.Bottom,
            _config.WeatherFlyoutWidth, targetHeight, _config.PopupDirection);
        return true;
    }

    public void HidePopup()
    {
        IsWindowVisible = false;
        _ambientTimer.Stop();
        Win32WindowHelper.GetAppWindow(this).Hide();
    }

    public void ApplyState(StateDto state)
    {
        _isDark = Win32WindowHelper.ResolveIsDark(state.Appearance, state.Settings, isTaskbar: false);
        RootFrame.RequestedTheme = _isDark ? ElementTheme.Dark : ElementTheme.Light;
        Win32WindowHelper.ApplyFontFamily(FontHost, state.Appearance.FontFamily ?? state.Settings.FontFamily);
        if (_backdropConfig != null)
            _backdropConfig.Theme = _isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;

        var city = state.Weather.City;
        var forecast = state.Weather.Forecast;
        bool isNight = forecast?.IsDay.HasValue == true
            ? !forecast.IsDay.Value
            : WeatherFormatting.IsCityNightNow(city?.TimeZone);
        int wmoCode = forecast?.Code ?? 2;

        _glowStrength = Math.Clamp(state.Appearance.Glow, 0.0, 1.0);
        _animationsEnabled = state.Settings.Animations;
        var (primaryGlow, secondaryGlow, accentTint, mood) = GetWeatherAtmosphere(wmoCode, isNight);

        ToolTipService.SetToolTip(HeroWeatherHost, _bridge.IsMockMode
            ? "示例模式：点击天气图标切换场景（晴夜星空 / 雨滴玻璃 / 雷雨 / 雪 / 暖阳 / 雾 / 多云）"
            : null);

        Win32WindowHelper.UpdateAcrylicTint(_acrylicController, _isDark, state.Appearance.GlassOpacity, accentTint);
        ApplySurfaceBrushes();
        ApplyWeatherAmbientGlow(primaryGlow, secondaryGlow, mood, wmoCode, isNight, state.Appearance.GlassOpacity);
        CityNameText.Text = city?.Name ?? "未选择城市";
        RegionText.Text = city?.Region ?? "";
        UpdatedText.Text = string.IsNullOrWhiteSpace(state.Weather.Status)
            ? (forecast?.Updated ?? "等待更新")
            : state.Weather.Status;

        if (!_bridge.IsMockMode && !_bridge.IsConnected)
        {
            StatusBanner.Title = "后端离线（当前展示最近缓存）";
            StatusBanner.Message = _bridge.LastErrorMessage;
            StatusBanner.Severity = InfoBarSeverity.Warning;
            StatusBanner.IsOpen = true;
        }
        else if (forecast == null)
        {
            StatusBanner.Title = "暂无天气实况数据";
            StatusBanner.Message = string.IsNullOrWhiteSpace(state.Weather.Status) ? "请点击右上角刷新或检查网络连接。" : state.Weather.Status;
            StatusBanner.Severity = InfoBarSeverity.Informational;
            StatusBanner.IsOpen = true;
        }
        else
        {
            StatusBanner.IsOpen = false;
        }

        PrecipTrendCard.Update(_bridge.GetOneHourPrecipitation(), _bridge.IsMockMode, _isDark);

        if (forecast == null)
        {
            HeroEmoji.Text = "☁️";
            HeroEmoji.Visibility = Visibility.Visible;
            HeroMistIconHost.Visibility = Visibility.Collapsed;
            HeroTempText.Text = "--°";
            HeroCondText.Text = "暂无数据";
            HeroRangeFeelsText.Text = "等待天气服务返回";
            HourlyItemsPanel.Children.Clear();
            HourlyCurveCanvas.Children.Clear();
            DailyRowsPanel.Children.Clear();
            DailyScrollHintText.Text = "";
            WindText.Text = "--";
            HumidityText.Text = "相对湿度 --";
            AqiNumberText.Text = "--";
            AqiLevelText.Text = "暂无";
            PmText.Text = "PM2.5 / PM10 暂无";
            return;
        }

        bool isF = state.Settings.Fahrenheit;
        bool isMps = state.Settings.WindMetersPerSecond;

        if (forecast.Code is 45 or 48)
        {
            HeroEmoji.Visibility = Visibility.Collapsed;
            HeroMistIconHost.Children.Clear();
            HeroMistIconHost.Children.Add(Win32WindowHelper.CreateMistIcon(34, _isDark));
            HeroMistIconHost.Visibility = Visibility.Visible;
        }
        else
        {
            HeroMistIconHost.Visibility = Visibility.Collapsed;
            HeroEmoji.Text = WeatherFormatting.EmojiForWmo(forecast.Code, isNight);
            HeroEmoji.Visibility = Visibility.Visible;
        }
        HeroTempText.Text = WeatherFormatting.FormatTemp(forecast.Temperature, isF);
        HeroCondText.Text = WeatherFormatting.DescribeWmo(forecast.Code);

        string feelsLabel = forecast.EstimatedFeelsLike ? "估算体感" : "体感";
        string feels = WeatherFormatting.FormatTemp(forecast.FeelsLike, isF);
        string solarNote = "";
        if (!string.IsNullOrWhiteSpace(forecast.Sunrise) && !string.IsNullOrWhiteSpace(forecast.Sunset) &&
            DateTimeOffset.TryParse(forecast.Sunrise, out var sr) &&
            DateTimeOffset.TryParse(forecast.Sunset, out var ss))
        {
            solarNote = $" · ☀️{sr:HH:mm}/🌙{ss:HH:mm}";
        }
        if (forecast.Days.Count > 0)
        {
            string hi = WeatherFormatting.FormatTemp(forecast.Days[0].Max, isF);
            string lo = WeatherFormatting.FormatTemp(forecast.Days[0].Min, isF);
            HeroRangeFeelsText.Text = $"高 {hi} 低 {lo} · {feelsLabel} {feels}{solarNote}";
        }
        else
        {
            HeroRangeFeelsText.Text = $"{feelsLabel} {feels}{solarNote}";
        }

        bool hideRainProb = string.Equals(forecast.Source, "MET Norway", StringComparison.OrdinalIgnoreCase);
        HourlySourceNote.Text = string.IsNullOrWhiteSpace(forecast.TimeZoneLabel) ? forecast.Source : $"{forecast.Source} · {forecast.TimeZoneLabel}";

        RenderHourlyForecast(forecast.Hours, isF, hideRainProb);
        RenderDailyForecast(forecast.Days, isF, hideRainProb);

        WindText.Text = WeatherFormatting.FormatWind(forecast.Wind, forecast.WindDirection, isMps);
        HumidityText.Text = $"相对湿度 {Math.Round(forecast.Humidity)}%";

        if (!string.IsNullOrWhiteSpace(forecast.AirError))
        {
            AqiNumberText.Text = "--";
            AqiLevelText.Text = "缺失";
            AqiBadge.Background = new SolidColorBrush(Color.FromArgb(255, 100, 116, 139));
            PmText.Text = forecast.AirError;
        }
        else if (!forecast.Aqi.HasValue)
        {
            AqiNumberText.Text = "--";
            AqiLevelText.Text = "暂无";
            AqiBadge.Background = new SolidColorBrush(Color.FromArgb(255, 100, 116, 139));
            PmText.Text = "空气质量数据暂缺";
        }
        else
        {
            var (lvl, col) = WeatherFormatting.DescribeUsAqi(forecast.Aqi);
            AqiNumberText.Text = Math.Round(forecast.Aqi.Value).ToString(CultureInfo.InvariantCulture);
            AqiLevelText.Text = lvl;
            AqiBadge.Background = new SolidColorBrush(col);
            string pm25 = forecast.Pm25.HasValue ? $"{Math.Round(forecast.Pm25.Value)}" : "--";
            string pm10 = forecast.Pm10.HasValue ? $"{Math.Round(forecast.Pm10.Value)}" : "--";
            PmText.Text = $"PM2.5: {pm25} · PM10: {pm10} µg/m³";
        }
    }

    private void RenderHourlyForecast(List<HourForecastDto> hours, bool isF, bool hideRain)
    {
        HourlyItemsPanel.Children.Clear();
        HourlyCurveCanvas.Children.Clear();

        var slice = hours.Take(24).ToList();
        if (slice.Count == 0) return;

        double minT = slice.Min(h => h.Temperature);
        double maxT = slice.Max(h => h.Temperature);
        double span = Math.Max(2.0, maxT - minT);

        var line = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(230, 56, 189, 248)),
            StrokeThickness = 2
        };

        const double itemStep = 56.0;
        for (int i = 0; i < slice.Count; i++)
        {
            var h = slice[i];
            double x = i * itemStep + 14;
            double norm = (h.Temperature - minT) / span;
            double y = 34 - norm * 26;
            line.Points.Add(new global::Windows.Foundation.Point(x, y));

            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(Color.FromArgb(255, 125, 211, 252))
            };
            Canvas.SetLeft(dot, x - 3);
            Canvas.SetTop(dot, y - 3);
            HourlyCurveCanvas.Children.Add(dot);

            string hourLabel = h.Time;
            if (DateTime.TryParse(h.Time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                hourLabel = dt.ToString("HH:00");

            var col = new StackPanel
            {
                Width = 42,
                Spacing = 3,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            col.Children.Add(new TextBlock
            {
                Text = WeatherFormatting.FormatTemp(h.Temperature, isF),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            UIElement hourIcon = h.Code is 45 or 48
                ? new Grid
                {
                    Height = 20,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { Win32WindowHelper.CreateMistIcon(16, _isDark) }
                }
                : new TextBlock
                {
                    Text = WeatherFormatting.EmojiForWmo(
                        h.Code,
                        h.IsDay.HasValue ? !h.IsDay.Value : WeatherFormatting.IsNightHour(h.Time)),
                    FontSize = 15,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
            col.Children.Add(hourIcon);
            col.Children.Add(new TextBlock
            {
                Text = hourLabel,
                FontSize = 12,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            if (!hideRain)
            {
                col.Children.Add(new TextBlock
                {
                    Text = $"{Math.Round(h.Rain)}%",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(220, 56, 189, 248)),
                    Opacity = h.Rain > 0 ? 1 : 0.45,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            HourlyItemsPanel.Children.Add(col);
        }

        HourlyCurveCanvas.Width = Math.Max(320, slice.Count * itemStep);
        HourlyCurveCanvas.Children.Insert(0, line);
    }

    private void RenderDailyForecast(List<DayForecastDto> days, bool isF, bool hideRain)
    {
        DailyRowsPanel.Children.Clear();
        if (days.Count == 0) return;

        DailyHeaderText.Text = $"{days.Count} 天天气预报";
        DailyScrollHintText.Text = days.Count > 5 ? "默认显示 5 天 · 模块内下滑" : "";
        double globalMin = days.Min(d => d.Min);
        double globalMax = days.Max(d => d.Max);
        double totalSpan = Math.Max(2.0, globalMax - globalMin);

        for (int i = 0; i < days.Count; i++)
        {
            var d = days[i];
            string dayName = i == 0 ? "今天" : i == 1 ? "明天" : d.Date;
            if (i >= 2 && DateTime.TryParse(d.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                dayName = dt.ToString("ddd", CultureInfo.GetCultureInfo("zh-CN"));

            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(hideRain ? 0 : 38) });

            var nameTb = new TextBlock { Text = dayName, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            FrameworkElement iconEl = d.Code is 45 or 48
                ? new Grid
                {
                    Height = 20,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { Win32WindowHelper.CreateMistIcon(16, _isDark) }
                }
                : new TextBlock { Text = WeatherFormatting.EmojiForWmo(d.Code, false), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center };
            var minTb = new TextBlock { Text = WeatherFormatting.FormatTemp(d.Min, isF), FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

            var barHost = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
            barHost.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(_isDark ? Color.FromArgb(35, 255, 255, 255) : Color.FromArgb(25, 15, 23, 42))
            });

            double startFrac = (d.Min - globalMin) / totalSpan;
            double widthFrac = Math.Max(0.12, (d.Max - d.Min) / totalSpan);
            var barGrid = new Grid();
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, startFrac), GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widthFrac, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - startFrac - widthFrac), GridUnitType.Star) });

            var rangeBar = new Border
            {
                CornerRadius = new CornerRadius(3),
                Background = new LinearGradientBrush
                {
                    StartPoint = new global::Windows.Foundation.Point(0, 0),
                    EndPoint = new global::Windows.Foundation.Point(1, 0),
                    GradientStops =
                    {
                        new GradientStop { Color = Color.FromArgb(255, 56, 189, 248), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(255, 251, 191, 36), Offset = 1 }
                    }
                }
            };
            Grid.SetColumn(rangeBar, 1);
            barGrid.Children.Add(rangeBar);
            barHost.Children.Add(barGrid);

            var maxTb = new TextBlock { Text = WeatherFormatting.FormatTemp(d.Max, isF), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var rainTb = new TextBlock
            {
                Text = hideRain ? "" : $"{Math.Round(d.Rain)}%",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(220, 56, 189, 248)),
                Opacity = d.Rain > 0 ? 1 : 0.4,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Grid.SetColumn(nameTb, 0);
            Grid.SetColumn(iconEl, 1);
            Grid.SetColumn(minTb, 2);
            Grid.SetColumn(barHost, 3);
            Grid.SetColumn(maxTb, 4);
            Grid.SetColumn(rainTb, 5);

            row.Children.Add(nameTb);
            row.Children.Add(iconEl);
            row.Children.Add(minTb);
            row.Children.Add(barHost);
            row.Children.Add(maxTb);
            row.Children.Add(rainTb);
            DailyRowsPanel.Children.Add(row);
        }
    }

    private void ApplySurfaceBrushes()
    {
        RootFrame.BorderBrush = new SolidColorBrush(Colors.Transparent);

        var subBg = new SolidColorBrush(_isDark ? Color.FromArgb(56, 0, 0, 0) : Color.FromArgb(152, 255, 255, 255));
        var subBorder = Win32WindowHelper.CreateGlassBorderBrush(_isDark, 44, 10);

        foreach (var card in new[] { CitySearchPanel, HourlyCard, DailyCard, WindCard, AqiCard })
        {
            card.Background = subBg;
            card.BorderBrush = subBorder;
        }
    }

    private void SyncPinIcon()
    {
        PinGlyph.Glyph = _config.PinWeatherFlyout ? "\uE840" : "\uE718";
        Win32WindowHelper.SetAlwaysOnTop(this, true);
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        RefreshBtn.IsEnabled = false;
        await _bridge.RefreshWeatherAsync();
        RefreshBtn.IsEnabled = true;
    }

    private void OnToggleCityPanelClick(object sender, RoutedEventArgs e)
    {
        CitySearchPanel.Visibility = CitySearchPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (CitySearchPanel.Visibility == Visibility.Visible)
            RenderFavoriteCities();
    }

    private void RenderFavoriteCities()
    {
        CityResultsList.Children.Clear();
        var favs = _bridge.CurrentState.Settings.Favorites;
        if (favs.Count == 0) return;

        CityResultsList.Children.Add(new TextBlock { Text = "已收藏城市", FontSize = 12, Opacity = 0.68 });
        foreach (var city in favs)
            CityResultsList.Children.Add(CreateCityResultButton(city));
    }

    private Button CreateCityResultButton(CityDto city)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = $"{city.Name}  ·  {city.Region}"
        };
        btn.Click += async (_, _) =>
        {
            CityLoadingBar.Visibility = Visibility.Visible;
            await _bridge.SelectCityAsync(city);
            CityLoadingBar.Visibility = Visibility.Collapsed;
            CitySearchPanel.Visibility = Visibility.Collapsed;
        };
        return btn;
    }

    private async void OnCityQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) await ExecuteCitySearchAsync();
    }

    private async void OnSearchCityClick(object sender, RoutedEventArgs e) => await ExecuteCitySearchAsync();

    private async System.Threading.Tasks.Task ExecuteCitySearchAsync()
    {
        var q = CityQueryBox.Text.Trim();
        if (string.IsNullOrEmpty(q)) return;

        CityLoadingBar.Visibility = Visibility.Visible;
        GpsHintText.Visibility = Visibility.Collapsed;
        try
        {
            var results = await _bridge.SearchCitiesAsync(q);
            CityResultsList.Children.Clear();
            if (results.Count == 0)
            {
                CityResultsList.Children.Add(new TextBlock { Text = "未找到匹配城市", FontSize = 12, Opacity = 0.7 });
            }
            else
            {
                foreach (var city in results.Take(6))
                    CityResultsList.Children.Add(CreateCityResultButton(city));
            }
        }
        catch (Exception ex)
        {
            GpsHintText.Text = $"搜索失败：{ex.Message}";
            GpsHintText.Visibility = Visibility.Visible;
        }
        finally
        {
            CityLoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnLocateGpsClick(object sender, RoutedEventArgs e)
    {
        CityLoadingBar.Visibility = Visibility.Visible;
        var (ok, msg) = await _bridge.LocateGpsAsync();
        CityLoadingBar.Visibility = Visibility.Collapsed;
        GpsHintText.Text = msg;
        GpsHintText.Visibility = Visibility.Visible;
        if (ok) CitySearchPanel.Visibility = Visibility.Collapsed;
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        _config.PinWeatherFlyout = !_config.PinWeatherFlyout;
        _config.Save();
        SyncPinIcon();
    }

    private void OnHeroWeatherTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_bridge.IsMockMode)
            _bridge.CycleMockWeatherScene();
    }

    private void OnBackdropHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        WeatherBackdropHost.Clip = new RectangleGeometry
        {
            Rect = new global::Windows.Foundation.Rect(0, 0, Math.Max(0, e.NewSize.Width), Math.Max(0, e.NewSize.Height))
        };
        if (_orbSecondary != null)
            Canvas.SetLeft(_orbSecondary, Math.Max(140, e.NewSize.Width - 185));
    }

    private static (Color Primary, Color Secondary, Color AccentTint, WeatherMood Mood) GetWeatherAtmosphere(int wmoCode, bool isNight)
    {
        if (wmoCode is >= 95 and <= 99)
            return (Color.FromArgb(255, 139, 92, 246), Color.FromArgb(255, 56, 189, 248), Color.FromArgb(255, 124, 58, 237), WeatherMood.Storm);
        if (wmoCode is (>= 71 and <= 77) or 85 or 86)
            return (Color.FromArgb(255, 186, 230, 253), Color.FromArgb(255, 125, 211, 252), Color.FromArgb(255, 56, 189, 248), WeatherMood.Snow);
        if (wmoCode is (>= 51 and <= 67) or (>= 80 and <= 82))
            return (Color.FromArgb(255, 14, 165, 233), Color.FromArgb(255, 59, 130, 246), Color.FromArgb(255, 2, 132, 199), WeatherMood.Rain);
        if (wmoCode is 45 or 48)
            return (Color.FromArgb(255, 148, 163, 184), Color.FromArgb(255, 203, 213, 225), Color.FromArgb(255, 100, 116, 139), WeatherMood.Fog);
        if (wmoCode <= 1 && !isNight)
            return (Color.FromArgb(255, 251, 191, 36), Color.FromArgb(255, 251, 146, 60), Color.FromArgb(255, 245, 158, 11), WeatherMood.Sunny);
        if (wmoCode <= 1 && isNight)
            return (Color.FromArgb(255, 56, 189, 248), Color.FromArgb(255, 99, 102, 241), Color.FromArgb(255, 79, 70, 229), WeatherMood.ClearNight);
        return isNight
            ? (Color.FromArgb(255, 59, 130, 246), Color.FromArgb(255, 100, 116, 139), Color.FromArgb(255, 30, 64, 175), WeatherMood.Cloudy)
            : (Color.FromArgb(255, 96, 165, 250), Color.FromArgb(255, 148, 163, 184), Color.FromArgb(255, 59, 130, 246), WeatherMood.Cloudy);
    }

    private void ApplyWeatherAmbientGlow(Color primary, Color secondary, WeatherMood mood, int wmoCode, bool isNight, double glassOpacity)
    {
        _currentMood = mood;
        double intensity = Math.Clamp(_glowStrength, 0.15, 1.0);
        byte topAlpha = (byte)Math.Clamp((_isDark ? 82 : 58) * (0.45 + intensity * 0.65), 22, 118);
        byte midAlpha = (byte)Math.Clamp((_isDark ? 42 : 28) * (0.45 + intensity * 0.65), 10, 62);

        var skyGrad = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0),
            EndPoint = new global::Windows.Foundation.Point(0.88, 0.80)
        };
        if (mood == WeatherMood.ClearNight)
        {
            // Midnight zenith into deep indigo twilight (Apple Weather night palette)
            skyGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb((byte)Math.Clamp(topAlpha + 36, 48, 148), 11, 19, 43), Offset = 0.0 });
            skyGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb((byte)Math.Clamp(midAlpha + 18, 24, 92), 30, 27, 75), Offset = 0.42 });
            skyGrad.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 0.88 });
        }
        else if (mood is WeatherMood.Rain or WeatherMood.Storm)
        {
            skyGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(topAlpha, primary.R, primary.G, primary.B), Offset = 0.0 });
            skyGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(midAlpha, secondary.R, secondary.G, secondary.B), Offset = 0.40 });
            skyGrad.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 0.86 });
        }
        else
        {
            skyGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(topAlpha, primary.R, primary.G, primary.B), Offset = 0.0 });
            skyGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(midAlpha, secondary.R, secondary.G, secondary.B), Offset = 0.36 });
            skyGrad.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 0.80 });
        }
        WeatherAmbientLayer.Background = skyGrad;

        var lightningBrush = new RadialGradientBrush
        {
            Center = new global::Windows.Foundation.Point(0.55, 0.12),
            GradientOrigin = new global::Windows.Foundation.Point(0.55, 0.12),
            RadiusX = 0.78,
            RadiusY = 0.64
        };
        lightningBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(145, 224, 242, 254), Offset = 0.0 });
        lightningBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(72, 129, 140, 248), Offset = 0.45 });
        lightningBrush.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 1.0 });
        WeatherLightningOverlay.Background = lightningBrush;
        WeatherLightningOverlay.Opacity = 0;

        var haloGrad = new RadialGradientBrush
        {
            Center = new global::Windows.Foundation.Point(0.5, 0.5),
            GradientOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        haloGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb((byte)Math.Clamp((_isDark ? 98 : 74) * (0.45 + intensity * 0.65), 22, 118), primary.R, primary.G, primary.B), Offset = 0.0 });
        haloGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb((byte)Math.Clamp((_isDark ? 38 : 26) * (0.45 + intensity * 0.65), 8, 52), secondary.R, secondary.G, secondary.B), Offset = 0.55 });
        haloGrad.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 1.0 });
        HeroGlowHalo.Background = haloGrad;

        string key = $"{mood}|{_isDark}|{Math.Round(intensity, 1)}";
        if (key != _lastAtmosphereKey)
        {
            _lastAtmosphereKey = key;
            RebuildAmbientLayer(primary, secondary, mood, intensity);
        }

        if (IsWindowVisible && _animationsEnabled)
        {
            if (!_ambientTimer.IsEnabled)
                _ambientTimer.Start();
        }
        else
        {
            _ambientTimer.Stop();
        }
    }

    private static RadialGradientBrush CreateOrbBrush(Color c, byte centerAlpha, byte midAlpha)
    {
        var b = new RadialGradientBrush
        {
            Center = new global::Windows.Foundation.Point(0.5, 0.5),
            GradientOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        b.GradientStops.Add(new GradientStop { Color = Color.FromArgb(centerAlpha, c.R, c.G, c.B), Offset = 0.0 });
        b.GradientStops.Add(new GradientStop { Color = Color.FromArgb(midAlpha, c.R, c.G, c.B), Offset = 0.52 });
        b.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 1.0 });
        return b;
    }

    private void RebuildAmbientLayer(Color primary, Color secondary, WeatherMood mood, double intensity)
    {
        WeatherAuraCanvas.Children.Clear();
        _particles.Clear();

        double w = Math.Max(360, _config.WeatherFlyoutWidth);
        double visScale = Math.Clamp(0.55 + intensity * 0.55, 0.55, 1.12);
        byte o1Center = (byte)Math.Clamp((_isDark ? 90 : 66) * visScale, 24, 114);
        byte o1Mid = (byte)Math.Clamp(o1Center * 0.42, 10, 54);
        byte o2Center = (byte)Math.Clamp((_isDark ? 72 : 52) * visScale, 18, 92);
        byte o2Mid = (byte)Math.Clamp(o2Center * 0.38, 8, 46);

        _orbPrimary = new Ellipse { Width = 290, Height = 290, Fill = CreateOrbBrush(primary, o1Center, o1Mid) };
        Canvas.SetLeft(_orbPrimary, -55);
        Canvas.SetTop(_orbPrimary, -65);
        WeatherAuraCanvas.Children.Add(_orbPrimary);

        _orbSecondary = new Ellipse { Width = 240, Height = 240, Fill = CreateOrbBrush(secondary, o2Center, o2Mid) };
        Canvas.SetLeft(_orbSecondary, w - 185);
        Canvas.SetTop(_orbSecondary, 14);
        WeatherAuraCanvas.Children.Add(_orbSecondary);

        var rng = new Random((int)mood * 997 + 42);

        if (mood == WeatherMood.ClearNight)
        {
            BuildClearNightField(w, visScale, rng);
        }
        else if (mood is WeatherMood.Rain or WeatherMood.Storm)
        {
            BuildRainOnGlassField(w, visScale, primary, mood == WeatherMood.Storm, rng);
        }
        else if (mood == WeatherMood.Snow)
        {
            BuildSnowField(w, visScale, rng);
        }
        else if (mood == WeatherMood.Sunny)
        {
            BuildSunnyField(w, visScale, primary, secondary, rng);
        }
        else
        {
            BuildCloudOrFogField(w, visScale, primary, mood == WeatherMood.Fog, rng);
        }
    }

    private void BuildClearNightField(double w, double visScale, Random rng)
    {
        // Soft lunar aura halo in upper right
        var moonGlow = new Ellipse
        {
            Width = 110,
            Height = 110,
            Fill = CreateOrbBrush(Color.FromArgb(255, 224, 231, 255), 52, 18)
        };
        Canvas.SetLeft(moonGlow, w - 118);
        Canvas.SetTop(moonGlow, -18);
        WeatherAuraCanvas.Children.Add(moonGlow);

        var starColors = new[]
        {
            Color.FromArgb(255, 248, 250, 252),
            Color.FromArgb(255, 224, 242, 254),
            Color.FromArgb(255, 199, 210, 254),
            Color.FromArgb(255, 254, 243, 199)
        };

        // 32 Multi-depth twinkling stars (including 6 diamond cross-glints)
        for (int i = 0; i < 32; i++)
        {
            bool isSparkle = i < 6;
            double x = 10 + rng.NextDouble() * (w - 20);
            // Bias slightly toward upper Milky Way band while keeping natural scatter
            double y = i < 20 ? 6 + rng.NextDouble() * 190 : 120 + rng.NextDouble() * 170;
            var col = starColors[rng.Next(starColors.Length)];
            FrameworkElement visual;
            double baseOp;

            if (isSparkle)
            {
                double sz = 7 + rng.NextDouble() * 4;
                var grid = new Grid { Width = sz, Height = sz };
                grid.Children.Add(new Ellipse
                {
                    Width = sz * 0.9,
                    Height = sz * 0.9,
                    Fill = CreateOrbBrush(col, 90, 24)
                });
                grid.Children.Add(new Border
                {
                    Width = sz,
                    Height = 1.2,
                    CornerRadius = new CornerRadius(1),
                    Background = new SolidColorBrush(col),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                grid.Children.Add(new Border
                {
                    Width = 1.2,
                    Height = sz,
                    CornerRadius = new CornerRadius(1),
                    Background = new SolidColorBrush(col),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                visual = grid;
                baseOp = Math.Clamp((0.55 + rng.NextDouble() * 0.35) * visScale, 0.35, 0.92);
            }
            else
            {
                double sz = i < 18 ? 1.8 + rng.NextDouble() * 1.4 : 1.1 + rng.NextDouble() * 0.8;
                visual = new Ellipse
                {
                    Width = sz,
                    Height = sz,
                    Fill = new SolidColorBrush(col)
                };
                baseOp = Math.Clamp((0.28 + rng.NextDouble() * 0.50) * visScale, 0.22, 0.82);
            }

            visual.Opacity = baseOp;
            Canvas.SetLeft(visual, x);
            Canvas.SetTop(visual, y);
            WeatherAuraCanvas.Children.Add(visual);
            _particles.Add(new AmbientParticle
            {
                Kind = AmbientKind.Star,
                Visual = visual,
                X = x,
                Y = y,
                Phase = rng.NextDouble() * Math.PI * 2,
                Speed = 1.5 + rng.NextDouble() * 2.4,
                BaseOpacity = baseOp
            });
        }

        // 2 Shooting star / meteor streaks (periodic across upper sky)
        for (int m = 0; m < 2; m++)
        {
            double len = 72 + m * 16;
            var grad = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0, 0.5),
                EndPoint = new global::Windows.Foundation.Point(1, 0.5)
            };
            grad.GradientStops.Add(new GradientStop { Color = Colors.White, Offset = 0.0 });
            grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(165, 186, 230, 253), Offset = 0.28 });
            grad.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 1.0 });

            var meteor = new Border
            {
                Width = len,
                Height = 2.0,
                CornerRadius = new CornerRadius(1),
                Background = grad,
                Opacity = 0,
                RenderTransformOrigin = new global::Windows.Foundation.Point(0, 0.5),
                RenderTransform = new RotateTransform { Angle = -24 }
            };
            Canvas.SetLeft(meteor, w * 0.8);
            Canvas.SetTop(meteor, 24);
            WeatherAuraCanvas.Children.Add(meteor);
            _particles.Add(new AmbientParticle
            {
                Kind = AmbientKind.Meteor,
                Visual = meteor,
                X = w * 0.85,
                Y = 20 + m * 46,
                Vx = -7.8,
                Vy = 3.45,
                Period = 6.4 + m * 3.1,
                Timer = m * 3.3,
                BaseOpacity = 0.92
            });
        }
    }

    private void BuildRainOnGlassField(double w, double visScale, Color primary, bool isStorm, Random rng)
    {
        // Layer 1: Atmospheric slanted rainfall streaks behind the glass
        int streakCount = isStorm ? 24 : 18;
        for (int i = 0; i < streakCount; i++)
        {
            double len = (isStorm ? 20 : 14) + rng.NextDouble() * 14;
            var streakGrad = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0.5, 0),
                EndPoint = new global::Windows.Foundation.Point(0.5, 1)
            };
            streakGrad.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 0.0 });
            streakGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(170, 125, 211, 252), Offset = 0.68 });
            streakGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(235, 240, 249, 255), Offset = 1.0 });

            var streak = new Border
            {
                Width = isStorm ? 1.7 : 1.4,
                Height = len,
                CornerRadius = new CornerRadius(1),
                Background = streakGrad,
                RenderTransform = new RotateTransform { Angle = isStorm ? 16 : 12 }
            };
            double x = rng.NextDouble() * w;
            double y = rng.NextDouble() * 340;
            double baseOp = Math.Clamp((0.20 + rng.NextDouble() * 0.24) * visScale, 0.16, 0.50);
            streak.Opacity = baseOp;
            Canvas.SetLeft(streak, x);
            Canvas.SetTop(streak, y);
            WeatherAuraCanvas.Children.Add(streak);
            _particles.Add(new AmbientParticle
            {
                Kind = AmbientKind.RainStreak,
                Visual = streak,
                X = x,
                Y = y,
                Vx = isStorm ? -1.8 : -1.05,
                Vy = (isStorm ? 7.2 : 5.0) + rng.NextDouble() * 3.2,
                BaseOpacity = baseOp
            });
        }

        // Layer 2: Glass condensation droplets + fading sliding water trails
        int dropletCount = isStorm ? 20 : 16;
        byte rimShadowAlpha = _isDark ? (byte)64 : (byte)56;
        for (int i = 0; i < dropletCount; i++)
        {
            double dropW = 4.6 + rng.NextDouble() * 5.2;
            double dropH = dropW * 1.18;
            double x = 8 + rng.NextDouble() * (w - 22);
            double y = 8 + rng.NextDouble() * 290;

            var trailGrad = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0.5, 0),
                EndPoint = new global::Windows.Foundation.Point(0.5, 1)
            };
            trailGrad.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 0.0 });
            trailGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(_isDark ? (byte)55 : (byte)45, 186, 230, 253), Offset = 0.72 });
            trailGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(_isDark ? (byte)96 : (byte)75, 224, 242, 254), Offset = 1.0 });

            var trail = new Border
            {
                Width = Math.Max(1.3, dropW * 0.34),
                Height = 0,
                CornerRadius = new CornerRadius(1),
                Background = trailGrad,
                Opacity = 0
            };
            Canvas.SetLeft(trail, x + dropW * 0.33);
            Canvas.SetTop(trail, y);
            WeatherAuraCanvas.Children.Add(trail);

            var dropGrid = new Grid { Width = dropW, Height = dropH };
            var bodyBrush = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0.2, 0.1),
                EndPoint = new global::Windows.Foundation.Point(0.8, 0.95)
            };
            bodyBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(_isDark ? (byte)28 : (byte)22, 255, 255, 255), Offset = 0.0 });
            bodyBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(_isDark ? (byte)72 : (byte)58, 125, 211, 252), Offset = 0.78 });
            bodyBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(_isDark ? (byte)115 : (byte)92, 224, 242, 254), Offset = 1.0 });

            dropGrid.Children.Add(new Ellipse
            {
                Fill = bodyBrush,
                Stroke = new SolidColorBrush(Color.FromArgb(rimShadowAlpha, 15, 23, 42)),
                StrokeThickness = 0.65
            });
            // Bottom lens refraction caustic highlight
            dropGrid.Children.Add(new Ellipse
            {
                Width = dropW * 0.56,
                Height = dropH * 0.24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, Math.Max(0.8, dropH * 0.12)),
                Fill = new SolidColorBrush(Color.FromArgb(145, 224, 242, 254))
            });
            // Top-left crisp specular highlight pinpoint
            double glint = Math.Max(1.4, dropW * 0.26);
            dropGrid.Children.Add(new Ellipse
            {
                Width = glint,
                Height = glint,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(dropW * 0.20, dropH * 0.18, 0, 0),
                Fill = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255))
            });

            double baseOp = Math.Clamp((0.46 + rng.NextDouble() * 0.34) * visScale, 0.38, 0.88);
            bool startSliding = i % 3 == 0;
            dropGrid.Opacity = baseOp;
            Canvas.SetLeft(dropGrid, x);
            Canvas.SetTop(dropGrid, y);
            WeatherAuraCanvas.Children.Add(dropGrid);

            _particles.Add(new AmbientParticle
            {
                Kind = AmbientKind.GlassDroplet,
                Visual = dropGrid,
                TrailVisual = trail,
                X = x,
                Y = y,
                Size = dropW,
                Vx = 0,
                Vy = startSliding ? 0.35 + rng.NextDouble() * 0.65 : 0,
                Phase = rng.NextDouble() * Math.PI * 2,
                BaseOpacity = baseOp,
                State = startSliding ? 1 : 0,
                Timer = rng.NextDouble() * 3.5,
                Period = 1.6 + rng.NextDouble() * 3.8,
                TrailHeight = 0
            });
        }

        // Layer 3: Expanding glass impact splash ripples (raindrops hitting the glass!)
        int splashCount = isStorm ? 5 : 4;
        for (int s = 0; s < splashCount; s++)
        {
            var scale = new ScaleTransform { ScaleX = 0.25, ScaleY = 0.25 };
            var splashGrid = new Grid
            {
                Width = 28,
                Height = 28,
                Opacity = 0,
                RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = scale
            };
            splashGrid.Children.Add(new Ellipse
            {
                Stroke = new SolidColorBrush(Color.FromArgb(200, 186, 230, 253)),
                StrokeThickness = 1.25
            });
            splashGrid.Children.Add(new Ellipse
            {
                Width = 4.5,
                Height = 4.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255))
            });

            double sx = 24 + rng.NextDouble() * (w - 64);
            double sy = 20 + rng.NextDouble() * 240;
            Canvas.SetLeft(splashGrid, sx);
            Canvas.SetTop(splashGrid, sy);
            WeatherAuraCanvas.Children.Add(splashGrid);

            _particles.Add(new AmbientParticle
            {
                Kind = AmbientKind.GlassSplash,
                Visual = splashGrid,
                Scale = scale,
                X = sx,
                Y = sy,
                Phase = s * 1.7 + rng.NextDouble(),
                Period = isStorm ? 0.72 + s * 0.14 : 0.95 + s * 0.18,
                Timer = s * 0.26,
                BaseOpacity = Math.Clamp(0.68 * visScale, 0.45, 0.85)
            });
        }
    }

    private void BuildSnowField(double w, double visScale, Random rng)
    {
        for (int i = 0; i < 28; i++)
        {
            double x = rng.NextDouble() * w;
            double y = rng.NextDouble() * 340;
            FrameworkElement visual;
            RotateTransform? rot = null;

            if (i < 6)
            {
                rot = new RotateTransform { Angle = rng.NextDouble() * 360 };
                visual = new TextBlock
                {
                    Text = "❄",
                    FontSize = 10 + rng.NextDouble() * 4,
                    Foreground = new SolidColorBrush(_isDark ? Color.FromArgb(230, 240, 249, 255) : Color.FromArgb(220, 56, 189, 248)),
                    RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
                    RenderTransform = rot
                };
            }
            else if (i < 10)
            {
                double sz = 10 + rng.NextDouble() * 10;
                visual = new Ellipse
                {
                    Width = sz,
                    Height = sz,
                    Fill = CreateOrbBrush(_isDark ? Colors.White : Color.FromArgb(255, 125, 211, 252), 95, 28)
                };
            }
            else
            {
                double sz = 2.4 + rng.NextDouble() * 3.0;
                visual = new Ellipse
                {
                    Width = sz,
                    Height = sz,
                    Fill = new SolidColorBrush(_isDark ? Colors.White : Color.FromArgb(255, 186, 230, 253))
                };
            }

            double baseOp = Math.Clamp((0.28 + rng.NextDouble() * 0.36) * visScale, 0.22, 0.72);
            visual.Opacity = baseOp;
            Canvas.SetLeft(visual, x);
            Canvas.SetTop(visual, y);
            WeatherAuraCanvas.Children.Add(visual);
            _particles.Add(new AmbientParticle
            {
                Kind = AmbientKind.Snow,
                Visual = visual,
                Rotate = rot,
                X = x,
                Y = y,
                Vx = -0.16 + rng.NextDouble() * 0.32,
                Vy = 0.52 + rng.NextDouble() * 0.65,
                Phase = rng.NextDouble() * Math.PI * 2,
                Speed = 0.4 + rng.NextDouble() * 0.8,
                BaseOpacity = baseOp
            });
        }
    }

    private void BuildSunnyField(double w, double visScale, Color primary, Color secondary, Random rng)
    {
        // Volumetric diagonal warm sunbeams anchored off top-left / hero corner
        for (int b = 0; b < 3; b++)
        {
            var beamGrad = new LinearGradientBrush
            {
                StartPoint = new global::Windows.Foundation.Point(0.5, 0),
                EndPoint = new global::Windows.Foundation.Point(0.5, 1)
            };
            beamGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(110, 254, 240, 138), Offset = 0.0 });
            beamGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(46, primary.R, primary.G, primary.B), Offset = 0.48 });
            beamGrad.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 1.0 });

            var rot = new RotateTransform { Angle = -28 + b * 9 };
            var beam = new Border
            {
                Width = 64 + b * 18,
                Height = 320,
                CornerRadius = new CornerRadius(24),
                Background = beamGrad,
                RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0),
                RenderTransform = rot
            };
            double bx = 18 + b * 68;
            double by = -45;
            double baseOp = Math.Clamp((0.28 - b * 0.05) * visScale, 0.16, 0.42);
            beam.Opacity = baseOp;
            Canvas.SetLeft(beam, bx);
            Canvas.SetTop(beam, by);
            WeatherAuraCanvas.Children.Add(beam);
            _particles.Add(new AmbientParticle
            {
                Kind = AmbientKind.Sunbeam,
                Visual = beam,
                Rotate = rot,
                X = -28 + b * 9,
                Phase = b * 1.4,
                BaseOpacity = baseOp
            });
        }

        // Drifting golden sunlit dust bokeh motes
        for (int i = 0; i < 12; i++)
        {
            double sz = 10 + rng.NextDouble() * 18;
            double x = rng.NextDouble() * w;
            double y = 20 + rng.NextDouble() * 240;
            var orb = new Ellipse
            {
                Width = sz,
                Height = sz,
                Fill = CreateOrbBrush(i % 2 == 0 ? primary : secondary, (byte)(_isDark ? 92 : 68), 18)
            };
            double baseOp = Math.Clamp((0.32 + rng.NextDouble() * 0.28) * visScale, 0.22, 0.68);
            orb.Opacity = baseOp;
            Canvas.SetLeft(orb, x);
            Canvas.SetTop(orb, y);
            WeatherAuraCanvas.Children.Add(orb);
            _particles.Add(new AmbientParticle
            {
                Kind = AmbientKind.SunDust,
                Visual = orb,
                X = x,
                Y = y,
                Vx = 0.14 + rng.NextDouble() * 0.18,
                Vy = -0.10 - rng.NextDouble() * 0.12,
                Phase = rng.NextDouble() * Math.PI * 2,
                BaseOpacity = baseOp
            });
        }
    }

    private void BuildCloudOrFogField(double w, double visScale, Color primary, bool isFog, Random rng)
    {
        int count = isFog ? 9 : 7;
        for (int i = 0; i < count; i++)
        {
            double cw = (isFog ? 140 : 100) + rng.NextDouble() * 90;
            double ch = (isFog ? 46 : 36) + rng.NextDouble() * 22;
            double x = rng.NextDouble() * (w + 60) - 40;
            double y = isFog ? 10 + rng.NextDouble() * 250 : 6 + rng.NextDouble() * 190;
            var puff = new Ellipse
            {
                Width = cw,
                Height = ch,
                Fill = CreateOrbBrush(primary, (byte)(_isDark ? 68 : 48), 16)
            };
            double baseOp = Math.Clamp((0.34 + rng.NextDouble() * 0.24) * visScale, 0.24, 0.66);
            puff.Opacity = baseOp;
            Canvas.SetLeft(puff, x);
            Canvas.SetTop(puff, y);
            WeatherAuraCanvas.Children.Add(puff);
            _particles.Add(new AmbientParticle
            {
                Kind = AmbientKind.CloudMist,
                Visual = puff,
                X = x,
                Y = y,
                Vx = (isFog ? 0.16 : 0.24) + rng.NextDouble() * 0.22,
                Vy = 0,
                Phase = rng.NextDouble() * Math.PI * 2,
                BaseOpacity = baseOp
            });
        }
    }

    private void OnAmbientTick(object? sender, object e)
    {
        if (!IsWindowVisible || !_animationsEnabled)
        {
            _ambientTimer.Stop();
            return;
        }

        const double dt = 0.033;
        _ambientTick += dt;
        double w = WeatherAuraCanvas.ActualWidth > 40 ? WeatherAuraCanvas.ActualWidth : _config.WeatherFlyoutWidth;
        double maxY = Math.Max(260, (WeatherAuraCanvas.ActualHeight > 60 ? WeatherAuraCanvas.ActualHeight : 540) * 0.64);

        if (_orbPrimary != null)
        {
            Canvas.SetLeft(_orbPrimary, -55 + Math.Sin(_ambientTick * 0.55) * 18);
            Canvas.SetTop(_orbPrimary, -65 + Math.Cos(_ambientTick * 0.42) * 12);
        }
        if (_orbSecondary != null)
        {
            double baseRight = Math.Max(140, w - 185);
            Canvas.SetLeft(_orbSecondary, baseRight + Math.Cos(_ambientTick * 0.48) * 16);
            Canvas.SetTop(_orbSecondary, 14 + Math.Sin(_ambientTick * 0.62) * 14);
        }

        HeroGlowHalo.Opacity = 0.72 + Math.Sin(_ambientTick * 1.4) * 0.22;

        // Multi-stage volumetric lightning flashes during storms
        if (_currentMood == WeatherMood.Storm)
        {
            double stormCycle = _ambientTick % 5.8;
            double flash = 0;
            if (stormCycle is > 4.20 and < 4.31) flash = 0.52;
            else if (stormCycle is > 4.36 and < 4.50) flash = 0.36;
            else if (stormCycle is >= 4.50 and < 4.92) flash = 0.36 * (1.0 - (stormCycle - 4.50) / 0.42);
            WeatherLightningOverlay.Opacity = flash;
            WeatherAmbientLayer.Opacity = 0.85 + flash * 0.25;
        }
        else
        {
            WeatherLightningOverlay.Opacity = 0;
            WeatherAmbientLayer.Opacity = _currentMood == WeatherMood.ClearNight ? 0.94 : 0.88;
        }

        foreach (var p in _particles)
        {
            switch (p.Kind)
            {
                case AmbientKind.Star:
                    p.Visual.Opacity = Math.Clamp(p.BaseOpacity * (0.42 + 0.58 * Math.Sin(_ambientTick * p.Speed + p.Phase)), 0.08, 0.95);
                    break;

                case AmbientKind.Meteor:
                    AnimateMeteor(p, w, dt);
                    break;

                case AmbientKind.RainStreak:
                    AnimateRainStreak(p, w, maxY);
                    break;

                case AmbientKind.GlassDroplet:
                    AnimateGlassDroplet(p, w, maxY, dt);
                    break;

                case AmbientKind.GlassSplash:
                    AnimateGlassSplash(p, w, maxY, dt);
                    break;

                case AmbientKind.Snow:
                    double sway = Math.Sin(_ambientTick * 1.65 + p.Phase) * 0.38;
                    p.X += p.Vx + sway;
                    p.Y += p.Vy;
                    if (p.Rotate != null) p.Rotate.Angle = (_ambientTick * 28 * p.Speed + p.Phase * 40) % 360;
                    WrapParticle(p, w, maxY);
                    p.Visual.Opacity = p.BaseOpacity * Math.Clamp(1.0 - (p.Y / maxY) * 0.82, 0.08, 1.0);
                    Canvas.SetLeft(p.Visual, p.X);
                    Canvas.SetTop(p.Visual, p.Y);
                    break;

                case AmbientKind.Sunbeam:
                    if (p.Rotate != null) p.Rotate.Angle = p.X + Math.Sin(_ambientTick * 0.55 + p.Phase) * 2.2;
                    p.Visual.Opacity = Math.Clamp(p.BaseOpacity * (0.72 + 0.28 * Math.Sin(_ambientTick * 0.85 + p.Phase)), 0.08, 0.52);
                    break;

                case AmbientKind.SunDust:
                    p.X += p.Vx + Math.Sin(_ambientTick * 0.9 + p.Phase) * 0.10;
                    p.Y += p.Vy;
                    WrapParticle(p, w, maxY);
                    p.Visual.Opacity = p.BaseOpacity * (0.62 + 0.38 * Math.Sin(_ambientTick * 1.3 + p.Phase));
                    Canvas.SetLeft(p.Visual, p.X);
                    Canvas.SetTop(p.Visual, p.Y);
                    break;

                case AmbientKind.CloudMist:
                    p.X += p.Vx;
                    if (p.X > w + 60) p.X = -180;
                    p.Visual.Opacity = p.BaseOpacity * (0.75 + 0.25 * Math.Sin(_ambientTick * 0.5 + p.Phase));
                    Canvas.SetLeft(p.Visual, p.X);
                    break;
            }
        }
    }

    private static void AnimateMeteor(AmbientParticle p, double w, double dt)
    {
        p.Timer += dt;
        if (p.Timer >= p.Period)
        {
            p.Timer = 0;
            // Deterministic varied lane across top-right/top-mid sky
            double lane = ((_nextLaneSeed(p) * 0.37 + 0.45) % 0.52) + 0.42;
            p.X = w * lane;
            p.Y = 12 + (p.Period * 9 % 62);
        }

        const double duration = 0.62;
        if (p.Timer <= duration)
        {
            p.X += p.Vx;
            p.Y += p.Vy;
            double t = p.Timer / duration;
            double envelope = t < 0.2 ? t / 0.2 : Math.Clamp(1.0 - (t - 0.2) / 0.8, 0, 1);
            p.Visual.Opacity = p.BaseOpacity * envelope;
            Canvas.SetLeft(p.Visual, p.X);
            Canvas.SetTop(p.Visual, p.Y);
        }
        else
        {
            p.Visual.Opacity = 0;
        }
    }

    private static double _nextLaneSeed(AmbientParticle p) => p.Y * 0.13 + p.Period;

    private static void AnimateRainStreak(AmbientParticle p, double w, double maxY)
    {
        p.X += p.Vx;
        p.Y += p.Vy;
        WrapParticle(p, w, maxY);
        double fadeBottom = Math.Clamp(1.0 - (p.Y / maxY) * 0.86, 0.04, 1.0);
        p.Visual.Opacity = p.BaseOpacity * fadeBottom;
        Canvas.SetLeft(p.Visual, p.X);
        Canvas.SetTop(p.Visual, p.Y);
    }

    private static void AnimateGlassDroplet(AmbientParticle p, double w, double maxY, double dt)
    {
        p.Timer += dt;

        if (p.State == 0)
        {
            // Beading/clinging on the glass surface, subtly swelling until tension breaks
            double swell = 0.55 + 0.45 * Math.Min(1.0, p.Timer / Math.Max(0.4, p.Period));
            p.Visual.Opacity = p.BaseOpacity * swell;
            if (p.TrailVisual != null) p.TrailVisual.Opacity = 0;

            if (p.Timer >= p.Period)
            {
                p.State = 1;
                p.Timer = 0;
                p.Vy = 0.42;
                p.TrailHeight = 0;
            }
        }
        else if (p.State == 1)
        {
            // Sliding down glass with organic stick-slip surface-tension micro-wobble
            double slipMod = 0.72 + 0.55 * Math.Sin(p.Timer * 7.5 + p.Phase);
            p.Vy = Math.Min(2.3, p.Vy + dt * 0.95);
            double stepY = p.Vy * Math.Max(0.25, slipMod);
            double stepX = Math.Sin(p.Timer * 5.2 + p.Phase) * 0.28;

            p.X = Math.Clamp(p.X + stepX, 4, Math.Max(12, w - 16));
            p.Y += stepY;
            p.TrailHeight = Math.Min(78, p.TrailHeight + stepY * 0.92);

            double fadeBottom = Math.Clamp(1.0 - (p.Y / maxY) * 0.88, 0.0, 1.0);
            p.Visual.Opacity = p.BaseOpacity * fadeBottom;
            Canvas.SetLeft(p.Visual, p.X);
            Canvas.SetTop(p.Visual, p.Y);

            if (p.TrailVisual != null)
            {
                p.TrailVisual.Height = Math.Max(0, p.TrailHeight);
                p.TrailVisual.Opacity = p.BaseOpacity * 0.68 * fadeBottom;
                Canvas.SetLeft(p.TrailVisual, p.X + p.Size * 0.34);
                Canvas.SetTop(p.TrailVisual, p.Y - p.TrailHeight + p.Size * 0.25);
            }

            if (p.Y > maxY || p.Timer > 3.8)
            {
                // Respawn cleanly as a fresh condensing bead higher on the pane
                p.State = 0;
                p.Timer = 0;
                p.TrailHeight = 0;
                p.Y = 8 + ((p.X * 1.7 + p.Period * 31) % Math.Max(80, maxY * 0.48));
                p.X = 8 + ((p.X + w * 0.39) % Math.Max(40, w - 22));
                Canvas.SetLeft(p.Visual, p.X);
                Canvas.SetTop(p.Visual, p.Y);
                if (p.TrailVisual != null)
                {
                    p.TrailVisual.Height = 0;
                    p.TrailVisual.Opacity = 0;
                }
            }
        }
    }

    private static void AnimateGlassSplash(AmbientParticle p, double w, double maxY, double dt)
    {
        p.Timer += dt;
        if (p.Timer >= p.Period)
        {
            p.Timer = 0;
            p.X = 16 + ((p.X + w * 0.43 + p.Phase * 29) % Math.Max(40, w - 52));
            p.Y = 14 + ((p.Y + 79 + p.Phase * 17) % Math.Max(80, maxY * 0.68));
            Canvas.SetLeft(p.Visual, p.X);
            Canvas.SetTop(p.Visual, p.Y);
        }

        const double splashDuration = 0.42;
        if (p.Timer <= splashDuration && p.Scale != null)
        {
            double t = p.Timer / splashDuration;
            double easeOut = 1.0 - Math.Pow(1.0 - t, 2.2);
            double s = 0.25 + easeOut * 0.95;
            p.Scale.ScaleX = s;
            p.Scale.ScaleY = s;
            p.Visual.Opacity = p.BaseOpacity * (1.0 - t);
        }
        else
        {
            p.Visual.Opacity = 0;
        }
    }

    private static void WrapParticle(AmbientParticle p, double w, double maxY)
    {
        if (p.X < -40) p.X = w + 10;
        if (p.X > w + 40) p.X = -30;
        if (p.Y > maxY) { p.Y = -18; p.X = ((p.X + w * 0.37) % w); }
        if (p.Y < -30) { p.Y = maxY; }
    }
}
