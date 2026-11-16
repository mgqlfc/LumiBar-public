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
    internal FadingLyricsHost(ScrollViewer scroll)
    {
        _scroll=scroll;Children.Add(scroll);VerticalAlignment=VerticalAlignment.Top;Margin=new Thickness(0,24,0,0);
        Loaded+=(_,_)=>Refresh();SizeChanged+=(_,_)=>Refresh();
        scroll.ViewChanged+=(_,_)=>Refresh();
    }
    internal void SetFontSize(double size)
    {
        var rows=size<=21?6:size<=29?5:4;
        Height=Math.Clamp((size*1.3+Math.Clamp(size*.5,9,18))*rows+48,240,320);
    }
    private void Refresh()
    {
        if(ActualWidth<1||ActualHeight<1||_scroll.Content is not UIElement source)return;
        if(_output==null)
        {
            var c=ElementCompositionPreview.GetElementVisual(this).Compositor;
            _surface=c.CreateVisualSurface();_surface.SourceVisual=ElementCompositionPreview.GetElementVisual(source);
            var brush=c.CreateSurfaceBrush(_surface);brush.Stretch=CompositionStretch.None;
            brush.HorizontalAlignmentRatio=brush.VerticalAlignmentRatio=0;
            var mask=c.CreateLinearGradientBrush();mask.StartPoint=new(0,0);mask.EndPoint=new(0,1);
            mask.ColorStops.Add(c.CreateColorGradientStop(0,Colors.Transparent));
            mask.ColorStops.Add(c.CreateColorGradientStop(.09f,ColorHelper.FromArgb(65,255,255,255)));
            mask.ColorStops.Add(c.CreateColorGradientStop(.24f,Colors.White));
            mask.ColorStops.Add(c.CreateColorGradientStop(.76f,Colors.White));
            mask.ColorStops.Add(c.CreateColorGradientStop(.91f,ColorHelper.FromArgb(65,255,255,255)));
            mask.ColorStops.Add(c.CreateColorGradientStop(1,Colors.Transparent));
            var masked=c.CreateMaskBrush();masked.Source=brush;masked.Mask=mask;
            _output=c.CreateSpriteVisual();_output.Brush=masked;
            ElementCompositionPreview.SetElementChildVisual(this,_output);
            // The sampled source excludes the ScrollViewer ancestor's opacity.
            ElementCompositionPreview.GetElementVisual(_scroll).Opacity=0;
        }
        _surface!.SourceOffset=new(0,(float)_scroll.VerticalOffset);
        _surface.SourceSize=new((float)ActualWidth,(float)ActualHeight);
        _output.Size=_surface.SourceSize;
    }
}


