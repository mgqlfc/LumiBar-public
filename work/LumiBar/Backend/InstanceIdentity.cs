using System.Security.Cryptography;
using System.Text;
namespace LumiBar;
internal static class InstanceIdentity
{
    internal static string PipeName(string owner,int session,bool preview,string? directory)=>$"LumiBar.Frontend.v1.{owner}.{session}"+(preview?".Preview":"")+DataSuffix(directory);
    // Isolated QA data needs isolated locks AND pipes. The normal user-data
    // endpoint stays unchanged for existing frontend integrations.
    internal static string DataSuffix(string? directory)
    {
        if(string.IsNullOrWhiteSpace(directory))return "";
        var canonical=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant();
        var standard=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumiBar")).ToUpperInvariant();
        if(canonical==standard)return "";
        return "-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
    }
}
