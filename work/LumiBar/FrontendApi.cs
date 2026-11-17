using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;

namespace LumiBar;

/// <summary>The renderer owns its windows; the backend only supplies state, actions and placement hints.</summary>
internal sealed class FrontendApi : IDisposable
{
    private readonly App _app;
    private readonly DispatcherQueue _dispatcher;
    private readonly FrontendPipeServer _server;
    private readonly string _descriptor;
    private readonly TaskbarOccupancy _occupancy;
    private readonly PlacementPolicy _placement=new();
    private readonly ChangeFeed _events=new();
    private sealed record SpectrumPlayback(bool Playing,bool Enabled);
    private SpectrumPlayback _spectrumPlayback=new(false,false);
    private void CaptureSpectrumPlayback()=>Volatile.Write(ref _spectrumPlayback,new(_app.Track?.Playing==true,_app.Prefs.Spectrum));
    internal void Notify(string topic){if(topic is "media" or "settings")CaptureSpectrumPlayback();_events.Publish(topic);}
    internal FrontendApi(App app,DispatcherQueue dispatcher)
    {
        _app=app;_dispatcher=dispatcher;_occupancy=app.Occupancy;_occupancy.Changed+=()=>Notify("windows");CaptureSpectrumPlayback();
        using var identity=WindowsIdentity.GetCurrent();
        var owner=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.User!.Value)))[..12];
        var pipe=InstanceIdentity.PipeName(owner,Process.GetCurrentProcess().SessionId,App.FrontendBackend,Environment.GetEnvironmentVariable("LUMIBAR_DATA_DIR"));
        _server=new(pipe,Dispatch);_server.Start();
        _descriptor=Path.Combine(Preferences.DataPath,"frontend-endpoint.json");
        AtomicStore.Write(_descriptor,JsonSerializer.Serialize(new{protocolVersion=1,pipeName=pipe,pid=Environment.ProcessId,mode=App.FrontendBackend?"preview":"builtin",startedAt=DateTimeOffset.UtcNow},FrontendPipeServer.Json));
    }
    private async Task<object?> Dispatch(FrontendPeer peer,string method,JsonElement args,CancellationToken ct)
    {
        if(method=="spectrum.get"){
            ct.ThrowIfCancellationRequested();var playback=Volatile.Read(ref _spectrumPlayback);
            return new{capturedAt=DateTimeOffset.UtcNow,playing=playback.Playing,source="system-output",status=_app.Spectrum.Status,bands=playback.Playing&&playback.Enabled?_app.Spectrum.Levels():new float[20]};
        }
        if(method=="fonts.list"){
            var query=args.ValueKind==JsonValueKind.Object&&args.TryGetProperty("query",out var q)?q.ValueKind==JsonValueKind.String?q.GetString()??"":throw new FrontendApiException("invalid_params","query 必须为文本。"):"";
            var offset=args.ValueKind==JsonValueKind.Object&&args.TryGetProperty("offset",out _)?Number(args,"offset"):0;
            var limit=args.ValueKind==JsonValueKind.Object&&args.TryGetProperty("limit",out _)?Number(args,"limit"):100;
            if(query.Length>128||offset<0||offset>100000||offset%1!=0||limit<1||limit>500||limit%1!=0)throw new FrontendApiException("invalid_params","字体查询范围有误。");
            var refresh=args.ValueKind==JsonValueKind.Object&&args.TryGetProperty("refresh",out _)?Boolean(args,"refresh"):false;
            var fonts=await _app.Fonts.Get(refresh,ct);var matches=fonts.Where(f=>string.IsNullOrWhiteSpace(query)||f.Family.Contains(query,StringComparison.OrdinalIgnoreCase)||f.Aliases.Any(n=>n.Contains(query,StringComparison.OrdinalIgnoreCase))).ToArray();
            return new{source="windows-directwrite",installedCount=fonts.Length,total=matches.Length,offset=(int)offset,hasMore=offset+limit<matches.Length,items=matches.Skip((int)offset).Take((int)limit).ToArray()};
        }
        if(method=="events.wait"){
            if(args.ValueKind!=JsonValueKind.Object||!args.TryGetProperty("afterVersion",out var v)||v.ValueKind!=JsonValueKind.Number||!v.TryGetInt64(out var version)||version<0)throw new FrontendApiException("invalid_params","afterVersion 必须为非负整数。");
            var timeout=args.TryGetProperty("timeoutMs",out _)?Number(args,"timeoutMs"):25000;
            return await _events.Wait(version,(int)Math.Clamp(timeout,50,25000),ct);
        }
        var completion=new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration=ct.Register(()=>completion.TrySetCanceled(ct));
        if(!_dispatcher.TryEnqueue(async()=>
        {
            try{ct.ThrowIfCancellationRequested();completion.TrySetResult(await Handle(peer,method,args,ct));}
            catch(Exception ex){completion.TrySetException(ex);}
        }))throw new FrontendApiException("shutting_down","后端正在退出。");
        return await completion.Task;
    }
    private static string Text(JsonElement args,string key,int max=128)
    {
        if(args.ValueKind!=JsonValueKind.Object||!args.TryGetProperty(key,out var v)||v.ValueKind!=JsonValueKind.String||v.GetString() is not {Length:>0} value||value.Length>max)throw new FrontendApiException("invalid_params",$"{key} 格式有误。");
        return value;
    }
    private static double Number(JsonElement args,string key)
    {
        if(args.ValueKind!=JsonValueKind.Object||!args.TryGetProperty(key,out var v)||v.ValueKind!=JsonValueKind.Number||!v.TryGetDouble(out var n)||!double.IsFinite(n))throw new FrontendApiException("invalid_params",$"{key} 必须为有效数值。");return n;
    }
    private static bool Boolean(JsonElement args,string key){if(args.ValueKind!=JsonValueKind.Object||!args.TryGetProperty(key,out var value)||value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new FrontendApiException("invalid_params",$"{key} 必须为布尔值。");return value.GetBoolean();}
    private async Task<object?> Handle(FrontendPeer peer,string method,JsonElement args,CancellationToken ct)
    {
        if(_app.Exiting)throw new FrontendApiException("shutting_down","后端正在退出。");
        switch(method)
        {
            case "hello":return Hello();
            case "state.get":return Snapshot();
            case "menus.list":return new{items=_app.PluginMenuViews()};
            case "menus.register":return _app.PluginMenuResponse(_app.RegisterPluginMenus(peer.ProcessId,PluginMenuArguments.Definition(args)),true);
            case "menus.update":
                var menuUpdate=_app.RequirePluginMenus(peer.ProcessId,Text(args,"registrationId"),Text(args,"registrationToken"));
                if(!args.TryGetProperty("definition",out var menuDefinition))throw new FrontendApiException("invalid_params","缺少 definition。");
                _app.PluginMenus.Update(menuUpdate,PluginMenuArguments.Definition(menuDefinition),Environment.TickCount64);_app.PluginMenusChanged();return _app.PluginMenuResponse(menuUpdate);
            case "menus.renew":
                var menuRenew=_app.RequirePluginMenus(peer.ProcessId,Text(args,"registrationId"),Text(args,"registrationToken"));_app.PluginMenus.Renew(menuRenew,Environment.TickCount64);return _app.PluginMenuResponse(menuRenew);
            case "menus.unregister":
                var menuRemove=_app.RequirePluginMenus(peer.ProcessId,Text(args,"registrationId"),Text(args,"registrationToken"));_app.PluginMenus.Remove(menuRemove.Id);_app.PluginMenusChanged();return new{removed=true};
            case "menus.actions.read":
                var menuRead=_app.RequirePluginMenus(peer.ProcessId,Text(args,"registrationId"),Text(args,"registrationToken"));var menuLimit=args.TryGetProperty("limit",out _)?Number(args,"limit"):32;
                if(menuLimit%1!=0||menuLimit<1||menuLimit>64)throw new FrontendApiException("invalid_params","limit 应为 1–64 的整数。");
                return _app.PluginMenus.Read(menuRead,PluginMenuArguments.Cursor(args,"afterSequence"),(int)menuLimit);
            case "menus.actions.ack":
                var menuAck=_app.RequirePluginMenus(peer.ProcessId,Text(args,"registrationId"),Text(args,"registrationToken"));_app.PluginMenus.Acknowledge(menuAck,PluginMenuArguments.Cursor(args,"throughSequence"));return new{acknowledgedSequence=menuAck.Acknowledged};
            case "app.activate":if(!App.FrontendBackend)_app.OpenSettings();return new{accepted=true};
            case "app.shutdown":
                // Let the pipe flush its acknowledgement before disposal.
                _=Task.Delay(150).ContinueWith(_=>_dispatcher.TryEnqueue(_app.Quit),TaskScheduler.Default);return new{accepted=true};
            case "media.artwork":
                var track=_app.Track;var wanted=Text(args,"trackKey",128);
                if(track==null||track.Key!=wanted)throw new FrontendApiException("stale_track","歌曲已经切换。");
                return new{trackKey=track.Key,mimeType=Mime(track.Cover),base64=track.Cover==null?null:Convert.ToBase64String(track.Cover)};
            case "media.toggle":RequireTrack();await _app.Media.Toggle();return new{accepted=true};
            case "media.next":RequireTrack();await _app.Media.Next();return new{accepted=true};
            case "media.previous":RequireTrack();await _app.Media.Previous();return new{accepted=true};
            case "media.setShuffle":RequireTrack();await _app.Media.SetShuffle(Boolean(args,"enabled"));return new{accepted=true};
            case "media.setRepeat":RequireTrack();var repeat=Text(args,"mode");if(repeat is not ("None" or "Track" or "List"))throw new FrontendApiException("invalid_params","mode 应为 None、Track 或 List。");await _app.Media.SetRepeat(repeat);return new{accepted=true};
            case "media.seek":RequireTrack();await _app.Media.Seek(Number(args,"seconds"));return new{accepted=true};
            case "weather.refresh":if(_app.Prefs.City==null)throw new FrontendApiException("city_required","请先选择城市。");_= _app.RefreshWeather();return new{accepted=true};
            case "cities.search":return await _app.Weather.Search(Text(args,"query"),ct);
            case "cities.select":
                var lat=Number(args,"latitude");var lon=Number(args,"longitude");
                if(Math.Abs(lat)>90||Math.Abs(lon)>180)throw new FrontendApiException("invalid_params","经纬度超出范围。");
                var city=new City(Text(args,"name"),lat,lon,OptionalText(args,"region"),OptionalText(args,"timeZone"));_app.SelectCity(city);return new{accepted=true};
            case "cities.locate":case "cities.locateGps":return await _app.UseSystemLocation(ct)??throw new FrontendApiException("location_unavailable",string.IsNullOrEmpty(_app.LocationStatus)?"定位已取消或由新的城市选择取代。":_app.LocationStatus);
            case "lyrics.refresh":RequireTrack();_= _app.LoadLyrics();return new{accepted=true};
            case "lyrics.import":
                RequireTrack();var key=Text(args,"trackKey");if(key!=_app.Track!.Key)throw new FrontendApiException("stale_track","歌曲已经切换。");
                var text=Text(args,"text",12000);await _app.Lyrics.Import(key,text);
                if(_app.Track?.Key==key){_app.Lines=Lrc.Parse(text);_app.LyricsStatus="本地 LRC 歌词";_app.MusicWindow.RenderLyrics();Notify("lyrics");}return new{accepted=true};
            case "settings.patch":GlassDiagnostics.Event("settings-patch",peer.ProcessId.ToString());await PatchSettings(args,ct);return FrontendSettings.Snapshot(_app.Prefs,_app.Track?.Key);
            case "windows.reservation":_app.CheckRenderer();return new{snapshot=_app.Reservation?.Snapshot,slots=_app.ActiveSlots,slotsVersion=_app.SlotsVersion,owner=_app.RenderingOwner,renderer=_app.RendererStatus()};
            case "windows.registerRenderer":
                if(peer.ProcessId<=0||Number(args,"pid")!=peer.ProcessId)throw new FrontendApiException("peer_mismatch","pid 必须与管道客户端的真实进程一致。");
                var windows=args.GetProperty("capsuleHwnds");
                var sizes=args.TryGetProperty("preferredSizes",out var preferred)?JsonSerializer.Deserialize<RendererSizes>(preferred.GetRawText(),FrontendPipeServer.Json)??new():new RendererSizes();
                var registered=_app.RegisterRenderer(peer.ProcessId,Text(args,"rendererId"),HandleValue(windows,"music"),HandleValue(windows,"weather"),sizes);
                return _app.RendererResponse(registered,true);
            case "windows.activateTaskbarRenderer":
                var activate=_app.RequireRenderer(peer.ProcessId,Text(args,"leaseId"),Text(args,"leaseToken"));_app.ActivateRenderer(activate);return _app.RendererResponse(activate);
            case "windows.renewTaskbarRenderer":
                var renew=_app.RequireRenderer(peer.ProcessId,Text(args,"leaseId"),Text(args,"leaseToken"));_app.RendererLeases.Renew(renew,Environment.TickCount64);return _app.RendererResponse(renew);
            case "windows.releaseTaskbarRenderer":
                _app.RequireRenderer(peer.ProcessId,Text(args,"leaseId"),Text(args,"leaseToken"));_app.RestoreBuiltinRenderer("client_release");return new{released=true,renderingOwner=_app.RenderingOwner};
            case "frontends.list":return new{roots=ExternalFrontendCatalog.Roots,items=ExternalFrontendCatalog.Scan(),renderer=_app.RendererStatus()};
            case "frontends.launch":return ExternalFrontendCatalog.Launch(Text(args,"id"),_descriptor);
            case "windows.hints":return WindowHints();
            case "windows.planCapsules":return WindowHints(args);
            default:throw new FrontendApiException("method_not_found","未提供这个接口。");
        }
    }
    private object Hello()=>new{
        protocolVersion=1,backendVersion="0.7-dev",instanceId=_events.InstanceId,mode=App.FrontendBackend?"preview":"builtin",
        capabilities=new{weather=true,precipitationTrend=true,taskbarGeometry=true,capsuleLayoutPlanning=true,extendedMediaControls=true,solarTimes=true,systemLocation=true,systemFonts=true,eventsLongPoll=true,eventPush=false,media=true,lyrics=true,spectrum=true,settings=true,independentWindows=true,rendererReplacement=!App.FrontendBackend,rendererLease=true,frontendDiscovery=true,pluginMenus=true,pluginMenuLocations=new[]{"tray","settings"},maxConnections=FrontendPipeServer.MaxConnections,taskbarReservation=!App.FrontendBackend&&_app.Reservation?.Snapshot.Compatible==true,builtinTaskbarReservation=_app.Reservation?.Snapshot.Compatible==true},
        methods=new[]{"state.get","spectrum.get","media.artwork","media.toggle","media.next","media.previous","media.seek","media.setShuffle","media.setRepeat","events.wait","weather.refresh","cities.search","cities.select","cities.locate","cities.locateGps","lyrics.refresh","lyrics.import","fonts.list","settings.patch","windows.hints","windows.planCapsules","windows.reservation","windows.registerRenderer","windows.activateTaskbarRenderer","windows.renewTaskbarRenderer","windows.releaseTaskbarRenderer","frontends.list","frontends.launch","menus.list","menus.register","menus.update","menus.renew","menus.unregister","menus.actions.read","menus.actions.ack"}
    };
    private static long HandleValue(JsonElement args,string key)
    {
        var text=Text(args,key,18);long value;
        var valid=text.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?long.TryParse(text.AsSpan(2),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out value):long.TryParse(text,out value);
        if(!valid||value<=0)throw new FrontendApiException("invalid_params","HWND 应为正数的十进制或 0x 十六进制字符串。");return value;
    }
    private void RequireTrack(){if(_app.Track==null)throw new FrontendApiException("no_session","当前没有媒体会话。");}
    private static string OptionalText(JsonElement args,string name)=>args.TryGetProperty(name,out _)?Text(args,name,256):"";
    private static string? Mime(byte[]? bytes)=>bytes==null?null:bytes.Length>3&&bytes[0]==0x89&&bytes[1]==0x50?"image/png":bytes.Length>2&&bytes[0]==0xff&&bytes[1]==0xd8?"image/jpeg":bytes.Length>3&&bytes[0]==0x47&&bytes[1]==0x49?"image/gif":"application/octet-stream";
    private object Snapshot()
    {
        var t=_app.Track;var accent=t?.Accent;
        return new
        {
            protocolVersion=1,capturedAt=DateTimeOffset.UtcNow,stateVersion=_events.Version,instanceId=_events.InstanceId,
            appearance=new{preference=_app.Prefs.Theme,accentColor=_app.Prefs.AccentColor,accentPalette=AccentPalettes.Get(_app.Prefs.AccentColor),popupTheme=_app.WeatherWindow.Root.ActualTheme.ToString(),taskbarTheme=_app.WeatherCapsule.Root.ActualTheme.ToString(),_app.Prefs.CoverGlow,_app.Prefs.CoverSaturation,_app.Prefs.Glow,_app.Prefs.GlassOpacity,_app.Prefs.Animations,_app.Prefs.FontFamily,_app.Prefs.ChineseFontFamily,_app.Prefs.WesternFontFamily},
            weather=new{city=_app.Prefs.City,locatedCity=_app.LocatedCity,status=_app.WeatherStatus,locationBusy=_app.LocationBusy,locationStatus=_app.LocationStatus,forecast=_app.Forecast,precipitationNextHour=Precipitation.NextHour(_app.Forecast?.ShortTermPrecipitation,DateTimeOffset.UtcNow)},
            media=t==null?null:new{t.Key,t.Title,t.Artist,t.Album,t.Source,t.Playing,t.Position,t.Duration,t.ShuffleEnabled,t.RepeatMode,hasArtwork=t.Cover!=null,accent=$"#{accent!.Value.R:X2}{accent.Value.G:X2}{accent.Value.B:X2}",secondaryAccent=t.SecondaryAccent is {} secondary?$"#{secondary.R:X2}{secondary.G:X2}{secondary.B:X2}":null,controls=t.Controls},
            lyrics=new{trackKey=t?.Key,status=_app.LyricsStatus,offsetSeconds=_app.CurrentLyricsOffset,lines=_app.Lines.Select(x=>new{seconds=double.IsFinite(x.Seconds)?(double?)x.Seconds:null,text=x.Text}).ToArray()},
            settings=FrontendSettings.Snapshot(_app.Prefs,t?.Key)
        };
    }
    private object WindowHints(JsonElement requested=default)
    {
        var area=Native.ReadTaskbar(_occupancy);
        double Dimension(string name,double fallback,double min,double max){if(requested.ValueKind==JsonValueKind.Undefined||requested.ValueKind==JsonValueKind.Object&&!requested.TryGetProperty(name,out _))return fallback;var value=Number(requested,name);if(value<min||value>max)throw new FrontendApiException("invalid_params",$"{name} 应在 {min}–{max} DIP 之间。");return value;}
        var mp=Dimension("musicPreferredDip",_app.Prefs.MusicWidth,32,600);var mm=Dimension("musicMinDip",48,32,600);var wp=Dimension("weatherPreferredDip",104,32,400);var wm=Dimension("weatherMinDip",64,32,400);var hd=Dimension("heightDip",34,24,64);
        if(mm>mp||wm>wp)throw new FrontendApiException("invalid_params","最小宽度不能大于期望宽度。");
        var policy=requested.ValueKind==JsonValueKind.Undefined?_placement:new PlacementPolicy(requested.TryGetProperty("wasLite",out _)?Boolean(requested,"wasLite"):false);
        CapsulePlacement? layout=area?.GeometryVerified==true?policy.Calculate(area.Left,area.Right,_app.Prefs.ShowWeather?(int)Math.Ceiling(wp*area.Scale):0,_app.Prefs.ShowMusic&&_app.Track!=null?(int)Math.Ceiling(mp*area.Scale):0,(int)Math.Ceiling(_app.Prefs.Gap*area.Scale),area.Scale,mm,wm):null;
        CapsuleSlots? slots=layout is {} plan&&area!=null?SlotPlanner.Calculate(new(area.Bar.Left,area.Bar.Top,area.Bar.Right,area.Bar.Bottom),area.Right,(int)Math.Ceiling(_app.Prefs.Gap*area.Scale),Math.Min((int)Math.Ceiling(hd*area.Scale),area.Bar.Height-4),plan,area.Visible&&!(_app.Prefs.HideInFullscreen&&Native.ForegroundCoversTaskbar(area)),(int)Math.Round(CapsuleGeometry.TaskbarVerticalOffset*area.Scale)):null;
        if(requested.ValueKind==JsonValueKind.Undefined&&!App.FrontendBackend){slots=_app.ActiveSlots??slots;layout=_app.ActivePlacement??layout;}
        return new{coordinateSpace="physical-screen-pixels",recommendedGapDip=10,popupSizeOwnedBy="frontend",taskbar=area==null?null:new{left=area.Bar.Left,top=area.Bar.Top,right=area.Bar.Right,bottom=area.Bar.Bottom,scale=area.Scale,visible=area.Visible},weatherAnchor=Bounds(_app.WeatherCapsule),musicAnchor=Bounds(_app.MusicCapsule),popups=new{weather=Bounds(_app.WeatherWindow),music=Bounds(_app.MusicWindow)},
            blankZoneRect=area?.GeometryVerified==true?new{left=Math.Min(area.Left,area.Right),top=area.Bar.Top,right=area.Right,bottom=area.Bar.Bottom,visible=area.Visible}:null,
            geometryStatus=area?.GeometryStatus??"unavailable",measuredAt=area?.MeasuredAt,layoutRecommendation=layout,musicSlot=slots?.MusicSlot,weatherSlot=slots?.WeatherSlot,layoutState=slots?.State??"unavailable",trayWidthPx=area?.TrayWidthPx,reservationSupported=!App.FrontendBackend&&_app.Reservation?.Snapshot.Compatible==true,nativeReservation=_app.Reservation?.Snapshot,renderingOwner=_app.RenderingOwner,slotsVersion=_app.SlotsVersion,renderer=_app.RendererStatus()};
    }
    private static object Bounds(SurfaceWindow window)
    {
        Native.GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(window),out var rect);
        return new{left=rect.Left,top=rect.Top,right=rect.Right,bottom=rect.Bottom,visible=window.IsOpen};
    }
    private async Task PatchSettings(JsonElement args,CancellationToken ct)
    {
        if(args.ValueKind!=JsonValueKind.Object)throw new FrontendApiException("invalid_params","设置必须为对象。");
        var allowed=new HashSet<string>{"theme","accentColor","fontFamily","chineseFontFamily","westernFontFamily","coverGlow","coverSaturation","glow","glassOpacity","animations","showWeather","showMusic","spectrum","hideInFullscreen","autoLyrics","weatherRefreshMinutes","weatherProvider","weatherProxyUrl","fahrenheit","windMetersPerSecond","gap","musicWidth","lyricsFontSize","lyricsOffset","lyricsOffsetTrackKey","sensitivity"};
        foreach(var property in args.EnumerateObject())
            if(!allowed.Contains(property.Name))throw new FrontendApiException("invalid_params",$"不支持修改 {property.Name}。");
        if(args.TryGetProperty("accentColor",out _)&&!AccentPalettes.Contains(Text(args,"accentColor",32)))throw new FrontendApiException("invalid_params","请选择提供的强调色。");
        var timing=args.TryGetProperty("lyricsOffset",out _);var timingKey=_app.Track?.Key;
        if(timing)RequireTrack();
        if(args.TryGetProperty("lyricsOffsetTrackKey",out _)&&(!timing||Text(args,"lyricsOffsetTrackKey")!=timingKey))throw new FrontendApiException("stale_track","歌曲已经切换，请刷新歌词同步设置。");
        var fonts=new Dictionary<string,string?>();
        foreach(var field in new[]{"fontFamily","chineseFontFamily","westernFontFamily"})
        {
            if(!args.TryGetProperty(field,out var requested))continue;
            fonts[field]=requested.ValueKind==JsonValueKind.Null?null:
                await _app.Fonts.Resolve(Text(args,field),ct)??throw new FrontendApiException("font_not_found",$"电脑上没有找到 {field} 指定的字体，请刷新字体列表。");
        }
        ct.ThrowIfCancellationRequested();
        if(_app.Exiting)throw new FrontendApiException("shutting_down","后端正在退出。");
        if(timing&&timingKey!=_app.Track?.Key)throw new FrontendApiException("stale_track","歌曲已经切换，请刷新歌词同步设置。");
        // Font enumeration can await. Merge into the latest preferences only after it finishes.
        var obj=JsonSerializer.SerializeToNode(_app.Prefs,FrontendPipeServer.Json)!.AsObject();
        foreach(var property in args.EnumerateObject())
            if(property.Name is not ("lyricsOffset" or "lyricsOffsetTrackKey"))obj[property.Name]=JsonNode.Parse(property.Value.GetRawText());
        // Legacy clients set both scripts; explicit script fields (including null) win.
        if(fonts.TryGetValue("fontFamily",out var legacyFont))
        {
            obj["chineseFontFamily"]=legacyFont;
            obj["westernFontFamily"]=legacyFont;
        }
        foreach(var (field,family) in fonts)obj[field]=family;
        Preferences prefs;
        try{prefs=obj.Deserialize<Preferences>(FrontendPipeServer.Json)!;prefs.Normalize();}catch(JsonException){throw new FrontendApiException("invalid_params","设置值类型有误。");}
        if(timing)TrackLyricTiming.Set(prefs.LyricsOffsets,timingKey,Number(args,"lyricsOffset"));
        var changes=PreferenceChanges.Between(_app.Prefs,prefs);
        if(!changes.Any){GlassDiagnostics.Event("settings-noop","skipped");return;}
        var weatherChanged=prefs.WeatherProvider!=_app.Prefs.WeatherProvider||prefs.WeatherProxyUrl!=_app.Prefs.WeatherProxyUrl;
        prefs.Save();foreach(var property in typeof(Preferences).GetProperties(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance).Where(p=>p.CanWrite))property.SetValue(_app.Prefs,property.GetValue(prefs));
        if(changes.GlassOnly){GlassDiagnostics.Event("settings-glass","targeted");_app.PreviewGlass();Notify("settings");}
        else if(changes.AccentOnly){_app.RefreshAccent();Notify("settings");}
        else _app.Save(persist:false);
        if(weatherChanged)_app.ReloadWeatherProvider();
        if(timing){_app.MusicWindow.RefreshLyricsTiming();Notify("lyrics");}
    }
    public void Dispose(){_server.Dispose();try{File.Delete(_descriptor);}catch(IOException){}catch(UnauthorizedAccessException){}}
}
