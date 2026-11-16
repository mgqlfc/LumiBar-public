using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace LumiBar;
internal static class StartupControl
{
    // Used by shortcuts and Setup. The server remains restricted to this user.
    internal static async Task<int> Send(string method,bool waitForExit=false)
    {
        try{
            var descriptor=Path.Combine(Preferences.DataPath,"frontend-endpoint.json");if(!File.Exists(descriptor))return 0;
            var info=new FileInfo(descriptor);if(info.Length>16384)return 2;
            using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(descriptor));var root=doc.RootElement;
            var pid=root.GetProperty("pid").GetInt32();Process process;
            try{process=Process.GetProcessById(pid);}catch(ArgumentException){return 0;}
            using(process){
                if(!string.Equals(process.ProcessName,"LumiBar",StringComparison.OrdinalIgnoreCase))return 0;
                using var identity=WindowsIdentity.GetCurrent();var owner=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.User!.Value)))[..12];
                var expected=InstanceIdentity.PipeName(owner,Process.GetCurrentProcess().SessionId,App.FrontendBackend,Environment.GetEnvironmentVariable("LUMIBAR_DATA_DIR"));
                if(root.GetProperty("pipeName").GetString()!=expected)return 2;
                using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var pipe=new NamedPipeClientStream(".",expected,PipeDirection.InOut,PipeOptions.Asynchronous);await pipe.ConnectAsync(ct.Token);
                if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var serverPid)||serverPid!=pid)return 2;
                using var reader=new StreamReader(pipe,new UTF8Encoding(false,true),false,4096,true);using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true,NewLine="\n"};
                async Task Request(string name,object args){await writer.WriteLineAsync(JsonSerializer.Serialize(new{id="startup",method=name,@params=args}).AsMemory(),ct.Token);var line=await reader.ReadLineAsync(ct.Token);if(line==null||line.Length>16384)throw new InvalidDataException();using var response=JsonDocument.Parse(line);if(!response.RootElement.GetProperty("ok").GetBoolean())throw new InvalidDataException();}
                await Request("hello",new{protocolVersion=1});await Request(method,new{});
                if(waitForExit){using var shutdown=new CancellationTokenSource(TimeSpan.FromSeconds(12));await process.WaitForExitAsync(shutdown.Token);}
            }
            return 0;
        }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OperationCanceledException or System.ComponentModel.Win32Exception){return 2;}
    }
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,out uint pid);
}
