namespace DesktopOrganizer.Models;


public enum IconSizeMode
{
    Small,
    Medium,
    Large
}

public sealed class IconData
{
    public string Path { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>从桌面移入隐藏目录前的原路径。</summary>
    public string? DesktopOriginPath { get; set; }
    /// <summary>是否已从桌面隐藏（文件已移到工具目录）。</summary>
    public bool HiddenFromDesktop { get; set; }
}

public sealed class ZoneData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "分区";
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public IconSizeMode IconSize { get; set; } = IconSizeMode.Medium;
    public bool IsLocked { get; set; }
    public List<IconData> Icons { get; set; } = [];
}

public sealed class AppConfig
{
    public List<ZoneData> Zones { get; set; } = [];
}

public static class SizeHelper
{
    /// <summary>2 厘米对应的 WPF DIP（1 DIP = 1/96 英寸）。</summary>
    public static double TwoCmInDip => 2.0 / 2.54 * 96.0;

    public static (int Icon, int Tile) GetPixels(IconSizeMode mode) => mode switch
    {
        IconSizeMode.Small => (32, 76),
        IconSizeMode.Large => (64, 112),
        _ => (48, 92)
    };

    public static string ToDisplay(IconSizeMode mode) => mode switch
    {
        IconSizeMode.Small => "小",
        IconSizeMode.Large => "大",
        _ => "中"
    };
}
