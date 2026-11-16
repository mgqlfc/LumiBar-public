using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using Windows.UI;

namespace AntigravityFrontend.Sdk;

public sealed class CityDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("latitude")] public double Latitude { get; set; }
    [JsonPropertyName("longitude")] public double Longitude { get; set; }
    [JsonPropertyName("region")] public string Region { get; set; } = "";
    [JsonPropertyName("timeZone")] public string TimeZone { get; set; } = "";
}

public sealed class HourForecastDto
{
    [JsonPropertyName("time")] public string Time { get; set; } = "";
    [JsonPropertyName("temperature")] public double Temperature { get; set; }
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("rain")] public double Rain { get; set; }
    [JsonPropertyName("isDay")] public bool? IsDay { get; set; }
}

public sealed class RainIntervalDto
{
    [JsonPropertyName("start")] public string Start { get; set; } = "";
    [JsonPropertyName("end")] public string End { get; set; } = "";
    [JsonPropertyName("amountMm")] public double? AmountMm { get; set; }
}

public sealed class PrecipitationSeriesDto
{
    [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("precisionNote")] public string PrecisionNote { get; set; } = "";
    [JsonPropertyName("intervals")] public List<RainIntervalDto> Intervals { get; set; } = new();
}

public sealed class PrecipitationTrendDto
{
    [JsonPropertyName("windowStart")] public string WindowStart { get; set; } = "";
    [JsonPropertyName("windowEnd")] public string WindowEnd { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "model-trend";
    [JsonPropertyName("intervalMinutes")] public int IntervalMinutes { get; set; } = 15;
    [JsonPropertyName("precisionNote")] public string PrecisionNote { get; set; } = "";
    [JsonPropertyName("complete")] public bool Complete { get; set; } = true;
    [JsonPropertyName("intervals")] public List<RainIntervalDto> Intervals { get; set; } = new();
}

public sealed class DayForecastDto
{
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("min")] public double Min { get; set; }
    [JsonPropertyName("max")] public double Max { get; set; }
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("rain")] public double Rain { get; set; }
}

public sealed class ForecastDto
{
    [JsonPropertyName("temperature")] public double Temperature { get; set; }
    [JsonPropertyName("feelsLike")] public double FeelsLike { get; set; }
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("humidity")] public double Humidity { get; set; }
    [JsonPropertyName("wind")] public double Wind { get; set; }
    [JsonPropertyName("windDirection")] public double WindDirection { get; set; }
    [JsonPropertyName("rain")] public double? Rain { get; set; }
    [JsonPropertyName("hours")] public List<HourForecastDto> Hours { get; set; } = new();
    [JsonPropertyName("days")] public List<DayForecastDto> Days { get; set; } = new();
    [JsonPropertyName("updated")] public string Updated { get; set; } = "";
    [JsonPropertyName("aqi")] public double? Aqi { get; set; }
    [JsonPropertyName("pm25")] public double? Pm25 { get; set; }
    [JsonPropertyName("pm10")] public double? Pm10 { get; set; }
    [JsonPropertyName("airError")] public string? AirError { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("estimatedFeelsLike")] public bool EstimatedFeelsLike { get; set; }
    [JsonPropertyName("timeZoneLabel")] public string TimeZoneLabel { get; set; } = "";
    [JsonPropertyName("shortTermPrecipitation")] public PrecipitationSeriesDto? ShortTermPrecipitation { get; set; }
    [JsonPropertyName("sunrise")] public string? Sunrise { get; set; }
    [JsonPropertyName("sunset")] public string? Sunset { get; set; }
    [JsonPropertyName("isDay")] public bool? IsDay { get; set; }
}

public sealed class MediaControlsDto
{
    [JsonPropertyName("toggle")] public bool Toggle { get; set; }
    [JsonPropertyName("previous")] public bool Previous { get; set; }
    [JsonPropertyName("next")] public bool Next { get; set; }
    [JsonPropertyName("seek")] public bool Seek { get; set; }
    [JsonPropertyName("shuffle")] public bool? Shuffle { get; set; }
    [JsonPropertyName("repeat")] public bool? Repeat { get; set; }
}

