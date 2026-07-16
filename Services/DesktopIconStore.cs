using System.IO;
using System.Runtime.InteropServices;
using DesktopOrganizer.Models;

namespace DesktopOrganizer.Services;

/// <summary>
/// 将桌面上的图标移入隐藏目录，从而从桌面消失；移出分区时再还原。
/// </summary>
public static class DesktopIconStore
{
    public static string StoreRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopOrganizer",
            "HiddenIcons");

    public static bool IsOnDesktop(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var parent = Path.GetDirectoryName(full);
            if (string.IsNullOrWhiteSpace(parent))
                return false;

            foreach (var desktop in GetDesktopFolders())
            {
                if (string.Equals(parent, desktop, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    public static IconData Intake(string sourcePath, string zoneId)
    {
        var displayName = IconExtractor.GetDisplayName(sourcePath);

        if (!IsOnDesktop(sourcePath))
        {
            return new IconData
            {
                Path = sourcePath,
                DisplayName = displayName,
                HiddenFromDesktop = false
            };
        }

        var originPath = Path.GetFullPath(sourcePath);
        var destDir = Path.Combine(StoreRoot, zoneId);
        Directory.CreateDirectory(destDir);
        TryHideDirectory(StoreRoot);
        TryHideDirectory(destDir);

        var destPath = MakeUniquePath(Path.Combine(destDir, Path.GetFileName(originPath)));
        MoveItem(originPath, destPath);
        NotifyShellChanged();

        return new IconData
        {
            Path = destPath,
            DisplayName = displayName,
            DesktopOriginPath = originPath,
            HiddenFromDesktop = true
        };
    }

    public static void RestoreToDesktop(IconData item)
    {
        if (item is null || !item.HiddenFromDesktop)
            return;

        try
        {
            if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
                return;

            var target = item.DesktopOriginPath;
            if (string.IsNullOrWhiteSpace(target))
            {
                target = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Path.GetFileName(item.Path));
            }

            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetDir))
                Directory.CreateDirectory(targetDir);

            target = MakeUniquePath(target);
            MoveItem(item.Path, target);
            NotifyShellChanged();

            item.Path = target;
            item.DesktopOriginPath = null;
            item.HiddenFromDesktop = false;
        }
        catch
        {
            // 还原失败时保留在隐藏目录，避免丢失
        }
    }

    public static void RestoreAll(IEnumerable<IconData> icons)
    {
        foreach (var icon in icons.ToList())
            RestoreToDesktop(icon);
    }

    public static void CleanupZoneFolder(string zoneId)
    {
        try
        {
            var dir = Path.Combine(StoreRoot, zoneId);
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir, recursive: false);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>跨分区移动时，把已隐藏的文件迁到目标分区目录。</summary>
    public static void RelocateToZone(IconData item, string newZoneId)
    {
        if (item is null || !item.HiddenFromDesktop)
            return;

        try
        {
            if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
                return;

            var destDir = Path.Combine(StoreRoot, newZoneId);
            Directory.CreateDirectory(destDir);
            TryHideDirectory(StoreRoot);
            TryHideDirectory(destDir);

            var currentDir = Path.GetDirectoryName(item.Path);
            if (string.Equals(currentDir, destDir, StringComparison.OrdinalIgnoreCase))
                return;

            var destPath = MakeUniquePath(Path.Combine(destDir, Path.GetFileName(item.Path)));
            MoveItem(item.Path, destPath);
            item.Path = destPath;
        }
        catch
        {
            // 迁移失败则保留原路径，避免丢失
        }
    }

    private static IEnumerable<string> GetDesktopFolders()
    {
        var list = new List<string>();
        foreach (var special in new[]
                 {
                     Environment.SpecialFolder.DesktopDirectory,
                     Environment.SpecialFolder.CommonDesktopDirectory
                 })
        {
            try
            {
                var path = Environment.GetFolderPath(special);
                if (!string.IsNullOrWhiteSpace(path))
                    list.Add(Path.GetFullPath(path).TrimEnd('\\', '/'));
            }
            catch
            {
                // ignore
            }
        }

        return list;
    }

    private static void MoveItem(string source, string dest)
    {
        if (Directory.Exists(source))
            Directory.Move(source, dest);
        else
            File.Move(source, dest);
    }

    private static string MakeUniquePath(string desiredPath)
    {
        if (!File.Exists(desiredPath) && !Directory.Exists(desiredPath))
            return desiredPath;

        var dir = Path.GetDirectoryName(desiredPath) ?? "";
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = Path.GetExtension(desiredPath);

        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{name}_{Guid.NewGuid():N}{ext}");
    }

    private static void TryHideDirectory(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
                return;

            var attrs = File.GetAttributes(dir);
            if ((attrs & FileAttributes.Hidden) == 0)
                File.SetAttributes(dir, attrs | FileAttributes.Hidden);
        }
        catch
        {
            // ignore
        }
    }

    private static void NotifyShellChanged()
    {
        try
        {
            // 通知资源管理器刷新桌面图标
            SHChangeNotify(SHCNE_ALLEVENTS, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // ignore
        }
    }

    private const uint SHCNE_ALLEVENTS = 0x7FFFFFFF;
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;
    private const uint SHCNF_FLUSH = 0x1000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
