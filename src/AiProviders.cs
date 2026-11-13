using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace Toma.Windows;
public sealed class AiBackup
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public bool Enabled { get; set; }
}
internal static class AiProviders
{
    internal static readonly string[] Ids = { "gemini", "groq", "openrouter" };
    internal static string Label(string id) => id switch { "groq" => "Groq", "openrouter" => "OpenRouter", _ => "Gemini" };
    internal static string DefaultModel(string id) => id switch { "groq" => "qwen/qwen3.8-27b", "openrouter" => "openrouter/free", _ => "gemini-3.5-flash-lite" };
    internal static string KeyUrl(string id) => id switch { "groq" => "https://console.groq.com/keys", "openrouter" => "https://openrouter.ai/settings/keys", _ => "https://aistudio.google.com/api-keys" };
    internal static string Validate(string id, string model)
    {
        if (id == "gemini") return GeminiPhonetics.ValidModel(model);
        if (id == "groq" && new[] { "qwen/qwen3.8-27b", "openai/gpt-oss-120b", "openai/gpt-oss-20b" }.Contains(model)) return model;
        if (id == "openrouter" && (model == "openrouter/free" || (model.EndsWith(":free", StringComparison.Ordinal) && System.Text.RegularExpressions.Regex.IsMatch(model, @"^[a-zA-Z0-9_./:-]{1,140}$") && !model.Contains("..")))) return model;
        throw new GeminiFailure("Pilih model gratis/free-tier yang didukung untuk " + Label(id) + ".");
    }
}
internal sealed class OpenAiPhonetics
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(90), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
    private readonly HttpClient http;
    private readonly GeminiRequestGate gate;
    internal OpenAiPhonetics(HttpClient? client = null) { http = client ?? Http; gate = client == null ? GeminiRequestGate.Shared : new GeminiRequestGate(null, TimeSpan.Zero, TimeSpan.Zero); }
    internal async Task<GeminiRow[]> Convert(string provider, string key, string model, string title, string artist, IReadOnlyList<LyricLine> lines, int[] ids, CancellationToken token, Action<string>? status)
    {
        model = AiProviders.Validate(provider, model);
        if (key.Length == 0 || key.Length > 512 || key.Any(char.IsControl)) throw new GeminiFailure("Isi key " + AiProviders.Label(provider) + " terlebih dahulu.");
        using var lease = await gate.Enter(key, provider + ":" + model, status, token);
        string endpoint = provider == "groq" ? "https://api.groq.com/openai/v1/chat/completions" : "https://openrouter.ai/api/v1/chat/completions";
        var body = new Dictionary<string, object> { ["model"] = model, ["messages"] = new[] { new { role = "system", content = GeminiPhonetics.Instruction }, new { role = "user", content = GeminiPhonetics.Prompt(title, artist, lines, ids) } }, ["response_format"] = new { type = "json_object" }, ["max_tokens"] = 4096, ["temperature"] = .15, ["stream"] = false };
        if (provider == "openrouter") body["provider"] = new { max_price = new { prompt = 0, completion = 0 }, require_parameters = true };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new("Bearer", key);
        using var response = await http.SendAsync(request, token);
        string text = await response.Content.ReadAsStringAsync(token);
        if ((int)response.StatusCode == 429) throw gate.Limit(key, provider + ":" + model, response, text, provider);
        if (!response.IsSuccessStatusCode) throw new GeminiFailure(AiProviders.Label(provider) + " belum tersedia (HTTP " + (int)response.StatusCode + "). Periksa key, model atau kuota.");
        try
        {
            using var json = JsonDocument.Parse(text); var choice = json.RootElement.GetProperty("choices")[0];
            if (choice.TryGetProperty("finish_reason", out var finish) && finish.GetString() != "stop") throw new GeminiFailure("Hasil " + AiProviders.Label(provider) + " belum selesai.");
            var rows = GeminiPhonetics.Parse(choice.GetProperty("message").GetProperty("content").GetString() ?? "", ids);
            return rows.Select(r => r with { Provider = AiProviders.Label(provider), Model = model }).ToArray();
        }
        catch (GeminiFailure) { throw; } catch { throw new GeminiFailure("Format hasil " + AiProviders.Label(provider) + " tidak valid."); }
    }
    internal async Task<List<string>> Models(string provider, string key, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, provider == "groq" ? "https://api.groq.com/openai/v1/models" : "https://openrouter.ai/api/v1/models");
        if (key.Length > 0) request.Headers.Authorization = new("Bearer", key);
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new GeminiFailure("Daftar model " + AiProviders.Label(provider) + " tidak tersedia.");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)); var found = new List<string>();
        foreach (var row in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            string id = row.GetProperty("id").GetString() ?? "";
            if (provider == "openrouter")
            {
                if (!row.TryGetProperty("pricing", out var pricing) || !decimal.TryParse(pricing.GetProperty("prompt").ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var input) || !decimal.TryParse(pricing.GetProperty("completion").ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var output) || input != 0 || output != 0) continue;
            }
            try { found.Add(AiProviders.Validate(provider, id)); } catch (GeminiFailure) { }
        }
        if (provider == "openrouter") found.Insert(0, "openrouter/free");
        return found.Distinct().ToList();
    }
}
internal sealed class AiFailover
{
    private readonly Settings settings;
    private readonly Func<string, string> secret;
    private readonly Func<string, string, string, string, string, IReadOnlyList<LyricLine>, int[], CancellationToken, Action<string>?, Task<GeminiRow[]>> convert;
    internal AiFailover(Settings settings, Func<string, string>? secret = null, Func<string, string, string, string, string, IReadOnlyList<LyricLine>, int[], CancellationToken, Action<string>?, Task<GeminiRow[]>>? convert = null) { this.settings = settings; this.secret = secret ?? ProviderSecrets.Read; this.convert = convert ?? Convert; }
    private async Task<GeminiRow[]> Convert(string id, string key, string model, string title, string artist, IReadOnlyList<LyricLine> lines, int[] ids, CancellationToken token, Action<string>? status)
    {
        if (id == "gemini") return (await new GeminiPhonetics().Convert(key, model, title, artist, lines, ids, token, status)).Select(r => r with { Provider = "Gemini", Model = model }).ToArray();
        return await new OpenAiPhonetics().Convert(id, key, model, title, artist, lines, ids, token, status);
    }
    internal async Task<GeminiRow[]> Batch(string title, string artist, IReadOnlyList<LyricLine> lines, int[] ids, CancellationToken token, Action<string>? status)
    {
        GeminiRateLimited? earliest = null; bool any = false, unavailable = false;
        foreach (string id in AiProviders.Ids)
        {
            token.ThrowIfCancellationRequested();
            var account = settings.AiBackups.FirstOrDefault(p => p.Provider == id);
            if (account != null && !account.Enabled) continue;
            if (id != "gemini" && account == null) continue;
            string key = secret(id); if (key.Length == 0) continue;
            any = true; string model = id == "gemini" ? settings.GeminiModel : account!.Model;
            try { status?.Invoke("Cara baca AI · mencoba " + AiProviders.Label(id)); return await convert(id, key, AiProviders.Validate(id, model), title, artist, lines, ids, token, status); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (GeminiRateLimited quota) { if (earliest == null || quota.Until < earliest.Until) earliest = quota; status?.Invoke(AiProviders.Label(id) + " sedang cooldown · mencoba cadangan"); }
            catch (Exception error) when (error is GeminiFailure or HttpRequestException or OperationCanceledException) { unavailable = true; status?.Invoke(AiProviders.Label(id) + " gagal · mencoba cadangan"); }
        }
        if (!any) throw new GeminiFailure("Isi dan aktifkan minimal satu key AI. Cadangan tanpa key dilewati.");
        if (!unavailable && earliest != null) throw earliest;
        throw new GeminiFailure("Penyedia AI yang diaktifkan belum tersedia. Periksa key/kuota; lirik lokal tetap dipakai.");
    }
}
