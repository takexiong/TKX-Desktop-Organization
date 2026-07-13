using System.Windows;
using DesktopOrganizer.Services;

namespace DesktopOrganizer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 二次启动：唤醒已有实例并退出
        if (!SingleInstanceGuard.TryStartAsPrimary(ActivateExistingInstance))
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"发生错误，但程序会继续运行：\n{args.Exception.Message}",
                "桌面图标整理",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"发生严重错误：\n{ex.Message}\n\n可在任务管理器结束“桌面图标整理”进程。",
                    "桌面图标整理",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    private void ActivateExistingInstance()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is MainWindow main)
            {
                main.BringToFrontFromSecondInstance();
                return;
            }

            if (MainWindow is not null)
            {
                MainWindow.Show();
                MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstanceGuard.Release();
        base.OnExit(e);
    }
}
