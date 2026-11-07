using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
namespace Toma.Windows;
public sealed class LyricsClient
{
    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new DurationConverter() } };
    private readonly HttpClient http;
    static LyricsClient()
    {
        Shared.DefaultRequestHeaders.UserAgent.ParseAdd("TomaTomaWindows/1.3 (desktop-lyrics-companion)");
    }
    public LyricsClient(HttpClient? transport = null)
    {
        http = transport ?? Shared;
    }
    private static string Escape(string value) => Uri.EscapeDataString(value);
    public async Task<List<LyricRecord>> Find(Track track, CancellationToken cancel, bool allVersions = false)
    {
        track = track with
        {
            Title = Lyrics.CleanTitle(track.Title),
            Artist = Lyrics.CleanArtist(track.Artist)
        };
        var records = new List<LyricRecord>();
        Exception? failure = null;
        int successful = 0;
        async Task Read(string path, bool single = false)
        {
            try
            {
                using var response = await GetWithRetry("https://lrclib.net" + path, cancel);
                if (single && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return;
                response.EnsureSuccessStatusCode();
                string body = await response.Content.ReadAsStringAsync(cancel);
                successful++;
                if (single)
                {
                    var record = JsonSerializer.Deserialize<LyricRecord>(body, Json);
                    if (record != null)
                        records.Add(record);
                }
                else
                    records.AddRange(JsonSerializer.Deserialize<List<LyricRecord>>(body, Json) ?? new());
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException) { failure = e; }
        }
        if (!allVersions && track.Artist.Length > 0)
        {
            await Read($"/api/get?track_name={Escape(track.Title)}&artist_name={Escape(track.Artist)}" + (track.Duration > 0 ? "&duration=" + Math.Round(track.Duration) : ""), true);
            var exact = records.FirstOrDefault(r => Lyrics.Exact(r, track) && (r.Instrumental || !string.IsNullOrWhiteSpace(r.SyncedLyrics)));
            if (exact != null)
                return new() { exact };
        }
        string titleQuery = "/api/search?track_name=" + Escape(track.Title);
        string lead = Lyrics.Artists(track.Artist).FirstOrDefault() ?? "";
        await Read(titleQuery + (lead.Length > 0 ? "&artist_name=" + Escape(lead) : ""));
        // Artist order and localized conjunctions vary across Windows/browser metadata.
        // A plain exact record must not prevent searching for an available timed version.
        if (lead.Length > 0 && !records.Any(r => Lyrics.Exact(r, track) && !string.IsNullOrWhiteSpace(r.SyncedLyrics)))
            await Read(titleQuery);
        // Free-text indexing can find records omitted by structured title/artist filters.
        if (!records.Any(r => Lyrics.Exact(r, track) && !string.IsNullOrWhiteSpace(r.SyncedLyrics)))
        {
            foreach (string artist in Lyrics.Artists(track.Artist).Take(3).DefaultIfEmpty(""))
            {
                await Read("/api/search?q=" + Escape(track.Title + " " + artist));
                if (!allVersions && records.Any(r => Lyrics.Exact(r, track) && !string.IsNullOrWhiteSpace(r.SyncedLyrics)))
                    break;
            }
        }
        records = records.Where(r => r.Instrumental || r.Raw.Length > 0).GroupBy(r => r.Id > 0 ? "id:" + r.Id : r.TrackName + "|" + r.ArtistName + "|" + r.Raw).Select(g => g.First())
         .OrderByDescending(r => Lyrics.Normalize(r.TrackName) == Lyrics.Normalize(track.Title))
         .ThenByDescending(r => Lyrics.ArtistMatches(r.ArtistName, track.Artist))
         .ThenByDescending(r => Lyrics.Exact(r, track))
         .ThenByDescending(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics))
         .ThenBy(r => track.Duration > 0 ? Math.Abs(r.Duration - track.Duration) : 0).Take(40).ToList();
        if (records.Count == 0 && track.Artist.Length > 0)
        {
            try
            {
                using var response = await http.GetAsync($"https://api.lyrics.ovh/v1/{Escape(track.Artist)}/{Escape(track.Title)}", cancel);
                if (response.IsSuccessStatusCode)
                {
                    using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
                    if (data.RootElement.TryGetProperty("lyrics", out var lyrics) && lyrics.GetString() is string text && text.Length > 0)
                        records.Add(new()
                        {
                            TrackName = track.Title,
                            ArtistName = track.Artist,
                            Duration = track.Duration,
                            PlainLyrics = text
                        });
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException) { failure ??= e; }
        }
        if (records.Count == 0 && successful == 0 && failure != null)
            throw new HttpRequestException("Layanan lirik belum dapat dihubungi.", failure);
        return records;
    }
    private async Task<HttpResponseMessage> GetWithRetry(string url, CancellationToken cancel)
    {
        for (int attempt = 0; ; attempt++)
        {
            var response = await http.GetAsync(url, cancel);
            if (attempt >= 1 || ((int)response.StatusCode < 500 && response.StatusCode != System.Net.HttpStatusCode.RequestTimeout))
                return response;
            response.Dispose();
            await Task.Delay(350, cancel);
        }
    }
    // LRCLIB community records may omit duration; do not discard the entire result list.
    private sealed class DurationConverter : JsonConverter<double>
    {
        public override bool HandleNull => true;
        public override double Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return 0;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out double value))
                return double.IsFinite(value) && value >= 0 ? value : 0;
            if (reader.TokenType == JsonTokenType.String && double.TryParse(reader.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                return double.IsFinite(value) && value >= 0 ? value : 0;
            throw new JsonException("Durasi lirik tidak valid.");
        }
        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }
}
