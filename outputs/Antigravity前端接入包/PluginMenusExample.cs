using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace LumiBar.Frontend.Sdk;

/// <summary>Compile beside LumiBarClient.cs. Call once from the frontend process,
/// observe the returned task, and cancel/await it before exiting. No UI or volume writes.</summary>
public static class PluginMenusExample
{
    public sealed record Click(long Sequence,string ActionId,string Location,long MenuRevision,bool? ProposedChecked);
    public static async Task RunAsync(string endpointFile,Func<Click,CancellationToken,Task> onClick,CancellationToken ct)
    {
        using var control=new LumiBarClient();using var events=new LumiBarClient();
        var hello=await control.ConnectAsync(endpointFile,ct);await events.ConnectAsync(endpointFile,ct);
        if(!hello.GetProperty("capabilities").TryGetProperty("pluginMenus",out var supported)||!supported.GetBoolean())throw new NotSupportedException("This backend has no plugin menu API.");
        var pinned=false;
        object Definition()=>new{pluginId="antigravity.lumibar-glass",displayName="Antigravity",navigationTitle="Antigravity",navigationDescription="界面、歌词和扩展操作。",items=new object[]{
            new{id="open-settings",label="前端设置",locations=new[]{"tray","settings"}},
            new{id="pin-lyrics",label="固定歌词",@checked=pinned,locations=new[]{"tray","settings"}},
            new{kind="separator"},
            new{id="tools",label="更多操作",kind="submenu",children=new[]{new{id="show-workbench",label="打开预览工作台"}}}
        }};
        var registration=await control.RequestAsync("menus.register",Definition(),ct);
        var id=registration.GetProperty("registrationId").GetString()!;var token=registration.GetProperty("registrationToken").GetString()!;
        var instance=hello.GetProperty("instanceId").GetString();
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var stop=lifetime.Token;
        // This timer is independent of events.wait and of potentially slow UI handlers.
        var heartbeat=Task.Run(async()=>{
            try{while(true){await Task.Delay(5000,stop);await control.RequestAsync("menus.renew",new{registrationId=id,registrationToken=token},stop);}}
            catch{lifetime.Cancel();throw;}
        },CancellationToken.None);
        try{
            var state=await control.RequestAsync("state.get",null,stop);var version=state.GetProperty("stateVersion").GetInt64();long after=0;
            while(!stop.IsCancellationRequested){
                var batch=await control.RequestAsync("menus.actions.read",new{registrationId=id,registrationToken=token,afterSequence=after,limit=32},stop);
                foreach(var item in batch.GetProperty("actions").EnumerateArray()){
                    var click=JsonSerializer.Deserialize<Click>(item.GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
                    // Marshal onto the frontend dispatcher inside onClick when opening a window.
                    // Deduplicate by (registrationId, sequence) if the app retries a handler.
                    await onClick(click,stop);
                    if(click.ActionId=="pin-lyrics"&&click.ProposedChecked is {} next){pinned=next;await control.RequestAsync("menus.update",new{registrationId=id,registrationToken=token,definition=Definition()},stop);}
                    await control.RequestAsync("menus.actions.ack",new{registrationId=id,registrationToken=token,throughSequence=click.Sequence},stop);after=click.Sequence;
                }
                if(batch.GetProperty("hasMore").GetBoolean())continue;
                var change=await events.RequestAsync("events.wait",new{afterVersion=version,timeoutMs=25000},stop);
                if(change.GetProperty("instanceId").GetString()!=instance)throw new InvalidOperationException("Backend instance changed; create a new menu registration.");
                version=change.GetProperty("version").GetInt64();
            }
        }
        finally{
            lifetime.Cancel();
            try{await heartbeat;}catch(OperationCanceledException){}finally{
                using var cleanup=new CancellationTokenSource(2500);
                // A cancelled read may leave an old response on the control pipe.
                // Use a new same-process connection for graceful unregister.
                try{using var finalClient=new LumiBarClient();await finalClient.ConnectAsync(endpointFile,cleanup.Token);await finalClient.RequestAsync("menus.unregister",new{registrationId=id,registrationToken=token},cleanup.Token);}catch{}
            }
        }
    }
}
