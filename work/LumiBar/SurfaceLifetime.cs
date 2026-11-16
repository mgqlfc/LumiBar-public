namespace LumiBar;

// Delayed input and animation completions belong to one presentation of a HWND.
internal sealed class SurfaceLifetime
{
    internal long Revision { get; private set; }
    internal bool IsOpen { get; private set; }
    internal long Show() { IsOpen=true; return ++Revision; }
    internal long Close() { IsOpen=false; return ++Revision; }
    internal bool CanDismiss(long revision)=>IsOpen&&revision==Revision;
    internal bool CanFinishClose(long revision)=>!IsOpen&&revision==Revision;
}
