namespace LumiBar;
internal readonly record struct LyricLayout(double Spacing,double Step,double ViewportHeight,double TopExtension,double BottomExtension,double FocusTop)
{
    internal static LyricLayout ForFont(double size)
    {
        size=double.IsFinite(size)?Math.Clamp(size,18,36):25;
        var spacing=Math.Clamp(size*.5,9,18);var step=size*1.3+spacing;
        var rows=size<=21?6:size<=29?5:4;
        var baseline=Math.Clamp(step*rows+120,312,392);
        var top=step*.5;var bottom=step*1.5;
        // Extending above the old viewport must not pull the highlighted line
        // upward. Its screen position returns to the original anchor.
        return new(spacing,step,baseline+top+bottom,top,bottom,Math.Clamp(step+48,80,128)+top);
    }
}
