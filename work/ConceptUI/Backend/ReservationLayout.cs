namespace LumiBar;

internal readonly record struct ReservationPlan(CapsulePlacement Capsules,double ReserveDip,bool Elevated);
internal sealed class ReservationLayoutPolicy
{
    private readonly PlacementPolicy _placement=new();
    internal ReservationPlan Calculate(ScreenRect bar,int appsRight,int nativeRight,ScreenRect? reserved,double scale,int weatherWidth,int musicWidth,int gap,bool compatible)
    {
        scale=double.IsFinite(scale)&&scale>0?scale:1;
        // Keep one app-icon step ahead of pressure so Lite happens before Windows compresses icons.
        var right=reserved?.Right??nativeRight;
        var headroom=(int)Math.Ceiling(48*scale);
        var layout=_placement.Calculate(appsRight+headroom,right,weatherWidth,musicWidth,gap,scale);
        // Native layout owns the promised area, even after Windows switches to overflow.
        var required=layout.RequiredWidth;
        return new(layout,required/scale,!compatible||reserved==null||reserved.Value.Right-reserved.Value.Left<required-2);
    }
}
