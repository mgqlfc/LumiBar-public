namespace LumiBar;
internal readonly record struct LyricFadeStops(double UpperMid,double UpperClear,double LowerClear,double LowerMid,double LowerTail);
internal static class LyricFade
{
    internal static LyricFadeStops ForFocus(double height,double top,double lineHeight)
    {
        if(height<=0||!double.IsFinite(height)||!double.IsFinite(top)||!double.IsFinite(lineHeight))return new(.10,.26,.59,.80,.94);
        var upper=Math.Clamp((top-8)/height,.02,.26);
        var lower=Math.Clamp((top+lineHeight+8)/height,.59,.995);
        return new(upper*(.10/.26),upper,lower,lower+(1-lower)*(.21/.41),lower+(1-lower)*(.35/.41));
    }
}
