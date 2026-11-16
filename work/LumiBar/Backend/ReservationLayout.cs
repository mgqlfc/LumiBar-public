namespace LumiBar;

internal readonly record struct ReservationPlan(CapsulePlacement Capsules,double ReserveDip,bool Elevated);
internal sealed class ReservationLayoutPolicy
{
    private readonly PlacementPolicy _placement=new();
    private ScreenRect? _measuredBar;private double _measuredScale;private int _appsRight;
    internal ReservationPlan Calculate(ScreenRect bar,int appsRight,int nativeRight,ScreenRect? reserved,double scale,int weatherWidth,int musicWidth,int gap,bool compatible,double weatherMinDip=64,double musicMinDip=48,bool geometryVerified=true)
    {
        scale=double.IsFinite(scale)&&scale>0?scale:1;
        // Keep one app-icon step ahead of pressure so Lite happens before Windows compresses icons.
        var right=reserved?.Right??nativeRight;
        var headroom=(int)Math.Ceiling(48*scale);
        if(geometryVerified){_measuredBar=bar;_measuredScale=scale;_appsRight=appsRight;}
        else if(_measuredBar is {} previous&&TaskbarGeometry.SameHorizontal(previous,bar)&&_measuredScale==scale)appsRight=_appsRight;
        else if(compatible&&reserved is {} owned){
            // The shell still owns this area even when UIA peers disappear.
            // At startup retain only the capacity verified by the native slot.
            appsRight=owned.Left-headroom;
        }
        var layout=_placement.Calculate(appsRight+headroom,right,weatherWidth,musicWidth,gap,scale,musicMinDip:musicMinDip,weatherMinDip:weatherMinDip);
        // Native layout owns the promised area, even after Windows switches to overflow.
        var required=layout.RequiredWidth;
        var ownsSpace=compatible&&reserved is {} slot&&slot.Right-slot.Left>=required-2;
        // A reconnect is not space pressure. Use the verified blank area while
        // the native reservation is negotiating, provided neither pill overlaps apps.
        var blankFits=geometryVerified&&!layout.RequiresReservation;
        return new(layout,required/scale,!ownsSpace&&!blankFits);
    }
}
