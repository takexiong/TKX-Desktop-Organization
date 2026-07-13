using System.Threading;

namespace DesktopOrganizer.Services;

/// <summary>保证只运行一个实例；再次启动时通知已有实例显示主窗口。</summary>
public static class SingleInstanceGuard
{
    private const string MutexName = @"Local\DesktopOrganizer_SingleInstance_Mutex_v1";
    private const string EventName = @"Local\DesktopOrganizer_SingleInstance_Activate_v1";

    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;
    private static CancellationTokenSource? _watchCts;

    /// <returns>true 表示当前是首个实例，应继续启动；false 表示已有实例并已请求其前台显示。</returns>
    public static bool TryStartAsPrimary(Action onActivateRequest)
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var createdNew);

        if (!createdNew)
        {
            try
            {
                _activateEvent.Set();
            }
            catch
            {
                // ignore
            }

            _activateEvent.Dispose();
            _activateEvent = null;
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _watchCts = new CancellationTokenSource();
        var token = _watchCts.Token;
        var activateEvent = _activateEvent;

        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (activateEvent.WaitOne(500))
                        onActivateRequest();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);

        return true;
    }

    public static void Release()
    {
        try
        {
            _watchCts?.Cancel();
            _watchCts?.Dispose();
            _watchCts = null;
        }
        catch
        {
            // ignore
        }

        try
        {
            _activateEvent?.Dispose();
            _activateEvent = null;
        }
        catch
        {
            // ignore
        }

        try
        {
            if (_mutex is not null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch
                {
                    // 可能未持有
                }

                _mutex.Dispose();
                _mutex = null;
            }
        }
        catch
        {
            // ignore
        }
    }
}