public sealed class MediaDto
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("artist")] public string Artist { get; set; } = "";
    [JsonPropertyName("album")] public string Album { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("playing")] public bool Playing { get; set; }
    [JsonPropertyName("position")] public double Position { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("hasArtwork")] public bool HasArtwork { get; set; }
    [JsonPropertyName("accent")] public string Accent { get; set; } = "#60CDFF";
    [JsonPropertyName("controls")] public MediaControlsDto? Controls { get; set; }
    [JsonPropertyName("shuffleEnabled")] public bool? ShuffleEnabled { get; set; }
    [JsonPropertyName("repeatMode")] public string? RepeatMode { get; set; }
}

public sealed class LyricLineDto
{
    [JsonPropertyName("seconds")] public double? Seconds { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

public sealed class LyricsDto
{
    [JsonPropertyName("trackKey")] public string? TrackKey { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("offsetSeconds")] public double OffsetSeconds { get; set; }
    [JsonPropertyName("lines")] public List<LyricLineDto> Lines { get; set; } = new();
}

public sealed class AppearanceDto
{
    [JsonPropertyName("preference")] public string Preference { get; set; } = "System";
    [JsonPropertyName("popupTheme")] public string PopupTheme { get; set; } = "Dark";
    [JsonPropertyName("taskbarTheme")] public string TaskbarTheme { get; set; } = "Dark";
    [JsonPropertyName("coverGlow")] public bool CoverGlow { get; set; } = true;
    [JsonPropertyName("glow")] public double Glow { get; set; } = 0.28;
    [JsonPropertyName("glassOpacity")] public double GlassOpacity { get; set; } = 0.62;
    [JsonPropertyName("animations")] public bool Animations { get; set; } = true;
    [JsonPropertyName("fontFamily")] public string? FontFamily { get; set; }
}

public sealed class SettingsDto
{
    [JsonPropertyName("city")] public CityDto? City { get; set; }
    [JsonPropertyName("favorites")] public List<CityDto> Favorites { get; set; } = new();
    [JsonPropertyName("theme")] public string Theme { get; set; } = "System";
    [JsonPropertyName("glow")] public double Glow { get; set; } = 0.28;
    [JsonPropertyName("glassOpacity")] public double GlassOpacity { get; set; } = 0.62;
    [JsonPropertyName("coverGlow")] public bool CoverGlow { get; set; } = true;
    [JsonPropertyName("animations")] public bool Animations { get; set; } = true;
    [JsonPropertyName("showWeather")] public bool ShowWeather { get; set; } = true;
    [JsonPropertyName("showMusic")] public bool ShowMusic { get; set; } = true;
    [JsonPropertyName("spectrum")] public bool Spectrum { get; set; } = true;
    [JsonPropertyName("hideInFullscreen")] public bool HideInFullscreen { get; set; } = true;
    [JsonPropertyName("autoLyrics")] public bool AutoLyrics { get; set; } = true;
    [JsonPropertyName("autoStart")] public bool AutoStart { get; set; }
    [JsonPropertyName("closeToTray")] public bool CloseToTray { get; set; } = true;
    [JsonPropertyName("weatherRefreshMinutes")] public int WeatherRefreshMinutes { get; set; } = 20;
    [JsonPropertyName("fahrenheit")] public bool Fahrenheit { get; set; }
    [JsonPropertyName("windMetersPerSecond")] public bool WindMetersPerSecond { get; set; }
    [JsonPropertyName("gap")] public double Gap { get; set; } = 8;
    [JsonPropertyName("musicWidth")] public double MusicWidth { get; set; } = 238;
    [JsonPropertyName("lyricsFontSize")] public double LyricsFontSize { get; set; } = 22;
    [JsonPropertyName("lyricsOffset")] public double LyricsOffset { get; set; }
    [JsonPropertyName("sensitivity")] public double Sensitivity { get; set; } = 1.0;
    [JsonPropertyName("fontFamily")] public string? FontFamily { get; set; }
    [JsonPropertyName("enabledPlugins")] public List<string> EnabledPlugins { get; set; } = new();
}

public sealed class SystemFontItemDto
{
    [JsonPropertyName("family")] public string Family { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("aliases")] public List<string> Aliases { get; set; } = new();
}

public sealed class FontsListResponseDto
{
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("installedCount")] public int InstalledCount { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("hasMore")] public bool HasMore { get; set; }
    [JsonPropertyName("items")] public List<SystemFontItemDto> Items { get; set; } = new();
}

public sealed class WeatherSnapshotDto
{
    [JsonPropertyName("city")] public CityDto? City { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("forecast")] public ForecastDto? Forecast { get; set; }
    [JsonPropertyName("precipitationNextHour")] public PrecipitationTrendDto? PrecipitationNextHour { get; set; }
}

public sealed class StateDto
{
    [JsonPropertyName("protocolVersion")] public int ProtocolVersion { get; set; } = 1;
    [JsonPropertyName("capturedAt")] public string CapturedAt { get; set; } = "";
    [JsonPropertyName("stateVersion")] public long? StateVersion { get; set; }
    [JsonPropertyName("instanceId")] public string? InstanceId { get; set; }
    [JsonPropertyName("appearance")] public AppearanceDto Appearance { get; set; } = new();
    [JsonPropertyName("weather")] public WeatherSnapshotDto Weather { get; set; } = new();
    [JsonPropertyName("media")] public MediaDto? Media { get; set; }
    [JsonPropertyName("lyrics")] public LyricsDto Lyrics { get; set; } = new();
    [JsonPropertyName("settings")] public SettingsDto Settings { get; set; } = new();
}

public sealed class SpectrumDto
{
    [JsonPropertyName("capturedAt")] public string CapturedAt { get; set; } = "";
    [JsonPropertyName("playing")] public bool Playing { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = "system-output";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("bands")] public double[] Bands { get; set; } = new double[20];
}

public sealed class ArtworkDto
{
    [JsonPropertyName("trackKey")] public string TrackKey { get; set; } = "";
    [JsonPropertyName("mimeType")] public string MimeType { get; set; } = "";
    [JsonPropertyName("base64")] public string Base64 { get; set; } = "";
}

public sealed class RectDto
{
    [JsonPropertyName("left")] public int Left { get; set; }
    [JsonPropertyName("top")] public int Top { get; set; }
    [JsonPropertyName("right")] public int Right { get; set; }
    [JsonPropertyName("bottom")] public int Bottom { get; set; }
    [JsonPropertyName("visible")] public bool Visible { get; set; }
}

public sealed class SlotRectDto
{
    [JsonPropertyName("left")] public int Left { get; set; }
    [JsonPropertyName("top")] public int Top { get; set; }
    [JsonPropertyName("right")] public int Right { get; set; }
    [JsonPropertyName("bottom")] public int Bottom { get; set; }
}

public sealed class TaskbarRectDto
{
    [JsonPropertyName("left")] public int Left { get; set; }
    [JsonPropertyName("top")] public int Top { get; set; }
    [JsonPropertyName("right")] public int Right { get; set; }
    [JsonPropertyName("bottom")] public int Bottom { get; set; }
    [JsonPropertyName("visible")] public bool Visible { get; set; }
    [JsonPropertyName("scale")] public double Scale { get; set; } = 1.0;
}

public sealed class CapsuleSlotDto
{
    [JsonPropertyName("rect")] public SlotRectDto? Rect { get; set; }
    [JsonPropertyName("mode")] public string Mode { get; set; } = "hidden";
}

public sealed class LayoutRecommendationDto
{
    [JsonPropertyName("weatherWidth")] public int WeatherWidth { get; set; }
    [JsonPropertyName("musicWidth")] public int MusicWidth { get; set; }
    [JsonPropertyName("lite")] public bool Lite { get; set; }
    [JsonPropertyName("requiredWidth")] public int RequiredWidth { get; set; }
    [JsonPropertyName("requiresReservation")] public bool RequiresReservation { get; set; }
}

public sealed class HelloCapabilitiesDto
{
    [JsonPropertyName("weather")] public bool Weather { get; set; }
    [JsonPropertyName("precipitationTrend")] public bool PrecipitationTrend { get; set; }
    [JsonPropertyName("taskbarGeometry")] public bool TaskbarGeometry { get; set; }
    [JsonPropertyName("capsuleLayoutPlanning")] public bool CapsuleLayoutPlanning { get; set; }
    [JsonPropertyName("extendedMediaControls")] public bool ExtendedMediaControls { get; set; }
    [JsonPropertyName("solarTimes")] public bool SolarTimes { get; set; }
    [JsonPropertyName("systemFonts")] public bool SystemFonts { get; set; }
    [JsonPropertyName("eventsLongPoll")] public bool EventsLongPoll { get; set; }
    [JsonPropertyName("rendererReplacement")] public bool RendererReplacement { get; set; }
    [JsonPropertyName("rendererLease")] public bool RendererLease { get; set; }
    [JsonPropertyName("frontendDiscovery")] public bool FrontendDiscovery { get; set; }
    [JsonPropertyName("taskbarReservation")] public bool TaskbarReservation { get; set; }
    [JsonPropertyName("builtinTaskbarReservation")] public bool BuiltinTaskbarReservation { get; set; }
    [JsonPropertyName("maxConnections")] public int MaxConnections { get; set; }
}

public sealed class HelloResponseDto
{
    [JsonPropertyName("protocolVersion")] public int ProtocolVersion { get; set; } = 1;
    [JsonPropertyName("backendVersion")] public string BackendVersion { get; set; } = "";
    [JsonPropertyName("instanceId")] public string InstanceId { get; set; } = "";
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    [JsonPropertyName("capabilities")] public HelloCapabilitiesDto? Capabilities { get; set; }
    [JsonPropertyName("methods")] public List<string> Methods { get; set; } = new();
}

public sealed class PreferredSizesDto
{
    [JsonPropertyName("musicFullDip")] public double MusicFullDip { get; set; }
    [JsonPropertyName("musicLiteDip")] public double MusicLiteDip { get; set; }
    [JsonPropertyName("weatherFullDip")] public double WeatherFullDip { get; set; }
    [JsonPropertyName("weatherLiteDip")] public double WeatherLiteDip { get; set; }
    [JsonPropertyName("gapDip")] public double GapDip { get; set; }
    [JsonPropertyName("heightDip")] public double HeightDip { get; set; }
}

public sealed class RendererLeaseDto
{
    [JsonPropertyName("leaseId")] public string LeaseId { get; set; } = "";
    [JsonPropertyName("rendererId")] public string RendererId { get; set; } = "";
    [JsonPropertyName("pid")] public int Pid { get; set; }
    [JsonPropertyName("phase")] public string Phase { get; set; } = "";
    [JsonPropertyName("expiresInMs")] public int ExpiresInMs { get; set; }
    [JsonPropertyName("leaseDurationMs")] public int LeaseDurationMs { get; set; }
    [JsonPropertyName("renewAfterMs")] public int RenewAfterMs { get; set; }
    [JsonPropertyName("sizes")] public PreferredSizesDto? Sizes { get; set; }
}

public sealed class RendererStatusDto
{
    [JsonPropertyName("owner")] public string Owner { get; set; } = "";
    [JsonPropertyName("lease")] public RendererLeaseDto? Lease { get; set; }
    [JsonPropertyName("lastReleaseReason")] public string? LastReleaseReason { get; set; }
}

public sealed class RendererLeaseCommandResultDto
{
    [JsonPropertyName("leaseId")] public string LeaseId { get; set; } = "";
    [JsonPropertyName("leaseToken")] public string? LeaseToken { get; set; }
    [JsonPropertyName("phase")] public string Phase { get; set; } = "";
    [JsonPropertyName("leaseDurationMs")] public int LeaseDurationMs { get; set; }
    [JsonPropertyName("renewAfterMs")] public int RenewAfterMs { get; set; }
    [JsonPropertyName("expiresInMs")] public int ExpiresInMs { get; set; }
    [JsonPropertyName("slotsVersion")] public int SlotsVersion { get; set; }
    [JsonPropertyName("slots")] public CapsuleSlotsDto? Slots { get; set; }
    [JsonPropertyName("renderingOwner")] public string RenderingOwner { get; set; } = "";
    [JsonPropertyName("released")] public bool Released { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

public sealed class ReservationSnapshotDto
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("error")] public int Error { get; set; }
    [JsonPropertyName("slot")] public RectDto? Slot { get; set; }
    [JsonPropertyName("widthDip")] public double WidthDip { get; set; }
    [JsonPropertyName("explorerPid")] public uint ExplorerPid { get; set; }
    [JsonPropertyName("at")] public long At { get; set; }
}

public sealed class CapsuleSlotsDto
{
    [JsonPropertyName("musicSlot")] public CapsuleSlotDto? MusicSlot { get; set; }
    [JsonPropertyName("weatherSlot")] public CapsuleSlotDto? WeatherSlot { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = "";
}

public sealed class WindowsReservationDto
{
    [JsonPropertyName("snapshot")] public ReservationSnapshotDto? Snapshot { get; set; }
    [JsonPropertyName("slots")] public CapsuleSlotsDto? Slots { get; set; }
    [JsonPropertyName("slotsVersion")] public int SlotsVersion { get; set; }
    [JsonPropertyName("owner")] public string Owner { get; set; } = "";
    [JsonPropertyName("renderer")] public RendererStatusDto? Renderer { get; set; }
}

public sealed class WindowHintsDto
{
    [JsonPropertyName("coordinateSpace")] public string CoordinateSpace { get; set; } = "physical-screen-pixels";
    [JsonPropertyName("recommendedGapDip")] public double RecommendedGapDip { get; set; } = 8;
    [JsonPropertyName("popupSizeOwnedBy")] public string PopupSizeOwnedBy { get; set; } = "frontend";
    [JsonPropertyName("taskbar")] public TaskbarRectDto? Taskbar { get; set; }
    [JsonPropertyName("weatherAnchor")] public RectDto? WeatherAnchor { get; set; }
    [JsonPropertyName("musicAnchor")] public RectDto? MusicAnchor { get; set; }
    [JsonPropertyName("blankZoneRect")] public RectDto? BlankZoneRect { get; set; }
    [JsonPropertyName("geometryStatus")] public string? GeometryStatus { get; set; }
    [JsonPropertyName("measuredAt")] public string? MeasuredAt { get; set; }
    [JsonPropertyName("musicSlot")] public CapsuleSlotDto? MusicSlot { get; set; }
    [JsonPropertyName("weatherSlot")] public CapsuleSlotDto? WeatherSlot { get; set; }
    [JsonPropertyName("trayWidthPx")] public int? TrayWidthPx { get; set; }
    [JsonPropertyName("layoutState")] public string? LayoutState { get; set; }
    [JsonPropertyName("layoutRecommendation")] public LayoutRecommendationDto? LayoutRecommendation { get; set; }
    [JsonPropertyName("reservationSupported")] public bool ReservationSupported { get; set; }
    [JsonPropertyName("nativeReservation")] public ReservationSnapshotDto? NativeReservation { get; set; }
    [JsonPropertyName("renderingOwner")] public string? RenderingOwner { get; set; }
    [JsonPropertyName("slotsVersion")] public int SlotsVersion { get; set; }
    [JsonPropertyName("renderer")] public RendererStatusDto? Renderer { get; set; }
}

public sealed class ChangeBatchDto
{
    [JsonPropertyName("instanceId")] public string InstanceId { get; set; } = "";
    [JsonPropertyName("version")] public long Version { get; set; }
    [JsonPropertyName("topics")] public List<string> Topics { get; set; } = new();
    [JsonPropertyName("resyncRequired")] public bool ResyncRequired { get; set; }
    [JsonPropertyName("timedOut")] public bool TimedOut { get; set; }
}

public sealed class PrecipitationSlot
{
    public string Label { get; init; } = "";
    public double? Millimeters { get; init; }
    public double? AvgIntensityMmPerHour => Millimeters.HasValue ? Millimeters.Value / 0.25 : null;
    public bool IsGap => !Millimeters.HasValue;
}

public sealed class PrecipitationViewModel
{
    public List<PrecipitationSlot> Slots { get; init; } = new();
    public string? PrecisionNote { get; init; }
    public bool Complete { get; init; } = true;
}

public static class WeatherFormatting
{
    public static string DescribeWmo(int code) => code switch
    {
        0 => "晴",
        1 => "晴间多云",
        2 => "多云",
        3 => "阴",
        45 or 48 => "雾",
        >= 51 and <= 57 => "毛毛雨",
        >= 61 and <= 67 => "雨",
        >= 71 and <= 77 => "雪",
        >= 80 and <= 82 => "阵雨",
        85 or 86 => "阵雪",
        >= 95 and <= 99 => "雷雨",
        _ => "多云"
    };

