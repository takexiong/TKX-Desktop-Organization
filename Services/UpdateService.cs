using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopOrganizer.Models;

namespace DesktopOrganizer.Services;

public static class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

    public static string CurrentVersionText
    {
        get
        {
            var v = CurrentVersion;
            return v.Revision > 0
                ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
                : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static bool IsConfigured =>
        UpdateSettings.ManifestUrls.Length > 0
        || (!string.IsNullOrWhiteSpace(UpdateSettings.GitHubOwner)
            && !string.Equals(UpdateSettings.GitHubOwner, "CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(UpdateSettings.GitHubRepo));

    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
            return null;

        using var http = CreateClient();

        // 1) 国内友好：版本清单（jsDelivr / gitmirror / 日后可换 Gitee、OSS）
        foreach (var manifestUrl in UpdateSettings.ManifestUrls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = await TryReadManifestAsync(http, manifestUrl, ct).ConfigureAwait(false);
                if (info is not null)
                    return info;
            }
            catch
            {
                // 换下一个源
            }
        }

        // 2) 兜底：GitHub Releases API
        try
        {
            return await TryReadGitHubReleaseAsync(http, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public static async Task DownloadAsync(
        UpdateInfo update,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        using var http = CreateClient();
        Exception? lastError = null;

        foreach (var url in BuildDownloadCandidates(update))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await DownloadFromUrlAsync(http, url, destinationPath, progress, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                TryDelete(destinationPath);
            }
        }

        throw lastError ?? new InvalidOperationException("没有可用的下载地址。");
    }

    public static void LaunchUpdater(string downloadedExePath)
    {
        var targetExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法定位当前程序路径。");

        if (!File.Exists(downloadedExePath))
            throw new FileNotFoundException("更新包不存在。", downloadedExePath);

        var pid = Environment.ProcessId;
        var scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"DesktopOrganizer_update_{pid}.cmd");

        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine("chcp 65001 >nul");
        script.AppendLine($"set \"TARGET={targetExe}\"");
        script.AppendLine($"set \"SOURCE={downloadedExePath}\"");
        script.AppendLine($":wait");
        script.AppendLine($"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul");
        script.AppendLine("if not errorlevel 1 (");
        script.AppendLine("  timeout /t 1 /nobreak >nul");
        script.AppendLine("  goto wait");
        script.AppendLine(")");
        script.AppendLine("timeout /t 1 /nobreak >nul");
        script.AppendLine("copy /y \"%SOURCE%\" \"%TARGET%\" >nul");
        script.AppendLine("if errorlevel 1 (");
        script.AppendLine("  ping 127.0.0.1 -n 3 >nul");
        script.AppendLine("  copy /y \"%SOURCE%\" \"%TARGET%\" >nul");
        script.AppendLine(")");
        script.AppendLine("del \"%SOURCE%\" >nul 2>&1");
        script.AppendLine("start \"\" \"%TARGET%\"");
        script.AppendLine("del \"%~f0\" >nul 2>&1");

        File.WriteAllText(scriptPath, script.ToString(), Encoding.UTF8);

        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        });
    }

    private static async Task<UpdateInfo?> TryReadManifestAsync(
        HttpClient http,
        string manifestUrl,
        CancellationToken ct)
    {
        using var response = await http.GetAsync(manifestUrl, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOptions, ct)
            .ConfigureAwait(false);

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
            return null;

        if (!TryParseVersion(manifest.Version, out var remoteVersion)
            && !TryParseVersion(manifest.Tag ?? "", out remoteVersion))
            return null;

        if (remoteVersion <= TrimVersion(CurrentVersion))
            return null;

        var download = FirstNonEmpty(manifest.DownloadUrl, manifest.Url);
        if (string.IsNullOrWhiteSpace(download))
            return null;

        var mirrors = (manifest.Mirrors ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new UpdateInfo
        {
            Version = remoteVersion,
            TagName = string.IsNullOrWhiteSpace(manifest.Tag) ? $"v{remoteVersion}" : manifest.Tag!,
            Title = string.IsNullOrWhiteSpace(manifest.Title)
                ? $"v{FormatVersion(remoteVersion)}"
                : manifest.Title!,
            ReleaseNotes = string.IsNullOrWhiteSpace(manifest.Notes)
                ? "（无更新说明）"
                : manifest.Notes!.Trim(),
            DownloadUrl = download!,
            AssetName = UpdateSettings.ReleaseAssetName,
            HtmlUrl = manifest.HtmlUrl,
            MirrorUrls = mirrors
        };
    }

    private static async Task<UpdateInfo?> TryReadGitHubReleaseAsync(HttpClient http, CancellationToken ct)
    {
        var url =
            $"https://api.github.com/repos/{UpdateSettings.GitHubOwner}/{UpdateSettings.GitHubRepo}/releases/latest";

        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, ct)
            .ConfigureAwait(false);

        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            return null;

        if (!TryParseVersion(release.TagName, out var remoteVersion))
            return null;

        if (remoteVersion <= TrimVersion(CurrentVersion))
            return null;

        var asset = FindAsset(release.Assets);
        if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            return null;

        return new UpdateInfo
        {
            Version = remoteVersion,
            TagName = release.TagName,
            Title = string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
            ReleaseNotes = string.IsNullOrWhiteSpace(release.Body)
                ? "（无更新说明）"
                : release.Body.Trim(),
            DownloadUrl = asset.BrowserDownloadUrl,
            AssetName = asset.Name ?? UpdateSettings.ReleaseAssetName,
            HtmlUrl = release.HtmlUrl,
            MirrorUrls = []
        };
    }

    private static IEnumerable<string> BuildDownloadCandidates(UpdateInfo update)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> Yield(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
                yield break;
            yield return url;
        }

        foreach (var u in Yield(update.DownloadUrl))
            yield return u;

        foreach (var mirror in update.MirrorUrls)
        {
            foreach (var u in Yield(mirror))
                yield return u;
        }

        // 对 GitHub 直链自动套镜像前缀
        if (IsGitHubDownloadUrl(update.DownloadUrl))
        {
            foreach (var prefix in UpdateSettings.GithubDownloadMirrors)
            {
                var mirrored = prefix.TrimEnd('/') + "/" + update.DownloadUrl;
                foreach (var u in Yield(mirrored))
                    yield return u;
            }
        }
    }

    private static bool IsGitHubDownloadUrl(string url) =>
        url.Contains("github.com/", StringComparison.OrdinalIgnoreCase)
        || url.Contains("githubusercontent.com/", StringComparison.OrdinalIgnoreCase);

    private static async Task DownloadFromUrlAsync(
        HttpClient http,
        string url,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            readTotal += read;
            if (total is > 0)
                progress?.Report(readTotal * 100.0 / total.Value);
            else
                progress?.Report(-1);
        }

        progress?.Report(100);

        // 过小基本是错误页，不是安装包
        if (readTotal < 1024 * 1024)
            throw new InvalidOperationException("下载内容过小，可能不是有效安装包。");
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = ResolveProxy(),
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DesktopOrganizer", CurrentVersionText));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static IWebProxy ResolveProxy()
    {
        var envProxy =
            FirstNonEmpty(
                Environment.GetEnvironmentVariable("HTTPS_PROXY"),
                Environment.GetEnvironmentVariable("https_proxy"),
                Environment.GetEnvironmentVariable("HTTP_PROXY"),
                Environment.GetEnvironmentVariable("http_proxy"),
                Environment.GetEnvironmentVariable("ALL_PROXY"),
                Environment.GetEnvironmentVariable("all_proxy"));

        if (!string.IsNullOrWhiteSpace(envProxy))
        {
            try
            {
                return new WebProxy(NormalizeProxyUri(envProxy))
                {
                    Credentials = CredentialCache.DefaultCredentials,
                    BypassProxyOnLocal = true
                };
            }
            catch
            {
                // ignore
            }
        }

        return HttpClient.DefaultProxy;
    }

    private static Uri NormalizeProxyUri(string value)
    {
        var text = value.Trim().Trim('"', '\'');
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "http://" + text;
        return new Uri(text);
    }

    private static GitHubAsset? FindAsset(List<GitHubAsset>? assets)
    {
        if (assets is null || assets.Count == 0)
            return null;

        var preferred = assets.FirstOrDefault(a =>
            string.Equals(a.Name, UpdateSettings.ReleaseAssetName, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null)
            return preferred;

        return assets.FirstOrDefault(a =>
            a.Name is not null
            && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];

        if (!Version.TryParse(text, out var parsed))
            return false;

        version = TrimVersion(parsed);
        return true;
    }

    private static Version TrimVersion(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    private static string FormatVersion(Version v) =>
        v.Revision > 0
            ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
            : $"{v.Major}.{v.Minor}.{v.Build}";

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private sealed class UpdateManifest
    {
        public string? Version { get; set; }
        public string? Tag { get; set; }
        public string? Title { get; set; }
        public string? Notes { get; set; }
        public string? DownloadUrl { get; set; }
        public string? Url { get; set; }
        public List<string>? Mirrors { get; set; }
        public string? HtmlUrl { get; set; }
    }

    private sealed class GitHubRelease
    {
        public string? TagName { get; set; }
        public string? Name { get; set; }
        public string? Body { get; set; }
        public string? HtmlUrl { get; set; }
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        public string? Name { get; set; }
        public string? BrowserDownloadUrl { get; set; }
    }
}
