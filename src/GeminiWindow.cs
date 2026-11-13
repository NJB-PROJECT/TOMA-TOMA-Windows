using System;
using System.Collections.Generic;
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
    private readonly ComboBox provider = new() { Padding = new Thickness(8), Foreground = Brushes.Black };
    private readonly ComboBox model = new() { IsEditable = true, Padding = new Thickness(8), Foreground = Brushes.Black };
    private readonly CheckBox master = new() { Content = "Aktifkan cara baca AI + cadangan otomatis" };
    private readonly CheckBox enabled = new() { Content = "Gunakan penyedia ini" };
    private readonly TextBlock keyHint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = IslandBrushes.Solid("#A5FFE5"), Margin = new Thickness(0, 12, 0, 0) };
    private readonly CancellationTokenSource closed = new();
    private readonly Dictionary<string, AiBackup> drafts = new();
    private readonly Dictionary<string, string> secrets = new();
    private readonly Button models = new() { Content = "Muat model" }, test = new() { Content = "Uji contoh" };
    private string selected = "gemini"; private bool busy, loading;
    public GeminiWindow(PlayerState state)
    {
        this.state = state;
        foreach (string id in AiProviders.Ids) { var saved = state.Settings.AiBackups.FirstOrDefault(p => p.Provider == id); drafts[id] = new() { Provider = id, Model = id == "gemini" ? state.Settings.GeminiModel : saved?.Model ?? AiProviders.DefaultModel(id), Enabled = saved?.Enabled ?? id == "gemini" }; }
        Title = "AI & API cadangan"; Width = 650; Height = 720; MinHeight = 500; MaxHeight = SystemParameters.WorkArea.Height - 24; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(26) }; Content = root;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var cancel = new Button { Content = "Batal" }; cancel.Click += (_, _) => Close(); footer.Children.Add(cancel);
        var save = new Button { Content = "Simpan & gunakan", Background = IslandBrushes.Solid("#78EBCB"), Foreground = IslandBrushes.Solid("#09251E") }; save.Click += Save; footer.Children.Add(save);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll); var panel = new StackPanel(); scroll.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Cara baca, tetap tersedia.", FontSize = 27, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Urutan cadangan: Gemini → Groq → OpenRouter. Penyedia tanpa key/dinonaktifkan dilewati. Setiap batas kuota tetap dihormati; tidak ada key bersama di installer.", TextWrapping = TextWrapping.Wrap, Foreground = IslandBrushes.Solid("#91A6B8"), Margin = new Thickness(0, 10, 0, 12) });
        master.IsChecked = state.Settings.GeminiEnabled; panel.Children.Add(master);
        foreach (string id in AiProviders.Ids) provider.Items.Add(AiProviders.Label(id)); provider.SelectedIndex = 0; panel.Children.Add(provider);
        keyHint.Margin = new Thickness(0, 12, 0, 8); panel.Children.Add(keyHint); key.Margin = new Thickness(0, 0, 0, 12); panel.Children.Add(key);
        panel.Children.Add(new TextBlock { Text = "MODEL", FontSize = 10, Foreground = IslandBrushes.Solid("#91A6B8"), Margin = new Thickness(0, 0, 0, 6) }); panel.Children.Add(model); panel.Children.Add(enabled);
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) }; actions.Children.Add(models); actions.Children.Add(test);
        var create = new Button { Content = "Buat key sendiri" }; create.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AiProviders.KeyUrl(selected)) { UseShellExecute = true }); actions.Children.Add(create); panel.Children.Add(actions);
        models.Click += async (_, _) => await Run(async () => { string id = selected, original = model.Text; var values = id == "gemini" ? await new GeminiPhonetics().Models(CurrentKey(), closed.Token) : await new OpenAiPhonetics().Models(id, CurrentKey(), closed.Token); model.Items.Clear(); foreach (string value in values) model.Items.Add(value); model.Text = values.Contains(original) ? original : values.FirstOrDefault() ?? AiProviders.DefaultModel(id); status.Text = values.Count + " model didukung tersedia. Groq/Gemini mengikuti paket akun; OpenRouter dibatasi model $0/:free."; });
        test.Click += async (_, _) => await Run(async () => { string id = selected; var source = new[] { new LyricLine(0, "Xin chào, tôi thích âm nhạc.") }; var rows = id == "gemini" ? await new GeminiPhonetics().Convert(CurrentKey(), model.Text, "Contoh", "", source, new[] { 0 }, closed.Token, message => status.Text = message) : await new OpenAiPhonetics().Convert(id, CurrentKey(), model.Text, "Contoh", "", source, new[] { 0 }, closed.Token, message => status.Text = message); status.Text = rows[0].Supported ? AiProviders.Label(id) + " berhasil · " + rows[0].Reading : "Cara baca contoh belum dikenali."; });
        panel.Children.Add(new TextBlock { Text = "Saat AI aktif, lirik/judul/artis dikirim hanya ke penyedia yang diaktifkan. Audio tidak dikirim. Key disimpan terenkripsi untuk akun Windows ini. Paket gratis tetap memiliki batas; bila akunmu memakai paket berbayar, ketentuan biaya penyedia berlaku. OpenRouter tidak dialihkan ke model berbayar.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = IslandBrushes.Solid("#91A6B8"), Margin = new Thickness(0, 10, 0, 0) }); panel.Children.Add(status);
        var remove = new Button { Content = "Hapus key penyedia ini", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0), FontSize = 10 }; remove.Click += (_, _) => { ProviderSecrets.Remove(selected); secrets.Remove(selected); key.Clear(); enabled.IsChecked = false; status.Text = "Key penyedia ini dihapus. Simpan untuk menerapkan perubahan."; }; panel.Children.Add(remove);
        provider.SelectionChanged += (_, _) => { if (loading || provider.SelectedIndex < 0) return; StoreDraft(); selected = AiProviders.Ids[provider.SelectedIndex]; LoadDraft(); };
        key.PasswordChanged += (_, _) => { if (!loading && key.Password.Length > 0) { enabled.IsChecked = true; master.IsChecked = true; } };
        LoadDraft(); Closed += (_, _) => { closed.Cancel(); key.Clear(); secrets.Clear(); };
    }
    private void StoreDraft() { drafts[selected].Model = model.Text.Trim(); drafts[selected].Enabled = enabled.IsChecked == true; if (key.Password.Trim().Length > 0) secrets[selected] = key.Password.Trim(); }
    private void LoadDraft()
    {
        loading = true; key.Clear(); model.Items.Clear();
        foreach (string id in selected == "gemini" ? new[] { "gemini-3.5-flash-lite", "gemini-3.5-flash" } : selected == "groq" ? new[] { "qwen/qwen3.8-27b", "openai/gpt-oss-120b", "openai/gpt-oss-20b" } : new[] { "openrouter/free" }) model.Items.Add(id);
        model.Text = drafts[selected].Model; enabled.IsChecked = drafts[selected].Enabled;
        keyHint.Text = secrets.ContainsKey(selected) ? "Key baru sudah diisi (belum disimpan)." : ProviderSecrets.Exists(selected) ? "Key tersimpan terenkripsi; kosongkan kolom untuk tetap memakainya." : "Masukkan API key " + AiProviders.Label(selected) + " milikmu sendiri.";
        loading = false;
    }
    private string CurrentKey() => key.Password.Trim().Length > 0 ? key.Password.Trim() : secrets.TryGetValue(selected, out var value) ? value : ProviderSecrets.Read(selected);
    private async Task Run(Func<Task> action)
    {
        if (busy) return; busy = true; models.IsEnabled = test.IsEnabled = provider.IsEnabled = false; status.Text = "Menghubungi penyedia…";
        try { await action(); } catch (OperationCanceledException) { status.Text = "Permintaan dibatalkan/terlalu lama."; } catch (GeminiFailure ex) { status.Text = ex.Message; } catch { status.Text = "Belum dapat terhubung. Periksa key/koneksi."; }
        finally { busy = false; models.IsEnabled = test.IsEnabled = provider.IsEnabled = true; }
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        if (busy) { status.Text = "Tunggu pengujian selesai dahulu."; return; }
        StoreDraft();
        try
        {
            AiProviders.Validate("gemini", drafts["gemini"].Model);
            AiProviders.Validate("gemini", drafts["gemini"].Model);
            foreach (var pair in drafts) if (pair.Value.Enabled && (secrets.ContainsKey(pair.Key) || ProviderSecrets.Exists(pair.Key))) AiProviders.Validate(pair.Key, pair.Value.Model);
            foreach (var pair in secrets) ProviderSecrets.Save(pair.Key, pair.Value);
            state.Settings.AiBackups = drafts.Values.Select(p => new AiBackup { Provider = p.Provider, Model = p.Model, Enabled = p.Enabled && ProviderSecrets.Exists(p.Provider) }).ToList();
            state.Settings.GeminiModel = drafts["gemini"].Model; state.Settings.GeminiEnabled = master.IsChecked == true;
            Storage.Save(state.Settings); state.RefreshLatin(); Close();
        }
        catch (GeminiFailure ex) { status.Text = ex.Message; } catch { status.Text = "Pengaturan/key belum dapat disimpan."; }
    }
}
