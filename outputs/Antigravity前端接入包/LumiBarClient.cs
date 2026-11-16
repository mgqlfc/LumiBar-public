using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LumiBar.Frontend.Sdk;

public sealed class LumiBarClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _requests=new(1);
    private long _sequence;
    public async Task<JsonElement> ConnectAsync(string endpointFile,CancellationToken ct=default)
    {
        if(_pipe!=null)throw new InvalidOperationException("Already connected");
        using var descriptor=JsonDocument.Parse(await File.ReadAllTextAsync(endpointFile,ct));
        var root=descriptor.RootElement;
        if(root.GetProperty("protocolVersion").GetInt32()!=1)throw new InvalidDataException("Unsupported protocol");
        var name=root.GetProperty("pipeName").GetString()!;
        if(!name.StartsWith("LumiBar.Frontend.v1.",StringComparison.Ordinal)||name.Contains('\\')||name.Contains('/'))throw new InvalidDataException("Invalid pipe name");
        _pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
        try
        {
            await _pipe.ConnectAsync(5000,ct);
            _reader=new(_pipe,new UTF8Encoding(false),false,4096,true);
            _writer=new(_pipe,new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
            return await RequestAsync("hello",new{protocolVersion=1,clientName="antigravity-winui"},ct);
        }
        catch{Dispose();throw;}
    }
    public async Task<JsonElement> RequestAsync(string method,object? parameters=null,CancellationToken ct=default)
    {
        await _requests.WaitAsync(ct);
        try
        {
            if(_pipe?.IsConnected!=true||_reader==null||_writer==null)throw new IOException("Backend disconnected");
            var id=Interlocked.Increment(ref _sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var request=JsonSerializer.Serialize(new{id,method,@params=parameters??new{}});
            if(request.Length>16384)throw new ArgumentException("Request too long");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(45));
            await _writer.WriteLineAsync(request.AsMemory(),timeout.Token);
            var line=await _reader.ReadLineAsync(timeout.Token)??throw new IOException("Backend disconnected");
            using var response=JsonDocument.Parse(line);var root=response.RootElement;
            if(root.GetProperty("id").GetString()!=id)throw new InvalidDataException("Mismatched response ID");
            if(!root.GetProperty("ok").GetBoolean())
            {
                var error=root.GetProperty("error");throw new LumiBarApiException(error.GetProperty("code").GetString()!,error.GetProperty("message").GetString()!);
            }
            return root.GetProperty("data").Clone();
        }
        catch(OperationCanceledException){Dispose();throw;}
        catch(IOException){Dispose();throw;}
        finally{_requests.Release();}
    }
    public void Dispose(){try{_writer?.Dispose();}catch(IOException){}finally{_writer=null;_reader?.Dispose();_reader=null;_pipe?.Dispose();_pipe=null;}}
}
public sealed class LumiBarApiException(string code,string message):Exception(message)
{
    public string Code {get;}=code;
}
