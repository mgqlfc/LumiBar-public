using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;
namespace LumiBar;

// Capture moving lights into a fixed alpha silhouette. XAML/ancestor geometry
// clips alone can leave a colored seam where the rectangular viewport meets a
// rounded capsule; this multiplies the actual pixels by supersampled coverage.
internal sealed class RoundedLightSurface:Grid
{
    internal Grid Source {get;}=new(){IsHitTestVisible=false};
    private CompositionVisualSurface? _sourceSurface,_maskSurface;
    private ShapeVisual? _maskVisual;
    private CompositionRoundedRectangleGeometry? _round;
    private SpriteVisual? _output;
    internal RoundedLightSurface()
    {
        IsHitTestVisible=false;var staging=new Canvas();staging.Children.Add(Source);Canvas.SetLeft(Source,-10000);Children.Add(staging);
        Loaded+=(_,_)=>Resize();SizeChanged+=(_,_)=>Resize();
    }
    private void Resize()
    {
        if(ActualWidth<1||ActualHeight<1)return;
        var size=new Vector2((float)ActualWidth,(float)ActualHeight);
        Source.Width=ActualWidth;Source.Height=ActualHeight;
        if(_output==null)
        {
            var c=ElementCompositionPreview.GetElementVisual(this).Compositor;
            _sourceSurface=c.CreateVisualSurface();_sourceSurface.SourceVisual=ElementCompositionPreview.GetElementVisual(Source);
            var source=c.CreateSurfaceBrush(_sourceSurface);source.Stretch=CompositionStretch.Fill;
            _round=c.CreateRoundedRectangleGeometry();var shape=c.CreateSpriteShape(_round);shape.FillBrush=c.CreateColorBrush(Colors.White);
            _maskVisual=c.CreateShapeVisual();_maskVisual.Shapes.Add(shape);
            _maskSurface=c.CreateVisualSurface();_maskSurface.SourceVisual=_maskVisual;
            var coverage=c.CreateSurfaceBrush(_maskSurface);coverage.Stretch=CompositionStretch.Fill;coverage.BitmapInterpolationMode=CompositionBitmapInterpolationMode.Linear;
            var masked=c.CreateMaskBrush();masked.Source=source;masked.Mask=coverage;
            _output=c.CreateSpriteVisual();_output.Brush=masked;_output.Clip=c.CreateInsetClip();
            ElementCompositionPreview.SetElementChildVisual(this,_output);
        }
        var high=size*4;_round!.Size=high;_round.CornerRadius=new(Math.Min(17,Math.Min(size.X,size.Y)/2)*4);
        _sourceSurface!.SourceSize=size;_maskVisual!.Size=high;_maskSurface!.SourceSize=high;_output.Size=size;
    }
}
