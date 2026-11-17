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
    private readonly Dictionary<string,string> _playerNames=[];
    internal string PlayerName(string? source)
    {
        if(string.IsNullOrWhiteSpace(source))return "播放器";
        if(_playerNames.TryGetValue(source,out var cached))return cached;
        string? display=null;try{display=Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(source)?.DisplayInfo.DisplayName;}catch(Exception ex) when(ex is ArgumentException or System.Runtime.InteropServices.COMException or FileNotFoundException or InvalidOperationException or UnauthorizedAccessException){}
        return _playerNames[source]=MediaLabels.Player(source,display);
    }
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    internal event Action? Changed;
    internal GlobalSystemMediaTransportControlsSession? Session;
    private readonly Dictionary<string,(byte[] Bytes,Color Primary,Color Secondary)> _artworkCache=[];
    private CancellationTokenSource? _artworkCts;private long _changeAt;
    private string _coverKey=""; private byte[]? _cover; private Color _accent=Color.FromArgb(255,83,151,157),_secondaryAccent=Color.FromArgb(255,115,169,174);
    public async Task<Track?> Read()
    {
        if(_manager==null){_manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();_manager.SessionsChanged+=OnChanged;_manager.CurrentSessionChanged+=OnChanged;}
        var sessions=_manager.GetSessions();
        var current=_manager.GetCurrentSession();var previous=Session;
        var index=MediaRules.Select(sessions.Select(s=>(s.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,s==current,s==previous)).ToList());
        SelectSession(index<0?null:sessions[index]);
        if(Session==null){_artworkCts?.Cancel();_coverKey="";_cover=null;return null;}
        var readAt=System.Diagnostics.Stopwatch.GetTimestamp();var session=Session;
        var props=await session.TryGetMediaPropertiesAsync();
        if(session!=Session)return null;
        if(string.IsNullOrWhiteSpace(props.Title))return null;
        var play=Session.GetPlaybackInfo();var timeline=Session.GetTimelineProperties();
        var duration=Math.Max(0,(timeline.EndTime-timeline.StartTime).TotalSeconds);
        var key=MediaRules.TrackKey(Session.SourceAppUserModelId,props.Title,props.Artist,props.AlbumTitle,duration);
        if(key!=_coverKey){
            _artworkCts?.Cancel();_artworkCts?.Dispose();_artworkCts=null;
            _coverKey=key;_cover=null;
            // Metadata can arrive before its thumbnail. Never pair a new song
            // with the previous song's colour while artwork is still loading.
            var fallback=CoverPalette.Extract([]);
            _accent=Color.FromArgb(255,fallback.Primary.R,fallback.Primary.G,fallback.Primary.B);
            _secondaryAccent=Color.FromArgb(255,fallback.Secondary.R,fallback.Secondary.G,fallback.Secondary.B);
            if(_artworkCache.TryGetValue(key,out var cached))(_cover,_accent,_secondaryAccent)=cached;
            else if(props.Thumbnail!=null){_artworkCts=new CancellationTokenSource(TimeSpan.FromSeconds(8));_=LoadArtwork(key,props.Thumbnail,_artworkCts.Token);}
            App.Trace("media-latency",$"metadata read={System.Diagnostics.Stopwatch.GetElapsedTime(readAt).TotalMilliseconds:F1}ms event={(_changeAt==0?-1:System.Diagnostics.Stopwatch.GetElapsedTime(_changeAt).TotalMilliseconds):F1}ms");
        }
        var playing=play.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var position=MediaRules.Position((timeline.Position-timeline.StartTime).TotalSeconds,duration,playing,play.PlaybackRate??1,timeline.LastUpdatedTime,DateTimeOffset.Now);
        var controls=play.Controls;
        return new(props.Title,props.Artist,props.AlbumTitle,Session.SourceAppUserModelId,playing,position,duration,_cover,_accent,key,new(controls.IsPlayPauseToggleEnabled,controls.IsPreviousEnabled,controls.IsNextEnabled,controls.IsPlaybackPositionEnabled,controls.IsShuffleEnabled,controls.IsRepeatEnabled),play.IsShuffleActive,play.AutoRepeatMode?.ToString(),_secondaryAccent);
    }
    private async Task LoadArtwork(string key,IRandomAccessStreamReference thumbnail,CancellationToken token)
    {
        var started=System.Diagnostics.Stopwatch.GetTimestamp();
        try{
            using var stream=await thumbnail.OpenReadAsync().AsTask(token);
            if(stream.Size==0||stream.Size>8*1024*1024)return;
            using var reader=new DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size).AsTask(token);
            var bytes=new byte[(int)stream.Size];reader.ReadBytes(bytes);
            var pair=await Palette(bytes);token.ThrowIfCancellationRequested();
            if(key!=_coverKey)return;
            if(_artworkCache.Count>=8)_artworkCache.Remove(_artworkCache.Keys.First());
            _artworkCache[key]=(bytes,pair.Primary,pair.Secondary);
            (_cover,_accent,_secondaryAccent)=(bytes,pair.Primary,pair.Secondary);
            App.Trace("media-latency",$"artwork {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}ms");
            Changed?.Invoke();
        }catch(OperationCanceledException){}catch(Exception ex){App.Log("media artwork",ex);}
    }
    public async Task Toggle(){var s=Session;if(s==null)return;if(!s.GetPlaybackInfo().Controls.IsPlayPauseToggleEnabled||!await s.TryTogglePlayPauseAsync())throw new InvalidOperationException("播放器暂不支持播放 / 暂停控制。");}
    public async Task Next(){var s=Session;if(s==null)return;if(!s.GetPlaybackInfo().Controls.IsNextEnabled||!await s.TrySkipNextAsync())throw new InvalidOperationException("播放器暂不支持下一首。");}
    public async Task Previous(){var s=Session;if(s==null)return;if(!s.GetPlaybackInfo().Controls.IsPreviousEnabled||!await s.TrySkipPreviousAsync())throw new InvalidOperationException("播放器暂不支持上一首。");}
    public async Task SetShuffle(bool enabled){var s=Session;if(s==null)throw new InvalidOperationException("当前没有媒体会话。");if(!s.GetPlaybackInfo().Controls.IsShuffleEnabled||!await s.TryChangeShuffleActiveAsync(enabled))throw new InvalidOperationException("播放器暂不支持随机播放控制。");}
    public async Task SetRepeat(string mode){if(mode is not ("None" or "Track" or "List"))throw new ArgumentException("循环模式有误。");var s=Session;if(s==null)throw new InvalidOperationException("当前没有媒体会话。");if(!s.GetPlaybackInfo().Controls.IsRepeatEnabled||!await s.TryChangeAutoRepeatModeAsync(Enum.Parse<Windows.Media.MediaPlaybackAutoRepeatMode>(mode)))throw new InvalidOperationException("播放器暂不支持循环模式控制。");}
    public async Task Seek(double sec){var s=Session;if(s==null||!double.IsFinite(sec))return;var t=s.GetTimelineProperties();var target=t.StartTime+TimeSpan.FromSeconds(Math.Clamp(sec,0,Math.Max(0,(t.EndTime-t.StartTime).TotalSeconds)));if(!s.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled||!await s.TryChangePlaybackPositionAsync(target.Ticks))throw new InvalidOperationException("播放器暂不支持调整播放进度。");}
    private void OnChanged(object sender,object args){_changeAt=System.Diagnostics.Stopwatch.GetTimestamp();Changed?.Invoke();}
    private void SelectSession(GlobalSystemMediaTransportControlsSession? next)
    {
        if(Session==next)return;
        if(Session!=null){Session.MediaPropertiesChanged-=OnChanged;Session.PlaybackInfoChanged-=OnChanged;Session.TimelinePropertiesChanged-=OnChanged;}
        Session=next;
        if(Session!=null){Session.MediaPropertiesChanged+=OnChanged;Session.PlaybackInfoChanged+=OnChanged;Session.TimelinePropertiesChanged+=OnChanged;}
    }
    public void Dispose(){_artworkCts?.Cancel();_artworkCts?.Dispose();SelectSession(null);if(_manager!=null){_manager.SessionsChanged-=OnChanged;_manager.CurrentSessionChanged-=OnChanged;}_manager=null;Changed=null;}
    internal static async Task<(Color Primary,Color Secondary)> Palette(byte[] bytes)
    {
        using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}
        stream.Seek(0);var decoder=await BitmapDecoder.CreateAsync(stream);
        var pixels=(await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,new BitmapTransform{ScaledWidth=32,ScaledHeight=32},ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage)).DetachPixelData();
        var pair=CoverPalette.Extract(pixels);return (Color.FromArgb(255,pair.Primary.R,pair.Primary.G,pair.Primary.B),Color.FromArgb(255,pair.Secondary.R,pair.Secondary.G,pair.Secondary.B));
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
        if(File.Exists(file))return(Lrc.Parse(await File.ReadAllTextAsync(file,ct)),"LRCLIB 歌词 · 缓存");
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
