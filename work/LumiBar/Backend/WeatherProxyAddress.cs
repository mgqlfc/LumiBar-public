namespace LumiBar;
internal static class WeatherProxyAddress
{
    internal const string Default="https://124.156.224.125/lumibar-weather/";
    internal static string? Normalize(string? text)
    {
        if(!Uri.TryCreate(text?.Trim(),UriKind.Absolute,out var uri)||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0||uri.Scheme!="https"&&!(uri.Scheme=="http"&&uri.IsLoopback))return null;
        return uri.AbsoluteUri.TrimEnd('/')+"/";
    }
}
