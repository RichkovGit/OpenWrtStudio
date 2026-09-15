using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface IVpnProtocolService
{
    Task<List<AmneziaWgConfig>> GetAmneziaWgConfigsAsync();
    AmneziaWgConfig ParseAmneziaWgConf(string confContent);
    Task<(bool Success, string Message)> ApplyAmneziaWgConfigAsync(AmneziaWgConfig config);
    Task<(bool Success, string Message)> DeleteAmneziaWgConfigAsync(string ifaceName);
    Task<(bool Success, string Message)> RestartAmneziaWgAsync(string ifaceName);
    Task<(bool Success, string Message)> ApplySingboxConfigAsync(SingboxQuickConfig config);
    Task<(bool Success, string Message)> ApplyOpenVpnConfigAsync(OpenVpnClientConfig config);
    Task<(bool Success, string Message)> ConfigureTailscaleAsync(MeshVpnConfig config);
    Task<(bool Success, string Status)> GetProtocolStatusAsync(VpnProtocolType type);
}

public class VpnProtocolService : IVpnProtocolService
{
    private readonly ISshService _ssh;

    public VpnProtocolService(ISshService ssh)
    {
        _ssh = ssh;
    }

    public async Task<List<AmneziaWgConfig>> GetAmneziaWgConfigsAsync()
    {
        var list = new List<AmneziaWgConfig>();
        if (!_ssh.IsConnected) return list;

        var (code, uciOut, _) = await _ssh.ExecuteCommandAsync("uci show network", 8);
        if (code != 0 || string.IsNullOrWhiteSpace(uciOut)) return list;

        var protoMatches = Regex.Matches(uciOut, @"network\.([^\s\.]+)\.proto='?(amneziawg|wireguard)'?", RegexOptions.IgnoreCase);
        var (_, awgShowOut, _) = await _ssh.ExecuteCommandAsync("awg show 2>/dev/null || wg show 2>/dev/null", 5);

        var processedIfaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match pm in protoMatches)
        {
            var iface = pm.Groups[1].Value;
            if (processedIfaces.Contains(iface)) continue;
            processedIfaces.Add(iface);

            var escIface = Regex.Escape(iface);
            
            var config = new AmneziaWgConfig
            {
                InterfaceName = iface,
                Jc = 0,
                Jmin = 0,
                Jmax = 0,
                S1 = 0,
                S2 = 0,
                H1 = 0,
                H2 = 0,
                H3 = 0,
                H4 = 0
            };

            var privMatch = Regex.Match(uciOut, @$"network\.{escIface}\.private_key='?([^'\r\n]+)'?", RegexOptions.IgnoreCase);
            if (privMatch.Success) config.PrivateKey = privMatch.Groups[1].Value.Trim();

            var portMatch = Regex.Match(uciOut, @$"network\.{escIface}\.listen_port='?(\d+)'?", RegexOptions.IgnoreCase);
            if (portMatch.Success && int.TryParse(portMatch.Groups[1].Value, out var lp)) config.ListenPort = lp;

            var mtuMatch = Regex.Match(uciOut, @$"network\.{escIface}\.mtu='?(\d+)'?", RegexOptions.IgnoreCase);
            if (mtuMatch.Success && int.TryParse(mtuMatch.Groups[1].Value, out var mtu)) config.Mtu = mtu;

            var addrMatches = Regex.Matches(uciOut, @$"network\.{escIface}\.addresses='?([^'\r\n]+)'?", RegexOptions.IgnoreCase);
            var addrList = new List<string>();
            foreach (Match am in addrMatches) addrList.Add(am.Groups[1].Value.Trim());
            if (addrList.Count > 0) config.Address = string.Join(", ", addrList);

            var dnsMatches = Regex.Matches(uciOut, @$"network\.{escIface}\.dns='?([^'\r\n]+)'?", RegexOptions.IgnoreCase);
            var dnsList = new List<string>();
            foreach (Match dm in dnsMatches) dnsList.Add(dm.Groups[1].Value.Trim());
            if (dnsList.Count > 0) config.Dns = string.Join(", ", dnsList);

            // Obfuscation from interface section
            var jcMatch = Regex.Match(uciOut, @$"network\.{escIface}\.jc='?(\d+)'?", RegexOptions.IgnoreCase);
            if (jcMatch.Success && int.TryParse(jcMatch.Groups[1].Value, out var jc)) config.Jc = jc;

            var jminMatch = Regex.Match(uciOut, @$"network\.{escIface}\.jmin='?(\d+)'?", RegexOptions.IgnoreCase);
            if (jminMatch.Success && int.TryParse(jminMatch.Groups[1].Value, out var jmin)) config.Jmin = jmin;

            var jmaxMatch = Regex.Match(uciOut, @$"network\.{escIface}\.jmax='?(\d+)'?", RegexOptions.IgnoreCase);
            if (jmaxMatch.Success && int.TryParse(jmaxMatch.Groups[1].Value, out var jmax)) config.Jmax = jmax;

            var s1Match = Regex.Match(uciOut, @$"network\.{escIface}\.s1='?(\d+)'?", RegexOptions.IgnoreCase);
            if (s1Match.Success && int.TryParse(s1Match.Groups[1].Value, out var s1)) config.S1 = s1;

            var s2Match = Regex.Match(uciOut, @$"network\.{escIface}\.s2='?(\d+)'?", RegexOptions.IgnoreCase);
            if (s2Match.Success && int.TryParse(s2Match.Groups[1].Value, out var s2)) config.S2 = s2;

            var h1Match = Regex.Match(uciOut, @$"network\.{escIface}\.h1='?(\d+)'?", RegexOptions.IgnoreCase);
            if (h1Match.Success && uint.TryParse(h1Match.Groups[1].Value, out var h1)) config.H1 = h1;

            var h2Match = Regex.Match(uciOut, @$"network\.{escIface}\.h2='?(\d+)'?", RegexOptions.IgnoreCase);
            if (h2Match.Success && uint.TryParse(h2Match.Groups[1].Value, out var h2)) config.H2 = h2;

            var h3Match = Regex.Match(uciOut, @$"network\.{escIface}\.h3='?(\d+)'?", RegexOptions.IgnoreCase);
            if (h3Match.Success && uint.TryParse(h3Match.Groups[1].Value, out var h3)) config.H3 = h3;

            var h4Match = Regex.Match(uciOut, @$"network\.{escIface}\.h4='?(\d+)'?", RegexOptions.IgnoreCase);
            if (h4Match.Success && uint.TryParse(h4Match.Groups[1].Value, out var h4)) config.H4 = h4;

            // Peer Section Resolution (handles standard LuCI, named peers, anonymous sections)
            string? peerSecId = null;

            // 1. LuCI format: section containing .interface='<iface>'
            var ifaceRefMatch = Regex.Match(uciOut, @$"network\.([^\s\.]+)\.interface='?{escIface}'?", RegexOptions.IgnoreCase);
            if (ifaceRefMatch.Success)
            {
                peerSecId = ifaceRefMatch.Groups[1].Value;
            }

            // 2. wireguard_<iface> or amneziawg_<iface> section type
            if (peerSecId == null)
            {
                var secTypeMatch = Regex.Match(uciOut, @$"network\.([^\s\.]+)=(wireguard|amneziawg)_{escIface}\b", RegexOptions.IgnoreCase);
                if (secTypeMatch.Success) peerSecId = secTypeMatch.Groups[1].Value;
            }

            // 3. Named peer section like <iface>_peer or peer_<iface>
            if (peerSecId == null)
            {
                var namedMatch = Regex.Match(uciOut, @$"network\.({escIface}_peer|peer_{escIface})\b", RegexOptions.IgnoreCase);
                if (namedMatch.Success) peerSecId = namedMatch.Groups[1].Value;
            }

            // 4. Description containing <iface>
            if (peerSecId == null)
            {
                var descMatch = Regex.Match(uciOut, @$"network\.([^\s\.]+)\.description='?[^'\r\n]*{escIface}[^'\r\n]*'?", RegexOptions.IgnoreCase);
                if (descMatch.Success) peerSecId = descMatch.Groups[1].Value;
            }

            // 5. Fallback if single interface and single peer section
            if (peerSecId == null && protoMatches.Count == 1)
            {
                var singlePeerMatch = Regex.Match(uciOut, @"network\.([^\s\.]+)=(wireguard_peer|amneziawg_peer)\b", RegexOptions.IgnoreCase);
                if (singlePeerMatch.Success) peerSecId = singlePeerMatch.Groups[1].Value;
            }

            if (peerSecId != null)
            {
                var escPeer = Regex.Escape(peerSecId);
                var pubKeyMatch = Regex.Match(uciOut, @$"network\.{escPeer}\.public_key='?([^'\r\n]+)'?", RegexOptions.IgnoreCase);
                if (pubKeyMatch.Success) config.PeerPublicKey = pubKeyMatch.Groups[1].Value.Trim();

                var pskMatch = Regex.Match(uciOut, @$"network\.{escPeer}\.preshared_key='?([^'\r\n]+)'?", RegexOptions.IgnoreCase);
                if (pskMatch.Success) config.PeerPresharedKey = pskMatch.Groups[1].Value.Trim();

                var epHostMatch = Regex.Match(uciOut, @$"network\.{escPeer}\.endpoint_host='?([^'\r\n]+)'?", RegexOptions.IgnoreCase);
                var epPortMatch = Regex.Match(uciOut, @$"network\.{escPeer}\.endpoint_port='?(\d+)'?", RegexOptions.IgnoreCase);
                if (epHostMatch.Success)
                {
                    config.Endpoint = epHostMatch.Groups[1].Value.Trim() + (epPortMatch.Success ? $":{epPortMatch.Groups[1].Value.Trim()}" : ":51820");
                }
                else
                {
                    var epDirectMatch = Regex.Match(uciOut, @$"network\.{escPeer}\.endpoint='?([^'\r\n]+)'?", RegexOptions.IgnoreCase);
                    if (epDirectMatch.Success) config.Endpoint = epDirectMatch.Groups[1].Value.Trim();
                }

                var pkaMatch = Regex.Match(uciOut, @$"network\.{escPeer}\.persistent_keepalive='?(\d+)'?", RegexOptions.IgnoreCase);
                if (pkaMatch.Success && int.TryParse(pkaMatch.Groups[1].Value, out var pka)) config.PersistentKeepalive = pka;

                var aipMatches = Regex.Matches(uciOut, @$"network\.{escPeer}\.allowed_ips='?([^'\r\n]+)'?", RegexOptions.IgnoreCase);
                var aipList = new List<string>();
                foreach (Match aip in aipMatches) aipList.Add(aip.Groups[1].Value.Trim());
                if (aipList.Count > 0) config.AllowedIPs = string.Join(", ", aipList);

                // Obfuscation parameters might be under peer section in some configurations
                if (config.Jc == 0)
                {
                    var pJc = Regex.Match(uciOut, @$"network\.{escPeer}\.jc='?(\d+)'?", RegexOptions.IgnoreCase);
                    if (pJc.Success && int.TryParse(pJc.Groups[1].Value, out var pjc)) config.Jc = pjc;
                }
                if (config.Jmin == 0)
                {
                    var pJmin = Regex.Match(uciOut, @$"network\.{escPeer}\.jmin='?(\d+)'?", RegexOptions.IgnoreCase);
                    if (pJmin.Success && int.TryParse(pJmin.Groups[1].Value, out var pjmin)) config.Jmin = pjmin;
                }
                if (config.Jmax == 0)
                {
                    var pJmax = Regex.Match(uciOut, @$"network\.{escPeer}\.jmax='?(\d+)'?", RegexOptions.IgnoreCase);
                    if (pJmax.Success && int.TryParse(pJmax.Groups[1].Value, out var pjmax)) config.Jmax = pjmax;
                }
                if (config.S1 == 0)
                {
                    var pS1 = Regex.Match(uciOut, @$"network\.{escPeer}\.s1='?(\d+)'?", RegexOptions.IgnoreCase);
                    if (pS1.Success && int.TryParse(pS1.Groups[1].Value, out var ps1)) config.S1 = ps1;
                }
                if (config.S2 == 0)
                {
                    var pS2 = Regex.Match(uciOut, @$"network\.{escPeer}\.s2='?(\d+)'?", RegexOptions.IgnoreCase);
                    if (pS2.Success && int.TryParse(pS2.Groups[1].Value, out var ps2)) config.S2 = ps2;
                }
                if (config.H1 == 0)
                {
                    var pH1 = Regex.Match(uciOut, @$"network\.{escPeer}\.h1='?(\d+)'?", RegexOptions.IgnoreCase);
                    if (pH1.Success && uint.TryParse(pH1.Groups[1].Value, out var ph1)) config.H1 = ph1;
                }
                if (config.H2 == 0)
                {
                    var pH2 = Regex.Match(uciOut, @$"network\.{escPeer}\.h2='?(\d+)'?", RegexOptions.IgnoreCase);
                    if (pH2.Success && uint.TryParse(pH2.Groups[1].Value, out var ph2)) config.H2 = ph2;
                }
                if (config.H3 == 0)
                {
                    var pH3 = Regex.Match(uciOut, @$"network\.{escPeer}\.h3='?(\d+)'?", RegexOptions.IgnoreCase);
                    if (pH3.Success && uint.TryParse(pH3.Groups[1].Value, out var ph3)) config.H3 = ph3;
                }
                if (config.H4 == 0)
                {
                    var pH4 = Regex.Match(uciOut, @$"network\.{escPeer}\.h4='?(\d+)'?", RegexOptions.IgnoreCase);
                    if (pH4.Success && uint.TryParse(pH4.Groups[1].Value, out var ph4)) config.H4 = ph4;
                }
            }

            // Runtime status from awg show / wg show
            if (!string.IsNullOrWhiteSpace(awgShowOut))
            {
                var ifaceBlockPattern = $@"interface:\s+({escIface}|awg-{escIface}|wg-{escIface})\b(.*?)(?=\ninterface:|\Z)";
                var ifaceBlockMatch = Regex.Match(awgShowOut, ifaceBlockPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (ifaceBlockMatch.Success)
                {
                    var block = ifaceBlockMatch.Groups[2].Value;
                    config.IsActive = true;
                    config.StatusText = "Активен (туннель работает)";

                    // Obfuscation from awg show
                    var jcM = Regex.Match(block, @"\bjc:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (jcM.Success && int.TryParse(jcM.Groups[1].Value, out var jcVal)) config.Jc = jcVal;
                    var jminM = Regex.Match(block, @"\bjmin:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (jminM.Success && int.TryParse(jminM.Groups[1].Value, out var jminVal)) config.Jmin = jminVal;
                    var jmaxM = Regex.Match(block, @"\bjmax:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (jmaxM.Success && int.TryParse(jmaxM.Groups[1].Value, out var jmaxVal)) config.Jmax = jmaxVal;
                    var s1M = Regex.Match(block, @"\bs1:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (s1M.Success && int.TryParse(s1M.Groups[1].Value, out var s1Val)) config.S1 = s1Val;
                    var s2M = Regex.Match(block, @"\bs2:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (s2M.Success && int.TryParse(s2M.Groups[1].Value, out var s2Val)) config.S2 = s2Val;
                    var h1M = Regex.Match(block, @"\bh1:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (h1M.Success && uint.TryParse(h1M.Groups[1].Value, out var h1Val)) config.H1 = h1Val;
                    var h2M = Regex.Match(block, @"\bh2:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (h2M.Success && uint.TryParse(h2M.Groups[1].Value, out var h2Val)) config.H2 = h2Val;
                    var h3M = Regex.Match(block, @"\bh3:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (h3M.Success && uint.TryParse(h3M.Groups[1].Value, out var h3Val)) config.H3 = h3Val;
                    var h4M = Regex.Match(block, @"\bh4:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (h4M.Success && uint.TryParse(h4M.Groups[1].Value, out var h4Val)) config.H4 = h4Val;

                    // Peer details from awg show
                    var peerKeyMatch = Regex.Match(block, @"\bpeer:\s*([A-Za-z0-9+/=]{43,44})");
                    if (peerKeyMatch.Success && string.IsNullOrWhiteSpace(config.PeerPublicKey))
                    {
                        config.PeerPublicKey = peerKeyMatch.Groups[1].Value.Trim();
                    }

                    var epMatch = Regex.Match(block, @"\bendpoint:\s*([^\r\n]+)");
                    if (epMatch.Success && string.IsNullOrWhiteSpace(config.Endpoint))
                    {
                        config.Endpoint = epMatch.Groups[1].Value.Trim();
                    }

                    var aipMatch = Regex.Match(block, @"\ballowed ips:\s*([^\r\n]+)");
                    if (aipMatch.Success && (string.IsNullOrWhiteSpace(config.AllowedIPs) || config.AllowedIPs == "0.0.0.0/0, ::/0"))
                    {
                        config.AllowedIPs = aipMatch.Groups[1].Value.Trim();
                    }

                    var pkaMatchBlock = Regex.Match(block, @"\bpersistent keepalive:\s*(?:every\s+)?(\d+)\s+seconds", RegexOptions.IgnoreCase);
                    if (pkaMatchBlock.Success && int.TryParse(pkaMatchBlock.Groups[1].Value, out var pkaB))
                    {
                        config.PersistentKeepalive = pkaB;
                    }

                    var hsMatch = Regex.Match(block, @"\blatest handshake:\s*([^\r\n]+)");
                    if (hsMatch.Success)
                    {
                        config.LatestHandshake = hsMatch.Groups[1].Value.Trim();
                        config.StatusText = $"В сети (Handshake: {config.LatestHandshake})";
                    }

                    var trMatch = Regex.Match(block, @"\btransfer:\s*([^\r\n]+received),\s*([^\r\n]+sent)");
                    if (trMatch.Success)
                    {
                        config.TransferRx = trMatch.Groups[1].Value.Trim();
                        config.TransferTx = trMatch.Groups[2].Value.Trim();
                    }
                }
            }

            list.Add(config);
        }

        return list;
    }

    public async Task<(bool Success, string Message)> DeleteAmneziaWgConfigAsync(string ifaceName)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");
        if (string.IsNullOrWhiteSpace(ifaceName)) return (false, "Имя интерфейса не указано");

        var sb = new StringBuilder();
        sb.AppendLine($"ifdown {ifaceName} 2>/dev/null");
        sb.AppendLine($"uci -q delete network.{ifaceName}");
        sb.AppendLine($"uci -q delete network.{ifaceName}_peer");
        sb.AppendLine($"for s in $(uci show network | grep -E '=(wireguard|amneziawg)_{Regex.Escape(ifaceName)}' | cut -d'.' -f2 | cut -d'=' -f1); do uci -q delete network.$s; done");
        sb.AppendLine($"for s in $(uci show network | grep -E '\\.interface=\'{Regex.Escape(ifaceName)}\'' | cut -d'.' -f2); do uci -q delete network.$s; done");
        sb.AppendLine("uci commit network");
        sb.AppendLine("/etc/init.d/network reload 2>/dev/null || /etc/init.d/network restart");

        var (code, _, err) = await _ssh.ExecuteCommandAsync(sb.ToString(), 15);
        if (code == 0)
        {
            return (true, $"Интерфейс {ifaceName} и связанные узлы успешно удалены с роутера.");
        }
        return (false, $"Ошибка удаления интерфейса {ifaceName}: {err}");
    }

    public async Task<(bool Success, string Message)> RestartAmneziaWgAsync(string ifaceName)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");
        if (string.IsNullOrWhiteSpace(ifaceName)) return (false, "Имя интерфейса не указано");

        var (code, _, err) = await _ssh.ExecuteCommandAsync($"ifdown {ifaceName} 2>/dev/null; sleep 1; ifup {ifaceName} 2>/dev/null", 10);
        if (code == 0)
        {
            return (true, $"Интерфейс {ifaceName} перезапущен (ifdown/ifup).");
        }
        return (false, $"Ошибка перезапуска {ifaceName}: {err}");
    }

    public AmneziaWgConfig ParseAmneziaWgConf(string confContent)
    {
        var config = new AmneziaWgConfig();
        if (string.IsNullOrWhiteSpace(confContent)) return config;

        var lines = confContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        string currentSection = "";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("#") || line.StartsWith(";")) continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                currentSection = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                continue;
            }

            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim().ToLowerInvariant();
            var val = parts[1].Trim();

            if (currentSection == "interface")
            {
                switch (key)
                {
                    case "privatekey": config.PrivateKey = val; break;
                    case "address": config.Address = val; break;
                    case "dns": config.Dns = val; break;
                    case "listenport": if (int.TryParse(val, out var lp)) config.ListenPort = lp; break;
                    case "mtu": if (int.TryParse(val, out var mtu)) config.Mtu = mtu; break;
                    case "jc": if (int.TryParse(val, out var jc)) config.Jc = jc; break;
                    case "jmin": if (int.TryParse(val, out var jmin)) config.Jmin = jmin; break;
                    case "jmax": if (int.TryParse(val, out var jmax)) config.Jmax = jmax; break;
                    case "s1": if (int.TryParse(val, out var s1)) config.S1 = s1; break;
                    case "s2": if (int.TryParse(val, out var s2)) config.S2 = s2; break;
                    case "h1": if (uint.TryParse(val, out var h1)) config.H1 = h1; break;
                    case "h2": if (uint.TryParse(val, out var h2)) config.H2 = h2; break;
                    case "h3": if (uint.TryParse(val, out var h3)) config.H3 = h3; break;
                    case "h4": if (uint.TryParse(val, out var h4)) config.H4 = h4; break;
                }
            }
            else if (currentSection == "peer")
            {
                switch (key)
                {
                    case "publickey": config.PeerPublicKey = val; break;
                    case "presharedkey": config.PeerPresharedKey = val; break;
                    case "endpoint": config.Endpoint = val; break;
                    case "allowedips": config.AllowedIPs = val; break;
                    case "persistentkeepalive": if (int.TryParse(val, out var pka)) config.PersistentKeepalive = pka; break;
                }
            }
        }

        return config;
    }

    public async Task<(bool Success, string Message)> ApplyAmneziaWgConfigAsync(AmneziaWgConfig config)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var iface = string.IsNullOrWhiteSpace(config.InterfaceName) ? "awg0" : config.InterfaceName;
        var peerName = $"{iface}_peer";

        string endpointHost = "";
        string endpointPort = "51820";
        if (!string.IsNullOrWhiteSpace(config.Endpoint) && config.Endpoint.Contains(":"))
        {
            var p = config.Endpoint.Split(':');
            endpointHost = p[0];
            endpointPort = p[1];
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Configuration for {iface}");
        sb.AppendLine($"uci -q delete network.{iface}");
        sb.AppendLine($"uci -q delete network.{peerName}");
        sb.AppendLine($"for s in $(uci show network | grep -E '=(wireguard|amneziawg)_{Regex.Escape(iface)}' | cut -d'.' -f2 | cut -d'=' -f1); do uci -q delete network.$s; done");
        sb.AppendLine($"for s in $(uci show network | grep -E '\\.interface=\'{Regex.Escape(iface)}\'' | cut -d'.' -f2); do uci -q delete network.$s; done");
        sb.AppendLine($"uci set network.{iface}=interface");
        sb.AppendLine($"uci set network.{iface}.proto='amneziawg'");
        sb.AppendLine($"uci set network.{iface}.private_key='{config.PrivateKey}'");
        sb.AppendLine($"uci set network.{iface}.listen_port='{config.ListenPort}'");
        sb.AppendLine($"uci set network.{iface}.mtu='{config.Mtu}'");

        // Addresses
        var addrs = config.Address.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var a in addrs)
        {
            sb.AppendLine($"uci add_list network.{iface}.addresses='{a.Trim()}'");
        }

        // AmneziaWG Obfuscation parameters
        sb.AppendLine($"uci set network.{iface}.jc='{config.Jc}'");
        sb.AppendLine($"uci set network.{iface}.jmin='{config.Jmin}'");
        sb.AppendLine($"uci set network.{iface}.jmax='{config.Jmax}'");
        sb.AppendLine($"uci set network.{iface}.s1='{config.S1}'");
        sb.AppendLine($"uci set network.{iface}.s2='{config.S2}'");
        sb.AppendLine($"uci set network.{iface}.h1='{config.H1}'");
        sb.AppendLine($"uci set network.{iface}.h2='{config.H2}'");
        sb.AppendLine($"uci set network.{iface}.h3='{config.H3}'");
        sb.AppendLine($"uci set network.{iface}.h4='{config.H4}'");

        // Peer configuration (wireguard_<iface> + option interface for LuCI compatibility)
        sb.AppendLine($"uci set network.{peerName}=wireguard_{iface}");
        sb.AppendLine($"uci set network.{peerName}.interface='{iface}'");
        sb.AppendLine($"uci set network.{peerName}.public_key='{config.PeerPublicKey}'");
        if (!string.IsNullOrWhiteSpace(config.PeerPresharedKey))
        {
            sb.AppendLine($"uci set network.{peerName}.preshared_key='{config.PeerPresharedKey}'");
        }
        if (!string.IsNullOrWhiteSpace(endpointHost))
        {
            sb.AppendLine($"uci set network.{peerName}.endpoint_host='{endpointHost}'");
            sb.AppendLine($"uci set network.{peerName}.endpoint_port='{endpointPort}'");
        }
        sb.AppendLine($"uci set network.{peerName}.persistent_keepalive='{config.PersistentKeepalive}'");
        sb.AppendLine($"uci set network.{peerName}.route_allowed_ips='1'");

        var allowedIps = config.AllowedIPs.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var ip in allowedIps)
        {
            sb.AppendLine($"uci add_list network.{peerName}.allowed_ips='{ip.Trim()}'");
        }

        sb.AppendLine("uci commit network");
        sb.AppendLine($"ifup {iface} 2>/dev/null || /etc/init.d/network restart");

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(sb.ToString(), 20);
        if (code == 0)
        {
            return (true, $"Конфигурация AmneziaWG ({iface}) успешно сохранена в UCI и интерфейс поднят!");
        }

        return (false, $"Ошибка применения конфигурации AWG: {err}\n{outStr}");
    }

    public async Task<(bool Success, string Message)> ApplySingboxConfigAsync(SingboxQuickConfig config)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var root = new Dictionary<string, object>
        {
            ["log"] = new Dictionary<string, object>
            {
                ["level"] = "info",
                ["timestamp"] = true
            },
            ["dns"] = new Dictionary<string, object>
            {
                ["servers"] = new List<object>
                {
                    new Dictionary<string, object> { ["tag"] = "dns-remote", ["address"] = "https://1.1.1.1/dns-query", ["detour"] = "proxy" },
                    new Dictionary<string, object> { ["tag"] = "dns-direct", ["address"] = "77.88.8.8", ["detour"] = "direct" }
                }
            },
            ["inbounds"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["type"] = "tun",
                    ["tag"] = "tun-in",
                    ["interface_name"] = "singbox0",
                    ["inet4_address"] = "172.19.0.1/30",
                    ["auto_route"] = true,
                    ["strict_route"] = true,
                    ["sniff"] = true
                }
            },
            ["outbounds"] = new List<object>
            {
                CreateSingboxOutbound(config),
                new Dictionary<string, object> { ["type"] = "direct", ["tag"] = "direct" },
                new Dictionary<string, object> { ["type"] = "block", ["tag"] = "block" }
            }
        };

        var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });

        var (upSuccess, upMsg) = await _ssh.UploadFileContentAsync(json, "/etc/sing-box/config.json");
        if (!upSuccess) return (false, $"Не удалось записать /etc/sing-box/config.json: {upMsg}");

        await _ssh.ExecuteCommandAsync("/etc/init.d/sing-box restart 2>/dev/null", 10);
        return (true, "Конфигурация Sing-box успешно развернута и служба перезапущена!");
    }

    private static Dictionary<string, object> CreateSingboxOutbound(SingboxQuickConfig config)
    {
        var ob = new Dictionary<string, object>
        {
            ["type"] = config.Protocol.ToLowerInvariant(),
            ["tag"] = "proxy",
            ["server"] = config.ServerAddress,
            ["server_port"] = config.ServerPort
        };

        if (config.Protocol.Equals("vless", StringComparison.OrdinalIgnoreCase))
        {
            ob["uuid"] = config.Uuid;
            ob["flow"] = config.Flow;
            if (config.EnableReality)
            {
                ob["tls"] = new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["server_name"] = config.ServerName,
                    ["reality"] = new Dictionary<string, object>
                    {
                        ["enabled"] = true,
                        ["public_key"] = config.PublicKey,
                        ["short_id"] = config.ShortId
                    }
                };
            }
        }
        else if (config.Protocol.Equals("hysteria2", StringComparison.OrdinalIgnoreCase))
        {
            ob["password"] = config.Password;
            ob["tls"] = new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["server_name"] = config.ServerName
            };
        }

        return ob;
    }

    public async Task<(bool Success, string Message)> ApplyOpenVpnConfigAsync(OpenVpnClientConfig config)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var path = $"/etc/openvpn/{config.ProfileName}.ovpn";
        var (upSuccess, upMsg) = await _ssh.UploadFileContentAsync(config.RawOvpnContent, path);
        if (!upSuccess) return (false, $"Ошибка загрузки профиля OpenVPN: {upMsg}");

        var script = $@"
