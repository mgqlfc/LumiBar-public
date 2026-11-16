using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace LumiBar;

internal sealed class FrontendApiException(string code,string message):Exception(message)
{
    public string Code {get;}=code;
}

/// <summary>Versioned local transport shared by the application and integration tests.</summary>
internal sealed class FrontendPipeServer : IDisposable
{
    internal static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    private readonly string _name;
    private readonly Func<string,JsonElement,CancellationToken,Task<object?>> _handler;
    private readonly CancellationTokenSource _stop=new();
    private readonly SemaphoreSlim _slots=new(4);
    private Task? _listener;
    public FrontendPipeServer(string name,Func<string,JsonElement,CancellationToken,Task<object?>> handler){_name=name;_handler=handler;}
    // Start is called from WinUI. Keep framing, JSON and pipe waits off its dispatcher.
    public void Start()=>_listener??=Task.Run(Listen);
    private async Task Listen()
    {
        var token=_stop.Token;
        while(!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe=null;var acquired=false;
            try
            {
                await _slots.WaitAsync(token);acquired=true;
                pipe=new(_name,PipeDirection.InOut,4,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                _=Serve(pipe,token);pipe=null;acquired=false;
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(IOException){try{await Task.Delay(250,token);}catch(OperationCanceledException){break;}}
            finally{pipe?.Dispose();if(acquired)_slots.Release();}
        }
    }
    private async Task Serve(NamedPipeServerStream pipe,CancellationToken stopping)
    {
        try
        {
            using(pipe)
            using(var reader=new StreamReader(pipe,new UTF8Encoding(false,true),false,4096,true))
            using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"})
            {
                var greeted=false;
                while(!stopping.IsCancellationRequested&&pipe.IsConnected)
                {
                    using var idle=CancellationTokenSource.CreateLinkedTokenSource(stopping);idle.CancelAfter(TimeSpan.FromSeconds(60));
                    var line=await ReadLine(reader,idle.Token);if(line==null)break;
                    string? id=null;object response;
                    try
                    {
                        using var doc=JsonDocument.Parse(line,new JsonDocumentOptions{MaxDepth=16});var root=doc.RootElement;
                        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("id",out var idNode)||idNode.ValueKind!=JsonValueKind.String||(id=idNode.GetString()) is not {Length:>0 and <=64})throw new FrontendApiException("invalid_request","id 必须为 1–64 字符的字符串。");
                        if(!root.TryGetProperty("method",out var methodNode)||methodNode.ValueKind!=JsonValueKind.String)throw new FrontendApiException("invalid_request","缺少 method。");
                        var method=methodNode.GetString()!;
                        var args=root.TryGetProperty("params",out var p)?p:default;
                        if(method=="hello")
                        {
                            if(args.ValueKind!=JsonValueKind.Object||!args.TryGetProperty("protocolVersion",out var v)||v.ValueKind!=JsonValueKind.Number||!v.TryGetInt32(out var version)||version!=1)throw new FrontendApiException("version_mismatch","当前支持 protocolVersion 1。");
                            greeted=true;
                        }
                        else if(!greeted)throw new FrontendApiException("handshake_required","先发送 hello。");
                        var data=await _handler(method,args,stopping);
                        response=new{id,ok=true,data};
                    }
                    catch(JsonException){response=new{id,ok=false,error=new{code="invalid_json",message="JSON 格式有误。"}};}
                    catch(FrontendApiException ex){response=new{id,ok=false,error=new{code=ex.Code,message=ex.Message}};}
                    catch(OperationCanceledException) when(stopping.IsCancellationRequested){break;}
                    catch(Exception){response=new{id,ok=false,error=new{code="operation_failed",message="操作失败，请稍后重试。"}};}
                    using var write=CancellationTokenSource.CreateLinkedTokenSource(stopping);write.CancelAfter(TimeSpan.FromSeconds(5));
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response,Json).AsMemory(),write.Token);
                    await Task.Delay(16,stopping);
                }
            }
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or DecoderFallbackException or InvalidDataException){}
        finally{pipe.Dispose();_slots.Release();}
    }
    private static async Task<string?> ReadLine(StreamReader reader,CancellationToken ct)
    {
        var line=new StringBuilder();var c=new char[1];
        while(await reader.ReadAsync(c.AsMemory(),ct)>0)
        {
            if(c[0]=='\n')return line.ToString().TrimEnd('\r');
            if(line.Length>=16384)throw new InvalidDataException("Request too long");
            line.Append(c[0]);
        }
        return null;
    }
    public void Dispose()=>_stop.Cancel();
}
