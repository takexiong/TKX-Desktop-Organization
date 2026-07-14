namespace DesktopOrganizer.Services;

/// <summary>
/// 更新源配置。国内优先走清单文件 / 镜像，GitHub 直连仅作兜底。
/// </summary>
public static class UpdateSettings
{
    public const string GitHubOwner = "takexiong";
    public const string GitHubRepo = "TKX-Desktop-Organization";
    public const string ReleaseAssetName = "TakexiongDesktopOrganizer.exe";

    /// <summary>
    /// 版本清单 URL（按顺序尝试）。可换成你的 Gitee / OSS 地址，国内更稳。
    /// 清单格式见仓库根目录 update.json。
    /// </summary>
    public static readonly string[] ManifestUrls =
    [
        // 可改为 Gitee：https://gitee.com/你的用户名/仓库/raw/master/update.json
        // 可改为 OSS： https://你的桶.oss-cn-hangzhou.aliyuncs.com/update.json
        "https://cdn.jsdelivr.net/gh/takexiong/TKX-Desktop-Organization@main/update.json",
        "https://testingcf.jsdelivr.net/gh/takexiong/TKX-Desktop-Organization@main/update.json",
        "https://fastly.jsdelivr.net/gh/takexiong/TKX-Desktop-Organization@main/update.json",
        "https://raw.gitmirror.com/takexiong/TKX-Desktop-Organization/main/update.json",
        "https://raw.githubusercontent.com/takexiong/TKX-Desktop-Organization/main/update.json"
    ];

    /// <summary>
    /// GitHub 下载加速前缀（仅当下载地址是 github.com 时依次尝试）。
    /// </summary>
    public static readonly string[] GithubDownloadMirrors =
    [
        "https://ghfast.top/",
        "https://gh-proxy.com/",
        "https://mirror.ghproxy.com/",
        "https://gitproxy.click/"
    ];
}
