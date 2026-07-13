using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DesktopOrganizer.Models;

namespace DesktopOrganizer.Services;

public static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
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
        !string.IsNullOrWhiteSpace(UpdateSettings.GitHubOwner)
        && !string.Equals(UpdateSettings.GitHubOwner, "CHANGE_ME", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(UpdateSettings.GitHubRepo);

    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
            return null;

        var url =
            $"https://api.github.com/repos/{UpdateSettings.GitHubOwner}/{UpdateSettings.GitHubRepo}/releases/latest";

        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
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
            HtmlUrl = release.HtmlUrl
        };
    }

    public static async Task DownloadAsync(
        UpdateInfo update,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(
                update.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct)
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
    }

    /// <summary>
    /// 写出替换脚本并启动，调用方随后应退出进程。
    /// </summary>
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

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DesktopOrganizer", CurrentVersionText));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
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
        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];

        // 允许 1.0.1 或 1.0.1.0
        if (!Version.TryParse(text, out var parsed))
            return false;

        version = TrimVersion(parsed);
        return true;
    }

    private static Version TrimVersion(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

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
