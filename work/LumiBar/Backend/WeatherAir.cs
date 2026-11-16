namespace LumiBar;
internal static class WeatherAir
{
    internal static string Caption(Forecast f)
    {
        if(f.Aqi==null)return "暂无数据";
        var category=f.AqiCategory;
        if(string.IsNullOrWhiteSpace(category))category=f.AqiStandard=="CN AQI"?f.Aqi<=50?"优":f.Aqi<=100?"良":f.Aqi<=150?"轻度污染":f.Aqi<=200?"中度污染":f.Aqi<=300?"重度污染":"严重污染":f.Aqi<=50?"优":f.Aqi<=100?"中等":f.Aqi<=150?"敏感人群":f.Aqi<=200?"不健康":"很不健康";
        return category;
    }
}
