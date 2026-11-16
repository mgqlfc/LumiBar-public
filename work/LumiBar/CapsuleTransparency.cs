using System.Runtime.InteropServices;
namespace LumiBar;

// The redirected Win32 client surface must have zero alpha as well as the
// composition brush. Otherwise the transparent mask reveals a black corner.
// Win32 API behavior cross-checked against WinUIEx TransparentTintBackdrop.
internal static class CapsuleTransparency
{
    private delegate nint Subclass(nint hwnd,uint message,nuint wParam,nint lParam,nuint id,nuint data);
    private static readonly Subclass Callback=Handle;
    [StructLayout(LayoutKind.Sequential)] private struct Margins{public int Left,Right,Top,Bottom;}
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint h,Subclass callback,nuint id,nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint h,Subclass callback,nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint h,uint m,nuint w,nint l);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint h,out Native.Rect rect);
    [DllImport("user32.dll")] private static extern nint GetDC(nint h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint h,nint dc);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc,ref Native.Rect rect,nint brush);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int objectId);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint h,ref Margins margins);
    internal static void Enable(nint hwnd,bool capsule=true)
    {
        SetWindowSubclass(hwnd,Callback,0x4C554D49,capsule?1u:0u);
        var margins=new Margins();DwmExtendFrameIntoClientArea(hwnd,ref margins);
        var dc=GetDC(hwnd);try{Clear(hwnd,dc);}finally{ReleaseDC(hwnd,dc);}
    }
    private static void Clear(nint hwnd,nint dc){GlassDiagnostics.Erase();if(dc!=0&&GetClientRect(hwnd,out var rect))FillRect(dc,ref rect,GetStockObject(4));}
    private static nint Handle(nint hwnd,uint message,nuint wParam,nint lParam,nuint id,nuint data)
    {
        if(message==0x14){Clear(hwnd,(nint)wParam);return 1;}
        if(message==0x84&&data==1&&Native.GetWindowRect(hwnd,out var bounds)){
            var scale=Native.GetDpiForWindow(hwnd)/96d;var x=(short)((long)lParam&0xffff)-bounds.Left;var y=(short)(((long)lParam>>16)&0xffff)-bounds.Top;
            if(CapsuleGeometry.Distance(x,y,bounds.Width,bounds.Height,scale)>0)return -1;
        }
        if(message==0x82)RemoveWindowSubclass(hwnd,Callback,id);
        return DefSubclassProc(hwnd,message,wParam,lParam);
    }
}
