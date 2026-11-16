namespace LumiBar;

// Keep the artwork hue, but compress chroma before painting large glass areas.
// This affects atmospheric light only, never the cover image or exported palette.
internal static class AlbumLightColor
{
    internal static (byte R,byte G,byte B) Soften(byte red,byte green,byte blue,bool light,double saturation=.35)
    {
        double r=red/255d,g=green/255d,b=blue/255d;
        var max=Math.Max(r,Math.Max(g,b));var min=Math.Min(r,Math.Min(g,b));
        var delta=max-min;var l=(max+min)/2;
        if(delta<.00001)return(red,green,blue);
        var s=delta/(1-Math.Abs(2*l-1));
        var h=max==r?((g-b)/delta+6)%6:max==g?(b-r)/delta+2:(r-g)/delta+4;
        s*=double.IsFinite(saturation)?Math.Clamp(saturation,0,1):.35;
        // Light glass needs luminous pastel light, not a dark paint overlay.
        // Preserve the sampled hue and let saturation remain user-controlled.
        l=light?.78+l*.10:Math.Clamp(l,.54,.68);
        var c=(1-Math.Abs(2*l-1))*s;var x=c*(1-Math.Abs(h%2-1));var m=l-c/2;
        (double R,double G,double B) rgb=h switch{<1=>(c,x,0d),<2=>(x,c,0d),<3=>(0d,c,x),<4=>(0d,x,c),<5=>(x,0d,c),_=>(c,0d,x)};
        static byte Channel(double v)=>(byte)Math.Clamp(Math.Round(v*255),0,255);
        return(Channel(rgb.R+m),Channel(rgb.G+m),Channel(rgb.B+m));
    }
}
