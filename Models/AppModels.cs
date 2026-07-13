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

    /// <summary>左右相邻图标之间的间距（DIP）。</summary>
    public const int IconGap = 10;

    public static int GetIconPixels(IconSizeMode mode) => mode switch
    {
        IconSizeMode.Small => 32,
        IconSizeMode.Large => 64,
        _ => 48
    };

    /// <summary>名称字号。</summary>
    public static double GetLabelFontSize(IconSizeMode mode) =>
        mode == IconSizeMode.Small ? 10.0 : 11.0;

    /// <summary>正常行距下，两行名称所需高度。</summary>
    public static double GetLabelAreaHeight(IconSizeMode mode)
    {
        var fontSize = GetLabelFontSize(mode);
        var lineHeight = fontSize + 3; // 接近正常行距，略留一点间距
        return lineHeight * 2;
    }

    public static string ToDisplay(IconSizeMode mode) => mode switch
    {
        IconSizeMode.Small => "小",
        IconSizeMode.Large => "大",
        _ => "中"
    };
}
