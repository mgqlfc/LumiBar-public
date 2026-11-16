namespace LumiBar;

internal static class TrackLyricTiming
{
    internal static double Get(IReadOnlyDictionary<string,double> offsets,string? trackKey)=>trackKey!=null&&offsets.TryGetValue(trackKey,out var offset)?offset:0;
    internal static bool Set(Dictionary<string,double> offsets,string? trackKey,double seconds)
    {
        if(string.IsNullOrWhiteSpace(trackKey)||trackKey.Length>256||!double.IsFinite(seconds))return false;
        var value=Math.Round(Math.Clamp(seconds,-60,60),2);offsets.Remove(trackKey);
        if(value!=0)offsets.Add(trackKey,value);
        while(offsets.Count>10000)offsets.Remove(offsets.Keys.First());return true;
    }
    internal static Dictionary<string,double> Normalize(Dictionary<string,double>? offsets)
    {
        var result=new Dictionary<string,double>(StringComparer.Ordinal);
        if(offsets!=null)foreach(var (key,value) in offsets)Set(result,key,value);
        return result;
    }
}
