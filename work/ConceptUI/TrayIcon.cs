using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace LumiBar;
internal sealed class TrayIcon : IDisposable
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct Data {
        public uint Size; public nint Window;public uint Id,Flags,Callback;public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle;
        public uint InfoFlags;public Guid Guid;public nint BalloonIcon;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern bool Shell_NotifyIcon(uint message,ref Data data);
    [DllImport("user32.dll")]private static extern nint LoadIcon(nint instance,nint id);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint LoadImage(nint instance,string path,uint type,int x,int y,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string message);
    [DllImport("comctl32.dll")]private static extern bool SetWindowSubclass(nint h,Subclass proc,nuint id,nuint data);
    [DllImport("comctl32.dll")]private static extern bool RemoveWindowSubclass(nint h,Subclass proc,nuint id);
    [DllImport("comctl32.dll")]private static extern nint DefSubclassProc(nint h,uint msg,nuint w,nint l);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(nint icon);
    private delegate nint Subclass(nint h,uint msg,nuint w,nint l,nuint id,nuint data);
    private readonly Subclass _proc;private Data _data;private readonly uint _taskbarCreated;private readonly Action<bool> _action;private bool _ownIcon;
    public TrayIcon(Window host,Action<bool> action)
    {
        _action=action;_taskbarCreated=RegisterWindowMessage("TaskbarCreated");_proc=Callback;
        var iconPath=Path.Combine(AppContext.BaseDirectory,"Assets","LumiBar.ico");
        var icon=File.Exists(iconPath)?LoadImage(0,iconPath,1,32,32,0x10):0;_ownIcon=icon!=0;
        _data=new(){Size=(uint)Marshal.SizeOf<Data>(),Window=WinRT.Interop.WindowNative.GetWindowHandle(host),Id=1,Flags=1|2|4,Callback=0x8001,Icon=icon!=0?icon:LoadIcon(0,(nint)32512),Tip="LumiBar · 天气与音乐\n左键打开设置，右键快捷菜单",Info="",InfoTitle=""};
        SetWindowSubclass(_data.Window,_proc,1,0);Shell_NotifyIcon(0,ref _data);
    }
    private nint Callback(nint h,uint msg,nuint w,nint l,nuint id,nuint data)
    {
        if(msg==_taskbarCreated)Shell_NotifyIcon(0,ref _data);
        if(msg==0x8001){var ev=(uint)((long)l&0xffff);if(ev is 0x202 or 0x205) _action(ev==0x205);}
        return DefSubclassProc(h,msg,w,l);
    }
    public void Dispose(){Shell_NotifyIcon(2,ref _data);RemoveWindowSubclass(_data.Window,_proc,1);if(_ownIcon)DestroyIcon(_data.Icon);}
}
