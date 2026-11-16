using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using System.Text.Json;

namespace LumiBar.Extensibility;

/// <summary>Versioned contract reserved for separately distributed WinUI widget modules.</summary>
public interface IWidgetPlugin : IAsyncDisposable
{
    string Id {get;}
    string Name {get;}
    int ApiVersion => 1;
    double PreferredWidth => 120;
    Task InitializeAsync(WidgetContext context,CancellationToken cancellationToken);
    FrameworkElement CreatePreview();
    FrameworkElement CreateFlyout();
    FrameworkElement? CreateSettings()=>null;
    void OnVisibilityChanged(bool visible){}
}
public sealed record WidgetContext(string DataDirectory,DispatcherQueue Dispatcher,Action RequestRefresh,Action<string> Log);

internal sealed record PluginManifest(string Id,string Name,string Version,string Assembly,string Type,int ApiVersion=1);
internal static class PluginCatalog
{
    public static string DirectoryPath=>Path.Combine(AppContext.BaseDirectory,"plugins");
    public static List<(PluginManifest? manifest,string path,string? error)> Scan()
    {
        Directory.CreateDirectory(DirectoryPath);var entries=new List<(PluginManifest?,string,string?)>();
        foreach(var path in Directory.EnumerateFiles(DirectoryPath,"manifest.json",SearchOption.AllDirectories))try{
            var m=JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("清单为空");
            var assembly=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,m.Assembly));
            var root=Path.GetFullPath(Path.GetDirectoryName(path)!)+Path.DirectorySeparatorChar;
            if(!assembly.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("入口文件必须位于插件目录内");
            if(m.ApiVersion!=1)throw new InvalidDataException("插件接口版本不兼容");
            entries.Add((m,path,null));
        }catch(Exception e){entries.Add((null,path,e.Message));}
        return entries;
    }
}
