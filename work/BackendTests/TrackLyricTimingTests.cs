using System.Text.Json;
using LumiBar;

internal static class TrackLyricTimingTests
{
    internal static List<string> Run()
    {
        var results=new List<string>();void Check(bool ok,string name)=>results.Add($"{(ok?"PASS":"FAIL")} {name}");
        var prefs=new Preferences();var first=MediaRules.TrackKey("player","A","artist","album",200);var second=MediaRules.TrackKey("player","B","artist","album",240);
        TrackLyricTiming.Set(prefs.LyricsOffsets,first,2.5);
        Check(TrackLyricTiming.Get(prefs.LyricsOffsets,first)==2.5&&TrackLyricTiming.Get(prefs.LyricsOffsets,second)==0,"Lyric timing change stays with one song while another starts at zero");
        TrackLyricTiming.Set(prefs.LyricsOffsets,second,-1);
        var restored=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(prefs))!;restored.Normalize();
        Check(TrackLyricTiming.Get(restored.LyricsOffsets,first)==2.5&&TrackLyricTiming.Get(restored.LyricsOffsets,second)==-1,"Independent song offsets survive settings serialization and reload");
        TrackLyricTiming.Set(restored.LyricsOffsets,first,0);
        Check(!restored.LyricsOffsets.ContainsKey(first)&&TrackLyricTiming.Get(restored.LyricsOffsets,second)==-1,"Resetting one song leaves other songs untouched");
        Check(!TrackLyricTiming.Set(restored.LyricsOffsets,null,4)&&!TrackLyricTiming.Set(restored.LyricsOffsets,first,double.NaN),"Missing track or invalid offset cannot create a global timing value");
        var legacy=JsonSerializer.Deserialize<Preferences>("""{"LyricsOffset":12}""")!;legacy.Normalize();
        Check(TrackLyricTiming.Get(legacy.LyricsOffsets,first)==0,"Legacy global offset is ignored instead of copied to every song");
        var snapshot=FrontendSettings.Snapshot(prefs,first);
        Check(snapshot["lyricsOffset"]!.GetValue<double>()==2.5&&snapshot["lyricsOffsetTrackKey"]!.GetValue<string>()==first&&!snapshot.ContainsKey("lyricsOffsets"),"Frontend snapshot reports only current song timing and its key");
        Check(FrontendSettings.Snapshot(prefs,null)["lyricsOffset"]!.GetValue<double>()==0,"No media session reports zero timing instead of leaking previous song offset");
        var previous=Environment.GetEnvironmentVariable("LUMIBAR_DATA_DIR");var directory=Path.Combine(AppContext.BaseDirectory,"test-data","song-timing-"+Guid.NewGuid().ToString("N"));
        try{Environment.SetEnvironmentVariable("LUMIBAR_DATA_DIR",directory);prefs.Save();var loaded=Preferences.Load();Check(TrackLyricTiming.Get(loaded.LyricsOffsets,first)==2.5&&TrackLyricTiming.Get(loaded.LyricsOffsets,second)==-1,"Song offsets survive actual atomic file save and process-style reload");}
        finally{Environment.SetEnvironmentVariable("LUMIBAR_DATA_DIR",previous);}
        return results;
    }
}
