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
    internal void Notify(string topic)=>_events.Publish(topic);
    internal FrontendApi(App app,DispatcherQueue dispatcher)
    {
        _app=app;_dispatcher=dispatcher;_occupancy=app.Occupancy;_occupancy.Changed+=()=>Notify("windows");
        using var identity=WindowsIdentity.GetCurrent();
        var owner=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.User!.Value)))[..12];
        var pipe=$"LumiBar.Frontend.v1.{owner}.{Process.GetCurrentProcess().SessionId}"+(App.FrontendBackend?".Preview":"");
        _server=new(pipe,Dispatch);_server.Start();
        _descriptor=Path.Combine(Preferences.DataPath,"frontend-endpoint.json");
        AtomicStore.Write(_descriptor,JsonSerializer.Serialize(new{protocolVersion=1,pipeName=pipe,pid=Environment.ProcessId,mode="preview",startedAt=DateTimeOffset.UtcNow},FrontendPipeServer.Json));
    }
    private async Task<object?> Dispatch(string method,JsonElement args,CancellationToken ct)
    {
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
            try{ct.ThrowIfCancellationRequested();completion.TrySetResult(await Handle(method,args,ct));}
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
    private async Task<object?> Handle(string method,JsonElement args,CancellationToken ct)
    {
        if(_app.Exiting)throw new FrontendApiException("shutting_down","后端正在退出。");
        switch(method)
        {
            case "hello":return new{protocolVersion=1,backendVersion="0.6-dev",instanceId=_events.InstanceId,mode="preview",capabilities=new{weather=true,precipitationTrend=true,taskbarGeometry=true,capsuleLayoutPlanning=true,extendedMediaControls=true,solarTimes=true,systemFonts=true,eventsLongPoll=true,eventPush=false,media=true,lyrics=true,spectrum=true,settings=true,independentWindows=true,rendererReplacement=false,taskbarReservation=false,builtinTaskbarReservation=_app.Reservation?.Snapshot.Compatible==true},methods=new[]{"state.get","spectrum.get","media.artwork","media.toggle","media.next","media.previous","media.seek","media.setShuffle","media.setRepeat","events.wait","weather.refresh","cities.search","cities.select","cities.locateGps","lyrics.refresh","lyrics.import","fonts.list","settings.patch","windows.hints","windows.planCapsules","windows.reservation"}};
            case "state.get":return Snapshot();
            case "spectrum.get":return new{capturedAt=DateTimeOffset.UtcNow,playing=_app.Track?.Playing==true,source="system-output",status=_app.Spectrum.Status,bands=_app.Track?.Playing==true&&_app.Prefs.Spectrum?_app.Spectrum.Levels():new float[20]};
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
            case "cities.locateGps":var gps=await _app.Weather.LocateGps();_app.SelectCity(gps);return gps;
            case "lyrics.refresh":RequireTrack();_= _app.LoadLyrics();return new{accepted=true};
            case "lyrics.import":
                RequireTrack();var key=Text(args,"trackKey");if(key!=_app.Track!.Key)throw new FrontendApiException("stale_track","歌曲已经切换。");
                var text=Text(args,"text",12000);await _app.Lyrics.Import(key,text);
                if(_app.Track?.Key==key){_app.Lines=Lrc.Parse(text);_app.LyricsStatus="本地 LRC 歌词";_app.MusicWindow.RenderLyrics();Notify("lyrics");}return new{accepted=true};
            case "settings.patch":await PatchSettings(args,ct);return JsonSerializer.SerializeToElement(_app.Prefs,FrontendPipeServer.Json);
            case "windows.reservation":return new{snapshot=_app.Reservation?.Snapshot,slots=_app.ActiveSlots,owner=App.FrontendBackend?"preview-no-reservation":"builtin"};
            case "windows.hints":return WindowHints();
            case "windows.planCapsules":return WindowHints(args);
            default:throw new FrontendApiException("method_not_found","未提供这个接口。");
        }
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
            appearance=new{preference=_app.Prefs.Theme,popupTheme=_app.WeatherWindow.Root.ActualTheme.ToString(),taskbarTheme=_app.WeatherCapsule.Root.ActualTheme.ToString(),_app.Prefs.CoverGlow,_app.Prefs.Glow,_app.Prefs.GlassOpacity,_app.Prefs.Animations,_app.Prefs.FontFamily},
            weather=new{city=_app.Prefs.City,status=_app.WeatherStatus,forecast=_app.Forecast,precipitationNextHour=Precipitation.NextHour(_app.Forecast?.ShortTermPrecipitation,DateTimeOffset.UtcNow)},
            media=t==null?null:new{t.Key,t.Title,t.Artist,t.Album,t.Source,t.Playing,t.Position,t.Duration,t.ShuffleEnabled,t.RepeatMode,hasArtwork=t.Cover!=null,accent=$"#{accent!.Value.R:X2}{accent.Value.G:X2}{accent.Value.B:X2}",controls=t.Controls},
            lyrics=new{trackKey=t?.Key,status=_app.LyricsStatus,offsetSeconds=_app.Prefs.LyricsOffset,lines=_app.Lines.Select(x=>new{seconds=double.IsFinite(x.Seconds)?(double?)x.Seconds:null,text=x.Text}).ToArray()},
            settings=JsonSerializer.SerializeToElement(_app.Prefs,FrontendPipeServer.Json)
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
        CapsuleSlots? slots=layout is {} plan&&area!=null?SlotPlanner.Calculate(new(area.Bar.Left,area.Bar.Top,area.Bar.Right,area.Bar.Bottom),area.Right,(int)Math.Ceiling(_app.Prefs.Gap*area.Scale),Math.Min((int)Math.Ceiling(hd*area.Scale),area.Bar.Height-4),plan,area.Visible&&!(_app.Prefs.HideInFullscreen&&Native.ForegroundCoversTaskbar(area))):null;
        if(requested.ValueKind==JsonValueKind.Undefined&&!App.FrontendBackend){slots=_app.ActiveSlots??slots;layout=_app.ActivePlacement??layout;}
        return new{coordinateSpace="physical-screen-pixels",recommendedGapDip=10,popupSizeOwnedBy="frontend",taskbar=area==null?null:new{left=area.Bar.Left,top=area.Bar.Top,right=area.Bar.Right,bottom=area.Bar.Bottom,scale=area.Scale,visible=area.Visible},weatherAnchor=Bounds(_app.WeatherCapsule),musicAnchor=Bounds(_app.MusicCapsule),
            blankZoneRect=area?.GeometryVerified==true?new{left=Math.Min(area.Left,area.Right),top=area.Bar.Top,right=area.Right,bottom=area.Bar.Bottom,visible=area.Visible}:null,
            geometryStatus=area?.GeometryStatus??"unavailable",measuredAt=area?.MeasuredAt,layoutRecommendation=layout,musicSlot=slots?.MusicSlot,weatherSlot=slots?.WeatherSlot,layoutState=slots?.State??"unavailable",trayWidthPx=area?.TrayWidthPx,reservationSupported=false,nativeReservation=_app.Reservation?.Snapshot,renderingOwner=App.FrontendBackend?"preview":"builtin"};
    }
    private static object Bounds(SurfaceWindow window)
    {
        Native.GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(window),out var rect);
        return new{left=rect.Left,top=rect.Top,right=rect.Right,bottom=rect.Bottom,visible=window.IsOpen};
    }
    private async Task PatchSettings(JsonElement args,CancellationToken ct)
    {
        if(args.ValueKind!=JsonValueKind.Object)throw new FrontendApiException("invalid_params","设置必须为对象。");
        var allowed=new HashSet<string>{"theme","fontFamily","coverGlow","glow","glassOpacity","animations","showWeather","showMusic","spectrum","hideInFullscreen","autoLyrics","weatherRefreshMinutes","fahrenheit","windMetersPerSecond","gap","musicWidth","lyricsFontSize","lyricsOffset","sensitivity"};
        string? selectedFont=null;var changesFont=args.TryGetProperty("fontFamily",out var requested);
        if(changesFont&&requested.ValueKind!=JsonValueKind.Null){var family=Text(args,"fontFamily");selectedFont=await _app.Fonts.Resolve(family,ct)??throw new FrontendApiException("font_not_found","电脑上没有找到这款字体，请刷新字体列表。");}
        // Font enumeration can await. Merge into the latest preferences only after it finishes.
        var obj=JsonSerializer.SerializeToNode(_app.Prefs,FrontendPipeServer.Json)!.AsObject();
        foreach(var property in args.EnumerateObject())
        {
            if(!allowed.Contains(property.Name))throw new FrontendApiException("invalid_params",$"不支持修改 {property.Name}。");
            obj[property.Name]=JsonNode.Parse(property.Value.GetRawText());
        }
        Preferences prefs;
        if(changesFont)obj["fontFamily"]=selectedFont;
        try{prefs=obj.Deserialize<Preferences>(FrontendPipeServer.Json)!;prefs.Normalize();}catch(JsonException){throw new FrontendApiException("invalid_params","设置值类型有误。");}
        prefs.Save();foreach(var property in typeof(Preferences).GetProperties(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance).Where(p=>p.CanWrite))property.SetValue(_app.Prefs,property.GetValue(prefs));_app.Save(persist:false);
    }
    public void Dispose(){_server.Dispose();try{File.Delete(_descriptor);}catch(IOException){}catch(UnauthorizedAccessException){}}
}
