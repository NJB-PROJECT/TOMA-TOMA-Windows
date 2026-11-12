using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Text.Json;
namespace Toma.Windows;
public partial class App : System.Windows.Application
{
    private readonly UpdateClient updates = new();private readonly System.Windows.Threading.DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromMinutes(15) };private bool checkingUpdate, updateBlocked;private readonly CancellationTokenSource updateCancel = new();
    private PlayerState? state; private MainWindow? main; private IslandWindow? island; private System.Windows.Forms.NotifyIcon? tray; private Mutex? single;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, error) => { FileLog.Write(error.Exception.ToString()); System.Windows.MessageBox.Show("Ada kendala. Detail disimpan di app.log pada folder data Lyrivane.", "Lyrivane"); error.Handled = true; };
        if (e.Args.Contains("--gemini-probe"))
        {
            int at = Array.IndexOf(e.Args, "--gemini-probe");
            var settings = Storage.Load();
            if (e.Args.Length > at + 2) settings.GeminiModel = GeminiPhonetics.ValidModel(e.Args[at + 2]);
            var source = new[] { new LyricLine(1000, "Xin chào, tôi thích âm nhạc."), new LyricLine(3000, "私は音楽が好きです"), new LyricLine(5000, "السلام عليكم"), new LyricLine(7000, "Привет, как дела?") };
            try
            {
                var rows = await new GeminiPhonetics().Convert(GeminiSecret.Read(), settings.GeminiModel, "Contoh pengucapan", "", source, new[] { 0, 1, 2, 3 }, CancellationToken.None);
                File.WriteAllText(e.Args[at + 1], JsonSerializer.Serialize(new { Success = true, settings.GeminiModel, Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (GeminiFailure failure) { File.WriteAllText(e.Args[at + 1], JsonSerializer.Serialize(new { Success = false, Error = failure.Message })); }
            catch { File.WriteAllText(e.Args[at + 1], "{\"Success\":false,\"Error\":\"Connection failed\"}"); }
            Shutdown(); return;
        }
        if (e.Args.Contains("--latin-probe"))
        {
            int at = Array.IndexOf(e.Args, "--latin-probe");
            using var engine = new Romanizer();
            var examples = new[] { "私は音楽が好きです", "未来", "こんにちは", "السَّلَامُ عَلَيْكُمْ", "السلام عليكم", "안녕하세요", "Привет мир", "नमस्ते", "你好", "Hello world" };
            var results = examples.Select(text => new { Original = text, Reading = engine.Read(text, text == "未来" ? "Japanese" : "Auto") }).ToArray();
            File.WriteAllText(e.Args[at + 1], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            Shutdown(); return;
        }
        if (e.Args.Contains("--update-probe"))
        {
            int at = Array.IndexOf(e.Args, "--update-probe");
            if (e.Args.Length < at + 3) { Shutdown(2); return; }
            string output = Path.GetFullPath(e.Args[at + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var probe = new UpdateClient(storage: Path.Combine(Path.GetDirectoryName(output)!, "update-probe-cache"), current: UpdateClient.ReadVersion(e.Args[at + 2]));
            var candidate = await probe.Check(CancellationToken.None);
            string? installerPath = null;
            if (candidate != null && e.Args.Contains("--download")) installerPath = await probe.Download(candidate, null, CancellationToken.None);
            File.WriteAllText(output, JsonSerializer.Serialize(new { Current = probe.Current.ToString(), UpdateRequired = candidate != null, Release = candidate, InstallerPath = installerPath, probe.CheckStatus }, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown(); return;
        }
        if (e.Args.Contains("--update-preview"))
        {
            var preview = new UpdateClient(storage: Path.Combine(Path.GetTempPath(), "TomaTomaUpdatePreview"), current: new Version(1, 5, 0));
            var gate = new UpdateWindow(preview) { Title = "Lyrivane · Preview update (simulasi versi 1.5.0)" };
            gate.ShowDialog(); Shutdown(); return;
        }
        if (e.Args.Contains("--design-preview"))
        {
            state = new PlayerState(true); state.ShowcaseFrame(0);
            if (e.Args.Contains("--latin-demo")) state.ShowcaseLatin();
            island = new IslandWindow(state, () => main?.Show());
            main = new MainWindow(state, island) { Title = "Lyrivane Desktop · Design preview (demo)" };
            MainWindow = main; main.Show();
            if (e.Args.Contains("--latin-demo")) { island.Show(); island.ShowcaseExpand(true); }
            double time = 0;
            var demoTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            demoTimer.Tick += (_, _) => { time += .05; state.ShowcaseFrame(time % 18); };
            main.Closed += (_, _) => { demoTimer.Stop(); state.Dispose(); Shutdown(); };
            demoTimer.Start(); return;
        }
        if (e.Args.Contains("--showcase"))
        {
            int at = Array.IndexOf(e.Args, "--showcase");
            await ShowcaseExporter.Export(e.Args.Length > at + 1 ? e.Args[at + 1] : "showcase-frames");
            Shutdown();
            return;
        }
        if (e.Args.Contains("--self-test"))
        {
            int at = Array.IndexOf(e.Args, "--self-test");
            string output = e.Args.Length > at + 1 ? e.Args[at + 1] : Path.Combine(Storage.DirectoryPath, "self-test.json");
            int failed = SelfTest.Run(output);
            Shutdown(failed);
            return;
        }
        if (e.Args.Contains("--lyrics-probe"))
        {
            int at = Array.IndexOf(e.Args, "--lyrics-probe");
            if (e.Args.Length < at + 5)
            {
                Shutdown(2);
                return;
            }
            string output = e.Args[at + 1];
            var track = new Track(e.Args[at + 2], e.Args[at + 3], "", "probe", double.Parse(e.Args[at + 4], System.Globalization.CultureInfo.InvariantCulture));
            var client = new LyricsClient();
            var automatic = await client.Find(track, CancellationToken.None);
            var choices = await client.Find(track, CancellationToken.None, true);
            var selected = Lyrics.ChooseAutomatic(automatic, track);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                AutomaticId = selected?.Id,
                AutomaticSynced = selected != null && Lyrics.Parse(selected.Raw).Any(l => l.Time >= 0),
                Versions = choices.Select(r => new { r.Id, r.TrackName, r.ArtistName, r.Duration, Synced = !string.IsNullOrWhiteSpace(r.SyncedLyrics), TimedLines = Lyrics.Parse(r.Raw).Count(l => l.Time >= 0) })
            }, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown();
            return;
        }
        if (e.Args.Contains("--probe"))
        {
            int at = Array.IndexOf(e.Args, "--probe");
            string output = e.Args.Length > at + 1 ? e.Args[at + 1] : Path.Combine(Storage.DirectoryPath, "probe.json");
            using var media = new MediaService();
            using var audio = new AudioSpectrum();
            audio.Start();
            await media.Start();
            double before = media.Position;
            await Task.Delay(4000);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                WindowsMediaStatus = media.Status,
                Sessions = media.Sources,
                TrackDetected = media.Current != null,
                PositionBefore = before,
                PositionAfter = media.Position,
                media.TimingInfo,
                AudioStatus = audio.Status,
                AudioPackets = audio.Packets,
                AudioRmsDb = audio.RmsDb,
                AudioPeak = audio.Levels.Max()
            }, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown();
            return;
        }
        single = new Mutex(true, "Local\\TomaTomaWindows.Desktop.1", out bool first);
        if (!first)
        {
            System.Windows.MessageBox.Show("Lyrivane sudah berjalan. Buka lewat ikon di system tray.", "Lyrivane");
            Shutdown();
            return;
        }
        var update = new UpdateWindow(updates);update.ShowDialog();
        if (update.Decision != UpdateDecision.Continue) { single.Dispose();Shutdown();return; }
        state = new();
        island = new IslandWindow(state, OpenMain);
        main = new MainWindow(state, island);
        MainWindow = main;
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Buka Lyrivane", null, (_, _) => Dispatcher.Invoke(OpenMain));
        menu.Items.Add("Tampilkan island", null, (_, _) => Dispatcher.Invoke(() => { state.Settings.Island = true; Storage.Save(state.Settings); island.Show(); island.Reposition(); }));
        menu.Items.Add("Sembunyikan island", null, (_, _) => Dispatcher.Invoke(() => { state.Settings.Island = false; Storage.Save(state.Settings); island.Hide(); }));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Periksa pembaruan", null, async (_, _) => await CheckUpdates(true));
        menu.Items.Add("Keluar", null, (_, _) => Dispatcher.Invoke(ExitApp));
        tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application, Text = "Lyrivane · Auto Lyrics", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenMain);
        main.Show();
        if (state.Settings.Island)
            island.Show();
        if (e.Args.Contains("--open-gemini")) _ = Dispatcher.BeginInvoke(new Action(() => new GeminiWindow(state) { Owner = main }.ShowDialog()));
        await state.Start();
        updateTimer.Tick += async (_, _) => await CheckUpdates(false);updateTimer.Start();
    }
    private async Task CheckUpdates(bool manual)
    {
        if (checkingUpdate) return;checkingUpdate = true;
        try
        {
            var available = await updates.Check(updateCancel.Token);
            if (available == null) { if (manual) MessageBox.Show(main!, updates.CheckStatus + "\nVersi terpasang: " + updates.Current.ToString(3), "Pembaruan");return; }
            updateBlocked = true;updateTimer.Stop();if (tray != null) tray.Visible = false;island?.Hide();main?.Hide();state?.PrepareForUpdate();var gate = new UpdateWindow(updates, available);gate.ShowDialog();ExitApp();
        }
        catch (OperationCanceledException) { }
        finally { checkingUpdate = false; }
    }
    private void OpenMain()
    {
        if (main == null || updateBlocked)
            return;
        main.Show();
        main.WindowState = WindowState.Normal;
        main.Activate();
    }
    private void ExitApp()
    {
        updateTimer.Stop();updateCancel.Cancel();
        tray?.Dispose();
        state?.Dispose();
        if (main != null)
        {
            main.Exiting = true;
            main.Close();
        }
        island?.Close();
        single?.Dispose();
        Shutdown();
    }
}
