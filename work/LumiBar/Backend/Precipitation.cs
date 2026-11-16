using System.Globalization;
using System.Text.Json;

namespace LumiBar;

internal record RainInterval(DateTimeOffset Start,DateTimeOffset End,double? AmountMm);
internal record PrecipitationSeries(DateTimeOffset UpdatedAt,string Source,string PrecisionNote,List<RainInterval> Intervals);
internal record PrecipitationTrend(DateTimeOffset WindowStart,DateTimeOffset WindowEnd,string Source,string Kind,int IntervalMinutes,string PrecisionNote,bool Complete,List<RainInterval> Intervals);

internal static class Precipitation
{
    internal static PrecipitationSeries? Parse(JsonElement root,DateTimeOffset now)
    {
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("minutely_15",out var data)||data.ValueKind!=JsonValueKind.Object||
           !data.TryGetProperty("time",out var times)||times.ValueKind!=JsonValueKind.Array||
           !data.TryGetProperty("precipitation",out var amounts)||amounts.ValueKind!=JsonValueKind.Array||
           !root.TryGetProperty("minutely_15_units",out var units)||units.ValueKind!=JsonValueKind.Object||
           !units.TryGetProperty("precipitation",out var unit)||unit.ValueKind!=JsonValueKind.String||unit.GetString()!="mm")return null;
        TimeZoneInfo? zone=null;
        if(root.TryGetProperty("timezone",out var tz)&&tz.ValueKind==JsonValueKind.String)
            try{zone=TimeZoneInfo.FindSystemTimeZoneById(tz.GetString()!);}catch(TimeZoneNotFoundException){}catch(InvalidTimeZoneException){}
        var offset=TimeSpan.Zero;
        if(root.TryGetProperty("utc_offset_seconds",out var off)&&off.ValueKind==JsonValueKind.Number&&off.TryGetInt32(out var seconds)&&Math.Abs((long)seconds)<=14*3600&&seconds%60==0)offset=TimeSpan.FromSeconds(seconds);
        else if(zone==null)return null;
        var list=new List<RainInterval>();
        for(var i=0;i<Math.Min(times.GetArrayLength(),amounts.GetArrayLength());i++)
        {
            if(times[i].ValueKind!=JsonValueKind.String||!DateTime.TryParse(times[i].GetString(),CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed))continue;
            var local=DateTime.SpecifyKind(parsed,DateTimeKind.Unspecified);
            // Ambiguous / nonexistent wall-clock labels cannot safely identify a forecast interval.
            if(zone!=null&&(zone.IsInvalidTime(local)||zone.IsAmbiguousTime(local)))continue;
            var end=new DateTimeOffset(local,zone?.GetUtcOffset(local)??offset);
            if(end<=now.AddMinutes(-15)||end>now.AddHours(4))continue;
            double? amount=amounts[i].ValueKind==JsonValueKind.Number&&amounts[i].TryGetDouble(out var mm)&&double.IsFinite(mm)&&mm>=0?mm:null;
            list.Add(new(end.AddMinutes(-15),end,amount));
        }
        if(list.Count==0)return null;
        return new(now,"Open-Meteo","15 分钟模式趋势；奎文区等地区由小时预报插值",list.DistinctBy(x=>x.End).OrderBy(x=>x.End).ToList());
    }

    internal static PrecipitationTrend? NextHour(PrecipitationSeries? series,DateTimeOffset now)
    {
        if(series?.Intervals==null||series.UpdatedAt>now.AddMinutes(5)||now-series.UpdatedAt>TimeSpan.FromHours(3))return null;
        var until=now.AddHours(1);
        var interval=series.Source=="和风天气"?5:15;
        var items=series.Intervals.Where(x=>x!=null&&x.End-x.Start==TimeSpan.FromMinutes(interval)&&x.End>now&&x.Start<until).Select(x=>x with{AmountMm=x.AmountMm is {} mm&&double.IsFinite(mm)&&mm>=0?mm:null}).OrderBy(x=>x.Start).ToList();
        if(items.Count==0)return null;
        var covered=now;var complete=true;
        foreach(var item in items)
        {
            if(item.Start>covered||item.AmountMm==null)complete=false;
            if(item.End>covered)covered=item.End;
        }
        return new(now,until,series.Source,interval==5?"nowcast":"model-trend",interval,series.PrecisionNote,complete&&covered>=until,items);
    }
}
