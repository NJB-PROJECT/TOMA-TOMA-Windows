using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Toma.Windows;
public sealed class GeminiWindow : Window
{
    private readonly PlayerState state;
    private readonly PasswordBox key = new() { Padding = new Thickness(10), FontSize = 15, Background = IslandBrushes.Solid("#15212D"), Foreground = Brushes.White };
    private readonly ComboBox model = new() { IsEditable = true, Padding = new Thickness(8), Foreground = Brushes.Black };
    private readonly CheckBox enabled = new() { Content = "Aktifkan AI untuk cara baca Indonesia" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = IslandBrushes.Solid("#A5FFE5"), Margin = new Thickness(0, 12, 0, 0) };
    private readonly CancellationTokenSource closed = new();
    private readonly Button models = new() { Content = "Muat model dari akun" };
    private readonly Button test = new() { Content = "Uji contoh Vietnam" };
    private bool busy;
    public GeminiWindow(PlayerState state)
    {
        this.state = state;
        Title = "Gemini · Cara baca lintas bahasa"; Width = 620; Height = 620;
        MaxHeight = SystemParameters.WorkArea.Height - 24; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(26) }; Content = root;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var cancel = new Button { Content = "Batal" }; cancel.Click += (_, _) => Close(); footer.Children.Add(cancel);
        var save = new Button { Content = "Simpan & gunakan", Background = IslandBrushes.Solid("#78EBCB"), Foreground = IslandBrushes.Solid("#09251E") }; save.Click += Save; footer.Children.Add(save);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll);
        var panel = new StackPanel(); scroll.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Nyanyikan bahasa lain.", FontSize = 28, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Gemini mengubah pengucapan menjadi ejaan yang mudah dibaca penutur Indonesia, termasuk Vietnam dan bahasa yang sudah berhuruf Latin. Bukan terjemahan; hasil tetap perkiraan.", TextWrapping = TextWrapping.Wrap, Foreground = IslandBrushes.Solid("#91A6B8"), Margin = new Thickness(0, 10, 0, 16) });
        panel.Children.Add(new TextBlock { Text = GeminiSecret.Exists ? "API key sudah tersimpan terenkripsi. Kosongkan kolom untuk tetap memakainya." : "API key Google AI Studio (disimpan terenkripsi untuk akun Windows ini)", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        key.Margin = new Thickness(0, 8, 0, 12); panel.Children.Add(key);
        panel.Children.Add(new TextBlock { Text = "MODEL GEMINI", FontSize = 10, Foreground = IslandBrushes.Solid("#91A6B8"), Margin = new Thickness(0, 0, 0, 7) });
        foreach (string id in new[] { "gemini-3.5-flash-lite", "gemini-3.5-flash" }) model.Items.Add(id);
        model.Text = state.Settings.GeminiModel; panel.Children.Add(model);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 8) }; actions.Children.Add(models); actions.Children.Add(test); panel.Children.Add(actions);
        models.Click += async (_, _) => await Run(async () => { string selected = model.Text; var values = await new GeminiPhonetics().Models(CurrentKey(), closed.Token); model.Items.Clear(); foreach (string id in values) model.Items.Add(id); model.Text = values.Contains(selected) ? selected : values.FirstOrDefault(id => id == "gemini-3.5-flash-lite") ?? values.FirstOrDefault() ?? selected; status.Text = values.Count + " model tersedia. Pilih Flash/Flash-Lite yang didukung akunmu."; });
        test.Click += async (_, _) => await Run(async () => { var source = new[] { new LyricLine(0, "Xin chào, tôi thích âm nhạc.") }; var rows = await new GeminiPhonetics().Convert(CurrentKey(), model.Text, "Contoh pengucapan", "", source, new[] { 0 }, closed.Token); var row = rows[0]; status.Text = row.Supported ? row.Language + " · " + row.Reading + "\nUji Gemini berhasil. Hasil adalah perkiraan cara baca." : "Gemini belum mengenali pembacaan contoh ini."; });
        enabled.IsChecked = state.Settings.GeminiEnabled || !GeminiSecret.Exists; panel.Children.Add(enabled);
        panel.Children.Add(new TextBlock { Text = "Saat aktif, lirik, judul dan artis lagu dikirim ke Google Gemini. Audio tidak dikirim. Kuota/biaya mengikuti akun Google. Hasil disimpan lokal agar lagu yang sama tidak diproses berulang. Key tidak disimpan dalam settings.json atau log.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = IslandBrushes.Solid("#91A6B8"), Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(status);
        var remove = new Button { Content = "Hapus key tersimpan", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0), FontSize = 10 }; remove.Click += (_, _) => { GeminiSecret.Remove(); key.Clear(); state.Settings.GeminiEnabled = false; enabled.IsChecked = false; Storage.Save(state.Settings); state.RefreshLatin(); status.Text = "Key dihapus; AI dinonaktifkan."; }; panel.Children.Add(remove);
        Closed += (_, _) => { closed.Cancel(); key.Clear(); };
    }
    private string CurrentKey() => key.Password.Trim().Length > 0 ? key.Password.Trim() : GeminiSecret.Read();
    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true; models.IsEnabled = test.IsEnabled = false; status.Text = "Menghubungi Google Gemini…";
        try { await action(); }
        catch (OperationCanceledException) { status.Text = "Permintaan dibatalkan atau terlalu lama. Coba lagi."; }
        catch (GeminiFailure ex) { status.Text = ex.Message; }
        catch { status.Text = "Belum dapat terhubung. Periksa koneksi/key lalu coba lagi."; }
        finally { busy = false; models.IsEnabled = test.IsEnabled = true; }
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        if (busy) { status.Text = "Tunggu pengujian selesai dahulu."; return; }
        try
        {
            string id = GeminiPhonetics.ValidModel(model.Text.Trim());
            if (key.Password.Trim().Length > 0) GeminiSecret.Save(key.Password.Trim());
            if (enabled.IsChecked == true && !GeminiSecret.Exists) { status.Text = "Isi API key dahulu untuk mengaktifkan Gemini."; return; }
            state.Settings.GeminiModel = id; state.Settings.GeminiEnabled = enabled.IsChecked == true;
            Storage.Save(state.Settings); state.RefreshLatin(); Close();
        }
        catch (GeminiFailure ex) { status.Text = ex.Message; }
        catch { status.Text = "Pengaturan/key belum dapat disimpan. Coba lagi."; }
    }
}
