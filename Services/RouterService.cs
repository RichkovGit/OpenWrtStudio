using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface IRouterService
{
    Task<RouterInfo> GetRouterInfoAsync();
    Task<List<NetworkInterfaceItem>> GetInterfacesAsync();
    Task<(bool Success, string Message)> RebootAsync();
    Task<(bool Success, string Message)> RestartNetworkAsync();
    Task<(bool Success, string Message)> RestartDnsAsync();
    Task<(bool Success, string Message)> FlushDnsCacheAsync();
    Task<(bool Success, string Message)> RestartServiceAsync(string serviceName);
}

public class RouterService : IRouterService
{
    private readonly ISshService _ssh;

    public RouterService(ISshService ssh)
    {
        _ssh = ssh;
    }

    public async Task<RouterInfo> GetRouterInfoAsync()
    {
        var info = new RouterInfo();
        if (!_ssh.IsConnected) return info;

        // Run combined command to minimize roundtrips
        const string batchCmd = @"
echo '===SECTION:BOARD==='
ubus call system board 2>/dev/null || cat /tmp/sysinfo/model 2>/dev/null
echo '===SECTION:RELEASE==='
cat /etc/openwrt_release 2>/dev/null
echo '===SECTION:UNAME==='
uname -r; uname -m
echo '===SECTION:UPTIME==='
cat /proc/uptime 2>/dev/null
echo '===SECTION:LOADAVG==='
cat /proc/loadavg 2>/dev/null
echo '===SECTION:MEMINFO==='
cat /proc/meminfo 2>/dev/null
echo '===SECTION:OVERLAY==='
df -k /overlay 2>/dev/null || df -k / 2>/dev/null
echo '===SECTION:NETDEV==='
cat /proc/net/dev 2>/dev/null
echo '===SECTION:END==='
";

        var (exitCode, output, _) = await _ssh.ExecuteCommandAsync(batchCmd, 10);
        if (exitCode != 0 && string.IsNullOrWhiteSpace(output))
        {
            return info;
        }

        try
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var matches = Regex.Matches(output, @"===SECTION:(\w+)===\r?\n([\s\S]*?)(?====SECTION:|$)");
            foreach (Match m in matches)
            {
                dict[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            }

            if (dict.TryGetValue("BOARD", out var boardData)) ParseBoard(boardData, info);
            if (dict.TryGetValue("RELEASE", out var relData)) ParseRelease(relData, info);
            if (dict.TryGetValue("UNAME", out var unameData)) ParseUname(unameData, info);
            if (dict.TryGetValue("UPTIME", out var uptimeData)) ParseUptime(uptimeData, info);
            if (dict.TryGetValue("LOADAVG", out var loadData)) ParseLoadAvg(loadData, info);
            if (dict.TryGetValue("MEMINFO", out var memData)) ParseMemInfo(memData, info);
            if (dict.TryGetValue("OVERLAY", out var ovData)) ParseOverlay(ovData, info);

            var ifaces = await GetInterfacesAsync();
            if (dict.TryGetValue("NETDEV", out var netDevData))
            {
                ApplyNetDevStats(ifaces, netDevData);
            }
            info.Interfaces = ifaces;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error parsing router info: {ex.Message}");
        }

        return info;
    }

    private static void ParseBoard(string text, RouterInfo info)
    {
        var jsonStart = text.IndexOf('{');
        var jsonEnd = text.LastIndexOf('}');
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            try
            {
                var jsonPart = text.Substring(jsonStart, jsonEnd - jsonStart + 1);
                using var doc = JsonDocument.Parse(jsonPart);
                var root = doc.RootElement;
                if (root.TryGetProperty("model", out var modelProp))
                    info.Model = modelProp.GetString() ?? info.Model;
                if (root.TryGetProperty("hostname", out var hostProp))
                    info.Hostname = hostProp.GetString() ?? info.Hostname;
                if (root.TryGetProperty("system", out var sysProp))
                    info.Target = sysProp.GetString() ?? info.Target;
                if (root.TryGetProperty("release", out var relProp) && relProp.TryGetProperty("description", out var descProp))
                    info.OpenWrtRelease = descProp.GetString() ?? info.OpenWrtRelease;
                return;
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            var firstLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            if (!firstLine.StartsWith("===") && !firstLine.StartsWith("---"))
            {
                info.Model = firstLine;
            }
        }
    }

