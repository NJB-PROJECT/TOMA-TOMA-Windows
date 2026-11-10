using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
namespace Toma.Windows;
public sealed class AppearanceWindow : Window
{
    private readonly PlayerState state; private readonly IslandWindow island; private readonly IslandAppearance original; private bool saved, rebuilding; private readonly StackPanel editor = new(); private readonly Border preview = new() { CornerRadius = new CornerRadius(22), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 18), MinHeight = 108 }; private readonly ComboBox target = new(); private IslandStyle Theme => target.SelectedIndex == 1 ? state.Settings.Appearance.Expanded : state.Settings.Appearance.Compact;
    public AppearanceWindow(PlayerState state, IslandWindow island)
    {
        this.state = state;
        this.island = island;
        original = state.Settings.Appearance.Copy();
        state.Settings.Appearance = original.Copy();
        Title = "Personalisasi island";
        Width = 580;
        Height = 740;
        MinHeight = 520;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = IslandBrushes.Solid("#11121A");
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        var root = new DockPanel { Margin = new Thickness(26) };
        Content = root;
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Buat island milikmu.", FontSize = 27, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Tema kapsul dan kartu besar disimpan secara terpisah.", Foreground = Brushes.LightGray, Margin = new Thickness(0, 8, 0, 18), TextWrapping = TextWrapping.Wrap });
        target.Style = ComboStyle;
        target.Items.Add("Kapsul kecil");
        target.Items.Add("Kartu besar");
        target.SelectedIndex = 0;
        header.Children.Add(target);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var cancel = new Button { Content = "Batal", Padding = new Thickness(18, 9, 18, 9) };
        cancel.Click += (_, _) => Close();
        footer.Children.Add(cancel);
        var save = new Button { Content = "Simpan tema", Padding = new Thickness(18, 9, 18, 9), Background = IslandBrushes.Solid("#C8BCFF"), Foreground = Brushes.Black };
        save.Click += (_, _) => { saved = true; Storage.Save(state.Settings); Close(); };
        footer.Children.Add(save);
        root.Children.Add(new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 18, 0, 0) });
        target.SelectionChanged += (_, _) => Build();
        Closed += (_, _) => { if (!saved) { state.Settings.Appearance = original; Storage.Save(state.Settings); } island.ApplyTheme(); };
        Build();
    }
    private static readonly Style ComboStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
 <Setter Property="Foreground" Value="#F3F2F8"/><Setter Property="Padding" Value="12,9"/><Setter Property="ItemContainerStyle"><Setter.Value><Style TargetType="ComboBoxItem"><Setter Property="Padding" Value="12,8"/><Setter Property="Background" Value="#232432"/><Setter Property="Foreground" Value="#F3F2F8"/><Style.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter Property="Background" Value="#49405F"/></Trigger></Style.Triggers></Style></Setter.Value></Setter>
 <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid><ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="#232432" BorderBrush="#393B4B" BorderThickness="1" CornerRadius="9"><Grid><ContentPresenter TextElement.Foreground="#F3F2F8" Content="{Binding SelectionBoxItem,RelativeSource={RelativeSource AncestorType=ComboBox}}" Margin="12,9,30,9"/><TextBlock Text="⌄" Foreground="#C8BCFF" HorizontalAlignment="Right" Margin="0,0,12,0" VerticalAlignment="Center"/></Grid></Border></ControlTemplate></ToggleButton.Template></ToggleButton><Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False"><Border Background="#232432" BorderBrush="#49405F" BorderThickness="1" CornerRadius="9" MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}"><ScrollViewer MaxHeight="240"><ItemsPresenter/></ScrollViewer></Border></Popup></Grid></ControlTemplate></Setter.Value></Setter>
