using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface IClientManagerService
{
    Task<List<NetworkClient>> GetClientsAsync();
    Task<(bool Success, string Message)> KickClientAsync(string mac);
    Task<(bool Success, string Message)> BlockClientInternetAsync(string mac, bool block);
    Task<(bool Success, string Message)> SetStaticLeaseAsync(string mac, string ip, string name);
    Task<(bool Success, string Message)> DeleteStaticLeaseAsync(string mac);
}

public class ClientManagerService : IClientManagerService
{
    private readonly ISshService _ssh;

    public ClientManagerService(ISshService ssh)
    {
        _ssh = ssh;
    }

    public async Task<List<NetworkClient>> GetClientsAsync()
    {
        var result = new List<NetworkClient>();
        if (!_ssh.IsConnected) return result;

        const string batchCmd = @"
echo '===SECTION:DHCP==='
cat /tmp/dhcp.leases 2>/dev/null
echo '===SECTION:ARP==='
cat /proc/net/arp 2>/dev/null
echo '===SECTION:STATIC==='
uci show dhcp 2>/dev/null
echo '===SECTION:FIREWALL==='
uci show firewall 2>/dev/null
echo '===SECTION:STATIONS_PHY0==='
iw dev phy0-ap0 station dump 2>/dev/null
echo '===SECTION:STATIONS_PHY1==='
iw dev phy1-ap0 station dump 2>/dev/null
echo '===SECTION:END==='
";

        var (code, output, _) = await _ssh.ExecuteCommandAsync(batchCmd, 12);
        if (code != 0 && string.IsNullOrWhiteSpace(output)) return result;

        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(output, @"===SECTION:(\w+)===
?
([\s\S]*?)(?====SECTION:|$)");
        foreach (Match m in matches)
        {
            sections[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        }

        var clientMap = new Dictionary<string, NetworkClient>(StringComparer.OrdinalIgnoreCase);

        // 1. Parse DHCP Leases: timestamp mac ip hostname client-id
        if (sections.TryGetValue("DHCP", out var dhcpText) && !string.IsNullOrWhiteSpace(dhcpText))
        {
            var lines = dhcpText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    var mac = parts[1].Trim().ToLowerInvariant();
                    var ip = parts[2].Trim();
                    var host = parts.Length > 3 && parts[3] != "*" ? parts[3].Trim() : "Устройство " + mac.Substring(Math.Max(0, mac.Length - 5));

                    clientMap[mac] = new NetworkClient
                    {
                        MacAddress = mac,
                        IpAddress = ip,
                        Hostname = host,
                        IsOnline = true,
                        Band = "LAN"
                    };
                }
            }
        }

        // 2. Parse ARP: IP hw_type flags MAC mask dev
        if (sections.TryGetValue("ARP", out var arpText) && !string.IsNullOrWhiteSpace(arpText))
        {
            var lines = arpText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines.Skip(1)) // skip header
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 6)
                {
                    var ip = parts[0].Trim();
                    var flags = parts[2].Trim();
                    var mac = parts[3].Trim().ToLowerInvariant();
                    var iface = parts[5].Trim();

                    if (mac == "00:00:00:00:00:00" || flags == "0x0") continue;

                    if (!clientMap.TryGetValue(mac, out var existing))
                    {
                        existing = new NetworkClient
                        {
                            MacAddress = mac,
                            IpAddress = ip,
                            Hostname = "Устройство " + mac.Substring(Math.Max(0, mac.Length - 5)),
                            IsOnline = true,
                            Band = "LAN"
                        };
                        clientMap[mac] = existing;
                    }
                    existing.InterfaceName = iface;
                }
            }
        }

        // 3. Parse Wireless Stations on phy0-ap0 (2.4 GHz)
        if (sections.TryGetValue("STATIONS_PHY0", out var phy0Text))
        {
            ParseStations(phy0Text, "2.4 GHz", clientMap);
        }

        // 4. Parse Wireless Stations on phy1-ap0 (5 GHz)
        if (sections.TryGetValue("STATIONS_PHY1", out var phy1Text))
        {
            ParseStations(phy1Text, "5 GHz", clientMap);
        }

        // 5. Parse Static Leases from UCI
        if (sections.TryGetValue("STATIC", out var staticText) && !string.IsNullOrWhiteSpace(staticText))
        {
            var staticHosts = ParseUciStaticHosts(staticText);
            foreach (var sh in staticHosts)
            {
                if (clientMap.TryGetValue(sh.Mac, out var c))
                {
                    c.IsStaticLease = true;
                    if (!string.IsNullOrWhiteSpace(sh.Name)) c.CustomName = sh.Name;
                    if (!string.IsNullOrWhiteSpace(sh.Ip)) c.IpAddress = sh.Ip;
                }
                else
                {
                    clientMap[sh.Mac] = new NetworkClient
                    {
                        MacAddress = sh.Mac,
                        IpAddress = sh.Ip,
                        Hostname = sh.Name,
                        CustomName = sh.Name,
                        IsStaticLease = true,
                        IsOnline = false,
                        Band = "LAN"
                    };
                }
            }
        }

        // 6. Parse Firewall Block Rules from UCI
        if (sections.TryGetValue("FIREWALL", out var fwText) && !string.IsNullOrWhiteSpace(fwText))
        {
            var blockedMacs = ParseFirewallBlockedMacs(fwText);
            foreach (var bmac in blockedMacs)
            {
                if (clientMap.TryGetValue(bmac, out var c))
                {
                    c.IsBlocked = true;
                }
            }
        }

        return clientMap.Values.OrderByDescending(x => x.IsOnline)
                               .ThenBy(x => x.Band)
                               .ThenBy(x => x.DisplayName)
                               .ToList();
    }

    private static void ParseStations(string stationDump, string band, Dictionary<string, NetworkClient> clientMap)
    {
        if (string.IsNullOrWhiteSpace(stationDump)) return;
        var blocks = stationDump.Split(new[] { "Station " }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var macMatch = Regex.Match(block, @"^([0-9a-fA-F:]{17})");
            if (!macMatch.Success) continue;
            var mac = macMatch.Groups[1].Value.ToLowerInvariant();

            if (!clientMap.TryGetValue(mac, out var client))
            {
                client = new NetworkClient
                {
                    MacAddress = mac,
                    IpAddress = "N/A",
                    Hostname = "Wi-Fi Клиент " + mac.Substring(Math.Max(0, mac.Length - 5)),
                    IsOnline = true
                };
                clientMap[mac] = client;
            }

            client.Band = band;
            client.IsOnline = true;

            var sigMatch = Regex.Match(block, @"signal:\s*(-?\d+)\s*dBm");
            if (sigMatch.Success && int.TryParse(sigMatch.Groups[1].Value, out var sig))
            {
                client.SignalDbm = sig;
            }

            var rxMatch = Regex.Match(block, @"rx bitrate:\s*([^

]+)");
            if (rxMatch.Success) client.RxBitrate = rxMatch.Groups[1].Value.Trim();

            var txMatch = Regex.Match(block, @"tx bitrate:\s*([^

]+)");
            if (txMatch.Success) client.TxBitrate = txMatch.Groups[1].Value.Trim();

            var timeMatch = Regex.Match(block, @"connected time:\s*(\d+)\s*seconds");
            if (timeMatch.Success && int.TryParse(timeMatch.Groups[1].Value, out var sec))
            {
                var ts = TimeSpan.FromSeconds(sec);
                client.ConnectedDuration = ts.TotalHours >= 1 
                    ? $"{(int)ts.TotalHours}ч {ts.Minutes}м" 
                    : $"{ts.Minutes}м {ts.Seconds}с";
            }
        }
    }

    private class StaticHostInfo
    {
        public string Mac = string.Empty;
        public string Ip = string.Empty;
        public string Name = string.Empty;
    }

    private static List<StaticHostInfo> ParseUciStaticHosts(string text)
    {
        var dict = new Dictionary<string, StaticHostInfo>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"dhcp\.(@host\[\d+\]|[a-zA-Z0-9_-]+)\.(\w+)='?([^']*)'?");
            if (m.Success)
            {
                var sec = m.Groups[1].Value;
                var opt = m.Groups[2].Value;
                var val = m.Groups[3].Value;

                if (!dict.TryGetValue(sec, out var info))
                {
                    info = new StaticHostInfo();
                    dict[sec] = info;
                }

                if (opt.Equals("mac", StringComparison.OrdinalIgnoreCase)) info.Mac = val.ToLowerInvariant();
                else if (opt.Equals("ip", StringComparison.OrdinalIgnoreCase)) info.Ip = val;
                else if (opt.Equals("name", StringComparison.OrdinalIgnoreCase)) info.Name = val;
            }
        }
        return dict.Values.Where(x => !string.IsNullOrWhiteSpace(x.Mac)).ToList();
    }

    private static HashSet<string> ParseFirewallBlockedMacs(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"firewall\.[^.]+\.src_mac='?([0-9a-fA-F:]{17})'?");
            if (m.Success)
            {
                set.Add(m.Groups[1].Value.ToLowerInvariant());
            }
        }
        return set;
    }

    public async Task<(bool Success, string Message)> KickClientAsync(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return (false, "Не указан MAC-адрес");
        var cmd = "ubus call hostapd.phy0-ap0 del_client '{\\\"addr\\\":\\\"" + mac + "\\\",\\\"deauth\\\":true}' 2>/dev/null; " +
                  "ubus call hostapd.phy1-ap0 del_client '{\\\"addr\\\":\\\"" + mac + "\\\",\\\"deauth\\\":true}' 2>/dev/null";

        var (code, outStr, errStr) = await _ssh.ExecuteCommandAsync(cmd, 5);
        return (code == 0, $"Клиент {mac} отключен от беспроводной сети");
    }

    public async Task<(bool Success, string Message)> BlockClientInternetAsync(string mac, bool block)
    {
        if (string.IsNullOrWhiteSpace(mac)) return (false, "Не указан MAC-адрес");
        var cleanMac = mac.Replace(":", "_");

        string cmd;
        if (block)
        {
            cmd = $@"
uci add firewall rule
uci set firewall.@rule[-1].name='Block_{cleanMac}'
uci set firewall.@rule[-1].src='lan'
uci set firewall.@rule[-1].dest='wan'
uci set firewall.@rule[-1].src_mac='{mac}'
uci set firewall.@rule[-1].target='REJECT'
uci commit firewall
/etc/init.d/firewall reload
";
        }
        else
        {
            cmd = $@"
for s in $(uci show firewall | grep 'Block_{cleanMac}' | cut -d'.' -f2 | cut -d'=' -f1 | sort -u); do
    uci delete firewall.$s 2>/dev/null
done
uci commit firewall
/etc/init.d/firewall reload
";
        }

        var (code, outStr, errStr) = await _ssh.ExecuteCommandAsync(cmd, 10);
        return (code == 0, block ? $"Доступ в интернет заблокирован для {mac}" : $"Доступ в интернет разблокирован для {mac}");
    }

    public async Task<(bool Success, string Message)> SetStaticLeaseAsync(string mac, string ip, string name)
    {
        if (string.IsNullOrWhiteSpace(mac) || string.IsNullOrWhiteSpace(ip))
            return (false, "Не указан MAC или IP адрес");

        var cleanName = string.IsNullOrWhiteSpace(name) ? "StaticDevice" : name.Replace(" ", "_");
        var cmd = $@"
for s in $(uci show dhcp | grep -i '{mac}' | cut -d'.' -f2 | cut -d'=' -f1 | sort -u); do
    uci delete dhcp.$s 2>/dev/null
done
uci add dhcp host
uci set dhcp.@host[-1].name='{cleanName}'
uci set dhcp.@host[-1].mac='{mac}'
uci set dhcp.@host[-1].ip='{ip}'
uci commit dhcp
/etc/init.d/dnsmasq reload
";
        var (code, outStr, errStr) = await _ssh.ExecuteCommandAsync(cmd, 10);
        return (code == 0, $"Статический IP {ip} успешно закреплён за {mac}");
    }

    public async Task<(bool Success, string Message)> DeleteStaticLeaseAsync(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return (false, "Не указан MAC");
        var cmd = $@"
for s in $(uci show dhcp | grep -i '{mac}' | cut -d'.' -f2 | cut -d'=' -f1 | sort -u); do
    uci delete dhcp.$s 2>/dev/null
done
uci commit dhcp
/etc/init.d/dnsmasq reload
";
        var (code, outStr, errStr) = await _ssh.ExecuteCommandAsync(cmd, 10);
        return (code == 0, $"Фиксация статического IP удалена для {mac}");
    }
}
