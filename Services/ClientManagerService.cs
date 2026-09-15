using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    private static readonly Dictionary<string, CachedDeviceInfo> _deviceCache = new(StringComparer.OrdinalIgnoreCase);

    private class CachedDeviceInfo
    {
        public string Hostname { get; set; } = string.Empty;
        public string Band { get; set; } = "LAN";
        public string Ip { get; set; } = string.Empty;
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }

    public ClientManagerService(ISshService ssh)
    {
        _ssh = ssh;
    }

    public async Task<List<NetworkClient>> GetClientsAsync()
    {
        var result = new List<NetworkClient>();
        if (!_ssh.IsConnected) return result;

        const string batchCmd = @"
echo '===SECTION:WAN_INFO==='
uci -q get network.wan.device || uci -q get network.wan.ifname || echo ''
ip -o route show to default 2>/dev/null | awk '{print $3, $5}'
echo '===SECTION:DHCP==='
cat /tmp/dhcp.leases 2>/dev/null
echo '===SECTION:ARP==='
cat /proc/net/arp 2>/dev/null
echo '===SECTION:IP_NEIGH==='
ip -4 neigh show 2>/dev/null
echo '===SECTION:STATIC==='
uci show dhcp 2>/dev/null
echo '===SECTION:FIREWALL==='
uci show firewall 2>/dev/null
echo '===SECTION:WIRELESS_IW==='
for wdev in $(iw dev 2>/dev/null | awk '$1==""Interface""{print $2}'); do
    echo ""---IFACE:$wdev---""
    iw dev ""$wdev"" info 2>/dev/null
    echo ""---STATIONS:$wdev---""
    iw dev ""$wdev"" station dump 2>/dev/null
done
echo '===SECTION:WIRELESS_IWINFO==='
if which iwinfo >/dev/null 2>&1; then
    for wdev in $(iwinfo 2>/dev/null | awk '/ESSID/{print $1}'); do
        echo ""---IWINFO_INFO:$wdev---""
        iwinfo ""$wdev"" info 2>/dev/null
        echo ""---IWINFO_ASSOC:$wdev---""
        iwinfo ""$wdev"" assoclist 2>/dev/null
    done
fi
echo '===SECTION:HOSTAPD_UBUS==='
for h in $(ubus list 'hostapd.*' 2>/dev/null); do
    echo ""---HOSTAPD:$h---""
    ubus call ""$h"" get_clients 2>/dev/null
done
echo '===SECTION:END==='
";

        var (code, output, _) = await _ssh.ExecuteCommandAsync(batchCmd, 15);
        if (code != 0 && string.IsNullOrWhiteSpace(output)) return result;

        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(output, @"===SECTION:(\w+)===?\r?\n([\s\S]*?)(?====SECTION:|$)");
        foreach (Match m in matches)
        {
            sections[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        }

        // Identify WAN Gateway & WAN Interfaces
        var wanIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wanIfaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wan", "wan6", "pppoe-wan" };

        if (sections.TryGetValue("WAN_INFO", out var wanText) && !string.IsNullOrWhiteSpace(wanText))
        {
            var wanLines = wanText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var wline in wanLines)
            {
                var wparts = wline.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (wparts.Length >= 2)
                {
                    wanIps.Add(wparts[0].Trim());
                    wanIfaces.Add(wparts[1].Trim());
                }
                else if (wparts.Length == 1)
                {
                    wanIfaces.Add(wparts[0].Trim());
                }
            }
        }

        var clientMap = new Dictionary<string, NetworkClient>(StringComparer.OrdinalIgnoreCase);
        var activeWifiMacs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeLanMacs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Parse Wireless Stations from 'iw dev'
        if (sections.TryGetValue("WIRELESS_IW", out var iwText) && !string.IsNullOrWhiteSpace(iwText))
        {
            ParseIwWireless(iwText, clientMap, activeWifiMacs);
        }

        // 2. Parse Wireless Stations from 'iwinfo'
        if (sections.TryGetValue("WIRELESS_IWINFO", out var iwinfoText) && !string.IsNullOrWhiteSpace(iwinfoText))
        {
            ParseIwinfoWireless(iwinfoText, clientMap, activeWifiMacs);
        }

        // 3. Parse Wireless Stations from 'hostapd.*' ubus
        if (sections.TryGetValue("HOSTAPD_UBUS", out var hostapdText) && !string.IsNullOrWhiteSpace(hostapdText))
        {
            ParseHostapdUbus(hostapdText, clientMap, activeWifiMacs);
        }

        // 4. Parse Active LAN ARP & IP Neighbor entries
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

                    if (mac == "00:00:00:00:00:00" || mac.Length != 17) continue;
                    if (flags == "0x0") continue; // incomplete / inactive
                    if (wanIps.Contains(ip) || wanIfaces.Contains(iface)) continue; // ignore WAN gateway

                    if (activeWifiMacs.Contains(mac))
                    {
                        if (clientMap.TryGetValue(mac, out var existingWifi))
                        {
                            if (string.IsNullOrWhiteSpace(existingWifi.IpAddress) || existingWifi.IpAddress == "N/A")
                                existingWifi.IpAddress = ip;
                        }
                    }
                    else
                    {
                        // True Active LAN Wired Client
                        activeLanMacs.Add(mac);
                        if (!clientMap.TryGetValue(mac, out var lanClient))
                        {
                            lanClient = new NetworkClient
                            {
                                MacAddress = mac,
                                IpAddress = ip,
                                Hostname = "Устройство " + mac.Substring(Math.Max(0, mac.Length - 5)),
                                IsOnline = true,
                                Band = "LAN"
                            };
                            clientMap[mac] = lanClient;
                        }
                        lanClient.IsOnline = true;
                        lanClient.Band = "LAN";
                        lanClient.InterfaceName = iface;
                    }
                }
            }
        }

        // Also check IP_NEIGH
        if (sections.TryGetValue("IP_NEIGH", out var neighText) && !string.IsNullOrWhiteSpace(neighText))
        {
            var lines = neighText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                // e.g. "192.168.0.214 dev br-lan lladdr 70:5f:a3:14:28:86 REACHABLE"
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    var ip = parts[0].Trim();
                    var devIdx = Array.IndexOf(parts, "dev");
                    var dev = devIdx >= 0 && devIdx + 1 < parts.Length ? parts[devIdx + 1].Trim() : "";
                    var lladdrIdx = Array.IndexOf(parts, "lladdr");
                    var mac = lladdrIdx >= 0 && lladdrIdx + 1 < parts.Length ? parts[lladdrIdx + 1].Trim().ToLowerInvariant() : "";
                    var state = parts[^1].Trim().ToUpperInvariant();

                    if (string.IsNullOrWhiteSpace(mac) || mac.Length != 17 || mac == "00:00:00:00:00:00") continue;
                    if (wanIps.Contains(ip) || wanIfaces.Contains(dev)) continue;
                    if (state == "FAILED") continue;

                    if (activeWifiMacs.Contains(mac))
                    {
                        if (clientMap.TryGetValue(mac, out var existingWifi))
                        {
                            if (string.IsNullOrWhiteSpace(existingWifi.IpAddress) || existingWifi.IpAddress == "N/A")
                                existingWifi.IpAddress = ip;
                        }
                    }
                    else if (state == "REACHABLE" || state == "DELAY" || state == "STALE" || state == "PROBE")
                    {
                        activeLanMacs.Add(mac);
                        if (!clientMap.TryGetValue(mac, out var lanClient))
                        {
                            lanClient = new NetworkClient
                            {
                                MacAddress = mac,
                                IpAddress = ip,
                                Hostname = "Устройство " + mac.Substring(Math.Max(0, mac.Length - 5)),
                                IsOnline = true,
                                Band = "LAN"
                            };
                            clientMap[mac] = lanClient;
                        }
                        lanClient.IsOnline = true;
                        lanClient.Band = "LAN";
                        lanClient.InterfaceName = dev;
                    }
                }
            }
        }

        // 5. Parse DHCP Leases: timestamp mac ip hostname client-id
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

                    if (wanIps.Contains(ip) || wanIfaces.Any(w => host.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;

                    var isRealHost = !string.IsNullOrWhiteSpace(host) && host != "*" && !host.StartsWith("Устройство") && !host.StartsWith("Wi-Fi");
                    if (isRealHost)
                    {
                        if (!_deviceCache.TryGetValue(mac, out var cInfo))
                        {
                            cInfo = new CachedDeviceInfo();
                            _deviceCache[mac] = cInfo;
                        }
                        cInfo.Hostname = host;
                        cInfo.Ip = ip;
                    }

                    if (clientMap.TryGetValue(mac, out var existing))
                    {
                        if (string.IsNullOrWhiteSpace(existing.IpAddress) || existing.IpAddress == "N/A") existing.IpAddress = ip;
                        if (!string.IsNullOrWhiteSpace(host) && (string.IsNullOrWhiteSpace(existing.Hostname) || existing.Hostname.StartsWith("Устройство") || existing.Hostname.StartsWith("Wi-Fi")))
                        {
                            existing.Hostname = host;
                        }
                    }
                    else
                    {
                        // Inactive/Offline lease reservation
                        clientMap[mac] = new NetworkClient
                        {
                            MacAddress = mac,
                            IpAddress = ip,
                            Hostname = host,
                            IsOnline = false,
                            Band = "LAN"
                        };
                    }
                }
            }
        }

        // 6. Parse Static Leases from UCI
        if (sections.TryGetValue("STATIC", out var staticText) && !string.IsNullOrWhiteSpace(staticText))
        {
            var staticHosts = ParseUciStaticHosts(staticText);
            foreach (var sh in staticHosts)
            {
                if (!string.IsNullOrWhiteSpace(sh.Name))
                {
                    if (!_deviceCache.TryGetValue(sh.Mac, out var cInfo))
                    {
                        cInfo = new CachedDeviceInfo();
                        _deviceCache[sh.Mac] = cInfo;
                    }
                    cInfo.Hostname = sh.Name;
                    cInfo.Ip = sh.Ip;
                }

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

        // 7. Parse Firewall Block Rules from UCI
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

        // 8. Ensure all clients have their real cached hostnames
        foreach (var client in clientMap.Values)
        {
            var isGeneric = string.IsNullOrWhiteSpace(client.Hostname) || 
                            client.Hostname.StartsWith("Устройство") || 
                            client.Hostname.StartsWith("Wi-Fi");
            if (isGeneric && _deviceCache.TryGetValue(client.MacAddress, out var cInfo) && !string.IsNullOrWhiteSpace(cInfo.Hostname))
            {
                client.Hostname = cInfo.Hostname;
            }
            else if (!isGeneric && !string.IsNullOrWhiteSpace(client.Hostname))
            {
                if (!_deviceCache.TryGetValue(client.MacAddress, out var cInfo2))
                {
                    cInfo2 = new CachedDeviceInfo();
                    _deviceCache[client.MacAddress] = cInfo2;
                }
                cInfo2.Hostname = client.Hostname;
                if (!string.IsNullOrWhiteSpace(client.IpAddress) && client.IpAddress != "N/A") cInfo2.Ip = client.IpAddress;
                if (client.IsOnline) cInfo2.Band = client.Band;
            }
        }

        // 9. Add any previously known devices that went offline so they appear in Offline count
        foreach (var (cachedMac, cachedInfo) in _deviceCache)
        {
            if (!clientMap.ContainsKey(cachedMac) && !wanIps.Contains(cachedInfo.Ip))
            {
                clientMap[cachedMac] = new NetworkClient
                {
                    MacAddress = cachedMac,
                    IpAddress = string.IsNullOrWhiteSpace(cachedInfo.Ip) ? "N/A" : cachedInfo.Ip,
                    Hostname = cachedInfo.Hostname,
                    IsOnline = false,
                    Band = cachedInfo.Band
                };
            }
        }

        return clientMap.Values.OrderByDescending(x => x.IsOnline)
                               .ThenBy(x => x.Band)
                               .ThenBy(x => x.DisplayName)
                               .ToList();
    }

    private static void ParseIwWireless(string iwText, Dictionary<string, NetworkClient> clientMap, HashSet<string> activeWifiMacs)
    {
        var ifaceBlocks = Regex.Split(iwText, @"---IFACE:(\S+)---");
        for (int i = 1; i < ifaceBlocks.Length; i += 2)
        {
            var dev = ifaceBlocks[i].Trim();
            var content = ifaceBlocks[i + 1];

            // Determine Band
            string band = DetectBandFromInfo(dev, content);

            // Split into stations part
            var stationBlocks = content.Split(new[] { "Station " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in stationBlocks.Skip(1)) // first is info part
            {
                var macMatch = Regex.Match(block, @"^([0-9a-fA-F:]{17})");
                if (!macMatch.Success) continue;
                var mac = macMatch.Groups[1].Value.ToLowerInvariant();
                activeWifiMacs.Add(mac);

                if (!clientMap.TryGetValue(mac, out var client))
                {
                    client = new NetworkClient
                    {
                        MacAddress = mac,
                        IpAddress = "N/A",
                        Hostname = "Wi-Fi Клиент " + mac.Substring(Math.Max(0, mac.Length - 5))
                    };
                    clientMap[mac] = client;
                }

                client.Band = band;
                client.IsOnline = true;
                client.InterfaceName = dev;

                var sigMatch = Regex.Match(block, @"signal:\s*(-?\d+)\s*dBm");
                if (sigMatch.Success && int.TryParse(sigMatch.Groups[1].Value, out var sig))
                    client.SignalDbm = sig;

                var rxMatch = Regex.Match(block, @"rx bitrate:\s*([^\r\n]+)");
                if (rxMatch.Success) client.RxBitrate = rxMatch.Groups[1].Value.Trim();

                var txMatch = Regex.Match(block, @"tx bitrate:\s*([^\r\n]+)");
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
    }

    private static void ParseIwinfoWireless(string iwinfoText, Dictionary<string, NetworkClient> clientMap, HashSet<string> activeWifiMacs)
    {
        var ifaceBlocks = Regex.Split(iwinfoText, @"---IWINFO_INFO:(\S+)---");
        for (int i = 1; i < ifaceBlocks.Length; i += 2)
        {
            var dev = ifaceBlocks[i].Trim();
            var content = ifaceBlocks[i + 1];

            string band = DetectBandFromInfo(dev, content);

            // Look for MAC addresses in assoclist
            var macMatches = Regex.Matches(content, @"([0-9a-fA-F:]{17})\s+(-?\d+)\s*dBm");
            foreach (Match m in macMatches)
            {
                var mac = m.Groups[1].Value.ToLowerInvariant();
                activeWifiMacs.Add(mac);

                if (!clientMap.TryGetValue(mac, out var client))
                {
                    client = new NetworkClient
                    {
                        MacAddress = mac,
                        IpAddress = "N/A",
                        Hostname = "Wi-Fi Клиент " + mac.Substring(Math.Max(0, mac.Length - 5))
                    };
                    clientMap[mac] = client;
                }

                client.Band = band;
                client.IsOnline = true;
                client.InterfaceName = dev;

                if (int.TryParse(m.Groups[2].Value, out var sig) && (client.SignalDbm == 0 || sig != 0))
                {
                    client.SignalDbm = sig;
                }
            }
        }
    }

    private static void ParseHostapdUbus(string hostapdText, Dictionary<string, NetworkClient> clientMap, HashSet<string> activeWifiMacs)
    {
        var blocks = Regex.Split(hostapdText, @"---HOSTAPD:(\S+)---");
        for (int i = 1; i < blocks.Length; i += 2)
        {
            var hName = blocks[i].Trim();
            var json = blocks[i + 1].Trim();
            if (string.IsNullOrWhiteSpace(json)) continue;

            string band = hName.Contains("1") || hName.Contains("5g") ? "5 GHz" : "2.4 GHz";

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("freq", out var freqEl) && freqEl.TryGetInt32(out var freq))
                {
                    if (freq > 5900) band = "6 GHz";
                    else if (freq >= 5000) band = "5 GHz";
                    else if (freq < 3000) band = "2.4 GHz";
                }

                if (root.TryGetProperty("clients", out var clientsEl) && clientsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in clientsEl.EnumerateObject())
                    {
                        var mac = prop.Name.ToLowerInvariant();
                        if (mac.Length != 17) continue;

                        activeWifiMacs.Add(mac);

                        if (!clientMap.TryGetValue(mac, out var client))
                        {
                            client = new NetworkClient
                            {
                                MacAddress = mac,
                                IpAddress = "N/A",
                                Hostname = "Wi-Fi " + mac.Substring(Math.Max(0, mac.Length - 5))
                            };
                            clientMap[mac] = client;
                        }

                        client.Band = band;
                        client.IsOnline = true;
                        client.InterfaceName = hName.Replace("hostapd.", "");

                        if (prop.Value.TryGetProperty("signal", out var sigEl) && sigEl.TryGetInt32(out var sig))
                        {
                            client.SignalDbm = sig;
                        }
                    }
                }
            }
            catch
            {
                var matches = Regex.Matches(json, @"""([0-9a-fA-F:]{17})""\s*:\s*\{");
                foreach (Match m in matches)
                {
                    var mac = m.Groups[1].Value.ToLowerInvariant();
                    activeWifiMacs.Add(mac);
                    if (!clientMap.TryGetValue(mac, out var client))
                    {
                        client = new NetworkClient
                        {
                            MacAddress = mac,
                            IpAddress = "N/A",
                            Hostname = "Wi-Fi " + mac.Substring(Math.Max(0, mac.Length - 5))
                        };
                        clientMap[mac] = client;
                    }
                    client.Band = band;
                    client.IsOnline = true;
                }
            }
        }
    }

    private static string DetectBandFromInfo(string devName, string text)
    {
        var dLower = devName.ToLowerInvariant();
        if (dLower.Contains("6g")) return "6 GHz";
        if (dLower.Contains("5g") || dLower.Contains("wl1") || dLower.Contains("rai") || dLower.Contains("rax") || dLower.Contains("phy1")) return "5 GHz";

        var freqMatch = Regex.Match(text, @"(?:frequency|freq|channel|Channel):\s*(\d+)");
        if (freqMatch.Success && int.TryParse(freqMatch.Groups[1].Value, out var val))
        {
            if (val > 5900) return "6 GHz";
            if (val >= 5000 || (val >= 36 && val <= 177)) return "5 GHz";
            if (val < 3000 || (val >= 1 && val <= 14)) return "2.4 GHz";
        }

        if (text.Contains("5.320") || text.Contains("5.180") || text.Contains("5.2") || text.Contains("5.8") || text.Contains("802.11ac") || text.Contains("802.11ax") || text.Contains("VHT") || text.Contains("HE80"))
            return "5 GHz";

        return "2.4 GHz";
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
        var blockSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"firewall\.([^\.]+)\.name='?Block_([0-9a-fA-F_]{17})(?:_(?:wan|input|dhcp|luci))?'?", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                blockSections.Add(m.Groups[1].Value);
            }
        }

        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"firewall\.([^\.]+)\.src_mac='?([0-9a-fA-F:]{17})'?");
            if (m.Success)
            {
                var sec = m.Groups[1].Value;
                if (blockSections.Contains(sec))
                {
                    set.Add(m.Groups[2].Value.ToLowerInvariant());
                }
            }
        }
        return set;
    }

    public async Task<(bool Success, string Message)> KickClientAsync(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return (false, "Не указан MAC-адрес");
        var cleanMac = mac.ToLowerInvariant();
        var cmd = $@"
for h in $(ubus list 'hostapd.*' 2>/dev/null); do
    ubus call ""$h"" del_client '{{\""addr\"":\""{cleanMac}\"",\""deauth\"":true,\""reason\"":1,\""ban_time\"":20000}}' 2>/dev/null
done
for iface in $(ls /sys/class/net 2>/dev/null); do
    ip neigh del ""{cleanMac}"" dev ""$iface"" 2>/dev/null
done
";

        var (code, outStr, errStr) = await _ssh.ExecuteCommandAsync(cmd, 6);
        return (code == 0, $"Сессия устройства {mac} сброшена (деавторизация Wi-Fi)");
    }

    public async Task<(bool Success, string Message)> BlockClientInternetAsync(string mac, bool block)
    {
        if (string.IsNullOrWhiteSpace(mac)) return (false, "Не указан MAC-адрес");
        var cleanMac = mac.Replace(":", "_");

        string cmd;
        if (block)
        {
            cmd = $@"
# Clean any existing rules first to prevent duplicates
while true; do
    sec=$(uci show firewall | grep -i 'Block_{cleanMac}' | head -n1 | cut -d'.' -f2 | cut -d'=' -f1)
    [ -z ""$sec"" ] && break
    uci delete firewall.$sec 2>/dev/null
done

# 1. Allow DHCP to keep client connected to LAN
uci add firewall rule
uci set firewall.@rule[-1].name='Block_{cleanMac}_dhcp'
uci set firewall.@rule[-1].src='lan'
uci set firewall.@rule[-1].src_mac='{mac}'
uci set firewall.@rule[-1].proto='udp'
uci set firewall.@rule[-1].dest_port='67 68'
uci set firewall.@rule[-1].target='ACCEPT'

# 2. Allow router web management (ports 80 & 443) so client can manage router & unblock
uci add firewall rule
uci set firewall.@rule[-1].name='Block_{cleanMac}_luci'
uci set firewall.@rule[-1].src='lan'
uci set firewall.@rule[-1].src_mac='{mac}'
uci set firewall.@rule[-1].proto='tcp'
uci set firewall.@rule[-1].dest_port='80 443'
uci set firewall.@rule[-1].target='ACCEPT'

# 3. Drop all other traffic to router (TPROXY :1602 Sing-box, DNS :53, ForkOP DNS :1603)
uci add firewall rule
uci set firewall.@rule[-1].name='Block_{cleanMac}_input'
uci set firewall.@rule[-1].src='lan'
uci set firewall.@rule[-1].src_mac='{mac}'
uci set firewall.@rule[-1].target='DROP'

# 4. Drop direct WAN traffic
uci add firewall rule
uci set firewall.@rule[-1].name='Block_{cleanMac}_wan'
uci set firewall.@rule[-1].src='lan'
uci set firewall.@rule[-1].dest='wan'
uci set firewall.@rule[-1].src_mac='{mac}'
uci set firewall.@rule[-1].target='DROP'

uci commit firewall
/etc/init.d/firewall reload

for h in $(ubus list 'hostapd.*' 2>/dev/null); do ubus call ""$h"" del_client '{{\""addr\"":\""{mac}\"",\""deauth\"":true,\""ban_time\"":60000}}' 2>/dev/null; done
";
        }
        else
        {
            cmd = $@"
while true; do
    sec=$(uci show firewall | grep -i 'Block_{cleanMac}' | head -n1 | cut -d'.' -f2 | cut -d'=' -f1)
    [ -z ""$sec"" ] && break
    uci delete firewall.$sec 2>/dev/null
done
uci commit firewall
/etc/init.d/firewall reload
";
        }

        var (code, outStr, errStr) = await _ssh.ExecuteCommandAsync(cmd, 10);
        return (code == 0, block ? $"Доступ в интернет (включая VPN/иностранный трафик) заблокирован для {mac}" : $"Доступ в интернет разблокирован для {mac}");
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
