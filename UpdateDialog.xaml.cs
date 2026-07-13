using System.IO;
using System.Windows;
using DesktopOrganizer.Models;
using DesktopOrganizer.Services;

namespace DesktopOrganizer;

public partial class UpdateDialog : Window
{
    private readonly UpdateInfo _update;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public string? DownloadedFilePath { get; private set; }

    public UpdateDialog(UpdateInfo update)
    {
        InitializeComponent();
        _update = update;

        TitleText.Text = string.IsNullOrWhiteSpace(update.Title)
            ? $"发现新版本 {FormatVersion(update.Version)}"
            : update.Title;
        VersionText.Text =
            $"当前版本：{UpdateService.CurrentVersionText}  →  新版本：{FormatVersion(update.Version)}";
        NotesText.Text = update.ReleaseNotes;
    }

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        _busy = true;
        ConfirmButton.IsEnabled = false;
        CancelButton.Content = "取消下载";
        DownloadProgress.Visibility = Visibility.Visible;
        ProgressText.Visibility = Visibility.Visible;
        ProgressText.Text = "正在下载更新…";
        DownloadProgress.IsIndeterminate = true;
        DownloadProgress.Value = 0;

        _cts = new CancellationTokenSource();
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"DesktopOrganizer_{_update.Version}_{Environment.ProcessId}.exe");

        try
        {
            var progress = new Progress<double>(p =>
            {
                if (p < 0)
                {
                    DownloadProgress.IsIndeterminate = true;
                    ProgressText.Text = "正在下载更新…";
                    return;
                }

                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = p;
                ProgressText.Text = $"正在下载更新… {p:0}%";
            });

            await UpdateService.DownloadAsync(_update, tempPath, progress, _cts.Token);
            DownloadedFilePath = tempPath;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            ProgressText.Text = "已取消下载。";
            ResetBusyState();
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            MessageBox.Show(
                this,
                $"下载更新失败：\n{ex.Message}\n\n请检查网络，或稍后到 GitHub Releases 手动下载。",
                "桌面图标整理",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ProgressText.Text = "下载失败，可重试。";
            ResetBusyState();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            _cts?.Cancel();
            return;
        }

        DialogResult = false;
    }

    private void ResetBusyState()
    {
        _busy = false;
        ConfirmButton.IsEnabled = true;
        CancelButton.Content = "稍后";
        DownloadProgress.IsIndeterminate = false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private static string FormatVersion(Version v) =>
        v.Revision > 0
            ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
            : $"{v.Major}.{v.Minor}.{v.Build}";

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
    }
}
