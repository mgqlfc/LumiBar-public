using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace LumiBar;
internal static class DiagnosticArchive
{
    private static readonly string[] Logs=["diagnostics.log","diagnostics.log.old"];
    internal static async Task Write(string destination,string dataDirectory,object snapshot,object events,CancellationToken ct=default)
    {
        destination=Path.GetFullPath(destination);
        if(!string.Equals(Path.GetExtension(destination),".zip",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请选择 ZIP 文件。");
        var temporary=Path.Combine(Path.GetDirectoryName(destination)!,".LumiBar-diagnostics-"+Guid.NewGuid().ToString("N")+".tmp");
        var options=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
        string Json(object value)=>DiagnosticPrivacy.Clean(JsonSerializer.SerializeToNode(value,options))!.ToJsonString(options);
        try{
            await using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){
                using var archive=new ZipArchive(file,ZipArchiveMode.Create,true,Encoding.UTF8);
                async Task Entry(string name,string text){ct.ThrowIfCancellationRequested();var entry=archive.CreateEntry(name,CompressionLevel.Optimal);await using var stream=entry.Open();await using var writer=new StreamWriter(stream,new UTF8Encoding(false));await writer.WriteAsync(text.AsMemory(),ct);}
                await Entry("运行状态.json",Json(snapshot));await Entry("相关崩溃事件.json",Json(events));
                foreach(var name in Logs){
                    var path=Path.Combine(dataDirectory,name);if(!File.Exists(path))continue;
                    try{
                        await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536,true);
                        var truncated=input.Length>512*1024;if(truncated)input.Seek(-512*1024,SeekOrigin.End);
                        using var reader=new StreamReader(input,Encoding.UTF8,true,65536,true);var text=await reader.ReadToEndAsync(ct);
                        // A cut starts mid-record; omit it before applying field redaction.
                        if(truncated){var newline=text.IndexOf('\n');text=newline<0?"":text[(newline+1)..];}
                        await Entry("日志/"+name,DiagnosticPrivacy.Redact(text));
                    }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){await Entry("日志/"+name+".status.txt","日志暂时无法读取。");}
                }
                await Entry("说明.txt","LumiBar 诊断包\n\n包含运行状态、最近日志与相关 Windows 崩溃事件。\n导出时会过滤常见凭据字段、用户路径及媒体文字内容。\n没有收集完整设置、城市缓存、歌词文件、封面、环境变量或内存转储。\n");
            }
            ct.ThrowIfCancellationRequested();File.Move(temporary,destination,true);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
