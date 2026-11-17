namespace LumiBar;

internal sealed record ReservationSnapshot(string Status,int Error,ScreenRect? Slot,double WidthDip,uint ExplorerPid,long At,int LayerError=0,uint TaskbarBand=0,uint LayerRepairs=0,uint CallbackCount=0,uint CandidateCount=0,int DiscoveryError=0,uint ContainerKind=0)
{
    internal bool Compatible=>Status is "ready" or "reserved";
    internal bool Reserved=>Status=="reserved"&&Slot is {} r&&r.Right>r.Left&&Environment.TickCount64-At<800;
}
