using System.Text.Json;
using LumiBar;

internal static class RainTests
{
    public static List<string> Run()
    {
        var results=new List<string>();void Check(bool value,string name)=>results.Add($"{(value?"PASS":"FAIL")} {name}");
        var now=DateTimeOffset.Parse("2026-11-14T17:07:00+08:00");
        const string fixture="""
        {"timezone":"Asia/Shanghai","utc_offset_seconds":28800,"minutely_15_units":{"precipitation":"mm"},"minutely_15":{"time":["2026-11-14T17:00","2026-11-14T17:15","2026-11-14T17:30","2026-11-14T17:45","2026-11-14T18:00","2026-11-14T18:15","2026-11-14T18:30","2026-11-14T18:45"],"precipitation":[0,0.2,0.4,0.5,0.3,0.1,0,0]}}
        """;
        using var doc=JsonDocument.Parse(fixture);
        var series=Precipitation.Parse(doc.RootElement,now);var trend=Precipitation.NextHour(series,now)!;
        Check(trend.Complete&&trend.Intervals.Count==5,"Next hour covers five partially overlapping quarter-hour intervals");
        Check(trend.Intervals[0].Start==now.AddMinutes(-7)&&trend.Intervals[^1].End==now.AddMinutes(68),"Keep original accumulation intervals instead of inventing minute precision");
        Check(trend.WindowStart==now&&trend.WindowEnd==now.AddHours(1)&&trend.Kind=="model-trend","Trend states exact viewing window and model provenance");
        Check(trend.Intervals[0].End.UtcDateTime.Hour==9,"City timezone converts to correct absolute time");
        var later=Precipitation.NextHour(series,now.AddMinutes(22))!;
        Check(later.Complete&&later.Intervals[0].End==DateTimeOffset.Parse("2026-11-14T17:30:00+08:00"),"Cached forecast window rolls forward with time");
        using var missing=JsonDocument.Parse(fixture.Replace("0.4","null"));
        var gap=Precipitation.NextHour(Precipitation.Parse(missing.RootElement,now),now)!;
        Check(!gap.Complete&&gap.Intervals.Any(x=>x.AmountMm==null),"Missing amount stays missing instead of zero rain");
        using var negative=JsonDocument.Parse(fixture.Replace("0.4","-1"));
        Check(!Precipitation.NextHour(Precipitation.Parse(negative.RootElement,now),now)!.Complete,"Negative rain amount cannot appear as valid forecast");
        Check(Precipitation.NextHour(series,now.AddHours(4))==null,"Expired forecast has no current trend");
        using var wrongUnits=JsonDocument.Parse(fixture.Replace("\"mm\"","\"inch\""));
        Check(Precipitation.Parse(wrongUnits.RootElement,now)==null,"Unexpected precipitation units rejected");
        using var absent=JsonDocument.Parse("{}");Check(Precipitation.Parse(absent.RootElement,now)==null,"Missing optional trend leaves main weather usable");
        using var badUnit=JsonDocument.Parse(fixture.Replace("\"mm\"","null"));Check(Precipitation.Parse(badUnit.RootElement,now)==null,"Malformed optional unit does not throw");
        var partial=series! with{Intervals=series!.Intervals.Take(4).ToList()};Check(!Precipitation.NextHour(partial,now)!.Complete,"Truncated horizon explicitly marked incomplete");
        return results;
    }
}
