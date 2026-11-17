using System.Globalization;
using System.Text.Json;

namespace LumiBar;

internal sealed record WeatherAlert(string Id,string Event,string Level,string Headline,string Description,string Instruction,string Source,string Area,DateTimeOffset? IssuedAt,DateTimeOffset? StartsAt,DateTimeOffset? EndsAt,bool Cancelled=false);
internal static class WeatherAlerts
{
    internal static int Rank(string level)=>level switch{"red"=>4,"orange"=>3,"yellow"=>2,"blue"=>1,_=>0};
    internal static string LevelName(string level)=>level switch{"red"=>"红色","orange"=>"橙色","yellow"=>"黄色","blue"=>"蓝色",_=>"预警"};
    internal static string Label(WeatherAlert alert)=>alert.Event;
    internal static IReadOnlyList<WeatherAlert> Current(IEnumerable<WeatherAlert>? alerts,DateTimeOffset now)=>(alerts??[])
        .Where(a=>a!=null&&!string.IsNullOrWhiteSpace(a.Id)&&!string.IsNullOrWhiteSpace(a.Event))
        .GroupBy(a=>a.Id,StringComparer.Ordinal).Select(g=>g.OrderByDescending(a=>a.IssuedAt).First())
        .Where(a=>!a.Cancelled&&(a.StartsAt==null||a.StartsAt<=now)&&(a.IssuedAt==null||a.IssuedAt<=now)
            &&(a.EndsAt is {} end?end>now:a.IssuedAt is {} issued&&now-issued<TimeSpan.FromHours(24)))
        .OrderByDescending(a=>Rank(a.Level)).ThenByDescending(a=>a.IssuedAt).Take(32).ToArray();

    // Optional provider-neutral extension. Current Open-Meteo/MET endpoints do
    // not publish alerts; no warning is inferred from WMO conditions or AQI.
    internal static List<WeatherAlert> Parse(JsonElement root,string source)
    {
        if(!root.TryGetProperty("alerts",out var list)||list.ValueKind!=JsonValueKind.Array)return [];
        static string Text(JsonElement a,string key,int limit=200)=>a.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?(v.GetString()??"").Trim()[..Math.Min((v.GetString()??"").Trim().Length,limit)]:"";
        static DateTimeOffset? Time(JsonElement a,string key)=>a.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String&&DateTimeOffset.TryParse(v.GetString(),CultureInfo.InvariantCulture,DateTimeStyles.None,out var t)?t:null;
        var result=new List<WeatherAlert>();
        foreach(var a in list.EnumerateArray().Take(128)){
            if(a.ValueKind!=JsonValueKind.Object)continue;
            var level=Text(a,"level").ToLowerInvariant();if(Rank(level)==0)level="unknown";
            result.Add(new(Text(a,"id"),Text(a,"event",40),level,Text(a,"headline",300),Text(a,"description",12000),Text(a,"instruction",6000),Text(a,"source") is {Length:>0} provider?provider:source,Text(a,"area",500),Time(a,"issuedAt"),Time(a,"startsAt"),Time(a,"endsAt"),a.TryGetProperty("cancelled",out var cancel)&&cancel.ValueKind==JsonValueKind.True));
        }
        return Current(result,DateTimeOffset.UtcNow).ToList();
    }
}
