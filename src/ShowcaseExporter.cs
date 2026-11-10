using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Toma.Windows;
/** Native WPF visual export for reproducible, privacy-safe feature clips. No desktop capture. */
internal static class ShowcaseExporter
{
    public static ImageSource Cover { get; } = CreateCover();
    private static ImageSource CreateCover()
    {
        return new BitmapImage(new Uri("pack://application:,,,/Assets/Lyrivane.png"));
    }

    public static async Task Export(string directory)
    {
        Directory.CreateDirectory(directory);
        using var state = new PlayerState(true);
        state.ShowcaseFrame(0);
        var window = new IslandWindow(state, () => { }) { Left = -32000, Top = -32000 };
        window.Show();
        window.UpdateLayout();
        var main = new MainWindow(state, window) { Left = -32000, Top = -32000, Width = 1080, Height = 760 };
        main.Show(); main.UpdateLayout();
        var mainBitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        mainBitmap.Render(main);
        var mainEncoder = new PngBitmapEncoder(); mainEncoder.Frames.Add(BitmapFrame.Create(mainBitmap));
        using (var mainFile = File.Create(Path.Combine(directory, "desktop.png"))) mainEncoder.Save(mainFile);
        main.Exiting = true; main.Close();
        for (int frame = 0; frame < 320; frame++)
        {
            double time = frame / 20.0;
            state.ShowcaseFrame(time);
            if (frame == 40)
                window.ShowcaseExpand(true);
            if (frame == 110)
            {
                state.Settings.Appearance.Expanded = IslandStyle.Preset("Aurora");
                window.ApplyTheme();
            }
            if (frame == 160)
            {
                state.Settings.Appearance.Expanded = IslandStyle.Preset("Sunset");
                window.ApplyTheme();
            }
            if (frame == 210)
            {
                state.Settings.Appearance.Expanded = IslandStyle.Preset("Ocean");
                window.ApplyTheme();
            }
            if (frame == 270)
            {
                state.Settings.Appearance.Compact = IslandStyle.Preset("Aurora");
                window.ShowcaseExpand(false);
            }
            await Task.Delay(50);
            window.UpdateLayout();
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(16, 18, 29)), null, new Rect(0, 0, 680, 620));
                dc.DrawText(Text("Lyrivane", 28, Brushes.White), new Point(28, 20));
                dc.DrawText(Text("WINDOWS   /   YOUR MUSIC, YOUR ISLAND", 10, Brushes.LightSlateGray), new Point(29, 58));
                dc.PushTransform(new TranslateTransform((680 - window.ActualWidth) / 2, 94));
                dc.DrawRectangle(new VisualBrush(window), null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
                dc.Pop();
                dc.DrawText(Text("FEATURE DEMO · ORIGINAL SAMPLE TEXT · GENERATED PCM", 10, Brushes.LightSlateGray), new Point(28, 582));
            }
            var bitmap = new RenderTargetBitmap(680, 620, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, $"frame-{frame:0000}.png"));
            encoder.Save(output);
        }
        window.Close();
    }
    private static FormattedText Text(string text, double size, Brush brush) => new(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1);
}
