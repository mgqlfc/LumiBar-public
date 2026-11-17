using LumiBar;

var results=new List<string>();
void Check(bool value,string name){results.Add($"{(value?"PASS":"FAIL")} {name}");}
foreach(var light in new[]{true,false}){
    var values=Enumerable.Range(0,61).Select(i=>GlassDensity.WashOpacity(.35+i*.01,light)).ToArray();
    Check(values[0]==0&&values.All(v=>v>=0&&v<1)&&values.Zip(values.Skip(1)).All(p=>p.First<=p.Second),$"Glass density changes monotonically inside a fixed acrylic material ({light})");
    Check(GlassDensity.WashOpacity(double.NaN,light)==GlassDensity.WashOpacity(.62,light)&&GlassDensity.WashOpacity(-1,light)==values[0]&&GlassDensity.WashOpacity(2,light)==values[^1],$"Glass density clamps saved values without invalid composition opacity ({light})");
}
async Task Cancelled(Task task,string name){try{await task;Check(false,name);}catch(OperationCanceledException){Check(true,name);}}
var screenBar=new ScreenRect(0,1522,2560,1600);var fullDesktop=new ScreenRect(0,0,2560,1600);
Check(!FullscreenPolicy.Covers(fullDesktop,screenBar,"XamlExplorerHostIslandWindow",true),"Explorer taskbar preview islands are not fullscreen applications");
Check(!FullscreenPolicy.Covers(fullDesktop,screenBar,"TaskListThumbnailWnd",true),"Shell thumbnails leave capsules visible");
Check(FullscreenPolicy.Covers(fullDesktop,screenBar,"Chrome_WidgetWin_1",false),"Real fullscreen browser windows still hide capsules");
Check(FullscreenPolicy.Covers(fullDesktop,screenBar,"CabinetWClass",true),"Fullscreen Explorer file windows retain normal fullscreen behavior");
Check(FullscreenPolicy.Covers(fullDesktop,screenBar,"XamlExplorerHostIslandWindow",false),"Matching a shell class without shell ownership does not bypass fullscreen detection");
var hiddenBar=screenBar with{Top=1598,Bottom=1676};
Check(TaskbarGeometry.SameHorizontal(screenBar,hiddenBar)&&!TaskbarGeometry.SameHorizontal(screenBar,screenBar with{Right=1920}),"Auto-hide vertical translation preserves only matching horizontal taskbar geometry");
var continuity=new TaskbarSlotContinuity();var autoHideOwnedSlot=new ScreenRect(1700,1522,2320,1600);
Check(continuity.Resolve(screenBar,1.5,10,autoHideOwnedSlot,"reserved",true,1000)==autoHideOwnedSlot,"Native slot continuity records an actually owned slot");
var continued=continuity.Resolve(hiddenBar,1.5,10,null,"ready",true,1300);
Check(continued==autoHideOwnedSlot,"Auto-hide arrangement frames keep their verified native slot");
Check(continuity.Resolve(hiddenBar,1.5,10,null,"failed",true,1400)==null&&continuity.Resolve(hiddenBar,1.5,10,null,"ready",true,2300)==null,"Failed or old reservations never gain an indefinite layout lease");
Check(continuity.Resolve(hiddenBar,1.5,10,null,"ready",true,1500)==null,"A ready response after failure cannot revive old native ownership");
Check(continuity.Resolve(hiddenBar,1.5,11,null,"ready",true,1500)==null,"Explorer restart invalidates cached native slot ownership");
var revealLayout=new ReservationLayoutPolicy();var revealStart=revealLayout.Calculate(screenBar,1300,2320,autoHideOwnedSlot,1.5,156,357,12,true,geometryVerified:true);
var revealFrame=revealLayout.Calculate(hiddenBar,2320,2320,continued,1.5,156,357,12,true,geometryVerified:false);
Check(!revealStart.Elevated&&!revealFrame.Elevated&&!revealFrame.Capsules.Lite&&revealStart.ReserveDip==revealFrame.ReserveDip,"Taskbar reveal does not create false Lite pressure or elevate capsules above the bar");
var now=DateTimeOffset.UtcNow;
Check(WeatherProxyAddress.Normalize("https://example.com/weather")=="https://example.com/weather/","Weather proxy address keeps a normalized HTTPS base path");
foreach(var bad in new[]{"http://example.com/","https://user:secret@example.com/","https://example.com/?key=secret","https://example.com/#x"})Check(WeatherProxyAddress.Normalize(bad)==null,"Weather proxy rejects insecure or credential-bearing address");
Check(WeatherProxyAddress.Normalize("http://127.0.0.1:8767/")!=null,"Weather proxy permits isolated loopback development");
var fiveMinuteRain=new PrecipitationSeries(now,"和风天气","5 分钟临近预报",Enumerable.Range(0,12).Select(i=>new RainInterval(now.AddMinutes(5*i),now.AddMinutes(5*(i+1)),.1)).ToList());
var fiveMinuteTrend=Precipitation.NextHour(fiveMinuteRain,now);Check(fiveMinuteTrend?.Complete==true&&fiveMinuteTrend.IntervalMinutes==5&&fiveMinuteTrend.Kind=="nowcast","QWeather 5-minute amounts cover the next hour without 15-minute relabeling");
using(var payload=System.Text.Json.JsonDocument.Parse("""{"forecast":{"temperature":9,"feelsLike":5,"humidity":66,"wind":14.4,"windDirection":45,"hours":[],"days":[],"updated":"2026-11-15T04:00:00Z","source":"和风天气","aqi":120,"aqiStandard":"CN AQI"}}""")){var parsed=WeatherProxyPayload.Read(payload.RootElement);Check(parsed.Temperature==9&&parsed.AqiStandard=="CN AQI"&&parsed.Aqi==120,"Proxy forecast maps camelCase data and preserves Chinese AQI standard");}
using(var missing=System.Text.Json.JsonDocument.Parse("""{"forecast":{"hours":[],"days":[]}}""")){try{WeatherProxyPayload.Read(missing.RootElement);Check(false,"Proxy missing temperature cannot publish a false zero");}catch(InvalidDataException){Check(true,"Proxy missing temperature cannot publish a false zero");}}
var advisory=new WeatherAlert("rain","暴雨","orange","暴雨橙色预警","区域降水预警","注意出行","测试气象台","测试地区",now.AddMinutes(-30),now.AddMinutes(-30),now.AddHours(2));
var blueAlert=advisory with{Id="wind",Event="大风",Level="blue"};var redAlert=advisory with{Id="red",Level="red"};
var currentAlerts=WeatherAlerts.Current([blueAlert,advisory,redAlert],now);
Check(currentAlerts.Count==3&&currentAlerts[0].Level=="red"&&currentAlerts[2].Level=="blue","Concurrent alerts show highest severity first without losing other warnings");
Check(WeatherAlerts.Label(advisory)=="暴雨"&&WeatherAlerts.Label(blueAlert)=="大风"&&WeatherAlerts.Label(blueAlert with{Event="海上大风"})=="海上大风","Warning badges show event names without redundant color text");
Check(WeatherAlerts.Current([advisory with{EndsAt=now},blueAlert with{StartsAt=now.AddMinutes(1)}],now).Count==0,"Expired and not-yet-active warnings stay hidden");
Check(WeatherAlerts.Current([advisory,advisory with{Cancelled=true,IssuedAt=now.AddMinutes(-1)}],now).Count==0,"Cancellation supersedes an older active warning with the same ID");
Check(WeatherAlerts.Current([advisory with{EndsAt=null,IssuedAt=now.AddDays(-2)}],now).Count==0,"Undated expiry never leaves a stale warning permanently visible");
Check(WeatherAlerts.Current(null,now).Count==0,"Unsupported alert source leaves no empty badge");
using(var alertJson=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new{alerts=new[]{new{id="a",@event="雷电",level="yellow",issuedAt=now.AddHours(-1),endsAt=now.AddHours(1),description="详细预警",source="气象台"}}}))){var parsedAlerts=WeatherAlerts.Parse(alertJson.RootElement,"fallback");Check(parsedAlerts.Count==1&&parsedAlerts[0].Level=="yellow"&&parsedAlerts[0].Description=="详细预警"&&parsedAlerts[0].Source=="气象台","Provider alert extension preserves actual level, details and issuer");}
var gatePid=(uint)Random.Shared.Next(500000,900000);using(var priorBridge=new Mutex(false,BridgeAttachGate.Name(gatePid))){var waiting=BridgeAttachGate.WaitAsync(gatePid,TimeSpan.FromSeconds(2));await Task.Delay(200);Check(!waiting.IsCompleted,"New diagnostics attachment waits while previous native owner exists");priorBridge.Dispose();Check(await waiting,"Attachment proceeds only after previous owner releases its object");}
using(var heldBridge=new Mutex(false,BridgeAttachGate.Name(gatePid))){Check(!await BridgeAttachGate.WaitAsync(gatePid,TimeSpan.FromMilliseconds(180)),"Busy native owner times out without replacing diagnostics site");using var stopGate=new CancellationTokenSource(100);await Cancelled(BridgeAttachGate.WaitAsync(gatePid,TimeSpan.FromSeconds(2),stopGate.Token),"Shutdown cancels waiting attachment");}
foreach(var font in new[]{18d,26d,36d}){
    var layout=LyricLayout.ForFont(font);var height=layout.ViewportHeight;var anchor=layout.FocusTop;var fade=LyricFade.ForFocus(height,anchor,font*1.3);
    Check(fade.UpperClear*height<anchor&&fade.LowerClear*height>anchor+font*1.3,$"Extended lyric fade keeps the highlighted line fully clear at font {font}");
}
var lyricGeometry=LyricLayout.ForFont(25);
Check(lyricGeometry.ViewportHeight==435&&lyricGeometry.TopExtension==22.5&&lyricGeometry.BottomExtension==67.5,"Default lyric viewport extends half a row above and one-and-a-half below");
Check(lyricGeometry.FocusTop-lyricGeometry.TopExtension==93,"Extending the top preserves the restored highlight screen anchor");
var wrappedFade=LyricFade.ForFocus(392,71,240);
Check(wrappedFade.LowerClear*392>311&&wrappedFade.UpperMid<wrappedFade.UpperClear&&wrappedFade.LowerClear<wrappedFade.LowerMid&&wrappedFade.LowerMid<wrappedFade.LowerTail,"Wrapped active lyric remains outside both fades with ordered gradient stops");
var oldForecast=System.Text.Json.JsonSerializer.Deserialize<Forecast>("""{"Temperature":12,"Hours":[],"Days":[],"Updated":"2026-11-14T12:00:00"}""")!;
Check(oldForecast.Temperature==12&&oldForecast.PressureHpa==null&&oldForecast.UvIndex==null,"Old weather cache loads with missing pressure and UV as null");
var weatherMetrics=oldForecast with{PressureHpa=1024.3,UvIndex=0};
Check(WeatherAir.Caption(oldForecast with{Aqi=110,AqiStandard="CN AQI"})=="轻度污染","Chinese AQI hover uses Chinese thresholds and a compact label");
Check(WeatherAir.Caption(oldForecast with{Aqi=110,AqiStandard="US AQI"})=="敏感人群","US AQI keeps its own compact health category");
Check(WeatherAir.Caption(oldForecast)=="暂无数据","Missing AQI never gets a pollution category");
var cachedMetrics=System.Text.Json.JsonSerializer.Deserialize<Forecast>(System.Text.Json.JsonSerializer.Serialize(weatherMetrics))!;
Check(cachedMetrics.PressureHpa==1024.3&&cachedMetrics.UvIndex==0,"Weather cache round trip keeps real pressure and nighttime zero UV");
using(var metricPayload=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(weatherMetrics,FrontendPipeServer.Json)))
Check(metricPayload.RootElement.GetProperty("pressureHpa").GetDouble()==1024.3&&metricPayload.RootElement.GetProperty("uvIndex").GetDouble()==0,"Frontend state exposes optional pressureHpa and uvIndex in camelCase");
var popupLifetime=new SurfaceLifetime();
var firstPresentation=popupLifetime.Show();
Check(popupLifetime.CanDismiss(firstPresentation),"Current outside click can dismiss popup");
var closingPresentation=popupLifetime.Close();
Check(!popupLifetime.CanDismiss(firstPresentation)&&popupLifetime.CanFinishClose(closingPresentation),"Closing popup ignores duplicate dismissals");
var reopenedPresentation=popupLifetime.Show();
Check(!popupLifetime.CanDismiss(firstPresentation),"Queued outside click and deactivation cannot dismiss reopened weather or lyrics");
Check(!popupLifetime.CanFinishClose(closingPresentation),"Old fade completion cannot hide reopened popup");
Check(popupLifetime.CanDismiss(reopenedPresentation),"Reopened popup still accepts new outside clicks");
var reopenSafe=true;for(var i=0;i<100;i++){var closing=popupLifetime.Close();var showing=popupLifetime.Show();reopenSafe&=!popupLifetime.CanFinishClose(closing)&&popupLifetime.CanDismiss(showing);}Check(reopenSafe,"100 rapid popup reopen cycles reject stale completions");
foreach(var light in new[]{false,true})foreach(var sample in new[]{(R:(byte)255,G:(byte)0,B:(byte)0),(R:(byte)0,G:(byte)255,B:(byte)0),(R:(byte)0,G:(byte)0,B:(byte)255),(R:(byte)255,G:(byte)0,B:(byte)255)})
{
    var soft=AlbumLightColor.Soften(sample.R,sample.G,sample.B,light);
    double max=Math.Max(soft.R,Math.Max(soft.G,soft.B))/255d,min=Math.Min(soft.R,Math.Min(soft.G,soft.B))/255d;
    Check((max-min)/(1-Math.Abs(max+min-1))<=.36,$"Default album light chroma bounded within 8-bit rounding for {sample} light={light}");
    Check(Math.Sign(sample.R-sample.G)==Math.Sign(soft.R-soft.G)&&Math.Sign(sample.G-sample.B)==Math.Sign(soft.G-soft.B),$"Album light preserves hue ordering for {sample} light={light}");
}
Check(AlbumLightColor.Soften(128,128,128,false)==(128,128,128),"Neutral cover light stays neutral");
Check(MediaRules.Select([(false,true,true)])==0,"Paused session stays available");
Check(MediaRules.Select([(true,false,false),(true,false,true)])==1,"Keep previous playing session instead of enumeration order");
Check(MediaRules.Select([(false,false,true),(true,true,false)])==1,"Current playing session wins");
Check(MediaRules.Select([])==-1,"No session means absent");
Check(MediaRules.Position(30,200,true,1,default,now)==30,"Unknown timestamp does not jump to end");
Check(MediaRules.Position(30,200,false,1,now.AddSeconds(-50),now)==30,"Pause does not advance timeline");
Check(MediaRules.Position(30,200,true,2,now.AddSeconds(-3),now)==36,"Playback speed contributes to timeline");
Check(MediaRules.Position(199,200,true,1,now.AddSeconds(-3),now)==200,"Timeline clamps to duration");
Check(MediaRules.TrackKey("a|b","c","d","e",200)!=MediaRules.TrackKey("a","b|c","d","e",200),"Track keys avoid delimiter collision");
Check(MediaRules.TrackKey("a","b","c","d",200)!=MediaRules.TrackKey("a","b","c","d",260),"Live/studio duration separates lyric cache");
Check(!MediaRules.LyricsMatch("微光","歌手",200,"微光","另一位歌手",200),"Reject wrong-artist lyric result");
var lrc=Lrc.Parse("[00:01.00]原文\n[00:01]译文\n[00:61]invalid\n[00:02.50]next\n[offset:100]");
Check(lrc.Count==2&&lrc[0].Text=="原文\n译文"&&Math.Abs(lrc[0].Seconds-.9)<.001,"Bilingual LRC merges equal timestamps and skips invalid seconds");
Check(Lrc.Active(lrc,.1)==-1&&Lrc.Active(lrc,1)==0&&Lrc.Active(lrc,4)==1,"Lyric selection before/start/after track");
try{Lrc.Parse(new string('x',1024*1024+1));Check(false,"Oversized lyric rejected");}catch(InvalidDataException){Check(true,"Oversized lyric rejected");}
using(var requests=new LatestRequest<int>())
{
    var source=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);var count=0;
    var first=requests.Run("city-a",_=>{count++;return source.Task;});
    var duplicate=requests.Run("city-a",_=>{count++;return Task.FromResult(99);});
    Check(ReferenceEquals(first,duplicate)&&count==1,"Duplicate refresh coalesces in-flight request");
    var latest=await requests.Run("city-b",_=>Task.FromResult(2));source.SetResult(1);
    await Cancelled(first,"Superseded request cannot return even if provider ignores cancellation");
    Check(latest.Value==2&&latest.Version==requests.Version,"Only latest city is eligible for commit");
}
var pending=new LatestRequest<int>();
var stopped=pending.Run("x",async ct=>{await Task.Delay(Timeout.Infinite,ct);return 0;});pending.Dispose();
await Cancelled(stopped,"Shutdown cancels outstanding work");
var schedule=new RefreshSchedule();schedule.Failure(now);Check(schedule.NextAttempt==now.AddMinutes(1),"First failure retries after one minute");
schedule.Failure(now);schedule.Failure(now);schedule.Failure(now);schedule.Failure(now);Check(schedule.NextAttempt==now.AddMinutes(30),"Retry delay caps at thirty minutes");
schedule.Success(now,20);Check(schedule.Failures==0&&schedule.NextAttempt==now.AddMinutes(20),"Successful update resets retry delay");
var policy=new PlacementPolicy();
var wide=policy.Calculate(0,500,104,238,8,1);Check(!wide.Lite&&wide.WeatherWidth==104&&wide.MusicWidth==238,"Normal layout preserves both widths");
var lite=policy.Calculate(0,200,104,238,8,1);Check(lite.Lite&&lite.WeatherWidth==64&&lite.MusicWidth==48&&!lite.RequiresReservation,"Crowding keeps BOTH capsules as Lite");
var full=policy.Calculate(0,100,104,238,8,1);Check(full.WeatherWidth>0&&full.MusicWidth>0&&full.RequiresReservation&&full.RequiredWidth==136,"Full taskbar requests reserved space instead of hiding or overlapping");
var edge=policy.Calculate(0,370,104,238,8,1);Check(edge.Lite,"Recovery hysteresis prevents layout flicker");
Check(!policy.Calculate(0,382,104,238,8,1).Lite,"Enough recovered space restores normal layout");
Check(policy.Calculate(0,500,104,0,8,1).MusicWidth==0,"Closed music session does not create empty capsule");
var scaled=new PlacementPolicy().Calculate(0,240,156,357,12,1.5);Check(scaled.Lite&&scaled.RequiredWidth==204,"Lite reservation respects 150 percent DPI");
var folder=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
Environment.SetEnvironmentVariable("LUMIBAR_DATA_DIR",folder);
var preferences=new Preferences{Theme="unexpected",City=new("bad",91,0),Favorites=[new("a",36,119),new("duplicate",36,119)],Gap=1000,Glow=double.NaN};preferences.Save();
Check(preferences.City==null&&preferences.Favorites.Count==1&&preferences.Gap==24&&preferences.Glow==new Preferences().Glow&&preferences.Theme=="System","Preferences validate invalid coordinates and values");
preferences.Gap=12;preferences.Save();File.WriteAllText(Path.Combine(folder,"settings.json"),"broken{");
Check(Preferences.Load().Gap==24,"Interrupted/corrupt settings recover from last backup");
Check(Directory.GetFiles(folder,"*.tmp").Length==0,"Atomic writes leave no temporary files");
results.AddRange(RainTests.Run());
results.AddRange(await ExtensionTests.Run());
results.AddRange(AudioTests.Run());
results.AddRange(AppearanceCityTests.Run());
results.AddRange(PreferenceUpdateTests.Run());
results.AddRange(DefaultSettingsTests.Run());
results.AddRange(AccentPaletteTests.Run());
results.AddRange(PluginMenuTests.Run());
results.AddRange(FontMigrationTests.Run());
results.AddRange(TrackLyricTimingTests.Run());
var taskbar=new ScreenRect(0,1522,2560,1600);
var space=TaskbarSpace.Measure(taskbar,[new(new(0,1522,91,1600),false),new(new(461,1522,534,1600),false),new(new(753,1522,826,1600),false),new(new(1896,1522,1942,1600),true),new(new(2530,1522,2550,1600),true)],true);
Check(space?.Left==826&&space?.Right==1896,"Taskbar geometry follows actual XAML buttons rather than stale legacy HWND bounds");
Check(TaskbarSpace.Measure(taskbar,[new(new(461,1522,534,1600),false)],true)==null,"Missing tray measurement cannot manufacture free space");
Check(TaskbarSpace.Measure(taskbar,[new(new(461,1522,534,1600),false),new(new(1900,1522,2000,1600),true)],false)==null,"Unknown shell layout remains unavailable");
var crowded=TaskbarSpace.Measure(taskbar,[new(new(1800,1522,2000,1600),false),new(new(1900,1522,2560,1600),true)],true);
Check(crowded?.Left==1900&&crowded?.Right==1900,"Crowded taskbar yields zero width instead of overlapping rectangle");
var popup=TaskbarSpace.Measure(taskbar,[new(new(0,1522,800,1600),false),new(new(0,100,2400,900),false),new(new(1900,1522,2560,1600),true)],true);
Check(popup?.Left==800,"Above-taskbar popup does not count as taskbar occupancy");
var negativeMonitor=TaskbarSpace.Measure(new(-1920,1000,0,1080),[new(new(-1500,1000,-1000,1080),false),new(new(-300,1000,0,1080),true)],true);
Check(negativeMonitor?.Left==-1000&&negativeMonitor?.Right==-300,"Geometry preserves negative monitor coordinates");
var stableReservation=new ReservationLayoutPolicy();
var reservedBar=new ScreenRect(0,1522,2560,1600);var ownedSlot=new ScreenRect(1300,1522,1900,1600);
var measuredFull=stableReservation.Calculate(reservedBar,900,1900,ownedSlot,1.5,156,357,12,true);
var startMenu=stableReservation.Calculate(reservedBar,1900,1900,ownedSlot,1.5,156,357,12,true,geometryVerified:false);
Check(!measuredFull.Capsules.Lite&&!startMenu.Capsules.Lite&&measuredFull.ReserveDip==startMenu.ReserveDip,"Start menu UIA loss keeps verified full reservation width");
var realCrowding=stableReservation.Calculate(reservedBar,1650,1900,ownedSlot,1.5,156,357,12,true);
Check(realCrowding.Capsules.Lite,"Real measured app pressure still enters Lite");
var unknownLite=stableReservation.Calculate(reservedBar,1900,1900,ownedSlot,1.5,156,357,12,true,geometryVerified:false);
Check(unknownLite.Capsules.Lite,"UIA loss retains measured Lite state instead of expanding");
Check(!stableReservation.Calculate(reservedBar,900,1900,ownedSlot,1.5,156,357,12,true).Capsules.Lite,"Fresh recovered app geometry restores full layout");
var reconnectWide=new ReservationLayoutPolicy().Calculate(reservedBar,900,1900,null,1.5,156,357,12,false);
Check(!reconnectWide.Elevated&&!reconnectWide.Capsules.Lite,"Reservation reconnect stays full on verified ample taskbar space");
var reconnectTight=new ReservationLayoutPolicy().Calculate(reservedBar,1580,1900,null,1.5,156,357,12,false);
Check(!reconnectTight.Elevated&&reconnectTight.Capsules.Lite,"Reservation reconnect keeps Lite inside a verified fitting blank area");
Check(new ReservationLayoutPolicy().Calculate(reservedBar,1880,1900,null,1.5,156,357,12,false).Elevated,"Genuine lack of blank space still prevents overlap during reconnect");
results.AddRange(await PipeTests.Run());
results.AddRange(await DiagnosticsTests.Run());
results.AddRange(await RendererTests.Run());
results.AddRange(await PluginMenuSdkTests.Run());
foreach(var line in results)Console.WriteLine(line);
Console.WriteLine($"{results.Count(x=>x.StartsWith("PASS"))}/{results.Count} passed");
Environment.ExitCode=results.Any(x=>x.StartsWith("FAIL"))?1:0;
