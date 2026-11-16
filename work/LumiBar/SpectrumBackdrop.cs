using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;
namespace LumiBar;

// Draw bars as bounded geometry. Scaling clipped SpriteVisuals down to zero
// during track gaps leaves a degenerate clip transform in the composition tree.
// Shapes use their actual height, and the owned root has an explicit viewport.
internal sealed class SpectrumBackdrop:Grid
{
    private const int Count=40;
    private ShapeVisual? _root;
    private readonly List<CompositionRoundedRectangleGeometry> _bars=[];
    private readonly List<CompositionLinearGradientBrush> _brushes=[];
    private readonly float[] _levels=new float[Count];
    private long _last;
    private Color _color=Color.FromArgb(255,100,175,195);
    internal SpectrumBackdrop(){IsHitTestVisible=false;Opacity=.3;Loaded+=(_,_)=>Build();SizeChanged+=(_,_)=>Resize();}
    private void Build()
    {
        if(_root!=null)return;
        var c=ElementCompositionPreview.GetElementVisual(this).Compositor;
        _root=c.CreateShapeVisual();_root.Clip=c.CreateInsetClip();
        for(var i=0;i<Count;i++)
        {
            var brush=c.CreateLinearGradientBrush();brush.MappingMode=CompositionMappingMode.Relative;brush.StartPoint=new(.5f,0);brush.EndPoint=new(.5f,1);
            brush.ColorStops.Add(c.CreateColorGradientStop(0,_color));
            brush.ColorStops.Add(c.CreateColorGradientStop(1,Color.FromArgb(0,_color.R,_color.G,_color.B)));
            var geometry=c.CreateRoundedRectangleGeometry();
            var shape=c.CreateSpriteShape(geometry);shape.FillBrush=brush;
            _bars.Add(geometry);_brushes.Add(brush);_root.Shapes.Add(shape);
        }
        ElementCompositionPreview.SetElementChildVisual(this,_root);Resize();
    }
    private void Resize()
    {
        if(_root==null)return;
        var width=(float)Math.Max(0,ActualWidth);var height=(float)Math.Max(0,ActualHeight);
        _root.Size=new(width,height);
        var step=width/Count;var barWidth=Math.Min(step,Math.Max(1,step*.46f));
        for(var i=0;i<_bars.Count;i++)
        {
            var barHeight=height*_levels[i];var bar=_bars[i];
            bar.Size=new(barWidth,barHeight);bar.Offset=new(i*step,height-barHeight);
            bar.CornerRadius=new(Math.Min(2,barWidth/2),Math.Min(2,barHeight/2));
        }
    }
    internal void Update(float[] values,bool playing,double sensitivity,Color color)
    {
        var now=Environment.TickCount64;var decay=(float)Math.Exp(-Math.Clamp(now-_last,0,100)/150d);_last=now;
        var gain=double.IsFinite(sensitivity)?(float)Math.Clamp(sensitivity,0,10):1;
        for(var i=0;i<Count;i++)
        {
            var p=values.Length>1?i*(values.Length-1f)/(Count-1):0;var left=(int)p;var right=Math.Min(left+1,values.Length-1);
            var level=playing&&values.Length>0?values[left]+(values[right]-values[left])*(p-left):0;
            if(!float.IsFinite(level))level=0;
            _levels[i]=Math.Max(Math.Clamp(level*gain,0,1),_levels[i]*decay);
            if(_levels[i]<.001f)_levels[i]=0;
        }
        if(_color!=color)foreach(var brush in _brushes){brush.ColorStops[0].Color=color;brush.ColorStops[1].Color=Color.FromArgb(0,color.R,color.G,color.B);}
        _color=color;Resize();
    }
}
