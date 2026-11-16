using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace LumiBar;
internal class SurfaceWindow : Window
{
    internal Grid Root=new();internal Grid Body=new();internal Grid LightHost=new(){IsHitTestVisible=false};internal GlowLayer Glow;internal bool IsOpen;internal bool KeepOpen;
    private readonly bool _capsule;private int _visibilityRevision;
    private readonly ContentControl _fontHost=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    internal event Action<bool>? SurfaceVisibilityChanged;
    public SurfaceWindow(string title,bool capsule=false)
    {
        _capsule=capsule;Title=title+" · 交互原型";KeepOpen=false;Root.Background=Ui.Brush(Colors.Transparent);Root.Children.Add(LightHost);Root.Children.Add(Body);_fontHost.Content=Root;Content=_fontHost;ApplyFont();Glow=new(LightHost);
        SystemBackdrop=new GlassBackdrop(Root,capsule);if(capsule){Body.Margin=LightHost.Margin=new Thickness(CapsuleGeometry.Gutter,0,CapsuleGeometry.Gutter,0);Body.Height=LightHost.Height=CapsuleGeometry.Height;Body.VerticalAlignment=LightHost.VerticalAlignment=VerticalAlignment.Center;Body.UseLayoutRounding=LightHost.UseLayoutRounding=false;Ui.ClipRounded(Body,(float)CapsuleGeometry.Radius);Ui.ClipRounded(LightHost,(float)CapsuleGeometry.Radius);}Root.ActualThemeChanged+=(_,_)=>Native.RefreshChrome(this);Native.Configure(this,capsule); if(!capsule && Environment.GetCommandLineArgs().Contains("--preview")){AppWindow.IsShownInSwitchers=true;var h=WinRT.Interop.WindowNative.GetWindowHandle(this);Native.SetWindowLong(h,-20,(nint)((long)Native.GetWindowLong(h,-20)&~0x80L));}
        var dither=new GlassDither();if(capsule){dither.Margin=new Thickness(CapsuleGeometry.Gutter,0,CapsuleGeometry.Gutter,0);dither.Height=CapsuleGeometry.Height;dither.VerticalAlignment=VerticalAlignment.Center;dither.UseLayoutRounding=false;dither.Opacity=.18;Ui.ClipRounded(dither,17);}Root.Children.Insert(1,dither);
        AppWindow.Closing+=(_,e)=>{if(!App.Instance.Exiting){e.Cancel=true;HideSurface();}};
        if(!capsule){Activated+=async(_,e)=>{if(e.WindowActivationState==WindowActivationState.Deactivated){await Task.Delay(80);if(!KeepOpen&&!Native.ForegroundWithin(this))HideSurface();}};Root.KeyDown+=(_,e)=>{if(e.Key==Windows.System.VirtualKey.Escape)HideSurface();};}
    }
    internal void ShowSurface(bool activate=true){++_visibilityRevision;IsOpen=true;var visual=Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(Root);visual.StopAnimation("Opacity");visual.Opacity=1;if(activate)Activate();Native.Show(this);Native.RefreshChrome(this);SurfaceVisibilityChanged?.Invoke(true);Ui.Enter(Body,App.Instance.Prefs.Animations);}
    internal async void HideSurface(){if(!IsOpen)return;var revision=++_visibilityRevision;var wasOpen=IsOpen;IsOpen=false;SurfaceVisibilityChanged?.Invoke(false);if(wasOpen&&!_capsule&&App.Instance.Prefs.Animations){Ui.Fade(Root,0,true,.18);await Task.Delay(190);}if(revision==_visibilityRevision&&!IsOpen)Native.Hide(this);}
    internal void ApplyFont(){_fontHost.FontFamily=Typography.Chinese;Typography.Refresh(Root);}
}


