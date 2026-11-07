using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using Microsoft.Win32;
namespace Toma.Windows;
public partial class MainWindow : Window
{
    private readonly PlayerState state; private readonly IslandWindow island; private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) }; private bool ready, selecting; private string? shownKey; private System.Collections.Generic.List<LyricLine>? shownLines; private int lastIndex = -2;
    public bool Exiting;
    public MainWindow(PlayerState state, IslandWindow island)
    {
        InitializeComponent();
        this.state = state;
        this.island = island;
        Height = Math.Max(MinHeight, Math.Min(780, SystemParameters.WorkArea.Height - 36));
        Width = Math.Max(MinWidth, Math.Min(1080, SystemParameters.WorkArea.Width - 48));
        IslandCheck.IsChecked = state.Settings.Island;
        SpectrumCheck.IsChecked = state.Settings.Spectrum;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            MonitorBox.Items.Add(new ComboBoxItem { Content = screen.DeviceName + (screen.Primary ? " · utama" : ""), Tag = screen.DeviceName });
        MonitorBox.SelectedIndex = 0;
        for (int i = 0; i < MonitorBox.Items.Count; i++)
            if (((ComboBoxItem)MonitorBox.Items[i]).Tag?.ToString() == state.Settings.Monitor)
                MonitorBox.SelectedIndex = i;
        ready = true;
        state.Changed += Refresh;
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Closing += (_, e) => { if (!Exiting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => { timer.Stop(); state.Changed -= Refresh; };
        Refresh();
    }
    private void Refresh()
    {
        TrackTitle.Text = state.Track?.Title ?? "Putar musikmu";
        TrackArtist.Text = state.Track == null ? "Spotify · YouTube Music · pemutar Windows" : state.Track.Artist + " · " + Ui.SourceName(state.Track.Source);
        Art.Source = state.Cover;
        StateLabel.Text = state.Track == null ? state.Media.Status : state.Status;
        Provider.Text = state.Provider.Length > 0 ? state.Provider + " · " + (state.Lines.Count > 0 && state.Lines[0].Time >= 0 ? "Lirik bertimestamp" : "Teks tanpa timestamp") : "LIRIK SAAT INI";
        if (shownKey != state.Track?.Key)
        {
            shownKey = state.Track?.Key;
            TitleInput.Text = state.Track?.Title ?? "";
            ArtistInput.Text = state.Track?.Artist ?? "";
        }
        if (shownLines != state.Lines)
        {
            shownLines = state.Lines;
            selecting = true;
            LyricsList.ItemsSource = state.Lines;
            selecting = false;
            lastIndex = -2;
        }
        EmptyLyrics.Visibility = state.Lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (state.Track != null && state.Lines.Count == 0)
            EmptyLyrics.Text = state.Status;
        Tick();
    }
    private void Tick()
    {
        PlayButton.Content = state.Playing ? "Ⅱ Jeda" : "▶ Putar";
        Clock.Text = Ui.Time(state.Position) + " / " + Ui.Time((state.Track?.Duration ?? 0) * 1000);
        OffsetLabel.Text = $"{state.Offset:+0;-0;0} ms";
        Credit.Foreground = Ui.CreditBrush();
        int index = state.Active;
        if (index != lastIndex)
        {
            lastIndex = index;
            selecting = true;
            LyricsList.SelectedIndex = index;
            if (index >= 0 && FollowCheck.IsChecked == true)
                LyricsList.ScrollIntoView(state.Lines[index]);
            selecting = false;
        }
    }
    private async void PlayClick(object sender, RoutedEventArgs e) => await state.Toggle(); private async void PreviousClick(object sender, RoutedEventArgs e) => await state.Skip(false); private async void NextClick(object sender, RoutedEventArgs e) => await state.Skip(true);
    private void SearchClick(object sender, RoutedEventArgs e)
    {
        if (state.Track == null)
        {
            MessageBox.Show(this, "Putar musik atau pilih file audio terlebih dahulu.", "Pilih lagu");
            return;
        }
        state.SearchAs(TitleInput.Text, ArtistInput.Text);
    }
    private void AppearanceClick(object sender, RoutedEventArgs e)
    {
        new AppearanceWindow(state, island) { Owner = this }.ShowDialog();
    }
    private void FollowClick(object sender, RoutedEventArgs e) => state.FollowWindows();
    private void LocalClick(object sender, RoutedEventArgs e)
    {
        var pick = new OpenFileDialog { Filter = "Audio|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.wma;*.ogg|Semua file|*.*" };
        if (pick.ShowDialog(this) == true)
            state.OpenLocal(pick.FileName);
    }
    private void ImportClick(object sender, RoutedEventArgs e)
    {
        if (state.Track == null)
        {
            MessageBox.Show(this, "Pilih lagu dahulu agar LRC disimpan untuk lagu yang tepat.");
            return;
        }
        var pick = new OpenFileDialog { Filter = "Lirik|*.lrc;*.txt" };
        if (pick.ShowDialog(this) == true)
        {
            if (new FileInfo(pick.FileName).Length > 262144)
            {
                MessageBox.Show(this, "Ukuran LRC maksimal 256 KB.");
                return;
            }
            state.Import(File.ReadAllText(pick.FileName));
        }
    }
    private void LinkClick(object sender, RoutedEventArgs e)
    {
        string? value = Ui.Ask(this, "Buka link lagu", "Tempel link Spotify atau YouTube Music. Putar lagunya di browser/pemutar; TOMA akan mengikuti sesi media Windows.");
        if (value == null)
            return;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || !(new[] { "music.youtube.com", "www.youtube.com", "youtube.com", "youtu.be", "open.spotify.com", "spotify.link" }).Contains(uri.Host.ToLowerInvariant()))
        {
            MessageBox.Show(this, "Gunakan link HTTPS Spotify atau YouTube Music yang valid.");
            return;
        }
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        state.FollowWindows();
    }
    private async void LineSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!selecting && LyricsList.SelectedItem is LyricLine line && line.Time >= 0)
            await state.Seek(Math.Max(0, line.Time - state.Offset));
    }
    private void OffsetBack(object sender, RoutedEventArgs e) => state.Adjust(-250); private void OffsetAhead(object sender, RoutedEventArgs e) => state.Adjust(250);
    private void IslandChanged(object sender, RoutedEventArgs e)
    {
        if (!ready)
            return;
        state.Settings.Island = IslandCheck.IsChecked == true;
        if (state.Settings.Island)
            island.Show();
        else
            island.Hide();
        Storage.Save(state.Settings);
    }
    private void SpectrumChanged(object sender, RoutedEventArgs e)
    {
        if (ready)
            state.SetSpectrum(SpectrumCheck.IsChecked == true);
    }
    private void MonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready)
            return;
        state.Settings.Monitor = (MonitorBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        island.Reposition();
        Storage.Save(state.Settings);
    }
    private async void VersionClick(object sender, RoutedEventArgs e)
    {
        if (state.Choices.Count <= 1)
            await state.Search(true, true);
        if (state.Choices.Count == 0)
            return;
        var dialog = new Window { Title = "Pilih versi lirik", Width = 650, Height = 480, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(20) };
        var select = new Button { Content = "Gunakan versi ini", Margin = new Thickness(0, 15, 0, 0) };
        DockPanel.SetDock(select, Dock.Bottom);
        root.Children.Add(select);
        var list = new ListBox();
        foreach (var record in state.Choices)
            list.Items.Add(new ListBoxItem { Content = new TextBlock { Text = record.Label + "\n" + record.Detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) }, Tag = record });
        root.Children.Add(list);
        select.Click += (_, _) => { if (list.SelectedItem is ListBoxItem item && item.Tag is LyricRecord record) { state.Select(record); dialog.Close(); } };
        dialog.Content = root;
        dialog.ShowDialog();
    }
}
public static class Ui
{
    public static string SourceName(string source) => source.Contains("Spotify", StringComparison.OrdinalIgnoreCase) ? "Spotify" : source.Contains("Chrome", StringComparison.OrdinalIgnoreCase) || source.Contains("Edge", StringComparison.OrdinalIgnoreCase) || source.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ? "Pemutar browser" : source == "File lokal" ? source : "Pemutar Windows";
    public static string Time(double millis) => TimeSpan.FromMilliseconds(Math.Max(0, millis)).ToString(@"mm\:ss");
    public static SolidColorBrush CreditBrush()
    {
        double phase = Environment.TickCount64 % 60000 / 60000.0 * Math.PI * 2;
        var brush = new SolidColorBrush(Color.FromRgb((byte)(180 + 65 * Math.Sin(phase)), (byte)(180 + 65 * Math.Sin(phase + 2 * Math.PI / 3)), (byte)(180 + 65 * Math.Sin(phase + 4 * Math.PI / 3))));
        brush.Freeze();
        return brush;
    }
    public static bool HasButtonAncestor(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase)
                return true;
            node = node is Visual || node is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }
    public static string? Ask(Window owner, string title, string description)
    {
        string? answer = null;
        var dialog = new Window { Owner = owner, Title = title, Width = 580, Height = 260, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var input = new TextBox();
        panel.Children.Add(input);
        var button = new Button { Content = "Buka link", Margin = new Thickness(0, 18, 0, 0) };
        button.Click += (_, _) => { answer = input.Text; dialog.Close(); };
        panel.Children.Add(button);
        dialog.Content = panel;
        dialog.ShowDialog();
        return answer;
    }
}
