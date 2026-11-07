using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace Toma.Windows;

public sealed record UpdateRelease(string Version, string Url, string Sha256, long Size);

public sealed class UpdateClient
{
    public const string Repository = "NJB-PROJECT/TOMA-TOMA-Windows";
    public const string InstallerName = "TOMA-TOMA-Windows-Setup-x64.msi";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases/latest";
    private const string Api = "https://api.github.com/repos/" + Repository + "/releases/latest";
    private static readonly HttpClient Shared = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(15) };
    private readonly HttpClient http;
    private readonly string directory;
    public Version Current { get; }
    public string CheckStatus { get; private set; } = "";

    public UpdateClient(HttpClient? transport = null, string? storage = null, Version? current = null)
    {
        http = transport ?? Shared;
        directory = storage ?? Storage.DirectoryPath;
        Current = Normalize(current ?? typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0));
    }

    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
    public static Version ReadVersion(string text)
    {
        if (!Regex.IsMatch(text, @"^\d{1,5}\.\d{1,5}\.\d{1,5}(?:\.\d{1,5})?$")) throw new InvalidDataException("Versi rilis tidak valid.");
        return Normalize(Version.Parse(text));
    }

    public static void Validate(UpdateRelease release)
    {
        ReadVersion(release.Version);
        if (!Uri.TryCreate(release.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.GetComponents(UriComponents.Path, UriFormat.Unescaped) != Repository + "/releases/download/v" + release.Version + "/" + InstallerName)
            throw new InvalidDataException("Sumber installer tidak valid.");
        if (!Regex.IsMatch(release.Sha256, "^[a-fA-F0-9]{64}$") || release.Size <= 0 || release.Size > 500L * 1024 * 1024) throw new InvalidDataException("Checksum atau ukuran installer tidak valid.");
    }

    public static UpdateRelease? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith("v", StringComparison.Ordinal)) throw new InvalidDataException("Tag rilis tidak valid.");
        string version = tag[1..]; ReadVersion(version);
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != InstallerName || asset.GetProperty("state").GetString() != "uploaded") continue;
            string digest = asset.GetProperty("digest").GetString() ?? "";
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal)) throw new InvalidDataException("Installer belum memiliki SHA-256.");
            var result = new UpdateRelease(version, asset.GetProperty("browser_download_url").GetString() ?? "", digest[7..], asset.GetProperty("size").GetInt64());
            Validate(result); return result;
        }
        throw new InvalidDataException("Installer rilis belum tersedia.");
    }

    private string CacheFile => Path.Combine(directory, "update-state.json");
    private UpdateRelease? Cached()
    {
        try { var value = JsonSerializer.Deserialize<UpdateRelease>(File.ReadAllText(CacheFile)); if (value == null) return null; Validate(value); return value; } catch { return null; }
    }

    public async Task<UpdateRelease?> Check(CancellationToken cancel)
    {
        UpdateRelease? known = Cached();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(TimeSpan.FromSeconds(7));
            using var request = Request(Api); using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token); using var buffer = new MemoryStream(); byte[] bytes = new byte[8192]; int read;
            while ((read = await input.ReadAsync(bytes, timeout.Token)) > 0)
            {
                if (buffer.Length + read > 1024 * 1024) throw new InvalidDataException("Metadata rilis terlalu besar.");
                await buffer.WriteAsync(bytes.AsMemory(0, read), timeout.Token);
            }
            var latest = ParseRelease(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
            if (latest != null && (known == null || ReadVersion(latest.Version) >= ReadVersion(known.Version)))
            {
                known = latest;
                try { Directory.CreateDirectory(directory); File.WriteAllText(CacheFile + ".tmp", JsonSerializer.Serialize(known)); File.Move(CacheFile + ".tmp", CacheFile, true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            CheckStatus = "Pemeriksaan versi selesai.";
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException or OperationCanceledException or System.Collections.Generic.KeyNotFoundException)
        { CheckStatus = "Pemeriksaan online belum tersedia; memakai versi rilis terakhir yang diketahui."; }
        return known != null && ReadVersion(known.Version) > Current ? known : null;
    }

    private static HttpRequestMessage Request(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.UserAgent.ParseAdd("TomaTomaWindows-Updater/1.6"); request.Headers.Accept.ParseAdd("application/vnd.github+json"); return request;
    }
    private static bool AllowedDownload(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && new[] { "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com", "releases.githubusercontent.com" }.Contains(uri.Host);
    private async Task<HttpResponseMessage> DownloadResponse(string url, CancellationToken cancel)
    {
        for (int hop = 0; hop < 6; hop++)
        {
            var uri = new Uri(url); if (!AllowedDownload(uri)) throw new InvalidDataException("Redirect installer ditolak.");
            using var request = Request(url); var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            int status = (int)response.StatusCode;
            if (status is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location; response.Dispose(); if (location == null) throw new InvalidDataException("Redirect installer kosong."); url = new Uri(uri, location).AbsoluteUri; continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; } catch { response.Dispose(); throw; }
        }
        throw new InvalidDataException("Terlalu banyak redirect installer.");
    }

    public async Task<string> Download(UpdateRelease release, IProgress<double>? progress, CancellationToken cancel)
    {
        Validate(release); string folder = Path.Combine(directory, "updates", release.Version); Directory.CreateDirectory(folder); string target = Path.Combine(folder, InstallerName);
        if (File.Exists(target) && await Verify(target, release, cancel)) { progress?.Report(1); return target; }
        string part = target + ".part";
        try
        {
            using var response = await DownloadResponse(release.Url, cancel); using var input = await response.Content.ReadAsStreamAsync(cancel);
            using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
            {
                byte[] bytes = new byte[65536]; long total = 0; int read;
                while ((read = await input.ReadAsync(bytes, cancel)) > 0)
                {
                    total += read; if (total > release.Size) throw new InvalidDataException("Ukuran unduhan melebihi rilis.");
                    await output.WriteAsync(bytes.AsMemory(0, read), cancel); progress?.Report((double)total / release.Size);
                }
            }
            if (!await Verify(part, release, cancel)) throw new InvalidDataException("SHA-256 installer tidak cocok. Unduhan tidak dijalankan.");
            File.Move(part, target, true); return target;
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }

    public static async Task<bool> Verify(string path, UpdateRelease release, CancellationToken cancel)
    {
        if (new FileInfo(path).Length != release.Size) return false;
        using var input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancel)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
