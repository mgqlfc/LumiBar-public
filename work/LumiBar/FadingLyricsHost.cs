using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
namespace LumiBar;

// Sample the lyric content rather than painting an opaque fade over the glass.
// The original ScrollViewer still owns layout, accessibility and wheel/touch input.
internal sealed class FadingLyricsHost:Grid
{
    private readonly ScrollViewer _scroll;
    private CompositionVisualSurface? _surface;
    private SpriteVisual? _output;
    private CompositionSurfaceBrush? _brush;
    private CompositionLinearGradientBrush? _mask;
    private double _focusTop=double.NaN,_focusHeight=double.NaN;
    private double? _followTarget;
    private double _from,_to,_duration;
    private long _started;
    internal FadingLyricsHost(ScrollViewer scroll)
    {
        _scroll=scroll;Children.Add(scroll);VerticalAlignment=VerticalAlignment.Top;Margin=new Thickness(0,12,0,0);
        Loaded+=(_,_)=>Refresh();SizeChanged+=(_,_)=>Refresh();
        scroll.ViewChanged+=(_,_)=>Refresh();
    }
    internal void SetFontSize(double size)
    {
        var layout=LyricLayout.ForFont(size);
        Height=layout.ViewportHeight;Margin=new Thickness(0,12-layout.TopExtension,0,0);
    }
    internal void ResetScroll(){_focusTop=_focusHeight=double.NaN;_followTarget=null;_duration=0;_brush?.StopAnimation("Offset");_scroll.ChangeView(null,0,null,true);Refresh();}
    internal void SetFocusSpan(double top,double height){_focusTop=top;_focusHeight=height;UpdateFadeMask();}
    private void UpdateFadeMask()
    {
        if(_mask==null)return;var stops=LyricFade.ForFocus(ActualHeight,_focusTop,_focusHeight);
        for(var i=0;i<6;i++){var t=i/5d;_mask.ColorStops[i].Offset=(float)(stops.UpperClear*t);_mask.ColorStops[6+i].Offset=(float)(stops.LowerClear+(1-stops.LowerClear)*t);}
    }
    private double DisplayOffset(){var t=_duration<=0?1:Math.Clamp((Environment.TickCount64-_started)/_duration,0,1);return _from+(_to-_from)*(t*t*(3-2*t));}
    internal bool ScrollTo(double target,bool animate)
    {
        if(_surface==null||_brush==null)return _scroll.ChangeView(null,target,null,true);
        var current=_followTarget.HasValue?DisplayOffset():_scroll.VerticalOffset;
        animate&=new Windows.UI.ViewManagement.UISettings().AnimationsEnabled&&Math.Abs(target-current)<ActualHeight*2;
        _followTarget=target;_from=current;_to=target;_started=Environment.TickCount64;_duration=animate?680:0;
        var captureStart=animate?Math.Min(current,target):target;
        _surface.SourceOffset=new(0,(float)captureStart);
        _surface.SourceSize=new((float)ActualWidth,(float)(ActualHeight+(animate?Math.Abs(target-current):0)));
        _brush.StopAnimation("Offset");_brush.Offset=new(0,(float)(captureStart-current));
        if(animate)
        {
            var c=_brush.Compositor;var ease=c.CreateCubicBezierEasingFunction(new(1f/3,0),new(2f/3,1));
            var motion=c.CreateVector2KeyFrameAnimation();motion.InsertKeyFrame(1,new(0,(float)(captureStart-target)),ease);
            motion.Duration=TimeSpan.FromMilliseconds(_duration);motion.StopBehavior=AnimationStopBehavior.SetToFinalValue;
            _brush.StartAnimation("Offset",motion);
        }
        else _brush.Offset=new(0,0);
        // Logical scrolling remains native; only the sampled visual glides on
        // the compositor. Wheel/touch changes cancel this follow animation.
        return _scroll.ChangeView(null,target,null,true)||Math.Abs(_scroll.VerticalOffset-target)<1;
    }
    private void Refresh()
    {
        if(ActualWidth<1||ActualHeight<1||_scroll.Content is not UIElement source)return;
        if(_output==null)
        {
            var c=ElementCompositionPreview.GetElementVisual(this).Compositor;
            _surface=c.CreateVisualSurface();_surface.SourceVisual=ElementCompositionPreview.GetElementVisual(source);
            var brush=_brush=c.CreateSurfaceBrush(_surface);brush.Stretch=CompositionStretch.None;
            brush.HorizontalAlignmentRatio=brush.VerticalAlignmentRatio=0;
            var mask=_mask=c.CreateLinearGradientBrush();mask.StartPoint=new(0,0);mask.EndPoint=new(0,1);
            for(var i=0;i<6;i++){var t=i/5d;var alpha=(byte)Math.Round(255*t*t*(3-2*t));mask.ColorStops.Add(c.CreateColorGradientStop((float)(.26*t),ColorHelper.FromArgb(alpha,255,255,255)));}
            for(var i=0;i<6;i++){var t=i/5d;var alpha=(byte)Math.Round(255*(1-t*t*(3-2*t)));mask.ColorStops.Add(c.CreateColorGradientStop((float)(.59+.41*t),ColorHelper.FromArgb(alpha,255,255,255)));}
            var masked=c.CreateMaskBrush();masked.Source=brush;masked.Mask=mask;
            _output=c.CreateSpriteVisual();_output.Brush=masked;
            ElementCompositionPreview.SetElementChildVisual(this,_output);
            // The sampled source excludes the ScrollViewer ancestor's opacity.
            ElementCompositionPreview.GetElementVisual(_scroll).Opacity=0;
        }
        var size=new System.Numerics.Vector2((float)ActualWidth,(float)ActualHeight);
        if(!_followTarget.HasValue||Math.Abs(_scroll.VerticalOffset-_followTarget.Value)>1||_output.Size!=size)
        {
            _followTarget=null;_duration=0;_brush!.StopAnimation("Offset");_brush.Offset=new(0,0);
            _surface!.SourceOffset=new(0,(float)_scroll.VerticalOffset);_surface.SourceSize=size;
        }
        _output.Size=size;
        UpdateFadeMask();
    }
}


