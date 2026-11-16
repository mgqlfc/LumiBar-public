using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace LumiBar;

internal static partial class Native
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
    [DllImport("user32.dll")] private static extern nint GetWindow(nint h,uint command);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    internal static bool ContainsPoint(Window window,int x,int y,bool contentOnly=false)
    {
        var h=WinRT.Interop.WindowNative.GetWindowHandle(window);
        if(GetWindowRect(h,out var r)){
            if(contentOnly&&window is not CapsuleWindow){var inset=(int)Math.Round(PopupShadow.Gutter*GetDpiForWindow(h)/96d);r.Left+=inset;r.Right-=inset;r.Top+=inset;r.Bottom-=inset;}
            if(x>=r.Left&&x<r.Right&&y>=r.Top&&y<r.Bottom)return true;
        }
        var hit=WindowFromPoint(new Point{X=x,Y=y});
        return hit!=0&&(contentOnly?GetAncestor(hit,2)!=h&&GetAncestor(hit,3)==h:hit==h||GetAncestor(hit,3)==h);
    }

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd,out uint processId);
    [DllImport("user32.dll")]private static extern nint GetAncestor(nint hwnd,uint flags);
    internal static bool ForegroundWithin(Window window){var h=WinRT.Interop.WindowNative.GetWindowHandle(window);var foreground=GetForegroundWindow();return foreground==h||GetAncestor(foreground,3)==h;}
    internal static bool OwnForeground(){GetWindowThreadProcessId(GetForegroundWindow(),out var p);return p==Environment.ProcessId;}
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint h,uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)]private struct AppbarData{public int Size;public nint Window;public uint Callback,Edge;public Rect Bounds;public nint Parameter;}
    [DllImport("shell32.dll")]private static extern nuint SHAppBarMessage(uint message,ref AppbarData data);
    private static long _appbarAt;private static bool _autoHide;
    private static bool ReadAutoHide(){var now=Environment.TickCount64;if(now-_appbarAt<200)return _autoHide;var data=new AppbarData{Size=Marshal.SizeOf<AppbarData>()};_autoHide=(SHAppBarMessage(4,ref data)&1)!=0;_appbarAt=now;return _autoHide;}
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool AppendMenu(nint menu,uint flags,nuint id,string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    internal static bool PointerWithin(Window window)=>GetCursorPos(out var p)&&IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window))&&ContainsPoint(window,p.X,p.Y);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(nint menu,uint flags,int x,int y,int reserved,nint h,nint rect);
    [StructLayout(LayoutKind.Sequential)] private struct Point{public int X,Y;}
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint h,int attr,ref int value,int size);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left,int top,int right,int bottom,int ellipseWidth,int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint h,nint region,bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left,int top,int right,int bottom);
    [StructLayout(LayoutKind.Sequential)] private struct BlurBehind{public uint Flags;public int Enabled;public nint Region;public int Transition;}
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(nint hwnd,ref BlurBehind blur);
    // A GDI region has binary pixel coverage and produces jagged pill edges.
    // Capsule alpha now comes from the compositor mask, including the acrylic.
    internal static void RoundCapsule(Window window,int width,int height){var h=WinRT.Interop.WindowNative.GetWindowHandle(window);SetWindowRgn(h,0,true);var corner=1;DwmSetWindowAttribute(h,33,ref corner,4);EnableAlphaComposition(h);}
    private static void EnableAlphaComposition(nint hwnd)
    {
        // A zero-alpha client bitmap alone still composites as opaque black on
        // a normal HWND. Enable DWM alpha compositing, as on the taskbar pills.
        var region=CreateRectRgn(-2,-2,-1,-1);
        try{var blur=new BlurBehind{Flags=3,Enabled=1,Region=region};DwmEnableBlurBehindWindow(hwnd,ref blur);}
        finally{DeleteObject(region);}
    }

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
        // We animate our surfaces ourselves. DWM's second hide transition can
        // briefly redraw acrylic after our compositor fade has completed.
        var noSystemTransition=1;DwmSetWindowAttribute(h,3,ref noSystemTransition,4);
        var border=unchecked((int)0xFFFFFFFE);DwmSetWindowAttribute(h,34,ref border,4);
        if(capsule){var excludeFromPeek=1;var result=DwmSetWindowAttribute(h,12,ref excludeFromPeek,4);App.Trace("capsule-peek",$"excluded=True; result=0x{result:x8}");}
    }
    internal static void RefreshChrome(Window window)
    {
        GlassDiagnostics.Chrome();
        var h=WinRT.Interop.WindowNative.GetWindowHandle(window);
        var style=(long)GetWindowLong(h,-16);var clean=style&~0x00C40000L;
        if(style!=clean){SetWindowLong(h,-16,(nint)clean);SetWindowPos(h,0,0,0,0,0,0x37);}
        var border=unchecked((int)0xFFFFFFFE);DwmSetWindowAttribute(h,34,ref border,4);
        var corner=1;if(window is not CapsuleWindow){var noNonClient=1;DwmSetWindowAttribute(h,2,ref noNonClient,4);}DwmSetWindowAttribute(h,33,ref corner,4);CapsuleTransparency.Enable(h,window is CapsuleWindow);EnableAlphaComposition(h);
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
        var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};var onScreen=true;var autoHide=ReadAutoHide();var screenBottom=bar.Bottom;
        if(GetMonitorInfo(MonitorFromWindow(h,2),ref monitor)){
            screenBottom=monitor.Monitor.Bottom;
            if(bar.Bottom>screenBottom+1)autoHide=true;
            var overlap=Math.Min(bar.Bottom,screenBottom)-Math.Max(bar.Top,monitor.Monitor.Top);
            onScreen=autoHide?overlap>2:overlap>bar.Height*.6;
        }
        return new(bar,left,right,scale,IsWindowVisible(h)&&bar.Height>8&&onScreen,measured&&trayFound,measured&&trayFound?"measured":"unavailable",measuredAt,trayWidth,autoHide,screenBottom);
    }

    internal static void Place(Window window,int x,int y,int width,int height)
    {
        SetWindowPos(WinRT.Interop.WindowNative.GetWindowHandle(window),(nint)(-1),x,y,width,height,0x10|0x40);
    }
    internal static void Hide(Window w)=>ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(w),0);
    internal static void Show(Window w)=>ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(w),8);
    internal static bool RestoreAboveTaskbar(Window window)=>RestoreAboveTaskbar(WinRT.Interop.WindowNative.GetWindowHandle(window));
    private static int _repairingLayer;
    internal static bool RestoreAboveTaskbar(nint h)
    {
        // SetWindowPos can dispatch another shell WinEvent before returning.
        // Never recurse through the desktop event callback while repairing it.
        if(Interlocked.CompareExchange(ref _repairingLayer,1,0)!=0)return false;
        try{return RestoreAboveTaskbarCore(h);}finally{Volatile.Write(ref _repairingLayer,0);}
    }
    private static bool RestoreAboveTaskbarCore(nint h)
    {
        var bar=FindWindow("Shell_TrayWnd",null);
        if(bar==0||!IsWindowVisible(bar)||!IsWindowVisible(h))return false;
        // Explorer raises its topmost taskbar when the overflow button is clicked.
        // A topmost capsule can still sit below that topmost taskbar. Only repair
        // this ordering; preserve every popup already above the taskbar.
        var above=GetWindow(h,3); // GW_HWNDPREV: walk toward the top of the Z order.
        for(int i=0;above!=0&&i<256;i++,above=GetWindow(above,3))
        {
            if(above!=bar)continue;
            var predecessor=GetWindow(bar,3);
            return SetWindowPos(h,predecessor==0?(nint)(-1):predecessor,0,0,0,0,0x1|0x2|0x10|0x200);
        }
        return false;
    }
    internal static bool ForegroundCoversTaskbar(TaskbarArea area)
    {
        var h=GetForegroundWindow(); if(h==0) return false;
        var name=new StringBuilder(200); GetClassName(h,name,200);
        if(!GetWindowRect(h,out var r)) return false;
        GetWindowThreadProcessId(h,out var foregroundPid);GetWindowThreadProcessId(FindWindow("Shell_TrayWnd",null),out var shellPid);
        return FullscreenPolicy.Covers(new(r.Left,r.Top,r.Right,r.Bottom),new(area.Bar.Left,area.Bar.Top,area.Bar.Right,Math.Min(area.Bar.Bottom,area.ScreenBottom)),name.ToString(),shellPid!=0&&foregroundPid==shellPid);
    }
}
internal record TaskbarArea(Native.Rect Bar,int Left,int Right,double Scale,bool Visible,bool GeometryVerified=false,string GeometryStatus="unmeasured",DateTimeOffset? MeasuredAt=null,int? TrayWidthPx=null,bool AutoHide=false,int ScreenBottom=int.MaxValue);

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