</Style>
""");
    private void Label(string value) => editor.Children.Add(new TextBlock { Text = value, Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 10, 0, 7) });
    private void Build()
    {
        rebuilding = true;
        editor.Children.Clear();
        editor.Children.Add(preview);
        Label("PRESET");
        var presets = new ComboBox { Style = ComboStyle };
        foreach (string name in new[] { "Custom / pilih preset", "Midnight", "Aurora", "Sunset", "Ocean" })
            presets.Items.Add(name);
        presets.SelectedIndex = 0;
        editor.Children.Add(presets);
        presets.SelectionChanged += (_, _) => { if (rebuilding || presets.SelectedIndex <= 0 || presets.SelectedItem is not string name) return; var selected = IslandStyle.Preset(name); if (target.SelectedIndex == 1) state.Settings.Appearance.Expanded = selected; else state.Settings.Appearance.Compact = selected; Build(); Update(); };
        Label("BACKGROUND");
        var modes = new ComboBox { Style = ComboStyle };
        foreach (string name in new[] { "Solid", "Gradient", "Image" })
            modes.Items.Add(name);
        modes.SelectedItem = Theme.Mode;
        editor.Children.Add(modes);
        modes.SelectionChanged += (_, _) => { if (rebuilding) return; Theme.Mode = modes.SelectedItem?.ToString() ?? "Solid"; Update(); };
        var colors = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        editor.Children.Add(colors);
        ColorButton(colors, "Dasar", () => Theme.Background, v => Theme.Background = v);
        ColorButton(colors, "Gradasi", () => Theme.Secondary, v => Theme.Secondary = v);
        ColorButton(colors, "Teks", () => Theme.Text, v => Theme.Text = v);
        ColorButton(colors, "Sekunder", () => Theme.Muted, v => Theme.Muted = v);
        ColorButton(colors, "Aksen lirik", () => Theme.Accent, v => Theme.Accent = v);
        ColorButton(colors, "Garis tepi", () => Theme.Border, v => Theme.Border = v);
        var images = new StackPanel { Orientation = Orientation.Horizontal };
        editor.Children.Add(images);
        var choose = new Button { Content = "Pilih gambar…", Margin = new Thickness(0, 8, 8, 0) };
        choose.Click += (_, _) => { var pick = new OpenFileDialog { Filter = "Gambar|*.png;*.jpg;*.jpeg;*.bmp" }; if (pick.ShowDialog(this) != true) return; if (new FileInfo(pick.FileName).Length > 20 * 1024 * 1024) { MessageBox.Show(this, "Ukuran gambar maksimal 20 MB."); return; } Theme.ImagePath = pick.FileName; Theme.Mode = "Image"; Build(); Update(); };
        images.Children.Add(choose);
        var remove = new Button { Content = "Lepas gambar", Margin = new Thickness(0, 8, 0, 0) };
        remove.Click += (_, _) => { Theme.ImagePath = ""; Theme.Mode = "Solid"; Build(); Update(); };
        images.Children.Add(remove);
        Label("OPASITAS BACKGROUND");
        var opacity = new Slider { Minimum = .4, Maximum = 1, Value = Theme.Opacity, TickFrequency = .05, IsSnapToTickEnabled = true };
        editor.Children.Add(opacity);
        opacity.ValueChanged += (_, _) => { if (rebuilding) return; Theme.Opacity = opacity.Value; Update(); };
        editor.Children.Add(new TextBlock { Text = "Gambar tetap di komputer ini. Pastikan warna lirik kontras dengan background.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 12, 0, 6) });
        rebuilding = false;
        Update();
    }
    private void ColorButton(Panel panel, string label, Func<string> get, Action<string> set)
    {
        var button = new Button { Content = label + "  " + get(), MinWidth = 145, Margin = new Thickness(0, 0, 8, 8), BorderBrush = IslandBrushes.Solid(get()), BorderThickness = new Thickness(0, 0, 0, 3) };
        button.Click += (_, _) => { using var picker = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(get()) }; if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK) return; set("#" + picker.Color.R.ToString("X2") + picker.Color.G.ToString("X2") + picker.Color.B.ToString("X2")); Build(); Update(); };
        panel.Children.Add(button);
    }
    private void Update()
    {
        preview.Background = IslandBrushes.Background(Theme);
        preview.BorderBrush = IslandBrushes.Solid(Theme.Border);
        preview.BorderThickness = new Thickness(1);
        var lines = new StackPanel();
        lines.Children.Add(new TextBlock { Text = target.SelectedIndex == 1 ? "Lyrivane  /  LIVE LYRICS" : "♫  Kapsul kecil", Foreground = IslandBrushes.Solid(Theme.Muted), FontSize = 10 });
        lines.Children.Add(new TextBlock { Text = "Musikmu. Warna pilihanmu.", TextWrapping = TextWrapping.Wrap, Foreground = IslandBrushes.Solid(Theme.Accent), FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        lines.Children.Add(new TextBlock { Text = "Preview langsung · perubahan belum disimpan", Foreground = IslandBrushes.Solid(Theme.Text), FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
        preview.Child = lines;
        island.ApplyTheme();
    }
}
