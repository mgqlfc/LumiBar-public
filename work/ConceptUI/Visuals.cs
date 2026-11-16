using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace LumiBar;
internal static class Ui
{
    public static readonly Color Teal=Color.FromArgb(255,38,151,158);
    public static LinearGradientBrush AccentBrush(byte alpha=255)=>new(){StartPoint=new(0,0),EndPoint=new(1,1),GradientStops={new GradientStop{Color=Color.FromArgb(alpha,108,231,218),Offset=0},new GradientStop{Color=Color.FromArgb(alpha,33,190,194),Offset=.46},new GradientStop{Color=Color.FromArgb(alpha,17,143,177),Offset=1}}};
    public static SolidColorBrush Brush(Color c)=>new(c);
    public static TextBlock Text(string text,double size=14,double opacity=1){var block=new TextBlock{Text=text,FontSize=size,Opacity=opacity,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};Typography.Attach(block);return block;}
    public static FontIcon Icon(string glyph,double size=18)=>new(){Glyph=glyph,FontSize=size,FontFamily=new FontFamily("Segoe Fluent Icons")};
    public static Button IconButton(string glyph,string label,RoutedEventHandler click)
    {
        var b=new Button{Content=Icon(glyph),Width=38,Height=38,Padding=new Thickness(0),Background=Brush(Colors.Transparent),BorderThickness=new Thickness(0),CornerRadius=new CornerRadius(10)};
        ToolTipService.SetToolTip(b,label);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b,label);b.Click+=click;PressFeedback(b);return b;
    }
    public static StackPanel Stack(double gap=10)=>new(){Spacing=gap};
    public static StackPanel Row(double gap=10)=>new(){Orientation=Orientation.Horizontal,Spacing=gap,VerticalAlignment=VerticalAlignment.Center};
    public static Border Panel(UIElement child,double padding=18)=>new(){Child=child,Padding=new Thickness(padding),CornerRadius=new CornerRadius(14),Background=(Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],BorderBrush=(Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],BorderThickness=new Thickness(1)};
    public static void Fade(UIElement element,double opacity,bool animate=true,double seconds=.3)
    {
        var visual=ElementCompositionPreview.GetElementVisual(element);
        if(!animate){visual.Opacity=(float)opacity;return;}
        var anim=visual.Compositor.CreateScalarKeyFrameAnimation();anim.InsertKeyFrame(1,(float)opacity);anim.Duration=TimeSpan.FromSeconds(seconds);visual.StartAnimation("Opacity",anim);
    }
    public static void Enter(UIElement element,bool animate)
    {
        var v=ElementCompositionPreview.GetElementVisual(element);v.Opacity=1;if(!animate)return;
        var c=v.Compositor;var ease=c.CreateCubicBezierEasingFunction(new Vector2(.16f,1),new Vector2(.3f,1));
        var fade=c.CreateScalarKeyFrameAnimation();fade.InsertKeyFrame(0,.15f);fade.InsertKeyFrame(1,1,ease);fade.Duration=TimeSpan.FromMilliseconds(300);v.StartAnimation("Opacity",fade);
        ElementCompositionPreview.SetIsTranslationEnabled(element,true);var shift=c.CreateVector3KeyFrameAnimation();shift.InsertKeyFrame(0,new Vector3(0,14,0));shift.InsertKeyFrame(1,Vector3.Zero,ease);shift.Duration=TimeSpan.FromMilliseconds(380);v.StartAnimation("Translation",shift);
    }
    public static void PressFeedback(FrameworkElement element)
    {
        element.PointerPressed+=(_,_)=>Scale(element,.97f);element.PointerReleased+=(_,_)=>Scale(element,1);element.PointerExited+=(_,_)=>Scale(element,1);
    }
    private static void Scale(FrameworkElement e,float scale){if(!App.Instance.Prefs.Animations)return;var v=ElementCompositionPreview.GetElementVisual(e);v.CenterPoint=new Vector3((float)e.ActualWidth/2,(float)e.ActualHeight/2,0);var a=v.Compositor.CreateVector3KeyFrameAnimation();a.InsertKeyFrame(1,new Vector3(scale,scale,1));a.Duration=TimeSpan.FromMilliseconds(140);v.StartAnimation("Scale",a);}
    public static Button More(params (string text,RoutedEventHandler click)[] actions){var menu=new MenuFlyout();foreach(var (text,click) in actions){var item=new MenuFlyoutItem{Text=text};item.Click+=click;menu.Items.Add(item);}var b=IconButton("\uE712","更多",(_,_)=>{});b.Flyout=menu;return b;}
    public static void ClipRounded(FrameworkElement element,float radius){void Update(){var v=ElementCompositionPreview.GetElementVisual(element);var geometry=v.Compositor.CreateRoundedRectangleGeometry();geometry.Size=new Vector2((float)element.ActualWidth,(float)element.ActualHeight);geometry.CornerRadius=new Vector2(radius);v.Clip=v.Compositor.CreateGeometricClip(geometry);}element.Loaded+=(_,_)=>Update();element.SizeChanged+=(_,_)=>Update();}
    public static string Time(double sec)=>$"{(int)sec/60}:{(int)sec%60:00}";
}

