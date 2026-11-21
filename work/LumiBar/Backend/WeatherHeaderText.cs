namespace LumiBar;

internal static class WeatherHeaderText
{
    internal static string Status(string value)
    {
        const string prefix="上次更新";
        return value.StartsWith(prefix,StringComparison.Ordinal)?prefix+" "+value[prefix.Length..].TrimStart('：',':',' '):value;
    }
}
