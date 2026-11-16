using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace LumiBar;
/// <summary>Stationary sub-percent luminance dither. Rebuilt only when physical surface size changes.</summary>
internal sealed class GlassDither : Grid
{
    private readonly Image _image=new(){Stretch=Stretch.Fill};
    private int _width,_height;
    internal GlassDither(){IsHitTestVisible=false;Children.Add(_image);Opacity=.65;Loaded+=(_,_)=>Update();SizeChanged+=(_,_)=>Update();}
    private void Update()
    {
        var scale=XamlRoot?.RasterizationScale??1;
        var w=(int)Math.Ceiling(ActualWidth*scale);var h=(int)Math.Ceiling(ActualHeight*scale);
        if(w<1||h<1||w==_width&&h==_height)return;
        w=Math.Min(w,2560);h=Math.Min(h,2160);_width=w;_height=h;
        var pixels=new byte[w*h*4];uint seed=0x6d2b79f5;
        for(var i=0;i<pixels.Length;i+=4){seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;var value=(byte)((seed&1)==0?0:5);pixels[i]=pixels[i+1]=pixels[i+2]=value;pixels[i+3]=5;}
        var bitmap=new WriteableBitmap(w,h);using(var stream=bitmap.PixelBuffer.AsStream())stream.Write(pixels);bitmap.Invalidate();_image.Source=bitmap;
    }
}

