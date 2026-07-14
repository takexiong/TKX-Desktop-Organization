using System.IO;
using Microsoft.Win32;

namespace DesktopOrganizer.Services;

/// <summary>通过当前用户注册表 Run 项控制开机自动启动。</summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "塔克熊桌面整理工具";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath)
                        ?? throw new InvalidOperationException("无法打开开机启动注册表项。");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            throw new InvalidOperationException("无法定位当前程序路径，请使用发布后的 exe 开启自启。");

        key.SetValue(ValueName, $"\"{exePath}\"");
    }

    /// <summary>已开启自启时，把注册表路径刷新为当前 exe（避免更新/移动后失效）。</summary>
    public static void RefreshPathIfEnabled()
    {
        if (!IsEnabled())
            return;

        try
        {
            SetEnabled(true);
        }
        catch
        {
            // ignore
        }
    }
}
