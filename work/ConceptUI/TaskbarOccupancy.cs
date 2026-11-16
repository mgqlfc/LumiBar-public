using System.Windows.Automation;
using System.Diagnostics;

namespace LumiBar;

/// <summary>UIA calls stay off the UI and motion threads; stale geometry is never advertised as free.</summary>
internal sealed class TaskbarOccupancy : IDisposable
{
    private sealed record Snapshot(nint Handle,Native.Rect Bar,TaskbarSpaceResult Space,long At,DateTimeOffset MeasuredAt);
    private Snapshot? _snapshot;
    private volatile bool _stop;
    private readonly Thread _thread;
    internal event Action? Changed;
    public TaskbarOccupancy(){_thread=new Thread(Run){IsBackground=true,Name="LumiBar taskbar occupancy"};_thread.SetApartmentState(ApartmentState.MTA);_thread.Start();}
    internal bool TrySpace(nint handle,Native.Rect bar,out TaskbarSpaceResult space,out DateTimeOffset? measuredAt)
    {
        var s=Volatile.Read(ref _snapshot);space=default;measuredAt=null;
        if(s==null||s.Handle!=handle||!s.Bar.Equals(bar)||Stopwatch.GetElapsedTime(s.At).TotalMilliseconds>=800)return false;
        space=s.Space;measuredAt=s.MeasuredAt;return true;
    }
    internal void Invalidate(){if(Interlocked.Exchange(ref _snapshot,null)!=null)Changed?.Invoke();}
    private void Run()
    {
        while(!_stop)
        {
            try
            {
                var taskbar=Native.FindWindow("Shell_TrayWnd",null);
                if(taskbar!=0&&Native.GetWindowRect(taskbar,out var bounds))
                {
                    // Windows 11 keeps a legacy MSTaskListWClass HWND whose bounds can end
                    // before the real app buttons. Read the actual XAML peers from Shell_TrayWnd.
                    var root=AutomationElement.FromHandle(taskbar);
                    var cache=new CacheRequest();cache.Add(AutomationElement.BoundingRectangleProperty);cache.Add(AutomationElement.IsOffscreenProperty);cache.Add(AutomationElement.ClassNameProperty);
                    AutomationElementCollection items;
                    using(cache.Activate())items=root.FindAll(TreeScope.Descendants,new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),new PropertyCondition(AutomationElement.ClassNameProperty,"Taskbar.TaskbarFrameAutomationPeer")));
                    var frame=false;var visible=new List<TaskbarItem>();
                    foreach(AutomationElement item in items)
                    {
                        var current=item.Cached;var r=current.BoundingRectangle;
                        if(current.IsOffscreen||r.IsEmpty||r.Width<=0||r.Height<=0)continue;
                        if(current.ClassName=="Taskbar.TaskbarFrameAutomationPeer"){frame=true;continue;}
                        if(current.ClassName.StartsWith("Taskbar.TaskListButton",StringComparison.Ordinal))frame=true;
                        visible.Add(new(new((int)Math.Floor(r.Left),(int)Math.Floor(r.Top),(int)Math.Ceiling(r.Right),(int)Math.Ceiling(r.Bottom)),current.ClassName.StartsWith("SystemTray.",StringComparison.Ordinal)));
                    }
                    var measured=TaskbarSpace.Measure(new(bounds.Left,bounds.Top,bounds.Right,bounds.Bottom),visible,frame);
                    if(!_stop&&measured is {} space&&Native.GetWindowRect(taskbar,out var after)&&after.Equals(bounds)){
                        var previous=Volatile.Read(ref _snapshot);Volatile.Write(ref _snapshot,new(taskbar,bounds,space,Stopwatch.GetTimestamp(),DateTimeOffset.UtcNow));
                        if(previous==null||previous.Handle!=taskbar||!previous.Bar.Equals(bounds)||previous.Space!=space)Changed?.Invoke();
                    }
                    else Invalidate();
                }
                else Invalidate();
            }
            catch(Exception ex) when(ex is System.Runtime.InteropServices.COMException or ElementNotAvailableException or InvalidOperationException or UnauthorizedAccessException or ArgumentException){Invalidate();}
            Thread.Sleep(200);
        }
    }
    public void Dispose(){_stop=true;Invalidate();}
}
