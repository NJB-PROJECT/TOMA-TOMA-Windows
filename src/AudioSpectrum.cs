using System;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using System.Windows;
using System.Windows.Media;
namespace Toma.Windows;
public sealed class SpectrumEngine
{
    public const int Count = 12, ExpandedCount = 22, Size = 4096;
    private static readonly double[] Edges = { 20, 40, 63, 100, 160, 250, 400, 630, 1000, 2500, 6300, 12000, 20000 };
    // Each expanded bar integrates its own measured frequency range; no cloned bars.
    public static readonly double[] ExpandedEdges = { 20, 40, 60, 80, 110, 150, 200, 270, 360, 480, 640, 850, 1100, 1500, 2000, 2700, 3600, 4800, 6400, 8500, 11000, 15000, 20000 };
    private readonly double[] ring = new double[Size], re = new double[Size], im = new double[Size], hann = new double[Size], cos = new double[Size / 2], sin = new double[Size / 2]; private int cursor, filled, pending;
    public float[] Levels { get; private set; } = new float[Count]; public float[] ExpandedLevels { get; private set; } = new float[ExpandedCount]; public double RmsDb { get; private set; } = -120;
    public float[] SubBassBands { get; private set; } = new float[4]; public float[] BassBands { get; private set; } = new float[16];
    public float SubBassLevel
    {
        get; private set;
    }
    public float BassLevel
    {
        get; private set;
    }
    public SpectrumEngine()
    {
        for (int i = 0; i < Size; i++)
            hann[i] = .5 - .5 * Math.Cos(2 * Math.PI * i / (Size - 1));
        for (int i = 0; i < Size / 2; i++)
        {
            cos[i] = Math.Cos(-2 * Math.PI * i / Size);
            sin[i] = Math.Sin(-2 * Math.PI * i / Size);
        }
    }
    public bool Add(double sample, int rate)
    {
        ring[cursor] = double.IsFinite(sample) ? sample : 0;
        cursor = (cursor + 1) & (Size - 1);
        filled = Math.Min(Size, filled + 1);
        pending++;
        if (filled < Size || pending < Math.Max(1, rate / 60))
            return false;
        pending = 0;
        Compute(rate);
        return true;
    }
    private void Compute(int rate)
    {
        double rms = 0;
        for (int i = 0; i < Size; i++)
        {
            double value = ring[(cursor + i) & (Size - 1)];
            rms += value * value;
            re[i] = value * hann[i];
            im[i] = 0;
        }
        RmsDb = 10 * Math.Log10(Math.Max(1e-12, rms / Size));
        for (int i = 1, j = 0; i < Size; i++)
        {
            int bit = Size >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
                (re[i], re[j]) = (re[j], re[i]);
        }
        for (int size = 2; size <= Size; size <<= 1)
        {
            int stride = Size / size;
            for (int start = 0; start < Size; start += size)
                for (int k = 0; k < size / 2; k++)
                {
                    int a = start + k, b = a + size / 2, t = k * stride;
                    double tr = re[b] * cos[t] - im[b] * sin[t], ti = re[b] * sin[t] + im[b] * cos[t];
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
        }
        Levels = Integrate(Edges, rate);
        ExpandedLevels = Integrate(ExpandedEdges, rate);
        var low = Integrate(new double[] { 20, 60, 250 }, rate);
        SubBassLevel = low[0];
        BassLevel = low[1];
        SubBassBands = Integrate(new double[] { 20, 30, 40, 50, 60 }, rate);
        var bassEdges = new double[17];
        for (int i = 0; i <= 16; i++)
            bassEdges[i] = 60 + 190.0 * i / 16;
        BassBands = Integrate(bassEdges, rate);
    }
    private float[] Integrate(double[] edges, int rate)
    {
        var levels = new float[edges.Length - 1];
        for (int band = 0; band < levels.Length; band++)
        {
            double power = 0;
            int bins = 0;
            for (int k = 1; k < Size / 2; k++)
            {
                double hz = k * (double)rate / Size;
                if (hz >= edges[band] && hz < edges[band + 1])
                {
                    power += (re[k] * re[k] + im[k] * im[k]) * 16 / (Size * Size);
                    bins++;
                }
            }
            double db = 20 * Math.Log10(Math.Max(1e-6, bins > 0 ? Math.Sqrt(power / bins) : 0));
            levels[band] = (float)Math.Clamp((db + 54) / 48, 0, 1);
        }
        return levels;
    }
}
public sealed class StereoSpectrumEngine
{
    public SpectrumEngine Left { get; } = new(); public SpectrumEngine Right { get; } = new();
    public bool Add(double left, double right, int rate)
    {
        bool ready = Left.Add(left, rate);
        Right.Add(right, rate);
        return ready;
    }
}
public sealed class AudioSpectrum : IDisposable
{
    private sealed class Loopback : WasapiCapture
    {
        public Loopback(MMDevice device) : base(device, false, 20) { }
        protected override AudioClientStreamFlags GetAudioClientStreamFlags() => AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
    }
    private Loopback? capture; private MMDevice? device; private SpectrumEngine engine = new(); private StereoSpectrumEngine stereo = new(); private volatile float[] latest = new float[12], latestExpanded = new float[22]; private volatile float[] left = new float[12], right = new float[12], leftExpanded = new float[22], rightExpanded = new float[22]; private long lastPacket; public long Packets
    {
        get; private set;
    }
    public double RmsDb { get; private set; } = -120; public string Status { get; private set; } = "Spektrum nonaktif";
    public float[] Levels => Environment.TickCount64 - lastPacket < 200 ? latest : new float[12];
    public float[] ExpandedLevels => Environment.TickCount64 - lastPacket < 200 ? latestExpanded : new float[22];
    public float[] LeftLevels => Environment.TickCount64 - lastPacket < 200 ? left : new float[12];
    public float[] RightLevels => Environment.TickCount64 - lastPacket < 200 ? right : new float[12];
    public float[] LeftExpanded => Environment.TickCount64 - lastPacket < 200 ? leftExpanded : new float[22];
    public float[] RightExpanded => Environment.TickCount64 - lastPacket < 200 ? rightExpanded : new float[22];
    public float[] LowBands(bool sub, bool rightChannel)
    {
        if (Environment.TickCount64 - lastPacket >= 200)
            return new float[sub ? 4 : 16];
        var e = rightChannel ? stereo.Right : stereo.Left;
        return sub ? e.SubBassBands : e.BassBands;
    }
    public float SubBassLevel => Environment.TickCount64 - lastPacket < 200 ? Math.Max(stereo.Left.SubBassLevel, stereo.Right.SubBassLevel) : 0;
    public float BassLevel => Environment.TickCount64 - lastPacket < 200 ? Math.Max(stereo.Left.BassLevel, stereo.Right.BassLevel) : 0;
    public void Start()
    {
        Stop();
        engine = new();
        stereo = new();
        try
        {
            using var devices = new MMDeviceEnumerator();
            device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            capture = new Loopback(device);
            var format = capture.WaveFormat;
            int width = format.BitsPerSample / 8, channels = format.Channels, step = format.BlockAlign;
            bool floating = format.Encoding == WaveFormatEncoding.IeeeFloat || (format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
            capture.DataAvailable += (_, e) =>
            {
                for (int at = 0; at + step <= e.BytesRecorded; at += step)
                {
                    double Read(int channel)
                    {
                        int index = at + channel * width;
                        return floating ? (width == 8 ? BitConverter.ToDouble(e.Buffer, index) : BitConverter.ToSingle(e.Buffer, index)) : width == 2 ? BitConverter.ToInt16(e.Buffer, index) / 32768.0 : width == 3 ? (e.Buffer[index] | e.Buffer[index + 1] << 8 | (sbyte)e.Buffer[index + 2] << 16) / 8388608.0 : width == 4 ? BitConverter.ToInt32(e.Buffer, index) / 2147483648.0 : width == 1 ? (e.Buffer[index] - 128) / 128.0 : 0;
                    }
                    // Windows interleaved stereo/surround: first pair is front left/right.
                    // Mono is duplicated explicitly; rear/center/LFE are not presented as stereo.
                    double l = Read(0), r = channels > 1 ? Read(1) : l;
                    stereo.Add(l, r, format.SampleRate);
                    double mono = 0;
                    for (int c = 0; c < channels; c++)
                        mono += Read(c);
                    if (engine.Add(mono / channels, format.SampleRate))
                    {
                        latest = engine.Levels;
                        latestExpanded = engine.ExpandedLevels;
                        left = stereo.Left.Levels;
                        right = stereo.Right.Levels;
                        leftExpanded = stereo.Left.ExpandedLevels;
                        rightExpanded = stereo.Right.ExpandedLevels;
                        lastPacket = Environment.TickCount64;
                        RmsDb = Math.Max(stereo.Left.RmsDb, stereo.Right.RmsDb);
                        Packets++;
                    }
                }
            };
            capture.RecordingStopped += (_, e) => { if (e.Exception != null) { Status = "Audio berubah · matikan lalu aktifkan spektrum"; FileLog.Write(e.Exception.Message); } };
            capture.StartRecording();
            Status = "Stereo nyata · " + (channels > 2 ? "Front L/R dari " + channels + " kanal · " : channels == 1 ? "Mono → L/R · " : "L/R · ") + device.FriendlyName;
        }
        catch (Exception e) { Status = "Keluaran audio belum tersedia"; FileLog.Write("WASAPI: " + e.Message); Stop(); }
    }
    private long showcaseSamples;
    internal void ShowcaseFrame(double seconds)
    {
        int count = showcaseSamples == 0 ? 4096 : 800;
        for (int i = 0; i < count; i++)
        {
            double t = showcaseSamples++ / 48000.0, l = .28 * (.6 + .4 * Math.Sin(seconds * 7)) * Math.Sin(2 * Math.PI * 90 * t) + .12 * Math.Sin(2 * Math.PI * 950 * t), r = .25 * (.6 + .4 * Math.Cos(seconds * 5)) * Math.Sin(2 * Math.PI * 120 * t) + .08 * Math.Sin(2 * Math.PI * 9000 * t);
            stereo.Add(l, r, 48000);
            engine.Add((l + r) / 2, 48000);
        }
        left = stereo.Left.Levels;
        right = stereo.Right.Levels;
        leftExpanded = stereo.Left.ExpandedLevels;
        rightExpanded = stereo.Right.ExpandedLevels;
        latest = engine.Levels;
        latestExpanded = engine.ExpandedLevels;
        lastPacket = Environment.TickCount64;
    }
    public void Stop()
    {
        if (capture != null)
        {
            try
            {
                capture.StopRecording();
                capture.Dispose();
            }
            catch { }
            capture = null;
        }
        device?.Dispose();
        device = null;
        latest = new float[12];
        latestExpanded = new float[22];
        left = new float[12];
        right = new float[12];
        leftExpanded = new float[22];
        rightExpanded = new float[22];
        lastPacket = 0;
    }
    public void Dispose() => Stop();
}
public sealed class SpectrumView : FrameworkElement
{
    public static readonly DependencyProperty BandCountProperty = DependencyProperty.Register(nameof(BandCount), typeof(int), typeof(SpectrumView), new FrameworkPropertyMetadata(22, FrameworkPropertyMetadataOptions.AffectsRender));
    public int BandCount
    {
        get => (int)GetValue(BandCountProperty); set => SetValue(BandCountProperty, value);
    }
    public static AudioSpectrum? Audio; private bool listening; private long lastDraw; private readonly float[] smoothL = new float[22], smoothR = new float[22], peakL = new float[22], peakR = new float[22];
    private readonly Brush leftBrush = new LinearGradientBrush(Color.FromRgb(59, 224, 235), Color.FromRgb(129, 255, 190), 0);
    private readonly Brush rightBrush = new LinearGradientBrush(Color.FromRgb(166, 122, 255), Color.FromRgb(255, 111, 177), 0);
    private readonly Brush rail = new SolidColorBrush(Color.FromArgb(22, 170, 180, 220));
    public SpectrumView()
    {
        ToolTip = "Spektrum stereo nyata · L atas / R bawah · bass di kiri, treble di kanan. Surround: kanal front L/R.";
        Loaded += (_, _) => { if (!listening) { CompositionTarget.Rendering += Render; listening = true; } };
        Unloaded += (_, _) => { CompositionTarget.Rendering -= Render; listening = false; };
    }
    private double elapsed = .016;
    private void Render(object? sender, EventArgs args)
    {
        long now = Environment.TickCount64;
        if (now - lastDraw < 15)
            return;
        elapsed = lastDraw == 0 ? .016 : Math.Clamp((now - lastDraw) / 1000.0, .001, .1);
        lastDraw = now;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;
        var l = BandCount == 12 ? Audio?.LeftLevels : Audio?.LeftExpanded;
        var r = BandCount == 12 ? Audio?.RightLevels : Audio?.RightExpanded;
        int count = BandCount == 12 ? 12 : 22;
        bool labels = ActualWidth >= 100 && ActualHeight >= 48;
        double inset = labels ? 15 : 0, usable = ActualWidth - inset, half = ActualHeight / 2, gap = labels ? 5 : 2, height = Math.Max(1, half - gap), step = usable / count;
        if (labels)
        {
            DrawLabel(dc, "L", leftBrush, 0, 2);
            DrawLabel(dc, "R", rightBrush, 0, half + 2);
        }
        for (int channel = 0; channel < 2; channel++)
        {
            var source = channel == 0 ? l : r;
            var values = channel == 0 ? smoothL : smoothR;
            var peaks = channel == 0 ? peakL : peakR;
            Brush brush = channel == 0 ? leftBrush : rightBrush;
            double bottom = (channel + 1) * half - 2;
            for (int i = 0; i < count; i++)
            {
                float target = source?[i] ?? 0;
                double tau = target > values[i] ? .018 : .085;
                values[i] += (target - values[i]) * (float)(1 - Math.Exp(-elapsed / tau));
                peaks[i] = Math.Max(values[i], peaks[i] - (float)(elapsed * .7));
                double w = Math.Max(1, step * .55), x = inset + i * step + (step - w) / 2, h = Math.Max(1, values[i] * height);
                dc.DrawRoundedRectangle(rail, null, new Rect(x, bottom - height, w, height), 1, 1);
                dc.DrawRoundedRectangle(brush, null, new Rect(x, bottom - h, w, h), 1, 1);
                if (peaks[i] > .04)
                    dc.DrawRectangle(brush, null, new Rect(x, bottom - peaks[i] * height, w, 1));
            }
        }
    }
    private void DrawLabel(DrawingContext dc, string text, Brush brush, double x, double y)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new Point(x, y));
    }
}

