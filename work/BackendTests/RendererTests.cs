using LumiBar;
using LumiBar.Frontend.Sdk;
using System.Text.Json;
internal static class RendererTests
{
    internal static async Task<List<string>> Run()
    {
        var results=new List<string>();void Check(bool ok,string title)=>results.Add($"{(ok?"PASS":"FAIL")} {title}");
        void Reject(Action action,string code,string title){try{action();Check(false,title);}catch(FrontendApiException ex){Check(ex.Code==code,title);}}
        var store=new RendererLeaseStore();var lease=store.Register(10,100,"test",101,102,new(),1000);
        Check(!lease.Active&&lease.Deadline==6000,"Renderer registration stays pending until activation");
        Reject(()=>store.Register(20,200,"other",201,202,new(),1001),"renderer_busy","A second renderer cannot steal the exclusive lease");
        Reject(()=>store.Require(20,lease.Id,lease.Token,1100),"lease_invalid","Lease token from another pipe PID is rejected");
        Reject(()=>store.Require(10,lease.Id,"wrong",1100),"lease_invalid","Incorrect lease secret cannot renew or release renderer");
        Check(ReferenceEquals(store.Require(10,lease.Id,lease.Token,1100),lease),"Same verified process can use a different pipe for heartbeat");
        store.Renew(lease,2000);lease.Active=true;
        Reject(()=>store.Require(10,lease.Id,lease.Token,7000),"lease_expired","Lease expiration uses monotonic deadline and rejects late heartbeat");
        Check(store.Release("heartbeat_timeout")==lease&&store.Current==null&&store.LastReleaseReason=="heartbeat_timeout","Expired renderer releases ownership for builtin fallback");
        var fresh=store.Register(10,100,"test",101,102,new(),8000);
        Reject(()=>store.Require(10,lease.Id,lease.Token,8001),"lease_invalid","Old lease cannot affect a newer registration from same PID");
        Reject(()=>new RendererSizes(MusicFullDip:40,MusicLiteDip:48).Validate(),"invalid_params","Renderer Lite width must fit full width");
        Reject(()=>new RendererSizes(HeightDip:double.NaN).Validate(),"invalid_params","Non-finite renderer dimensions are rejected");
        var plan=new ReservationLayoutPolicy().Calculate(new(0,0,600,60),500,600,new(300,0,600,60),1,104,238,8,true,70,60);
        Check(plan.Capsules.Lite&&plan.Capsules.MusicWidth==60&&plan.Capsules.WeatherWidth==70,"Reservation honors both external Lite widths");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeName="LumiBar.Frontend.v1."+Guid.NewGuid().ToString("N");
        using var server=new FrontendPipeServer(pipeName,async(peer,method,args,ct)=>{
            if(method=="events.wait"){entered.TrySetResult();await finish.Task.WaitAsync(ct);return new{topics=new[]{"windows"}};}
            return new{pid=peer.ProcessId,connectionId=peer.ConnectionId,method};
        });server.Start();
        var endpoint=Path.Combine(AppContext.BaseDirectory,"test-data","renderer-pipes.json");Directory.CreateDirectory(Path.GetDirectoryName(endpoint)!);await File.WriteAllTextAsync(endpoint,JsonSerializer.Serialize(new{protocolVersion=1,pipeName}));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var state=new LumiBarClient();using var events=new LumiBarClient();using var spectrum=new LumiBarClient();using var fonts=new LumiBarClient();
        var hello=await Task.WhenAll(state.ConnectAsync(endpoint,timeout.Token),events.ConnectAsync(endpoint,timeout.Token),spectrum.ConnectAsync(endpoint,timeout.Token),fonts.ConnectAsync(endpoint,timeout.Token));
        Check(hello.All(x=>x.GetProperty("pid").GetInt32()==Environment.ProcessId),"Pipe server obtains the real OS client PID on all four channels");
        Check(hello.Select(x=>x.GetProperty("connectionId").GetGuid()).Distinct().Count()==4,"Each pipe has its own connection identity");
        var waiting=events.RequestAsync("events.wait",ct:timeout.Token);await entered.Task.WaitAsync(timeout.Token);
        var fast=Task.WhenAll(state.RequestAsync("windows.renewTaskbarRenderer",ct:timeout.Token),spectrum.RequestAsync("spectrum.get",ct:timeout.Token),fonts.RequestAsync("fonts.list",ct:timeout.Token));
        var responses=await fast.WaitAsync(TimeSpan.FromSeconds(2),timeout.Token);
        Check(responses.Length==3&&!waiting.IsCompleted,"State heartbeat, FFT and font query remain responsive while events pipe is parked");
        finish.TrySetResult();await waiting;
        return results;
    }
}
