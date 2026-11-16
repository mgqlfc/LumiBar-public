using System.Text.Json;
using System.Text.Json.Nodes;
namespace LumiBar;

internal sealed record PreferenceChanges(string[] Fields)
{
    internal bool Any=>Fields.Length>0;
    internal bool GlassOnly=>Fields.Length==1&&Fields[0]==nameof(Preferences.GlassOpacity);
    internal bool AccentOnly=>Fields.Length==1&&Fields[0]==nameof(Preferences.AccentColor);
    internal static PreferenceChanges Between(Preferences previous,Preferences next)
    {
        var before=JsonSerializer.SerializeToNode(previous)!.AsObject();
        var after=JsonSerializer.SerializeToNode(next)!.AsObject();
        return new(after.Where(p=>!Equal(before[p.Key],p.Value)).Select(p=>p.Key).ToArray());
    }
    private static bool Equal(JsonNode? a,JsonNode? b)
    {
        if(a is JsonValue av&&b is JsonValue bv&&av.TryGetValue<double>(out var x)&&bv.TryGetValue<double>(out var y)&&double.IsFinite(x)&&double.IsFinite(y))
            return Math.Abs(x-y)<1e-8;
        return JsonNode.DeepEquals(a,b);
    }
}
