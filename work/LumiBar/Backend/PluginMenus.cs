using System.Security.Cryptography;
using System.Text;
namespace LumiBar;

internal sealed record PluginMenuItem(string Id="",string Label="",string Kind="action",bool Enabled=true,bool? Checked=null,string[]? Locations=null,PluginMenuItem[]? Children=null);
internal sealed record PluginMenuSpec(string PluginId,string DisplayName,PluginMenuItem[] Items,string? NavigationTitle=null,string? NavigationDescription=null);
internal sealed record PluginMenuView(string RegistrationId,long Revision,string PluginId,string DisplayName,string NavigationTitle,string NavigationDescription,PluginMenuItem[] Items,int ExpiresInMs);
internal sealed record PluginMenuAction(long Sequence,string ActionId,string Location,long MenuRevision,bool? ProposedChecked,DateTimeOffset CreatedAt);
internal sealed class PluginMenuRegistration
{
    internal string Id {get;}=Guid.NewGuid().ToString("N");
    internal string Token {get;}=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal required int Pid;internal required long Started;internal required PluginMenuSpec Spec;
    internal long Deadline,Revision=1,Sequence,Acknowledged;
    internal Queue<PluginMenuAction> Actions=new();
}
// UI-thread owned. Identity belongs to the verified process, not one pipe.
// Registrations are deliberately independent of exclusive taskbar ownership.
internal sealed class PluginMenuRegistry
{
    internal const int LeaseMs=15000,RenewAfterMs=5000,MaxRegistrations=8,MaxItems=32,MaxPendingActions=64;
    private readonly Dictionary<string,PluginMenuRegistration> _items=new(StringComparer.Ordinal);
    internal IEnumerable<PluginMenuRegistration> Registrations=>_items.Values;
    private static FrontendApiException Invalid(string message)=>new("invalid_params",message);
    private static string Label(string? value,int max,string field,bool optional=false)
    {
        var text=value?.Trim()??"";
        if((!optional&&text.Length==0)||text.Length>max||text.Any(char.IsControl))throw Invalid($"{field} 长度或字符有误。");return text;
    }
    internal static PluginMenuSpec Validate(PluginMenuSpec spec)
    {
        if(spec==null)throw Invalid("缺少菜单定义。");
        var id=Label(spec.PluginId,128,"pluginId");
        if(id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ('.' or '-' or '_')))throw Invalid("pluginId 只接受字母、数字、点、横线和下划线。");
        var name=Label(spec.DisplayName,48,"displayName");var title=Label(spec.NavigationTitle,48,"navigationTitle",true);var description=Label(spec.NavigationDescription,200,"navigationDescription",true);
        if(spec.Items==null||spec.Items.Length==0)throw Invalid("至少提供一个菜单项。");
        var ids=new HashSet<string>(StringComparer.Ordinal);var count=0;
        PluginMenuItem[] Walk(PluginMenuItem[] source,int depth,string[] inherited)
        {
            if(depth>2)throw Invalid("菜单最多支持两级子菜单。");
            return source.Select(item=>{
                if(item==null||++count>MaxItems)throw Invalid($"每个插件最多注册 {MaxItems} 个菜单节点。");
                if(item.Kind is not ("action" or "separator" or "submenu"))throw Invalid("菜单类型应为 action、separator 或 submenu。");
                var locations=item.Locations??inherited;
                if(locations.Length==0||locations.Any(x=>x is not ("tray" or "settings"))||locations.Distinct().Count()!=locations.Length)throw Invalid("菜单位置只支持 tray 和 settings。");
                if(depth>0&&locations.Any(x=>!inherited.Contains(x)))throw Invalid("子菜单的位置不能超出父菜单。");
                if(item.Kind=="separator"){
                    if(item.Checked!=null||item.Children?.Length>0)throw Invalid("分隔线不接受子项或勾选状态。");
                    return new PluginMenuItem(Kind:"separator",Locations:locations.ToArray());
                }
                var actionId=Label(item.Id,64,"菜单 id");
                if(!ids.Add(actionId))throw Invalid("同一插件内的菜单 id 不能重复。");
                var label=Label(item.Label,80,"菜单文字");
                if(item.Kind=="submenu"){
                    if(item.Checked!=null||item.Children==null||item.Children.Length==0)throw Invalid("子菜单需要 children，且不接受勾选状态。");
                    return new(actionId,label,"submenu",item.Enabled,null,locations.ToArray(),Walk(item.Children,depth+1,locations));
                }
                if(item.Children?.Length>0)throw Invalid("普通菜单项不能包含 children。");
                return new(actionId,label,"action",item.Enabled,item.Checked,locations.ToArray());
            }).ToArray();
        }
        var items=Walk(spec.Items,0,["tray","settings"]);
        bool HasAction(IEnumerable<PluginMenuItem> nodes)=>nodes.Any(x=>x.Kind=="action"||x.Children!=null&&HasAction(x.Children));
        if(!HasAction(items))throw Invalid("菜单中至少需要一个操作。");
        return new(id,name,items,title.Length==0?name:title,description);
    }
    internal PluginMenuRegistration Register(int pid,long started,PluginMenuSpec spec,long now)
    {
        spec=Validate(spec);
        if(pid<=0||started<=0)throw new FrontendApiException("peer_mismatch","无法核对插件进程。");
        if(_items.Values.Any(x=>x.Spec.PluginId==spec.PluginId))throw new FrontendApiException("menu_already_registered","此插件已经注册菜单。");
        if(_items.Count>=MaxRegistrations)throw new FrontendApiException("menu_limit","已达到同时注册的插件数量上限。");
        var entry=new PluginMenuRegistration{Pid=pid,Started=started,Spec=spec,Deadline=now+LeaseMs};_items.Add(entry.Id,entry);return entry;
    }
    internal PluginMenuRegistration Require(int pid,string id,string token,long now)
    {
        if(!_items.TryGetValue(id,out var entry)||entry.Pid!=pid||!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(entry.Token),Encoding.UTF8.GetBytes(token)))throw new FrontendApiException("menu_registration_invalid","菜单注册无效或不属于当前进程。");
        if(now>=entry.Deadline)throw new FrontendApiException("menu_registration_expired","菜单注册已过期，请重新注册。");return entry;
    }
    internal void Update(PluginMenuRegistration entry,PluginMenuSpec spec,long now)
    {
        spec=Validate(spec);if(spec.PluginId!=entry.Spec.PluginId)throw Invalid("更新时不能更改 pluginId。");
        entry.Spec=spec;entry.Revision++;entry.Deadline=now+LeaseMs;
    }
    internal void Renew(PluginMenuRegistration entry,long now)=>entry.Deadline=now+LeaseMs;
    internal bool Remove(string id)=>_items.Remove(id);
    internal bool Sweep(long now,Func<int,long,bool> alive)
    {
        var dead=_items.Values.Where(x=>now>=x.Deadline||!alive(x.Pid,x.Started)).Select(x=>x.Id).ToArray();foreach(var id in dead)_items.Remove(id);return dead.Length>0;
    }
    internal PluginMenuView[] Views(long now)=>_items.Values.Where(x=>now<x.Deadline).Select(x=>View(x,now)).ToArray();
    internal static PluginMenuView View(PluginMenuRegistration x,long now)=>new(x.Id,x.Revision,x.Spec.PluginId,x.Spec.DisplayName,x.Spec.NavigationTitle!,x.Spec.NavigationDescription!,x.Spec.Items,(int)Math.Max(0,x.Deadline-now));
    internal static PluginMenuItem[] ForLocation(IEnumerable<PluginMenuItem> items,string location)
    {
        var result=new List<PluginMenuItem>();
        foreach(var item in items){
            if(item.Locations?.Contains(location)!=true)continue;
            var filtered=item.Kind=="submenu"?item with{Children=ForLocation(item.Children!,location)}:item;
            if(filtered.Kind=="submenu"&&filtered.Children!.Length==0)continue;
            if(filtered.Kind=="separator"&&(result.Count==0||result[^1].Kind=="separator"))continue;
            result.Add(filtered);
        }
        if(result.Count>0&&result[^1].Kind=="separator")result.RemoveAt(result.Count-1);return result.ToArray();
    }
    internal bool Enqueue(string registrationId,long revision,string actionId,string location,long now)
    {
        if(!_items.TryGetValue(registrationId,out var entry)||now>=entry.Deadline||entry.Revision!=revision||entry.Actions.Count>=MaxPendingActions||location is not ("tray" or "settings"))return false;
        PluginMenuItem? Find(IEnumerable<PluginMenuItem> nodes){foreach(var item in nodes){if(!item.Enabled||item.Locations?.Contains(location)!=true)continue;if(item.Kind=="action"&&item.Id==actionId)return item;if(item.Children!=null&&Find(item.Children) is {} found)return found;}return null;}
        var action=Find(entry.Spec.Items);if(action==null)return false;
        entry.Actions.Enqueue(new(++entry.Sequence,action.Id,location,entry.Revision,action.Checked is {} check?!check:null,DateTimeOffset.UtcNow));return true;
    }
    internal object Read(PluginMenuRegistration entry,long after,int limit)
    {
        if(after<0||after>entry.Sequence||limit is <1 or >64)throw Invalid("菜单操作读取范围有误。");
        var events=entry.Actions.Where(x=>x.Sequence>after).Take(limit).ToArray();var cursor=events.LastOrDefault()?.Sequence??Math.Max(after,entry.Acknowledged);
        return new{registrationId=entry.Id,actions=events,nextSequence=cursor,acknowledgedSequence=entry.Acknowledged,hasMore=entry.Actions.Any(x=>x.Sequence>cursor),resyncRequired=after<entry.Acknowledged};
    }
    internal void Acknowledge(PluginMenuRegistration entry,long through)
    {
        if(through<0||through>entry.Sequence)throw Invalid("确认序号超出已产生的操作范围。");
        while(entry.Actions.Count>0&&entry.Actions.Peek().Sequence<=through)entry.Actions.Dequeue();entry.Acknowledged=Math.Max(entry.Acknowledged,through);
    }
}
