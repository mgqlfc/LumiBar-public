using System.Security.Cryptography;
namespace LumiBar;
internal static class TaskbarComponentCache
{
    internal static string Prepare(string source,string dataDirectory)
    {
        using var input=File.OpenRead(source);var hash=Convert.ToHexString(SHA256.HashData(input));
        var directory=Path.Combine(dataDirectory,"runtime-components");Directory.CreateDirectory(directory);
        var destination=Path.Combine(directory,$"LumiBar.TaskbarBridge.{hash[..20]}.dll");
        if(File.Exists(destination)){
            using var existing=File.OpenRead(destination);if(Convert.ToHexString(SHA256.HashData(existing))!=hash)throw new InvalidDataException("任务栏组件校验失败。");
            return destination;
        }
        var temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.Copy(source,temporary);try{File.Move(temporary,destination);}catch(IOException) when(File.Exists(destination)){using var existing=File.OpenRead(destination);if(Convert.ToHexString(SHA256.HashData(existing))!=hash)throw new InvalidDataException("任务栏组件校验失败。");}}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
        return destination;
    }
}
