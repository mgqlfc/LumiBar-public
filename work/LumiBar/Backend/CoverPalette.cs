namespace LumiBar;

internal readonly record struct PaletteRgb(byte R,byte G,byte B);
internal static class CoverPalette
{
    private static double Hue(PaletteRgb color)
    {
        double max=Math.Max(color.R,Math.Max(color.G,color.B)),min=Math.Min(color.R,Math.Min(color.G,color.B)),d=max-min;
        if(d<1)return 0;
        return (max==color.R?((color.G-color.B)/d+6)%6:max==color.G?(color.B-color.R)/d+2:(color.R-color.G)/d+4)*60;
    }
    internal static double HueDistance(PaletteRgb a,PaletteRgb b){var d=Math.Abs(Hue(a)-Hue(b));return Math.Min(d,360-d);}
    private static double Chroma(PaletteRgb c)=>Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B));
    private static double Value(PaletteRgb c)=>Math.Max(c.R,Math.Max(c.G,c.B));
    private static double Distance(PaletteRgb a,PaletteRgb b)=>Math.Sqrt(Math.Pow(a.R-b.R,2)+Math.Pow(a.G-b.G,2)+Math.Pow(a.B-b.B,2));
    private static PaletteRgb Lift(PaletteRgb color)
    {
        // Raising HSV value keeps the sampled hue and saturation intact. The
        // user's saturation setting softens the atmospheric light separately.
        var value=Value(color);if(value>=176)return color;
        byte Channel(byte channel)=>(byte)Math.Clamp(Math.Round(channel*176/value),0,255);
        return new(Channel(color.R),Channel(color.G),Channel(color.B));
    }
    internal static (PaletteRgb Primary,PaletteRgb Secondary) Extract(ReadOnlySpan<byte> rgba)
    {
        var bins=new Dictionary<int,(long R,long G,long B,int Count)>();
        for(var i=0;i+3<rgba.Length;i+=4)
        {
            byte r=rgba[i],g=rgba[i+1],b=rgba[i+2];
            var maximum=Math.Max(r,Math.Max(g,b));var minimum=Math.Min(r,Math.Min(g,b));
            // Black borders, gray backgrounds and weak neutral tints must not
            // drown out a small, colorful part of the cover.
            if(rgba[i+3]<180||maximum<40||maximum-minimum<24||(maximum-minimum)/(double)maximum<.18)continue;
            var id=(r>>5)<<6|(g>>5)<<3|(b>>5);var bin=bins.GetValueOrDefault(id);
            bins[id]=(bin.R+r,bin.G+g,bin.B+b,bin.Count+1);
        }
        var samples=bins.Values.Select(v=>(Color:new PaletteRgb((byte)(v.R/v.Count),(byte)(v.G/v.Count),(byte)(v.B/v.Count)),v.Count)).ToArray();
        // Monochrome or missing artwork gets a quiet blue-violet pair instead
        // of black/gray light. Both colors remain close on the hue wheel.
        if(samples.Length==0)return(new(137,153,217),new(166,148,221));
        var bright=samples.Where(v=>Value(v.Color)>=160).ToArray();
        var candidates=bright.Length>0?bright:samples;
        var primary=Lift(candidates.MaxBy(v=>Math.Sqrt(v.Count)*(.25+Chroma(v.Color)/Value(v.Color))*(.35+.65*Value(v.Color)/255)).Color);
        var colors=samples.Select(v=>(Color:Lift(v.Color),v.Count)).ToArray();
        var nearby=colors.Where(v=>Distance(primary,v.Color)>=18&&
            HueDistance(primary,v.Color)<=60).ToArray();
        if(nearby.Length>0)return(primary,nearby.MaxBy(v=>Math.Sqrt(v.Count)*Math.Min(90,Distance(primary,v.Color))*(1-HueDistance(primary,v.Color)/120)).Color);
        // Flat artwork has no second distinct hue: use a nearby tone of the
        // sampled colour, rather than introducing an unrelated accent.
        var lighten=(primary.R+primary.G+primary.B)/3<155;
        byte Tone(byte value)=>(byte)Math.Round(lighten?value*.76+255*.24:value*.76);
        return(primary,new(Tone(primary.R),Tone(primary.G),Tone(primary.B)));
    }
}
