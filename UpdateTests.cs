using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
namespace InerConfig;

internal static class UpdateTests
{
    private sealed class Download(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) });
    }
    public static int Run(string[] packages)
    {
        int count = 0;
        void Check(bool value) { if (!value) throw new Exception("GitHub updater check failed"); count++; }
        void Reject(Action action) { try { action(); } catch { count++; return; } throw new Exception("Unsafe update accepted"); }
        string Release(string tag = "v1.5.0", bool draft = false, bool prerelease = false, string? hash = null, string? url = null, string? name = null) => JsonSerializer.Serialize(new
        {
            tag_name = tag, draft, prerelease,
            assets = new[] { new { name = name ?? "InerConfig-1.5.0-x64.msi", state = "uploaded", size = 3,
                digest = hash ?? "sha256:" + new string('a', 64),
                browser_download_url = url ?? UpdateService.ReleasesUrl + "/download/" + tag + "/InerConfig-1.5.0-x64.msi" } }
        });
        Check(!UpdateService.IsConfigured);
        Check(UpdateService.UpgradeCode != "{4F188516-2CA0-4F77-9B39-823C11A772E3}");
        var current = new Version(1, 4, 3);
        var candidate = UpdateService.ParseRelease(Release(), current)!;
        Check(candidate.Version == new Version(1, 5, 0));
        Check(UpdateService.ParseRelease(Release(), new Version(1, 5, 0)) == null);
        Check(UpdateService.ParseRelease(Release(), new Version(1, 6, 0)) == null);
        Check(UpdateService.ParseRelease(Release(draft: true), current) == null);
        Check(UpdateService.ParseRelease(Release(prerelease: true), current) == null);
        Check(UpdateService.ParseRelease(Release(tag: "1.5.0"), current) != null);
        Reject(() => UpdateService.ParseRelease(Release(tag: "v1.5.0-beta"), current));
        Reject(() => UpdateService.ParseRelease(Release(hash: ""), current));
        Reject(() => UpdateService.ParseRelease(Release(url: "https://example.com/app.msi"), current));
        Reject(() => UpdateService.ParseRelease(Release(name: "InerConfig-1.5.0-arm64.msi"), current));
        using var fake = new HttpClient(new Download([1, 2, 3]));
        Reject(() => Task.Run(() => UpdateService.Prepare(candidate, fake)).GetAwaiter().GetResult()); // hash mismatch
        Reject(() => Task.Run(() => UpdateService.Prepare(candidate with { Size = 4 }, fake)).GetAwaiter().GetResult());
        Reject(() => Task.Run(() => UpdateService.Prepare(candidate with { Path = "http://github.com/file.msi" }, fake)).GetAwaiter().GetResult());
        using var denied = new HttpClient(new Download([], HttpStatusCode.Forbidden));
        Reject(() => Task.Run(() => UpdateService.Prepare(candidate, denied)).GetAwaiter().GetResult());
        foreach (var path in packages)
        {
            var name = Path.GetFileName(path);
            var version = Version.Parse(name["InerConfig-".Length..^"-x64.msi".Length]);
            var bytes = File.ReadAllBytes(path);
            var package = new UpdatePackage(version, $"{UpdateService.ReleasesUrl}/download/v{version}/{name}", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
            using var transport = new HttpClient(new Download(bytes));
            var local = Task.Run(() => UpdateService.Prepare(package, transport)).GetAwaiter().GetResult();
            try { Check(File.ReadAllBytes(local).SequenceEqual(bytes)); Reject(() => UpdateService.ValidateMsi(local, new Version(99, 0, 0))); }
            finally { File.Delete(local); Directory.Delete(Path.GetDirectoryName(local)!); }
        }
        return count;
    }
}
