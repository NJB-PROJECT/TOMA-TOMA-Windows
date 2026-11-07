using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Toma.Windows;
public sealed class IslandStyle
{
    public string Background { get; set; } = "#08090D"; public string Secondary { get; set; } = "#221A36"; public string Text { get; set; } = "#F3F2F8"; public string Muted { get; set; } = "#A49BAF"; public string Accent { get; set; } = "#DDD2FF"; public string Border { get; set; } = "#353440"; public string Mode { get; set; } = "Solid"; public string ImagePath { get; set; } = ""; public double Opacity { get; set; } = 1;
    public IslandStyle Copy() => (IslandStyle)MemberwiseClone();
    public static IslandStyle Preset(string name) => name switch
    {
        "Aurora" => new() { Mode = "Gradient", Background = "#082D30", Secondary = "#142442", Text = "#F0FFFA", Muted = "#9BD2C4", Accent = "#80FFE0", Border = "#38766D" },
        "Sunset" => new() { Mode = "Gradient", Background = "#381A30", Secondary = "#602A22", Text = "#FFF4E7", Muted = "#E4B5A8", Accent = "#FFC69A", Border = "#96566D" },
        "Ocean" => new() { Mode = "Gradient", Background = "#091A38", Secondary = "#123D58", Text = "#EBF7FF", Muted = "#9BBEDB", Accent = "#83DFFF", Border = "#356386" },
        _ => new()
    };
}
public sealed class IslandAppearance
{
    public IslandStyle Compact { get; set; } = new(); public IslandStyle Expanded { get; set; } = new(); public IslandAppearance Copy() => new() { Compact = Compact.Copy(), Expanded = Expanded.Copy() };
}
public static class IslandBrushes
{
    public static Color Color(string? value, string fallback = "#08090D")
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value ?? fallback);
        }
        catch { return (Color)ColorConverter.ConvertFromString(fallback); }
    }
    public static SolidColorBrush Solid(string? value, string fallback = "#F3F2F8")
    {
        var brush = new SolidColorBrush(Color(value, fallback));
        brush.Freeze();
        return brush;
    }
    public static Brush Background(IslandStyle style)
    {
        Brush brush;
        if (style.Mode == "Image" && File.Exists(style.ImagePath))
        {
            try
            {
                var file = new FileInfo(style.ImagePath);
                if (file.Length > 20 * 1024 * 1024)
                    throw new IOException("Background maksimal 20 MB.");
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 1200;
                image.UriSource = new Uri(file.FullName);
                image.EndInit();
                image.Freeze();
                brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            }
            catch { brush = new SolidColorBrush(Color(style.Background)); }
        }
        else if (style.Mode == "Gradient")
            brush = new LinearGradientBrush(Color(style.Background), Color(style.Secondary), 35);
        else
            brush = new SolidColorBrush(Color(style.Background));
        brush.Opacity = double.IsFinite(style.Opacity) ? Math.Clamp(style.Opacity, .4, 1) : 1;
        brush.Freeze();
        return brush;
    }
}
