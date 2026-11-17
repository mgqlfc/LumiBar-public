using System.Diagnostics;
using System.Runtime.InteropServices;
namespace LumiBar;

internal static partial class Native
{
    private static nint _shellForeground;
    private static uint _shellForegroundPid;
    private static bool _shellOverlay;
    private static readonly HashSet<nint> _shellWindows=[];
    private static long _lastShellOverlay=-1;
    internal static bool TaskbarShellTransition()
    {
        var window=GetForegroundWindow();GetWindowThreadProcessId(window,out var pid);
        if(window!=_shellForeground||pid!=_shellForegroundPid){
            _shellForeground=window;_shellForegroundPid=pid;_shellOverlay=false;
            try{
                using var process=Process.GetProcessById((int)pid);
                _shellOverlay=process.ProcessName is "StartMenuExperienceHost" or "SearchHost" or "SearchApp" or "ShellExperienceHost";
            }catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){}
        }
        if(_shellOverlay)_shellWindows.Add(window);
        // A shell window can remain visible while another window gets focus.
        // Track the actual observed overlay, not only its activation state.
        var visible=_shellOverlay||_shellWindows.Any(VisibleShellWindow);
        var now=Environment.TickCount64;if(visible)_lastShellOverlay=now;
        // Allow UIA to rebuild its peers after the shell closes, then require
        // fresh measurements again. Hidden taskbars still use the normal path.
        return visible||_lastShellOverlay>=0&&now-_lastShellOverlay<2000;
    }
    private static bool VisibleShellWindow(nint window)=>IsWindowVisible(window)&&GetWindowRect(window,out var rect)&&rect.Width>4&&rect.Height>4&&ShellWindowCloaked(window,14,out var cloaked,4)==0&&cloaked==0;
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")]private static extern int ShellWindowCloaked(nint window,uint attribute,out int value,int size);
}
