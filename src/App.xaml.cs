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
    private PlayerState? state; private MainWindow? main; private IslandWindow? island; private System.Windows.Forms.NotifyIcon? tray; private Mutex? single;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, error) => { FileLog.Write(error.Exception.ToString()); System.Windows.MessageBox.Show("Ada kendala. Detail disimpan di app.log pada folder data TOMA TOMA.", "TOMA TOMA"); error.Handled = true; };
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
            System.Windows.MessageBox.Show("TOMA TOMA sudah berjalan. Buka lewat ikon di system tray.", "TOMA TOMA");
            Shutdown();
            return;
        }
        state = new();
        island = new IslandWindow(state, OpenMain);
        main = new MainWindow(state, island);
        MainWindow = main;
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Buka TOMA TOMA", null, (_, _) => Dispatcher.Invoke(OpenMain));
        menu.Items.Add("Tampilkan island", null, (_, _) => Dispatcher.Invoke(() => { state.Settings.Island = true; Storage.Save(state.Settings); island.Show(); island.Reposition(); }));
        menu.Items.Add("Sembunyikan island", null, (_, _) => Dispatcher.Invoke(() => { state.Settings.Island = false; Storage.Save(state.Settings); island.Hide(); }));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Keluar", null, (_, _) => Dispatcher.Invoke(ExitApp));
        tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application, Text = "TOMA TOMA · Auto Lyrics", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenMain);
        main.Show();
        if (state.Settings.Island)
            island.Show();
        await state.Start();
    }
    private void OpenMain()
    {
        if (main == null)
            return;
        main.Show();
        main.WindowState = WindowState.Normal;
        main.Activate();
    }
    private void ExitApp()
    {
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
