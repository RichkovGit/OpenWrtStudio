using System;

namespace OpenWrtStudio.Models;

public enum HealthStatus
{
    Good,
    Warning,
    Danger,
    Info
}

public class HealthCheckItem
{
    public string Title { get; set; } = "";
    public string Category { get; set; } = "Система";
    public string Description { get; set; } = "";
    public HealthStatus Status { get; set; } = HealthStatus.Good;
    public string StatusText { get; set; } = "В норме";
    public string Recommendation { get; set; } = "";
    public string FixCommand { get; set; } = "";
    public bool CanAutoFix { get => !string.IsNullOrWhiteSpace(FixCommand); set { } }
}

public class PingResultItem
{
    public int Seq { get; set; }
    public string Host { get; set; } = "";
    public string Ip { get; set; } = "";
    public double TimeMs { get; set; }
    public int Ttl { get; set; }
    public bool Success { get; set; } = true;
    public string RawText { get; set; } = "";
}

public class TracerouteHop
{
    public int Hop { get; set; }
    public string Host { get; set; } = "*";
    public string Ip { get; set; } = "*";
    public string Rtt1 { get; set; } = "*";
    public string Rtt2 { get; set; } = "*";
    public string Rtt3 { get; set; } = "*";
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string TimeText { get; set; } = "";
    public string Facility { get; set; } = "";
    public string Level { get; set; } = "info";
    public string Process { get; set; } = "";
    public string Message { get; set; } = "";
    public HealthStatus Severity
    {
        get => Level.ToLowerInvariant() switch
        {
            "err" or "error" or "emerg" or "alert" or "crit" => HealthStatus.Danger,
            "warn" or "warning" => HealthStatus.Warning,
            _ => HealthStatus.Info
        };
        set { }
    }
}
