namespace DesktopOrganizer.Services;

/// <summary>
/// GitHub Releases 更新源配置。发布前请改成你的公开仓库。
/// Release 请打 tag（如 v1.0.1），并上传资源文件「塔克熊桌面整理工具.exe」。
/// </summary>
public static class UpdateSettings
{
    /// <summary>GitHub 用户名或组织名。</summary>
    public const string GitHubOwner = "takexiong";

    /// <summary>仓库名。</summary>
    public const string GitHubRepo = "TKX-Desktop-Organization";

    /// <summary>发布包资源文件名（与 Releases 里上传的文件名一致）。</summary>
    public const string ReleaseAssetName = "塔克熊桌面整理工具.exe";
}
