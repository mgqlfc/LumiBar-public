using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;
namespace LumiBar;

// Change existing brushes in place. Native acrylic, fonts, layout and media
// services never participate in an accent change.
internal static class AccentTheme
{
    private sealed class Binding(Brush brush,byte[] alpha,bool? darkInk=null){internal readonly WeakReference<Brush> Brush=new(brush);internal byte[] Alpha=alpha;internal bool Active=true;internal bool? DarkInk=darkInk;}
    private static readonly List<Binding> Bindings=[];
    private static readonly ConditionalWeakTable<Brush,Binding> Lookup=new();
    private static readonly DependencyProperty RoleProperty=DependencyProperty.RegisterAttached("AccentRole",typeof(string),typeof(AccentTheme),new PropertyMetadata(null));
    private static readonly DependencyProperty GenerationProperty=DependencyProperty.RegisterAttached("AccentGeneration",typeof(int),typeof(AccentTheme),new PropertyMetadata(0));
    private static int _generation;
    private static readonly List<ResourceDictionary> ResourceScopes=[];
    private static Storyboard? _transition;
    private static bool _initialized;
    internal static AccentPalette Current {get;private set;}=AccentPalettes.Get(AccentPalettes.Default);
    internal static Color Color(uint rgb,byte alpha=255)=>Windows.UI.Color.FromArgb(alpha,(byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb);
    internal static LinearGradientBrush Create(AccentPalette palette,byte alpha=255)=>new(){StartPoint=new(0,0),EndPoint=new(1,1),GradientStops={new(){Color=Color(palette.First,alpha)},new(){Offset=.46,Color=Color(palette.Middle,alpha)},new(){Offset=1,Color=Color(palette.Last,alpha)}}};
    internal static LinearGradientBrush Brush(byte alpha=255){var brush=Create(Current,alpha);Track(brush,alpha);return brush;}
    internal static LinearGradientBrush Area(byte alpha)
    {
        var brush=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1),GradientStops={new(){Color=Color(Current.First,alpha)},new(){Offset=1,Color=Color(Current.Last,0)}}};
        Track(brush);return brush;
    }
    internal static void Track(LinearGradientBrush brush,byte? alpha=null)
    {
        var values=brush.GradientStops.Select(s=>alpha??s.Color.A).ToArray();
        brush.SetValue(RoleProperty,"g:"+string.Join(",",values));
        if(Lookup.TryGetValue(brush,out var old)){old.Alpha=values;old.Active=true;return;}
        var binding=new Binding(brush,values);Lookup.Add(brush,binding);Bindings.Add(binding);
        if(Bindings.Count>512)Bindings.RemoveAll(b=>!b.Brush.TryGetTarget(out _));
    }
    internal static void Untrack(LinearGradientBrush brush){brush.ClearValue(RoleProperty);if(Lookup.TryGetValue(brush,out var binding))binding.Active=false;}
    private static void Ink(ResourceDictionary dictionary,string key,bool dark)
    {
        var brush=new SolidColorBrush(Color(dark?Current.DarkInk:Current.LightInk));dictionary[key]=brush;
        brush.SetValue(RoleProperty,dark?"dark":"light");
        var binding=new Binding(brush,[255],dark);Lookup.Add(brush,binding);Bindings.Add(binding);
    }
    internal static void Initialize(string id)
    {
        if(_initialized){Apply(id,false);return;}_initialized=true;Current=AccentPalettes.Get(id);
        foreach(var dictionary in Application.Current.Resources.MergedDictionaries.Where(d=>d.Source?.OriginalString.EndsWith("GlassTokens.xaml",StringComparison.OrdinalIgnoreCase)==true))
        foreach(var item in dictionary.ThemeDictionaries)
        {
            if(item.Value is not ResourceDictionary theme)continue;var dark=item.Key.ToString()=="Dark";
            ResourceScopes.Add(theme);
            foreach(var key in new[]{"SliderTrackValueFill","SliderTrackValueFillPointerOver","SliderTrackValueFillPressed","SliderThumbBackground","SliderThumbBackgroundPointerOver","SliderThumbBackgroundPressed","ToggleSwitchFillOn","ToggleSwitchFillOnPointerOver","ToggleSwitchFillOnPressed","SystemControlHighlightAccentBrush","TextControlBorderBrushFocused","ComboBoxBorderBrushFocused","ProgressBarForeground","NavigationViewSelectionIndicatorForeground"})
            {var brush=Create(Current);theme[key]=brush;Track(brush);}
            foreach(var key in new[]{"AccentFillColorDefaultBrush","AccentFillColorSecondaryBrush","AccentTextFillColorPrimaryBrush","AccentTextFillColorSecondaryBrush","HyperlinkForeground","HyperlinkForegroundPointerOver","FocusStrokeColorOuterBrush"})Ink(theme,key,dark);
        }
    }
    private static void RecoverLiveBindings()
    {
        // Native XAML can retain a brush after its managed wrapper is collected.
        // Re-read native ownership and attached roles, like Typography.Refresh.
        void Recover(Brush? brush){
            if(brush==null||brush.GetValue(RoleProperty) is not string role)return;
            if(Lookup.TryGetValue(brush,out var known)){known.Active=true;return;}
            Binding binding;
            if(brush is LinearGradientBrush&&role.StartsWith("g:")){binding=new(brush,role[2..].Split(',').Select(byte.Parse).ToArray());}
            else if(brush is SolidColorBrush&&role is "light" or "dark")binding=new(brush,[255],role=="dark");
            else return;
            Lookup.Add(brush,binding);Bindings.Add(binding);
        }
        void Resources(ResourceDictionary dictionary){foreach(var pair in dictionary)if(pair.Value is Brush brush)Recover(brush);}
        foreach(var scope in ResourceScopes)Resources(scope);
        var visited=new HashSet<DependencyObject>();
        void Walk(DependencyObject node){
            if(!visited.Add(node))return;
            if(node is FrameworkElement e)Resources(e.Resources);
            if(node is Microsoft.UI.Xaml.Controls.Control control){Recover(control.Background);Recover(control.Foreground);Recover(control.BorderBrush);}
            if(node is Microsoft.UI.Xaml.Controls.Panel panel){Recover(panel.Background);foreach(var child in panel.Children)Walk(child);}
            if(node is Microsoft.UI.Xaml.Controls.Border border){Recover(border.Background);Recover(border.BorderBrush);if(border.Child!=null)Walk(border.Child);}
            if(node is Microsoft.UI.Xaml.Controls.TextBlock text)Recover(text.Foreground);
            if(node is Microsoft.UI.Xaml.Shapes.Shape shape){Recover(shape.Fill);Recover(shape.Stroke);}
            if(node is Microsoft.UI.Xaml.Controls.ContentControl content&&content.Content is DependencyObject childContent)Walk(childContent);
            for(var i=0;i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);i++)Walk(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node,i));
        }
        var app=App.Instance;
        foreach(var window in new SurfaceWindow?[]{app.SettingsWindow,app.WeatherWindow,app.MusicWindow,app.WeatherCapsule,app.MusicCapsule,app.WeatherHover,app.MusicHover})if(window!=null)Walk(window.Root);
    }
    internal static bool Apply(string id,bool animate)
    {
        var palette=AccentPalettes.Get(id);if(palette.Id==Current.Id)return false;Current=palette;
        RecoverLiveBindings();
        var targets=new List<(DependencyObject Target,Color From,Color To)>();
        var generation=unchecked(++_generation);if(generation==0)generation=++_generation;
        void Add(DependencyObject target,Color from,Color to){
            // Several WinRT wrappers can refer to the same native stop. Keep the
            // marker on the native object so each property gets one animation.
            if((int)target.GetValue(GenerationProperty)==generation)return;
            target.SetValue(GenerationProperty,generation);targets.Add((target,from,to));
        }
        Bindings.RemoveAll(b=>!b.Brush.TryGetTarget(out _));
        foreach(var binding in Bindings.Where(b=>b.Active)){
            if(!binding.Brush.TryGetTarget(out var brush)||brush.GetValue(RoleProperty) is not string role)continue;
            if(brush is LinearGradientBrush gradient){var alpha=role[2..].Split(',').Select(byte.Parse).ToArray();for(var i=0;i<gradient.GradientStops.Count;i++){var stop=gradient.GradientStops[i];Add(stop,stop.Color,Color(palette.Sample(stop.Offset),alpha[Math.Min(i,alpha.Length-1)]));}}
            else if(brush is SolidColorBrush solid)Add(solid,solid.Color,Color(role=="dark"?palette.DarkInk:palette.LightInk));
        }
        _transition?.Stop();_transition=null;
        var storyboard=animate?new Storyboard():null;
        foreach(var (target,from,to) in targets){
            if(target is GradientStop stop)stop.Color=to;else ((SolidColorBrush)target).Color=to;
            if(storyboard==null||from==to)continue;
            var color=new ColorAnimation{From=from,To=to,Duration=new Duration(TimeSpan.FromMilliseconds(220)),EnableDependentAnimation=true,EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
            Storyboard.SetTarget(color,target);Storyboard.SetTargetProperty(color,"Color");storyboard.Children.Add(color);
        }
        if(storyboard?.Children.Count>0){_transition=storyboard;storyboard.Completed+=(_,_)=>{if(_transition==storyboard){_transition=null;storyboard.Stop();}};try{storyboard.Begin();}catch(Exception ex){_transition=null;storyboard.Stop();App.Log("accent-transition",ex);}}
        return true;
    }
}
