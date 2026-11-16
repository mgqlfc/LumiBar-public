using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace LumiBar;
public partial class App
{
    private void InitializeVisualPolishPreview(bool music)
    {
        if(!FrontendBackend||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LUMIBAR_DATA_DIR")))throw new InvalidOperationException("Visual inspection requires isolated frontend-backend mode.");
        _timer?.Stop();_watcher?.Dispose();
        if(!music){
            var located=new City("武清区",39.38,117.04,"天津市","Asia/Shanghai");LocatedCity=located;Prefs.City=located;Prefs.AutoLocateCity=true;
            Prefs.Favorites=[new("奎文区",36.706,119.13,"山东省","Asia/Shanghai"),new("北京市",39.9,116.4,"北京市","Asia/Shanghai")];
            Forecast=new(8,5,3,66,14.1,45,20,Enumerable.Range(0,24).Select(i=>new HourForecast(DateTime.Now.AddHours(i),8+3*Math.Sin(i/5d),i%4==0?61:3,20)).ToList(),Enumerable.Range(0,10).Select(i=>new DayForecast(DateTime.Today.AddDays(i),3-i*.2,12-i*.3,i%3==0?61:3,20)).ToList(),DateTime.Now,83,24,51,null,"界面预览",PressureHpa:1026,UvIndex:1.6);
            WeatherStatus="上次更新："+DateTime.Now.ToString("HH:mm");WeatherWindow.KeepOpen=true;WeatherWindow.Render();OpenWeather();
        }else{
            var paused=Environment.GetCommandLineArgs().Contains("--inspect-paused");
            Track=new("播放控件预览","LumiBar","播放、暂停与切歌","preview",!paused,36,155,File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Assets","weather-icons-clean.png")),Windows.UI.Color.FromArgb(255,162,154,220),"visual-inspection",new(true,true,true,true),SecondaryAccent:Windows.UI.Color.FromArgb(255,189,168,214));
            LyricsStatus="界面预览 · 控件不连接播放器";Lines=[];MusicWindow.KeepOpen=true;MusicWindow.RenderTrack(Track);OpenMusic();
            // This static preview never forwards transport input to real audio.
            MusicWindow.Body.IsHitTestVisible=false;var seen=new HashSet<DependencyObject>();
            void DisableFocus(DependencyObject node){if(!seen.Add(node))return;if(node is Control control)control.IsTabStop=false;if(node is Panel panel)foreach(var child in panel.Children)DisableFocus(child);if(node is Border border&&border.Child!=null)DisableFocus(border.Child);if(node is ContentControl content&&content.Content is DependencyObject childContent)DisableFocus(childContent);for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)DisableFocus(VisualTreeHelper.GetChild(node,i));}
            DisableFocus(MusicWindow.Body);
            if(Environment.GetCommandLineArgs().Contains("--inspect-capsule-polish")){
                MusicCapsule.SetLite(false);MusicCapsule.Refresh();MusicCapsule.SetExtent(238,1);
                MusicCapsule.AppWindow.IsShownInSwitchers=true;
                var handle=WinRT.Interop.WindowNative.GetWindowHandle(MusicCapsule);Native.SetWindowLong(handle,-20,(nint)((long)Native.GetWindowLong(handle,-20)&~0x80L));
                Native.Place(MusicCapsule,300,300,258,54);MusicCapsule.KeepOpen=true;MusicCapsule.ShowSurface(false,false);MusicCapsule.SetPresent(true);
                // This fixture must never forward transport commands either.
                void DisableTransport(DependencyObject node){if(node is Button b){var name=Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b);if(name is "上一首" or "播放 / 暂停" or "下一首")b.IsEnabled=false;}for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)DisableTransport(VisualTreeHelper.GetChild(node,i));}
                DisableTransport(MusicCapsule.Body);
            }
        }
    }
}