uci -q delete openvpn.{config.ProfileName}
uci set openvpn.{config.ProfileName}=openvpn
uci set openvpn.{config.ProfileName}.enabled='1'
uci set openvpn.{config.ProfileName}.config='{path}'
uci commit openvpn
/etc/init.d/openvpn restart
";
        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(script, 15);
        if (code == 0) return (true, $"Профиль OpenVPN {config.ProfileName} успешно активирован!");
        return (false, $"Ошибка активации OpenVPN: {err}\n{outStr}");
    }

    public async Task<(bool Success, string Message)> ConfigureTailscaleAsync(MeshVpnConfig config)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var sb = new StringBuilder("tailscale up");
        if (!string.IsNullOrWhiteSpace(config.TailscaleAuthKey))
        {
            sb.Append($" --authkey='{config.TailscaleAuthKey}'");
        }
        if (config.TailscaleAcceptRoutes)
        {
            sb.Append(" --accept-routes");
        }
        if (config.TailscaleExitNode)
        {
            sb.Append(" --advertise-exit-node");
        }

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(sb.ToString(), 25);
        if (code == 0) return (true, $"Команда Tailscale успешно выполнена:\n{outStr}");
        return (false, $"Ошибка Tailscale: {err}\n{outStr}");
    }

    public async Task<(bool Success, string Status)> GetProtocolStatusAsync(VpnProtocolType type)
    {
        if (!_ssh.IsConnected) return (false, "Офлайн");

        string cmd = type switch
        {
            VpnProtocolType.AmneziaWg => "awg show 2>/dev/null || wg show 2>/dev/null",
            VpnProtocolType.WireGuard => "wg show 2>/dev/null",
            VpnProtocolType.SingBox => "pidof sing-box 2>/dev/null && echo 'running' || echo 'stopped'",
            VpnProtocolType.Mihomo => "pidof mihomo 2>/dev/null && echo 'running' || echo 'stopped'",
            VpnProtocolType.OpenVpn => "pidof openvpn 2>/dev/null && echo 'running' || echo 'stopped'",
            VpnProtocolType.Tailscale => "tailscale status 2>/dev/null",
            VpnProtocolType.ZeroTier => "zerotier-cli status 2>/dev/null",
            _ => "echo 'unknown'"
        };

        var (code, outStr, _) = await _ssh.ExecuteCommandAsync(cmd, 5);
        return (code == 0, outStr.Trim());
    }
}
