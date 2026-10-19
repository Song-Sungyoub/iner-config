using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace InerConfig;
internal sealed record UpdatePackage(Version Version, string Path, string Sha256, long Size);
internal static class UpdateService
{
    public const string Repository = ""; // Store 배포 방식 확정 전에는 업데이트 채널을 연결하지 않는다.
    public static bool IsConfigured => Repository.Length > 0;
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    public const string UpgradeCode = "{1BEA7678-610B-4256-86CD-AE0D265F63BC}";
    public static Version Current => Version.Parse(Assembly.GetExecutingAssembly().GetName().Version!.ToString(3));
    public static string Cache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InerConfig", "Updates");
    public static bool IsInstalled => string.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "InerConfig"), StringComparison.OrdinalIgnoreCase);
    private static readonly HttpClient client = new() { Timeout = Timeout.InfiniteTimeSpan };
    internal static UpdatePackage? ParseRelease(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var release = doc.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$") || !Version.TryParse(tag.TrimStart('v'), out var version))
            throw new IOException("GitHub 릴리즈 버전 형식이 올바르지 않습니다.");
        if (version <= current) return null;
        string name = $"InerConfig-{version}-x64.msi";
        var assets = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length != 1) throw new IOException("릴리즈에 맞는 x64 설치 파일이 없습니다.");
        var asset = assets[0];
        string digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
        long size = asset.GetProperty("size").GetInt64();
        string url = $"{ReleasesUrl}/download/{tag}/{name}";
        if (asset.GetProperty("state").GetString() != "uploaded" || size <= 0 || size > 1024L * 1024 * 1024 ||
            !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$") || asset.GetProperty("browser_download_url").GetString() != url)
            throw new IOException("릴리즈 설치 파일의 주소·크기·SHA-256 정보를 확인할 수 없습니다.");
        return new UpdatePackage(version, url, digest[7..], size);
    }
    public static async Task<UpdatePackage?> FindLatest()
    {
        if (!IsConfigured) throw new IOException("InerConfig 배포 채널을 준비 중입니다.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd($"InerConfig/{Current}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new IOException("공개 GitHub 릴리즈가 아직 없거나 접근할 수 없습니다.");
        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests) throw new IOException("GitHub 요청 한도에 도달했습니다. 잠시 후 다시 확인해주세요.");
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(timeout.Token), Current);
    }
    public static Task<string> Prepare(UpdatePackage package) => Prepare(package, client);
    internal static async Task<string> Prepare(UpdatePackage package, HttpClient transport)
    {
        string name = $"InerConfig-{package.Version}-x64.msi";
        if (!Uri.TryCreate(package.Path, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" ||
            !(uri.AbsoluteUri == $"{ReleasesUrl}/download/v{package.Version}/{name}" || uri.AbsoluteUri == $"{ReleasesUrl}/download/{package.Version}/{name}") ||
            !Regex.IsMatch(package.Sha256, "^[a-fA-F0-9]{64}$") || package.Size <= 0 || package.Size > 1024L * 1024 * 1024)
            throw new IOException("잘못된 업데이트 패키지입니다.");
        var directory = Path.Combine(Cache, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var local = Path.Combine(directory, name);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var response = await transport.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != package.Size) throw new IOException("설치 파일 크기가 일치하지 않습니다.");
            await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var destination = new FileStream(local, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920]; long total = 0; int read;
                while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += read;
                    if (total > package.Size) throw new IOException("설치 파일이 예상 크기를 초과했습니다.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
                await destination.FlushAsync(timeout.Token); destination.Position = 0;
                if (total != package.Size || !Convert.ToHexString(await SHA256.HashDataAsync(destination, timeout.Token)).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("업데이트 파일 SHA-256 또는 크기 검증에 실패했습니다.");
            }
            await Task.Run(() => ValidateMsi(local, package.Version));
            return local;
        }
        catch { if (File.Exists(local)) File.Delete(local); Directory.Delete(directory); throw; }
    }
    internal static void ValidateMsi(string path, Version expected)
    {
        object? installer = null, database = null;
        try
        {
            installer = Activator.CreateInstance(Type.GetTypeFromProgID("WindowsInstaller.Installer")!);
            database = ((dynamic)installer!).OpenDatabase(path, 0);
            string Read(string property)
            {
                object? view = null, record = null;
                try
                {
                    view = ((dynamic)database).OpenView($"SELECT `Value` FROM `Property` WHERE `Property` = '{property}'");
                    ((dynamic)view).Execute(); record = ((dynamic)view).Fetch();
                    return record == null ? "" : (string)((dynamic)record).StringData[1];
                }
                finally { if (record != null) Marshal.FinalReleaseComObject(record); if (view != null) Marshal.FinalReleaseComObject(view); }
            }
            if (Read("ProductName") != "InerConfig" || Read("Manufacturer") != "InerConfig" ||
                !string.Equals(Read("UpgradeCode"), UpgradeCode, StringComparison.OrdinalIgnoreCase) ||
                !Version.TryParse(Read("ProductVersion"), out var actual) || actual != expected)
                throw new IOException("InerConfig 업데이트의 제품 정보 또는 버전이 일치하지 않습니다.");
        }
        finally { if (database != null) Marshal.FinalReleaseComObject(database); if (installer != null) Marshal.FinalReleaseComObject(installer); }
    }
    internal static string RestartCommand(string package, string executable, string result, int parent, Version expected)
    {
        static string Q(string value) => "'" + value.Replace("'", "''") + "'";
        return "$ErrorActionPreference='Stop'; $code=-1; try {" +
            $"Wait-Process -Id {parent} -Timeout 120 -ErrorAction SilentlyContinue; if(Get-Process -Id {parent} -ErrorAction SilentlyContinue) {{exit 2}}; " +
            $"$p=Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/i',('\"'+{Q(package)}+'\"'),'/passive','/norestart','/l*v',('\"'+{Q(package + ".log")}+'\"')) -PassThru -Wait; $code=$p.ExitCode" +
            "} catch {$code=-1}; " +
            $"[IO.File]::WriteAllText({Q(result)},'{expected}|' + $code); " +
            $"if(Test-Path -LiteralPath {Q(executable)}) {{Start-Process -FilePath {Q(executable)} -ArgumentList '--skip-update-once'}}";
    }
    public static void Install(string package, Version expected)
    {
        var script = RestartCommand(package, Environment.ProcessPath!, Path.Combine(Cache, "result.txt"), Environment.ProcessId, expected);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        _ = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"), $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}") { UseShellExecute = false, CreateNoWindow = true }) ?? throw new IOException("업데이트 설치 도우미를 시작하지 못했습니다.");
    }
    public static string? Result()
    {
        var file = Path.Combine(Cache, "result.txt");
        if (!File.Exists(file)) return null;
        var parts = File.ReadAllText(file).Split('|'); File.Delete(file);
        if (parts.Length != 2 || !Version.TryParse(parts[0], out var version)) return "업데이트 결과를 확인하지 못했습니다.";
        return (parts[1] is "0" or "3010") && Current >= version ? $"InerConfig {Current} 업데이트 완료" + (parts[1] == "3010" ? " · Windows 재시작이 필요합니다." : "") : "업데이트를 완료하지 못했습니다. 업데이트 확인으로 다시 시도하세요.";
    }
}
