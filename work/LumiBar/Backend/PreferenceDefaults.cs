using System.Text.Json;
namespace LumiBar;

// One embedded manifest supplies new-profile values. Per-user JSON still
// overrides individual properties, so installing an update never resets them.
internal static class PreferenceDefaults
{
    private static readonly JsonDocument Document=Load();
    internal static JsonElement Manifest=>Document.RootElement;
    private static JsonDocument Load()
    {
        using var stream=typeof(PreferenceDefaults).Assembly.GetManifestResourceStream("LumiBar.DefaultSettings.json");
        if(stream==null)return JsonDocument.Parse("{}");
        try{return JsonDocument.Parse(stream);}catch(JsonException){return JsonDocument.Parse("{}");}
    }
    internal static T Get<T>(string name,T fallback)
    {
        if(!Manifest.TryGetProperty(name,out var value))return fallback;
        try{
            if(typeof(T)==typeof(string))return value.ValueKind==JsonValueKind.Null?default!:(T)(object)value.GetString()!;
            if(typeof(T)==typeof(bool))return (T)(object)value.GetBoolean();
            if(typeof(T)==typeof(int))return (T)(object)value.GetInt32();
            if(typeof(T)==typeof(double))return (T)(object)value.GetDouble();
        }catch(Exception ex) when(ex is InvalidOperationException or FormatException or OverflowException){}
        return fallback;
    }
}
