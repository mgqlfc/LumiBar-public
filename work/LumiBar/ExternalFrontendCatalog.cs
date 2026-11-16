using System.Diagnostics;
using System.Text.Json;
namespace LumiBar;
internal sealed record ExternalFrontendInfo(string Id,string Name,string Version,string ManifestPath,string? Entry,string Mode,string? Error);
internal static class ExternalFrontendCatalog
{
    internal static string[] Roots=>new[]{Path.Combine(AppContext.BaseDirectory,"frontends"),Path.Combine(Preferences.DataPath,"frontends"),Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","work","frontends"))}.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    internal static IReadOnlyList<ExternalFrontendInfo> Scan()
    {
        var items=new List<ExternalFrontendInfo>();
        foreach(var root in Roots.Where(Directory.Exists))
        {
            try{
                var options=new EnumerationOptions{RecurseSubdirectories=true,MaxRecursionDepth=2,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint};
                foreach(var file in Directory.EnumerateFiles(root,"plugin.manifest.json",options).Take(100))
                {
                    string id=Path.GetFileName(Path.GetDirectoryName(file))!,name=id,version="",mode="preview";
                    try{
                        if(new FileInfo(file).Length>65536)throw new InvalidDataException("清单过大");
                        using var doc=JsonDocument.Parse(File.ReadAllText(file));var json=doc.RootElement;
                        if(json.GetProperty("kind").GetString()!="external-frontend")continue;
                        id=json.GetProperty("id").GetString()??throw new InvalidDataException("缺少 id");name=json.GetProperty("name").GetString()??id;
                        version=json.TryGetProperty("version",out var v)?v.GetString()??"":"";mode=json.TryGetProperty("mode",out var m)?m.GetString()??"preview":"preview";
                        if(id.Length is <1 or >128||json.GetProperty("protocolVersion").GetInt32()!=1)throw new InvalidDataException("清单协议或 id 无效");
                        var folder=Path.GetFullPath(Path.GetDirectoryName(file)!);var relative=json.GetProperty("entry").GetString()??"";
                        var entry=Path.GetFullPath(Path.Combine(folder,relative));
                        if(Path.IsPathRooted(relative)||!entry.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!entry.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("入口必须是插件目录中的 exe");
                        for(var check=entry;check.Length>=folder.Length;check=Path.GetDirectoryName(check)??"")if((File.Exists(check)||Directory.Exists(check))&&(File.GetAttributes(check)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("入口路径不支持链接或目录跳转");
                        if(!File.Exists(entry))throw new FileNotFoundException("入口尚未编译");
                        items.Add(new(id,name,version,file,entry,mode,null));
                    }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException){items.Add(new(id,name,version,file,null,mode,ex.Message));}
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return items;
    }
    internal static object Launch(string id,string endpoint)
    {
        var matches=Scan().Where(x=>x.Id==id).ToArray();
        if(matches.Length!=1)throw new FrontendApiException("frontend_ambiguous","找不到唯一的前端清单，请检查插件目录。");
        var entry=matches[0];if(entry.Error!=null||entry.Entry==null)throw new FrontendApiException("frontend_unavailable",entry.Error??"入口不可用");
        var start=new ProcessStartInfo(entry.Entry){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(entry.ManifestPath)!,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        start.Environment["LUMIBAR_ENDPOINT_FILE"]=endpoint;start.Environment["LUMIBAR_RENDERER_MODE"]="taskbar";
        using var process=Process.Start(start)??throw new FrontendApiException("launch_failed","前端启动失败。");
        return new{started=true,pid=process.Id,id=entry.Id,endpointFile=endpoint,claimRequired=true};
    }
}
