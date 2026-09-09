using System;
using System.Collections.Concurrent;

namespace OpenWrtStudio.Services;

public enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Critical
}

public interface INotificationService
{
    void ShowNotification(string title, string message, NotificationSeverity severity = NotificationSeverity.Info, string? tag = null, int cooldownSeconds = 30);
    event Action<string, string, NotificationSeverity>? OnNotificationRequested;
    event Action<string>? OnNotificationActionTriggered;
    void TriggerAction(string action);
}

public class NotificationService : INotificationService
{
    private readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();

    public event Action<string, string, NotificationSeverity>? OnNotificationRequested;
    public event Action<string>? OnNotificationActionTriggered;

    public void ShowNotification(string title, string message, NotificationSeverity severity = NotificationSeverity.Info, string? tag = null, int cooldownSeconds = 30)
    {
        var key = tag ?? title;
        var now = DateTime.UtcNow;

        if (_cooldowns.TryGetValue(key, out var lastTime))
        {
            if ((now - lastTime).TotalSeconds < cooldownSeconds)
            {
                return; // Suppressed by cooldown
            }
        }
        _cooldowns[key] = now;

        OnNotificationRequested?.Invoke(title, message, severity);
    }

    public void TriggerAction(string action)
    {
        OnNotificationActionTriggered?.Invoke(action);
    }
}
