using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.IO;
using System.Collections.Generic;
using Windows.Media.Control;
using Windows.Storage.Streams;
namespace Toma.Windows;
public sealed class MediaService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? manager; private GlobalSystemMediaTransportControlsSession? session;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) }; private bool busy; private double position; private long received; private double speed = 1;
    public Track? Current
    {
        get; private set;
    }
    public bool Playing
    {
        get; private set;
    }
    public BitmapSource? Cover
    {
        get; private set;
    }
    public string Status { get; private set; } = "Mendeteksi pemutar Windows…";
    public string SelectedSource { get; set; } = ""; public List<string> Sources { get; private set; } = new(); public event Action? Updated;
    public string TimingInfo { get; private set; } = "";
    public double Position => Math.Clamp(position + (Playing ? (Environment.TickCount64 - received) * speed : 0), 0, Current?.Duration > 0 ? Current.Duration * 1000 : double.MaxValue);
    public async Task Start()
    {
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            timer.Tick += async (_, _) => await Poll();
            timer.Start();
            await Poll();
        }
        catch (Exception e) { Status = "Sesi media Windows belum tersedia. Gunakan file audio lokal."; FileLog.Write(e.Message); Updated?.Invoke(); }
    }
    private async Task Poll()
    {
        if (busy || manager == null)
            return;
        busy = true;
        try
        {
            var sessions = manager.GetSessions();
            Sources = sessions.Select(s => s.SourceAppUserModelId).Distinct().ToList();
            session = SelectedSource.Length > 0 ? sessions.FirstOrDefault(s => s.SourceAppUserModelId == SelectedSource) : sessions.FirstOrDefault(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) ?? manager.GetCurrentSession();
            if (session == null)
            {
                Current = null;
                Cover = null;
                Playing = false;
                Status = "Putar lagu di Spotify, YouTube Music, atau pemutar Windows.";
                Updated?.Invoke();
                return;
            }
            var media = await session.TryGetMediaPropertiesAsync();
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            Playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            speed = playback.PlaybackRate ?? 1;
            double age = (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalMilliseconds;
            position = MediaClock.Estimate(timeline.Position.TotalMilliseconds, age, Playing, speed, timeline.EndTime.TotalMilliseconds);
            received = Environment.TickCount64;
            TimingInfo = $"rawMs={timeline.Position.TotalMilliseconds}; updateAgeMs={Math.Round(age)}; rate={speed}";
            var track = new Track(media.Title ?? "", media.Artist ?? "", media.AlbumTitle ?? "", session.SourceAppUserModelId, Math.Max(0, timeline.EndTime.TotalSeconds));
            if (Current?.Key != track.Key)
            {
                Cover = null;
                Current = track;
                if (media.Thumbnail != null)
                {
                    try
                    {
                        using var stream = await media.Thumbnail.OpenReadAsync();
                        if (stream.Size <= 4000000)
                        {
                            using var reader = new DataReader(stream);
                            uint length = await reader.LoadAsync((uint)stream.Size);
                            var bytes = new byte[length];
                            reader.ReadBytes(bytes);
                            Cover = Images.Decode(bytes);
                        }
                    }
                    catch (Exception e) { FileLog.Write("Cover: " + e.Message); }
                }
            }
            else
                Current = track;
            Status = Playing ? "Auto-sync aktif · posisi dari pemutar Windows" : "Pemutar dijeda";
            Updated?.Invoke();
        }
        catch (Exception e) { Status = "Metadata pemutar belum dapat dibaca."; FileLog.Write("Media: " + e.Message); Updated?.Invoke(); }
        finally { busy = false; }
    }
    public async Task Toggle()
    {
        try
        {
            if (session != null)
                await session.TryTogglePlayPauseAsync();
            await Poll();
        }
        catch (Exception e) { FileLog.Write(e.Message); }
    }
    public async Task Skip(bool next)
    {
        try
        {
            if (session != null)
            {
                if (next)
                    await session.TrySkipNextAsync();
                else
                    await session.TrySkipPreviousAsync();
            }
            await Poll();
        }
        catch (Exception e) { FileLog.Write(e.Message); }
    }
    public async Task Seek(double ms)
    {
        try
        {
            if (session != null)
                await session.TryChangePlaybackPositionAsync(TimeSpan.FromMilliseconds(Math.Max(0, ms)).Ticks);
            await Poll();
        }
        catch (Exception e) { FileLog.Write(e.Message); }
    }
    public void Dispose() => timer.Stop();
}
public static class Images
{
    public static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 512;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}
