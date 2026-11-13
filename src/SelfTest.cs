using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace Toma.Windows;
public static class SelfTest
{
    public static int Run(string output)
    {
        var results = new List<object>();
        int failed = 0;
        void Test(string name, Func<bool> check)
        {
            try
            {
                bool pass = check();
                if (!pass)
                    failed++;
                results.Add(new
                {
                    name,
                    pass
                });
            }
            catch (Exception e) { failed++; results.Add(new { name, pass = false, error = e.Message }); }
        }
        Test("LRC pecahan dan offset", () => Lyrics.Parse("[offset:250]\n[01:02.5] contoh")[0].Time == 62750);
        Test("Multiple timestamp", () => Lyrics.Parse("[00:01.20][00:03.00] contoh").Count == 2);
        Test("Teks biasa tidak diberi waktu buatan", () => Lyrics.Parse("satu\ndua").All(l => l.Time == -1));
        Test("Baris aktif sesuai posisi", () => Lyrics.Active(Lyrics.Parse("[00:01] satu\n[00:03] dua"), 2999) == 0);
        Test("Sebelum lirik mulai", () => Lyrics.Active(Lyrics.Parse("[00:01] satu"), 0) == -1);
        Test("Timestamp diurutkan", () => Lyrics.Parse("[00:03] tiga\n[00:01] satu")[0].Text == "satu");
        Test("Teks panjang tidak dipotong", () => Lyrics.Parse("[00:01] " + new string('a', 800))[0].Text.Length == 800);
        Test("Versi berbeda durasi tidak otomatis", () => !Lyrics.Exact(new() { TrackName = "Contoh", ArtistName = "Artis", Duration = 90 }, new("Contoh", "Artis", "", "test", 120)));
        Test("Metadata tepat", () => Lyrics.Exact(new() { TrackName = "Contoh!", ArtistName = "Artis", Duration = 120 }, new("Contoh", "Artis", "", "test", 122)));
        float[] Measure(int frequency, double amplitude)
        {
            var fft = new SpectrumEngine();
            for (int i = 0; i < 4800; i++)
                fft.Add(amplitude * Math.Sin(2 * Math.PI * frequency * i / 48000), 48000);
            return fft.Levels;
        }
        int Peak(float[] levels) => Array.IndexOf(levels, levels.Max());
        Test("Bass 80 Hz di band 63–100 Hz", () => Peak(Measure(80, .4)) == 2);
        Test("Treble 9 kHz di band 6.3–12 kHz", () => Peak(Measure(9000, .4)) == 10);
        Test("Hening tidak membuat demo", () => Measure(1000, 0).All(x => x == 0));
        Test("Amplitudo pelan tidak di-boost otomatis", () => Measure(1500, .4)[8] > Measure(1500, .004)[8] + .4);
        float[] ExpandedMeasure(int frequency, double amplitude)
        {
            var fft = new SpectrumEngine();
            for (int i = 0; i < 9600; i++)
                fft.Add(amplitude * Math.Sin(2 * Math.PI * frequency * i / 48000), 48000);
            return fft.ExpandedLevels;
        }
        Test("Kapsul 12 band dan kartu besar 22 band", () => Measure(1000, .4).Length == 12 && ExpandedMeasure(1000, .4).Length == 22);
        Test("Sub-bass 30 Hz di sisi kiri", () => Peak(ExpandedMeasure(30, .4)) == 0);
        Test("Bass 90 Hz punya band tersendiri", () => Peak(ExpandedMeasure(90, .4)) == 3);
        Test("Low-mid 240 Hz punya band tersendiri", () => Peak(ExpandedMeasure(240, .4)) == 6);
        Test("Mid 950 Hz punya band tersendiri", () => Peak(ExpandedMeasure(950, .4)) == 11);
        Test("Presence 1.8 kHz punya band tersendiri", () => Peak(ExpandedMeasure(1800, .4)) == 13);
        Test("Treble 10 kHz di sisi kanan", () => Peak(ExpandedMeasure(10000, .4)) == 19);
        Test("High treble 17 kHz di band terakhir", () => Peak(ExpandedMeasure(17000, .4)) == 21);
        Test("22 band hening tanpa batang demo", () => ExpandedMeasure(1000, 0).All(x => x == 0));
        Test("Lirik panjang kapsul membungkus dan menambah tinggi", () =>
        {
            var state = new PlayerState();
            var window = new IslandWindow(state, () => { });
            try
            {
                window.Capsule.Width = 318;
                window.MiniLine.Text = string.Join(" ", Enumerable.Repeat("contoh lirik panjang sampai akhir", 6));
                window.Capsule.Measure(new System.Windows.Size(318, double.PositiveInfinity));
                double height = window.Capsule.DesiredSize.Height;
                window.Capsule.Arrange(new System.Windows.Rect(0, 0, 318, height));
                return window.MiniLine.TextWrapping == System.Windows.TextWrapping.Wrap && window.MiniLine.TextTrimming == System.Windows.TextTrimming.None && height > 70 && window.MiniLine.ActualHeight > 40 && height >= window.MiniLine.ActualHeight + window.MiniTitle.ActualHeight;
            }
            finally { window.Close(); }
        });
        Test("Offset negatif ditahan di nol", () => Lyrics.Parse("[offset:-5000]\n[00:01] contoh")[0].Time == 0);
        Test("Timeline browser tetap maju setelah dua menit", () => MediaClock.Estimate(0, 185000, true, 1, 240000) == 185000);
        Test("Pause tidak memajukan posisi", () => MediaClock.Estimate(25000, 185000, false, 1, 240000) == 25000);
        Test("Kecepatan playback dan batas durasi", () => MediaClock.Estimate(10000, 5000, true, 2, 18000) == 18000);
        Test("Timestamp masa depan tidak memundurkan posisi", () => MediaClock.Estimate(10000, -5000, true, 1, 240000) == 10000);
        var multi = new Track("In Love With Myself", "Petite Meller, Kalman Chodakiewitz, dan Eli Shiffman", "", "test", 173);
        LyricRecord Synced() => new()
        {
            Id = 1,
            TrackName = multi.Title,
            ArtistName = "Kalman, Eli Shiffman & Petite Meller",
            Duration = 173,
            SyncedLyrics = "[00:01.00] contoh sinkron"
        };
        Test("Artis berkolaborasi berbeda urutan tetap cocok", () => Lyrics.Exact(Synced(), multi));
        Test("Konjungsi dan setelah koma dipisah", () => Lyrics.Artists(multi.Artist).Last() == "Eli Shiffman");
        Test("Nama Dan Stevens tetap utuh", () => Lyrics.Artists("Dan Stevens")[0] == "Dan Stevens");
        Test("Cache teks layanan dicari ulang", () => !Lyrics.KeepCached("teks biasa", "lyrics.ovh"));
        Test("LRC impor pengguna tidak ditimpa", () => Lyrics.KeepCached("teks pengguna", "LRC lokal"));
        Test("Cache sinkron tetap dipakai", () => Lyrics.KeepCached("[00:01] contoh", "LRCLIB"));
        Test("Sinkron diprioritaskan pada versi yang cocok", () => Lyrics.ChooseAutomatic(new[] { new LyricRecord { TrackName = multi.Title, ArtistName = multi.Artist, Duration = 173, PlainLyrics = "contoh" }, Synced() }, multi)?.Id == 1);
        Test("Satu hasil beda durasi tidak dipaksa otomatis", () => Lyrics.ChooseAutomatic(new[] { Synced() }, multi with { Duration = 230 }) == null);
        Test("Pencarian terstruktur artis utama menemukan timestamp", () => Task.Run(async () => { using var http = new HttpClient(new Stub(request => { string path = request.RequestUri!.PathAndQuery; if (path.StartsWith("/api/get")) return new(HttpStatusCode.NotFound); if (path.Contains("track_name=") && path.Contains("artist_name=Petite%20Meller")) return JsonResponse(new[] { Synced() }); return JsonResponse(Array.Empty<LyricRecord>()); })); var found = await new LyricsClient(http).Find(multi, CancellationToken.None); return found.Count == 1 && found[0].SyncedLyrics != null; }).GetAwaiter().GetResult());
        Test("Hasil exact plain tidak menghentikan pencarian timestamp", () => Task.Run(async () => { using var http = new HttpClient(new Stub(request => request.RequestUri!.AbsolutePath == "/api/get" ? JsonResponse(new LyricRecord { Id = 2, TrackName = multi.Title, ArtistName = multi.Artist, Duration = 173, PlainLyrics = "contoh" }) : JsonResponse(new[] { Synced() }))); var found = await new LyricsClient(http).Find(multi, CancellationToken.None); return Lyrics.ChooseAutomatic(found, multi)?.Id == 1; }).GetAwaiter().GetResult());
        Test("Pencarian judul saja menjadi cadangan sebelum lyrics ovh", () => Task.Run(async () => { bool fallback = false; using var http = new HttpClient(new Stub(request => { string path = request.RequestUri!.PathAndQuery; if (path.StartsWith("/api/get")) return new(HttpStatusCode.NotFound); if (path.Contains("artist_name=")) return JsonResponse(Array.Empty<LyricRecord>()); if (request.RequestUri.Host == "lrclib.net") return JsonResponse(new[] { Synced() }); fallback = true; return JsonResponse(new { lyrics = "contoh" }); })); var found = await new LyricsClient(http).Find(multi, CancellationToken.None); return !fallback && found[0].SyncedLyrics != null; }).GetAwaiter().GetResult());
        Test("Durasi null tidak menggugurkan hasil sinkron lain", () => Task.Run(async () => { using var http = new HttpClient(new Stub(request => request.RequestUri!.AbsolutePath == "/api/get" ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = new StringContent("[{\"id\":9,\"trackName\":\"In Love With Myself\",\"artistName\":\"Petite Meller\",\"duration\":null,\"syncedLyrics\":\"[00:01] contoh\"},{\"id\":1,\"trackName\":\"In Love With Myself\",\"artistName\":\"Petite Meller\",\"duration\":173,\"syncedLyrics\":\"[00:01] contoh\"}]") })); var found = await new LyricsClient(http).Find(multi, CancellationToken.None); return found.Count == 2 && Lyrics.ChooseAutomatic(found, multi)?.Id == 1; }).GetAwaiter().GetResult());
        Test("Free text tetap dicari setelah hasil terstruktur plain", () => Task.Run(async () => { using var http = new HttpClient(new Stub(request => request.RequestUri!.AbsolutePath == "/api/get" ? new(HttpStatusCode.NotFound) : JsonResponse(request.RequestUri.Query.Contains("?q=") ? new[] { Synced() } : new[] { new LyricRecord { Id = 2, TrackName = multi.Title, ArtistName = multi.Artist, Duration = 173, PlainLyrics = "contoh" } }))); var found = await new LyricsClient(http).Find(multi, CancellationToken.None); return Lyrics.ChooseAutomatic(found, multi)?.Id == 1; }).GetAwaiter().GetResult());
        Test("503 dicoba sekali lagi lalu berhasil", () => Task.Run(async () => { int calls = 0; using var http = new HttpClient(new Stub(request => request.RequestUri!.AbsolutePath == "/api/get" ? (++calls == 1 ? new(HttpStatusCode.ServiceUnavailable) : JsonResponse(Synced())) : JsonResponse(Array.Empty<LyricRecord>()))); var found = await new LyricsClient(http).Find(multi, CancellationToken.None); return calls == 2 && found[0].Id == 1; }).GetAwaiter().GetResult());
        Test("Hasil kosong valid tidak dianggap layanan mati", () => Task.Run(async () => { using var http = new HttpClient(new Stub(request => request.RequestUri!.AbsolutePath == "/api/get" ? new(HttpStatusCode.ServiceUnavailable) : request.RequestUri.Host == "lrclib.net" ? JsonResponse(Array.Empty<LyricRecord>()) : new(HttpStatusCode.NotFound))); return (await new LyricsClient(http).Find(multi, CancellationToken.None)).Count == 0; }).GetAwaiter().GetResult());
        Test("Label video dibersihkan tanpa menghapus versi remix", () => Lyrics.CleanTitle("Contoh (Official Video)") == "Contoh" && Lyrics.CleanTitle("Contoh (Ultra Slowed)") == "Contoh (Ultra Slowed)");
        Test("Artis kanal Topic cocok dengan artis rekaman", () => Lyrics.ArtistMatches("Harry Styles - Topic", "Harry Styles"));
        Test("Unicode lebar penuh cocok", () => Lyrics.Normalize("ＡＢＣ") == Lyrics.Normalize("ABC"));
        Test("Stereo kiri saja tidak menggerakkan kanal kanan", () => { var stereo = new StereoSpectrumEngine(); for (int i = 0; i < 9600; i++) stereo.Add(.4 * Math.Sin(2 * Math.PI * 90 * i / 48000), 0, 48000); return stereo.Left.ExpandedLevels.Max() > .5 && stereo.Right.ExpandedLevels.All(x => x == 0); });
        Test("Stereo kanan treble dan kiri bass terpisah", () => { var stereo = new StereoSpectrumEngine(); for (int i = 0; i < 9600; i++) stereo.Add(.4 * Math.Sin(2 * Math.PI * 90 * i / 48000), .4 * Math.Sin(2 * Math.PI * 10000 * i / 48000), 48000); return Peak(stereo.Left.ExpandedLevels) == 3 && Peak(stereo.Right.ExpandedLevels) == 19; });
        Test("Stereo fase berlawanan tetap terlihat kedua kanal", () => { var stereo = new StereoSpectrumEngine(); for (int i = 0; i < 9600; i++) { double v = .4 * Math.Sin(2 * Math.PI * 950 * i / 48000); stereo.Add(v, -v, 48000); } return stereo.Left.ExpandedLevels.Max() > .5 && stereo.Right.ExpandedLevels.Max() > .5; });
        Test("Stereo hening tetap tanpa demo", () => { var stereo = new StereoSpectrumEngine(); for (int i = 0; i < 9600; i++) stereo.Add(0, 0, 48000); return stereo.Left.Levels.All(x => x == 0) && stereo.Right.Levels.All(x => x == 0); });
        SpectrumEngine LowMeasure(int hz, double amplitude)
        {
            var e = new SpectrumEngine();
            for (int i = 0; i < 9600; i++)
                e.Add(amplitude * Math.Sin(2 * Math.PI * hz * i / 48000), 48000);
            return e;
        }
        Test("Sub-bass 30 Hz lebih dominan dari bass", () => { var e = LowMeasure(30, .4); return e.SubBassLevel > .5 && e.SubBassLevel > e.BassLevel + .3; });
        Test("Bass 120 Hz lebih dominan dari sub-bass", () => { var e = LowMeasure(120, .4); return e.BassLevel > .5 && e.BassLevel > e.SubBassLevel + .3; });
        Test("Treble tidak menggerakkan indikator bass", () => { var e = LowMeasure(9000, .4); return e.BassLevel < .01 && e.SubBassLevel < .01; });
        Test("Meter frekuensi rendah hening tanpa demo", () => { var e = LowMeasure(120, 0); return e.BassLevel == 0 && e.SubBassLevel == 0; });
        Test("Tema kecil dan besar independen", () => { var a = new IslandAppearance(); a.Compact.Background = "#123456"; return a.Expanded.Background == "#0B131C"; });
        Test("Salinan tema tidak mengubah tema asal", () => { var a = new IslandAppearance(); var b = a.Copy(); b.Compact.Accent = "#FFFFFF"; return a.Compact.Accent != "#FFFFFF"; });
        Test("Warna tidak valid jatuh ke warna aman", () => IslandBrushes.Color("invalid") == IslandBrushes.Color("#0B131C"));
        Test("Background hilang memakai warna dasar", () => IslandBrushes.Background(new() { Mode = "Image", ImagePath = "missing-file.png" }) is System.Windows.Media.SolidColorBrush);
        Test("Tema transparansi dan preset tersimpan", () => { var s = new Settings { Appearance = new() { Expanded = IslandStyle.Preset("Aurora") } }; s.Appearance.Expanded.Opacity = .65; var restored = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s)); return restored?.Appearance.Expanded.Mode == "Gradient" && restored.Appearance.Expanded.Opacity == .65; });
        using (var romanizer = new Romanizer())
        {
            Test("Latin: kanji Jepang memakai pembacaan Jepang", () => { var r = romanizer.Read("私は音楽が好きです"); return r.Text.Contains("ongaku") && r.Text.Contains("suki") && !r.Text.Contains("音"); });
            Test("Latin: kanji saja mengikuti konteks Jepang", () => romanizer.Read("未来", "Japanese").Text == "mirai");
            Test("Latin: Arab berharakat menjadi huruf Latin", () => { var r = romanizer.Read("سَلَام"); return r.Text.Contains("salam") && !r.Text.Contains("س"); });
            Test("Latin: Arab tanpa harakat diberi batasan vokal", () => { var r = romanizer.Read("سلام عليكم"); return r.Text.Length > 0 && r.Note.Contains("tanpa harakat"); });
            Test("Latin: Hangul dan Cyrillic terbaca", () => romanizer.Read("안녕하세요").Text.Contains("annyeong") && romanizer.Read("Привет мир").Text.Contains("Privet"));
            Test("Latin: Mandarin tidak dianggap Jepang tanpa kana", () => romanizer.Read("你好").Text.Contains("ni"));
            Test("Latin: lirik Latin sumber tidak diduplikasi", () => romanizer.Read("Hello world, 123!").Text == "");
            Test("Latin: tanda baca dan campuran Inggris dipertahankan", () => romanizer.Read("音楽, Hello!", "Japanese").Text.Contains(", Hello!"));
            Test("Latin: teks panjang dibatasi", () => romanizer.Read(new string('あ', 4097)).Text == "");
            Test("Latin: anotasi tidak mengubah teks atau timestamp", () => { var a = new System.Collections.Generic.List<LyricLine> { new(1200, "未来"), new(3000, "こんにちは") }; var b = Romanizer.Annotate(a, CancellationToken.None); return b[0].Time == 1200 && b[0].Text == "未来" && b[0].Latin == "mirai" && a[0].Latin == "" && b[1].Time == 3000; });
            Test("Latin: deteksi Jepang tidak mematikan baris Arab atau Korea", () => { var a = Romanizer.Annotate(new[] { new LyricLine(0, "こんにちは"), new LyricLine(1000, "سَلَام"), new LyricLine(2000, "안녕하세요") }, CancellationToken.None); return a.All(l => l.Latin.Length > 0) && a[1].Latin.Contains("salam"); });
            Test("Latin: island mempertahankan pembungkusan cara baca panjang", () => { using var state = new PlayerState(true); var w = new IslandWindow(state, () => { }); try { w.CurrentLatin.Text = string.Join(" ", Enumerable.Repeat("cara baca panjang", 20)); w.CurrentLatin.Visibility = System.Windows.Visibility.Visible; w.CurrentLatin.Measure(new System.Windows.Size(300, double.PositiveInfinity)); return w.CurrentLatin.TextWrapping == System.Windows.TextWrapping.Wrap && w.CurrentLatin.DesiredSize.Height > 40; } finally { w.Close(); } });
            Test("Latin: pembatalan menghentikan pekerjaan lama", () => { using var c = new CancellationTokenSource(); c.Cancel(); try { Romanizer.Annotate(new[] { new LyricLine(0, "こんにちは") }, c.Token); return false; } catch (OperationCanceledException) { return true; } });
            Test("Latin: pengaturan tersimpan dan default aktif", () => JsonSerializer.Deserialize<Settings>("{}")!.ShowLatin && !JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { ShowLatin = false }))!.ShowLatin);
        }
        GeminiTests.Run(Test);
        GeminiRateTests.Run(Test);
        AiProviderTests.Run(Test);
        UpdateTests.Run(Test);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            Total = results.Count,
            Failed = failed,
            Results = results
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failed;
    }
    private static HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private sealed class Stub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> reply; public Stub(Func<HttpRequestMessage, HttpResponseMessage> reply)
        {
            this.reply = reply;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) => Task.FromResult(reply(request));
    }
}
