namespace LumiBar;

internal static class LightFieldGeometry
{
    internal static int Overscan(float x,float y)=>Math.Max(8,(int)Math.Ceiling(Math.Max(Math.Abs(x),Math.Abs(y)))+3);
    internal static double EdgeOpacity(double x,double y,double width,double height,double feather)
    {
        if(feather<=0)return 1;
        var t=Math.Clamp(Math.Min(Math.Min(x,width-x),Math.Min(y,height-y))/feather,0,1);
        return t*t*(3-2*t);
    }
}
