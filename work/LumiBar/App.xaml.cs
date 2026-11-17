using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using System.Text.Json;

namespace LumiBar;
public partial class App : Application
{
    internal static App Instance {get;private set;}=null!;
    internal Preferences Prefs=Preferences.Load();
    internal readonly SystemFontCatalog Fonts=new();
    internal TaskbarOccupancy Occupancy=null!;internal TaskbarReservation? Reservation;
    internal CapsuleSlots? ActiveSlots;internal CapsulePlacement? ActivePlacement;private readonly ReservationLayoutPolicy _reservedLayout=new();
    internal readonly WeatherService Weather=new();internal readonly MediaService Media=new();internal readonly LyricsService Lyrics=new();internal readonly SpectrumService Spectrum=new();
    internal WeatherWindow WeatherWindow=null!;internal MusicWindow MusicWindow=null!;internal SettingsWindow SettingsWindow=null!;internal CapsuleWindow WeatherCapsule=null!;internal CapsuleWindow MusicCapsule=null!;
    internal HoverPreviewWindow WeatherHover=null!,MusicHover=null!; internal string MediaStatus=""; internal Forecast? Forecast;internal Track? Track;internal List<LyricLine> Lines=[];internal string LyricsStatus="播放一首歌，歌词会显示在这里";internal string WeatherStatus="选择一个城市，开始查看天气";
    internal static bool FrontendBackend=>Environment.GetCommandLineArgs().Contains("--frontend-backend");internal bool Exiting;private FrontendApi? _frontendApi;private TrayIcon? _tray;private OutsideClickWatcher? _outsideClicks;private DispatcherQueueTimer? _timer;private bool _mediaBusy,_weatherBusy;private DateTime _lastMedia,_lastAudio,_lastTheme,_missingSince;private string _theme="";private CancellationTokenSource? _lyricsCts;private int _weatherGeneration,_activeWeatherGeneration=-1;private readonly LatestRequest<Forecast> _weatherRequests=new();private readonly RefreshSchedule _weatherSchedule=new();private Mutex? _mutex;private WindowMotion _motion=new();private TaskbarWatcher? _watcher;private DateTime _lastLayout;private bool _layoutPending;
    internal City? LocatedCity {get;private set;}
    public App(){Instance=this;LocatedCity=Prefs.AutoLocateCity?Prefs.City:null;InitializeComponent();UnhandledException+=(_,e)=>{Log("UI",e.Exception);e.Handled=true;};}
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if(Environment.GetCommandLineArgs().Contains("--shutdown")){var code=await StartupControl.Send("app.shutdown",true);Exit();Environment.Exit(code);return;}
        var hoverLayoutProbe=Environment.GetCommandLineArgs().Contains("--inspect-hover-layout");
        var settingsSyncProbe=Environment.GetCommandLineArgs().Contains("--inspect-settings-sync");
        var surfaceProbe=Environment.GetCommandLineArgs().Contains("--inspect-surface-cycle")||hoverLayoutProbe;
        var isolatedData=Environment.GetEnvironmentVariable("LUMIBAR_DATA_DIR");
        var dataSuffix=InstanceIdentity.DataSuffix(isolatedData);
        if(settingsSyncProbe&&(!FrontendBackend||string.IsNullOrWhiteSpace(isolatedData)))throw new InvalidOperationException("Settings sync inspection requires an isolated frontend backend data directory.");
        _mutex=new Mutex(true,"Local\\LumiBar-5F98F556-86DE-48A8-B81B-5296D2A1C9A4"+(surfaceProbe?"-SurfaceProbe":FrontendBackend?"-FrontendBackend":"")+dataSuffix,out var created);if(!created){await StartupControl.Send("app.activate");Exit();Environment.Exit(0);return;}
        if(Environment.GetCommandLineArgs().Contains("--self-test")){RunTests();Exit();return;}
        AccentTheme.Initialize(Prefs.AccentColor);
        Occupancy=new();SettingsWindow=new();WeatherWindow=new();MusicWindow=new();WeatherCapsule=new(false);MusicCapsule=new(true);WeatherHover=new(false);MusicHover=new(true);
        if(settingsSyncProbe){await InspectSettingsSync();return;}
        if(hoverLayoutProbe){await InspectHoverLayout();return;}
        if(surfaceProbe){await InspectSurfaceCycle();return;}
        if(!FrontendBackend&&dataSuffix==""){try{StartupRegistration.Apply(Prefs.AutoStart);}catch(Exception ex){Log("startup",ex);}}
        if(!FrontendBackend)_tray=new TrayIcon(SettingsWindow,right=>{if(right)SettingsWindow.ShowQuickMenu();else OpenSettings();});
        var dispatcher=DispatcherQueue.GetForCurrentThread();if(!FrontendBackend)_outsideClicks=new((x,y)=>{var open=new SurfaceWindow[]{WeatherWindow,MusicWindow,SettingsWindow}.Where(w=>w.IsOpen&&!w.KeepOpen).Select(w=>(Window:w,Revision:w.VisibilityRevision)).ToArray();dispatcher.TryEnqueue(()=>{foreach(var entry in open){var w=entry.Window;if(!w.CanDismiss(entry.Revision)||Native.ContainsPoint(w,x,y,true))continue;var owner=w==MusicWindow?MusicCapsule:w==WeatherWindow?WeatherCapsule:null;if(owner!=null&&Native.ContainsPoint(owner,x,y))continue;Trace("outside-click",$"Dismiss {w.Title}");w.HideSurface();}});});Media.Changed+=()=>dispatcher.TryEnqueue(()=>_lastMedia=default);
        _frontendApi=new(this,dispatcher);if(!FrontendBackend){if(!Environment.GetCommandLineArgs().Contains("--inspect-no-reservation"))Reservation=new();if(Reservation!=null)Reservation.Changed+=()=>dispatcher.TryEnqueue(()=>{Layout();_frontendApi?.Notify("windows");});Occupancy.Changed+=()=>dispatcher.TryEnqueue(Layout);}ApplyTheme(true);WeatherWindow.Render();MusicWindow.RenderTrack(null);WeatherCapsule.Refresh();
        if(Environment.GetCommandLineArgs().Contains("--inspect-spectrum-ipc")){InspectSpectrumIpc(dispatcher);return;}
        _timer=DispatcherQueue.GetForCurrentThread().CreateTimer();_timer.Interval=TimeSpan.FromMilliseconds(16);_timer.Tick+=Tick;_timer.Start();
        _watcher=new TaskbarWatcher(()=>{if(_layoutPending)return;_layoutPending=true;dispatcher.TryEnqueue(()=>{try{Layout();}finally{_layoutPending=false;}});},RestoreCapsuleLayers); if(Prefs.City==null){if(!FrontendBackend)OpenWeather();}else _=RefreshWeather();
        if(!FrontendBackend&&Environment.GetCommandLineArgs().Contains("--preview")){if(Environment.GetCommandLineArgs().Contains("--inspect-weather"))OpenWeather();else if(Environment.GetCommandLineArgs().Contains("--inspect-music"))OpenMusic();else OpenSettings();}
        if(Environment.GetCommandLineArgs().Contains("--inspect-release"))_=InspectRelease(dispatcher);
        if(Environment.GetCommandLineArgs().Contains("--inspect-glass")){SettingsWindow.KeepOpen=true;OpenSettings();}
        if(Environment.GetCommandLineArgs().Contains("--inspect-weather-polish"))InitializeVisualPolishPreview(false);
        if(Environment.GetCommandLineArgs().Contains("--inspect-media-polish"))InitializeVisualPolishPreview(true);
        var exportIndex=Array.IndexOf(Environment.GetCommandLineArgs(),"--inspect-export-diagnostics");
        if(exportIndex>=0&&exportIndex+1<Environment.GetCommandLineArgs().Length)_=InspectDiagnostics(Environment.GetCommandLineArgs()[exportIndex+1],dispatcher);
    }
    private async Task InspectDiagnostics(string path,DispatcherQueue dispatcher){try{await Diagnostics.Export(path,Diagnostics.Capture(this));}catch(Exception ex){Log("diagnostic-export",ex);}finally{dispatcher.TryEnqueue(Quit);}}
    private async Task InspectRelease(DispatcherQueue dispatcher){await Task.Delay(4500).ConfigureAwait(false);dispatcher.TryEnqueue(Quit);}
    private async Task InspectHoverLayout()
    {
        // A temporary, self-closing render probe; it never attaches to Explorer.
        Prefs.City=new("武清区",39.38,117.05);Prefs.Theme="Light";Prefs.GlassOpacity=.5;
        Forecast=JsonSerializer.Deserialize<Forecast>("""{"Temperature":9,"FeelsLike":6,"Code":-1,"Rain":0,"Hours":[],"Days":[],"Aqi":163,"AqiStandard":"CN AQI","AqiCategory":"中度污染","ConditionText":"霾","Updated":"2026-11-15T12:00:00"}""")!;
        ApplyTheme(true);WeatherHover.Render();
        var scale=Native.ReadTaskbar()?.Scale??1;
        Native.Place(WeatherHover,500,350,(int)Math.Round(296*scale),(int)Math.Round(192*scale));
        WeatherHover.ShowSurface(false,false);await Task.Delay(500);
        var root=WeatherHover.Root;var content=(Microsoft.UI.Xaml.Controls.StackPanel)WeatherHover.Body.Children[0];
        object Bounds(FrameworkElement e){var p=e.TransformToVisual(root).TransformPoint(new(0,0));var slot=Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(e);return new{left=p.X,right=p.X+e.ActualWidth,width=e.ActualWidth,desired=e.DesiredSize.Width,centre=p.X+e.ActualWidth/2,slotLeft=slot.X,slotWidth=slot.Width,alignment=e.HorizontalAlignment.ToString()};}
        IEnumerable<FrameworkElement> Descendants(DependencyObject parent){for(var i=0;i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);i++){var child=Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent,i);if(child is FrameworkElement element)yield return element;foreach(var nested in Descendants(child))yield return nested;}}
        var report=new{root=root.ActualWidth,body=Bounds(WeatherHover.Body),content=Bounds(content),top=Bounds((FrameworkElement)content.Children[0]),metrics=Bounds((FrameworkElement)content.Children[1]),text=Descendants(content).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Select(t=>new{t.Text,bounds=Bounds(t)}).ToArray()};
        File.WriteAllText(Path.Combine(Preferences.DataPath,"hover-layout.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        await Task.Delay(30000);Quit();
    }
    private async Task InspectSurfaceCycle()
    {
        // Isolated compositor regression probe: no tray, shell hooks, native
        // reservation, media session or network service can start on this path.
        Trace("surface-probe","begin");ApplyTheme(true);
        for(var i=0;i<12;i++){
            foreach(var capsule in new[]{WeatherCapsule,MusicCapsule}){
                capsule.Root.RequestedTheme=i%4<2?ElementTheme.Light:ElementTheme.Dark;
                capsule.SetExtent(i%2==0?180:64,1.5);
                Native.Place(capsule,capsule==WeatherCapsule?400:740,400,i%2==0?300:126,89);
                Trace("surface-probe",$"show {i} {capsule.Title}");capsule.ShowSurface(false,false);
            }
            await Task.Delay(150);
            foreach(var capsule in new[]{WeatherCapsule,MusicCapsule}){
                capsule.HideImmediately();var revision=capsule.VisibilityRevision;capsule.HideImmediately();
                if(capsule.VisibilityRevision!=revision)throw new InvalidOperationException("Repeated hidden state changed surface lifetime");
            }
            await Task.Delay(150);
        }
        Trace("surface-probe","complete: 24 reveals, light/dark, full/lite, idempotent hide");Quit();
    }
    private DateTime _lastWarningRefresh;private bool _warningsBusy;
    private async Task RefreshWarnings()
    {
        if(_warningsBusy||Prefs.City is not {} city)return;_warningsBusy=true;_lastWarningRefresh=DateTime.UtcNow;var generation=_weatherGeneration;
        try{var alerts=await WeatherProxyClient.Alerts(city,Prefs.WeatherProxyUrl,CancellationToken.None);if(!Exiting&&generation==_weatherGeneration&&Prefs.WeatherProvider=="Auto"&&Forecast?.Source=="和风天气"){Forecast=Forecast with{Alerts=alerts};WeatherWindow.Render();_frontendApi?.Notify("weather");}}
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidDataException){Trace("weather-alerts","refresh unavailable; keeping unexpired alerts");}
        finally{_warningsBusy=false;}
    }
    internal void ReloadWeatherProvider(){_weatherGeneration++;_weatherSchedule.Invalidate();_=RefreshWeather();}
    private async void Tick(DispatcherQueueTimer timer,object args)
    {
        try{
            GlassDiagnostics.Report();
            if(Environment.TickCount64-_lastRendererCheck>=250){_lastRendererCheck=Environment.TickCount64;CheckRenderer();CheckPluginMenus();}
            RestoreCapsuleLayers();
            WeatherHover.CheckPointer();MusicHover.CheckPointer();
            var now=DateTime.UtcNow;if(!FrontendBackend&&Prefs.AutoLocateCity&&!LocationBusy&&(now-_lastLocation).TotalMinutes>=15)_=UseSystemLocation();if((now-_lastLayout).TotalMilliseconds>500){_lastLayout=now;ExpireWeatherAlerts();Layout();}
            if((now-_lastTheme).TotalSeconds>2){_lastTheme=now;ApplyTheme();}
            if((now-_lastAudio).TotalSeconds>5){_lastAudio=now;if(Track?.Playing==true&&Prefs.Spectrum)Spectrum.EnsureDevice();else Spectrum.Stop();}
            MusicCapsule.UpdateSpectrum();MusicWindow.UpdateSpectrum();SettingsWindow.UpdatePreview();if(MusicWindow.IsOpen)MusicWindow.UpdatePosition();
            if(!_mediaBusy&&(now-_lastMedia).TotalSeconds>.9){_mediaBusy=true;_lastMedia=now;try{await ReadMedia();}catch(Exception ex){Log("media",ex);}finally{_mediaBusy=false;}}
            if(Prefs.City!=null&&!_weatherBusy&&_weatherSchedule.IsDue(DateTimeOffset.UtcNow))_=RefreshWeather();
            if(!_weatherBusy&&!_warningsBusy&&Prefs.WeatherProvider=="Auto"&&Forecast?.Source=="和风天气"&&(now-_lastWarningRefresh).TotalMinutes>=5)_=RefreshWarnings();
        }catch(Exception ex){Log("tick",ex);}
    }
    private async Task ReadMedia()
    {
        var current=await Media.Read();if(Exiting)return;
        if(current==null){
            if(_missingSince==default)_missingSince=DateTime.UtcNow;
            if((DateTime.UtcNow-_missingSince).TotalSeconds<1.3)return;
            if(Track!=null){Track=null;Lines=[];MusicHover.Cancel();LyricsStatus="播放一首歌，歌词会显示在这里";_lyricsCts?.Cancel();MusicWindow.RenderTrack(null);MusicCapsule.SetPresent(false);Spectrum.Stop();_frontendApi?.Notify("media");}
            return;
        }
        _missingSince=default;var changed=Track?.Key!=current.Key;var artworkChanged=!ReferenceEquals(Track?.Cover,current.Cover);var playbackChanged=Track?.Playing!=current.Playing;var notify=changed||artworkChanged||playbackChanged||Track?.Controls!=current.Controls||Track?.ShuffleEnabled!=current.ShuffleEnabled||Track?.RepeatMode!=current.RepeatMode||!current.Playing&&Math.Abs((Track?.Position??0)-current.Position)>.5;Track=current;if(notify)_frontendApi?.Notify("media");
        if(changed){MusicWindow.RenderTrack(current);_ = LoadLyrics();}else if(artworkChanged)MusicWindow.RenderArtwork(current);if(notify){MusicCapsule.Refresh();MusicWindow.UpdatePlayback();if(MusicHover.IsOpen)MusicHover.Render();SettingsWindow.ApplyPreview();}
        if(playbackChanged){MusicWindow.UpdatePlayback();if(current.Playing&&Prefs.Spectrum)Spectrum.EnsureDevice();else Spectrum.Stop();}
        MusicCapsule.SetPresent(Prefs.ShowMusic&&!ExternalRendererActive&&!FrontendBackend);MusicWindow.UpdatePosition();
    }
    internal async Task LoadLyrics()
    {
        _lyricsCts?.Cancel();_lyricsCts?.Dispose();_lyricsCts=new();var token=_lyricsCts.Token;var track=Track;
        Lines=[];LyricsStatus=Prefs.AutoLyrics?"正在匹配歌词…":"自动匹配已关闭，可导入 LRC";MusicWindow.RenderLyrics();_frontendApi?.Notify("lyrics");if(track==null)return;
        try{var result=await Lyrics.Get(track,token,Prefs.AutoLyrics);if(token.IsCancellationRequested||Track?.Key!=track.Key)return;Lines=result.lines;LyricsStatus=result.status;MusicWindow.RenderLyrics();_frontendApi?.Notify("lyrics");}
        catch(OperationCanceledException){}catch(Exception ex){Log("lyrics",ex);if(!Exiting&&!token.IsCancellationRequested&&Track?.Key==track.Key){LyricsStatus="歌词暂时无法加载";MusicWindow.RenderLyrics();_frontendApi?.Notify("lyrics");}}
    }
    internal async Task RefreshWeather()
    {
        var city=Prefs.City;if(city==null||Exiting||_weatherBusy&&_activeWeatherGeneration==_weatherGeneration)return;
        var generation=_weatherGeneration;_activeWeatherGeneration=generation;_weatherBusy=true;
        if(Forecast==null)Forecast=Weather.Cached(city);WeatherStatus="正在更新…";WeatherWindow.UpdateStatus();_frontendApi?.Notify("weather");
        try
        {
            var request=await _weatherRequests.Run($"{city.Latitude:R}/{city.Longitude:R}",ct=>Weather.Get(city,ct));
            if(!Exiting&&generation==_weatherGeneration&&request.Version==_weatherRequests.Version)
            {var incoming=request.Value;if(incoming.Source=="和风天气"&&incoming.ProviderNote?.Any(n=>n.StartsWith("alerts:"))==true)incoming=incoming with{Alerts=WeatherAlerts.Current(Forecast?.Alerts,DateTimeOffset.UtcNow).ToList()};Forecast=incoming;_lastWarningRefresh=DateTime.UtcNow;WeatherStatus=$"上次更新：{Forecast.Updated:HH:mm}";_weatherSchedule.Success(DateTimeOffset.UtcNow,Prefs.WeatherRefreshMinutes);}
        }
        catch(OperationCanceledException) when(Exiting||generation!=_weatherGeneration){}
        catch(Exception ex)
        {
            Log("weather",ex);if(!Exiting&&generation==_weatherGeneration){_weatherSchedule.Failure(DateTimeOffset.UtcNow);WeatherStatus=Forecast==null?"天气暂时无法更新，稍后自动重试":$"更新失败 · 保留 {Forecast.Updated:MM-dd HH:mm} 的天气";}
        }
        finally{if(generation==_weatherGeneration){_weatherBusy=false;if(!Exiting){WeatherWindow.Render();WeatherCapsule.Refresh();SettingsWindow.ApplyPreview();if(WeatherHover.IsOpen)WeatherHover.Render();Layout();if(WeatherWindow.IsOpen)PositionFlyout(WeatherWindow,480,WeatherWindow.PreferredHeight());_frontendApi?.Notify("weather");}}}
    }
    internal void SelectCity(City city,bool automatic=false){if(automatic)LocatedCity=city;if(!automatic){++_locationRequest;_locationCts?.Cancel();LocationBusy=false;LocationStatus="";}SavedCities.Select(Prefs,city,automatic);_weatherGeneration++;_weatherSchedule.Invalidate();Forecast=Weather.Cached(city);PersistSettings();WeatherWindow.Render();WeatherCapsule.Refresh();if(WeatherHover.IsOpen)WeatherHover.Render();_=RefreshWeather();}
    internal string Temperature(double value){var rounded=Math.Round(Prefs.Fahrenheit?value*1.8+32:value);return $"{(rounded==0?0:rounded):0}°";}
    internal double CurrentLyricsOffset=>TrackLyricTiming.Get(Prefs.LyricsOffsets,Track?.Key);
    internal bool SetLyricsOffset(double seconds,string? expectedKey)
    {
        if(Track==null||Track.Key!=expectedKey||!TrackLyricTiming.Set(Prefs.LyricsOffsets,expectedKey,seconds))return false;
        PersistSettings();MusicWindow.RefreshLyricsTiming();_frontendApi?.Notify("lyrics");_frontendApi?.Notify("settings");return true;
    }
    internal bool LocationBusy;internal string LocationStatus="";private DateTime _lastLocation;private int _locationRequest;private CancellationTokenSource? _locationCts;
    internal void DisableAutoLocation(){++_locationRequest;_locationCts?.Cancel();LocationBusy=false;Prefs.AutoLocateCity=false;LocationStatus="";Save();WeatherWindow.Render();}
    internal async Task<City?> UseSystemLocation(CancellationToken cancellation=default)
    {
        _locationCts?.Cancel();var source=CancellationTokenSource.CreateLinkedTokenSource(cancellation);_locationCts=source;
        LocationBusy=true;var request=++_locationRequest;_lastLocation=DateTime.UtcNow;
        SavedCities.BeginAutomatic(Prefs);LocationStatus="正在读取系统位置…";Save();WeatherWindow.Render();
        try{var city=await Weather.Locate(source.Token);if(!Exiting&&request==_locationRequest&&Prefs.AutoLocateCity&&!source.IsCancellationRequested){LocationStatus="";SelectCity(city,true);return city;}}
        catch(OperationCanceledException) when(source.IsCancellationRequested){if(request==_locationRequest)LocationStatus="";}
        catch(Exception ex){if(request==_locationRequest){LocationStatus=ex is InvalidOperationException?ex.Message:"系统暂时无法定位，可选择已保存城市。";Log("location",ex);}}
        finally{if(request==_locationRequest){LocationBusy=false;if(!Exiting){WeatherWindow.Render();_frontendApi?.Notify("weather");}}if(ReferenceEquals(_locationCts,source))_locationCts=null;source.Dispose();}
        return null;
    }
    internal void RemoveSavedCity(City city)
    {
        Prefs.Favorites.RemoveAll(c=>SavedCities.Same(c,city));
        if(SavedCities.Same(Prefs.City,city)&&!Prefs.AutoLocateCity)
        {
            Prefs.City=null;Forecast=null;++_weatherGeneration;_weatherBusy=false;_weatherSchedule.Invalidate();
            if(Prefs.Favorites.FirstOrDefault() is {} next){SelectCity(next);return;}
            WeatherStatus="选择一个城市，开始查看天气";
        }
        Save();WeatherWindow.Render();
    }
    internal void Save(bool persist=true){try{if(persist)Prefs.Save();}catch(Exception ex){Log("settings",ex);}RefreshAccent();ApplyTheme(true);foreach(var window in new SurfaceWindow[]{WeatherWindow,MusicWindow,SettingsWindow,WeatherCapsule,MusicCapsule,WeatherHover,MusicHover})window.ApplyFont();WeatherCapsule.Refresh();MusicCapsule.Refresh();MusicWindow.ApplyGlow();WeatherWindow.ApplyEffects();SettingsWindow.ApplyPreview();if(WeatherHover.IsOpen)WeatherHover.Render();if(MusicHover.IsOpen)MusicHover.Render();if(Prefs.Spectrum&&Track?.Playing==true)Spectrum.EnsureDevice();else Spectrum.Stop();Layout();_frontendApi?.Notify("settings");}
    internal void PersistSettings(){try{Prefs.Save();}catch(Exception ex){Log("settings",ex);}_frontendApi?.Notify("settings");}
    internal void PreviewGlass(){GlassDiagnostics.Preview();foreach(var window in new SurfaceWindow[]{WeatherWindow,MusicWindow,SettingsWindow,WeatherCapsule,MusicCapsule,WeatherHover,MusicHover})(window.SystemBackdrop as GlassBackdrop)?.Refresh();}
    internal void SetAccent(string id){id=AccentPalettes.Normalize(id);if(Prefs.AccentColor==id)return;Prefs.AccentColor=id;PersistSettings();RefreshAccent();}
    internal void RefreshAccent(){AccentTheme.Apply(Prefs.AccentColor,Prefs.Animations);SettingsWindow.UpdateAccentSelection();_frontendApi?.Notify("appearance");}
    internal void PreviewSettings(){PreviewGlass();WeatherCapsule.Refresh();MusicCapsule.Refresh();MusicWindow.ApplyGlow();WeatherWindow.ApplyEffects();SettingsWindow.ApplyPreview();if(WeatherHover.IsOpen)WeatherHover.Render();if(MusicHover.IsOpen)MusicHover.Render();Layout();}
    private void ApplyTheme(bool force=false)
    {
        using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var appLight=(int?)key?.GetValue("AppsUseLightTheme")!=0;var taskLight=(int?)key?.GetValue("SystemUsesLightTheme")!=0;
        var signature=$"{Prefs.Theme}/{appLight}/{taskLight}/{Prefs.Glow}/{Prefs.CoverGlow}";if(!force&&signature==_theme)return;_theme=signature;
        foreach(var win in new SurfaceWindow[]{WeatherWindow,MusicWindow,SettingsWindow,WeatherCapsule,MusicCapsule,WeatherHover,MusicHover}){
            var light=Prefs.Theme=="Light"||Prefs.Theme=="System"&&(win is CapsuleWindow?taskLight:appLight);
            win.Root.RequestedTheme=light?ElementTheme.Light:ElementTheme.Dark;(win.SystemBackdrop as GlassBackdrop)?.Refresh();
        }
        MusicWindow.ApplyGlow();MusicCapsule.ApplyGlow();_frontendApi?.Notify("appearance");
    }
    private void ExpireWeatherAlerts()
    {
        if(Forecast?.Alerts is not {Count:>0} alerts)return;
        var active=WeatherAlerts.Current(alerts,DateTimeOffset.UtcNow);
        if(active.SequenceEqual(alerts))return;
        Forecast=Forecast with{Alerts=active.ToList()};WeatherWindow.Render();_frontendApi?.Notify("weather");
    }
    private readonly TaskbarSlotContinuity _slotContinuity=new();
    private string? _lastLayoutTrace;
    private void Layout()
    {
        if(FrontendBackend||Exiting)return;var a=Native.ReadTaskbar(Occupancy);if(a==null){SetSlots(new(new(null,"hidden"),new(null,"hidden"),"unavailable"));if(RendererLeases.Current is {} missing)RendererWindows.Hide(missing);return;}
        var reservation=Reservation?.Snapshot;var bar=new ScreenRect(a.Bar.Left,a.Bar.Top,a.Bar.Right,a.Bar.Bottom);
        var hasCapsules=Prefs.ShowWeather||Prefs.ShowMusic&&Track!=null;
        var slot=_slotContinuity.Resolve(bar,a.Scale,a.ExplorerPid,hasCapsules&&reservation?.ExplorerPid==a.ExplorerPid&&reservation.Reserved?reservation.Slot:null,hasCapsules?reservation?.Status??"unavailable":"disabled",a.AutoHide,Environment.TickCount64);
        if(a.AutoHide&&!a.Visible&&Reservation!=null){
            var warmSizes=RendererLeases.Current?.Sizes;var warmScale=a.Scale;
            var warmWeather=Prefs.ShowWeather?(int)Math.Ceiling((warmSizes?.WeatherFullDip??WeatherCapsule.PreferredWeatherWidth(false))*warmScale):0;
            var warmMusic=Track!=null&&Prefs.ShowMusic?(int)Math.Ceiling((warmSizes?.MusicFullDip??Prefs.MusicWidth)*warmScale):0;
            var warmGap=(int)Math.Ceiling((warmSizes?.GapDip??Prefs.Gap)*warmScale);
            if(ActivePlacement?.Lite==true){warmWeather=warmWeather>0?(int)Math.Ceiling((warmSizes?.WeatherLiteDip??WeatherCapsule.PreferredWeatherWidth(true))*warmScale):0;warmMusic=warmMusic>0?(int)Math.Ceiling((warmSizes?.MusicLiteDip??48)*warmScale):0;}
            Reservation.SetCapsules(WinRT.Interop.WindowNative.GetWindowHandle(MusicCapsule),WinRT.Interop.WindowNative.GetWindowHandle(WeatherCapsule),Environment.ProcessId);
            if(RendererLeases.Current is {Active:true} external)Reservation.SetCapsules((nint)external.MusicHwnd,(nint)external.WeatherHwnd,external.Pid);
            Reservation.SetWidth((warmWeather+warmMusic+(warmWeather>0&&warmMusic>0?warmGap:0)+(warmWeather+warmMusic>0?2*warmGap:0))/warmScale);
        }
        var hidden=!a.Visible||Prefs.HideInFullscreen&&Native.ForegroundCoversTaskbar(a);
        if(hidden){WeatherHover.Cancel();MusicHover.Cancel();if(a.AutoHide&&!a.Visible){_motion.Forget(WeatherCapsule);_motion.Forget(MusicCapsule);WeatherCapsule.HideImmediately();MusicCapsule.HideImmediately();}else{WeatherCapsule.HideSurface();MusicCapsule.HideSurface();}SetSlots(new(new(null,"hidden"),new(null,"hidden"),"hidden"));if(RendererLeases.Current is {} invisible)RendererWindows.Hide(invisible);return;}
        var sizes=RendererLeases.Current?.Sizes;var scale=a.Scale;int ww=Prefs.ShowWeather?(int)Math.Ceiling((sizes?.WeatherFullDip??WeatherCapsule.PreferredWeatherWidth(false))*scale):0,mw=(Track!=null||(!ExternalRendererActive&&MusicCapsule.Fading))&&Prefs.ShowMusic?(int)Math.Ceiling((sizes?.MusicFullDip??Prefs.MusicWidth)*scale):0,gap=(int)Math.Ceiling((sizes?.GapDip??Prefs.Gap)*scale);
        var plan=_reservedLayout.Calculate(bar,a.GeometryVerified?a.Left:a.Right,a.Right,slot,scale,ww,mw,gap,reservation?.ExplorerPid==a.ExplorerPid&&reservation.Compatible,sizes?.WeatherLiteDip??WeatherCapsule.PreferredWeatherWidth(true),sizes?.MusicLiteDip??48,a.GeometryVerified,a.ExplorerPid);
        var layoutTrace=$"geometry={a.GeometryStatus}; explorer={a.ExplorerPid}; bridge={reservation?.Status}; lite={plan.Capsules.Lite}; elevated={plan.Elevated}; right={plan.Right}";
        if(layoutTrace!=_lastLayoutTrace){_lastLayoutTrace=layoutTrace;Trace("taskbar-layout",layoutTrace);}
        Reservation?.SetCapsules(WinRT.Interop.WindowNative.GetWindowHandle(MusicCapsule),WinRT.Interop.WindowNative.GetWindowHandle(WeatherCapsule),Environment.ProcessId);
        if(RendererLeases.Current is {Active:true} renderer)Reservation?.SetCapsules((nint)renderer.MusicHwnd,(nint)renderer.WeatherHwnd,renderer.Pid);
        Reservation?.SetWidth(plan.ReserveDip);
        if(a.AutoHide&&plan.Elevated&&!WeatherCapsule.IsOpen&&!MusicCapsule.IsOpen&&reservation?.Status is "starting" or "waiting-for-release" or "connecting" or "ready")return;
        var layout=plan.Capsules;var height=Math.Min((int)Math.Ceiling((sizes?.HeightDip??34)*scale),a.Bar.Height-4);
        if(ExternalRendererActive&&plan.Elevated){RestoreBuiltinRenderer("reservation_lost");return;}
        // Keep both Lite capsules visible above the edge during unavailable / reconnecting native layout.
        // Never fall back to painting over application buttons or dropping music due to crowding.
        if(plan.Elevated){layout=layout with{WeatherWidth=ww>0?(int)Math.Ceiling(WeatherCapsule.PreferredWeatherWidth(true)*scale):0,MusicWidth=mw>0?(int)Math.Ceiling(48*scale):0,Lite=true};bar=new(bar.Left,bar.Top-height-gap,bar.Right,bar.Top-gap);}
        var slots=SlotPlanner.Calculate(bar,plan.Right,gap,height,layout with{RequiresReservation=false},true,plan.Elevated?0:(int)Math.Round(CapsuleGeometry.TaskbarVerticalOffset*scale));SetSlots(slots);ActivePlacement=layout;
        if(ExternalRendererActive){var external=RendererLeases.Current!;if(slots.MusicSlot?.Rect==null)Native.ShowWindow((nint)external.MusicHwnd,0);if(slots.WeatherSlot?.Rect==null)Native.ShowWindow((nint)external.WeatherHwnd,0);return;}
        WeatherCapsule.SetLite(layout.Lite);MusicCapsule.SetLite(layout.Lite);
        if(slots.WeatherSlot?.Rect is {} w){PlaceCapsule(WeatherCapsule,w,scale,a.AutoHide);}else {WeatherHover.Cancel();WeatherCapsule.HideSurface();}
        if(slots.MusicSlot?.Rect is {} m){PlaceCapsule(MusicCapsule,m,scale,a.AutoHide);}else if(!MusicCapsule.Fading){MusicHover.Cancel();MusicCapsule.HideSurface();}
        if(WeatherWindow.IsOpen)PositionFlyout(WeatherWindow,480,WeatherWindow.PreferredHeight());if(MusicWindow.IsOpen)PositionFlyout(MusicWindow,420,650);
    }
    private void PlaceCapsule(CapsuleWindow window,ScreenRect rect,double scale,bool followTaskbar=false)
    {
        var gutter=(int)Math.Round(CapsuleGeometry.Gutter*scale);
        window.SetExtent((rect.Right-rect.Left)/scale,scale);
        _motion.Target(window,rect.Left-gutter,rect.Top-gutter,rect.Right-rect.Left+gutter*2,(int)Math.Round(CapsuleGeometry.OuterHeight*scale),Prefs.Animations,followTaskbar);
        if(!window.IsOpen)window.ShowSurface(false,!followTaskbar);
        if(Native.RestoreAboveTaskbar(window))Trace("taskbar-layer",$"Restored {window.Title} above taskbar without activation");
    }
    private void RestoreCapsuleLayers()
    {
        if(FrontendBackend||Exiting)return;
        if(ExternalRendererActive){var l=RendererLeases.Current!;Native.RestoreAboveTaskbar((nint)l.MusicHwnd);Native.RestoreAboveTaskbar((nint)l.WeatherHwnd);return;}
        if(WeatherCapsule.IsOpen)Native.RestoreAboveTaskbar(WeatherCapsule);
        if(MusicCapsule.IsOpen)Native.RestoreAboveTaskbar(MusicCapsule);
    }
    internal async Task ControlMedia(Func<Task> command)
    {
        try{await command();MediaStatus="";_lastMedia=default;}
        catch(Exception ex){MediaStatus=ex is InvalidOperationException?ex.Message:"播放器暂时没有响应";Log("media-control",ex);}
        MusicWindow.UpdatePlayback();
    }
    internal Task TogglePlayback()=>ControlMedia(Media.Toggle);
    internal Task NextTrack()=>ControlMedia(Media.Next);
    internal Task PreviousTrack()=>ControlMedia(Media.Previous);
    internal Task SeekPlayback(double seconds)=>ControlMedia(()=>Media.Seek(seconds));
    internal void PositionFlyout(SurfaceWindow window,int width,double height)
    {
        width+=PopupShadow.Gutter*2;height+=PopupShadow.Gutter*2;var a=Native.ReadTaskbar();if(a==null){window.AppWindow.Resize(new(width,(int)Math.Ceiling(height)));return;}
        var owner=window==MusicWindow?MusicCapsule:WeatherCapsule;
        var s=a.Scale;var margin=(int)Math.Ceiling(10*s);
        var anchor=new Native.Rect{Left=a.Right,Right=a.Right,Top=a.Bar.Top};
        if(owner.IsOpen&&Native.GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(owner),out var rect)){anchor=rect;anchor.Top+=(int)Math.Round(CapsuleGeometry.Gutter*s);}
        var targetSlot=window==MusicWindow?ActiveSlots?.MusicSlot?.Rect:ActiveSlots?.WeatherSlot?.Rect;
        if(owner.IsOpen&&targetSlot is {} target)anchor=new Native.Rect{Left=target.Left,Right=target.Right,Top=target.Top,Bottom=target.Bottom};
        var w=(int)Math.Min(width*s,a.Bar.Width-margin*2);var h=(int)Math.Ceiling(Math.Min(height*s,Math.Max(100,anchor.Top-margin*2)));
        var x=Math.Clamp(anchor.Left+(anchor.Width-w)/2,a.Bar.Left+margin,Math.Max(a.Bar.Left+margin,a.Bar.Right-w-margin));
        var y=Math.Max(margin,anchor.Top-h-margin+(int)Math.Round(PopupShadow.Gutter*s));_motion.Target(window,x,y,w,h,Prefs.Animations&&window.IsOpen&&!(window==WeatherWindow&&WeatherWindow.IsRevealing));
    }
    internal void OpenWeather(){WeatherHover.Cancel();MusicHover.Cancel();SettingsWindow.HideSurface();MusicWindow.HideSurface();PositionFlyout(WeatherWindow,480,WeatherWindow.PreferredHeight());WeatherWindow.ShowSurface();}
    internal void OpenMusic(){WeatherHover.Cancel();MusicHover.Cancel();SettingsWindow.HideSurface();WeatherWindow.HideSurface();PositionFlyout(MusicWindow,420,650);MusicWindow.ShowSurface();}
    internal void OpenSettings(){WeatherHover.Cancel();MusicHover.Cancel();WeatherWindow.HideSurface();MusicWindow.HideSurface();SettingsWindow.PrepareToShow();PositionFlyout(SettingsWindow,560,650);SettingsWindow.ShowSurface();}
    internal async void Quit(){if(Exiting)return;var dispatcher=SettingsWindow.DispatcherQueue;SettingsWindow.FlushPendingSettings();Exiting=true;Trace("shutdown","begin");if(RendererLeases.Release("backend_shutdown") is {} external)RendererWindows.Hide(external);_locationCts?.Cancel();_timer?.Stop();_watcher?.Dispose();try{if(Reservation!=null)await Reservation.StopAsync().ConfigureAwait(false);}catch(Exception ex){Log("reservation shutdown",ex);}dispatcher.TryEnqueue(FinishQuit);}
    private void FinishQuit(){Trace("shutdown","native owner released; disposing services");Occupancy.Dispose();_frontendApi?.Dispose();_motion.Dispose();_weatherRequests.Dispose();_lyricsCts?.Cancel();_tray?.Dispose();_outsideClicks?.Dispose();Spectrum.Dispose();Media.Dispose();_mutex?.Dispose();Trace("shutdown","exit");Exit();Environment.Exit(0);}
    internal static void Log(string category,Exception ex)=>Trace(category,ex.ToString());
    internal static void Trace(string category,string message){try{Directory.CreateDirectory(Preferences.DataPath);var file=Path.Combine(Preferences.DataPath,"diagnostics.log");if(File.Exists(file)&&new FileInfo(file).Length>512000)File.Move(file,file+".old",true);File.AppendAllText(file,$"{DateTime.Now:O} [{category}] {message}\n");}catch{}}
    private static void RunTests(){
        var results=new List<string>();void Check(bool ok,string name){results.Add($"{(ok?"PASS":"FAIL")} {name}");}
        var lines=Lrc.Parse("[00:01.50]one\n[00:03][00:04.123]two\n[offset:100]");Check(lines.Count==3&&Math.Abs(lines[0].Seconds-1.4)<.001,"LRC timestamps and offset");Check(Lrc.Active(lines,.2)==-1&&Lrc.Active(lines,3.95)==1,"lyric active-line binary search");
        var p=new PlacementPolicy().Calculate(300,1000,104,238,8,1);Check(!p.Lite&&p.MusicWidth==238,"full taskbar layout");var p2=new PlacementPolicy().Calculate(850,1000,104,238,8,1);Check(p2.Lite&&p2.MusicWidth==48&&p2.WeatherWidth==64,"crowding retains both Lite capsules");
        Directory.CreateDirectory(Preferences.DataPath);File.WriteAllLines(Path.Combine(Preferences.DataPath,"self-test.txt"),results);Environment.ExitCode=results.Any(x=>x.StartsWith("FAIL"))?1:0;
    }
}





