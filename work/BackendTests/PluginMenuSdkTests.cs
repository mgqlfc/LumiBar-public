using System.Text.Json;
using LumiBar;
using LumiBar.Frontend.Sdk;
internal static class PluginMenuSdkTests
{
    internal static async Task<List<string>> Run()
    {
        var results=new List<string>();void Check(bool value,string name)=>results.Add($"{(value?"PASS":"FAIL")} {name}");
        var menus=new PluginMenuRegistry();var gate=new object();var feed=new ChangeFeed();var peers=new HashSet<Guid>();var renewals=0;var handlerFinished=false;
        var registered=new TaskCompletionSource<PluginMenuRegistration>(TaskCreationOptions.RunContinuationsAsynchronously);var acknowledged=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<object?> Handle(FrontendPeer peer,string method,JsonElement args,CancellationToken ct){
            if(method=="events.wait")return await feed.Wait(args.GetProperty("afterVersion").GetInt64(),25000,ct);
            if(method=="menus.actions.ack"){
                lock(gate){var owner=menus.Require(peer.ProcessId,args.GetProperty("registrationId").GetString()!,args.GetProperty("registrationToken").GetString()!,Environment.TickCount64);menus.Acknowledge(owner,args.GetProperty("throughSequence").GetInt64());acknowledged.TrySetResult();}
                // Cancellation deliberately races an outstanding response.
                await Task.Delay(150,ct);return new{accepted=true};
            }
            lock(gate){
                peers.Add(peer.ConnectionId);
                PluginMenuRegistration Auth()=>menus.Require(peer.ProcessId,args.GetProperty("registrationId").GetString()!,args.GetProperty("registrationToken").GetString()!,Environment.TickCount64);
                switch(method){
                    case "hello":return new{instanceId=feed.InstanceId,capabilities=new{pluginMenus=true}};
                    case "state.get":return new{stateVersion=feed.Version};
                    case "menus.register":var entry=menus.Register(peer.ProcessId,123,JsonSerializer.Deserialize<PluginMenuSpec>(args.GetRawText(),FrontendPipeServer.Json)!,Environment.TickCount64);registered.TrySetResult(entry);return new{registrationId=entry.Id,registrationToken=entry.Token};
                    case "menus.renew":menus.Renew(Auth(),Environment.TickCount64);renewals++;return new{accepted=true};
                    case "menus.actions.read":return menus.Read(Auth(),args.GetProperty("afterSequence").GetInt64(),32);
                    case "menus.update":menus.Update(Auth(),JsonSerializer.Deserialize<PluginMenuSpec>(args.GetProperty("definition").GetRawText(),FrontendPipeServer.Json)!,Environment.TickCount64);return new{accepted=true};
                    case "menus.unregister":menus.Remove(Auth().Id);return new{removed=true};
                    default:throw new FrontendApiException("method_not_found","test");
                }
            }
        }
        var name="LumiBar.Frontend.v1.MenuSdk."+Guid.NewGuid().ToString("N");using var server=new FrontendPipeServer(name,Handle);server.Start();
        var folder=Path.Combine(Path.GetTempPath(),"lumibar-menu-sdk-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var endpoint=Path.Combine(folder,"frontend-endpoint.json");
        await File.WriteAllTextAsync(endpoint,JsonSerializer.Serialize(new{protocolVersion=1,pipeName=name}));using var cancel=new CancellationTokenSource();
        var received="";
        var run=PluginMenusExample.RunAsync(endpoint,async(click,ct)=>{received=click.ActionId;await Task.Delay(5700,ct);handlerFinished=true;},cancel.Token);
        try{
            var entry=await registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            lock(gate){Check(entry.Spec.PluginId=="antigravity.lumibar-glass"&&entry.Spec.Items.Any(x=>x.Id=="pin-lyrics"),"Delivered menu SDK example registers its real JSON schema");menus.Enqueue(entry.Id,1,"pin-lyrics","settings",Environment.TickCount64);}
            feed.Publish("pluginMenuActions");await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(15));
            lock(gate){
                Check(received=="pin-lyrics"&&handlerFinished,"Delivered menu SDK dispatches the requested action and waits for its handler");
                Check(renewals>0,"Menu SDK heartbeat continues while a slow UI action is awaited");
                Check(entry.Spec.Items.Single(x=>x.Id=="pin-lyrics").Checked==true,"Menu SDK updates native check state after handling the action");
                Check(entry.Actions.Count==0&&entry.Acknowledged==1,"Menu SDK acknowledges successfully completed actions");
                Check(peers.Count>=2,"Menu SDK keeps event waits on an independent connection");
            }
        }finally{cancel.Cancel();try{await run.WaitAsync(TimeSpan.FromSeconds(5));}catch(OperationCanceledException){}}
        lock(gate)Check(!menus.Registrations.Any(),"Menu SDK cancellation during a pending response unregisters on a fresh pipe");
        return results;
    }
}
