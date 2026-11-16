using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace LumiBar;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public int Width => Right-Left; public int Height => Bottom-Top; }
    internal delegate bool EnumProc(nint hwnd, nint data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern nint FindWindow(string name, string? title);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint h, out Rect r);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint h, EnumProc cb, nint data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetClassName(nint h, StringBuilder text,int count);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint h,nint after,int x,int y,int w,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern nint GetWindowLong(nint h,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] internal static extern nint SetWindowLong(nint h,int index,nint value);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint h,int command);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd,out uint processId);
    [DllImport("user32.dll")]private static extern nint GetAncestor(nint hwnd,uint flags);
    internal static bool ForegroundWithin(Window window){var h=WinRT.Interop.WindowNative.GetWindowHandle(window);var foreground=GetForegroundWindow();return foreground==h||GetAncestor(foreground,3)==h;}
    internal static bool OwnForeground(){GetWindowThreadProcessId(GetForegroundWindow(),out var p);return p==Environment.ProcessId;}
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint h,uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool AppendMenu(nint menu,uint flags,nuint id,string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(nint menu,uint flags,int x,int y,int reserved,nint h,nint rect);
    [StructLayout(LayoutKind.Sequential)] private struct Point{public int X,Y;}
    internal static int TrayMenu(Window host){var menu=CreatePopupMenu();try{AppendMenu(menu,0,1,"天气");AppendMenu(menu,0,2,"音乐与歌词");AppendMenu(menu,0,3,"设置");AppendMenu(menu,0x800,0,null);AppendMenu(menu,0,4,"退出 LumiBar");GetCursorPos(out var p);var h=WinRT.Interop.WindowNative.GetWindowHandle(host);SetForegroundWindow(h);return TrackPopupMenu(menu,0x100|2,p.X,p.Y,0,h,0);}finally{DestroyMenu(menu);}}
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint h,int attr,ref int value,int size);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left,int top,int right,int bottom,int ellipseWidth,int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint h,nint region,bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left,int top,int right,int bottom);
    [StructLayout(LayoutKind.Sequential)] private struct BlurBehind{public uint Flags;public int Enabled;public nint Region;public int Transition;}
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(nint hwnd,ref BlurBehind blur);
    // A GDI region has binary pixel coverage and produces jagged pill edges.
    // Capsule alpha now comes from the compositor mask, including the acrylic.
    internal static void RoundCapsule(Window window,int width,int height){var h=WinRT.Interop.WindowNative.GetWindowHandle(window);SetWindowRgn(h,0,true);var corner=1;DwmSetWindowAttribute(h,33,ref corner,4);var region=CreateRectRgn(-2,-2,-1,-1);try{var blur=new BlurBehind{Flags=3,Enabled=1,Region=region};DwmEnableBlurBehindWindow(h,ref blur);}finally{DeleteObject(region);}}

    internal static void Configure(Window window,bool capsule)
    {
        var h=WinRT.Interop.WindowNative.GetWindowHandle(window);
        if(window.AppWindow.Presenter is OverlappedPresenter p) { p.SetBorderAndTitleBar(false,false); p.IsResizable=false; p.IsMaximizable=false; p.IsMinimizable=false; p.IsAlwaysOnTop=true; }
        window.AppWindow.IsShownInSwitchers=false;
        var style=(long)GetWindowLong(h,-20)|0x80L;
        if(capsule) style|=0x08000000L;
        if(Environment.GetCommandLineArgs().Contains("--inspect-ui")){window.AppWindow.IsShownInSwitchers=true;style=(style&~0x80L&~0x08000000L)|0x40000L;}
        SetWindowLong(h,-20,(nint)style);
        var corner=2; DwmSetWindowAttribute(h,33,ref corner,4);
        var border=unchecked((int)0xFFFFFFFE);DwmSetWindowAttribute(h,34,ref border,4);
    }
    internal static void RefreshChrome(Window window)
    {
        var h=WinRT.Interop.WindowNative.GetWindowHandle(window);
        var style=(long)GetWindowLong(h,-16);var clean=style&~0x00C40000L;
        if(style!=clean){SetWindowLong(h,-16,(nint)clean);SetWindowPos(h,0,0,0,0,0,0x37);}
        var border=unchecked((int)0xFFFFFFFE);DwmSetWindowAttribute(h,34,ref border,4);
        var corner=window is CapsuleWindow?1:2;DwmSetWindowAttribute(h,33,ref corner,4);if(window is CapsuleWindow)CapsuleTransparency.Enable(h);
        var dark=window.Content is FrameworkElement element&&element.ActualTheme==ElementTheme.Dark?1:0;DwmSetWindowAttribute(h,20,ref dark,4);
    }

    internal static TaskbarArea? ReadTaskbar(TaskbarOccupancy? occupancy=null)
    {
        var h=FindWindow("Shell_TrayWnd",null);
        if(h==0||!GetWindowRect(h,out var bar)||bar.Width<=bar.Height) return null;
        var scale=GetDpiForWindow(h)/96d; if(scale<=0) scale=1;
        var right=bar.Right-(int)(240*scale); var left=bar.Left+(int)(80*scale);
        var occupied=new List<(int left,int right)>();
        var trayFound=false;var measured=false;DateTimeOffset? measuredAt=null;
        EnumChildWindows(h,(child,_)=>{
            var name=new StringBuilder(200); GetClassName(child,name,200);
            if(!IsWindowVisible(child)||!GetWindowRect(child,out var r)||r.Width<=0) return true;
            if(name.ToString()=="TrayNotifyWnd"){right=r.Left;trayFound=true;}
            if(occupancy==null&&name.ToString()=="MSTaskListWClass"&&r.Width<bar.Width*.85)left=Math.Max(left,r.Right);
            if(name.ToString()=="WidBarRemotePreviewOverlay" && r.Top>=bar.Top-2 && r.Top<bar.Bottom) occupied.Add((r.Left,r.Right));
            return true;
        },0);
        if(occupancy!=null){measured=occupancy.TrySpace(h,bar,out var space,out measuredAt);if(measured){left=space.Left;right=Math.Min(right,space.Right);}}
        var trayWidth=trayFound?bar.Right-right:(int?)null;
        // Respect existing WidBar previews instead of covering them during evaluation.
        foreach(var item in occupied.OrderByDescending(x=>x.right)) if(item.right<=right+4) right=Math.Min(right,item.left);
        var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};var onScreen=true;if(GetMonitorInfo(MonitorFromWindow(h,2),ref monitor))onScreen=Math.Min(bar.Bottom,monitor.Monitor.Bottom)-Math.Max(bar.Top,monitor.Monitor.Top)>bar.Height*.6;
        return new(bar,left,right,scale,IsWindowVisible(h)&&bar.Height>8&&onScreen,measured&&trayFound,measured&&trayFound?"measured":"unavailable",measuredAt,trayWidth);
    }

    internal static void Place(Window window,int x,int y,int width,int height)
    {
        SetWindowPos(WinRT.Interop.WindowNative.GetWindowHandle(window),(nint)(-1),x,y,width,height,0x10|0x40);
    }
    internal static void Hide(Window w)=>ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(w),0);
    internal static void Show(Window w)=>ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(w),8);
    internal static bool ForegroundCoversTaskbar(TaskbarArea area)
    {
        var h=GetForegroundWindow(); if(h==0) return false;
        var name=new StringBuilder(200); GetClassName(h,name,200);
        if(name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if(!GetWindowRect(h,out var r)) return false;
        return r.Left<=area.Bar.Left && r.Right>=area.Bar.Right && r.Top<area.Bar.Top && r.Bottom>=area.Bar.Bottom;
    }
}
internal record TaskbarArea(Native.Rect Bar,int Left,int Right,double Scale,bool Visible,bool GeometryVerified=false,string GeometryStatus="unmeasured",DateTimeOffset? MeasuredAt=null,int? TrayWidthPx=null);

internal static class Placement
{
    internal static (int weatherX,int musicX,int musicWidth,bool fits) Calculate(int left,int right,int weatherWidth,int musicWidth,int gap)
    {
        var available=right-left-gap*2;
        if(available<weatherWidth) return (right-weatherWidth-gap,0,0,false);
        var actualMusic=Math.Min(musicWidth,Math.Max(0,available-weatherWidth-gap));
        if(actualMusic<100) actualMusic=0;
        return(right-weatherWidth-gap,right-weatherWidth-gap*2-actualMusic,actualMusic,true);
    }
}

