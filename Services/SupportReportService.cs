using System.Globalization;
using System.Text;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

/// <summary>Builds an allowlisted report; never serializes connection profiles or raw router configuration.</summary>
public static class SupportReportService
{
    public static string Build(RouterInfo info, Version version, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(info);
        var report = new StringBuilder();
        report.AppendLine($"OpenWrt Studio {version.ToString(3)} — отчёт состояния");
        report.AppendLine($"Время снимка: {capturedAt:O}");
        report.AppendLine($"Модель: {OneLine(info.Model)}");
        report.AppendLine($"OpenWrt: {OneLine(info.OpenWrtRelease)}");
        report.AppendLine($"Ядро: {OneLine(info.KernelRelease)}");
        report.AppendLine($"Архитектура: {OneLine(info.Architecture)}");
        report.AppendLine($"Аптайм (сек.): {info.UptimeSeconds}");
        report.AppendLine(FormattableString.Invariant($"CPU: {info.CpuUsagePercent:F1}%; load: {info.Load1:F2} / {info.Load5:F2} / {info.Load15:F2}"));
        report.AppendLine(FormattableString.Invariant($"RAM (KB): {info.RamUsedKB} / {info.RamTotalKB}; {info.RamUsagePercent:F1}%"));
        report.AppendLine(FormattableString.Invariant($"Overlay (KB): {info.OverlayUsedKB} / {info.OverlayTotalKB}; {info.OverlayUsagePercent:F1}%"));
        report.AppendLine($"Сетевых интерфейсов: {info.Interfaces.Count}");
        report.AppendLine($"Активных интерфейсов: {info.Interfaces.Count(i => i.IsUp)}");
        report.AppendLine("IP/MAC-адреса, hostname, имена сетей, пароли, ключи и конфигурация не включены.");
        return report.ToString();
    }

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
