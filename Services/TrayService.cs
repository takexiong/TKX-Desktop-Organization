using System.Windows.Forms;
using Drawing = System.Drawing;

namespace DesktopOrganizer.Services;

/// <summary>右下角托盘图标：隐藏后可再次呼出控制面板。</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private bool _disposed;

    public TrayService(Action showMain, Action addZone, Action exitApp)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开控制面板", null, (_, _) => showMain());
        menu.Items.Add("添加窗口", null, (_, _) => addZone());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出程序", null, (_, _) => exitApp());

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "桌面图标整理",
            Icon = Drawing.SystemIcons.Application,
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

        _notifyIcon.BalloonTipTitle = "桌面图标整理";
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
    }
}
