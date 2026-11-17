namespace LumiBar;

internal readonly record struct ReservationPlan(CapsulePlacement Capsules,double ReserveDip,bool Elevated,int Right);
internal sealed class ReservationLayoutPolicy
{
    private readonly PlacementPolicy _placement=new();
    private ScreenRect? _measuredBar;private double _measuredScale;private int _appsRight,_nativeRight;private uint _explorerPid;
    internal ReservationPlan Calculate(ScreenRect bar,int appsRight,int nativeRight,ScreenRect? reserved,double scale,int weatherWidth,int musicWidth,int gap,bool compatible,double weatherMinDip=64,double musicMinDip=48,bool geometryVerified=true,bool shellTransition=false,uint explorerPid=0)
    {
        scale=double.IsFinite(scale)&&scale>0?scale:1;
        // Keep one app-icon step ahead of pressure so Lite happens before Windows compresses icons.
        var headroom=(int)Math.Ceiling(48*scale);
        var retained=false;
        if(geometryVerified){_measuredBar=bar;_measuredScale=scale;_appsRight=appsRight;_nativeRight=nativeRight;_explorerPid=explorerPid;}
        else if(_measuredBar is {} previous&&TaskbarGeometry.SameHorizontal(previous,bar)&&_measuredScale==scale&&_explorerPid==explorerPid&&(compatible||shellTransition)){
            appsRight=_appsRight;
            if(shellTransition){nativeRight=Math.Min(nativeRight,_nativeRight);retained=true;}
        }
        else if(compatible&&reserved is {} owned){
            // The shell still owns this area even when UIA peers disappear.
            // At startup retain only the capacity verified by the native slot.
            appsRight=owned.Left-headroom;
        }
        var right=reserved?.Right??nativeRight;
        var layout=_placement.Calculate(appsRight+headroom,right,weatherWidth,musicWidth,gap,scale,musicMinDip:musicMinDip,weatherMinDip:weatherMinDip);
        // Native layout owns the promised area, even after Windows switches to overflow.
        var required=layout.RequiredWidth;
        var ownsSpace=compatible&&reserved is {} slot&&slot.Right-slot.Left>=required-2;
        // A reconnect is not space pressure. Use the verified blank area while
        // the native reservation is negotiating, provided neither pill overlaps apps.
        // Start/search can temporarily remove UIA peers without changing the
        // physical blank area. Keep both measured edges through that shell
        // transition, even when the optional native bridge is reconnecting.
        var blankFits=(geometryVerified||retained)&&!layout.RequiresReservation;
        return new(layout,required/scale,!ownsSpace&&!blankFits,right);
    }
}
