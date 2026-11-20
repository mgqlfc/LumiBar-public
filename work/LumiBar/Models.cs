using System.Text.Json;
using System.Text.RegularExpressions;

namespace LumiBar;
internal record City(string Name,double Latitude,double Longitude,string Region="",string TimeZone="");
internal sealed class Preferences
{
    public City? City {get;set;}
    public List<City> Favorites {get;set;}=[];
    public double Glow {get;set;}=PreferenceDefaults.Get(nameof(Glow),.28);
    public double CoverSaturation {get;set;}=PreferenceDefaults.Get(nameof(CoverSaturation),.35);
    public bool AutoLocateCity {get;set;}=PreferenceDefaults.Get(nameof(AutoLocateCity),false);
    public double GlassOpacity {get;set;}=PreferenceDefaults.Get(nameof(GlassOpacity),.62);
    public string Theme {get;set;}=PreferenceDefaults.Get(nameof(Theme),"System");
    public string AccentColor {get;set;}=PreferenceDefaults.Get(nameof(AccentColor),AccentPalettes.Default);
    public string? FontFamily {get;set;}=PreferenceDefaults.Get<string?>(nameof(FontFamily),null);
    public string? ChineseFontFamily {get;set;}=PreferenceDefaults.Get<string?>(nameof(ChineseFontFamily),null);
    public string? WesternFontFamily {get;set;}=PreferenceDefaults.Get<string?>(nameof(WesternFontFamily),null);
    public bool CoverGlow {get;set;}=PreferenceDefaults.Get(nameof(CoverGlow),true);
    public bool Animations {get;set;}=PreferenceDefaults.Get(nameof(Animations),true);
    public bool ShowWeather {get;set;}=PreferenceDefaults.Get(nameof(ShowWeather),true);
    public bool ShowMusic {get;set;}=PreferenceDefaults.Get(nameof(ShowMusic),true);
    public bool Spectrum {get;set;}=PreferenceDefaults.Get(nameof(Spectrum),true);
    public bool HideInFullscreen {get;set;}=PreferenceDefaults.Get(nameof(HideInFullscreen),true);
    public bool AutoLyrics {get;set;}=PreferenceDefaults.Get(nameof(AutoLyrics),true);
    public bool AutoStart {get;set;}=PreferenceDefaults.Get(nameof(AutoStart),false);
    public bool CloseToTray {get;set;}=PreferenceDefaults.Get(nameof(CloseToTray),true);
    public int WeatherRefreshMinutes {get;set;}=PreferenceDefaults.Get(nameof(WeatherRefreshMinutes),20);
    public string WeatherProvider {get;set;}=PreferenceDefaults.Get(nameof(WeatherProvider),"Auto");
    public string WeatherProxyUrl {get;set;}=PreferenceDefaults.Get(nameof(WeatherProxyUrl),WeatherProxyAddress.Default);
    public bool Fahrenheit {get;set;}=PreferenceDefaults.Get(nameof(Fahrenheit),false);
    public bool WindMetersPerSecond {get;set;}=PreferenceDefaults.Get(nameof(WindMetersPerSecond),false);
    public int Gap {get;set;}=PreferenceDefaults.Get(nameof(Gap),8);
    public int MusicWidth {get;set;}=PreferenceDefaults.Get(nameof(MusicWidth),238);
    public int LyricsFontSize {get;set;}=PreferenceDefaults.Get(nameof(LyricsFontSize),25);
    public Dictionary<string,double> LyricsOffsets {get;set;}=[];
    public double Sensitivity {get;set;}=PreferenceDefaults.Get(nameof(Sensitivity),1d);
    public List<string> EnabledPlugins {get;set;}=[];
    public static string DataPath => Environment.GetEnvironmentVariable("LUMIBAR_DATA_DIR") is { Length: > 0 } custom ? Path.GetFullPath(custom) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumiBar");
    public static Preferences Load()
    {
        var path=Path.Combine(DataPath,"settings.json");
        foreach(var candidate in new[]{path,path+".bak"})
        {
            using var document=AtomicStore.Read<JsonDocument>(candidate);
            if(document==null)continue;
            try
            {
                var value=document.RootElement.Deserialize<Preferences>();
                if(value==null)continue;
                // Missing keys belong to the old single-font format. Explicit
                // null means the user restored the system font for that script.
                if(!document.RootElement.TryGetProperty(nameof(ChineseFontFamily),out _))value.ChineseFontFamily=value.FontFamily;
                if(!document.RootElement.TryGetProperty(nameof(WesternFontFamily),out _))value.WesternFontFamily=value.FontFamily;
                value.Normalize();return value;
            }
            catch(JsonException){ } // A valid JSON document can still have invalid setting types; try its backup.
        }
        var defaults=new Preferences();defaults.Normalize();return defaults;
    }
    public void Save(){Normalize();AtomicStore.Write(Path.Combine(DataPath,"settings.json"),JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}),backup:true);}
    internal void Normalize()
    {
        static double Clamp(double value,double min,double max,double fallback)=>double.IsFinite(value)?Math.Clamp(value,min,max):fallback;
        static bool Valid(City? city)=>city!=null&&!string.IsNullOrWhiteSpace(city.Name)&&double.IsFinite(city.Latitude)&&double.IsFinite(city.Longitude)&&Math.Abs(city.Latitude)<=90&&Math.Abs(city.Longitude)<=180;
        if(!Valid(City))City=null;
        Favorites=(Favorites??[]).Where(Valid).DistinctBy(c=>(Math.Round(c.Latitude,4),Math.Round(c.Longitude,4))).Take(30).ToList();
        EnabledPlugins=(EnabledPlugins??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Take(50).ToList();
        if(Theme is not ("System" or "Light" or "Dark"))Theme=PreferenceDefaults.Get(nameof(Theme),"System");
        AccentColor=AccentPalettes.Normalize(AccentColor);
        FontFamily=FontFamily?.Trim();if(string.IsNullOrEmpty(FontFamily)||FontFamily.Length>128||FontFamily.Any(char.IsControl)||FontFamily.IndexOfAny([':', '/', '\\'])>=0)FontFamily=null;
        static string? CleanFont(string? name){name=name?.Trim();return string.IsNullOrEmpty(name)||name.Length>128||name.Any(char.IsControl)||name.IndexOfAny([':', '/', '\\'])>=0?null:name;}
        ChineseFontFamily=CleanFont(ChineseFontFamily);WesternFontFamily=CleanFont(WesternFontFamily);
        // Old built-in font choices return to the current defaults for their scripts.
        if(FontDefaults.IsLegacyChinese(FontFamily))FontFamily=null;
        if(FontDefaults.IsLegacyChinese(ChineseFontFamily))ChineseFontFamily=null;
        if(FontDefaults.IsLegacyChinese(WesternFontFamily))WesternFontFamily=null;
        Glow=Clamp(Glow,.08,.55,PreferenceDefaults.Get(nameof(Glow),.28));CoverSaturation=Clamp(CoverSaturation,0,1,PreferenceDefaults.Get(nameof(CoverSaturation),.35));GlassOpacity=Clamp(GlassOpacity,.35,.95,PreferenceDefaults.Get(nameof(GlassOpacity),.62));Sensitivity=Clamp(Sensitivity,.5,2.5,PreferenceDefaults.Get(nameof(Sensitivity),1d));LyricsOffsets=TrackLyricTiming.Normalize(LyricsOffsets);
        Gap=Math.Clamp(Gap,4,24);MusicWidth=Math.Clamp(MusicWidth,180,340);LyricsFontSize=Math.Clamp(LyricsFontSize,18,36);
        if(WeatherRefreshMinutes is not (10 or 20 or 30 or 60))WeatherRefreshMinutes=PreferenceDefaults.Get(nameof(WeatherRefreshMinutes),20);
        if(WeatherProvider is not ("Auto" or "Public"))WeatherProvider=PreferenceDefaults.Get(nameof(WeatherProvider),"Auto");
        WeatherProxyUrl=WeatherProxyAddress.Normalize(WeatherProxyUrl)??PreferenceDefaults.Get(nameof(WeatherProxyUrl),WeatherProxyAddress.Default);
    }
}
internal record HourForecast(DateTime Time,double Temperature,int Code,double Rain,bool? IsDay=null);
internal record DayForecast(DateTime Date,double Min,double Max,int Code,double Rain);
internal record Forecast(double Temperature,double FeelsLike,int Code,double Humidity,double Wind,double WindDirection,double? Rain,List<HourForecast> Hours,List<DayForecast> Days,DateTime Updated,double? Aqi,double? Pm25,double? Pm10,string? AirError,string Source="Open-Meteo",bool EstimatedFeelsLike=false,string TimeZoneLabel="城市当地时间",PrecipitationSeries? ShortTermPrecipitation=null,DateTime? Sunrise=null,DateTime? Sunset=null,bool? IsDay=null,double? PressureHpa=null,double? UvIndex=null,List<WeatherAlert>? Alerts=null,string AqiStandard="US AQI",string? AqiCategory=null,string? ConditionText=null,List<string>? Attributions=null,List<string>? ProviderNote=null);
internal record LyricLine(double Seconds,string Text);
internal static class Lrc
{
    private static readonly Regex Tags=new(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]",RegexOptions.Compiled);
    public static List<LyricLine> Parse(string text)
    {
        if(text.Length>1024*1024)throw new InvalidDataException("歌词内容超过 1 MB。");
        var result=new List<LyricLine>();
        var offsetMatch=Regex.Match(text,@"\[offset:([+-]?\d+)\]",RegexOptions.IgnoreCase);
        var offset=offsetMatch.Success&&double.TryParse(offsetMatch.Groups[1].Value,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out var parsed)&&double.IsFinite(parsed)?Math.Clamp(parsed/1000,-3600,3600):0;
        foreach(var line in text.Split('\n'))
        {
            var tags=Tags.Matches(line); if(tags.Count==0)continue;
            var body=Tags.Replace(line,"").Trim(); if(body.Length==0)continue;
            foreach(Match m in tags) {
                if(int.Parse(m.Groups[2].Value)>59)continue;
                var fraction=m.Groups[3].Value;
                var secs=int.Parse(m.Groups[1].Value)*60+int.Parse(m.Groups[2].Value)+(fraction.Length>0?int.Parse(fraction)/Math.Pow(10,fraction.Length):0)-offset;
                result.Add(new(Math.Max(0,secs),body));
            }
        }
        return result.GroupBy(x=>Math.Round(x.Seconds,3)).OrderBy(g=>g.Key).Select(g=>new LyricLine(g.Key,string.Join("\n",g.Select(l=>l.Text).Distinct(StringComparer.Ordinal)))).ToList();
    }
    public static int Active(IReadOnlyList<LyricLine> lines,double seconds) {
        int low=0,high=lines.Count-1,result=-1;
        while(low<=high){var mid=(low+high)/2;if(lines[mid].Seconds<=seconds){result=mid;low=mid+1;}else high=mid-1;}
        return result;
    }
}
internal static class WeatherNames
{
    // Local concept conditions, not WMO provider codes.
    internal const int Haze=-1,DustStorm=-2;
    public static string Describe(int c)=>c switch{Haze=>"雾霾",DustStorm=>"沙尘暴",0=>"晴",1=>"晴间多云",2=>"多云",3=>"阴",45 or 48=>"雾",>=51 and <=57=>"毛毛雨",>=61 and <=67=>"雨",>=71 and <=77=>"雪",>=80 and <=82=>"阵雨",>=85 and <=86=>"阵雪",>=95=>"雷雨",_=>"天气"};
    public static string Glyph(int c)=>c switch{0=>"\uE706",1 or 2=>"\uE9C0",3=>"\uE753",45 or 48=>"\uE9BE",>=71 and <=77=>"\uE9C5",>=85 and <=86=>"\uE9C5",>=95=>"\uE9C6",_=>"\uE9C4"};
}
