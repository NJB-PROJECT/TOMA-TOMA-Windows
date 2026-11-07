using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
namespace Toma.Windows;
public sealed class PlayerState : IDisposable
{
    public Settings Settings
    {
        get;
    }
    public bool Showcase
    {
        get;
    }
    private double showcasePosition; public MediaService Media { get; } = new(); public AudioSpectrum Audio { get; } = new();
    private readonly LyricsClient client = new(); private CancellationTokenSource? lookup; private readonly MediaPlayer local = new(); private bool localMode, localPlaying; private string lastExternal = ""; private int offset;
    public Track? Track
    {
        get; private set;
    }
    public string Raw { get; private set; } = ""; public string Provider { get; private set; } = ""; public string Status { get; private set; } = "Putar musik untuk memulai.";
    public List<LyricLine> Lines { get; private set; } = new(); public List<LyricRecord> Choices { get; private set; } = new();
    public bool Playing => Showcase ? true : localMode ? localPlaying : Media.Playing; public double Position => Showcase ? showcasePosition : localMode ? local.Position.TotalMilliseconds : Media.Position;
    public ImageSource? Cover => Showcase ? ShowcaseExporter.Cover : localMode ? null : Media.Cover; public int Active => Lyrics.Active(Lines, Position + offset); public int Offset => offset;
    public event Action? Changed; public event Action? ChooseRequested;
    public PlayerState(bool showcase = false)
    {
        Showcase = showcase;
        Settings = showcase ? new() : Storage.Load();
        Media.Updated += OnMedia;
        SpectrumView.Audio = Audio;
        local.MediaEnded += (_, _) => { localPlaying = false; Changed?.Invoke(); };
        local.MediaFailed += (_, _) => { Status = "Format audio belum dapat dibuka Windows. Pilih file lain."; localPlaying = false; Changed?.Invoke(); };
    }
    public async Task Start()
    {
        if (Showcase)
            return;
        if (Settings.Spectrum)
            Audio.Start();
        await Media.Start();
    }
    private void OnMedia()
    {
        if (localMode)
            return;
        if (Media.Current == null)
        {
            if (Track != null)
            {
                lookup?.Cancel();
                Track = null;
                Raw = "";
                Lines = new();
                Choices = new();
            }
            Status = Media.Status;
            lastExternal = "";
            Changed?.Invoke();
            return;
        }
        if (Media.Current.Key != lastExternal)
        {
            lastExternal = Media.Current.Key;
            SetTrack(Media.Current);
            _ = Search();
        }
        Changed?.Invoke();
    }
    private void SetTrack(Track track)
    {
        lookup?.Cancel();
        Track = track;
        Choices = new();
        Raw = "";
        Lines = new();
        Provider = "";
        offset = 0;
        if (Settings.Songs.TryGetValue(track.Key, out var saved))
        {
            offset = saved.Offset;
            Raw = saved.Raw;
            Provider = saved.Source;
            Lines = Lyrics.Parse(Raw);
        }
        Status = Raw.Length > 0 ? "Lirik tersimpan · mengikuti pemutar" : "Mencari lirik otomatis…";
        Changed?.Invoke();
    }
    public async Task Search(bool force = false, bool versions = false)
    {
        var track = Track;
        if (track == null)
            return;
        if (!force && Lyrics.KeepCached(Raw, Provider))
            return;
        lookup?.Cancel();
        lookup = new();
        var cancel = lookup.Token;
        Status = versions ? "Mencari versi lirik lainnya…" : "Mencari lirik otomatis…";
        Changed?.Invoke();
        try
        {
            var result = await client.Find(track, cancel, versions);
            if (cancel.IsCancellationRequested || Track?.Key != track.Key)
                return;
            Choices = result;
            if (versions && result.Count > 0)
            {
                Status = "Pilih versi lirik yang sesuai.";
            }
            else if (Lyrics.ChooseAutomatic(result, track) is LyricRecord automatic)
            {
                Select(automatic);
            }
            else if (result.Count > 0)
            {
                Status = "Pilih versi lirik yang sesuai dengan lagu.";
                ChooseRequested?.Invoke();
            }
            else
                Status = "Lirik belum ditemukan. Periksa judul/artis atau impor LRC.";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!cancel.IsCancellationRequested) { Status = Raw.Length > 0 ? "Pencarian gagal · lirik tersimpan tetap tersedia" : "Layanan lirik belum tersedia. Coba lagi atau impor LRC."; FileLog.Write(e.Message); } }
        Changed?.Invoke();
    }
    public void Select(LyricRecord record)
    {
        if (Track == null || !Choices.Contains(record))
        {
            Status = "Lagu atau hasil pencarian sudah berubah. Buka pilihan versi terbaru.";
            Changed?.Invoke();
            return;
        }
        Raw = record.Raw;
        Provider = record.Id > 0 ? "LRCLIB" : "lyrics.ovh";
        Lines = Lyrics.Parse(Raw);
        Status = record.Instrumental ? "Versi ini ditandai instrumental." : Lines.Count > 0 && Lines[0].Time < 0 ? "Lirik tersedia tanpa timestamp; belum bisa mengikuti posisi otomatis." : "Lirik dipilih · auto-sync aktif";
        Save();
        Changed?.Invoke();
    }
    public void Import(string raw)
    {
        lookup?.Cancel();
        Raw = raw;
        Provider = "LRC lokal";
        Lines = Lyrics.Parse(raw);
        Status = Lines.Count > 0 && Lines[0].Time >= 0 ? "LRC diimpor · mengikuti pemutar" : "Teks diimpor tanpa timestamp";
        Save();
        Changed?.Invoke();
    }
    public void SearchAs(string title, string artist)
    {
        if (Track == null || string.IsNullOrWhiteSpace(title))
            return;
        SetTrack(new Track(title.Trim(), artist.Trim(), Track.Album, Track.Source, Track.Duration));
        _ = Search(true);
    }
    public void Adjust(int millis)
    {
        offset = Math.Clamp(offset + millis, -10000, 10000);
        Save();
        Changed?.Invoke();
    }
    private void Save()
    {
        if (Showcase || Track == null)
            return;
        Settings.Songs[Track.Key] = new()
        {
            Raw = Raw,
            Source = Provider,
            Offset = offset,
            Title = Track.Title,
            Artist = Track.Artist
        };
        while (Settings.Songs.Count > 100)
            Settings.Songs.Remove(Settings.Songs.Keys.First());
        Storage.Save(Settings);
    }
    public async Task Toggle()
    {
        if (localMode)
        {
            if (localPlaying)
                local.Pause();
            else
                local.Play();
            localPlaying = !localPlaying;
            Changed?.Invoke();
        }
        else
            await Media.Toggle();
    }
    public async Task Seek(double millis)
    {
        if (localMode)
            local.Position = TimeSpan.FromMilliseconds(Math.Clamp(millis, 0, Track?.Duration * 1000 ?? double.MaxValue));
        else
            await Media.Seek(millis);
    }
    public async Task Skip(bool next)
    {
        if (!localMode)
            await Media.Skip(next);
    }
    public void OpenLocal(string path)
    {
        localMode = true;
        lookup?.Cancel();
        local.Stop();
        localPlaying = false;
        local.Open(new Uri(path));
        EventHandler? opened = null;
        opened = (_, _) => { local.MediaOpened -= opened; double seconds = local.NaturalDuration.HasTimeSpan ? local.NaturalDuration.TimeSpan.TotalSeconds : 0; SetTrack(new Track(System.IO.Path.GetFileNameWithoutExtension(path), "", "", "File lokal", seconds)); _ = Search(); };
        local.MediaOpened += opened;
    }
    public void FollowWindows()
    {
        local.Stop();
        localPlaying = false;
        localMode = false;
        lastExternal = "";
        OnMedia();
    }
    public void SetSpectrum(bool enabled)
    {
        Settings.Spectrum = enabled;
        if (enabled)
            Audio.Start();
        else
            Audio.Stop();
        Storage.Save(Settings);
        Changed?.Invoke();
    }
    internal void ShowcaseFrame(double seconds)
    {
        if (!Showcase)
            return;
        showcasePosition = seconds * 1000;
        if (Track == null)
        {
            Track = new("A Little Light", "TOMA Studio · demo original", "", "Showcase", 18);
            Raw = "[00:00]Music, made a little more yours.\n[00:03]A little light across the room.\n[00:06]Long lyric lines wrap naturally, so every word has room to breathe.\n[00:10]Your colors. Your corner of the screen.\n[00:14]Keep the music close.";
            Lines = Lyrics.Parse(Raw);
            Provider = "DEMO · original sample text";
            Status = "Showcase · generated PCM, no microphone";
        }
        Audio.ShowcaseFrame(seconds);
        Changed?.Invoke();
    }
    public void Dispose()
    {
        lookup?.Cancel();
        Media.Dispose();
        Audio.Dispose();
        local.Close();
        if (!Showcase)
            Storage.Save(Settings);
    }
}
