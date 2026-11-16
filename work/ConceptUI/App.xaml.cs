using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;

namespace LumiBar;
public partial class App : Application
{
    internal static App Instance {get;private set;}=null!;
    internal const bool ConceptMode=true;
    internal Preferences Prefs=new(){City=new("奎文区",36.7,119.1),Theme="Light",GlassOpacity=.62,Glow=.4,LyricsFontSize=32};
    internal readonly SystemFontCatalog Fonts=new();
    internal TaskbarOccupancy Occupancy=null!;internal TaskbarReservation? Reservation;
    internal CapsuleSlots? ActiveSlots;internal CapsulePlacement? ActivePlacement;private readonly ReservationLayoutPolicy _reservedLayout=new();
    internal readonly WeatherService Weather=new();internal readonly MediaService Media=new();internal readonly LyricsService Lyrics=new();internal readonly SpectrumService Spectrum=new();
    internal WeatherWindow WeatherWindow=null!;internal MusicWindow MusicWindow=null!;internal SettingsWindow SettingsWindow=null!;internal CapsuleWindow WeatherCapsule=null!;internal CapsuleWindow MusicCapsule=null!;
    internal Forecast? Forecast;internal Track? Track;internal List<LyricLine> Lines=[];internal string LyricsStatus="播放一首歌，歌词会显示在这里";internal string WeatherStatus="选择一个城市，开始查看天气";
    internal static bool FrontendBackend=>Environment.GetCommandLineArgs().Contains("--frontend-backend");internal bool Exiting;private FrontendApi? _frontendApi;private TrayIcon? _tray;private DispatcherQueueTimer? _timer;private bool _mediaBusy,_weatherBusy;private DateTime _lastMedia,_lastAudio,_lastTheme,_missingSince;private string _theme="";private CancellationTokenSource? _lyricsCts;private int _weatherGeneration,_activeWeatherGeneration=-1;private readonly LatestRequest<Forecast> _weatherRequests=new();private readonly RefreshSchedule _weatherSchedule=new();private Mutex? _mutex;private WindowMotion _motion=new();private TaskbarWatcher? _watcher;private DateTime _lastLayout;private bool _layoutPending;
    public App(){Instance=this;Environment.SetEnvironmentVariable("LUMIBAR_DATA_DIR",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumiBar.Concept"));InitializeComponent();UnhandledException+=(_,e)=>{Log("UI",e.Exception);e.Handled=true;};}
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartConcept();return;
#pragma warning disable CS0162
        _mutex=new Mutex(true,"Local\\LumiBar-5F98F556-86DE-48A8-B81B-5296D2A1C9A4"+(FrontendBackend?"-FrontendBackend":""),out var created);if(!created){Exit();return;}
        if(Environment.GetCommandLineArgs().Contains("--self-test")){RunTests();Exit();return;}
        Occupancy=new();SettingsWindow=new();WeatherWindow=new();MusicWindow=new();WeatherCapsule=new(false);MusicCapsule=new(true);
        if(!FrontendBackend)_tray=new TrayIcon(SettingsWindow,right=>{if(right)SettingsWindow.ShowQuickMenu();else OpenSettings();});
        var dispatcher=DispatcherQueue.GetForCurrentThread();Media.Changed+=()=>dispatcher.TryEnqueue(()=>_lastMedia=default);
        _frontendApi=new(this,dispatcher);if(!FrontendBackend){Reservation=new();Reservation.Changed+=()=>dispatcher.TryEnqueue(()=>{Layout();_frontendApi?.Notify("windows");});Occupancy.Changed+=()=>dispatcher.TryEnqueue(Layout);}ApplyTheme(true);WeatherWindow.Render();MusicWindow.RenderTrack(null);WeatherCapsule.Refresh();
        _timer=DispatcherQueue.GetForCurrentThread().CreateTimer();_timer.Interval=TimeSpan.FromMilliseconds(16);_timer.Tick+=Tick;_timer.Start();
        _watcher=new TaskbarWatcher(()=>{if(_layoutPending)return;_layoutPending=true;DispatcherQueue.GetForCurrentThread().TryEnqueue(()=>{_layoutPending=false;Layout();});}); if(Prefs.City==null){if(!FrontendBackend)OpenWeather();}else _=RefreshWeather();
        if(!FrontendBackend&&Environment.GetCommandLineArgs().Contains("--preview"))OpenSettings();
#pragma warning restore CS0162
    }
    private async void Tick(DispatcherQueueTimer timer,object args)
    {
        try{
            var now=DateTime.UtcNow;if((now-_lastLayout).TotalMilliseconds>500){_lastLayout=now;Layout();}
            if((now-_lastTheme).TotalSeconds>2){_lastTheme=now;ApplyTheme();}
            if((now-_lastAudio).TotalSeconds>5){_lastAudio=now;if(Track?.Playing==true&&Prefs.Spectrum)Spectrum.EnsureDevice();else Spectrum.Stop();}
            MusicCapsule.UpdateSpectrum();if(MusicWindow.IsOpen)MusicWindow.UpdatePosition();
            if(!_mediaBusy&&(now-_lastMedia).TotalSeconds>.9){_mediaBusy=true;_lastMedia=now;try{await ReadMedia();}catch(Exception ex){Log("media",ex);}finally{_mediaBusy=false;}}
            if(Prefs.City!=null&&!_weatherBusy&&_weatherSchedule.IsDue(DateTimeOffset.UtcNow))_=RefreshWeather();
        }catch(Exception ex){Log("tick",ex);}
    }
    private async Task ReadMedia()
    {
        var current=await Media.Read();if(Exiting)return;
        if(current==null){
            if(_missingSince==default)_missingSince=DateTime.UtcNow;
            if((DateTime.UtcNow-_missingSince).TotalSeconds<1.3)return;
            if(Track!=null){Track=null;Lines=[];LyricsStatus="播放一首歌，歌词会显示在这里";_lyricsCts?.Cancel();MusicWindow.RenderTrack(null);MusicCapsule.SetPresent(false);Spectrum.Stop();_frontendApi?.Notify("media");}
            return;
        }
        _missingSince=default;var changed=Track?.Key!=current.Key;var playbackChanged=Track?.Playing!=current.Playing;var notify=changed||playbackChanged||Track?.Controls!=current.Controls||Track?.ShuffleEnabled!=current.ShuffleEnabled||Track?.RepeatMode!=current.RepeatMode||!current.Playing&&Math.Abs((Track?.Position??0)-current.Position)>.5;Track=current;if(notify)_frontendApi?.Notify("media");
        if(changed){MusicWindow.RenderTrack(current);_ = LoadLyrics();}if(notify)MusicCapsule.Refresh();
        if(playbackChanged){MusicWindow.UpdatePlayback();if(current.Playing&&Prefs.Spectrum)Spectrum.EnsureDevice();else Spectrum.Stop();}
        MusicCapsule.SetPresent(Prefs.ShowMusic);MusicWindow.UpdatePosition();
    }
    internal async Task LoadLyrics()
    {
        if(ConceptMode){SetDemoLyrics();MusicWindow.RenderLyrics();return;}
        _lyricsCts?.Cancel();_lyricsCts?.Dispose();_lyricsCts=new();var token=_lyricsCts.Token;var track=Track;
        Lines=[];LyricsStatus=Prefs.AutoLyrics?"正在匹配歌词…":"自动匹配已关闭，可导入 LRC";MusicWindow.RenderLyrics();_frontendApi?.Notify("lyrics");if(track==null)return;
        try{var result=await Lyrics.Get(track,token,Prefs.AutoLyrics);if(token.IsCancellationRequested||Track?.Key!=track.Key)return;Lines=result.lines;LyricsStatus=result.status;MusicWindow.RenderLyrics();_frontendApi?.Notify("lyrics");}
        catch(OperationCanceledException){}catch(Exception ex){Log("lyrics",ex);if(!Exiting&&!token.IsCancellationRequested&&Track?.Key==track.Key){LyricsStatus="歌词暂时无法加载";MusicWindow.RenderLyrics();_frontendApi?.Notify("lyrics");}}
    }
    internal async Task RefreshWeather()
    {
        if(ConceptMode){SetDemoWeather(_weatherCode);WeatherWindow.Render();WeatherCapsule.Refresh();return;}
        var city=Prefs.City;if(city==null||Exiting||_weatherBusy&&_activeWeatherGeneration==_weatherGeneration)return;
        var generation=_weatherGeneration;_activeWeatherGeneration=generation;_weatherBusy=true;
        if(Forecast==null)Forecast=Weather.Cached(city);WeatherStatus="正在更新…";WeatherWindow.Render();_frontendApi?.Notify("weather");
        try
        {
            var request=await _weatherRequests.Run($"{city.Latitude:R}/{city.Longitude:R}",ct=>Weather.Get(city,ct));
            if(!Exiting&&generation==_weatherGeneration&&request.Version==_weatherRequests.Version)
            {Forecast=request.Value;WeatherStatus=$"更新于 {Forecast.Updated:HH:mm}";_weatherSchedule.Success(DateTimeOffset.UtcNow,Prefs.WeatherRefreshMinutes);}
        }
        catch(OperationCanceledException) when(Exiting||generation!=_weatherGeneration){}
        catch(Exception ex)
        {
            Log("weather",ex);if(!Exiting&&generation==_weatherGeneration){_weatherSchedule.Failure(DateTimeOffset.UtcNow);WeatherStatus=Forecast==null?"天气暂时无法更新，稍后自动重试":$"更新失败 · 保留 {Forecast.Updated:MM-dd HH:mm} 的天气";}
        }
        finally{if(generation==_weatherGeneration){_weatherBusy=false;if(!Exiting){WeatherWindow.Render();WeatherCapsule.Refresh();_frontendApi?.Notify("weather");}}}
    }
    internal void SelectCity(City city){Prefs.City=city;if(!Prefs.Favorites.Any(c=>Math.Abs(c.Latitude-city.Latitude)<.001&&Math.Abs(c.Longitude-city.Longitude)<.001))Prefs.Favorites.Add(city);_weatherGeneration++;_weatherSchedule.Invalidate();Forecast=Weather.Cached(city);Save();WeatherWindow.Render();_=RefreshWeather();}
    internal string Temperature(double value){var rounded=Math.Round(Prefs.Fahrenheit?value*1.8+32:value);return $"{(rounded==0?0:rounded):0}°";}
    internal void Save(bool persist=true){if(_chosenTheme!=Prefs.Theme){_chosenTheme=Prefs.Theme;ApplyTheme(true);}foreach(var window in new SurfaceWindow[]{WeatherWindow,MusicWindow,SettingsWindow,WeatherCapsule,MusicCapsule}){window.ApplyFont();(window.SystemBackdrop as GlassBackdrop)?.Refresh();}WeatherCapsule.Refresh();MusicCapsule.Refresh();MusicWindow.ApplyGlow();WeatherWindow.ApplyEffects();SettingsWindow.ApplyPreview();PlaceDemoCapsules();}
    private void ApplyTheme(bool force=false)
    {
        using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var appLight=(int?)key?.GetValue("AppsUseLightTheme")!=0;var taskLight=(int?)key?.GetValue("SystemUsesLightTheme")!=0;
        var signature=$"{Prefs.Theme}/{appLight}/{taskLight}/{Prefs.Glow}/{Prefs.CoverGlow}";if(!force&&signature==_theme)return;_theme=signature;
        foreach(var win in new SurfaceWindow[]{WeatherWindow,MusicWindow,SettingsWindow,WeatherCapsule,MusicCapsule}){
            var light=Prefs.Theme=="Light"||Prefs.Theme=="System"&&(win is CapsuleWindow?taskLight:appLight);
            win.Root.RequestedTheme=light?ElementTheme.Light:ElementTheme.Dark;(win.SystemBackdrop as GlassBackdrop)?.Refresh();
        }
        MusicWindow.ApplyGlow();MusicCapsule.ApplyGlow();_frontendApi?.Notify("appearance");
    }
    private void Layout()
    {
        if(FrontendBackend)return;var a=Native.ReadTaskbar(Occupancy);if(a==null)return;var hidden=!a.Visible||Prefs.HideInFullscreen&&Native.ForegroundCoversTaskbar(a);
        if(hidden){WeatherCapsule.HideSurface();MusicCapsule.HideSurface();ActiveSlots=new(new(null,"hidden"),new(null,"hidden"),"hidden");return;}
        var scale=a.Scale;int ww=Prefs.ShowWeather?(int)Math.Ceiling(104*scale):0,mw=(Track!=null||MusicCapsule.Fading)&&Prefs.ShowMusic?(int)Math.Ceiling(Prefs.MusicWidth*scale):0,gap=(int)Math.Ceiling(Prefs.Gap*scale);
        var reservation=Reservation?.Snapshot;var slot=reservation?.Reserved==true?reservation.Slot:null;
        var bar=new ScreenRect(a.Bar.Left,a.Bar.Top,a.Bar.Right,a.Bar.Bottom);
        var plan=_reservedLayout.Calculate(bar,a.GeometryVerified?a.Left:a.Right,a.Right,slot,scale,ww,mw,gap,reservation?.Compatible==true);
        Reservation?.SetWidth(plan.ReserveDip);
        var layout=plan.Capsules;var height=Math.Min((int)Math.Ceiling(34*scale),a.Bar.Height-4);
        // Keep both Lite capsules visible above the edge during unavailable / reconnecting native layout.
        // Never fall back to painting over application buttons or dropping music due to crowding.
        if(plan.Elevated){layout=layout with{WeatherWidth=ww>0?(int)Math.Ceiling(64*scale):0,MusicWidth=mw>0?(int)Math.Ceiling(48*scale):0,Lite=true};bar=new(bar.Left,bar.Top-height-gap,bar.Right,bar.Top-gap);}
        var slots=SlotPlanner.Calculate(bar,slot?.Right??a.Right,gap,height,layout with{RequiresReservation=false},true);ActiveSlots=slots;ActivePlacement=layout;
        WeatherCapsule.SetLite(layout.Lite);MusicCapsule.SetLite(layout.Lite);
        if(slots.WeatherSlot?.Rect is {} w){_motion.Target(WeatherCapsule,w.Left,w.Top,w.Right-w.Left,w.Bottom-w.Top,Prefs.Animations);WeatherCapsule.IsOpen=true;}else WeatherCapsule.HideSurface();
        if(slots.MusicSlot?.Rect is {} m){_motion.Target(MusicCapsule,m.Left,m.Top,m.Right-m.Left,m.Bottom-m.Top,Prefs.Animations);MusicCapsule.IsOpen=true;}else if(!MusicCapsule.Fading)MusicCapsule.HideSurface();
    }
    private double Smooth(double current,double target)=>double.IsNaN(current)||!Prefs.Animations||Math.Abs(current-target)>600?target:Math.Abs(current-target)<1?target:current+(target-current)*.3;
    internal void PositionFlyout(SurfaceWindow window,int width,int height)
    {
        if(ConceptMode){PlaceConcept(window,width,height);return;}
        var a=Native.ReadTaskbar();if(a==null){window.AppWindow.Resize(new(width,height));return;}
        var s=a.Scale;var w=(int)(width*s);var h=(int)Math.Min(height*s,Math.Max(340*s,a.Bar.Top-24*s));
        var x=Math.Clamp(a.Right-w-(int)(Prefs.Gap*s),a.Bar.Left+8,Math.Max(a.Bar.Left+8,a.Bar.Right-w-8));var y=Math.Max(8,a.Bar.Top-h-(int)(10*s));Native.Place(window,x,y,w,h);
    }
    internal void OpenWeather(){PositionFlyout(WeatherWindow,480,WeatherWindow.PreferredHeight());WeatherWindow.ShowSurface();}
    internal void OpenMusic(){PositionFlyout(MusicWindow,420,650);MusicWindow.ShowSurface();}
    internal void OpenSettings(){PositionFlyout(SettingsWindow,560,650);SettingsWindow.ShowSurface();}
    internal void Quit(){if(Exiting)return;Exiting=true;_demoTimer?.Stop();Exit();}
    internal static void Log(string category,Exception ex){try{Directory.CreateDirectory(Preferences.DataPath);var file=Path.Combine(Preferences.DataPath,"diagnostics.log");if(File.Exists(file)&&new FileInfo(file).Length>512000)File.Move(file,file+".old",true);File.AppendAllText(file,$"{DateTime.Now:O} [{category}] {ex}\n");}catch{}}
    private static void RunTests(){
        var results=new List<string>();void Check(bool ok,string name){results.Add($"{(ok?"PASS":"FAIL")} {name}");}
        var lines=Lrc.Parse("[00:01.50]one\n[00:03][00:04.123]two\n[offset:100]");Check(lines.Count==3&&Math.Abs(lines[0].Seconds-1.4)<.001,"LRC timestamps and offset");Check(Lrc.Active(lines,.2)==-1&&Lrc.Active(lines,3.95)==1,"lyric active-line binary search");
        var p=new PlacementPolicy().Calculate(300,1000,104,238,8,1);Check(!p.Lite&&p.MusicWidth==238,"full taskbar layout");var p2=new PlacementPolicy().Calculate(850,1000,104,238,8,1);Check(p2.Lite&&p2.MusicWidth==48&&p2.WeatherWidth==64,"crowding retains both Lite capsules");
        Directory.CreateDirectory(Preferences.DataPath);File.WriteAllLines(Path.Combine(Preferences.DataPath,"self-test.txt"),results);Environment.ExitCode=results.Any(x=>x.StartsWith("FAIL"))?1:0;
    }
}



