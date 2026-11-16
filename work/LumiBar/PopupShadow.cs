using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
namespace LumiBar;

// Nine slices retain the original outside-only shadow without rasterizing the
// full window again on every animation frame. The centre is transparent.
internal sealed class PopupShadow:Grid
{
    internal const int Gutter=16;
    private const int Tile=32;
    private readonly Dictionary<double,WriteableBitmap[]> Cache=[];
    private double _scale;
    internal PopupShadow()
    {
        IsHitTestVisible=false;
        for(var i=0;i<3;i++){
            RowDefinitions.Add(new(){Height=i==1?new GridLength(1,GridUnitType.Star):new GridLength(Tile)});
            ColumnDefinitions.Add(new(){Width=i==1?new GridLength(1,GridUnitType.Star):new GridLength(Tile)});
        }
        Loaded+=(_,_)=>Refresh();SizeChanged+=(_,_)=>Refresh();
    }
    private void Refresh()
    {
        var scale=XamlRoot?.RasterizationScale??1;if(scale==_scale)return;_scale=scale;
        if(!Cache.TryGetValue(scale,out var tiles))Cache[scale]=tiles=Rasterize(scale);
        Children.Clear();
        for(var y=0;y<3;y++)for(var x=0;x<3;x++){
            if(x==1&&y==1)continue;
            var image=new Image{Source=tiles[y*3+x],Stretch=Stretch.Fill};
            SetColumn(image,x);SetRow(image,y);Children.Add(image);
        }
    }
    private static WriteableBitmap[] Rasterize(double scale)
    {
        var tile=(int)Math.Ceiling(Tile*scale);var size=tile*3;var dip=size/scale;var half=dip/2;
        double Distance(double x,double y){var dx=Math.Abs(x-half)-(half-Gutter-12);var dy=Math.Abs(y-half)-(half-Gutter-12);return Math.Sqrt(Math.Pow(Math.Max(dx,0),2)+Math.Pow(Math.Max(dy,0),2))+Math.Min(Math.Max(dx,dy),0)-12;}
        var result=new WriteableBitmap[9];
        for(var row=0;row<3;row++)for(var col=0;col<3;col++){
            if(row==1&&col==1)continue;var pixels=new byte[tile*tile*4];
            for(var y=0;y<tile;y++)for(var x=0;x<tile;x++){
                double alpha=0;
                for(var sy=0;sy<2;sy++)for(var sx=0;sx<2;sx++){
                    var px=(col*tile+x+(sx+.5)/2)/scale;var py=(row*tile+y+(sy+.5)/2)/scale;
                    var outside=Math.Clamp(Distance(px,py)*scale+.5,0,1);if(outside==0)continue;
                    var distance=Math.Max(0,Distance(px,py-2));alpha+=.14*Math.Exp(-.5*Math.Pow(distance/4,2))*outside/4;
                }
                pixels[(y*tile+x)*4+3]=(byte)Math.Clamp(Math.Round(alpha*255),0,255);
            }
            var bitmap=new WriteableBitmap(tile,tile);using(var stream=bitmap.PixelBuffer.AsStream())stream.Write(pixels);bitmap.Invalidate();result[row*3+col]=bitmap;
        }
        return result;
    }
}
