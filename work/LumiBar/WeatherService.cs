using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Windows.Devices.Geolocation;

namespace LumiBar;
internal sealed class WeatherService
{
    internal static readonly HttpClient Http=new(){Timeout=TimeSpan.FromSeconds(18)};
    static WeatherService(){Http.DefaultRequestHeaders.UserAgent.ParseAdd("LumiBar/0.1 (Windows desktop weather and media)");}
    private static string Num(double n)=>n.ToString(CultureInfo.InvariantCulture);
    public async Task<List<City>> Search(string query,CancellationToken ct=default)
    {
        using var doc=JsonDocument.Parse(await Http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=12&language=zh&format=json",ct));
        if(!doc.RootElement.TryGetProperty("results",out var results))return await SearchDistrict(query,ct);
        return results.EnumerateArray().Select(r=>new City(r.GetProperty("name").GetString()!,r.GetProperty("latitude").GetDouble(),r.GetProperty("longitude").GetDouble(),string.Join(" · ",new[]{r.TryGetProperty("admin1",out var a)?a.GetString():"",r.TryGetProperty("country",out var c)?c.GetString():""}.Where(x=>!string.IsNullOrEmpty(x))))).ToList();
    }
    private static readonly SemaphoreSlim GeoGate=new(1,1);
    private static DateTime LastGeo;
    private static async Task<List<City>> SearchDistrict(string query,CancellationToken ct)
    {
        await GeoGate.WaitAsync(ct);try{
            var delay=1000-(DateTime.UtcNow-LastGeo).TotalMilliseconds;if(delay>0)await Task.Delay((int)delay,ct);LastGeo=DateTime.UtcNow;
            using var doc=JsonDocument.Parse(await Http.GetStringAsync($"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&format=jsonv2&limit=6&accept-language=zh-CN",ct));
            return doc.RootElement.EnumerateArray().Select(r=>new City(r.GetProperty("name").GetString()??query,double.Parse(r.GetProperty("lat").GetString()!,CultureInfo.InvariantCulture),double.Parse(r.GetProperty("lon").GetString()!,CultureInfo.InvariantCulture),r.GetProperty("display_name").GetString()??"",(r.GetProperty("display_name").GetString()??"").Contains("中国")?"Asia/Shanghai":"")).ToList();
        }finally{GeoGate.Release();}
    }
    public async Task<City> Locate(CancellationToken ct=default)
    {
        var access=await Geolocator.RequestAccessAsync().AsTask(ct);
        if(access!=GeolocationAccessStatus.Allowed)throw new InvalidOperationException("尚未获得定位权限，请在 Windows 设置中允许定位，或手动选择城市。");
        var locator=new Geolocator{DesiredAccuracy=PositionAccuracy.High};
        var pos=await locator.GetGeopositionAsync(TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(30)).AsTask(ct);
        App.Trace("location-source",$"source={pos.Coordinate.PositionSource}; accuracyMeters={pos.Coordinate.Accuracy:0}");
        var latitude=pos.Coordinate.Point.Position.Latitude;var longitude=pos.Coordinate.Point.Position.Longitude;
        await GeoGate.WaitAsync(ct);
        try
        {
            var delay=1000-(DateTime.UtcNow-LastGeo).TotalMilliseconds;if(delay>0)await Task.Delay((int)delay,ct);LastGeo=DateTime.UtcNow;
            using var doc=JsonDocument.Parse(await Http.GetStringAsync($"https://nominatim.openstreetmap.org/reverse?lat={Num(latitude)}&lon={Num(longitude)}&format=jsonv2&zoom=10&addressdetails=1&accept-language=zh-CN",ct));
            return GpsCityName.Resolve(doc.RootElement,latitude,longitude);
        }
        catch(Exception ex) when(!ct.IsCancellationRequested&&ex is (HttpRequestException or TaskCanceledException or JsonException)){return new City("当前位置",latitude,longitude);}
        finally{GeoGate.Release();}
    }
    public async Task<Forecast> Get(City city,CancellationToken ct=default)
    {
        if(App.Instance.Prefs.WeatherProvider=="Auto"){
            try{var remote=await WeatherProxyClient.Get(city,App.Instance.Prefs.WeatherProxyUrl,ct);SaveCache(city,remote,ct);return remote;}
            catch(Exception ex) when(!ct.IsCancellationRequested&&ex is (HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidDataException)){App.Trace("weather-proxy","unavailable; using public source");}
        }
        try{return await GetPrimary(city,ct);}
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or FormatException){
            if(ct.IsCancellationRequested)throw;
            App.Log("weather primary; trying MET Norway",ex);
            return await GetFallback(city,ct);
        }
    }
    private async Task<Forecast> GetPrimary(City city,CancellationToken ct=default)
    {
        var ll=$"latitude={Num(city.Latitude)}&longitude={Num(city.Longitude)}";
        var url=$"https://api.open-meteo.com/v1/forecast?{ll}&current=is_day,temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m,wind_direction_10m,pressure_msl&hourly=is_day,temperature_2m,weather_code,precipitation_probability&daily=sunrise,sunset,weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto&forecast_days=10&minutely_15=precipitation&forecast_minutely_15=12";
        using var doc=JsonDocument.Parse(await Http.GetStringAsync(url,ct));
        var root=doc.RootElement;var current=root.GetProperty("current");var hourly=root.GetProperty("hourly");var daily=root.GetProperty("daily");
        var cityNow=DateTime.Parse(current.GetProperty("time").GetString()!,CultureInfo.InvariantCulture);
        var hours=new List<HourForecast>();
        for(var i=0;i<hourly.GetProperty("time").GetArrayLength();i++) {
            var t=DateTime.Parse(hourly.GetProperty("time")[i].GetString()!,CultureInfo.InvariantCulture);
            if(t>=cityNow.AddMinutes(-59)&&hours.Count<24)hours.Add(new(t,hourly.GetProperty("temperature_2m")[i].GetDouble(),hourly.GetProperty("weather_code")[i].GetInt32(),hourly.GetProperty("precipitation_probability")[i].GetDouble(),hourly.TryGetProperty("is_day",out var hd)&&hd.GetArrayLength()>i?WeatherClock.DayValue(hd[i]):null));
        }
        var days=new List<DayForecast>();
        for(var i=0;i<daily.GetProperty("time").GetArrayLength();i++)days.Add(new(DateTime.Parse(daily.GetProperty("time")[i].GetString()!,CultureInfo.InvariantCulture),daily.GetProperty("temperature_2m_min")[i].GetDouble(),daily.GetProperty("temperature_2m_max")[i].GetDouble(),daily.GetProperty("weather_code")[i].GetInt32(),daily.GetProperty("precipitation_probability_max")[i].GetDouble()));
        double? aqi=null,pm25=null,pm10=null,uvIndex=null; string? airError=null;
        try{
            using var air=JsonDocument.Parse(await Http.GetStringAsync($"https://air-quality-api.open-meteo.com/v1/air-quality?{ll}&current=us_aqi,pm2_5,pm10,uv_index&timezone=auto",ct));
            var ac=air.RootElement.GetProperty("current");
            aqi=ReadNullable(ac,"us_aqi");pm25=ReadNullable(ac,"pm2_5");pm10=ReadNullable(ac,"pm10");uvIndex=ReadNullable(ac,"uv_index");
        }catch(Exception ex) when(!ct.IsCancellationRequested && ex is (HttpRequestException or TaskCanceledException or JsonException)){airError="空气质量暂时无法更新";}
        var forecast=new Forecast(current.GetProperty("temperature_2m").GetDouble(),current.GetProperty("apparent_temperature").GetDouble(),current.GetProperty("weather_code").GetInt32(),current.GetProperty("relative_humidity_2m").GetDouble(),current.GetProperty("wind_speed_10m").GetDouble(),current.GetProperty("wind_direction_10m").GetDouble(),hours.FirstOrDefault()?.Rain??0,hours,days,DateTime.Now,aqi,pm25,pm10,airError,ShortTermPrecipitation:Precipitation.Parse(root,DateTimeOffset.UtcNow),Sunrise:WeatherClock.DailyTime(daily,"sunrise"),Sunset:WeatherClock.DailyTime(daily,"sunset"),IsDay:WeatherClock.IsDay(current),PressureHpa:ReadNullable(current,"pressure_msl"),UvIndex:uvIndex);
        forecast=forecast with{Alerts=WeatherAlerts.Parse(root,"Open-Meteo")};SaveCache(city,forecast,ct);
        return forecast;
    }
    static double? ReadNullable(JsonElement e,string p)=>e.TryGetProperty(p,out var v)&&v.ValueKind==JsonValueKind.Number?v.GetDouble():null;
    private async Task<Forecast> GetFallback(City city,CancellationToken ct)
    {
        var url=$"https://api.met.no/weatherapi/locationforecast/2.0/compact?lat={Num(Math.Round(city.Latitude,4))}&lon={Num(Math.Round(city.Longitude,4))}";
        using var doc=JsonDocument.Parse(await Http.GetStringAsync(url,ct));var series=doc.RootElement.GetProperty("properties").GetProperty("timeseries");
        TimeZoneInfo zone=TimeZoneInfo.Utc;
        try{if(!string.IsNullOrWhiteSpace(city.TimeZone))zone=TimeZoneInfo.FindSystemTimeZoneById(city.TimeZone);else if(city.Region.Contains("中国")||city.Region.Contains("山东"))zone=TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");}catch{}
        var list=new List<HourForecast>();var today=TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,zone).Date;
        foreach(var item in series.EnumerateArray()){
            var t=TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(item.GetProperty("time").GetString()!,CultureInfo.InvariantCulture),zone).DateTime;
            if(t.Date<today)continue;var data=item.GetProperty("data");var temp=data.GetProperty("instant").GetProperty("details").GetProperty("air_temperature").GetDouble();
            var symbol="cloudy";foreach(var part in new[]{"next_1_hours","next_6_hours","next_12_hours"})if(data.TryGetProperty(part,out var next)){symbol=next.GetProperty("summary").GetProperty("symbol_code").GetString()!;break;}
            int code=symbol.Contains("thunder")?95:symbol.Contains("snow")?73:symbol.Contains("rain")?63:symbol.Contains("fog")?45:symbol.StartsWith("clearsky")?0:symbol.StartsWith("fair")?1:symbol.StartsWith("partlycloudy")?2:3;
            list.Add(new(t,temp,code,0));
        }
        var instant=series[0].GetProperty("data").GetProperty("instant").GetProperty("details");var temperature=instant.GetProperty("air_temperature").GetDouble();var humidity=instant.GetProperty("relative_humidity").GetDouble();var wind=instant.GetProperty("wind_speed").GetDouble();
        var feels=temperature+.33*(humidity/100*6.105*Math.Exp(17.27*temperature/(237.7+temperature)))-.7*wind-4;
        var days=list.GroupBy(h=>h.Time.Date).Select(g=>new DayForecast(g.Key,g.Min(h=>h.Temperature),g.Max(h=>h.Temperature),g.GroupBy(h=>h.Code).MaxBy(c=>c.Count())!.Key,0)).Take(10).ToList();
        double? aqi=null,pm25=null,pm10=null,uvIndex=null;string? error=null;
        try{using var air=JsonDocument.Parse(await Http.GetStringAsync($"https://air-quality-api.open-meteo.com/v1/air-quality?latitude={Num(city.Latitude)}&longitude={Num(city.Longitude)}&current=us_aqi,pm2_5,pm10,uv_index&timezone=auto",ct));var ac=air.RootElement.GetProperty("current");aqi=ReadNullable(ac,"us_aqi");pm25=ReadNullable(ac,"pm2_5");pm10=ReadNullable(ac,"pm10");uvIndex=ReadNullable(ac,"uv_index");}catch(Exception ex) when(!ct.IsCancellationRequested && ex is (HttpRequestException or TaskCanceledException or JsonException)){error="空气质量暂时无法更新";}
        var forecast=new Forecast(temperature,feels,list.FirstOrDefault()?.Code??3,humidity,wind*3.6,instant.GetProperty("wind_from_direction").GetDouble(),null,list.Take(24).ToList(),days,DateTime.Now,aqi,pm25,pm10,error,"MET Norway",true,zone.Id,PressureHpa:ReadNullable(instant,"air_pressure_at_sea_level"),UvIndex:uvIndex);
        forecast=forecast with{Alerts=WeatherAlerts.Parse(doc.RootElement,"MET Norway")};SaveCache(city,forecast,ct);return forecast;
    }
    private static void SaveCache(City city,Forecast forecast,CancellationToken ct){ct.ThrowIfCancellationRequested();try{AtomicStore.Write(CachePath(city),JsonSerializer.Serialize(forecast));}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){App.Log("weather cache",ex);}}
    static string CachePath(City c)=>Path.Combine(Preferences.DataPath,$"weather-{Num(c.Latitude)}-{Num(c.Longitude)}.json");
    public Forecast? Cached(City c){try{return JsonSerializer.Deserialize<Forecast>(File.ReadAllText(CachePath(c)));}catch{return null;}}
}
