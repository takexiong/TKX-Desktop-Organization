using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopOrganizer.Models;
using DesktopOrganizer.Services;

namespace DesktopOrganizer;

public partial class MainWindow : Window
{
    private readonly List<ZoneWindow> _zones = [];
    private readonly DispatcherTimer _saveTimer;
    private AppConfig _config = new();
    private TrayService? _tray;
    private UpdateInfo? _pendingUpdate;
    private bool _forceClose;
    private bool _savePending;
    private bool _hideTipShown;
    private bool _updateDialogOpen;

    public MainWindow()
    {
        InitializeComponent();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            if (_savePending)
                SaveConfigNow();
        };
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        VersionLabel.Text = $"v{UpdateService.CurrentVersionText}";
        _tray = new TrayService(ShowMainPanel, AddZone, RequestExitFromTray);
        _config = ConfigService.Load();
        foreach (var zone in _config.Zones)
            OpenZone(zone, save: false);
        UpdateStatus();
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            // 稍等 UI 就绪，再安静检查，失败不打扰用户
            await Task.Delay(1200);
            var update = await UpdateService.CheckForUpdateAsync();
            if (update is null)
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                _pendingUpdate = update;
                UpdateBannerText.Text =
                    $"发现新版本 v{FormatVersion(update.Version)}，点击查看更新内容";
                UpdateBanner.Visibility = Visibility.Visible;
            });
        }
        catch
        {
            // 自动检查失败时静默忽略（网络/仓库未配置等）
        }
    }

    private void UpdateBanner_Click(object sender, MouseButtonEventArgs e)
    {
        if (_pendingUpdate is null || _updateDialogOpen)
            return;

        _updateDialogOpen = true;
        try
        {
            var dialog = new UpdateDialog(_pendingUpdate) { Owner = this };
            var confirmed = dialog.ShowDialog() == true;
            if (!confirmed || string.IsNullOrWhiteSpace(dialog.DownloadedFilePath))
                return;

            try
            {
                SaveConfigNow();
                UpdateService.LaunchUpdater(dialog.DownloadedFilePath);
                ExitApp();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"准备安装更新失败：\n{ex.Message}",
                    "桌面图标整理",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            _updateDialogOpen = false;
        }
    }

    private static string FormatVersion(Version v) =>
        v.Revision > 0
            ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
            : $"{v.Major}.{v.Minor}.{v.Build}";

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose)
            return;

        // 点关闭只隐藏主窗口，分区继续留在桌面
        e.Cancel = true;
        HideToTray();
    }

    private void HideButton_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void HideToTray()
    {
        Hide();
        if (_hideTipShown || _tray is null)
            return;

        _hideTipShown = true;
        _tray.ShowBalloon("程序仍在运行。点击右下角托盘图标可重新打开控制面板。");
    }

    private void ShowMainPanel()
    {
        Dispatcher.BeginInvoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
        });
    }

    /// <summary>已有实例被再次启动时，把控制面板唤到前台。</summary>
    public void BringToFrontFromSecondInstance()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e) => RequestExitFromUi();

    private void RequestExitFromTray()
    {
        Dispatcher.BeginInvoke(RequestExitFromUi);
    }

    private void RequestExitFromUi()
    {
        var result = MessageBox.Show(
            "确定退出？所有分区窗口将关闭（布局已自动保存，下次打开会恢复）。",
            "桌面图标整理",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        ExitApp();
    }

    private void ExitApp()
    {
        _forceClose = true;
        _saveTimer.Stop();
        SaveConfigNow();

        _tray?.Dispose();
        _tray = null;

        foreach (var zone in _zones.ToList())
        {
            try
            {
                zone.CloseForAppExit();
            }
            catch
            {
                // 忽略单个窗口关闭异常，继续关其余窗口
            }
        }

        _zones.Clear();

        foreach (Window window in Application.Current.Windows.Cast<Window>().ToList())
        {
            if (ReferenceEquals(window, this))
                continue;

            try
            {
                if (window is ZoneWindow zone)
                    zone.CloseForAppExit();
                else
                    window.Close();
            }
            catch
            {
                // ignore
            }
        }

        Application.Current.Shutdown();
    }

    private void AddZoneButton_Click(object sender, RoutedEventArgs e) => AddZone();

    private void AddZone()
    {
        void Create()
        {
            var size = SizeHelper.TwoCmInDip;
            var screen = SystemParameters.WorkArea;
            var data = new ZoneData
            {
                Title = $"分区 {_zones.Count + 1}",
                Left = screen.Left + 80 + _zones.Count * 24,
                Top = screen.Top + 80 + _zones.Count * 24,
                Width = size,
                Height = size,
                IconSize = IconSizeMode.Medium
            };

            _config.Zones.Add(data);
            OpenZone(data, save: true);
            UpdateStatus();
        }

        if (Dispatcher.CheckAccess())
            Create();
        else
            Dispatcher.BeginInvoke(Create);
    }

    private void OpenZone(ZoneData data, bool save)
    {
        var zone = new ZoneWindow(data, ScheduleSave, OnZoneClosed);
        _zones.Add(zone);
        zone.Show();
        if (save)
            ScheduleSave();
    }

    private void OnZoneClosed(ZoneWindow zone)
    {
        if (_forceClose)
            return;

        _zones.Remove(zone);
        _config.Zones.Remove(zone.Data);
        SaveConfigNow();
        UpdateStatus();
    }

    private void ScheduleSave()
    {
        _savePending = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveConfigNow()
    {
        _savePending = false;
        foreach (var zone in _zones)
        {
            zone.Data.Left = zone.Left;
            zone.Data.Top = zone.Top;
            zone.Data.Width = zone.Width;
            zone.Data.Height = zone.Height;
        }

        ConfigService.Save(_config);
    }

    private void UpdateStatus()
    {
        StatusText.Text = $"当前分区：{_zones.Count} 个";
    }
}
