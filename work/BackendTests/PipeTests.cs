using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using LumiBar;

internal static class PipeTests
{
    public static async Task<List<string>> Run()
    {
        var results=new List<string>();void Check(bool ok,string name)=>results.Add($"{(ok?"PASS":"FAIL")} {name}");
        var name="LumiBar.Frontend.v1."+Guid.NewGuid().ToString("N");
        using var server=new FrontendPipeServer(name,(method,args,ct)=>method switch
        {
            "hello"=>Task.FromResult<object?>(new{protocolVersion=1}),
            "state.get"=>Task.FromResult<object?>(new{title="微光",artist="Lumi Studio"}),
            _=>throw new FrontendApiException("method_not_found","unknown")
        });
        var context=new CountingContext();var previous=SynchronizationContext.Current;
        try{SynchronizationContext.SetSynchronizationContext(context);server.Start();}finally{SynchronizationContext.SetSynchronizationContext(previous);}
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);await pipe.ConnectAsync(timeout.Token);
        using var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true);
        async Task<JsonDocument> Call(string line)
        {
            var bytes=Encoding.UTF8.GetBytes(line+"\n");var split=Math.Min(3,bytes.Length);await pipe.WriteAsync(bytes.AsMemory(0,split),timeout.Token);await pipe.WriteAsync(bytes.AsMemory(split),timeout.Token);
            return JsonDocument.Parse((await reader.ReadLineAsync(timeout.Token))!);
        }
        using(var response=await Call("{\"id\":\"a\",\"method\":\"state.get\"}"))Check(response.RootElement.GetProperty("error").GetProperty("code").GetString()=="handshake_required","Pipe requires handshake");
        using(var response=await Call("{\"id\":\"b\",\"method\":\"hello\",\"params\":{\"protocolVersion\":2}}"))Check(response.RootElement.GetProperty("error").GetProperty("code").GetString()=="version_mismatch","Pipe rejects unsupported protocol version");
        using(var response=await Call("{\"id\":\"c\",\"method\":\"hello\",\"params\":{\"protocolVersion\":1}}"))Check(response.RootElement.GetProperty("ok").GetBoolean(),"Pipe accepts supported protocol");
        using(var response=await Call("{\"id\":\"d\",\"method\":\"state.get\"}"))Check(response.RootElement.GetProperty("id").GetString()=="d"&&response.RootElement.GetProperty("data").GetProperty("title").GetString()=="微光","Fragmented pipe frames retain IDs and UTF-8 data");
        using(var response=await Call("{\"id\":\"e\",\"method\":\"shell.execute\"}"))Check(response.RootElement.GetProperty("error").GetProperty("code").GetString()=="method_not_found","Unknown command rejected");
        using(var response=await Call("{"))Check(response.RootElement.GetProperty("error").GetProperty("code").GetString()=="invalid_json","Malformed JSON yields structured error");
        using(var response=await Call("{\"id\":\"f\",\"method\":\"state.get\"}"))Check(response.RootElement.GetProperty("ok").GetBoolean(),"Connection survives malformed request");
        var endpoint=Path.Combine(AppContext.BaseDirectory,"test-data","sdk-endpoint.json");Directory.CreateDirectory(Path.GetDirectoryName(endpoint)!);
        await File.WriteAllTextAsync(endpoint,JsonSerializer.Serialize(new{protocolVersion=1,pipeName=name}));
        using(var sdk=new LumiBar.Frontend.Sdk.LumiBarClient())
        {
            await sdk.ConnectAsync(endpoint,timeout.Token);var state=await sdk.RequestAsync("state.get",ct:timeout.Token);
            Check(state.GetProperty("title").GetString()=="微光","Delivered C# SDK connects and reads typed JSON");
        }
        Check(context.Posts==0,"Pipe transport does not capture frontend UI synchronization context");
        return results;
    }
}
internal sealed class CountingContext : SynchronizationContext
{
    private int _posts;
    public int Posts=>Volatile.Read(ref _posts);
    public override void Post(SendOrPostCallback d,object? state){Interlocked.Increment(ref _posts);ThreadPool.QueueUserWorkItem(_=>d(state));}
}
