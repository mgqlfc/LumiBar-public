using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace LumiBar;
internal static class Diagnostics
{
    private static readonly DateTimeOffset Started=DateTimeOffset.Now;
    // Capture on the UI thread; never serialize Preferences or renderer leases.
    internal static object Capture(App app)
    {
        object Bounds(SurfaceWindow window){Native.GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(window),out var r);return new{r.Left,r.Top,r.Right,r.Bottom,window.IsOpen};}
        var p=app.Prefs;var t=app.Track;var reservation=app.Reservation?.Snapshot;
        return new{
            schemaVersion=1,capturedAt=DateTimeOffset.Now,startedAt=Started,
            application=new{version=typeof(App).Assembly.GetName().Version?.ToString(3),RuntimeInformation.FrameworkDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),RuntimeInformation.OSDescription},
            appearance=new{p.Theme,p.ChineseFontFamily,p.WesternFontFamily,p.LyricsFontSize,p.GlassOpacity,p.Glow,p.CoverGlow,p.CoverSaturation,p.Animations},
            taskbar=new{slots=app.ActiveSlots,placement=app.ActivePlacement,reservation,externalRenderer=app.ExternalRendererActive},
            windows=new{weather=Bounds(app.WeatherWindow),music=Bounds(app.MusicWindow),settings=Bounds(app.SettingsWindow),weatherCapsule=Bounds(app.WeatherCapsule),musicCapsule=Bounds(app.MusicCapsule)},
            media=new{hasSession=t!=null,playing=t?.Playing==true,hasArtwork=t?.Cover!=null,player=t?.Source,lyricCount=app.Lines.Count,spectrumEnabled=p.Spectrum,spectrumStatus=app.Spectrum.Status},
            weather=new{hasForecast=app.Forecast!=null,provider=app.Forecast?.Source,updatedAt=app.Forecast?.Updated,hasSelectedCity=p.City!=null,automaticLocation=p.AutoLocateCity,p.WeatherRefreshMinutes}
        };
    }
    internal static async Task Export(string path,object snapshot,CancellationToken ct=default)
    {
        var events=await Task.Run(ReadCrashEvents,ct);
        await DiagnosticArchive.Write(path,Preferences.DataPath,snapshot,events,ct);
    }
    private static object ReadCrashEvents()
    {
        try{
            var query=new EventLogQuery("Application",PathType.LogName,"*[System[(EventID=1000 or EventID=1002) and TimeCreated[timediff(@SystemTime) <= 86400000]]]"){ReverseDirection=true};
            using var reader=new EventLogReader(query);var results=new List<object>();
            for(var i=0;i<512;i++){
                using var record=reader.ReadEvent();if(record==null)break;
                var xml=XElement.Parse(record.ToXml());XNamespace ns=xml.Name.Namespace;
                var data=xml.Descendants(ns+"Data").Where(x=>x.Attribute("Name")!=null).GroupBy(x=>(string)x.Attribute("Name")!,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First().Value,StringComparer.OrdinalIgnoreCase);
                data.TryGetValue("AppName",out var app);app=Path.GetFileName(app);
                if(!string.Equals(app,"LumiBar.exe",StringComparison.OrdinalIgnoreCase)&&!string.Equals(app,"explorer.exe",StringComparison.OrdinalIgnoreCase))continue;
                string? Field(string key)=>data.TryGetValue(key,out var value)?value:null;
                results.Add(new{record.Id,record.TimeCreated,application=app,applicationVersion=Field("AppVersion"),module=Path.GetFileName(Field("ModuleName")),moduleVersion=Field("ModuleVersion"),exceptionCode=Field("ExceptionCode"),faultOffset=Field("FaultingOffset")});
                if(results.Count>=40)break;
            }
            return new{status="available",periodHours=24,events=results};
        }catch(Exception ex) when(ex is EventLogException or UnauthorizedAccessException or System.Security.SecurityException or System.Xml.XmlException){return new{status="unavailable",error=ex.GetType().Name,code=ex.HResult};}
    }
}
