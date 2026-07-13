using System.Windows.Forms;
using Drawing = System.Drawing;

namespace DesktopOrganizer.Services;

/// <summary>右下角托盘图标：隐藏后可再次呼出控制面板。</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Drawing.Icon _icon;
    private bool _disposed;

    public TrayService(Action showMain, Action addZone, Action exitApp)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开控制面板", null, (_, _) => showMain());
        menu.Items.Add("添加窗口", null, (_, _) => addZone());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出程序", null, (_, _) => exitApp());

        _icon = LoadAppIcon();
        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "塔克熊桌面整理工具",
            Icon = _icon,
            ContextMenuStrip = menu
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                showMain();
        };

        _notifyIcon.DoubleClick += (_, _) => showMain();
    }

    public void ShowBalloon(string tip)
    {
        if (_disposed)
            return;

        _notifyIcon.BalloonTipTitle = "塔克熊桌面整理工具";
        _notifyIcon.BalloonTipText = tip;
        _notifyIcon.ShowBalloonTip(2000);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private static Drawing.Icon LoadAppIcon()
    {
        try
        {
            var stream = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/app.ico"))?.Stream;
            if (stream is not null)
            {
                using (stream)
                    return new Drawing.Icon(stream);
            }
        }
        catch
        {
            // 回退到系统默认图标
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }
}
