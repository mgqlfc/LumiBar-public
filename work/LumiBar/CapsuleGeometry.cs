namespace LumiBar;
internal static class CapsuleGeometry
{
    internal const double Height=34,Radius=Height/2,Gutter=10;
    internal const double TaskbarVerticalOffset=2;
    internal const double OuterHeight=Height+Gutter*2;
    // Shared pill silhouette in physical pixels. Width/height retain fractional
    // DPI precision; the bitmap allocation may round up without moving the shape.
    internal static double Distance(double x,double y,double width,double height,double scale)
    {
        var radius=Radius*scale;var halfSegment=Math.Max(0,width/2-(Gutter+Radius)*scale);
        var dx=Math.Max(0,Math.Abs(x-width/2)-halfSegment);var dy=y-height/2;
        return Math.Sqrt(dx*dx+dy*dy)-radius;
    }
}
