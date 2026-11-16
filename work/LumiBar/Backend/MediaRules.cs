using System.Text;
using System.Security.Cryptography;

namespace LumiBar;

internal static class MediaRules
{
    public static int Select(IReadOnlyList<(bool Playing, bool Current, bool Previous)> candidates)
    {
        if (candidates.Count == 0) return -1;
        for (var i = 0; i < candidates.Count; i++) if (candidates[i].Playing && candidates[i].Current) return i;
        for (var i = 0; i < candidates.Count; i++) if (candidates[i].Playing && candidates[i].Previous) return i;
        for (var i = 0; i < candidates.Count; i++) if (candidates[i].Playing) return i;
        for (var i = 0; i < candidates.Count; i++) if (candidates[i].Previous) return i;
        for (var i = 0; i < candidates.Count; i++) if (candidates[i].Current) return i;
        return 0;
    }

    public static string TrackKey(string source, string title, string artist, string album, double duration)
    {
        // Length-prefixed metadata avoids delimiter collisions; duration separates studio/live recordings.
        var fields = new[] { source, title, artist, album, Math.Round(Math.Max(0, duration)).ToString(System.Globalization.CultureInfo.InvariantCulture) };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(fields.Select(f => $"{f.Length}:{f}")))));
    }

    public static double Position(double position, double duration, bool playing, double rate, DateTimeOffset updated, DateTimeOffset now)
    {
        if (!double.IsFinite(position) || !double.IsFinite(duration) || duration <= 0) return 0;
        // Missing/stale timestamps must not jump a newly discovered track to its end.
        var age = (now - updated).TotalSeconds;
        var elapsed = playing && age >= 0 && age <= 600 && double.IsFinite(rate) ? age * Math.Clamp(rate, .1, 4) : 0;
        return Math.Clamp(position + elapsed, 0, duration);
    }

    public static bool LyricsMatch(string title, string artist, double duration, string foundTitle, string foundArtist, double foundDuration)
    {
        static string Normalize(string s) => new(s.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return duration > 0 && double.IsFinite(foundDuration) && Math.Abs(foundDuration - duration) <= 3
            && Normalize(title) == Normalize(foundTitle) && Normalize(artist) == Normalize(foundArtist);
    }
}