public sealed class LowFrequencyMeters : FrameworkElement
{
    private bool listening; private long last; private readonly float[,] levels = new float[4, 16], peaks = new float[4, 16];
    private readonly Brush left = new LinearGradientBrush(Color.FromRgb(59, 224, 235), Color.FromRgb(129, 255, 190), 0), right = new LinearGradientBrush(Color.FromRgb(166, 122, 255), Color.FromRgb(255, 111, 177), 0), dim = new SolidColorBrush(Color.FromRgb(33, 32, 43));
    public LowFrequencyMeters()
    {
        Height = 106;
        ToolTip = "Spektrum frekuensi rendah stereo nyata. Sub-Bass: 4 rentang 10 Hz; Bass: 16 rentang. Jumlah band mengikuti resolusi FFT agar tidak membuat batang duplikat. Front L/R, bukan kanal LFE.";
        Loaded += (_, _) => { if (!listening) { CompositionTarget.Rendering += Tick; listening = true; } };
        Unloaded += (_, _) => { CompositionTarget.Rendering -= Tick; listening = false; };
    }
    private void Tick(object? sender, EventArgs args)
    {
        long now = Environment.TickCount64;
        if (now - last < 15)
            return;
        double dt = last == 0 ? .016 : Math.Clamp((now - last) / 1000.0, .001, .1);
        last = now;
        for (int panel = 0; panel < 2; panel++)
            for (int c = 0; c < 2; c++)
            {
                int row = panel * 2 + c;
                var target = SpectrumView.Audio?.LowBands(panel == 0, c == 1);
                int count = panel == 0 ? 4 : 16;
                for (int j = 0; j < count; j++)
                {
                    float v = target?[j] ?? 0;
                    levels[row, j] += (v - levels[row, j]) * (float)(1 - Math.Exp(-dt / (v > levels[row, j] ? .018 : .085)));
                    peaks[row, j] = Math.Max(levels[row, j], peaks[row, j] - (float)(dt * .7));
                }
            }
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 100)
            return;
        double gap = 22, w = (ActualWidth - gap) / 2;
        for (int panel = 0; panel < 2; panel++)
        {
            double x = panel * (w + gap);
            Text(dc, panel == 0 ? "Sub-Bass" : "Bass", x, 0, Brushes.White, 11);
            Text(dc, panel == 0 ? "20–60 Hz" : "60–250 Hz", x + w - 65, 1, Brushes.Gray, 9);
            int count = panel == 0 ? 4 : 16;
            double step = (w - 17) / count;
            for (int c = 0; c < 2; c++)
            {
                int row = panel * 2 + c;
                Brush brush = c == 0 ? left : right;
                double bottom = 58 + c * 39;
                Text(dc, c == 0 ? "L" : "R", x, bottom - 27, brush, 9);
                for (int j = 0; j < count; j++)
                {
                    double bx = x + 17 + j * step + 2, bw = Math.Max(1, Math.Min(7, step * .55)), h = Math.Max(1, levels[row, j] * 30);
                    dc.DrawRoundedRectangle(dim, null, new Rect(bx, bottom - 30, bw, 30), 1, 1);
                    dc.DrawRoundedRectangle(brush, null, new Rect(bx, bottom - h, bw, h), 1, 1);
                    if (peaks[row, j] > .04)
                        dc.DrawRectangle(brush, null, new Rect(bx, bottom - peaks[row, j] * 30, bw, 1));
                }
            }
        }
    }
    private void Text(DrawingContext dc, string text, double x, double y, Brush brush, double size) => dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
}
