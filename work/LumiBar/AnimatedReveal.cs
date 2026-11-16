using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
namespace LumiBar;

// One UI-frame clock owns content height and the containing HWND's height.
internal sealed class AnimatedReveal:Grid
{
    private readonly FrameworkElement _content;
    private readonly RectangleGeometry _clip=new();
    private long _started;
    private double _from,_duration;
    private bool _running;private int _frames;private double _maxWork;
    internal event Action? LayoutFrame;
    internal bool Expanded{get;private set;}
    internal bool Animating=>_running;
    internal double TargetHeight{get;private set;}
    internal AnimatedReveal(FrameworkElement content)
    {
        _content=content;content.VerticalAlignment=VerticalAlignment.Top;Children.Add(content);
        Height=0;Clip=_clip;IsHitTestVisible=false;
        SizeChanged+=(_,_)=>_clip.Rect=new Rect(0,0,ActualWidth,Math.Max(0,ActualHeight));
        Unloaded+=(_,_)=>Finish();
    }
    internal void SetExpanded(bool expanded,bool animate)
    {
        Expanded=expanded;IsHitTestVisible=expanded;_from=Height;
        _content.Height=double.NaN;
        _content.Measure(new Size(ActualWidth>0?ActualWidth:436,double.PositiveInfinity));
        var full=_content.DesiredSize.Height;_content.Height=full;
        TargetHeight=expanded?full:0;
        animate&=IsLoaded&&new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        Ui.Fade(_content,expanded?1:0,animate,expanded?.2:.14);
        if(!animate||Math.Abs(TargetHeight-_from)<.1){Finish();LayoutFrame?.Invoke();return;}
        _frames=0;_maxWork=0;_duration=expanded?280:220;_started=Stopwatch.GetTimestamp();
        if(!_running){_running=true;CompositionTarget.Rendering+=Frame;}
        LayoutFrame?.Invoke();
    }
    private void Frame(object? sender,object args)
    {
        var frameStarted=Stopwatch.GetTimestamp();
        var t=Math.Clamp(Stopwatch.GetElapsedTime(_started).TotalMilliseconds/_duration,0,1);
        var ease=1-Math.Pow(1-t,3);var scale=XamlRoot?.RasterizationScale??1;Height=Math.Round((_from+(TargetHeight-_from)*ease)*scale)/scale;
        LayoutFrame?.Invoke();
        _frames++;_maxWork=Math.Max(_maxWork,Stopwatch.GetElapsedTime(frameStarted).TotalMilliseconds);
        if(t>=1){Finish();LayoutFrame?.Invoke();App.Trace("weather-motion",$"expanded={Expanded} frames={_frames} elapsed={Stopwatch.GetElapsedTime(_started).TotalMilliseconds:F1}ms maxLayoutWork={_maxWork:F1}ms");}
    }
    internal void Finish()
    {
        if(_running)CompositionTarget.Rendering-=Frame;
        _running=false;Height=TargetHeight;
    }
}
