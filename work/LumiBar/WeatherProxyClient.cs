using System.Globalization;
using System.Text.Json;
namespace LumiBar;
internal static class WeatherProxyClient
{
    internal static async Task<Forecast> Get(City city,string address,CancellationToken ct)
    {
        var root=await Read(city,address,"forecast",ct);
        return WeatherProxyPayload.Read(root);
    }
    internal static async Task<List<WeatherAlert>> Alerts(City city,string address,CancellationToken ct)
    {
        var root=await Read(city,address,"alerts",ct);
        var alerts=root.GetProperty("alerts").Deserialize<List<WeatherAlert>>(FrontendPipeServer.Json);
        return WeatherAlerts.Current(alerts,DateTimeOffset.UtcNow).ToList();
    }
    private static async Task<JsonElement> Read(City city,string address,string path,CancellationToken ct)
    {
        var url=WeatherProxyAddress.Normalize(address)??throw new InvalidOperationException("请填写有效的天气服务 HTTPS 地址。");
        url+=$"v1/{path}?lat={city.Latitude.ToString("F2",CultureInfo.InvariantCulture)}&lon={city.Longitude.ToString("F2",CultureInfo.InvariantCulture)}";
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);linked.CancelAfter(TimeSpan.FromSeconds(13));
        using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.Add("X-LumiBar-Client","LumiBar");
        using var response=await WeatherService.Http.SendAsync(request,linked.Token);
        if(!response.IsSuccessStatusCode)throw new HttpRequestException($"云端天气暂不可用（{(int)response.StatusCode}）。");
        var bytes=await response.Content.ReadAsByteArrayAsync(linked.Token);if(bytes.Length>2000000)throw new InvalidDataException("云端天气响应过大。");
        using var doc=JsonDocument.Parse(bytes);return doc.RootElement.Clone();
    }
}
