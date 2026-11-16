using System.Text.Json;
using LumiBar;
internal static class PreferenceUpdateTests
{
    internal static List<string> Run()
    {
        var result=new List<string>();
        void Check(bool ok,string name)=>result.Add($"{(ok?"PASS":"FAIL")} {name}");
        Preferences Clone(Preferences p)=>JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(p))!;
        var original=new Preferences{GlassOpacity=.58,LyricsOffsets=new(){["song-a"]=.5}};
        Check(Enumerable.Range(0,120).All(_=>!PreferenceChanges.Between(original,Clone(original)).Any),"Repeated frontend settings echoes produce no UI invalidation");
        var tiny=Clone(original);tiny.GlassOpacity+=1e-12;
        Check(!PreferenceChanges.Between(original,tiny).Any,"Floating point slider noise is not a new setting");
        var glass=Clone(original);glass.GlassOpacity=.8;
        Check(PreferenceChanges.Between(original,glass).GlassOnly,"A real glass-only update bypasses fonts, audio and taskbar refresh");
        var mixed=Clone(glass);mixed.Theme="Dark";
        Check(!PreferenceChanges.Between(original,mixed).GlassOnly,"Theme plus density still refreshes appearance");
        var font=Clone(glass);font.ChineseFontFamily="Example Font";
        Check(!PreferenceChanges.Between(original,font).GlassOnly,"Font changes cannot enter the glass-only path");
        var timing=Clone(original);timing.LyricsOffsets["song-a"]=-.5;
        Check(PreferenceChanges.Between(original,timing).Any,"Per-song lyric adjustments survive settings deduplication");
        Check(InstanceIdentity.DataSuffix(null)==""&&InstanceIdentity.DataSuffix("  ")=="","Default frontend pipe identity remains compatible");
        Check(InstanceIdentity.DataSuffix(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumiBar"))=="","Explicit standard data path shares the normal instance identity");
        var dir=Path.Combine(Path.GetTempPath(),"LumiBar identity qa");
        Check(InstanceIdentity.DataSuffix(dir)==InstanceIdentity.DataSuffix(dir.ToUpperInvariant()+Path.DirectorySeparatorChar),"Isolated identity ignores path casing and trailing separators");
        Check(InstanceIdentity.DataSuffix(dir)!=InstanceIdentity.DataSuffix(dir+"-other"),"Independent QA data cannot connect to another instance pipe");
        Check(InstanceIdentity.PipeName("test",4,false,null)=="LumiBar.Frontend.v1.test.4"&&InstanceIdentity.PipeName("test",4,true,dir)=="LumiBar.Frontend.v1.test.4.Preview"+InstanceIdentity.DataSuffix(dir),"Activation and backend use one pipe identity factory in normal and isolated modes");
        return result;
    }
}
