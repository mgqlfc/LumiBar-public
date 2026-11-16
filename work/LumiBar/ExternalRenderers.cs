using System.Diagnostics;
using System.Runtime.InteropServices;
namespace LumiBar;

internal static class RendererWindows
{
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd,out uint pid);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd,uint flags);
    internal static bool Owned(long hwnd,int pid)=>hwnd>0&&IsWindow((nint)hwnd)&&GetWindowThreadProcessId((nint)hwnd,out var owner)!=0&&owner==pid&&GetAncestor((nint)hwnd,2)==(nint)hwnd;
    internal static long Started(int pid){try{using var process=Process.GetProcessById(pid);return process.HasExited?0:process.StartTime.ToUniversalTime().Ticks;}catch{return 0;}}
    internal static bool Alive(RendererLease l)=>Started(l.Pid)==l.ProcessStarted&&Owned(l.MusicHwnd,l.Pid)&&Owned(l.WeatherHwnd,l.Pid);
    internal static void Hide(RendererLease l){if(Started(l.Pid)!=l.ProcessStarted)return;foreach(var h in new[]{l.MusicHwnd,l.WeatherHwnd})if(Owned(h,l.Pid))Native.ShowWindow((nint)h,0);}
}
public partial class App
{
    internal readonly RendererLeaseStore RendererLeases=new();
    private long _lastRendererCheck;
    internal long SlotsVersion {get;private set;}
    internal string RenderingOwner=>FrontendBackend?"preview":RendererLeases.Current?.Active==true?"external":"builtin";
    internal bool ExternalRendererActive=>RendererLeases.Current?.Active==true;
    internal RendererLease RegisterRenderer(int pid,string rendererId,long music,long weather,RendererSizes sizes)
    {
        CheckRenderer();
        if(FrontendBackend)throw new FrontendApiException("preview_only","预览后端不接管真实任务栏，请连接正常运行的 LumiBar。");
        if(Reservation?.Snapshot.Compatible!=true)throw new FrontendApiException("reservation_unavailable","当前任务栏没有可用的原生预留空间。");
        if(pid==Environment.ProcessId||!RendererWindows.Owned(music,pid)||!RendererWindows.Owned(weather,pid))throw new FrontendApiException("window_owner_mismatch","胶囊窗口必须属于当前管道客户端进程，且为两个顶层窗口。");
        if(Native.IsWindowVisible((nint)music)||Native.IsWindowVisible((nint)weather))throw new FrontendApiException("windows_must_be_hidden","注册前请隐藏两个胶囊窗口。");
        var started=RendererWindows.Started(pid);if(started==0)throw new FrontendApiException("process_unavailable","前端进程已退出。");
        var lease=RendererLeases.Register(pid,started,rendererId,music,weather,sizes,Environment.TickCount64);
        Layout();_frontendApi?.Notify("windows");return lease;
    }
    internal RendererLease RequireRenderer(int pid,string id,string token){CheckRenderer();return RendererLeases.Require(pid,id,token,Environment.TickCount64);}
    internal void ActivateRenderer(RendererLease lease)
    {
        Layout();
        if(Reservation?.Snapshot.Reserved!=true||ActiveSlots?.State is null or "hidden" or "unavailable"||!RendererWindows.Alive(lease))throw new FrontendApiException("reservation_not_ready","真实任务栏槽位尚未就绪，续租后重试。");
        var slot=Reservation.Snapshot.Slot;
        if(slot==null||ActivePlacement is not {} layout||(slot.Value.Right-slot.Value.Left)<layout.RequiredWidth-2)throw new FrontendApiException("reservation_not_ready","原生预留宽度尚未就绪。");
        lease.Active=true;RendererLeases.Renew(lease,Environment.TickCount64);
        _motion.Forget(WeatherCapsule);_motion.Forget(MusicCapsule);
        WeatherHover.Cancel();MusicHover.Cancel();WeatherWindow.HideSurface();MusicWindow.HideSurface();
        WeatherCapsule.HideImmediately();MusicCapsule.HideImmediately();
        Layout();_frontendApi?.Notify("windows");
    }
    internal void CheckRenderer()
    {
        var l=RendererLeases.Current;if(l==null)return;
        var reason=Environment.TickCount64>=l.Deadline?"heartbeat_timeout":!RendererWindows.Alive(l)?"process_or_window_lost":null;
        if(reason!=null)RestoreBuiltinRenderer(reason);
    }
    internal void RestoreBuiltinRenderer(string reason="user_release")
    {
        var old=RendererLeases.Release(reason);if(old==null)return;
        RendererWindows.Hide(old);Trace("renderer",$"Released {old.RendererId}: {reason}");
        Layout();_frontendApi?.Notify("windows");
    }
    internal object RendererStatus()=>new{owner=RenderingOwner,lease=RendererLeases.Current is {} l?new{leaseId=l.Id,rendererId=l.RendererId,pid=l.Pid,phase=l.Active?"active":"pending",expiresInMs=Math.Max(0,l.Deadline-Environment.TickCount64),leaseDurationMs=RendererLeaseStore.LeaseMs,renewAfterMs=RendererLeaseStore.HeartbeatMs,sizes=l.Sizes}:null,lastReleaseReason=RendererLeases.LastReleaseReason};
    internal object RendererResponse(RendererLease lease,bool includeToken=false)=>new{leaseId=lease.Id,leaseToken=includeToken?lease.Token:null,phase=lease.Active?"active":"pending",leaseDurationMs=RendererLeaseStore.LeaseMs,renewAfterMs=RendererLeaseStore.HeartbeatMs,expiresInMs=Math.Max(0,lease.Deadline-Environment.TickCount64),slotsVersion=SlotsVersion,slots=ActiveSlots,renderingOwner=RenderingOwner};
    private void SetSlots(CapsuleSlots slots){if(ActiveSlots==slots)return;ActiveSlots=slots;SlotsVersion++;_frontendApi?.Notify("windows");}
}
