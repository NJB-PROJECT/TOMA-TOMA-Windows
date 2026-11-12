using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Lucene.Net.Analysis.Ja;
using Lucene.Net.Analysis.Ja.TokenAttributes;
using Lucene.Net.Analysis.TokenAttributes;
namespace Toma.Windows;

public sealed record LatinReading(string Text, string Note);
/// <summary>Offline display annotation. Never changes original text or timestamps.</summary>
public sealed class Romanizer : IDisposable
{
    private static readonly ConcurrentDictionary<string, LatinReading> Cache = new();
    private static readonly Regex Kana = new("[\\u3041-\\u30ff]", RegexOptions.Compiled);
    private static readonly Regex Han = new("[\\u3400-\\u9fff]", RegexOptions.Compiled);
    private static readonly Regex JapaneseRun = new("[\\u3041-\\u30ff\\u3400-\\u9fff々〆ヶー]+", RegexOptions.Compiled);
    private static readonly Regex Arabic = new("[\\u0620-\\u064a\\u066e-\\u06d3]", RegexOptions.Compiled);
    private static readonly Regex NonLatin = new("[\\u0370-\\u052f\\u0590-\\u08ff\\u0900-\\u0e7f\\u3040-\\u30ff\\u3400-\\u9fff\\uac00-\\ud7af]", RegexOptions.Compiled);
    private static readonly Regex Vietnamese = new("[ăâđêôơưĂÂĐÊÔƠƯạảấầẩẫậắằẳẵặẹẻẽếềểễệịỉĩọỏốồổỗộớờởỡợụủũứừửữựỵỷỹ]", RegexOptions.Compiled);
    public static bool IsVietnamese(string text) => Vietnamese.IsMatch(text);
    private IntPtr general, kana;
    public static string Detect(IEnumerable<LyricLine> lines) => lines.Any(l => Kana.IsMatch(l.Text)) ? "Japanese" : "Auto";
    public static List<LyricLine> Annotate(IReadOnlyList<LyricLine> lines, CancellationToken cancel)
    {
        string hint = Detect(lines);
        using var engine = new Romanizer();
        var result = new List<LyricLine>(lines.Count);
        foreach (var line in lines)
        {
            cancel.ThrowIfCancellationRequested();
            var reading = engine.Read(line.Text, hint);
            result.Add(line with { Latin = reading.Text, LatinNote = reading.Note });
        }
        return result;
    }
    public LatinReading Read(string source, string hint = "Auto")
    {
        if (string.IsNullOrWhiteSpace(source) || (!NonLatin.IsMatch(source) && !IsVietnamese(source))) return new("", "");
        if (source.Length > 4096) return new("", "Baris terlalu panjang untuk Latin otomatis.");
        string key = hint + "|" + source;
        if (Cache.TryGetValue(key, out var saved)) return saved;
        LatinReading result;
        try
        {
            bool japanese = Kana.IsMatch(source) || (hint == "Japanese" && Han.IsMatch(source));
            string value;
            if (japanese)
            {
                value = JapaneseRun.Replace(source, m => Japanese(m.Value));
                // Do not read unrecognised Japanese kanji as Mandarin.
            }
            else value = Transform(source, false);
            value = Regex.Replace(value, @"\s+", " ").Trim();
            bool partial = NonLatin.IsMatch(value);
            string note = partial ? "Latin otomatis · sebagian pembacaan belum dikenali" : "Latin otomatis · perkiraan cara baca";
            if (IsVietnamese(source)) note = "Ejaan Vietnam disederhanakan · aktifkan Gemini untuk cara baca Indonesia";
            if (Arabic.IsMatch(source) && !Regex.IsMatch(source, "[\\u064b-\\u0652]"))
                note = "Latin perkiraan · sumber Arab tanpa harakat; vokal dapat tidak lengkap";
            if (value == source || string.IsNullOrWhiteSpace(value)) result = new("", "Cara baca belum tersedia untuk baris ini.");
            else result = new(value, note);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            FileLog.Write("Latin engine: " + ex.GetType().Name);
            result = new("", "Latin otomatis belum tersedia; lirik asli tetap ditampilkan.");
        }
        if (Cache.Count >= 2048) Cache.Clear();
        Cache.TryAdd(key, result);
        return result;
    }
    private string Japanese(string input)
    {
        using var tokenizer = new JapaneseTokenizer(new StringReader(input), null, false, JapaneseTokenizerMode.NORMAL);
        var terms = tokenizer.AddAttribute<ICharTermAttribute>();
        var reading = tokenizer.AddAttribute<IReadingAttribute>();
        tokenizer.Reset();
        var output = new List<string>();
        while (tokenizer.IncrementToken())
        {
            string text = reading.GetPronunciation() ?? reading.GetReading() ?? terms.ToString();
            if (string.IsNullOrEmpty(text)) text = terms.ToString();
            output.Add(Transform(text, true));
        }
        tokenizer.End();
        return string.Join(" ", output);
    }
    private string Transform(string text, bool japanese)
    {
        ref IntPtr handle = ref (japanese ? ref kana : ref general);
        if (handle == IntPtr.Zero)
        {
            string id = japanese ? "Katakana-Latin; Hiragana-Latin; Latin-ASCII" : "Any-Latin; Latin-ASCII";
            int status = 0;
            handle = utrans_openU(id, id.Length, 0, IntPtr.Zero, 0, IntPtr.Zero, ref status);
            if (status > 0 || handle == IntPtr.Zero) throw new InvalidOperationException("ICU transliterator unavailable: " + status);
        }
        int capacity = checked(text.Length * 24 + 256), length = text.Length, limit = length, error = 0;
        var buffer = new ushort[capacity]; for (int i = 0; i < length; i++) buffer[i] = text[i];
        utrans_transUChars(handle, buffer, ref length, capacity, 0, ref limit, ref error);
        if (error > 0 || length > capacity) throw new InvalidOperationException("ICU conversion failed: " + error);
        var value = new string(buffer.Take(length).Select(c => (char)c).ToArray());
        if (value.Any(c => char.IsControl(c) && !char.IsWhiteSpace(c))) throw new InvalidOperationException("Invalid transliteration output");
        return value;
    }
    public void Dispose() { if (general != IntPtr.Zero) { utrans_close(general); general = IntPtr.Zero; } if (kana != IntPtr.Zero) { utrans_close(kana); kana = IntPtr.Zero; } }
    [DllImport("icu.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr utrans_openU(string id, int idLength, int direction, IntPtr rules, int rulesLength, IntPtr parseError, ref int status);
    [DllImport("icu.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void utrans_transUChars(IntPtr trans, [In, Out] ushort[] text, ref int textLength, int capacity, int start, ref int limit, ref int status);
    [DllImport("icu.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void utrans_close(IntPtr trans);
}
