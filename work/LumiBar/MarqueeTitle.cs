using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;
namespace LumiBar;
internal sealed class MarqueeTitle:Grid
{
    private readonly TextBlock _text;private readonly Canvas _source=new();private readonly UISettings _system=new();
    private CompositionPropertySet? _state;private CompositionVisualSurface? _surface;private CompositionSurfaceBrush? _brush;private SpriteVisual? _output;
    private bool _enabled=true,_active=true,_queued,_loaded;private double _reserved,_natural;private bool _running;private string? _layoutKey;
    internal MarqueeTitle(TextBlock text)
    {
        _text=text;_source.Children.Add(text);Canvas.SetLeft(text,8);var staging=new Canvas();staging.Children.Add(_source);Canvas.SetLeft(_source,-10000);Children.Add(staging);text.HorizontalAlignment=HorizontalAlignment.Left;
        SizeChanged+=(_,_)=>Schedule();text.RegisterPropertyChangedCallback(TextBlock.TextProperty,(_,_)=>Schedule());
        Loaded+=(_,_)=>{_loaded=true;_system.AnimationsEnabledChanged+=SystemMotionChanged;Schedule();};Unloaded+=(_,_)=>{_loaded=false;_system.AnimationsEnabledChanged-=SystemMotionChanged;Stop();};
    }
    private void SystemMotionChanged(UISettings sender,object args)=>DispatcherQueue.TryEnqueue(Schedule);
    internal void SetEnabled(bool enabled){_enabled=enabled;Schedule();}
    internal void SetActive(bool active){_active=active;if(!active)Stop();else Schedule();}
    internal void SetReserved(double reserved,bool animate)
    {
        _reserved=reserved;if(_state==null)return;
        if(!animate||!_system.AnimationsEnabled){_state.StopAnimation("Reserved");_state.InsertScalar("Reserved",(float)reserved);_layoutKey=null;Schedule();return;}
        var motion=_state.Compositor.CreateScalarKeyFrameAnimation();motion.InsertKeyFrame(1,(float)reserved,_state.Compositor.CreateCubicBezierEasingFunction(new(.2f,.8f),new(.2f,1)));motion.Duration=TimeSpan.FromMilliseconds(260);_state.StartAnimation("Reserved",motion);if((!_running&&_natural>ActualWidth-reserved+1)||(_running&&reserved==0&&_natural<=ActualWidth+1)){_layoutKey=null;Schedule();}
    }
    private void Schedule(){if(_queued)return;_queued=true;DispatcherQueue.TryEnqueue(()=>{_queued=false;Update();});}
    private void Stop(){_running=false;if(_state!=null){_state.StopAnimation("Progress");_state.InsertScalar("Progress",0);}_layoutKey=null;}
    private void Build()
    {
        if(_output!=null)return;var c=ElementCompositionPreview.GetElementVisual(this).Compositor;
        _state=c.CreatePropertySet();_state.InsertScalar("Progress",0);_state.InsertScalar("Reserved",(float)_reserved);_state.InsertScalar("Width",1);_state.InsertScalar("Height",1);_state.InsertScalar("Natural",1);
        _surface=c.CreateVisualSurface();_surface.SourceVisual=ElementCompositionPreview.GetElementVisual(_source);_brush=c.CreateSurfaceBrush(_surface);_brush.Stretch=CompositionStretch.None;_brush.HorizontalAlignmentRatio=0;_brush.VerticalAlignmentRatio=0;
        var offset=c.CreateExpressionAnimation("Vector2(-p.Progress * Max(0, p.Natural - Max(1, p.Width-p.Reserved)), 0)");offset.SetReferenceParameter("p",_state);_brush.StartAnimation("Offset",offset);
        var mask=c.CreateLinearGradientBrush();mask.StartPoint=new(0,0);mask.EndPoint=new(1,0);mask.ColorStops.Add(c.CreateColorGradientStop(0,Colors.Transparent));mask.ColorStops.Add(c.CreateColorGradientStop(.05f,Colors.White));mask.ColorStops.Add(c.CreateColorGradientStop(.95f,Colors.White));mask.ColorStops.Add(c.CreateColorGradientStop(1,Colors.Transparent));
        var masked=c.CreateMaskBrush();masked.Source=_brush;masked.Mask=mask;_output=c.CreateSpriteVisual();_output.Brush=masked;var size=c.CreateExpressionAnimation("Vector2(Max(1,p.Width-p.Reserved),p.Height)");size.SetReferenceParameter("p",_state);_output.StartAnimation("Size",size);
        ElementCompositionPreview.SetElementChildVisual(this,_output);ElementCompositionPreview.GetElementVisual(this).Clip=c.CreateInsetClip();
    }
    private void Update()
    {
        if(!_loaded||!_active||ActualWidth<1){Stop();return;}Build();var animate=_enabled&&App.Instance.Prefs.Animations&&_system.AnimationsEnabled;
        var key=$"{_text.Text}|{ActualWidth:F2}|{App.Instance.Prefs.ChineseFontFamily}|{App.Instance.Prefs.WesternFontFamily}|{animate}";if(key==_layoutKey)return;Stop();_layoutKey=key;
        _text.Width=double.NaN;_text.TextTrimming=TextTrimming.None;_text.InvalidateMeasure();_text.Measure(new(double.PositiveInfinity,double.PositiveInfinity));var natural=Math.Ceiling(_text.DesiredSize.Width);Height=_text.DesiredSize.Height;var total=natural+16;_natural=total;var overflow=total-(ActualWidth-_reserved);
        _text.Width=animate?natural:Math.Max(1,ActualWidth-_reserved-16);_text.TextTrimming=animate?TextTrimming.None:TextTrimming.CharacterEllipsis;_source.Width=animate?total:ActualWidth;_source.Height=Height;_surface!.SourceSize=new((float)_source.Width,(float)Height);
        _state!.InsertScalar("Width",(float)ActualWidth);_state.InsertScalar("Height",(float)Height);_state.InsertScalar("Natural",(float)total);if(!animate||overflow<=1)return;
        const double pause=1.8;var travel=Math.Max(1,overflow/22);var duration=2*(pause+travel);var c=_state.Compositor;var motion=c.CreateScalarKeyFrameAnimation();var ease=c.CreateCubicBezierEasingFunction(new(.25f,.12f),new(.75f,.88f));motion.InsertKeyFrame(0,0);motion.InsertKeyFrame((float)(pause/duration),0);motion.InsertKeyFrame((float)((pause+travel)/duration),1,ease);motion.InsertKeyFrame((float)((2*pause+travel)/duration),1);motion.InsertKeyFrame(1,0,ease);motion.Duration=TimeSpan.FromSeconds(duration);motion.IterationBehavior=AnimationIterationBehavior.Forever;_state.StartAnimation("Progress",motion);_running=true;
    }
}