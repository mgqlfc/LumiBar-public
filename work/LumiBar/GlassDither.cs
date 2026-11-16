using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
namespace LumiBar;

// Keep the proven single-image composition path. Reuse one low-opacity noise
// surface per window; resizing no longer allocates or uploads a full bitmap.
internal sealed class GlassDither:Grid
{
    private readonly Image _image=new(){Stretch=Stretch.Fill};
    private bool _ready;
    internal GlassDither(){IsHitTestVisible=false;Children.Add(_image);ActualThemeChanged+=(_,_)=>Theme();Loaded+=(_,_)=>{Theme();Update();};}
    private void Theme()=>Opacity=Height==CapsuleGeometry.Height?.18:ActualTheme==ElementTheme.Light?.18:.65;
    private void Update()
    {
        if(_ready)return;_ready=true;
        var scale=XamlRoot?.RasterizationScale??1;
        var w=Math.Clamp((int)Math.Ceiling(ActualWidth*scale),64,2560);
        var h=Math.Clamp((int)Math.Ceiling(ActualHeight*scale),64,2160);
        var pixels=new byte[w*h*4];uint seed=0x6d2b79f5;
        for(var i=0;i<pixels.Length;i+=4){seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;var value=(byte)((seed&1)==0?0:5);pixels[i]=pixels[i+1]=pixels[i+2]=value;pixels[i+3]=5;}
        var bitmap=new WriteableBitmap(w,h);using(var stream=bitmap.PixelBuffer.AsStream())stream.Write(pixels);bitmap.Invalidate();_image.Source=bitmap;
    }
}
