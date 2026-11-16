using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
namespace LumiBar;
internal static partial class Native
{
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd,uint message,nuint wParam,nint lParam);
    internal static int TrayMenu(Window host)
    {
        var root=CreatePopupMenu();var commands=new Dictionary<int,(PluginMenuView Menu,PluginMenuItem Item)>();var next=1000;
        static string Caption(string label)=>label.Replace("&","&&");
        void Add(nint target,PluginMenuView plugin,PluginMenuItem[] entries){
            foreach(var item in entries){
                if(item.Kind=="separator"){AppendMenu(target,0x800,0,null);continue;}
                if(item.Kind=="submenu"){
                    var child=CreatePopupMenu();Add(child,plugin,item.Children!);
                    if(!AppendMenu(target,0x10u|(item.Enabled?0u:1u),(nuint)child,Caption(item.Label)))DestroyMenu(child);
                }else{var id=next++;commands[id]=(plugin,item);AppendMenu(target,(item.Enabled?0u:1u)|(item.Checked==true?8u:0u),(nuint)id,Caption(item.Label));}
            }
        }
        try{
            AppendMenu(root,0,1,"天气");AppendMenu(root,0,2,"音乐与歌词");AppendMenu(root,0,3,"设置");
            var menus=App.Instance.PluginMenuViews().Select(x=>(Menu:x,Items:PluginMenuRegistry.ForLocation(x.Items,"tray"))).Where(x=>x.Items.Length>0).ToArray();
            if(menus.Length>0){AppendMenu(root,0x800,0,null);foreach(var (plugin,items) in menus){var child=CreatePopupMenu();Add(child,plugin,items);if(!AppendMenu(root,0x10,(nuint)child,Caption(plugin.DisplayName)))DestroyMenu(child);}}
            AppendMenu(root,0x800,0,null);AppendMenu(root,0,4,"退出 LumiBar");
            GetCursorPos(out var pointer);var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(host);SetForegroundWindow(hwnd);
            var selected=TrackPopupMenu(root,0x100|2,pointer.X,pointer.Y,0,hwnd,0);PostMessage(hwnd,0,0,0);
            if(commands.TryGetValue(selected,out var command)){App.Instance.InvokePluginMenu(command.Menu,command.Item,"tray");return 0;}return selected;
        }catch(Exception ex){App.Log("plugin-tray-menu",ex);return 0;}finally{if(root!=0)DestroyMenu(root);}
    }
}
