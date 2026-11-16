using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;

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
    private readonly string _name;
    private readonly Border _densityWash;
    private ElementTheme? _materialTheme;
    private ICompositionSupportsSystemBackdrop? _target;
    private Windows.UI.Composition.CompositionMaskBrush? _mask;
    private Windows.UI.Composition.CompositionVisualSurface? _maskSurface;
    private Windows.UI.Composition.ShapeVisual? _maskVisual;
    private Windows.UI.Composition.CompositionRoundedRectangleGeometry? _geometry;
    private Windows.UI.Composition.CompositionColorBrush? _coverage;
    private readonly SystemBackdropConfiguration _configuration=new(){IsInputActive=true};
    internal GlassBackdrop(FrameworkElement root,bool capsule=false)
    {
        _root=root;_capsule=capsule;_name=capsule?"capsule":"popup-"+System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(root);
        _densityWash=new Border{IsHitTestVisible=false,Opacity=0};
        if(capsule){_densityWash.Height=CapsuleGeometry.Height;_densityWash.Margin=new Thickness(CapsuleGeometry.Gutter,0,CapsuleGeometry.Gutter,0);_densityWash.VerticalAlignment=VerticalAlignment.Center;_densityWash.CornerRadius=new CornerRadius(CapsuleGeometry.Radius);_densityWash.UseLayoutRounding=false;}
        ((Grid)root).Children.Insert(0,_densityWash);
    }
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target,XamlRoot xamlRoot)
    {
        GlassDiagnostics.Connected(_name);base.OnTargetConnected(target,xamlRoot);if(!DesktopAcrylicController.IsSupported())return;
        _target=target;_root.SizeChanged+=ResizeMask;_root.Loaded+=Loaded;_brushTarget=new BrushTarget(brush=>{GlassDiagnostics.Source();GlassDiagnostics.Event("source",_name);_source=brush;ApplyMask();});
        _controller=new DesktopAcrylicController{Kind=DesktopAcrylicKind.Thin};_controller.SetSystemBackdropConfiguration(_configuration);_controller.AddSystemBackdropTarget(_brushTarget??target);
        _root.ActualThemeChanged+=ThemeChanged;Update(target,xamlRoot);
    }
    private void ThemeChanged(FrameworkElement sender,object args)=>Refresh();
    // The acrylic controller owns our configuration. Hidden HWNDs can deliver a
    // late default-config callback after WinUI has detached its target; forwarding
    // that callback to the base implementation throws E_INVALIDARG.
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target,XamlRoot root){GlassDiagnostics.Configuration();GlassDiagnostics.Event("config",_name);if(_controller!=null)Update(target,root);}
    private void Update(ICompositionSupportsSystemBackdrop target,XamlRoot root){var highContrast=new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;if(_configuration.IsHighContrast!=highContrast)_configuration.IsHighContrast=highContrast;Refresh();}
    private void Loaded(object sender,RoutedEventArgs args)=>ApplyMask();
    internal void Refresh([System.Runtime.CompilerServices.CallerMemberName]string caller="")
    {
        GlassDiagnostics.Refresh();
        GlassDiagnostics.Event("refresh-caller",caller);
        if(_controller==null)return;var light=_root.ActualTheme!=ElementTheme.Dark;
        var theme=light?ElementTheme.Light:ElementTheme.Dark;
        if(_materialTheme!=theme){
            GlassDiagnostics.Theme(_name);
            _materialTheme=theme;
            _configuration.Theme=light?SystemBackdropTheme.Light:SystemBackdropTheme.Dark;
            _controller.TintColor=light?Colors.White:ColorHelper.FromArgb(255,30,43,68);
            _controller.TintOpacity=light?(float)(.38+GlassDensity.Minimum*.32):.24f;
            _controller.LuminosityOpacity=(float)(light?.15+GlassDensity.Minimum*.25:.3+GlassDensity.Minimum*.45);
            _controller.FallbackColor=light?ColorHelper.FromArgb(255,247,249,253):ColorHelper.FromArgb(255,24,27,35);
            _densityWash.Background=new SolidColorBrush(light?Colors.White:ColorHelper.FromArgb(255,30,43,68));
        }
        // No controller property changes or mask reassignment while dragging.
        var density=_configuration.IsHighContrast?0:GlassDensity.WashOpacity(App.Instance.Prefs.GlassOpacity,light);
        if(Math.Abs(_densityWash.Opacity-density)>1e-8)_densityWash.Opacity=density;
    }
    private void ResizeMask(object sender,SizeChangedEventArgs args){GlassDiagnostics.Size();ApplyMask();}
    internal void Show()
    {
        if(_coverage==null)return;
        _coverage.StopAnimation("Color");_coverage.Color=Colors.White;
    }
    internal async Task FadeOut(int milliseconds)
    {
        if(_coverage==null)return;
        var compositor=_coverage.Compositor;
        var finished=new TaskCompletionSource<bool>();
        using var batch=compositor.CreateScopedBatch(Windows.UI.Composition.CompositionBatchTypes.Animation);
        batch.Completed+=(_,_)=>finished.TrySetResult(true);
        using var fade=compositor.CreateColorKeyFrameAnimation();
        fade.InsertKeyFrame(1,ColorHelper.FromArgb(0,255,255,255));
        fade.Duration=TimeSpan.FromMilliseconds(milliseconds);
        fade.StopBehavior=Windows.UI.Composition.AnimationStopBehavior.SetToFinalValue;
        _coverage.StartAnimation("Color",fade);batch.End();await finished.Task;
    }
    private void ApplyMask()
    {
        GlassDiagnostics.Mask();
        GlassDiagnostics.Event("mask",_name);
        if(_target==null||_source==null)return;
        // Popup acrylic is a separate native composition tree from the XAML
        // content. Leave a transparent gutter for the shadow we own and fade.
        if(!_capsule)
        {
            // Supersample rounded coverage, then downsample like the capsules;
            // one physical pixel per mask sample shows stair steps on light glass.
            var popupScale=(float)(_root.XamlRoot?.RasterizationScale??1)*2;
            var inner=new System.Numerics.Vector2((float)_root.ActualWidth*popupScale,(float)_root.ActualHeight*popupScale);
            if(inner.X<=0||inner.Y<=0)return;
            if(_mask==null)
            {
                var c=_source.Compositor;_coverage=c.CreateColorBrush(Colors.White);
                _geometry=c.CreateRoundedRectangleGeometry();
                var fill=c.CreateSpriteShape(_geometry);fill.FillBrush=_coverage;
                _maskVisual=c.CreateShapeVisual();_maskVisual.Shapes.Add(fill);
                _maskSurface=c.CreateVisualSurface();_maskSurface.SourceVisual=_maskVisual;
                var coverage=c.CreateSurfaceBrush(_maskSurface);coverage.Stretch=Windows.UI.Composition.CompositionStretch.Fill;coverage.BitmapInterpolationMode=Windows.UI.Composition.CompositionBitmapInterpolationMode.Linear;
                _mask=c.CreateMaskBrush();_mask.Mask=coverage;
            }
            var popupInset=PopupShadow.Gutter*popupScale;var full=inner+new System.Numerics.Vector2(popupInset*2);
            _geometry!.Offset=new(popupInset);_geometry.Size=inner;_geometry.CornerRadius=new(12*popupScale);
            _maskVisual!.Size=full;_maskSurface!.SourceSize=full;
            _mask.Source=_source;_target.SystemBackdrop=_mask;return;
        }
        if(_mask==null){var compositor=_source.Compositor;_geometry=compositor.CreateRoundedRectangleGeometry();var shape=compositor.CreateSpriteShape(_geometry);shape.FillBrush=compositor.CreateColorBrush(Colors.White);_maskVisual=compositor.CreateShapeVisual();_maskVisual.Shapes.Add(shape);_maskSurface=compositor.CreateVisualSurface();_maskSurface.SourceVisual=_maskVisual;_mask=compositor.CreateMaskBrush();var maskBrush=compositor.CreateSurfaceBrush(_maskSurface);maskBrush.Stretch=Windows.UI.Composition.CompositionStretch.Fill;maskBrush.BitmapInterpolationMode=Windows.UI.Composition.CompositionBitmapInterpolationMode.Linear;_mask.Mask=maskBrush;}
        if(_geometry==null||_maskVisual==null||_maskSurface==null)return;
        var scale=(float)(_root.XamlRoot?.RasterizationScale??1);var size=new System.Numerics.Vector2((float)_root.ActualWidth*scale,(float)_root.ActualHeight*scale);if(size.X<=0||size.Y<=0)return;
        // Rasterize the coverage mask at 4x then linearly downsample to the window.
        var high=size*4;var inset=(float)CapsuleGeometry.Gutter*scale*4;var contentHeight=(float)CapsuleGeometry.Height*scale*4;_geometry.Offset=new(inset,(high.Y-contentHeight)/2);_geometry.Size=new(high.X-inset*2,contentHeight);_geometry.CornerRadius=new((float)CapsuleGeometry.Radius*scale*4);_maskVisual.Size=high;_maskSurface.SourceSize=high;
        _mask.Source=_source;_target.SystemBackdrop=_mask;
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target){GlassDiagnostics.Disconnected(_name);_root.ActualThemeChanged-=ThemeChanged;_root.SizeChanged-=ResizeMask;_root.Loaded-=Loaded;_controller?.RemoveSystemBackdropTarget(_brushTarget??target);_controller?.Dispose();_controller=null;_target=null;_materialTheme=null;_mask?.Dispose();_maskSurface?.Dispose();_maskVisual?.Dispose();_geometry?.Dispose();_coverage?.Dispose();_mask=null;_maskSurface=null;_maskVisual=null;_geometry=null;_coverage=null;_source=null;base.OnTargetDisconnected(target);}
}
