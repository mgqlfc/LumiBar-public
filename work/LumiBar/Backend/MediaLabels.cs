namespace LumiBar;
internal static class MediaLabels
{
    internal static string Player(string? source,string? displayName=null)
    {
        if(string.IsNullOrWhiteSpace(source))return "播放器";
        var id=source.ToLowerInvariant();
        foreach(var (match,label) in new[]{("cloudmusic","网易云音乐"),("netease","网易云音乐"),("qqmusic","QQ 音乐"),("spotify","Spotify"),("applemusic","Apple Music"),("itunes","iTunes"),("msedge","Microsoft Edge"),("chrome","Chrome"),("firefox","Firefox"),("zunemusic","Windows 媒体播放器"),("foobar","foobar2000"),("musicbee","MusicBee"),("potplayer","PotPlayer"),("aimp","AIMP")})if(id.Contains(match))return label;
        if(!string.IsNullOrWhiteSpace(displayName))return displayName;
        var file=source.Split('!')[0].Split(['\\','/']).Last();if(file.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))file=file[..^4];
        if(file.Contains('_'))file=file.Split('_')[0];return file;
    }
    internal static string Lyrics(string status,bool hasLines)=>status.StartsWith("本地",StringComparison.Ordinal)?"本地歌词":status.Contains("LRCLIB",StringComparison.OrdinalIgnoreCase)?"LRCLIB 歌词":status.Contains("正在")?"匹配歌词中":status.Contains("纯音乐")?"纯音乐":hasLines?"歌词":"暂无歌词";
}
