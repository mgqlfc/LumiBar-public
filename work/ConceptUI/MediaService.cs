using System.Net.Http;
using System.Text.Json;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Control;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace LumiBar;
internal record PlaybackCapabilities(bool Toggle,bool Previous,bool Next,bool Seek,bool Shuffle=false,bool Repeat=false);
internal record Track(string Title,string Artist,string Album,string Source,bool Playing,double Position,double Duration,byte[]? Cover,Color Accent,string Key,PlaybackCapabilities? Controls=null,bool? ShuffleEnabled=null,string? RepeatMode=null,Color? SecondaryAccent=null);
internal sealed class MediaService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    internal event Action? Changed;
    internal GlobalSystemMediaTransportControlsSession? Session;
    private string _coverKey=""; private byte[]? _cover; private Color _accent=Color.FromArgb(255,83,151,157);
    public async Task<Track?> Read()
    {
        if(_manager==null){_manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();_manager.SessionsChanged+=OnChanged;_manager.CurrentSessionChanged+=OnChanged;}
        var sessions=_manager.GetSessions();
        var current=_manager.GetCurrentSession();var previous=Session;
        var index=MediaRules.Select(sessions.Select(s=>(s.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,s==current,s==previous)).ToList());
        SelectSession(index<0?null:sessions[index]);
        if(Session==null){_coverKey="";_cover=null;return null;}
        var props=await Session.TryGetMediaPropertiesAsync();
        if(string.IsNullOrWhiteSpace(props.Title))return null;
        var play=Session.GetPlaybackInfo();var timeline=Session.GetTimelineProperties();
        var duration=Math.Max(0,(timeline.EndTime-timeline.StartTime).TotalSeconds);
        var key=MediaRules.TrackKey(Session.SourceAppUserModelId,props.Title,props.Artist,props.AlbumTitle,duration);
        if(key!=_coverKey){
            _cover=null;_accent=Color.FromArgb(255,83,151,157);
            if(props.Thumbnail!=null)try{
                using var stream=await props.Thumbnail.OpenReadAsync();
                if(stream.Size<=8*1024*1024){using var reader=new DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size);_cover=new byte[(int)stream.Size];reader.ReadBytes(_cover);_accent=await Palette(_cover);}
            }catch{ }
            _coverKey=key;
        }
        var playing=play.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var position=MediaRules.Position((timeline.Position-timeline.StartTime).TotalSeconds,duration,playing,play.PlaybackRate??1,timeline.LastUpdatedTime,DateTimeOffset.Now);
        var controls=play.Controls;
        return new(props.Title,props.Artist,props.AlbumTitle,Session.SourceAppUserModelId,playing,position,duration,_cover,_accent,key,new(controls.IsPlayPauseToggleEnabled,controls.IsPreviousEnabled,controls.IsNextEnabled,controls.IsPlaybackPositionEnabled,controls.IsShuffleEnabled,controls.IsRepeatEnabled),play.IsShuffleActive,play.AutoRepeatMode?.ToString());
    }
    public async Task Toggle(){var s=Session;if(s==null)return;if(!s.GetPlaybackInfo().Controls.IsPlayPauseToggleEnabled||!await s.TryTogglePlayPauseAsync())throw new InvalidOperationException("播放器暂不支持播放 / 暂停控制。");}
    public async Task Next(){var s=Session;if(s==null)return;if(!s.GetPlaybackInfo().Controls.IsNextEnabled||!await s.TrySkipNextAsync())throw new InvalidOperationException("播放器暂不支持下一首。");}
    public async Task Previous(){var s=Session;if(s==null)return;if(!s.GetPlaybackInfo().Controls.IsPreviousEnabled||!await s.TrySkipPreviousAsync())throw new InvalidOperationException("播放器暂不支持上一首。");}
    public async Task SetShuffle(bool enabled){var s=Session;if(s==null)throw new InvalidOperationException("当前没有媒体会话。");if(!s.GetPlaybackInfo().Controls.IsShuffleEnabled||!await s.TryChangeShuffleActiveAsync(enabled))throw new InvalidOperationException("播放器暂不支持随机播放控制。");}
    public async Task SetRepeat(string mode){if(mode is not ("None" or "Track" or "List"))throw new ArgumentException("循环模式有误。");var s=Session;if(s==null)throw new InvalidOperationException("当前没有媒体会话。");if(!s.GetPlaybackInfo().Controls.IsRepeatEnabled||!await s.TryChangeAutoRepeatModeAsync(Enum.Parse<Windows.Media.MediaPlaybackAutoRepeatMode>(mode)))throw new InvalidOperationException("播放器暂不支持循环模式控制。");}
    public async Task Seek(double sec){var s=Session;if(s==null||!double.IsFinite(sec))return;var t=s.GetTimelineProperties();var target=t.StartTime+TimeSpan.FromSeconds(Math.Clamp(sec,0,Math.Max(0,(t.EndTime-t.StartTime).TotalSeconds)));if(!s.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled||!await s.TryChangePlaybackPositionAsync(target.Ticks))throw new InvalidOperationException("播放器暂不支持调整播放进度。");}
    private void OnChanged(object sender,object args)=>Changed?.Invoke();
    private void SelectSession(GlobalSystemMediaTransportControlsSession? next)
    {
        if(Session==next)return;
        if(Session!=null){Session.MediaPropertiesChanged-=OnChanged;Session.PlaybackInfoChanged-=OnChanged;Session.TimelinePropertiesChanged-=OnChanged;}
        Session=next;
        if(Session!=null){Session.MediaPropertiesChanged+=OnChanged;Session.PlaybackInfoChanged+=OnChanged;Session.TimelinePropertiesChanged+=OnChanged;}
    }
    public void Dispose(){SelectSession(null);if(_manager!=null){_manager.SessionsChanged-=OnChanged;_manager.CurrentSessionChanged-=OnChanged;}_manager=null;Changed=null;}
    internal static async Task<Color> Palette(byte[] bytes)
    {
        using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}
        stream.Seek(0);var decoder=await BitmapDecoder.CreateAsync(stream);
        var pixels=(await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,new BitmapTransform{ScaledWidth=32,ScaledHeight=32},ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage)).DetachPixelData();
        var bins=new Dictionary<int,(double r,double g,double b,double score,int count)>();
        for(int i=0;i+3<pixels.Length;i+=4){
            var r=pixels[i];var g=pixels[i+1];var b=pixels[i+2];var max=Math.Max(r,Math.Max(g,b));var min=Math.Min(r,Math.Min(g,b));
            if(pixels[i+3]<180||max<45||min>225||max-min<25)continue;
            var id=(r/40<<8)|(g/40<<4)|(b/40);var v=bins.GetValueOrDefault(id);
            bins[id]=(v.r+r,v.g+g,v.b+b,v.score+(max-min)/255d+.2,v.count+1);
        }
        if(bins.Count==0)return Color.FromArgb(255,83,151,157);
        var best=bins.Values.MaxBy(v=>v.score);return Color.FromArgb(255,(byte)(best.r/best.count),(byte)(best.g/best.count),(byte)(best.b/best.count));
    }
}
internal sealed class LyricsService
{
    private static string CacheFile(string key,bool imported=false)=>Path.Combine(Preferences.DataPath,imported?"lyrics-imported":"lyrics",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))+".lrc");
    public async Task<(List<LyricLine> lines,string status)> Get(Track track,CancellationToken ct,bool allowOnline=true)
    {
        var file=CacheFile(track.Key);
        var imported=CacheFile(track.Key,true);
        if(File.Exists(imported))return(Lrc.Parse(await File.ReadAllTextAsync(imported,ct)),"本地 LRC 歌词");
        if(File.Exists(file))return(Lrc.Parse(await File.ReadAllTextAsync(file,ct)),"已缓存 · LRCLIB / 本地歌词");
        if(!allowOnline)return([],"自动匹配已关闭，可导入 LRC");
        if(track.Duration<=0)return([],"播放器尚未提供歌曲时长，可导入 LRC 歌词");
        var url=$"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(track.Title)}&artist_name={Uri.EscapeDataString(track.Artist)}&album_name={Uri.EscapeDataString(track.Album)}&duration={track.Duration.ToString("F0",System.Globalization.CultureInfo.InvariantCulture)}";
        try{
            using var response=await WeatherService.Http.GetAsync(url,ct);
            if(response.StatusCode==System.Net.HttpStatusCode.NotFound)return([],"暂时没有匹配的歌词，可导入 LRC");
            response.EnsureSuccessStatusCode();using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));var root=doc.RootElement;
            if(!root.TryGetProperty("trackName",out var foundTitle)||!root.TryGetProperty("artistName",out var foundArtist)||!root.TryGetProperty("duration",out var dur)||!MediaRules.LyricsMatch(track.Title,track.Artist,track.Duration,foundTitle.GetString()??"",foundArtist.GetString()??"",dur.GetDouble()))return([],"歌词版本不匹配，可导入正确的 LRC");
            if(root.TryGetProperty("instrumental",out var inst)&&inst.ValueKind==JsonValueKind.True)return([],"纯音乐，静静听一会儿");
            var lrc=root.TryGetProperty("syncedLyrics",out var sync)?sync.GetString():null;
            if(!string.IsNullOrWhiteSpace(lrc)){var lines=Lrc.Parse(lrc);ct.ThrowIfCancellationRequested();try{AtomicStore.Write(file,lrc);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){App.Log("lyrics cache",ex);}return(lines,"同步歌词 · LRCLIB");}
            var plain=root.TryGetProperty("plainLyrics",out var text)?text.GetString():null;
            if(!string.IsNullOrWhiteSpace(plain))return(plain.Split('\n').Where(x=>x.Trim().Length>0).Select(x=>new LyricLine(double.PositiveInfinity,x.Trim())).ToList(),"文本歌词 · LRCLIB");
            return([],"暂时没有匹配的歌词，可导入 LRC");
        }catch(OperationCanceledException){throw;}catch(Exception ex) when(ex is HttpRequestException or JsonException){return([],"歌词暂时无法加载，可重试或导入 LRC");}
    }
    public Task Import(string key,string text){if(Lrc.Parse(text).Count==0)throw new InvalidOperationException("这个文件没有有效的 LRC 时间标签。");AtomicStore.Write(CacheFile(key,true),text);return Task.CompletedTask;}
}
