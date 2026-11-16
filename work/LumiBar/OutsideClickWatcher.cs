using System.Runtime.InteropServices;
namespace LumiBar;

// Observe only button-down locations, never consume input or retain a history.
// Unlike activation events, this also catches clicks on non-activating taskbar UI.
internal sealed class OutsideClickWatcher:IDisposable
{
    private delegate nint Hook(int code,nuint message,nint data);
    [StructLayout(LayoutKind.Sequential)]private struct MouseData{public int X,Y;public uint Mouse,Flags,Time;public nuint Extra;}
    [DllImport("user32.dll")]private static extern nint SetWindowsHookEx(int id,Hook callback,nint module,uint thread);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]private static extern nint CallNextHookEx(nint hook,int code,nuint message,nint data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string? name);
    private readonly Hook _callback;private readonly nint _hook;
    internal OutsideClickWatcher(Action<int,int> pressed)
    {
        _callback=(code,message,data)=>{if(code>=0&&message is 0x201 or 0x204 or 0x207){var mouse=Marshal.PtrToStructure<MouseData>(data);try{pressed(mouse.X,mouse.Y);}catch(Exception ex){App.Log("outside-click",ex);}}return CallNextHookEx(0,code,message,data);};
        _hook=SetWindowsHookEx(14,_callback,GetModuleHandle(null),0);
        if(_hook==0)App.Trace("outside-click","Mouse observer unavailable; activation dismissal remains enabled");
    }
    public void Dispose(){if(_hook!=0)UnhookWindowsHookEx(_hook);}
}

