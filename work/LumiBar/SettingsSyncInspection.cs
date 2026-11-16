using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace LumiBar;

internal sealed partial class SettingsWindow
{
    internal void InspectSettingsPage(string page)=>Render(page);
    internal Dictionary<string,double> InspectSettingsRanges()=>InspectSettingsRangeControls().ToDictionary(s=>AutomationProperties.GetName(s),s=>s.Value);
    internal List<Slider> InspectSettingsRangeControls()
    {
        var values=new List<Slider>();var seen=new HashSet<DependencyObject>();
        void Visit(DependencyObject node){
            if(!seen.Add(node))return;
            if(node is Slider slider)values.Add(slider);
            if(node is Panel panel)foreach(var child in panel.Children)Visit(child);
            if(node is Border border&&border.Child!=null)Visit(border.Child);
            if(node is ContentControl content&&content.Content is DependencyObject childContent)Visit(childContent);
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Visit(VisualTreeHelper.GetChild(node,i));
        }
        Visit(Body);return values;
    }
}
public partial class App
{
    private async Task InspectSettingsSync()
    {
        var results=new List<object>();var passed=true;
        void Record(string name,bool ok,object detail){results.Add(new{name,pass=ok,detail});passed&=ok;}
        try{
            ApplyTheme(true);SettingsWindow.KeepOpen=true;
            // The constructor created a hidden settings page from the initial
            // profile. Simulate an external preference update before showing it.
            Prefs.GlassOpacity=.82;Prefs.LyricsFontSize=31;Prefs.Sensitivity=1.7;Prefs.Save();Save(persist:false);
            OpenSettings();await Task.Delay(450);
            var ranges=SettingsWindow.InspectSettingsRanges();
            Record("Hidden appearance page reads current external density",ranges.TryGetValue("玻璃密度",out var density)&&Math.Abs(density-.82)<1e-8,new{shown=density,actual=Prefs.GlassOpacity});
            Record("Showing appearance does not write stale density",Math.Abs(Prefs.GlassOpacity-.82)<1e-8,new{actual=Prefs.GlassOpacity});

            SettingsWindow.InspectSettingsPage("音乐与歌词");await Task.Delay(200);SettingsWindow.HideImmediately();
            Prefs.LyricsFontSize=29;Prefs.Sensitivity=2.1;Prefs.Save();Save(persist:false);
            OpenSettings();await Task.Delay(450);ranges=SettingsWindow.InspectSettingsRanges();
            Record("Reopened music page reads external lyric size",ranges.TryGetValue("歌词字号",out var size)&&size==29,new{shown=size,actual=Prefs.LyricsFontSize});
            Record("Reopened music page reads external spectrum response",ranges.TryGetValue("频谱响应",out var response)&&Math.Abs(response-2.1)<1e-8,new{shown=response,actual=Prefs.Sensitivity});
            Record("Showing music does not write stale numeric preferences",Prefs.LyricsFontSize==29&&Math.Abs(Prefs.Sensitivity-2.1)<1e-8,new{Prefs.LyricsFontSize,Prefs.Sensitivity});

            // Keep old managed controls alive after leaving their page, as a
            // queued native ValueChanged callback can do during page removal.
            var oldRanges=SettingsWindow.InspectSettingsRangeControls();
            SettingsWindow.InspectSettingsPage("外观");await Task.Delay(120);
            foreach(var old in oldRanges)old.Value=old.Minimum;
            await Task.Delay(350);
            Record("Detached controls cannot overwrite current preferences",Prefs.LyricsFontSize==29&&Math.Abs(Prefs.Sensitivity-2.1)<1e-8,new{Prefs.LyricsFontSize,Prefs.Sensitivity});
            var saved=Preferences.Load();Record("Persisted values survive page lifecycle",Math.Abs(saved.GlassOpacity-.82)<1e-8&&saved.LyricsFontSize==29&&Math.Abs(saved.Sensitivity-2.1)<1e-8,new{saved.GlassOpacity,saved.LyricsFontSize,saved.Sensitivity});
            var currentDensity=SettingsWindow.InspectSettingsRangeControls().Single(s=>AutomationProperties.GetName(s)=="玻璃密度");
            currentDensity.Value=.69;await Task.Delay(350);saved=Preferences.Load();
            Record("A live current-page edit still previews and persists",Math.Abs(Prefs.GlassOpacity-.69)<1e-8&&Math.Abs(saved.GlassOpacity-.69)<1e-8,new{actual=Prefs.GlassOpacity,saved=saved.GlassOpacity});
            SettingsWindow.HideImmediately();currentDensity.Value=.35;await Task.Delay(350);saved=Preferences.Load();
            Record("A hidden current-page callback cannot overwrite settings",Math.Abs(Prefs.GlassOpacity-.69)<1e-8&&Math.Abs(saved.GlassOpacity-.69)<1e-8,new{actual=Prefs.GlassOpacity,saved=saved.GlassOpacity});
        }catch(Exception ex){Record("Inspection completes",false,new{error=ex.ToString()});}
        finally{
            File.WriteAllText(Path.Combine(Preferences.DataPath,"settings-sync-result.json"),JsonSerializer.Serialize(new{pass=passed,results},new JsonSerializerOptions{WriteIndented=true}));
            Exiting=true;SettingsWindow.HideImmediately();Exit();Environment.Exit(passed?0:1);
        }
    }
}
