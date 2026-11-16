using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace LumiBar;
internal class SurfaceWindow : Window
{
    internal Grid Root=new();internal Grid Body=new();internal Grid LightHost=new(){IsHitTestVisible=false};internal GlowLayer Glow;internal bool IsOpen=>_lifetime.IsOpen;internal bool KeepOpen;
    private readonly bool _capsule;private readonly SurfaceLifetime _lifetime=new();private int _activationRevision;
    internal long VisibilityRevision=>_lifetime.Revision;
    internal bool CanDismiss(long revision)=>_lifetime.CanDismiss(revision)&&!KeepOpen;
    private readonly ContentControl _fontHost=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    private Grid _visualRoot=null!;
    internal event Action<bool>? SurfaceVisibilityChanged;
    protected virtual int ExitDurationMs=>180;
    private Grid CreateVisualRoot(bool capsule)
    {
        if(capsule)return Root;
        var outer=new Grid{Background=Ui.Brush(Colors.Transparent)};
        outer.Children.Add(new PopupShadow());
        Root.Margin=new Thickness(PopupShadow.Gutter);Ui.ClipRounded(Root,12);outer.Children.Add(Root);return outer;
    }
    public SurfaceWindow(string title,bool capsule=false)
    {
        _capsule=capsule;Title=title;KeepOpen=false;Root.Background=Ui.Brush(Colors.Transparent);Root.Children.Add(LightHost);Root.Children.Add(Body);_fontHost.Content=_visualRoot=CreateVisualRoot(capsule);Content=_fontHost;ApplyFont();Glow=new(LightHost);
        SystemBackdrop=new GlassBackdrop(Root,capsule);if(capsule){Body.Margin=LightHost.Margin=new Thickness(CapsuleGeometry.Gutter,0,CapsuleGeometry.Gutter,0);Body.Height=LightHost.Height=CapsuleGeometry.Height;Body.VerticalAlignment=LightHost.VerticalAlignment=VerticalAlignment.Center;Body.UseLayoutRounding=LightHost.UseLayoutRounding=false;Ui.ClipRounded(Body,(float)CapsuleGeometry.Radius);Ui.ClipRounded(LightHost,(float)CapsuleGeometry.Radius);}Root.ActualThemeChanged+=(_,_)=>Native.RefreshChrome(this);Native.Configure(this,capsule); if(!capsule && Environment.GetCommandLineArgs().Contains("--preview")){AppWindow.IsShownInSwitchers=true;var h=WinRT.Interop.WindowNative.GetWindowHandle(this);Native.SetWindowLong(h,-20,(nint)((long)Native.GetWindowLong(h,-20)&~0x80L));}
        var dither=new GlassDither();if(capsule){dither.Margin=new Thickness(CapsuleGeometry.Gutter,0,CapsuleGeometry.Gutter,0);dither.Height=CapsuleGeometry.Height;dither.VerticalAlignment=VerticalAlignment.Center;dither.UseLayoutRounding=false;dither.Opacity=.18;Ui.ClipRounded(dither,17);}Root.Children.Insert(1,dither);
        AppWindow.Closing+=(_,e)=>{if(!App.Instance.Exiting){e.Cancel=true;if(this is SettingsWindow&&!App.Instance.Prefs.CloseToTray)App.Instance.Quit();else HideSurface();}};
        if(!capsule){Activated+=async(_,e)=>{if(App.Instance.Exiting)return;var activation=++_activationRevision;if(e.WindowActivationState==WindowActivationState.Deactivated&&IsOpen){var revision=VisibilityRevision;await Task.Delay(80);if(App.Instance.Exiting)return;if(activation==_activationRevision&&CanDismiss(revision)&&!Native.ForegroundWithin(this)){App.Trace("surface",$"Deactivate {Title} revision={revision}");HideSurface();}}};Root.KeyDown+=(_,e)=>{if(e.Key==Windows.System.VirtualKey.Escape)HideSurface();};}
    }
    internal void ShowSurface(bool activate=true,bool animateEnter=true){_lifetime.Show();var visual=Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_visualRoot);visual.StopAnimation("Opacity");visual.Opacity=1;(SystemBackdrop as GlassBackdrop)?.Show();if(activate)Activate();Native.Show(this);Native.RefreshChrome(this);SurfaceVisibilityChanged?.Invoke(true);Ui.Enter(Body,animateEnter&&App.Instance.Prefs.Animations);if(!_capsule)App.Trace("surface",$"Show {Title} revision={VisibilityRevision}");}
    internal void HideImmediately(){if(!IsOpen&&!Native.IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(this)))return;_lifetime.Close();var visual=Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_visualRoot);visual.StopAnimation("Opacity");visual.Opacity=0;Native.Hide(this);SurfaceVisibilityChanged?.Invoke(false);}
    internal async void HideSurface()
    {
        if(!IsOpen)return;var revision=_lifetime.Close();if(!_capsule)App.Trace("surface",$"Hide start {Title} revision={revision}");
        var visual=Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_visualRoot);
        if(!_capsule&&App.Instance.Prefs.Animations)
        {
            var duration=Environment.GetCommandLineArgs().Contains("--inspect-slow-fade")?3000:ExitDurationMs;
            var finished=new TaskCompletionSource<bool>();
            using var batch=visual.Compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
            batch.Completed+=(_,_)=>finished.TrySetResult(true);
            var fade=visual.Compositor.CreateScalarKeyFrameAnimation();fade.InsertKeyFrame(1,0);
            fade.Duration=TimeSpan.FromMilliseconds(duration);fade.StopBehavior=Microsoft.UI.Composition.AnimationStopBehavior.SetToFinalValue;
            visual.StartAnimation("Opacity",fade);batch.End();
            await Task.WhenAll(finished.Task,(SystemBackdrop as GlassBackdrop)?.FadeOut(duration)??Task.CompletedTask);
        }
        if(App.Instance.Exiting||!_lifetime.CanFinishClose(revision))return;
        visual.StopAnimation("Opacity");visual.Opacity=0;
        Native.Hide(this);
        if(!_capsule)App.Trace("surface",$"Hide complete {Title} revision={revision}");
        // Stop/reset background motion only after the HWND is hidden, so it
        // cannot jump to its initial pose on the last visible animation frame.
        SurfaceVisibilityChanged?.Invoke(false);
    }
    internal void ApplyFont(){_fontHost.FontFamily=Typography.Default;Typography.Refresh(Root);}
}