internal sealed class GlowLayer
{
    private readonly Grid _host;private SpriteVisual? _light;private readonly List<CompositionColorGradientStop> _stops=[];private Color _last=Ui.Teal;private double _amount;private bool _enabled=true,_painted;private Vector2 _position,_radius;
    public GlowLayer(Grid host,Vector2? position=null,Vector2? radius=null){_host=host;_position=position??new(.78f,.8f);_radius=radius??new(.85f,.75f);host.Loaded+=(_,_)=>Build();host.SizeChanged+=(_,_)=>{if(_light!=null)_light.Size=new((float)host.ActualWidth,(float)host.ActualHeight);};}
    public void SetGeometry(Vector2 position,Vector2 radius){_position=position;_radius=radius;if(_light?.Brush is CompositionRadialGradientBrush b){b.CenterPoint=position;b.EllipseRadius=radius;}}
    private void Build(){if(_light!=null)return;var c=ElementCompositionPreview.GetElementVisual(_host).Compositor;var b=c.CreateRadialGradientBrush();b.CenterPoint=_position;b.EllipseRadius=_radius;b.InterpolationSpace=CompositionColorSpace.RgbLinear;foreach(var pair in new[]{(0f,(byte)255),(.4f,(byte)180),(.7f,(byte)55),(1f,(byte)0)}){var stop=c.CreateColorGradientStop(pair.Item1,Color.FromArgb(pair.Item2,_last.R,_last.G,_last.B));b.ColorStops.Add(stop);_stops.Add(stop);}_light=c.CreateSpriteVisual();_light.Brush=b;_light.Size=new((float)_host.ActualWidth,(float)_host.ActualHeight);ElementCompositionPreview.SetElementChildVisual(_host,_light);Apply(_last,_amount,_enabled,false);}
    public void Apply(Color color,double amount,bool enabled,bool animate){var changed=color!=_last||amount!=_amount||enabled!=_enabled;_last=color;_amount=amount;_enabled=enabled;if(_light==null||_painted&&!changed)return;_painted=true;var alpha=new byte[]{255,180,55,0};for(int i=0;i<_stops.Count;i++){var stop=_stops[i];var old=stop.Color;var target=Color.FromArgb(alpha[i],color.R,color.G,color.B);stop.StopAnimation("Color");stop.Color=target;if(animate){var anim=stop.Compositor.CreateColorKeyFrameAnimation();anim.InterpolationColorSpace=CompositionColorSpace.RgbLinear;anim.InsertKeyFrame(0,old);anim.InsertKeyFrame(1,target);anim.Duration=TimeSpan.FromMilliseconds(900);stop.StartAnimation("Color",anim);}}var opacity=enabled?(float)Math.Clamp(amount,0,.7):0;var before=_light.Opacity;_light.StopAnimation("Opacity");_light.Opacity=opacity;if(animate){var fade=_light.Compositor.CreateScalarKeyFrameAnimation();fade.InsertKeyFrame(0,before);fade.InsertKeyFrame(1,opacity);fade.Duration=TimeSpan.FromMilliseconds(900);_light.StartAnimation("Opacity",fade);}}
}

internal sealed class SpectrumView : StackPanel
{
    private readonly Rectangle[] _bars;private readonly float[] _last=new float[20];
    public SpectrumView(){Orientation=Orientation.Horizontal;Spacing=2;Height=22;VerticalAlignment=VerticalAlignment.Center;_bars=Enumerable.Range(0,20).Select(_=>new Rectangle{Width=2,Height=2,RadiusX=1,RadiusY=1,VerticalAlignment=VerticalAlignment.Center,Fill=Ui.Brush(Ui.Teal)}).ToArray();foreach(var b in _bars)Children.Add(b);}
    public void Update(float[] values,bool playing,double sensitivity,Color color){for(int i=0;i<_bars.Length;i++){var target=playing?Math.Clamp(values[i]*sensitivity,0,1):0;_last[i]=(float)Math.Max(target,_last[i]*.76f);_bars[i].Height=2+20*_last[i];if(_bars[i].Fill is SolidColorBrush brush)brush.Color=color;}}
}
