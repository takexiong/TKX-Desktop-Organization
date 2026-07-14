namespace DesktopOrganizer.Models;

public sealed class UpdateInfo
{
    public required Version Version { get; init; }
    public required string TagName { get; init; }
    public required string Title { get; init; }
    public required string ReleaseNotes { get; init; }
    public required string DownloadUrl { get; init; }
    public required string AssetName { get; init; }
    public string? HtmlUrl { get; init; }
    public IReadOnlyList<string> MirrorUrls { get; init; } = [];
}
