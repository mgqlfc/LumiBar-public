using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace LumiBar;
internal static class FrontendSettings
{
    private static readonly JsonSerializerOptions Options=CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var resolver=new DefaultJsonTypeInfoResolver();resolver.Modifiers.Add(info=>{
            if(info.Type==typeof(Preferences))for(var i=info.Properties.Count-1;i>=0;i--)if(info.Properties[i].Name=="lyricsOffsets")info.Properties.RemoveAt(i);
        });
        return new(FrontendPipeServer.Json){TypeInfoResolver=resolver};
    }
    internal static JsonObject Snapshot(Preferences prefs,string? trackKey)
    {
        var obj=JsonSerializer.SerializeToNode(prefs,Options)!.AsObject();
        obj["lyricsOffset"]=TrackLyricTiming.Get(prefs.LyricsOffsets,trackKey);obj["lyricsOffsetTrackKey"]=trackKey;
        obj["defaultChineseFontFamily"]=FontDefaults.Chinese;obj["defaultWesternFontFamily"]=FontDefaults.Western;return obj;
    }
}
