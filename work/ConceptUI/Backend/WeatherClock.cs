using System.Globalization;
using System.Text.Json;

namespace LumiBar;
internal static class WeatherClock
{
    internal static DateTime? DailyTime(JsonElement daily,string name)
    {
        if(daily.ValueKind!=JsonValueKind.Object||!daily.TryGetProperty(name,out var list)||list.ValueKind!=JsonValueKind.Array||list.GetArrayLength()==0||list[0].ValueKind!=JsonValueKind.String)return null;
        return DateTime.TryParse(list[0].GetString(),CultureInfo.InvariantCulture,DateTimeStyles.None,out var value)&&value.Year>=2000?DateTime.SpecifyKind(value,DateTimeKind.Unspecified):null;
    }
    internal static bool? IsDay(JsonElement current)=>current.ValueKind==JsonValueKind.Object&&current.TryGetProperty("is_day",out var value)?DayValue(value):null;
    internal static bool? DayValue(JsonElement value)=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var n)&&n is 0 or 1?n==1:null;
}
