using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace Toma.Windows;

internal static class UpdateTests
{
    private static readonly byte[] Payload = { 1, 2, 3, 4 };
    private static UpdateRelease Release(string version = "1.7.0") => new(version, "https://github.com/" + UpdateClient.Repository + "/releases/download/v" + version + "/" + UpdateClient.InstallerName, Convert.ToHexString(SHA256.HashData(Payload)), Payload.Length);
    private static string Metadata(UpdateRelease r) => JsonSerializer.Serialize(new { draft = false, prerelease = false, tag_name = "v" + r.Version, assets = new[] { new { name = UpdateClient.InstallerName, state = "uploaded", browser_download_url = r.Url, size = r.Size, digest = "sha256:" + r.Sha256 } } });
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static bool Async(Func<Task<bool>> task) => Task.Run(task).GetAwaiter().GetResult();

    internal static void Run(Action<string, Func<bool>> test)
    {
        test("Updater membandingkan versi numerik", () => UpdateClient.ReadVersion("1.10.0") > UpdateClient.ReadVersion("1.9.9"));
        test("Updater menyamakan versi assembly dan tag", () => UpdateClient.ReadVersion("1.6.0") == UpdateClient.Normalize(new Version(1, 6, 0, 0)));
        test("Updater hanya memilih MSI resmi dengan digest", () => UpdateClient.ParseRelease(Metadata(Release()))?.Version == "1.7.0");
        test("Updater mengabaikan prerelease", () => UpdateClient.ParseRelease(Metadata(Release()).Replace("\"prerelease\":false", "\"prerelease\":true")) == null);
        test("Updater menolak host dan path palsu", () => { try { UpdateClient.Validate(Release() with { Url = "https://example.com/setup.msi" }); return false; } catch (InvalidDataException) { return true; } });
        test("Updater menolak checksum kosong", () => { try { UpdateClient.Validate(Release() with { Sha256 = "" }); return false; } catch (InvalidDataException) { return true; } });
        test("Offline tanpa versi baru diketahui tetap diizinkan", () => Async(async () => { using var folder = new Temp(); using var http = new HttpClient(new Stub(_ => new(HttpStatusCode.ServiceUnavailable))); return await new UpdateClient(http, folder.Path, new(1, 6, 0)).Check(CancellationToken.None) == null; }));
        test("Versi baru tetap wajib saat koneksi berikutnya putus", () => Async(async () => { using var folder = new Temp(); bool offline = false; using var http = new HttpClient(new Stub(_ => offline ? new(HttpStatusCode.ServiceUnavailable) : Json(Metadata(Release())))); var client = new UpdateClient(http, folder.Path, new(1, 6, 0)); var online = await client.Check(CancellationToken.None); offline = true; var cached = await client.Check(CancellationToken.None); return online?.Version == "1.7.0" && cached?.Version == "1.7.0"; }));
        test("Versi setara tidak memunculkan gerbang update", () => Async(async () => { using var folder = new Temp(); using var http = new HttpClient(new Stub(_ => Json(Metadata(Release("1.6.0"))))); return await new UpdateClient(http, folder.Path, new(1, 6, 0, 0)).Check(CancellationToken.None) == null; }));
        test("Cache updater yang dimodifikasi ke host asing diabaikan", () => Async(async () => { using var folder = new Temp(); File.WriteAllText(System.IO.Path.Combine(folder.Path, "update-state.json"), JsonSerializer.Serialize(Release() with { Url = "https://evil.invalid/setup.msi" })); using var http = new HttpClient(new Stub(_ => new(HttpStatusCode.ServiceUnavailable))); return await new UpdateClient(http, folder.Path, new(1, 6, 0)).Check(CancellationToken.None) == null; }));
        test("Unduhan MSI harus cocok SHA-256 dan ukuran", () => Async(async () => { using var folder = new Temp(); using var http = new HttpClient(new Stub(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) })); var path = await new UpdateClient(http, folder.Path).Download(Release(), null, CancellationToken.None); return await UpdateClient.Verify(path, Release(), CancellationToken.None); }));
        test("MSI yang sudah tervalidasi tidak diunduh ulang", () => Async(async () => { using var folder = new Temp(); int requests = 0; using var http = new HttpClient(new Stub(_ => { requests++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) }; })); var client = new UpdateClient(http, folder.Path); await client.Download(Release(), null, CancellationToken.None); await client.Download(Release(), null, CancellationToken.None); return requests == 1; }));
        test("Checksum salah tidak menyisakan installer atau part", () => Async(async () => { using var folder = new Temp(); using var http = new HttpClient(new Stub(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 4, 3, 2, 1 }) })); try { await new UpdateClient(http, folder.Path).Download(Release(), null, CancellationToken.None); return false; } catch (InvalidDataException) { return Directory.GetFiles(folder.Path, "*", SearchOption.AllDirectories).Length == 0; } }));
        test("Redirect unduhan ke host asing ditolak", () => Async(async () => { using var folder = new Temp(); using var http = new HttpClient(new Stub(_ => { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri("https://evil.invalid/setup.msi"); return response; })); try { await new UpdateClient(http, folder.Path).Download(Release(), null, CancellationToken.None); return false; } catch (InvalidDataException) { return true; } }));
        test("Unduhan terlalu besar ditolak sebelum instalasi", () => Async(async () => { using var folder = new Temp(); using var http = new HttpClient(new Stub(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[5]) })); try { await new UpdateClient(http, folder.Path).Download(Release(), null, CancellationToken.None); return false; } catch (InvalidDataException) { return true; } }));
        test("Cache readonly tidak menghilangkan kewajiban versi baru", () => Async(async () => { using var folder = new Temp(); string path = System.IO.Path.Combine(folder.Path, "update-state.json"); File.WriteAllText(path, JsonSerializer.Serialize(Release("1.6.0"))); File.SetAttributes(path, FileAttributes.ReadOnly); try { using var http = new HttpClient(new Stub(_ => Json(Metadata(Release())))); return (await new UpdateClient(http, folder.Path, new(1, 6, 0)).Check(CancellationToken.None))?.Version == "1.7.0"; } finally { File.SetAttributes(path, FileAttributes.Normal); } }));
    }
    private sealed class Stub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> reply;
        public Stub(Func<HttpRequestMessage, HttpResponseMessage> reply) { this.reply = reply; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) => Task.FromResult(reply(request));
    }
    private sealed class Temp : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TomaUpdaterTest-" + Guid.NewGuid().ToString("N"));
        public Temp() { Directory.CreateDirectory(Path); }
        public void Dispose() { Directory.Delete(Path, true); }
    }
}