    public static string GlyphForWmo(int code, bool isNight) => code switch
    {
        0 => isNight ? "\uE708" : "\uE706", // Moon / Sunny
        1 or 2 => isNight ? "\uE708" : "\uE753", // Partly cloudy
        3 => "\uE753",
        45 or 48 => "\uE753",
        >= 51 and <= 67 => "\uE9C0",
        >= 71 and <= 77 => "\uE9C8",
        >= 80 and <= 82 => "\uE9C0",
        >= 85 and <= 86 => "\uE9C8",
        >= 95 and <= 99 => "\uE945",
        _ => "\uE753"
    };

    public static string EmojiForWmo(int code, bool isNight) => code switch
    {
        0 => isNight ? "🌙" : "☀️",
        1 => isNight ? "🌙" : "🌤️",
        2 => "⛅",
        3 => "☁️",
        45 or 48 => "🌫️",
        >= 51 and <= 57 => "🌦️",
        >= 61 and <= 67 => "🌧️",
        >= 71 and <= 77 => "❄️",
        >= 80 and <= 82 => "🌧️",
        85 or 86 => "🌨️",
        >= 95 and <= 99 => "⛈️",
        _ => "⛅"
    };

    public static bool IsNightHour(string? localTimeIso)
    {
        if (string.IsNullOrWhiteSpace(localTimeIso)) return false;
        if (DateTime.TryParse(localTimeIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.Hour < 6 || dt.Hour >= 18;
        return false;
    }

    public static bool IsCityNightNow(string? timeZoneId)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
                return local.Hour < 6 || local.Hour >= 18;
            }
        }
        catch { }
        var nowHour = DateTime.Now.Hour;
        return nowHour < 6 || nowHour >= 18;
    }

