using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace Toma.Windows;
internal static class GeminiTests
{
    private sealed class Stub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> reply;
        public Stub(Func<HttpRequestMessage, HttpResponseMessage> reply) => this.reply = reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) { cancel.ThrowIfCancellationRequested(); return Task.FromResult(reply(request)); }
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private static string Rows(params GeminiRow[] rows) => JsonSerializer.Serialize(new { rows = rows.Select(r => new { id = r.Id, language = r.Language, supported = r.Supported, reading = r.Reading }) });
    private static bool Reject(string text, int[] ids) { try { GeminiPhonetics.Parse(text, ids); return false; } catch (GeminiFailure) { return true; } }
    internal static void Run(Action<string, Func<bool>> test)
    {
        test("Gemini: URL model tidak menerima endpoint/path lain", () => { try { GeminiPhonetics.ValidModel("../../secret?key=x"); return false; } catch (GeminiFailure) { return true; } });
        test("Gemini: alias models dihapus dengan aman", () => GeminiPhonetics.ValidModel("models/gemini-3.5-flash-lite") == "gemini-3.5-flash-lite");
        test("Gemini: teks Vietnam tidak lagi dilewati sebagai Latin biasa", () => { using var r = new Romanizer(); var value = r.Read("Xin chào, tôi thích âm nhạc."); return value.Text.Length > 0 && value.Note.Contains("Vietnam"); });
        test("Gemini: ID hasil boleh diurut ulang tanpa kehilangan pemetaan", () => GeminiPhonetics.Parse(Rows(new GeminiRow(4, "Vietnam", true, "sin cao"), new GeminiRow(1, "Arab", false, "")), new[] { 1, 4 }).Length == 2);
        test("Gemini: baris hilang ditolak", () => Reject(Rows(new GeminiRow(0, "Vietnam", true, "sin cao")), new[] { 0, 1 }));
        test("Gemini: ID duplikat ditolak", () => Reject(Rows(new GeminiRow(0, "Vietnam", true, "sin cao"), new GeminiRow(0, "Vietnam", true, "sin cao")), new[] { 0, 1 }));
        test("Gemini: ID asing ditolak", () => Reject(Rows(new GeminiRow(9, "Vietnam", true, "sin cao")), new[] { 0 }));
        test("Gemini: Unicode non-ASCII tidak dianggap cara baca Latin", () => Reject(Rows(new GeminiRow(0, "Vietnam", true, "chào")), new[] { 0 }));
        test("Gemini: hasil kosong yang diklaim didukung ditolak", () => Reject(Rows(new GeminiRow(0, "Vietnam", true, "")), new[] { 0 }));
        test("Gemini: key berada di header, bukan URL/prompt", () => Task.Run(async () =>
        {
            bool safe = false;
            using var client = new HttpClient(new Stub(req => { string payload = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult(); safe = req.RequestUri!.Host == "generativelanguage.googleapis.com" && !req.RequestUri.Query.Contains("key") && req.Headers.Contains("x-goog-api-key") && !payload.Contains("dummy-test-secret"); return Json(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = Rows(new GeminiRow(0, "Vietnam", true, "sin cao")) } } } } } }); }));
            var rows = await new GeminiPhonetics(client).Convert("dummy-test-secret", "gemini-3.5-flash-lite", "Example", "", new[] { new LyricLine(1200, "Xin chào") }, new[] { 0 }, CancellationToken.None);
            return safe && rows[0].Reading == "sin cao";
        }).GetAwaiter().GetResult());
        test("Gemini: cache menghindari permintaan ulang dan waktu tetap utuh", () => Task.Run(async () =>
        {
            string cache = Path.Combine(Path.GetTempPath(), "LyrivaneGeminiTest-" + Guid.NewGuid().ToString("N")); int calls = 0;
            using var client = new HttpClient(new Stub(_ => { calls++; return Json(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = Rows(new GeminiRow(0, "Vietnam", true, "sin cao")) } } } } } }); }));
            try
            {
                var service = new GeminiPhonetics(client, cache); var source = new[] { new LyricLine(1234, "Xin chào") }; bool preserved = false;
                await service.Annotate("dummy-test-secret", "gemini-3.5-flash-lite", "Example", "", source, 0, (lines, _, _) => preserved = lines[0].Time == 1234 && lines[0].Text == source[0].Text && lines[0].Latin == "sin cao", CancellationToken.None);
                await new GeminiPhonetics(client, cache).Annotate("dummy-test-secret", "gemini-3.5-flash-lite", "Example", "", source, 0, (_, _, _) => { }, CancellationToken.None);
                return calls == 1 && preserved && source[0].Latin == "";
            }
            finally { foreach (var f in Directory.Exists(cache) ? Directory.GetFiles(cache) : Array.Empty<string>()) File.Delete(f); if (Directory.Exists(cache)) Directory.Delete(cache); }
        }).GetAwaiter().GetResult());
        test("Gemini: hasil unsupported tetap lokal dan dapat dicoba ulang", () => Task.Run(async () =>
        {
            string cache = Path.Combine(Path.GetTempPath(), "LyrivaneGeminiTest-" + Guid.NewGuid().ToString("N")); int calls = 0; bool fallback = false, retried = false;
            using var client = new HttpClient(new Stub(_ => { calls++; return Json(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = Rows(new GeminiRow(0, "Rusia", calls > 1, calls > 1 ? "privet" : "")) } } } } } }); }));
            try
            {
                var service = new GeminiPhonetics(client, cache); var source = new[] { new LyricLine(1000, "Привет") { Latin = "Privet" } };
                await service.Annotate("dummy", "gemini-3.5-flash-lite", "", "", source, 0, (lines, _, _) => fallback = lines[0].Latin == "Privet", CancellationToken.None);
                await service.Annotate("dummy", "gemini-3.5-flash-lite", "", "", source, 0, (lines, _, _) => retried = lines[0].Latin == "privet", CancellationToken.None);
                return calls == 2 && fallback && retried;
            }
            finally { foreach (var f in Directory.Exists(cache) ? Directory.GetFiles(cache) : Array.Empty<string>()) File.Delete(f); if (Directory.Exists(cache)) Directory.Delete(cache); }
        }).GetAwaiter().GetResult());
        test("Gemini: pembatalan tidak mengirim permintaan", () => Task.Run(async () => { bool called = false; using var c = new CancellationTokenSource(); c.Cancel(); using var client = new HttpClient(new Stub(_ => { called = true; return Json(new { }); })); try { await new GeminiPhonetics(client).Convert("dummy", "gemini-3.5-flash-lite", "", "", new[] { new LyricLine(0, "Example") }, new[] { 0 }, c.Token); return false; } catch (OperationCanceledException) { return !called; } }).GetAwaiter().GetResult());
        test("Gemini: error tidak memantulkan secret server", () => Task.Run(async () => { using var client = new HttpClient(new Stub(_ => new(HttpStatusCode.Forbidden) { Content = new StringContent("dummy-test-secret") })); try { await new GeminiPhonetics(client).Models("dummy-test-secret", CancellationToken.None); return false; } catch (GeminiFailure ex) { return !ex.Message.Contains("dummy-test-secret") && ex.Message.Contains("key/izin"); } }).GetAwaiter().GetResult());
        test("Gemini: DPAPI menyembunyikan dan memulihkan key dummy", () => { byte[] clear = System.Text.Encoding.UTF8.GetBytes("dummy-test-secret"); byte[] cipher = System.Security.Cryptography.ProtectedData.Protect(clear, null, System.Security.Cryptography.DataProtectionScope.CurrentUser); return !System.Text.Encoding.UTF8.GetString(cipher).Contains("dummy-test-secret") && System.Security.Cryptography.ProtectedData.Unprotect(cipher, null, System.Security.Cryptography.DataProtectionScope.CurrentUser).SequenceEqual(clear); });
        test("Gemini: pengguna lama tetap offline secara default", () => !JsonSerializer.Deserialize<Settings>("{}")!.GeminiEnabled);
    }
}
