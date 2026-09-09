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

public class BackgroundMonitorService : IBackgroundMonitorService
{
    private readonly ISshService _ssh;
    private readonly IRouterService _routerService;
    private readonly ISentinelService _sentinel;
    private readonly INotificationService _notifications;

    private CancellationTokenSource? _cts;
    private bool _lastKnownConnected = false;
    private bool _hasInitializedState = false;

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
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
        if (issue == null) return;

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
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await CheckRouterHealthAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BackgroundMonitor] Error during poll: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public void StopMonitoring()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task CheckRouterHealthAsync()
    {
        bool isConnected = _ssh.IsConnected;

        if (!_hasInitializedState)
        {
            _lastKnownConnected = isConnected;
            _hasInitializedState = true;
            return;
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

        if (!isConnected) return;

        // 2. Poll Router Stats (RAM, CPU, Overlay)
        try
        {
            var info = await _routerService.GetRouterInfoAsync();
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
        catch
        {
            // Transient SSH error
        }

        // 3. Sentinel Quick Probe
        try
        {
            await _sentinel.ProbeHealthAsync();
        }
        catch
        {
            // Transient probe error
        }
    }
}
