namespace LumiBar;
internal static class TaskbarGeometry
{
    internal static bool SameHorizontal(ScreenRect a,ScreenRect b)=>a.Left==b.Left&&a.Right==b.Right&&a.Bottom-a.Top==b.Bottom-b.Top;
}
internal sealed class TaskbarSlotContinuity
{
    private ScreenRect? _bar,_slot;private double _scale;private uint _explorer;private long _at;
    internal ScreenRect? Resolve(ScreenRect bar,double scale,uint explorer,ScreenRect? current,string status,bool autoHide,long now)
    {
        if(_bar is not {} previous||!TaskbarGeometry.SameHorizontal(previous,bar)||_scale!=scale||_explorer!=explorer){_slot=null;_bar=bar;_scale=scale;_explorer=explorer;}
        if(current is {} slot&&slot.Right>slot.Left&&slot.Left>=bar.Left&&slot.Right<=bar.Right){_slot=slot;_at=now;return slot;}
        if(status is not ("ready" or "reserved")){_slot=null;return null;}
        // The owned spacer stays in Explorer while an auto-hide frame is being
        // arranged. Bridge a short 'ready' response, never failure or reconnect.
        return autoHide&&status=="ready"&&_slot.HasValue&&now-_at<=1200?_slot:null;
    }
}
