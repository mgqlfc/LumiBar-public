using System.Text.Json;
namespace LumiBar;
public partial class App
{
    internal readonly PluginMenuRegistry PluginMenus=new();
    private long _lastPluginMenuCheck;
    internal void CheckPluginMenus(bool force=false)
    {
        var now=Environment.TickCount64;if(!force&&now-_lastPluginMenuCheck<1000)return;_lastPluginMenuCheck=now;
        if(!PluginMenus.Registrations.Any())return;
        var started=new Dictionary<int,long>();
        if(PluginMenus.Sweep(now,(pid,expected)=>{if(!started.TryGetValue(pid,out var value))started[pid]=value=RendererWindows.Started(pid);return value==expected;}))PluginMenusChanged();
    }
    internal PluginMenuRegistration RegisterPluginMenus(int pid,PluginMenuSpec spec)
    {
        CheckPluginMenus(true);var result=PluginMenus.Register(pid,RendererWindows.Started(pid),spec,Environment.TickCount64);PluginMenusChanged();return result;
    }
    internal PluginMenuRegistration RequirePluginMenus(int pid,string id,string token)
    {
        CheckPluginMenus(true);return PluginMenus.Require(pid,id,token,Environment.TickCount64);
    }
    internal object PluginMenuResponse(PluginMenuRegistration entry,bool includeToken=false)=>new{registrationId=entry.Id,registrationToken=includeToken?entry.Token:null,leaseDurationMs=PluginMenuRegistry.LeaseMs,renewAfterMs=PluginMenuRegistry.RenewAfterMs,expiresInMs=Math.Max(0,entry.Deadline-Environment.TickCount64),menu=PluginMenuRegistry.View(entry,Environment.TickCount64)};
    internal PluginMenuView[] PluginMenuViews(){CheckPluginMenus();return PluginMenus.Views(Environment.TickCount64);}
    internal void PluginMenusChanged(){SettingsWindow?.RefreshPluginMenus();_frontendApi?.Notify("pluginMenus");}
    internal bool InvokePluginMenu(PluginMenuView menu,PluginMenuItem item,string location)
    {
        CheckPluginMenus(true);
        if(!PluginMenus.Enqueue(menu.RegistrationId,menu.Revision,item.Id,location,Environment.TickCount64))return false;
        _frontendApi?.Notify("pluginMenuActions");return true;
    }
}
internal static class PluginMenuArguments
{
    internal static PluginMenuSpec Definition(JsonElement value)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw new FrontendApiException("invalid_params","菜单定义必须是对象。");
        try{return JsonSerializer.Deserialize<PluginMenuSpec>(value.GetRawText(),FrontendPipeServer.Json)??throw new JsonException();}
        catch(JsonException){throw new FrontendApiException("invalid_params","菜单定义字段格式有误。");}
    }
    internal static long Cursor(JsonElement args,string key,long fallback=0)
    {
        if(!args.TryGetProperty(key,out var value))return fallback;
        if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt64(out var number)||number<0)throw new FrontendApiException("invalid_params",$"{key} 必须为非负整数。");return number;
    }
}
