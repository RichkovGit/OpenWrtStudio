using System;
using System.Collections.Generic;

namespace OpenWrtStudio.Models;

public class RouterInfo
{
    public string Hostname { get; set; } = "OpenWrt";
    public string Model { get; set; } = "Неизвестная модель";
    public string Target { get; set; } = "x86/64";
    public string Architecture { get; set; } = "";
    public string OpenWrtRelease { get; set; } = "OpenWrt";
    public string KernelRelease { get; set; } = "";
    public long UptimeSeconds { get; set; }
    public string UptimeFormatted { get; set; } = "0ч 0м";

    // CPU
    public double Load1 { get; set; }
    public double Load5 { get; set; }
    public double Load15 { get; set; }
    public double CpuUsagePercent { get; set; }

    // Memory (RAM) in KB
    public long RamTotalKB { get; set; }
    public long RamUsedKB { get; set; }
    public long RamFreeKB { get; set; }
    public long RamBufferedKB { get; set; }
    public double RamUsagePercent
    {
        get => RamTotalKB > 0 ? Math.Round((double)RamUsedKB / RamTotalKB * 100, 1) : 0;
        set { }
    }
    public string RamFormatted
    {
        get => $"{RamUsedKB / 1024} МБ / {RamTotalKB / 1024} МБ ({RamUsagePercent}%)";
        set { }
    }

    // Storage (/overlay) in KB
    public long OverlayTotalKB { get; set; }
    public long OverlayUsedKB { get; set; }
    public long OverlayFreeKB { get; set; }
    public double OverlayUsagePercent
    {
        get => OverlayTotalKB > 0 ? Math.Round((double)OverlayUsedKB / OverlayTotalKB * 100, 1) : 0;
        set { }
    }
    public string OverlayFormatted
    {
        get => $"{OverlayUsedKB / 1024} МБ / {OverlayTotalKB / 1024} МБ ({OverlayUsagePercent}%)";
        set { }
    }

    public List<NetworkInterfaceItem> Interfaces { get; set; } = new();
}
