using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
namespace Toma.Windows;
public partial class IslandWindow : Window
{
    private readonly PlayerState state; private readonly Action openMain; private bool expanded, choosing, swiping; private Point start; private int page; private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) }; private bool placing, morphing; private int morphGeneration;
    private EventHandler? morphObserver;
    public IslandWindow(PlayerState state, Action openMain)
    {
        InitializeComponent();
        this.state = state;
        this.openMain = openMain;
        state.Changed += Refresh;
        state.ChooseRequested += ShowChoices;
        Loaded += (_, _) => Reposition();
        SizeChanged += (_, _) => Reposition();
        DpiChanged += (_, _) => Reposition();
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Closed += (_, _) => { timer.Stop(); if (morphObserver != null) CompositionTarget.Rendering -= morphObserver; state.Changed -= Refresh; state.ChooseRequested -= ShowChoices; };
        ApplyTheme();
        Refresh();
    }
    public void ApplyTheme()
    {
        var theme = expanded ? state.Settings.Appearance.Expanded : state.Settings.Appearance.Compact;
        Card.Background = IslandBrushes.Background(theme);
        Card.BorderBrush = IslandBrushes.Solid(theme.Border);
        Foreground = IslandBrushes.Solid(theme.Text);
        MiniLine.Foreground = TrackTitle.Foreground = IslandBrushes.Solid(theme.Text);
        MiniTitle.Foreground = TrackArtist.Foreground = IslandBrushes.Solid(theme.Muted);
        CurrentLine.Foreground = Progress.Foreground = IslandBrushes.Solid(theme.Accent);
        PlayButton.Background = IslandBrushes.Solid(theme.Accent);
        PlayButton.Foreground = IslandBrushes.Solid(theme.Background);
    }
    public void Reposition()
    {
        if (state.Showcase || placing || !IsLoaded)
            return;
        placing = true;
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            var monitor = screens.FirstOrDefault(s => s.DeviceName == state.Settings.Monitor) ?? System.Windows.Forms.Screen.PrimaryScreen ?? screens[0];
            var dpi = VisualTreeHelper.GetDpi(this);
            int width = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
            SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), monitor.WorkingArea.Left + (monitor.WorkingArea.Width - width) / 2, monitor.WorkingArea.Top + 6, 0, 0, 0x0001 | 0x0010 | 0x0040);
        }
        finally { placing = false; }
    }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    internal void ShowcaseExpand(bool value) => SetExpanded(value);
    private void SetExpanded(bool value)
    {
        if (value == expanded && !morphing)
        {
            Refresh();
            return;
        }
        double fromWidth = ActualWidth > 0 ? ActualWidth : Width, fromHeight = ActualHeight > 0 ? ActualHeight : 78;
        double fromCardWidth = Card.ActualWidth > 0 ? Card.ActualWidth : fromWidth - 20, fromCardHeight = Card.ActualHeight > 0 ? Card.ActualHeight : fromHeight - 20;
        double expandedOpacity = Expanded.Visibility == Visibility.Visible ? Expanded.Opacity : 0, capsuleOpacity = Capsule.Visibility == Visibility.Visible ? Capsule.Opacity : 0;
        int generation = ++morphGeneration;
        expanded = value;
        ApplyTheme();
        if (!value)
            choosing = false;
        if (morphObserver != null)
            CompositionTarget.Rendering -= morphObserver;
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        SizeToContent = SizeToContent.Manual;
        Width = fromWidth;
        Height = fromHeight;
        Card.BeginAnimation(WidthProperty, null);
        Card.BeginAnimation(HeightProperty, null);
        Card.Width = fromCardWidth;
        Card.Height = fromCardHeight;
        Card.HorizontalAlignment = HorizontalAlignment.Center;
        Card.VerticalAlignment = VerticalAlignment.Top;
        morphing = true;
        Card.IsHitTestVisible = false;
        Refresh();
        Expanded.BeginAnimation(OpacityProperty, null);
        Capsule.BeginAnimation(OpacityProperty, null);
        Expanded.Opacity = expandedOpacity;
        Capsule.Opacity = capsuleOpacity;
        Expanded.Width = 552;
        Capsule.Width = 318;
        Expanded.HorizontalAlignment = Capsule.HorizontalAlignment = HorizontalAlignment.Center;
        Expanded.VerticalAlignment = Capsule.VerticalAlignment = VerticalAlignment.Top;
        Expanded.Visibility = Capsule.Visibility = Visibility.Visible;
        Expanded.Measure(new Size(558, double.PositiveInfinity));
        Capsule.Measure(new Size(318, double.PositiveInfinity));
        double targetWidth = value ? 610 : 370, targetHeight = (value ? Expanded.DesiredSize.Height : Capsule.DesiredSize.Height) + 42;
        // Keep the native layered window stable during the morph. Animate the retained
        // WPF card visual instead of repeatedly resizing an HWND on every frame.
        Width = Math.Max(fromWidth, targetWidth);
        Height = Math.Max(fromHeight, targetHeight);
        Reposition();
        var duration = TimeSpan.FromMilliseconds(300);
        var ease = new QuarticEase { EasingMode = EasingMode.EaseInOut };
        var width = new DoubleAnimation(fromCardWidth, targetWidth - 20, duration) { EasingFunction = ease };
        var height = new DoubleAnimation(fromCardHeight, targetHeight - 20, duration) { EasingFunction = ease };
        var observedWidths = new System.Collections.Generic.HashSet<int>();
        morphObserver = (_, _) => observedWidths.Add((int)Math.Round(Card.ActualWidth * 100));
        CompositionTarget.Rendering += morphObserver;
        height.Completed += (_, _) =>
        {
            if (generation != morphGeneration)
                return;
            if (morphObserver != null)
                CompositionTarget.Rendering -= morphObserver;
            morphObserver = null;
            FileLog.Write($"Island visual morph {(value ? "expand" : "collapse")}: {fromCardWidth:0}→{targetWidth - 20:0}, distinct rendered widths={observedWidths.Count}, duration=300ms");
            Card.Width = targetWidth - 20;
            Card.Height = targetHeight - 20;
            Card.BeginAnimation(WidthProperty, null);
            Card.BeginAnimation(HeightProperty, null);
            Width = targetWidth;
            Height = targetHeight;
            Expanded.BeginAnimation(OpacityProperty, null);
            Capsule.BeginAnimation(OpacityProperty, null);
            Expanded.Opacity = Capsule.Opacity = 1;
            Expanded.RenderTransform = Capsule.RenderTransform = Transform.Identity;
            Expanded.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Capsule.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            Expanded.Width = Capsule.Width = double.NaN;
            Expanded.HorizontalAlignment = Capsule.HorizontalAlignment = HorizontalAlignment.Stretch;
            Card.Width = Card.Height = double.NaN;
            Card.HorizontalAlignment = HorizontalAlignment.Stretch;
            Card.VerticalAlignment = VerticalAlignment.Stretch;
            SizeToContent = SizeToContent.Height;
            Card.IsHitTestVisible = true;
            morphing = false;
            Refresh();
            UpdateLayout();
            Reposition();
        };
        Expanded.BeginAnimation(OpacityProperty, new DoubleAnimation(expandedOpacity, value ? 1 : 0, TimeSpan.FromMilliseconds(value ? 230 : 150)) { BeginTime = TimeSpan.FromMilliseconds(value ? 60 : 0) });
        Capsule.BeginAnimation(OpacityProperty, new DoubleAnimation(capsuleOpacity, value ? 0 : 1, TimeSpan.FromMilliseconds(value ? 120 : 200)) { BeginTime = TimeSpan.FromMilliseconds(value ? 0 : 90) });
        var bodyScale = new ScaleTransform(value ? 0.95 : 1, value ? 0.95 : 1);
        Expanded.RenderTransformOrigin = new Point(.5, 0);
        Expanded.RenderTransform = bodyScale;
        bodyScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(value ? 0.95 : 1, value ? 1 : 0.95, duration) { EasingFunction = ease });
        bodyScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(value ? 0.95 : 1, value ? 1 : 0.95, duration) { EasingFunction = ease });
        Card.BeginAnimation(WidthProperty, width);
        Card.BeginAnimation(HeightProperty, height);
    }
    private void Refresh()
    {
        MiniArt.Source = Art.Source = state.Cover;
        TrackTitle.Text = state.Track?.Title ?? "Putar musikmu";
        TrackArtist.Text = state.Track?.Artist is string artist && artist.Length > 0 ? artist : "Menunggu metadata artis";
        MiniTitle.Text = state.Track?.Title ?? "TOMA TOMA · klik untuk membuka";
        PlayButton.Content = state.Playing ? "Ⅱ" : "▶";
        LyricsPanel.Visibility = choosing ? Visibility.Collapsed : Visibility.Visible;
        ChoicesPanel.Visibility = choosing ? Visibility.Visible : Visibility.Collapsed;
        if (choosing)
            RefreshChoices();
        Tick();
    }
    private void Tick()
    {
        int index = state.Active;
        string text = index >= 0 ? state.Lines[index].Text : state.Lines.Count > 0 && state.Lines[0].Time < 0 ? "Lirik tanpa timestamp · buka aplikasi untuk membaca" : state.Track == null ? "Musikmu. Lirikmu." : state.Raw.Length > 0 ? "♪" : state.Status;
        if (CurrentLine.Text != text)
            CurrentLine.Text = text;
        MiniLine.Text = text;
        PlayButton.Content = state.Playing ? "Ⅱ" : "▶";
        Progress.Value = state.Track?.Duration > 0 ? Math.Clamp(state.Position / (state.Track.Duration * 10), 0, 100) : 0;
        Credit.Foreground = Ui.CreditBrush();
    }
    private void ShowChoices()
    {
        if (!state.Settings.Island)
            return;
        page = 0;
        choosing = true;
        if (!IsVisible)
            Show();
        SetExpanded(true);
        choosing = true;
        Refresh();
    }
    private void RefreshChoices()
    {
        page = Math.Clamp(page, 0, Math.Max(0, (state.Choices.Count - 1) / 2));
        PageLabel.Text = $"{page + 1}/{Math.Max(1, (state.Choices.Count + 1) / 2)}";
        int first = page * 2;
        Choice0.Visibility = first < state.Choices.Count ? Visibility.Visible : Visibility.Collapsed;
        Choice1.Visibility = first + 1 < state.Choices.Count ? Visibility.Visible : Visibility.Collapsed;
        if (first < state.Choices.Count)
            ChoiceText0.Text = state.Choices[first].Label + "\n" + state.Choices[first].Detail;
        if (first + 1 < state.Choices.Count)
            ChoiceText1.Text = state.Choices[first + 1].Label + "\n" + state.Choices[first + 1].Detail;
    }
    private void SwipeStart(object sender, MouseButtonEventArgs e)
    {
        if (Ui.HasButtonAncestor(e.OriginalSource as DependencyObject))
            return;
        start = PointToScreen(e.GetPosition(this));
        swiping = true;
    }
    private void SwipeMove(object sender, MouseEventArgs e)
    {
        if (!swiping || e.LeftButton != MouseButtonState.Pressed)
            return;
        Point point = PointToScreen(e.GetPosition(this));
        if (point.Y - start.Y < -38 && Math.Abs(point.X - start.X) < 140)
        {
            swiping = false;
            SetExpanded(false);
            e.Handled = true;
        }
    }
    private void SwipeEnd(object sender, MouseButtonEventArgs e)
    {
        if (!swiping)
            return;
        swiping = false;
        Point point = PointToScreen(e.GetPosition(this));
        if (!expanded && (point - start).Length < 12)
        {
            SetExpanded(true);
            e.Handled = true;
        }
    }
    private void OpenMain(object sender, RoutedEventArgs e) => openMain(); private async void Toggle(object sender, RoutedEventArgs e) => await state.Toggle(); private async void Previous(object sender, RoutedEventArgs e) => await state.Skip(false); private async void Next(object sender, RoutedEventArgs e) => await state.Skip(true);
    private async void Versions(object sender, RoutedEventArgs e)
    {
        if (state.Choices.Count <= 1)
            await state.Search(true, true);
        choosing = !choosing;
        page = 0;
        Refresh();
    }
    private void BackPage(object sender, RoutedEventArgs e)
    {
        page = Math.Max(0, page - 1);
        RefreshChoices();
    }
    private void NextPage(object sender, RoutedEventArgs e)
    {
        page = Math.Min(Math.Max(0, (state.Choices.Count - 1) / 2), page + 1);
        RefreshChoices();
    }
    private void Select(int index)
    {
        if (index < state.Choices.Count)
        {
            state.Select(state.Choices[index]);
            choosing = false;
            Refresh();
        }
    }
    private void SelectFirst(object sender, RoutedEventArgs e) => Select(page * 2); private void SelectSecond(object sender, RoutedEventArgs e) => Select(page * 2 + 1);
}
