using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
namespace LumiBar;

// Quiet elevation, rendered once per physical size/theme. The transparent
// gutter gives the shadow room instead of chopping it at the HWND boundary.
internal sealed class CapsuleEdge:Grid
{
    private readonly Image _image=new(){Stretch=Stretch.Fill};
    private string _key="";
    internal CapsuleEdge(){IsHitTestVisible=false;Children.Add(_image);Loaded+=(_,_)=>Draw();SizeChanged+=(_,_)=>Draw();ActualThemeChanged+=(_,_)=>Draw();}
    private void Draw()
    {
        var s=XamlRoot?.RasterizationScale??1;var w=(int)Math.Ceiling(ActualWidth*s);var h=(int)Math.Ceiling(ActualHeight*s);if(w<14||h<14)return;
        var dark=ActualTheme==ElementTheme.Dark;var key=$"{w}/{h}/{dark}";if(key==_key)return;_key=key;
        var pixels=new byte[w*h*4];var width=ActualWidth*s;var height=ActualHeight*s;
        double distance(double x,double y)=>CapsuleGeometry.Distance(x,y,width,height,s);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++){
            double alpha=0,white=0;
            for(int sy=0;sy<4;sy++)for(int sx=0;sx<4;sx++){
                var px=x+(sx+.5)/4;var py=y+(sy+.5)/4;var d=distance(px,py);var ds=distance(px,py-.55*s);
                // A shallow contact shadow, like a taskbar control. Keep the
                // shared silhouette and supersampling independent of the light.
                var shadow=(dark?.08:.07)*Math.Exp(-Math.Pow(Math.Max(0,ds)/(1.1*s),2)/2)*Math.Clamp(d+.5,0,1);
                var light=(dark?.13:.22)*(1-py/height)*Math.Exp(-Math.Pow((d+.7*s)/(.65*s),2))*Math.Clamp(.5-d,0,1);
                alpha+=(light+shadow*(1-light))/16;white+=light/16;
            }
            var i=(y*w+x)*4;var c=(byte)Math.Clamp((int)Math.Round(white*255),0,255);pixels[i]=pixels[i+1]=pixels[i+2]=c;pixels[i+3]=(byte)Math.Clamp((int)Math.Round(alpha*255),0,255);
        }
        var bitmap=new WriteableBitmap(w,h);using(var stream=bitmap.PixelBuffer.AsStream())stream.Write(pixels);bitmap.Invalidate();_image.Source=bitmap;
    }
}
