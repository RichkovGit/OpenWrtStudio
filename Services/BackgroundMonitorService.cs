using System;
using System.Threading;
using System.Threading.Tasks;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface IBackgroundMonitorService
{
    bool IsRunning { get; }
    void StartMonitoring(int intervalSeconds = 25);
    void StopMonitoring();
    event Action<RouterInfo?>? OnRouterStatePolled;
}

public class BackgroundMonitorService : IBackgroundMonitorService, IDisposable
{
    private readonly ISshService _ssh;
    private readonly IRouterService _routerService;
    private readonly ISentinelService _sentinel;
    private readonly INotificationService _notifications;

    private CancellationTokenSource? _cts;
    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private bool _disposed;
    private bool _lastKnownConnected = false;
    private bool _hasInitializedState = false;

    public bool IsRunning
    {
        get { lock (_lifecycleLock) return _cts != null && !_cts.IsCancellationRequested; }
    }
    public event Action<RouterInfo?>? OnRouterStatePolled;

    public BackgroundMonitorService(
        ISshService ssh,
        IRouterService routerService,
        ISentinelService sentinel,
        INotificationService notifications)
    {
        _ssh = ssh;
        _routerService = routerService;
        _sentinel = sentinel;
        _notifications = notifications;

        // Instant notification when Sentinel watchdog catches an issue
        _sentinel.IssueChanged += OnSentinelIssueChanged;
    }

    private void OnSentinelIssueChanged(object? sender, SentinelIssue? issue)
    {
        if (issue == null || !IsRunning) return;

        var severity = issue.Severity switch
        {
            IssueSeverity.Critical => NotificationSeverity.Critical,
            IssueSeverity.Info => NotificationSeverity.Info,
            _ => NotificationSeverity.Warning
        };

        var title = issue.IsProviderIssue ? "Сбой на стороне провайдера" : "Сбой сетевой службы роутера";
        _notifications.ShowNotification(
            title,
            $"{issue.Title}: {issue.Description}",
            severity,
            "sentinel_issue",
            cooldownSeconds: 120);
    }

    public void StartMonitoring(int intervalSeconds = 25)
    {
        if (intervalSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cts != null) return;
            _hasInitializedState = false;
            _cts = new CancellationTokenSource();
            var source = _cts;
            _ = Task.Run(() => RunMonitoringAsync(source, intervalSeconds));
        }
    }

    private async Task RunMonitoringAsync(CancellationTokenSource source, int intervalSeconds)
    {
        var token = source.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _pollLock.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    await CheckRouterHealthAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BackgroundMonitor] Error during poll: {ex.Message}");
                }
                finally
                {
                    _pollLock.Release();
                }
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            lock (_lifecycleLock)
            {
                if (ReferenceEquals(_cts, source)) _cts = null;
                source.Dispose();
            }
        }
    }

    public void StopMonitoring()
    {
        lock (_lifecycleLock)
        {
            _cts?.Cancel();
            // The worker owns disposal; it may still be finishing an SSH poll.
            _cts = null;
        }
    }

    private async Task CheckRouterHealthAsync(CancellationToken token)
    {
        bool isConnected = _ssh.IsConnected;

        if (!_hasInitializedState)
        {
            _lastKnownConnected = isConnected;
            _hasInitializedState = true;
        }

        // 1. Connection state transition checks
        if (_lastKnownConnected && !isConnected)
        {
            _notifications.ShowNotification(
                "Потеряна связь с роутером",
                "Связь по SSH прервалась. Проверьте питание роутера или физическое подключение.",
                NotificationSeverity.Warning,
                "router_conn_status",
                cooldownSeconds: 60);
        }
        else if (!_lastKnownConnected && isConnected)
        {
            _notifications.ShowNotification(
                "Связь с роутером восстановлена",
                "Роутер снова в сети. Мониторинг параметров активен.",
                NotificationSeverity.Success,
                "router_conn_status",
                cooldownSeconds: 60);
        }

        _lastKnownConnected = isConnected;

        if (!isConnected)
        {
            OnRouterStatePolled?.Invoke(null);
            return;
        }

        // 2. Poll Router Stats (RAM, CPU, Overlay)
        try
        {
            var info = await _routerService.GetRouterInfoAsync();
            token.ThrowIfCancellationRequested();
            OnRouterStatePolled?.Invoke(info);

            if (info != null)
            {
                if (info.RamUsagePercent >= 92)
                {
                    _notifications.ShowNotification(
                        "Высокая нагрузка RAM",
                        $"Оперативная память роутера заполнена на {info.RamUsagePercent:F0}%. Рекомендуется сбросить кэш или перезагрузить службы.",
                        NotificationSeverity.Warning,
                        "high_ram",
                        cooldownSeconds: 300);
                }

                if (info.OverlayUsagePercent >= 92)
                {
                    _notifications.ShowNotification(
                        "Заполнена память /overlay",
                        $"Свободное место во внутренней памяти почти исчерпано ({info.OverlayUsagePercent:F0}%). Очистите неиспользуемые пакеты.",
                        NotificationSeverity.Critical,
                        "high_overlay",
                        cooldownSeconds: 300);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch
        {
            // Transient SSH error
        }

        // 3. Sentinel Quick Probe
        try
        {
            token.ThrowIfCancellationRequested();
            if (_sentinel.IsMonitoring) await _sentinel.ProbeHealthAsync();
        }
        catch
        {
            // Transient probe error
        }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            StopMonitoring();
            _sentinel.IssueChanged -= OnSentinelIssueChanged;
        }
    }
}
