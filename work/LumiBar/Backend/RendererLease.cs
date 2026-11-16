using System.Security.Cryptography;
namespace LumiBar;

internal sealed record RendererSizes(double MusicFullDip=238,double MusicLiteDip=48,double WeatherFullDip=104,double WeatherLiteDip=64,double GapDip=8,double HeightDip=34)
{
    internal void Validate()
    {
        bool In(double v,double min,double max)=>double.IsFinite(v)&&v>=min&&v<=max;
        if(!In(MusicFullDip,32,600)||!In(MusicLiteDip,24,MusicFullDip)||!In(WeatherFullDip,32,400)||!In(WeatherLiteDip,24,WeatherFullDip)||!In(GapDip,0,24)||!In(HeightDip,24,64))
            throw new FrontendApiException("invalid_params","胶囊尺寸超出允许范围，Lite 宽度不能大于完整宽度。");
    }
}
internal sealed class RendererLease
{
    internal string Id {get;}=Guid.NewGuid().ToString("N");
    internal string Token {get;}=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal required int Pid {get;init;}
    internal required long ProcessStarted {get;init;}
    internal required string RendererId {get;init;}
    internal required long MusicHwnd {get;init;}
    internal required long WeatherHwnd {get;init;}
    internal required RendererSizes Sizes {get;init;}
    internal long Deadline {get;set;}
    internal bool Active {get;set;}
}
// UI-thread owned. Tokens work across independent pipes of the SAME verified
// process; an events/spectrum connection closing does not release the renderer.
internal sealed class RendererLeaseStore
{
    internal const int LeaseMs=5000,HeartbeatMs=1000;
    internal RendererLease? Current {get;private set;}
    internal string? LastReleaseReason {get;private set;}
    internal RendererLease Register(int pid,long started,string rendererId,long music,long weather,RendererSizes sizes,long now)
    {
        if(Current!=null)throw new FrontendApiException("renderer_busy","已有前端持有渲染租约。");
        sizes.Validate();
        if(pid<=0||music<=0||weather<=0||music==weather)throw new FrontendApiException("invalid_params","需要同一进程的两个不同胶囊窗口。");
        Current=new(){Pid=pid,ProcessStarted=started,RendererId=rendererId,MusicHwnd=music,WeatherHwnd=weather,Sizes=sizes,Deadline=now+LeaseMs};
        LastReleaseReason=null;return Current;
    }
    internal RendererLease Require(int pid,string id,string token,long now)
    {
        var lease=Current;
        if(lease==null||lease.Id!=id||lease.Pid!=pid||!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(lease.Token),System.Text.Encoding.UTF8.GetBytes(token)))
            throw new FrontendApiException("lease_invalid","渲染租约无效或不属于当前进程。");
        if(now>=lease.Deadline)throw new FrontendApiException("lease_expired","渲染租约已过期，请重新注册。");
        return lease;
    }
    internal void Renew(RendererLease lease,long now)=>lease.Deadline=now+LeaseMs;
    internal RendererLease? Release(string reason){var old=Current;Current=null;LastReleaseReason=reason;return old;}
}
