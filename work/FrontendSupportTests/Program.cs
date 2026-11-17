using AntigravityFrontend.Sdk;
using System.Text.Json;

var path=Path.Combine(AppContext.BaseDirectory,"antigravity-plugin.config.json");
var backup=File.Exists(path)?File.ReadAllBytes(path):null;
var results=new List<(string Name,bool Pass)>();
void Check(string name,bool pass){results.Add((name,pass));Console.WriteLine($"{(pass?"PASS":"FAIL")} {name}");}
try
{
    if(File.Exists(path))File.Delete(path);
    var defaults=PluginConfig.Load();
    Check("Fresh v7 defaults are 512x384",defaults.ConfigVersion==7&&defaults.LyricsFlyoutWidth==512&&defaults.LyricsFlyoutHeight==384);
    File.WriteAllText(path,"""{"ConfigVersion":5,"LyricsFlyoutWidth":420,"LyricsFlyoutHeight":548,"PopupTrigger":"Hover","HoverDelayMs":400} """);
    var migrated=PluginConfig.Load();
    Check("Legacy 548px config migrates to v7 512x384",migrated.ConfigVersion==7&&migrated.LyricsFlyoutWidth==512&&migrated.LyricsFlyoutHeight==384);
    Check("Migration retains unrelated user preferences",migrated.PopupTrigger=="Hover"&&migrated.HoverDelayMs==400);
    using(var saved=JsonDocument.Parse(File.ReadAllText(path)))Check("Migration persists version and dimensions",saved.RootElement.GetProperty("ConfigVersion").GetInt32()==7&&saved.RootElement.GetProperty("LyricsFlyoutWidth").GetDouble()==512&&saved.RootElement.GetProperty("LyricsFlyoutHeight").GetDouble()==384);
    File.WriteAllText(path,"""{"ConfigVersion":7,"LyricsFlyoutWidth":444,"LyricsFlyoutHeight":410,"PopupTrigger":"Hover"} """);
    var before=File.ReadAllText(path);var current=PluginConfig.Load();
    Check("V7 custom dimensions are preserved",current.LyricsFlyoutWidth==444&&current.LyricsFlyoutHeight==410);
    Check("V7 load does not rewrite user config",File.ReadAllText(path)==before);
    File.WriteAllText(path,"{broken");var recovered=PluginConfig.Load();
    Check("Invalid config falls back to current compact defaults",recovered.ConfigVersion==7&&recovered.LyricsFlyoutWidth==512&&recovered.LyricsFlyoutHeight==384);
}
finally{if(backup==null){if(File.Exists(path))File.Delete(path);}else File.WriteAllBytes(path,backup);}
Console.WriteLine($"{results.Count(r=>r.Pass)}/{results.Count} passed");
Environment.ExitCode=results.All(r=>r.Pass)?0:1;
