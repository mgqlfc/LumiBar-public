using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Numerics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace LumiBar;
public partial class App
{
    internal HoverPreviewWindow WeatherHover=null!,MusicHover=null!;
    private string _chosenTheme="Light";private DispatcherQueueTimer? _demoTimer;private ConceptLauncher? _launcher;private byte[][] _covers=[];private int _song,_weatherCode;private bool _demoLite;private long _demoLast;
    internal async void StartConcept()
    {
        _mutex=new Mutex(true,"Local\\LumiBar-Concept-20261114",out var created);if(!created){Exit();return;}
        _covers=[await CreateCover(false),await CreateCover(true)];SetDemoTrack();SetDemoWeather(0);SetDemoLyrics();
        SettingsWindow=new();WeatherWindow=new();MusicWindow=new();WeatherCapsule=new(false);MusicCapsule=new(true);WeatherHover=new(false);MusicHover=new(true);
        ApplyTheme(true);MusicWindow.Root.RequestedTheme=ElementTheme.Dark;(MusicWindow.SystemBackdrop as GlassBackdrop)?.Refresh();WeatherWindow.Render();MusicWindow.RenderTrack(Track);MusicCapsule.Refresh();
        _launcher=new ConceptLauncher();_launcher.Activate();PlaceConcept(_launcher,660,150);OpenWeather();OpenMusic();OpenSettings();PlaceDemoCapsules();
        _demoLast=Environment.TickCount64;_demoTimer=DispatcherQueue.GetForCurrentThread().CreateTimer();_demoTimer.Interval=TimeSpan.FromMilliseconds(33);
        _demoTimer.Tick+=(_,_)=>{var now=Environment.TickCount64;var dt=Math.Clamp((now-_demoLast)/1000d,0,.2);_demoLast=now;if(Track is {} t&&t.Playing)Track=t with{Position=(t.Position+dt)%t.Duration};MusicWindow.UpdatePosition();MusicWindow.UpdateSpectrum();MusicCapsule.UpdateSpectrum();SettingsWindow.UpdatePreview();};_demoTimer.Start();
    }
    internal void PlaceConcept(Window window,int width,int height)
    {
        var bar=Native.ReadTaskbar();var s=bar?.Scale??1;var left=bar?.Bar.Left??0;var screenWidth=bar?.Bar.Width??1920;
        var x=window==WeatherWindow?28:window==MusicWindow?524:window==SettingsWindow?960:28;
        var y=window==_launcher?(bar?.Bar.Top??1000)/s-176:24;
        Native.Place(window,left+(int)(Math.Min(x,Math.Max(8,screenWidth/s-width-16))*s),(int)(y*s),(int)(width*s),(int)(height*s));
    }
    internal void PlaceDemoCapsules()
    {
        if(_launcher==null)return;var bar=Native.ReadTaskbar();var s=bar?.Scale??1;var y=(bar?.Bar.Top??1000)-(int)(90*s);var x=(bar?.Bar.Left??0)+(int)(790*s);
        var mw=_demoLite?48:Prefs.MusicWidth;
        MusicCapsule.SetLite(_demoLite);WeatherCapsule.SetLite(_demoLite);var ww=WeatherCapsule.PreferredWeatherWidth();MusicCapsule.SetExtent(mw);WeatherCapsule.SetExtent(ww);
        if(Prefs.ShowMusic&&Track!=null){Native.Place(MusicCapsule,x-(int)(CapsuleGeometry.Gutter*s),y-(int)(CapsuleGeometry.Gutter*s),(int)Math.Round((mw+CapsuleGeometry.Gutter*2)*s),(int)Math.Round(CapsuleGeometry.OuterHeight*s));MusicCapsule.IsOpen=true;MusicCapsule.SetPresent(true);Native.Show(MusicCapsule);}else MusicCapsule.HideSurface();
        if(Prefs.ShowWeather){Native.Place(WeatherCapsule,x+(int)((mw+Prefs.Gap-CapsuleGeometry.Gutter)*s),y-(int)(CapsuleGeometry.Gutter*s),(int)Math.Round((ww+CapsuleGeometry.Gutter*2)*s),(int)Math.Round(CapsuleGeometry.OuterHeight*s));WeatherCapsule.IsOpen=true;Native.Show(WeatherCapsule);}else WeatherCapsule.HideSurface();
    }
    internal void ShowHoverDemo(bool music){(music?MusicHover:WeatherHover).Demonstrate(music?MusicCapsule:WeatherCapsule);}
    internal void DemoLongTitle(){if(Track is {} t){Track=t with{Title=t.Title.Length>20?"微光":"微光漫过漫长的夜晚 — A Little Light Across the Endless Night"};MusicCapsule.Refresh();MusicWindow.RenderTrack(Track);SettingsWindow.ApplyPreview();}}
    internal void ToggleLite(){_demoLite=!_demoLite;PlaceDemoCapsules();}
    internal float[] DemoLevels()=>Enumerable.Range(0,20).Select(i=>Track?.Playing==true?(float)(.13+.62*Math.Pow((1+Math.Sin((Prefs.Animations?Environment.TickCount64:0)/410d+i*.51))/2,2)*(.5+.5*Math.Sin((Prefs.Animations?Environment.TickCount64:0)/1700d+i*.22))):0).ToArray();
    internal Task DemoToggle(){if(Track is {} t)Track=t with{Playing=!t.Playing};MusicWindow.UpdatePlayback();MusicCapsule.Refresh();SettingsWindow.ApplyPreview();if(MusicHover.IsOpen)MusicHover.Render();return Task.CompletedTask;}
    internal Task DemoNext(){_song=1-_song;SetDemoTrack();SetDemoLyrics();MusicWindow.RenderTrack(Track);MusicCapsule.Refresh();SettingsWindow.ApplyPreview();if(MusicHover.IsOpen)MusicHover.Render();return Task.CompletedTask;}
    internal Task DemoSeek(double position){if(Track is {} t)Track=t with{Position=Math.Clamp(position,0,t.Duration)};MusicWindow.UpdatePosition();return Task.CompletedTask;}
    internal void DemoCloseSession(){Track=null;Lines=[];MusicHover.Cancel();MusicWindow.RenderTrack(null);MusicCapsule.SetPresent(false);MusicHover.Cancel();}
    internal void DemoRestore(){SetDemoTrack();SetDemoLyrics();MusicWindow.RenderTrack(Track);MusicCapsule.SetPresent(true);MusicCapsule.Refresh();PlaceDemoCapsules();}
    private void SetDemoTrack(){Track=new(_song==0?"微光":"海风吹过漫长的夏日傍晚",_song==0?"Lumi Studio":"Lumi Studio / 夏日合奏","玻璃之间","LumiBar 概念预览",false,84,222,_covers[_song],_song==0?ColorHelper.FromArgb(255,244,151,129):ColorHelper.FromArgb(255,70,193,195),"concept-"+_song,new(true,true,true,true),SecondaryAccent:_song==0?ColorHelper.FromArgb(255,139,122,225):ColorHelper.FromArgb(255,83,135,215));}
    internal void SetDemoLyrics(){Lines=[new(0,"城市渐渐安静"),new(30,"风吹过未眠的街"),new(60,"把光留在窗边"),new(80,"让旋律慢慢靠近"),new(110,"此刻只听见你"),new(150,"晚风藏起了时间"),new(185,"我们等下一次天明")];LyricsStatus="概念预览 · 示例歌词";}
    internal void SetDemoWeather(int code)
    {
        _weatherCode=code;var start=DateTime.Today.AddHours(17);var temps=new[]{6d,7,9,11,13,14,13,11};var codes=new[]{0,0,3,3,3,3,3,3};
        var hours=Enumerable.Range(0,24).Select(i=>new HourForecast(start.AddHours(i),temps[Math.Min(7,i*8/24)],codes[Math.Min(7,i*8/24)],0,i<2?false:true)).ToList();
        var days=Enumerable.Range(0,10).Select(i=>new DayForecast(DateTime.Today.AddDays(i),6+i%3,14+i%2,new[]{0,2,3,61}[i%4],i==3?60:0)).ToList();
        var now=DateTimeOffset.UtcNow;var amounts=new[]{.1,.35,.7,.5,.1};var intervals=Enumerable.Range(0,5).Select(i=>new RainInterval(now.AddMinutes(i*15-5),now.AddMinutes(i*15+10),amounts[i])).ToList();
        Forecast=new(12,11,code,64,12,100,code>=51?85:35,hours,days,DateTime.Now,code==WeatherNames.Haze?168:code==WeatherNames.DustStorm?235:83,26,41,null,"概念示例",ShortTermPrecipitation:new(now,"交互原型","示例趋势，仅用于界面预览",intervals),IsDay:code!=1);WeatherStatus="更新于 16:40 · 示例数据";
    }
    internal void CycleWeather(){var codes=new[]{0,2,3,61,45,WeatherNames.Haze,71,WeatherNames.DustStorm,95,1};ChooseWeather(codes[(Array.IndexOf(codes,_weatherCode)+1)%codes.Length]);}
    internal void ChooseWeather(int code){SetDemoWeather(code);WeatherWindow.Render();WeatherCapsule.Refresh();PlaceDemoCapsules();if(WeatherHover.IsOpen)WeatherHover.Render();}
    internal void DemoWeatherWidth(){if(Forecast is {} f){Forecast=f.Temperature==-20?f with{Temperature=12,FeelsLike=11,Rain=35}:f with{Temperature=-20,FeelsLike=-27,Rain=100};WeatherCapsule.Refresh();WeatherWindow.Render();PlaceDemoCapsules();if(WeatherHover.IsOpen)WeatherHover.Render();}}
    private static async Task<byte[]> CreateCover(bool cool)
    {
        const int size=192;var pixels=new byte[size*size*4];var a=cool?new Vector3(67,199,191):new Vector3(255,170,113);var b=cool?new Vector3(69,105,195):new Vector3(135,103,207);
        for(int y=0;y<size;y++)for(int x=0;x<size;x++){var u=x/(float)size;var v=y/(float)size;var t=Math.Clamp(.5+.38*Math.Sin(u*6+v*5)+.15*Math.Cos(v*9-u*4),0,1);var c=Vector3.Lerp(a,b,(float)t);var i=(y*size+x)*4;pixels[i]=(byte)c.X;pixels[i+1]=(byte)c.Y;pixels[i+2]=(byte)c.Z;pixels[i+3]=255;}
        using var stream=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);encoder.SetPixelData(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,size,size,96,96,pixels);await encoder.FlushAsync();stream.Seek(0);using var reader=new DataReader(stream);await reader.LoadAsync((uint)stream.Size);var bytes=new byte[(int)stream.Size];reader.ReadBytes(bytes);return bytes;
    }
}
internal sealed class ConceptLauncher:Window
{
    internal ConceptLauncher()
    {
        Title="LumiBar · 交互原型控制台 · 示例数据";var root=new StackPanel{Padding=new Thickness(12),Spacing=8};Content=root;
        
        
        var row=Ui.Row(6);root.Children.Add(row);var a=App.Instance;
        foreach(var (name,action) in new (string,Action)[]{("天气",a.OpenWeather),("音乐",a.OpenMusic),("设置",a.OpenSettings),("切换天气",a.CycleWeather),("双 Lite",a.ToggleLite),("关闭音乐",a.DemoCloseSession),("恢复音乐",a.DemoRestore)}){var b=new Button{Content=name,Padding=new Thickness(10,5,10,5)};b.Click+=(_,_)=>action();row.Children.Add(b);}
        var previews=Ui.Row(6);root.Children.Add(previews);foreach(var music in new[]{false,true}){var button=new Button{Content=music?"音乐悬停速览":"天气悬停速览",Padding=new Thickness(10,5,10,5)};button.Click+=(_,_)=>a.ShowHoverDemo(music);previews.Children.Add(button);}var weatherChoice=new ComboBox{Width=116,PlaceholderText="选择天气"};var conditions=new[]{0,2,3,61,45,WeatherNames.Haze,71,WeatherNames.DustStorm,95,1};foreach(var condition in conditions)weatherChoice.Items.Add(condition==1?"夜间":WeatherNames.Describe(condition));weatherChoice.SelectionChanged+=(_,_)=>{if(weatherChoice.SelectedIndex>=0)a.ChooseWeather(conditions[weatherChoice.SelectedIndex]);};previews.Children.Add(weatherChoice);var widthCase=new Button{Content="−20° / 100%",Padding=new Thickness(10,5,10,5)};widthCase.Click+=(_,_)=>a.DemoWeatherWidth();previews.Children.Add(widthCase);var longTitle=new Button{Content="长歌名",Padding=new Thickness(10,5,10,5)};longTitle.Click+=(_,_)=>a.DemoLongTitle();previews.Children.Add(longTitle);Closed+=(_,_)=>a.Quit();
    }
}
