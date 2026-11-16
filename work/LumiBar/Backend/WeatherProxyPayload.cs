using System.Text.Json;
namespace LumiBar;
internal static class WeatherProxyPayload
{
    internal static Forecast Read(JsonElement root)
    {
        var data=root.GetProperty("forecast");
        foreach(var field in new[]{"temperature","feelsLike","humidity","wind","windDirection"})
            if(!data.TryGetProperty(field,out var value)||value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out var number)||!double.IsFinite(number))throw new InvalidDataException("云端天气缺少有效观测数据。");
        var result=data.Deserialize<Forecast>(new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})??throw new InvalidDataException("云端天气返回为空。");
        if(result.Hours==null||result.Days==null||result.Hours.Count>240||result.Days.Count>30||result.Humidity<0||result.Humidity>100||result.Wind<0||result.Updated.Year<2020)throw new InvalidDataException("云端天气格式有误。");
        if(result.Hours.Any(h=>h==null||!double.IsFinite(h.Temperature))||result.Days.Any(d=>d==null||!double.IsFinite(d.Min)||!double.IsFinite(d.Max)||d.Min>d.Max))throw new InvalidDataException("云端预报格式有误。");
        return result with{Updated=result.Updated.ToLocalTime(),Alerts=WeatherAlerts.Current(result.Alerts,DateTimeOffset.UtcNow).ToList()};
    }
}
