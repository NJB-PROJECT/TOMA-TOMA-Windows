using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace Toma.Windows;
public sealed class GeminiRateLimited : GeminiFailure
{
    public DateTimeOffset Until { get; }
    public bool Daily { get; }
    public GeminiRateLimited(DateTimeOffset until, bool daily, string provider = "gemini") : base(daily ? "Kuota harian " + AiProviders.Label(provider) + " habis. Jeda sampai " + until.ToLocalTime().ToString("dd MMM HH:mm") + "." : AiProviders.Label(provider) + " dibatasi sementara. Tunggu sampai " + until.ToLocalTime().ToString("HH:mm:ss") + ".") { Until = until; Daily = daily; }
}
internal sealed record GeminiQuota(DateTimeOffset Until, bool Daily);
internal sealed class GeminiRequestGate
{
    internal static readonly GeminiRequestGate Shared = new(Path.Combine(Storage.DirectoryPath, "gemini-rate-state.json"), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(3));
    private readonly SemaphoreSlim serial = new(1, 1);
    private readonly Dictionary<string, GeminiQuota> blocked = new();
    private readonly Dictionary<string, DateTimeOffset> next = new();
    private readonly string? file;
    private readonly TimeSpan interval, settle;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    internal GeminiRequestGate(string? file, TimeSpan interval, TimeSpan settle, Func<DateTimeOffset>? now = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.file = file; this.interval = interval; this.settle = settle; this.now = now ?? (() => DateTimeOffset.UtcNow); this.delay = delay ?? Task.Delay;
        try { if (file != null && File.Exists(file) && new FileInfo(file).Length < 65536) foreach (var p in JsonSerializer.Deserialize<Dictionary<string, GeminiQuota>>(File.ReadAllText(file)) ?? new()) if (Regex.IsMatch(p.Key, "^[A-F0-9]{64}$") && p.Value.Until > this.now() && p.Value.Until < this.now().AddDays(2)) blocked[p.Key] = p.Value; } catch { }
    }
    private static string Scope(string key, string model) => System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key + "|" + model)));
    private void Save()
    {
        if (file == null) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(blocked)); File.Move(file + ".tmp", file, true); } catch { }
    }
    internal async Task<IDisposable> Enter(string key, string model, Action<string>? status, CancellationToken cancel)
    {
        string scope = Scope(key, model);
        // Debounce rapid song changes before consuming a request. Cancellation here costs no API request.
        if (settle > TimeSpan.Zero) await delay(settle, cancel);
        await serial.WaitAsync(cancel);
        try
        {
            if (blocked.TryGetValue(scope, out var quota) && quota.Until > now()) throw new GeminiRateLimited(quota.Until, quota.Daily, model.StartsWith("groq:") ? "groq" : model.StartsWith("openrouter:") ? "openrouter" : "gemini");
            if (next.TryGetValue(scope, out var ready) && ready > now()) { status?.Invoke("Antrean Gemini · jeda hemat kuota sampai " + ready.ToLocalTime().ToString("HH:mm:ss")); await delay(ready - now(), cancel); }
            cancel.ThrowIfCancellationRequested();
            next[scope] = now() + interval;
            return new Release(serial);
        }
        catch { serial.Release(); throw; }
    }
    internal GeminiRateLimited Limit(string key, string model, HttpResponseMessage response, string body, string provider = "gemini")
    {
        var quota = Parse(response, body, now(), provider); blocked[Scope(key, model)] = quota; Save();
        return new GeminiRateLimited(quota.Until, quota.Daily, provider);
    }
    internal static GeminiQuota Parse(HttpResponseMessage response, string body, DateTimeOffset now, string provider = "gemini")
    {
        bool daily = false, explicitReset = false; TimeSpan wait = TimeSpan.FromSeconds(60);
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > wait) wait = delta;
        if (response.Headers.RetryAfter?.Date is DateTimeOffset date && date - now > wait) wait = date - now;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("details", out var details)) foreach (var detail in details.EnumerateArray())
            {
                if (detail.TryGetProperty("retryDelay", out var retry))
                {
                    string value = retry.GetString() ?? "";
                    if (Regex.IsMatch(value, @"^\d+(\.\d+)?s$") && double.TryParse(value.TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > wait.TotalSeconds) wait = TimeSpan.FromSeconds(Math.Min(seconds, 172800));
                }
                if (detail.TryGetProperty("violations", out var violations)) foreach (var violation in violations.EnumerateArray())
                {
                    string id = violation.TryGetProperty("quotaId", out var quotaId) ? quotaId.GetString() ?? "" : "";
                    string metric = violation.TryGetProperty("quotaMetric", out var quotaMetric) ? quotaMetric.GetString() ?? "" : "";
                    daily |= id.Contains("PerDay", StringComparison.OrdinalIgnoreCase) || id.Contains("daily", StringComparison.OrdinalIgnoreCase) || metric.Contains("per_day", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch { }
        if (provider != "gemini")
        {
            if (response.Headers.TryGetValues("x-ratelimit-remaining-requests", out var remaining) && long.TryParse(System.Linq.Enumerable.FirstOrDefault(remaining), out var count) && count == 0)
            {
                daily = true;
                if (response.Headers.TryGetValues("x-ratelimit-reset-requests", out var resetHeaders))
                {
                    double seconds = 0; foreach (Match m in Regex.Matches(System.Linq.Enumerable.FirstOrDefault(resetHeaders) ?? "", @"(\d+(?:\.\d+)?)(ms|s|m|h|d)")) seconds += double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * (m.Groups[2].Value switch { "ms" => .001, "m" => 60, "h" => 3600, "d" => 86400, _ => 1 });
                    if (seconds > 0) explicitReset = true; if (seconds > wait.TotalSeconds) wait = TimeSpan.FromSeconds(Math.Min(seconds, 172800));
                }
            }
            try { using var doc = JsonDocument.Parse(body); string message = doc.RootElement.GetProperty("error").GetProperty("message").GetString() ?? ""; daily |= message.Contains("daily", StringComparison.OrdinalIgnoreCase) || message.Contains("per day", StringComparison.OrdinalIgnoreCase); } catch { }
        }
        DateTimeOffset until = now + (wait > TimeSpan.FromDays(2) ? TimeSpan.FromDays(2) : wait);
        if (daily && (provider == "gemini" || !explicitReset))
        {
            var pacific = provider == "gemini" ? TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time") : TimeZoneInfo.Utc;
            var tomorrow = TimeZoneInfo.ConvertTime(now, pacific).Date.AddDays(1);
            var reset = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(tomorrow, pacific), TimeSpan.Zero);
            if (reset > until) until = reset;
        }
        return new(until, daily);
    }
    private sealed class Release : IDisposable
    {
        private SemaphoreSlim? gate;
        internal Release(SemaphoreSlim gate) => this.gate = gate;
        public void Dispose() => Interlocked.Exchange(ref gate, null)?.Release();
    }
}
