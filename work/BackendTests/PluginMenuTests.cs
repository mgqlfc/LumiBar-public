using System.Text.Json;
using LumiBar;
internal static class PluginMenuTests
{
    internal static List<string> Run()
    {
        var results=new List<string>();
        void Check(bool ok,string name)=>results.Add($"{(ok?"PASS":"FAIL")} {name}");
        bool Error(Action action,string code="invalid_params"){try{action();return false;}catch(FrontendApiException ex){return ex.Code==code;}}
        PluginMenuSpec Spec(string id="test.plugin")=>new(id,"测试插件",[
            new(Kind:"separator"),new("open","前端设置"),new("check","固定歌词",Checked:false),
            new("disabled","暂不可用",Enabled:false),new("nested","更多","submenu",Children:[new("nested.open","更多设置")]),new(Kind:"separator")],"插件设置","可用操作");
        var menus=new PluginMenuRegistry();var entry=menus.Register(11,123,Spec(),100);
        Check(entry.Spec.Items[1].Locations!.SequenceEqual(new[]{"tray","settings"}),"Menu defaults contain only tray and settings; no capsule context target");
        Check(entry.Deadline==100+PluginMenuRegistry.LeaseMs&&entry.Token.Length==64,"Plugin menu has a bounded renewable process lease and random token");
        Check(ReferenceEquals(menus.Require(11,entry.Id,entry.Token,200),entry),"Menu credentials work independently of a pipe connection");
        Check(Error(()=>menus.Require(12,entry.Id,entry.Token,200),"menu_registration_invalid"),"Another process cannot read or change a plugin menu registration");
        Check(Error(()=>menus.Require(11,entry.Id,"bad",200),"menu_registration_invalid"),"Incorrect menu token is rejected");
        Check(Error(()=>menus.Require(11,entry.Id,entry.Token,entry.Deadline),"menu_registration_expired"),"Menu deadline is exclusive and uses the supplied monotonic clock");
        Check(Error(()=>menus.Register(12,234,Spec(),200),"menu_already_registered"),"A second process cannot replace an existing plugin id");
        Check(Error(()=>PluginMenuRegistry.Validate(Spec() with{Items=[new("a","A",Locations:["music"])]})),"Capsule right-click menu location is rejected");
        Check(Error(()=>PluginMenuRegistry.Validate(Spec() with{Items=[new("a","A"),new("a","B")]})),"Duplicate action ids are rejected across a plugin definition");
        Check(Error(()=>PluginMenuRegistry.Validate(Spec() with{DisplayName="bad\nname"})),"Menu labels reject control characters");
        Check(Error(()=>PluginMenuRegistry.Validate(Spec() with{PluginId="bad/id"})),"Plugin menu identities cannot contain path syntax");
        Check(Error(()=>PluginMenuRegistry.Validate(Spec() with{Items=[new(Kind:"separator")]})),"Separator-only menus are rejected");
        Check(Error(()=>PluginMenuRegistry.Validate(Spec() with{Items=[new("a","A","submenu")]})),"Empty submenus are rejected");
        Check(Error(()=>PluginMenuRegistry.Validate(Spec() with{Items=[new("a","A",Children:[new("b","B")])]})),"Action nodes cannot contain hidden child commands");
        Check(Error(()=>PluginMenuRegistry.Validate(Spec() with{Items=Enumerable.Range(0,33).Select(i=>new PluginMenuItem(i.ToString(),"A")).ToArray()})),"Plugin menus enforce a bounded node count");
        Check(Error(()=>PluginMenuRegistry.Validate(Spec() with{Items=[new("a","A","submenu",Children:[new("b","B","submenu",Children:[new("c","C","submenu",Children:[new("d","D")])])])]})),"Excessively deep menus are rejected");
        var filtered=PluginMenuRegistry.ForLocation(entry.Spec.Items,"tray");
        Check(filtered.First().Kind!="separator"&&filtered.Last().Kind!="separator","Menu projection trims leading and trailing separators");
        var settingsOnly=PluginMenuRegistry.Validate(Spec() with{Items=[new("s","S",Locations:["settings"])]});
        Check(PluginMenuRegistry.ForLocation(settingsOnly.Items,"tray").Length==0&&PluginMenuRegistry.ForLocation(settingsOnly.Items,"settings").Length==1,"Tray and settings menu locations are independently selectable");
        Check(menus.Enqueue(entry.Id,1,"open","tray",300),"Tray click enqueues a plugin action without executing a command");
        Check(menus.Enqueue(entry.Id,1,"check","settings",301)&&entry.Actions.Last().ProposedChecked==true,"Checkable menu reports the proposed value without changing plugin state");
        Check(!menus.Enqueue(entry.Id,1,"disabled","tray",302),"Disabled menu actions cannot be dispatched");
        Check(!menus.Enqueue(entry.Id,1,"missing","tray",302),"Unregistered menu actions cannot be dispatched");
        Check(menus.Enqueue(entry.Id,1,"nested.open","settings",303),"Nested menu actions reach the correct registration");
        Check(!menus.Enqueue(entry.Id,1,"open","music",304),"Host rejects commands from a removed capsule location");
        var read=JsonSerializer.SerializeToElement(menus.Read(entry,0,2),FrontendPipeServer.Json);
        Check(read.GetProperty("actions").GetArrayLength()==2&&read.GetProperty("hasMore").GetBoolean(),"Action reads support bounded pagination");
        Check(JsonSerializer.Serialize(menus.Read(entry,0,2),FrontendPipeServer.Json)==read.GetRawText(),"Unacknowledged action reads replay the same sequence");
        menus.Acknowledge(entry,2);menus.Acknowledge(entry,1);
        Check(entry.Acknowledged==2&&entry.Actions.Count==1&&entry.Actions.Peek().Sequence==3,"Action acknowledgement is monotonic and only clears confirmed actions");
        Check(Error(()=>menus.Acknowledge(entry,99)),"Future acknowledgements cannot discard unseen actions");
        Check(JsonSerializer.SerializeToElement(menus.Read(entry,0,32),FrontendPipeServer.Json).GetProperty("resyncRequired").GetBoolean(),"An old action cursor explicitly reports already acknowledged history");
        var previous=entry.Spec;Check(Error(()=>menus.Update(entry,Spec("other.plugin"),350))&&entry.Spec==previous,"Invalid updates preserve the previous complete menu");
        menus.Update(entry,Spec() with{NavigationTitle="已更新"},400);
        Check(entry.Revision==2&&entry.Spec.NavigationTitle=="已更新","Menu updates atomically replace definitions and advance revision");
        Check(!menus.Enqueue(entry.Id,1,"open","tray",500)&&menus.Enqueue(entry.Id,2,"open","tray",500),"A stale open native menu cannot invoke an updated definition");
        Check(entry.Actions.Last().MenuRevision==2,"Queued actions carry the definition revision for frontend validation");
        menus.Acknowledge(entry,entry.Sequence);
        for(var i=0;i<PluginMenuRegistry.MaxPendingActions;i++)menus.Enqueue(entry.Id,2,"open","tray",600);
        Check(!menus.Enqueue(entry.Id,2,"open","tray",600)&&entry.Actions.Count==64,"A full queue rejects new actions without silently dropping old ones");
        menus.Acknowledge(entry,entry.Sequence);Check(menus.Enqueue(entry.Id,2,"open","tray",700),"Acknowledgement releases action queue capacity");
        menus.Renew(entry,800);Check(entry.Deadline==800+PluginMenuRegistry.LeaseMs,"Menu heartbeat renews from the current monotonic time");
        var serialized=JsonSerializer.Serialize(menus.Views(900),FrontendPipeServer.Json);
        Check(!serialized.Contains(entry.Token)&&!serialized.Contains("registrationToken"),"Public menu catalogue never exposes registration secrets");
        Check(!menus.Sweep(900,(_,started)=>started==123),"A live plugin retains menus independently of renderer activation");
        Check(menus.Sweep(901,(_,_)=>false)&&menus.Views(901).Length==0,"Plugin process exit removes menu registrations and queued actions");
        var expired=menus.Register(11,456,Spec(),1000);Check(menus.Sweep(expired.Deadline,(_,_)=>true),"Unresponsive plugin menu expires even if its process still exists");
        var removed=menus.Register(11,789,Spec(),2000);menus.Remove(removed.Id);
        Check(Error(()=>menus.Require(11,removed.Id,removed.Token,2100),"menu_registration_invalid"),"Unregistered menu credentials cannot be reused");
        var bounded=new PluginMenuRegistry();for(var i=0;i<8;i++)bounded.Register(11,123,Spec("plugin."+i),0);
        Check(Error(()=>bounded.Register(11,123,Spec("plugin.9"),0),"menu_limit"),"Total plugin registration count is bounded");
        return results;
    }
}
