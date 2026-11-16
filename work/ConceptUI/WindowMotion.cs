using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace LumiBar;
/// <summary>Move HWNDs independently of network/UI timers. DwmFlush paces active frames; idle waits consume no CPU.</summary>
internal sealed class WindowMotion : IDisposable
{
    [DllImport("dwmapi.dll")]private static extern int DwmFlush();
    private sealed class State{public nint Handle;public double FromX,FromY,X,Y;public int ToX,ToY,Width,Height;public long Start;public bool Active;}
    private readonly Dictionary<nint,State> _states=[];private readonly object _gate=new();private readonly AutoResetEvent _wake=new(false);private readonly Thread _thread;private volatile bool _stop;
    public WindowMotion(){_thread=new Thread(Run){IsBackground=true,Name="LumiBar window motion"};_thread.Start();}
    internal void Target(Window window,int x,int y,int width,int height,bool animate)
    {
        var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(window);lock(_gate){
            if(!_states.TryGetValue(hwnd,out var state)){state=new(){Handle=hwnd,X=x,Y=y,FromX=x,FromY=y,ToX=x,ToY=y,Width=width,Height=height};_states.Add(hwnd,state);Native.Place(window,x,y,width,height);Native.RoundCapsule(window,width,height);return;}
            if(state.ToX==x&&state.ToY==y&&state.Width==width&&state.Height==height){if(!window.Visible)Native.Show(window);return;}
            if(state.Width!=width||state.Height!=height)Native.RoundCapsule(window,width,height);
            state.FromX=state.X;state.FromY=state.Y;state.ToX=x;state.ToY=y;state.Width=width;state.Height=height;state.Start=Stopwatch.GetTimestamp();
            state.Active=animate&&Math.Abs(state.X-x)<600&&Math.Abs(state.Y-y)<150;
            if(!state.Active){state.X=x;state.Y=y;Native.Place(window,x,y,width,height);}else{Native.Show(window);_wake.Set();}
        }
    }
    private void Run(){while(!_stop){bool active;lock(_gate)active=_states.Values.Any(s=>s.Active);if(!active){_wake.WaitOne();continue;}if(DwmFlush()!=0)Thread.Sleep(8);lock(_gate){foreach(var s in _states.Values.Where(s=>s.Active)){
                var elapsed=Stopwatch.GetElapsedTime(s.Start).TotalMilliseconds;var t=Math.Clamp(elapsed/220,0,1);var ease=1-Math.Pow(1-t,3);s.X=s.FromX+(s.ToX-s.FromX)*ease;s.Y=s.FromY+(s.ToY-s.FromY)*ease;
                Native.SetWindowPos(s.Handle,0,(int)Math.Round(s.X),(int)Math.Round(s.Y),s.Width,s.Height,0x10|0x4|0x4000);if(t>=1)s.Active=false;
            }}}}
    internal void Forget(Window window){lock(_gate)_states.Remove(WinRT.Interop.WindowNative.GetWindowHandle(window));}
    public void Dispose(){_stop=true;_wake.Set();_thread.Join(500);}
}
internal sealed class TaskbarWatcher : IDisposable
{
    private delegate void WinEvent(nint hook,uint ev,nint hwnd,int idObject,int idChild,uint thread,uint time);
    [DllImport("user32.dll")]private static extern nint SetWinEventHook(uint min,uint max,nint module,WinEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")]private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")]private static extern nint GetAncestor(nint hwnd,uint flags);
    private readonly WinEvent _callback;private readonly nint _hook;
    public TaskbarWatcher(Action changed){_callback=(_,ev,hwnd,obj,child,thread,time)=>{if(hwnd==0)return;var taskbar=Native.FindWindow("Shell_TrayWnd",null);if(hwnd==taskbar||GetAncestor(hwnd,2)==taskbar)changed();};_hook=SetWinEventHook(0x8002,0x800B,0,_callback,0,0,2);}
    public void Dispose(){if(_hook!=0)UnhookWinEvent(_hook);}
}
