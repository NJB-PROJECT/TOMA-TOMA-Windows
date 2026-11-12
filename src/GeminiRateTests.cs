using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace Toma.Windows;
internal static class GeminiRateTests
{
    private sealed class Stub : HttpMessageHandler
    {
        public int Calls;
        public int MaximumBatch;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            using var prompt = JsonDocument.Parse(body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!);
            var inputs = prompt.RootElement.GetProperty("lines").EnumerateArray().ToArray(); MaximumBatch = Math.Max(MaximumBatch, inputs.Length);
            var output = JsonSerializer.Serialize(new { rows = inputs.Select(l => new { id = l.GetProperty("id").GetInt32(), language = "Vietnam", supported = true, reading = "cara baca contoh" }) });
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = output } } } } } })) };
        }
    }
    private sealed class RateStub : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            var response = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("{\"error\":{\"details\":[{\"retryDelay\":\"120s\"}]}}") };
            return Task.FromResult(response);
        }
    }
    private static bool Async(Func<Task<bool>> action) => Task.Run(action).GetAwaiter().GetResult();
    private static void Cleanup(string path) { foreach (var f in Directory.Exists(path) ? Directory.GetFiles(path) : Array.Empty<string>()) File.Delete(f); if (Directory.Exists(path)) Directory.Delete(path); }
    internal static void Run(Action<string, Func<bool>> test)
    {
        test("Gemini quota: Retry-After diikuti", () => { using var response = new HttpResponseMessage((HttpStatusCode)429); response.Headers.RetryAfter = new(System.TimeSpan.FromSeconds(180)); var now = DateTimeOffset.UtcNow; return GeminiRequestGate.Parse(response, "{}", now).Until >= now.AddSeconds(180); });
        test("Gemini quota: RetryInfo desimal diikuti", () => { using var response = new HttpResponseMessage((HttpStatusCode)429); var now = DateTimeOffset.UtcNow; return GeminiRequestGate.Parse(response, "{\"error\":{\"details\":[{\"retryDelay\":\"125.5s\"}]}}", now).Until >= now.AddSeconds(125.5); });
        test("Gemini quota: harian tidak dianggap jeda satu menit", () => { using var response = new HttpResponseMessage((HttpStatusCode)429); var now = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero); var quota = GeminiRequestGate.Parse(response, "{\"error\":{\"details\":[{\"violations\":[{\"quotaId\":\"GenerateRequestsPerDayPerProjectPerModel-FreeTier\"}]}]}}", now); return quota.Daily && quota.Until == new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero); });
        test("Gemini quota: malformed body tetap konservatif", () => { using var response = new HttpResponseMessage((HttpStatusCode)429); var now = DateTimeOffset.UtcNow; return GeminiRequestGate.Parse(response, "not json", now).Until >= now.AddMinutes(1); });
        test("Gemini quota: jarak minimal permintaan berlaku lintas antrean", () => Async(async () => { var now = DateTimeOffset.UtcNow; var waits = new List<TimeSpan>(); var gate = new GeminiRequestGate(null, TimeSpan.FromSeconds(20), TimeSpan.Zero, () => now, (wait, token) => { token.ThrowIfCancellationRequested(); waits.Add(wait); now += wait; return Task.CompletedTask; }); using (await gate.Enter("dummy", "model", null, CancellationToken.None)) { } using (await gate.Enter("dummy", "model", null, CancellationToken.None)) { } return waits.Count == 1 && waits[0] >= TimeSpan.FromSeconds(20); }));
        test("Gemini quota: debounce dibatalkan sebelum konsumsi request", () => Async(async () => { using var c = new CancellationTokenSource(); var gate = new GeminiRequestGate(null, TimeSpan.Zero, TimeSpan.FromSeconds(3), delay: (_, token) => { c.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }); try { using var lease = await gate.Enter("dummy", "model", null, c.Token); return false; } catch (OperationCanceledException) { return true; } }));
        test("Gemini quota: cooldown bertahan sesudah restart tanpa plaintext key", () => Async(async () => { string dir = Path.Combine(Path.GetTempPath(), "LyrivaneRateTest-" + Guid.NewGuid().ToString("N")); string file = Path.Combine(dir, "rate.json"); try { var now = DateTimeOffset.UtcNow; var gate = new GeminiRequestGate(file, TimeSpan.Zero, TimeSpan.Zero, () => now); using var response = new HttpResponseMessage((HttpStatusCode)429); gate.Limit("dummy-private-key", "model", response, "{}"); var restored = new GeminiRequestGate(file, TimeSpan.Zero, TimeSpan.Zero, () => now); try { using var lease = await restored.Enter("dummy-private-key", "model", null, CancellationToken.None); return false; } catch (GeminiRateLimited) { return !File.ReadAllText(file).Contains("dummy-private-key"); } } finally { Cleanup(dir); } }));
        test("Gemini quota: HTTP 429 menghentikan panggilan berikutnya", () => Async(async () =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "LyrivaneRateTest-" + Guid.NewGuid().ToString("N")); var gate = new GeminiRequestGate(null, TimeSpan.Zero, TimeSpan.Zero); using var handler = new RateStub(); using var http = new HttpClient(handler);
            try { var service = new GeminiPhonetics(http, dir, gate); for (int n = 0; n < 2; n++) { try { await service.Convert("dummy", "gemini-3.5-flash-lite", "", "", new[] { new LyricLine(0, "Contoh") }, new[] { 0 }, CancellationToken.None); return false; } catch (GeminiRateLimited) { } } return handler.Calls == 1; } finally { Cleanup(dir); }
        }));
        test("Gemini quota: cache tetap tersedia selama cooldown", () => Async(async () =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "LyrivaneRateTest-" + Guid.NewGuid().ToString("N")); var gate = new GeminiRequestGate(null, TimeSpan.Zero, TimeSpan.Zero); using var handler = new Stub(); using var http = new HttpClient(handler); bool available = false;
            try { var service = new GeminiPhonetics(http, dir, gate); var source = new[] { new LyricLine(0, "Contoh") }; await service.Annotate("dummy", "gemini-3.5-flash-lite", "", "", source, 0, (_, _, _) => { }, CancellationToken.None); using var response = new HttpResponseMessage((HttpStatusCode)429); gate.Limit("dummy", "gemini-3.5-flash-lite", response, "{}"); await service.Annotate("dummy", "gemini-3.5-flash-lite", "", "", source, 0, (lines, _, _) => available = lines[0].Latin.Length > 0, CancellationToken.None); return available && handler.Calls == 1; } finally { Cleanup(dir); }
        }));
        test("Gemini quota: satu lagu 150 baris memakai tiga request", () => Async(async () => { string dir = Path.Combine(Path.GetTempPath(), "LyrivaneRateTest-" + Guid.NewGuid().ToString("N")); using var handler = new Stub(); using var http = new HttpClient(handler); try { var lines = Enumerable.Range(0, 150).Select(i => new LyricLine(i * 1000, "Contoh baris nomor " + i)).ToArray(); await new GeminiPhonetics(http, dir).Annotate("dummy", "gemini-3.5-flash-lite", "", "", lines, 0, (_, _, _) => { }, CancellationToken.None); return handler.Calls == 3 && handler.MaximumBatch == 64; } finally { Cleanup(dir); } }));
        test("Gemini quota: chorus identik hanya diproses sekali", () => Async(async () => { string dir = Path.Combine(Path.GetTempPath(), "LyrivaneRateTest-" + Guid.NewGuid().ToString("N")); using var handler = new Stub(); using var http = new HttpClient(handler); bool all = false; try { var lines = Enumerable.Range(0, 100).Select(i => new LyricLine(i * 1000, "Contoh chorus sama")).ToArray(); await new GeminiPhonetics(http, dir).Annotate("dummy", "gemini-3.5-flash-lite", "", "", lines, 0, (result, _, _) => all = result.All(l => l.Latin.Length > 0), CancellationToken.None); return handler.Calls == 1 && handler.MaximumBatch == 1 && all; } finally { Cleanup(dir); } }));
        test("Gemini quota: cache dokumen dipakai untuk metadata/timing berbeda", () => Async(async () => { string dir = Path.Combine(Path.GetTempPath(), "LyrivaneRateTest-" + Guid.NewGuid().ToString("N")); using var handler = new Stub(); using var http = new HttpClient(handler); bool preserved = false; try { var service = new GeminiPhonetics(http, dir); await service.Annotate("dummy", "gemini-3.5-flash-lite", "original", "artist", new[] { new LyricLine(1000, "Contoh kalimat") }, 0, (_, _, _) => { }, CancellationToken.None); foreach (var document in Directory.GetFiles(dir, "doc-*.json")) File.Delete(document); await service.Annotate("dummy", "gemini-3.5-flash-lite", "original", "artist", new[] { new LyricLine(1000, "Contoh kalimat") }, 0, (_, _, _) => { }, CancellationToken.None); await service.Annotate("dummy", "gemini-3.5-flash-lite", "remix", "another", new[] { new LyricLine(9000, "Contoh kalimat") }, 0, (result, _, _) => preserved = result[0].Time == 9000 && result[0].Latin.Length > 0, CancellationToken.None); return handler.Calls == 1 && preserved; } finally { Cleanup(dir); } }));
    }
}
