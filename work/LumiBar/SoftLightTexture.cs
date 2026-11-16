using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Numerics;
using Windows.UI;
namespace LumiBar;
internal readonly record struct LightSpot(Color Color,double X,double Y,double RadiusX,double RadiusY,double Amount);
// Evaluate smooth falloff in double precision, then stochastically quantize premultiplied pixels.
// Rebuild only for colour/geometry/DPI changes; transitions and drift stay on the compositor.
internal sealed class SoftLightTexture:Grid
{
    private float _driftX=3,_driftY=-2;private double _seconds=15;private string? _motionKey;private LightSpot[] _spots=[];private Image? _front;private string _signature="";private int _version;private bool _active=true,_animate=true;
    private CancellationTokenSource? _rasterCancellation;
    private readonly Canvas _stage=new();
    private readonly Grid _moving=new();
    // Drift moves the texture, never the clipping silhouette. Eight DIP of
    // overscan exceeds the largest authored travel (5 DIP) plus filter support,
    // so a moving image edge cannot expose an unpainted strip inside a capsule.
    internal SoftLightTexture(){IsHitTestVisible=false;Children.Add(_stage);_stage.Children.Add(_moving);Canvas.SetLeft(_moving,-8);Canvas.SetTop(_moving,-8);Loaded+=(_,_)=>{Viewport();Render();};SizeChanged+=(_,_)=>{Viewport();Render();};Unloaded+=(_,_)=>{++_version;_signature="";_rasterCancellation?.Cancel();_rasterCancellation=null;SetActive(false);};}
    private void Viewport()
    {
        Clip=new RectangleGeometry{Rect=new Windows.Foundation.Rect(0,0,ActualWidth,ActualHeight)};
        _moving.Width=ActualWidth+16;_moving.Height=ActualHeight+16;
        foreach(var child in _moving.Children.OfType<Image>()){child.Width=_moving.Width;child.Height=_moving.Height;}
    }
    internal void Update(LightSpot[] spots,bool animate,float driftX=3,float driftY=-2,double seconds=15){_driftX=driftX;_driftY=driftY;_seconds=seconds;_spots=spots;_animate=animate&&new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;Render();Motion();}
    internal void SetActive(bool active){_active=active;Motion();}
    private async void Render()
    {
        if(!IsLoaded||ActualWidth<1||ActualHeight<1)return;var scale=Math.Min(XamlRoot?.RasterizationScale??1,1800/Math.Max(ActualWidth+16,ActualHeight+16));var w=(int)Math.Ceiling((ActualWidth+16)*scale);var h=(int)Math.Ceiling((ActualHeight+16)*scale);var signature=$"{w}/{h}/"+string.Join(";",_spots.Select(s=>$"{s.Color}/{s.X}/{s.Y}/{s.RadiusX}/{s.RadiusY}/{s.Amount}"));if(signature==_signature)return;_signature=signature;var version=++_version;var spots=_spots.ToArray();
        _rasterCancellation?.Cancel();var cancellation=new CancellationTokenSource();_rasterCancellation=cancellation;var token=cancellation.Token;
        try
        {
            GlassDiagnostics.Raster();var fieldWidth=ActualWidth*scale;var fieldHeight=ActualHeight*scale;var padX=8*scale;var padY=8*scale;
            var pixels=await Task.Run(()=>Rasterize(w,h,spots,fieldWidth,fieldHeight,padX,padY,token),token);
            if(token.IsCancellationRequested||version!=_version||!IsLoaded)return;
            GlassDiagnostics.Completed();var bitmap=new WriteableBitmap(w,h);using(var stream=bitmap.PixelBuffer.AsStream())stream.Write(pixels);bitmap.Invalidate();var next=new Image{Source=bitmap,Stretch=Stretch.Fill,Width=_moving.Width,Height=_moving.Height};var old=_front;_front=next;_moving.Children.Add(next);
            if(_animate&&_active&&old!=null)
            {
                Ui.Fade(next,0,false);Ui.Fade(next,1,true,.8);Ui.Fade(old,0,true,.8);
                // Every outgoing image owns its cleanup, even when a later
                // slider value starts another transition before this one ends.
                await Task.Delay(850);if(old!=_front)_moving.Children.Remove(old);
            }
            else{_moving.Children.Clear();_moving.Children.Add(next);}
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception ex){if(version==_version)_signature="";App.Log("light-texture",ex);}
        finally{if(ReferenceEquals(_rasterCancellation,cancellation))_rasterCancellation=null;cancellation.Dispose();}
    }
    private static byte[] Rasterize(int w,int h,LightSpot[] spots,double fieldWidth,double fieldHeight,double padX,double padY,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var data=new byte[w*h*4];
        static double Noise(uint value){value^=value>>16;value*=0x7feb352d;value^=value>>15;value*=0x846ca68b;value^=value>>16;return (value&0xffffff)/16777216d-.5;}
        for(int y=0;y<h;y++){
            token.ThrowIfCancellationRequested();
            for(int x=0;x<w;x++){
            double alpha=0,r=0,g=0,b=0;foreach(var spot in spots){var dx=((x+.5-padX)/fieldWidth-spot.X)/spot.RadiusX;var dy=((y+.5-padY)/fieldHeight-spot.Y)/spot.RadiusY;var a=spot.Amount*Math.Exp(-3.6*(dx*dx+dy*dy));a=Math.Clamp(a,0,1);r=spot.Color.R*a+r*(1-a);g=spot.Color.G*a+g*(1-a);b=spot.Color.B*a+b*(1-a);alpha=a+alpha*(1-a);}
            var seed=(uint)(y*w+x);var qa=(byte)Math.Clamp((int)Math.Floor(alpha*255+.5+Noise(seed)),0,255);var i=(y*w+x)*4;data[i]=(byte)Math.Clamp((int)Math.Floor(b+.5+Noise(seed+173)),0,qa);data[i+1]=(byte)Math.Clamp((int)Math.Floor(g+.5+Noise(seed+731)),0,qa);data[i+2]=(byte)Math.Clamp((int)Math.Floor(r+.5+Noise(seed+991)),0,qa);data[i+3]=qa;
            }
        }
        return data;
    }
    private void Motion(){var key=$"{_animate}/{_active}/{_driftX}/{_driftY}/{_seconds}";if(key==_motionKey)return;_motionKey=key;var visual=ElementCompositionPreview.GetElementVisual(_moving);ElementCompositionPreview.SetIsTranslationEnabled(_moving,true);visual.StopAnimation("Translation");if(!_animate||!_active){visual.Properties.InsertVector3("Translation",Vector3.Zero);return;}var animation=visual.Compositor.CreateVector3KeyFrameAnimation();var ease=visual.Compositor.CreateCubicBezierEasingFunction(new(.42f,0),new(.58f,1));animation.InsertKeyFrame(0,Vector3.Zero);animation.InsertKeyFrame(.5f,new(_driftX,_driftY,0),ease);animation.InsertKeyFrame(1,Vector3.Zero,ease);animation.Duration=TimeSpan.FromSeconds(_seconds);animation.IterationBehavior=Microsoft.UI.Composition.AnimationIterationBehavior.Forever;visual.StartAnimation("Translation",animation);}
}
