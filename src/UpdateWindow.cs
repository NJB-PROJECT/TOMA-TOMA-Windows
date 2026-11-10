using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Toma.Windows;

public enum UpdateDecision { Continue, Exit, InstallerStarted }
public sealed class UpdateWindow : Window
{
    private readonly UpdateClient client;
    private UpdateRelease? release;
    private readonly CancellationTokenSource cancel = new();
    private readonly TextBlock title = new() { FontSize = 27, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 14, 0, 20) };
    private readonly ProgressBar progress = new() { Height = 5, IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 16) };
    private readonly Button install = new() { Content = "Update sekarang", IsEnabled = false, Padding = new Thickness(18, 10, 18, 10), Background = IslandBrushes.Solid("#C8BCFF"), Foreground = Brushes.Black };
    private readonly Button retry = new() { Content = "Cek ulang", Visibility = Visibility.Collapsed, Padding = new Thickness(12, 10, 12, 10) };
    public UpdateDecision Decision { get; private set; } = UpdateDecision.Exit;

    public UpdateWindow(UpdateClient client, UpdateRelease? available = null)
    {
        this.client = client; release = available;
        Title = "Lyrivane · Pembaruan"; Width = 510; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = IslandBrushes.Solid("#11121A"); Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI");
        var root = new StackPanel { Margin = new Thickness(28) }; Content = root;
        root.Children.Add(new TextBlock { Text = "Lyrivane  /  WINDOWS", Foreground = IslandBrushes.Solid("#B2A1E0"), FontSize = 10, Margin = new Thickness(0, 0, 0, 18) });
        root.Children.Add(title); root.Children.Add(details); root.Children.Add(progress);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; root.Children.Add(buttons);
        var exit = new Button { Content = "Keluar", Padding = new Thickness(12, 10, 12, 10) }; exit.Click += (_, _) => Close();
        buttons.Children.Add(exit); buttons.Children.Add(retry); buttons.Children.Add(install);
        install.Click += async (_, _) => await Install(); retry.Click += async (_, _) => await Check();
        Loaded += async (_, _) => { if (release == null) await Check(); else Offer(); };
        Closed += (_, _) => cancel.Cancel();
    }

    private async Task Check()
    {
        title.Text = "Memeriksa versi terbaru…";
        details.Text = "Sebentar, mengambil informasi release resmi. Versi terpasang: " + client.Current.ToString(3);
        install.IsEnabled = false; progress.Visibility = Visibility.Visible; progress.IsIndeterminate = true; retry.Visibility = Visibility.Collapsed;
        try
        {
            var checkedRelease = await client.Check(cancel.Token);
            if (checkedRelease != null) release = checkedRelease;
            if (cancel.IsCancellationRequested) return;
            if (release == null) { Decision = UpdateDecision.Continue; Close(); return; }
            Offer();
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
    }

    private void Offer()
    {
        title.Text = "Versi baru wajib dipasang.";
        details.Text = "Versi " + release!.Version + " tersedia. Perbarui Lyrivane sebelum melanjutkan. Tema dan lirik tersimpan tetap dipertahankan.\n\nInstaller MSI akan dibuka setelah unduhan dan SHA-256 terverifikasi.";
        progress.Visibility = Visibility.Collapsed; install.Content = "Update ke " + release.Version; install.IsEnabled = true; retry.Visibility = Visibility.Visible;
    }

    private async Task Install()
    {
        if (release == null) return;
        install.IsEnabled = false; retry.IsEnabled = false; progress.Visibility = Visibility.Visible; progress.IsIndeterminate = false; progress.Minimum = 0; progress.Maximum = 1;
        try
        {
            string path = await client.Download(release, new Progress<double>(value =>
            {
                progress.Value = value;
                details.Text = "Mengunduh installer resmi… " + value.ToString("P0") + "\nSetelah verifikasi selesai, ikuti wizard instalasi Windows.";
            }), cancel.Token);
            if (cancel.IsCancellationRequested) return;
            if (!await UpdateClient.Verify(path, release, cancel.Token)) throw new System.IO.InvalidDataException("Installer berubah setelah diunduh.");
            var start = new ProcessStartInfo("msiexec.exe") { UseShellExecute = true };
            start.ArgumentList.Add("/i"); start.ArgumentList.Add(path); start.ArgumentList.Add("/norestart");
            Process.Start(start); Decision = UpdateDecision.InstallerStarted; Close();
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception e)
        {
            details.Text = "Pembaruan belum berhasil. " + e.Message + "\nCoba unduh ulang atau cek koneksi. Aplikasi lama belum dapat dilanjutkan.";
            install.Content = "Coba unduh lagi"; install.IsEnabled = true; retry.IsEnabled = true; progress.Visibility = Visibility.Collapsed;
        }
    }
}
