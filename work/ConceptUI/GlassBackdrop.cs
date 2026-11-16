using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LumiBar;
internal sealed partial class GlassBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private BrushTarget? _brushTarget;
    private Windows.UI.Composition.CompositionBrush? _source;
    [WinRT.GeneratedWinRTExposedType]
    private sealed partial class BrushTarget : ICompositionSupportsSystemBackdrop
    {
        private readonly Action<Windows.UI.Composition.CompositionBrush?> _changed;
        private Windows.UI.Composition.CompositionBrush? _brush;
        internal BrushTarget(Action<Windows.UI.Composition.CompositionBrush?> changed){_changed=changed;}
        public Windows.UI.Composition.CompositionBrush SystemBackdrop{get=>_brush!;set{_brush=value;_changed(value);}}
    }
    private readonly FrameworkElement _root;
    private readonly bool _capsule;
    private ICompositionSupportsSystemBackdrop? _target;
    private Windows.UI.Composition.CompositionMaskBrush? _mask;
    private Windows.UI.Composition.CompositionVisualSurface? _maskSurface;
    private Windows.UI.Composition.ShapeVisual? _maskVisual;
    private Windows.UI.Composition.CompositionRoundedRectangleGeometry? _geometry;
    private readonly SystemBackdropConfiguration _configuration=new(){IsInputActive=true};
    internal GlassBackdrop(FrameworkElement root,bool capsule=false){_root=root;_capsule=capsule;}
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target,XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target,xamlRoot);if(!DesktopAcrylicController.IsSupported())return;
        if(_capsule){_target=target;_root.SizeChanged+=ResizeMask;_root.Loaded+=(_,_)=>ApplyMask();_brushTarget=new BrushTarget(brush=>{_source=brush;ApplyMask();});}
        _controller=new DesktopAcrylicController{Kind=DesktopAcrylicKind.Thin};_controller.SetSystemBackdropConfiguration(_configuration);_controller.AddSystemBackdropTarget(_brushTarget??target);
        _root.ActualThemeChanged+=ThemeChanged;Update(target,xamlRoot);
    }
    private void ThemeChanged(FrameworkElement sender,object args)=>Refresh();
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target,XamlRoot root){base.OnDefaultSystemBackdropConfigurationChanged(target,root);Update(target,root);}
    private void Update(ICompositionSupportsSystemBackdrop target,XamlRoot root){_configuration.IsHighContrast=GetDefaultSystemBackdropConfiguration(target,root).IsHighContrast;Refresh();}
    internal void Refresh()
    {
        if(_controller==null)return;var light=_root.ActualTheme!=ElementTheme.Dark;
        _configuration.Theme=light?SystemBackdropTheme.Light:SystemBackdropTheme.Dark;
        _controller.TintColor=light?ColorHelper.FromArgb(255,229,239,254):ColorHelper.FromArgb(255,30,43,68);
        _controller.TintOpacity=light?.38f:.24f;
        _controller.LuminosityOpacity=(float)(light?.65+App.Instance.Prefs.GlassOpacity*.3:.3+App.Instance.Prefs.GlassOpacity*.45);
        _controller.FallbackColor=light?ColorHelper.FromArgb(255,247,249,253):ColorHelper.FromArgb(255,24,27,35);
        ApplyMask();
    }
    private void ResizeMask(object sender,SizeChangedEventArgs args)=>ApplyMask();
    private void ApplyMask()
    {
        if(_target==null||_source==null)return;
        if(_mask==null){var compositor=_source.Compositor;_geometry=compositor.CreateRoundedRectangleGeometry();var shape=compositor.CreateSpriteShape(_geometry);shape.FillBrush=compositor.CreateColorBrush(Colors.White);_maskVisual=compositor.CreateShapeVisual();_maskVisual.Shapes.Add(shape);_maskSurface=compositor.CreateVisualSurface();_maskSurface.SourceVisual=_maskVisual;_mask=compositor.CreateMaskBrush();var maskBrush=compositor.CreateSurfaceBrush(_maskSurface);maskBrush.Stretch=Windows.UI.Composition.CompositionStretch.Fill;maskBrush.BitmapInterpolationMode=Windows.UI.Composition.CompositionBitmapInterpolationMode.Linear;_mask.Mask=maskBrush;}
        if(_geometry==null||_maskVisual==null||_maskSurface==null)return;
        var scale=(float)(_root.XamlRoot?.RasterizationScale??1);var size=new System.Numerics.Vector2((float)_root.ActualWidth*scale,(float)_root.ActualHeight*scale);if(size.X<=0||size.Y<=0)return;
        // Rasterize the coverage mask at 4x then linearly downsample to the window.
        var high=size*4;var inset=(float)CapsuleGeometry.Gutter*scale*4;var contentHeight=(float)CapsuleGeometry.Height*scale*4;_geometry.Offset=new(inset,(high.Y-contentHeight)/2);_geometry.Size=new(high.X-inset*2,contentHeight);_geometry.CornerRadius=new((float)CapsuleGeometry.Radius*scale*4);_maskVisual.Size=high;_maskSurface.SourceSize=high;
        _mask.Source=_source;_target.SystemBackdrop=_mask;
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target){_root.ActualThemeChanged-=ThemeChanged;_root.SizeChanged-=ResizeMask;_controller?.RemoveSystemBackdropTarget(_brushTarget??target);_controller?.Dispose();_controller=null;_target=null;_mask?.Dispose();_maskSurface?.Dispose();_maskVisual?.Dispose();_geometry?.Dispose();base.OnTargetDisconnected(target);}
}
