using System.Text.Json;
using LumiBar;
internal static class DefaultSettingsTests
{
    internal static List<string> Run()
    {
        var results=new List<string>();void Check(bool ok,string name)=>results.Add($"{(ok?"PASS":"FAIL")} {name}");
        var oldPath=Environment.GetEnvironmentVariable("LUMIBAR_DATA_DIR");
        var folder=Path.Combine(AppContext.BaseDirectory,"test-data","defaults-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);Environment.SetEnvironmentVariable("LUMIBAR_DATA_DIR",folder);
        try{
            var fresh=Preferences.Load();
            Check(fresh.GlassOpacity==.5&&fresh.Glow==.26&&fresh.CoverSaturation==.8&&fresh.Gap==4&&fresh.LyricsFontSize==22&&fresh.Sensitivity==1.2,"A fresh profile receives the captured visual, spacing and lyrics defaults");
            Check(fresh.Theme=="System"&&fresh.AutoLocateCity&&fresh.AutoStart&&fresh.Animations&&fresh.CoverGlow&&fresh.Spectrum&&fresh.AutoLyrics,"A fresh profile receives the captured feature switches");
            Check(fresh.FontFamily==null&&fresh.ChineseFontFamily==null&&fresh.WesternFontFamily==null&&fresh.WeatherRefreshMinutes==20&&fresh.MusicWidth==238&&!fresh.Fahrenheit&&!fresh.WindMetersPerSecond,"Bundled font selection, weather interval and units match the capture");
            var privateFields=new[]{"City","Favorites","LyricsOffsets","EnabledPlugins","ApiKey","Token","Password"};
            Check(PreferenceDefaults.Manifest.EnumerateObject().Count()==27&&!PreferenceDefaults.Manifest.EnumerateObject().Any(p=>privateFields.Contains(p.Name))&&fresh.City==null&&fresh.Favorites.Count==0&&fresh.LyricsOffsets.Count==0&&fresh.EnabledPlugins.Count==0,"The shipping manifest excludes location, song adjustments, credentials and local plugin state");
            fresh.GlassOpacity=.83;fresh.Glow=.41;fresh.Gap=12;fresh.LyricsFontSize=28;fresh.AutoLocateCity=false;fresh.AutoStart=false;fresh.LyricsOffsets["saved-song"]=-.5;fresh.Save();
            var saved=Preferences.Load();
            Check(saved.GlassOpacity==.83&&saved.Glow==.41&&saved.Gap==12&&saved.LyricsFontSize==28&&!saved.AutoLocateCity&&!saved.AutoStart&&saved.LyricsOffsets["saved-song"]==-.5,"Existing preferences and per-song timing override shipping defaults after reload");
            File.WriteAllText(Path.Combine(folder,"settings.json"),"""{"GlassOpacity":0.88,"Glow":0.33,"AutoStart":false,"AutoLocateCity":false}""");
            var partial=Preferences.Load();
            Check(partial.GlassOpacity==.88&&partial.Glow==.33&&!partial.AutoStart&&!partial.AutoLocateCity&&partial.Gap==4&&partial.LyricsFontSize==22,"Older partial settings retain saved choices and use shipping defaults only for missing fields");
            var invalid=new Preferences{Glow=double.NaN,CoverSaturation=double.NaN,GlassOpacity=double.NaN,Sensitivity=double.NaN};invalid.Normalize();
            Check(invalid.Glow==.26&&invalid.CoverSaturation==.8&&invalid.GlassOpacity==.5&&invalid.Sensitivity==1.2,"Invalid saved numbers recover to the same shipping defaults");
        }finally{Environment.SetEnvironmentVariable("LUMIBAR_DATA_DIR",oldPath);}
        return results;
    }
}
