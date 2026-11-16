namespace LumiBar;

internal readonly record struct CapsulePlacement(int WeatherWidth,int MusicWidth,bool Lite,int RequiredWidth,bool RequiresReservation);

internal sealed class PlacementPolicy
{
    private bool _lite;
    public PlacementPolicy(bool wasLite=false){_lite=wasLite;}
    public CapsulePlacement Calculate(int left,int right,int weatherWidth,int musicWidth,int gap,double scale,double musicMinDip=48,double weatherMinDip=64)
    {
        // Both enabled capsules survive space pressure. The host must reserve RequiredWidth;
        // RequiresReservation is an explicit handoff, never permission to draw over app buttons.
        scale=double.IsFinite(scale)&&scale>0?scale:1;
        gap=Math.Max(0,gap);weatherWidth=Math.Max(0,weatherWidth);musicWidth=Math.Max(0,musicWidth);
        var available=Math.Max(0,right-left);
        var recovery=(int)Math.Ceiling(16*scale);
        int Required(int weather,int music)=>weather+music+(weather>0&&music>0?gap:0)+(weather+music>0?2*gap:0);
        var full=Required(weatherWidth,musicWidth);
        _lite=full>0&&available<full+(_lite?recovery:0);
        var weather=_lite?Math.Min(weatherWidth,(int)Math.Ceiling(weatherMinDip*scale)):weatherWidth;
        var music=_lite?Math.Min(musicWidth,(int)Math.Ceiling(musicMinDip*scale)):musicWidth;
        var required=Required(weather,music);
        return new(weather,music,_lite,required,available<required);
    }
}
internal sealed record CapsuleSlot(ScreenRect? Rect,string Mode);
internal sealed record CapsuleSlots(CapsuleSlot? MusicSlot,CapsuleSlot? WeatherSlot,string State);
internal static class SlotPlanner
{
    internal static CapsuleSlots Calculate(ScreenRect bar,int right,int gap,int height,CapsulePlacement layout,bool visible)
    {
        if(!visible)return new(new(null,"hidden"),new(null,"hidden"),"hidden");
        if(layout.RequiresReservation)return new(null,null,"reservation-required");
        height=Math.Clamp(height,1,Math.Max(1,bar.Bottom-bar.Top));var top=bar.Top+(bar.Bottom-bar.Top-height)/2;
        var weatherX=right-gap-layout.WeatherWidth;var musicX=weatherX-(layout.WeatherWidth>0&&layout.MusicWidth>0?gap:0)-layout.MusicWidth;
        var mode=layout.Lite?"lite":"full";
        return new(layout.MusicWidth>0?new(new(musicX,top,musicX+layout.MusicWidth,top+height),mode):new(null,"hidden"),layout.WeatherWidth>0?new(new(weatherX,top,weatherX+layout.WeatherWidth,top+height),mode):new(null,"hidden"),mode);
    }
}
