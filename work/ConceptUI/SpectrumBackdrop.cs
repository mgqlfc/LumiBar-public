using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;
using Windows.UI;
namespace LumiBar;
internal sealed class SpectrumBackdrop:Grid
{
    private const int Count=40;private ContainerVisual? _root;private readonly List<SpriteVisual> _bars=[];private readonly float[] _levels=new float[Count];private long _last;private Color _color;
    internal SpectrumBackdrop(){IsHitTestVisible=false;Opacity=.3;Loaded+=(_,_)=>Build();SizeChanged+=(_,_)=>Resize();}
    private void Build(){if(_root!=null)return;var c=ElementCompositionPreview.GetElementVisual(this).Compositor;_root=c.CreateContainerVisual();for(int i=0;i<Count;i++){var brush=c.CreateLinearGradientBrush();brush.StartPoint=new(.5f,0);brush.EndPoint=new(.5f,1);brush.ColorStops.Add(c.CreateColorGradientStop(0,Colors.White));brush.ColorStops.Add(c.CreateColorGradientStop(1,Color.FromArgb(0,255,255,255)));var bar=c.CreateSpriteVisual();bar.Brush=brush;_bars.Add(bar);_root.Children.InsertAtTop(bar);}ElementCompositionPreview.SetElementChildVisual(this,_root);Resize();}
    private void Resize(){if(_root==null)return;var step=(float)Math.Max(0,ActualWidth)/Count;var height=(float)Math.Max(1,ActualHeight);for(int i=0;i<_bars.Count;i++){var bar=_bars[i];bar.Size=new(Math.Max(1,step*.46f),height);bar.Offset=new(i*step,height,0);bar.AnchorPoint=new(0,1);var shape=bar.Compositor.CreateRoundedRectangleGeometry();shape.Size=bar.Size;shape.CornerRadius=new(Math.Min(2,bar.Size.X/2));bar.Clip=bar.Compositor.CreateGeometricClip(shape);}}
    internal void Update(float[] values,bool playing,double sensitivity,Color color){var now=Environment.TickCount64;var decay=(float)Math.Exp(-Math.Clamp(now-_last,0,100)/150d);_last=now;for(int i=0;i<_bars.Count;i++){var p=values.Length>1?i*(values.Length-1f)/(Count-1):0;var left=(int)p;var right=Math.Min(left+1,values.Length-1);var level=playing&&values.Length>0?values[left]+(values[right]-values[left])*(p-left):0;_levels[i]=Math.Max(Math.Clamp(level*(float)sensitivity,0,1),_levels[i]*decay);_bars[i].Scale=new(1,_levels[i],1);if(_color!=color&&_bars[i].Brush is CompositionLinearGradientBrush brush){brush.ColorStops[0].Color=color;brush.ColorStops[1].Color=Color.FromArgb(0,color.R,color.G,color.B);}}_color=color;}
}