    public static double FormatTemperatureValue(double celsius, bool fahrenheit)
        => fahrenheit ? celsius * 9.0 / 5.0 + 32.0 : celsius;

    public static string FormatTemp(double celsius, bool fahrenheit)
        => $"{Math.Round(FormatTemperatureValue(celsius, fahrenheit))}°{(fahrenheit ? "F" : "")}";

    public static string FormatWind(double kmh, double degrees, bool mps)
    {
        var dir = CompassDirection(degrees);
        return mps ? $"{dir} {kmh / 3.6:0.0} m/s" : $"{dir} {Math.Round(kmh)} km/h";
    }

    public static string CompassDirection(double degrees)
    {
        string[] dirs = { "北风", "东北风", "东风", "东南风", "南风", "西南风", "西风", "西北风" };
        var idx = (int)Math.Round(((degrees % 360) + 360) % 360 / 45.0) % 8;
        return dirs[idx];
    }

    public static (string Label, Color Accent) DescribeUsAqi(double? aqi)
    {
        if (!aqi.HasValue) return ("暂无", Color.FromArgb(255, 148, 163, 184));
        var v = aqi.Value;
        return v switch
        {
            <= 50 => ("优", Color.FromArgb(255, 34, 197, 94)),
            <= 100 => ("良", Color.FromArgb(255, 234, 179, 8)),
            <= 150 => ("轻度敏感", Color.FromArgb(255, 249, 115, 22)),
            <= 200 => ("不健康", Color.FromArgb(255, 239, 68, 68)),
            _ => ("重度污染", Color.FromArgb(255, 168, 85, 247))
        };
    }

    public static Color ParseHexColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        var raw = hex.Trim().TrimStart('#');
        if (raw.Length == 6 &&
            byte.TryParse(raw[..2], NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(raw[2..4], NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(raw[4..6], NumberStyles.HexNumber, null, out var b))
        {
            return Color.FromArgb(255, r, g, b);
        }
        return fallback;
    }

    public static string FormatTimeSpan(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }
}
