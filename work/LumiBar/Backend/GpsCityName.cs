using System.Text.Json;
namespace LumiBar;
internal static class GpsCityName
{
    internal static City Resolve(JsonElement root,double latitude,double longitude)
    {
        if(!root.TryGetProperty("address",out var address))return new("当前位置",latitude,longitude);
        string Read(string key)=>address.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
        var name=new[]{"city_district","town","city","county","village","suburb"}.Select(Read).FirstOrDefault(s=>!string.IsNullOrWhiteSpace(s))??"当前位置";
        var region=string.Join(" · ",new[]{Read("state"),Read("city"),Read("country")}.Where(s=>s.Length>0&&s!=name).Distinct());
        return new(name,latitude,longitude,region,Read("country_code")=="cn"?"Asia/Shanghai":"");
    }
}
