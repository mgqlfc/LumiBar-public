using Microsoft.UI.Xaml.Media;
using Windows.UI;
namespace LumiBar;
internal static class TemperaturePalette
{
    internal static Color At(double t,byte alpha=255)
    {
        t=Math.Clamp(t,0,1);var a=t<.5?Color.FromArgb(alpha,71,153,255):Color.FromArgb(alpha,164,151,218);var b=t<.5?Color.FromArgb(alpha,164,151,218):Color.FromArgb(alpha,255,165,79);var f=t<.5?t*2:(t-.5)*2;
        return Color.FromArgb(alpha,(byte)Math.Round(a.R+(b.R-a.R)*f),(byte)Math.Round(a.G+(b.G-a.G)*f),(byte)Math.Round(a.B+(b.B-a.B)*f));
    }
    internal static LinearGradientBrush Range(double low=0,double high=1,bool vertical=false,byte alpha=255)
    {
        var brush=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=vertical?new(0,1):new(1,0)};
        for(int i=0;i<=8;i++){var f=i/8d;brush.GradientStops.Add(new GradientStop{Offset=f,Color=At(low+(high-low)*f,alpha)});}return brush;
    }
}