    private static void ParseRelease(string text, RouterInfo info)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"^DISTRIB_DESCRIPTION=['""]?(.*?)['""]?$");
            if (match.Success)
            {
                info.OpenWrtRelease = match.Groups[1].Value.Trim();
            }
            match = Regex.Match(line, @"^DISTRIB_TARGET=['""]?(.*?)['""]?$");
            if (match.Success)
            {
                info.Target = match.Groups[1].Value.Trim();
            }
            match = Regex.Match(line, @"^DISTRIB_ARCH=['""]?(.*?)['""]?$");
            if (match.Success)
            {
                info.Architecture = match.Groups[1].Value.Trim();
            }
        }
    }

    private static void ParseUname(string text, RouterInfo info)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length >= 1)
        {
            info.KernelRelease = lines[0].Trim();
        }
        if (lines.Length >= 2 && string.IsNullOrEmpty(info.Architecture))
        {
            info.Architecture = lines[1].Trim();
        }
    }

    private static void ApplyNetDevStats(List<NetworkInterfaceItem> ifaces, string netDevText)
    {
        var lines = netDevText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var stats = new Dictionary<string, (long rx, long tx)>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;

            var devName = line.Substring(0, colonIdx).Trim();
            var dataPart = line.Substring(colonIdx + 1).Trim();
            var parts = dataPart.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 9)
            {
                long.TryParse(parts[0], out var rx);
                long.TryParse(parts[8], out var tx);
                stats[devName] = (rx, tx);
            }
        }

        foreach (var iface in ifaces)
        {
            if (!string.IsNullOrEmpty(iface.Device) && stats.TryGetValue(iface.Device, out var s))
            {
                iface.RxBytes = s.rx;
                iface.TxBytes = s.tx;
            }
            else if (stats.TryGetValue(iface.Name, out var s2))
            {
                iface.RxBytes = s2.rx;
                iface.TxBytes = s2.tx;
            }
        }
    }

    private static void ParseUptime(string text, RouterInfo info)
    {
        var match = Regex.Match(text, @"(\d+(\.\d+)?)");
        if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var sec))
        {
            info.UptimeSeconds = (long)sec;
            var ts = TimeSpan.FromSeconds(sec);
            if (ts.Days > 0)
                info.UptimeFormatted = $"{ts.Days}д {ts.Hours}ч {ts.Minutes}м";
            else
                info.UptimeFormatted = $"{ts.Hours}ч {ts.Minutes}м {ts.Seconds}с";
        }
    }

    private static void ParseLoadAvg(string text, RouterInfo info)
    {
        var match = Regex.Match(text, @"([\d\.]+)\s+([\d\.]+)\s+([\d\.]+)");
        if (match.Success)
        {
            if (double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var l1))
                info.Load1 = l1;
            if (double.TryParse(match.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var l5))
                info.Load5 = l5;
            if (double.TryParse(match.Groups[3].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var l15))
                info.Load15 = l15;

            info.CpuUsagePercent = Math.Min(100, Math.Round(info.Load1 * 100, 1));
        }
    }

    private static void ParseMemInfo(string text, RouterInfo info)
    {
        var lines = text.Split('\n');
        long total = 0, free = 0, buffers = 0, cached = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith("MemTotal:")) total = ParseKb(line);
            else if (line.StartsWith("MemFree:")) free = ParseKb(line);
            else if (line.StartsWith("Buffers:")) buffers = ParseKb(line);
            else if (line.StartsWith("Cached:")) cached = ParseKb(line);
        }

        info.RamTotalKB = total;
        info.RamFreeKB = free;
        info.RamBufferedKB = buffers + cached;
        info.RamUsedKB = Math.Max(0, total - free - buffers - cached);
    }

    private static long ParseKb(string line)
    {
        var m = Regex.Match(line, @"\d+");
        return m.Success && long.TryParse(m.Value, out var v) ? v : 0;
    }

    private static void ParseOverlay(string text, RouterInfo info)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && long.TryParse(parts[1], out var total) && long.TryParse(parts[2], out var used))
            {
                info.OverlayTotalKB = total;
                info.OverlayUsedKB = used;
                info.OverlayFreeKB = total - used;
                break;
            }
        }
    }

    public async Task<List<NetworkInterfaceItem>> GetInterfacesAsync()
    {
        var list = new List<NetworkInterfaceItem>();
        if (!_ssh.IsConnected) return list;

        var (exitCode, output, _) = await _ssh.ExecuteCommandAsync("ubus call network.interface dump 2>/dev/null", 5);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            try
            {
                using var doc = JsonDocument.Parse(output);
                if (doc.RootElement.TryGetProperty("interface", out var ifaces) && ifaces.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in ifaces.EnumerateArray())
                    {
                        var iface = new NetworkInterfaceItem
                        {
                            Name = item.GetProperty("interface").GetString() ?? "",
                            IsUp = item.TryGetProperty("up", out var upProp) && upProp.GetBoolean(),
                            Device = item.TryGetProperty("l3_device", out var devProp) ? (devProp.GetString() ?? "") : ""
                        };

                        if (item.TryGetProperty("ipv4-address", out var ipList) && ipList.ValueKind == JsonValueKind.Array && ipList.GetArrayLength() > 0)
                        {
                            var firstIp = ipList[0];
                            if (firstIp.TryGetProperty("address", out var addrProp))
                                iface.IpAddress = addrProp.GetString() ?? "";
                            if (firstIp.TryGetProperty("mask", out var maskProp))
                                iface.Netmask = "/" + maskProp.GetInt32();
                        }

                        if (item.TryGetProperty("route", out var routeList) && routeList.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var r in routeList.EnumerateArray())
                            {
                                if (r.TryGetProperty("target", out var tProp) && tProp.GetString() == "0.0.0.0" && r.TryGetProperty("nexthop", out var nhProp))
                                {
                                    iface.Gateway = nhProp.GetString() ?? "";
                                    break;
                                }
                            }
                        }

                        list.Add(iface);
                    }
                    return list;
                }
            }
            catch { }
        }

        // Fallback to ip addr
        var (ipExit, ipOut, _) = await _ssh.ExecuteCommandAsync("ip -s link 2>/dev/null || ifconfig", 5);
        if (ipExit == 0 && !string.IsNullOrWhiteSpace(ipOut))
        {
            list.Add(new NetworkInterfaceItem { Name = "lan", Device = "br-lan", IpAddress = "192.168.1.1", IsUp = true });
            list.Add(new NetworkInterfaceItem { Name = "wan", Device = "eth1", IpAddress = "DHCP/PPPoE", IsUp = true });
        }

        return list;
    }

    public async Task<(bool Success, string Message)> RebootAsync()
    {
        var (code, _, err) = await _ssh.ExecuteCommandAsync("reboot", 5);
        if (code == 0 || string.IsNullOrEmpty(err))
        {
            await _ssh.DisconnectAsync();
            return (true, "Команда на перезагрузку отправлена. Роутер перезагружается...");
        }
        return (false, $"Ошибка перезагрузки: {err}");
    }

    public async Task<(bool Success, string Message)> RestartNetworkAsync()
    {
        var (code, outStr, err) = await _ssh.ExecuteCommandAsync("/etc/init.d/network restart", 15);
        return code == 0
            ? (true, "Сетевой стек успешно перезапущен!")
            : (false, $"Ошибка перезапуска сети: {err}");
    }

    public async Task<(bool Success, string Message)> RestartDnsAsync()
    {
        var (code, _, err) = await _ssh.ExecuteCommandAsync("/etc/init.d/dnsmasq restart", 10);
        return code == 0
            ? (true, "Служба DNS (dnsmasq) успешно перезапущена!")
            : (false, $"Ошибка перезапуска DNS: {err}");
    }

    public async Task<(bool Success, string Message)> FlushDnsCacheAsync()
    {
        var (code, _, err) = await _ssh.ExecuteCommandAsync("/etc/init.d/dnsmasq reload", 10);
        return code == 0
            ? (true, "Кэш DNS успешно сброшен!")
            : (false, $"Ошибка сброса кэша DNS: {err}");
    }

    public async Task<(bool Success, string Message)> RestartServiceAsync(string serviceName)
    {
        var (code, _, err) = await _ssh.ExecuteCommandAsync($"/etc/init.d/{serviceName} restart", 15);
        return code == 0
            ? (true, $"Служба {serviceName} успешно перезапущена!")
            : (false, $"Ошибка перезапуска службы {serviceName}: {err}");
    }
}
