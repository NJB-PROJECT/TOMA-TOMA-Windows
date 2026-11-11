using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace Toma.Windows;
public sealed record LyricLine(long Time, string Text)
{
    public string Latin { get; init; } = "";
    public string LatinNote { get; init; } = "";
}
public sealed class LyricRecord
{
    public int Id
    {
        get; set;
    }
    public string TrackName { get; set; } = ""; public string ArtistName { get; set; } = ""; public string AlbumName { get; set; } = "";
    public double Duration
    {
        get; set;
    }
    public bool Instrumental
    {
        get; set;
    }
    public string? SyncedLyrics
    {
        get; set;
    }
    public string? PlainLyrics
    {
        get; set;
    }
    public string Raw => !string.IsNullOrWhiteSpace(SyncedLyrics) ? SyncedLyrics : PlainLyrics ?? "";
    public string Label => $"{TrackName} · {ArtistName}"; public string Detail => $"{(!string.IsNullOrWhiteSpace(SyncedLyrics) ? "Sinkron" : "Teks tanpa timestamp")} · {TimeSpan.FromSeconds(Math.Max(0, Duration)):m\\:ss} · {AlbumName}";
}
public sealed record Track(string Title, string Artist, string Album, string Source, double Duration)
{
    public string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Source + "|" + Title + "|" + Artist)));
}
public static class Lyrics
{
    private static readonly Regex Stamp = new(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);
    public static List<LyricLine> Parse(string raw)
    {
        var result = new List<LyricLine>();
        long offset = 0;
        var off = Regex.Match(raw, @"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase);
        if (off.Success)
            long.TryParse(off.Groups[1].Value, out offset);
        foreach (string line in raw.Replace("\r", "").Split('\n'))
        {
            var matches = Stamp.Matches(line);
            if (matches.Count == 0)
                continue;
            string text = Stamp.Replace(line, "").Trim();
            foreach (Match m in matches)
            {
                int seconds = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (seconds > 59)
                    continue;
                string fraction = m.Groups[3].Value.PadRight(3, '0');
                long time = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60000L + seconds * 1000 + int.Parse(fraction, CultureInfo.InvariantCulture) + offset;
                result.Add(new(Math.Max(0, time), text.Length == 0 ? "♪" : text));
            }
        }
        if (result.Count == 0)
            return raw.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s) && !Regex.IsMatch(s, @"^\[\w+:")).Select(s => new LyricLine(-1, s.Trim())).ToList();
        return result.OrderBy(l => l.Time).ToList();
    }
    public static int Active(IReadOnlyList<LyricLine> lines, double position)
    {
        int low = 0, high = lines.Count - 1, answer = -1;
        if (lines.Count == 0 || lines[0].Time < 0)
            return -1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (lines[mid].Time <= position)
            {
                answer = mid;
                low = mid + 1;
            }
            else
                high = mid - 1;
        }
        return answer;
    }
    public static string Normalize(string s) => Regex.Replace(s.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[^\p{L}\p{Nd}]", "");
    public static string CleanTitle(string title) => Regex.Replace(title, @"\s*[\[(](?:official(?: music)? (?:video|audio)|lyrics?(?: video)?|visuali[sz]er|hd|4k)[\])]", "", RegexOptions.IgnoreCase).Trim();
    public static string CleanArtist(string artist) => Regex.Replace(artist, @"(?:\s*-\s*Topic|VEVO)$", "", RegexOptions.IgnoreCase).Trim();
    public static string[] Artists(string value) => Regex.Split(Regex.Replace(CleanArtist(value).Trim(), @"([,;&])\s*(?:and|dan)\s+", "$1", RegexOptions.IgnoreCase), @"\s*(?:,|;|&|\s+(?:and|dan|feat\.?|ft\.?|with)\s+)\s*", RegexOptions.IgnoreCase).Where(a => Normalize(a).Length > 0).ToArray();
    public static bool ArtistMatches(string left, string right) => Normalize(left).Length > 0 && Normalize(right).Length > 0 && (Normalize(left) == Normalize(right) || Artists(left).Any(a => Artists(right).Any(b => Normalize(a) == Normalize(b))));
    public static bool Exact(LyricRecord record, Track track) => Normalize(CleanTitle(record.TrackName)) == Normalize(CleanTitle(track.Title)) && ArtistMatches(record.ArtistName, track.Artist) && (track.Duration <= 0 || record.Duration > 0 && Math.Abs(record.Duration - track.Duration) <= 3);
    public static LyricRecord? ChooseAutomatic(IEnumerable<LyricRecord> records, Track track)
    {
        var matches = records.Where(r => Exact(r, track)).ToList();
        if (matches.Count == 0)
            return null;
        if (track.Duration <= 0 && (matches.Select(r => r.Instrumental).Distinct().Count() > 1 || matches.Max(r => r.Duration) - matches.Min(r => r.Duration) > 3))
            return null;
        return matches.OrderByDescending(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics)).ThenBy(r => track.Duration > 0 ? Math.Abs(r.Duration - track.Duration) : 0).First();
    }
    public static bool KeepCached(string raw, string provider)
    {
        var lines = Parse(raw);
        return lines.Count > 0 && (lines[0].Time >= 0 || provider == "LRC lokal");
    }
}
public static class MediaClock
{
    public static double Estimate(double rawMs, double ageMs, bool playing, double rate, double durationMs)
    {
        double position = rawMs + (playing && ageMs >= 0 && ageMs <= 86400000 ? ageMs * Math.Clamp(rate, .1, 8) : 0);
        return Math.Clamp(position, 0, durationMs > 0 ? durationMs : double.MaxValue);
    }
}
