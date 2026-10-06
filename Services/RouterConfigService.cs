using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface IRouterConfigService
{
    Task<List<WifiRadioConfig>> GetWifiConfigsAsync();
    Task<(bool Success, string Message)> ApplyWifiConfigAsync(List<WifiRadioConfig> radios);
    Task<LanDhcpConfig> GetLanDhcpConfigAsync();
    Task<(bool Success, string Message)> ApplyLanDhcpConfigAsync(LanDhcpConfig config);
    Task<WanConfig> GetWanConfigAsync();
    Task<(bool Success, string Message)> ApplyWanConfigAsync(WanConfig config);
    Task<List<PortForwardRule>> GetPortForwardRulesAsync();
    Task<(bool Success, string Message)> SavePortForwardRuleAsync(PortForwardRule rule);
    Task<(bool Success, string Message)> DeletePortForwardRuleAsync(string sectionId);
    Task<SystemAdminConfig> GetSystemAdminConfigAsync();
    Task<(bool Success, string Message)> ApplySystemAdminConfigAsync(SystemAdminConfig config);
}

public class RouterConfigService : IRouterConfigService
{
    private readonly ISshService _ssh;

    public RouterConfigService(ISshService ssh)
    {
        _ssh = ssh;
    }

    public async Task<List<WifiRadioConfig>> GetWifiConfigsAsync()
    {
        var list = new List<WifiRadioConfig>();
        if (!_ssh.IsConnected) return list;

        var (code, outStr, _) = await _ssh.ExecuteCommandAsync("uci show wireless", 5);
        if (code != 0 || string.IsNullOrWhiteSpace(outStr)) return list;

        // Group by radio
        var radioMatches = Regex.Matches(outStr, @"wireless\.([^\s\.]+)=wifi-device");
        foreach (Match rm in radioMatches)
        {
            var rName = rm.Groups[1].Value;
            var escR = Regex.Escape(rName);
            var radio = new WifiRadioConfig { Device = rName };

            var bandMatch = Regex.Match(outStr, @$"wireless\.{escR}\.band='?([^'\r\n]+)'?");
            var chanMatch = Regex.Match(outStr, @$"wireless\.{escR}\.channel='?([^'\r\n]+)'?");
            var htMatch = Regex.Match(outStr, @$"wireless\.{escR}\.htmode='?([^'\r\n]+)'?");
            var disMatch = Regex.Match(outStr, @$"wireless\.{escR}\.disabled='?([01])'?");

            radio.Band = bandMatch.Success ? bandMatch.Groups[1].Value : (rName.Contains("1") ? "5 GHz" : "2.4 GHz");
            radio.Channel = chanMatch.Success ? chanMatch.Groups[1].Value : "auto";
            radio.HtMode = htMatch.Success ? htMatch.Groups[1].Value : "HT40";
            radio.Disabled = disMatch.Success && disMatch.Groups[1].Value == "1";

            list.Add(radio);
        }

        // Interfaces
        var ifaceMatches = Regex.Matches(outStr, @"wireless\.([^\s\.]+)=wifi-iface");
        foreach (Match im in ifaceMatches)
        {
            var ifName = im.Groups[1].Value;
            var escIf = Regex.Escape(ifName);
            var devMatch = Regex.Match(outStr, @$"wireless\.{escIf}\.device='?([^'\r\n]+)'?");
            var ssidMatch = Regex.Match(outStr, @$"wireless\.{escIf}\.ssid='?([^'\r\n]+)'?");
            var encMatch = Regex.Match(outStr, @$"wireless\.{escIf}\.encryption='?([^'\r\n]+)'?");
            var keyMatch = Regex.Match(outStr, @$"wireless\.{escIf}\.key='?([^'\r\n]+)'?");
            var disMatch = Regex.Match(outStr, @$"wireless\.{escIf}\.disabled='?([01])'?");

            var dev = devMatch.Success ? devMatch.Groups[1].Value : "radio0";
            var iface = new WifiIfaceConfig
            {
                SectionId = ifName,
                Device = dev,
                Ssid = ssidMatch.Success ? ssidMatch.Groups[1].Value : "OpenWrt",
                Encryption = encMatch.Success ? encMatch.Groups[1].Value : "psk2",
                Key = keyMatch.Success ? keyMatch.Groups[1].Value : "",
                Disabled = disMatch.Success && disMatch.Groups[1].Value == "1"
            };

            var targetRadio = list.Find(r => r.Device == dev);
            if (targetRadio != null)
            {
                targetRadio.Interfaces.Add(iface);
            }
            else if (list.Count > 0)
            {
                list[0].Interfaces.Add(iface);
            }
        }

        return list;
    }

    public async Task<(bool Success, string Message)> ApplyWifiConfigAsync(List<WifiRadioConfig> radios)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var sb = new StringBuilder();
        foreach (var r in radios)
        {
            var cleanDev = ShellHelper.SanitizeIdentifier(r.Device, "radio0");
            sb.AppendLine($"uci set wireless.{cleanDev}.channel={ShellHelper.EscapeArg(r.Channel)}");
            sb.AppendLine($"uci set wireless.{cleanDev}.htmode={ShellHelper.EscapeArg(r.HtMode)}");
            sb.AppendLine($"uci set wireless.{cleanDev}.disabled='{(r.Disabled ? 1 : 0)}'");

            foreach (var iface in r.Interfaces)
            {
                var rawId = string.IsNullOrWhiteSpace(iface.SectionId) ? $"default_{cleanDev}" : iface.SectionId;
                var cleanId = ShellHelper.SanitizeIdentifier(rawId, $"default_{cleanDev}");
                sb.AppendLine($"uci set wireless.{cleanId}.ssid={ShellHelper.EscapeArg(iface.Ssid)}");
                sb.AppendLine($"uci set wireless.{cleanId}.encryption={ShellHelper.EscapeArg(iface.Encryption)}");
                if (!string.IsNullOrWhiteSpace(iface.Key))
                {
                    sb.AppendLine($"uci set wireless.{cleanId}.key={ShellHelper.EscapeArg(iface.Key)}");
                }
                sb.AppendLine($"uci set wireless.{cleanId}.disabled='{(iface.Disabled ? 1 : 0)}'");
            }
        }

        sb.AppendLine("uci commit wireless");
        sb.AppendLine("wifi reload");

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(sb.ToString(), 15);
        if (code == 0) return (true, "Параметры Wi-Fi успешно сохранены и перезапущены!");
        return (false, $"Ошибка применения Wi-Fi: {err}\n{outStr}");
    }

    public async Task<LanDhcpConfig> GetLanDhcpConfigAsync()
    {
        var cfg = new LanDhcpConfig();
        if (!_ssh.IsConnected) return cfg;

        var (_, netOut, _) = await _ssh.ExecuteCommandAsync("uci show network.lan", 5);
        var ipM = Regex.Match(netOut, @"network\.lan\.ipaddr='?([^'\r\n]+)'?");
        var maskM = Regex.Match(netOut, @"network\.lan\.netmask='?([^'\r\n]+)'?");
        if (ipM.Success) cfg.IpAddress = ipM.Groups[1].Value;
        if (maskM.Success) cfg.Netmask = maskM.Groups[1].Value;

        var (_, dhcpOut, _) = await _ssh.ExecuteCommandAsync("uci show dhcp", 5);
        var startM = Regex.Match(dhcpOut, @"dhcp\.lan\.start='?(\d+)'?");
        var limitM = Regex.Match(dhcpOut, @"dhcp\.lan\.limit='?(\d+)'?");
        var leaseM = Regex.Match(dhcpOut, @"dhcp\.lan\.leasetime='?([^'\r\n]+)'?");
        if (startM.Success && int.TryParse(startM.Groups[1].Value, out var s)) cfg.DhcpStart = s;
        if (limitM.Success && int.TryParse(limitM.Groups[1].Value, out var l)) cfg.DhcpLimit = l;
        if (leaseM.Success) cfg.LeaseTime = leaseM.Groups[1].Value;

        // Static Leases
        var hostMatches = Regex.Matches(dhcpOut, @"dhcp\.([^\s\.]+)=host");
        foreach (Match hm in hostMatches)
        {
            var secId = hm.Groups[1].Value;
            var escSecId = Regex.Escape(secId);
            var nameM = Regex.Match(dhcpOut, @$"dhcp\.{escSecId}\.name='?([^'\r\n]+)'?");
            var macM = Regex.Match(dhcpOut, @$"dhcp\.{escSecId}\.mac='?([^'\r\n]+)'?");
            var ipLM = Regex.Match(dhcpOut, @$"dhcp\.{escSecId}\.ip='?([^'\r\n]+)'?");

            var name = nameM.Success ? nameM.Groups[1].Value : "";
            var mac = macM.Success ? macM.Groups[1].Value : "";
            var ip = ipLM.Success ? ipLM.Groups[1].Value : "";

            if (string.IsNullOrWhiteSpace(name))
            {
                name = !string.IsNullOrWhiteSpace(mac) && mac.Length >= 5
                    ? $"Устройство {mac[^5..]}"
                    : "Клиент";
            }

            cfg.StaticLeases.Add(new StaticDhcpLease
            {
                SectionId = secId,
                Name = name,
                Mac = mac,
                Ip = ip
            });
        }

        return cfg;
    }

    public async Task<(bool Success, string Message)> ApplyLanDhcpConfigAsync(LanDhcpConfig config)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var sb = new StringBuilder();
        sb.AppendLine($"uci set network.lan.ipaddr={ShellHelper.EscapeArg(config.IpAddress)}");
        sb.AppendLine($"uci set network.lan.netmask={ShellHelper.EscapeArg(config.Netmask)}");
        sb.AppendLine($"uci set dhcp.lan.start='{Math.Clamp(config.DhcpStart, 1, 254)}'");
        sb.AppendLine($"uci set dhcp.lan.limit='{Math.Clamp(config.DhcpLimit, 1, 254)}'");
        sb.AppendLine($"uci set dhcp.lan.leasetime={ShellHelper.EscapeArg(string.IsNullOrWhiteSpace(config.LeaseTime) ? "12h" : config.LeaseTime)}");

        sb.AppendLine("uci commit network");
        sb.AppendLine("uci commit dhcp");
        sb.AppendLine("/etc/init.d/dnsmasq restart");

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(sb.ToString(), 12);
        if (code == 0) return (true, "Настройки сети и DHCP успешно применены!");
        return (false, $"Ошибка сохранения: {err}\n{outStr}");
    }

    public async Task<WanConfig> GetWanConfigAsync()
    {
        var cfg = new WanConfig();
        if (!_ssh.IsConnected) return cfg;

        var (_, outStr, _) = await _ssh.ExecuteCommandAsync("uci show network.wan", 5);
        var protoM = Regex.Match(outStr, @"network\.wan\.proto='?([^'\r\n]+)'?");
        var userM = Regex.Match(outStr, @"network\.wan\.username='?([^'\r\n]+)'?");
        var passM = Regex.Match(outStr, @"network\.wan\.password='?([^'\r\n]+)'?");
        var mtuM = Regex.Match(outStr, @"network\.wan\.mtu='?(\d+)'?");

        if (protoM.Success) cfg.Proto = protoM.Groups[1].Value;
        if (userM.Success) cfg.PppoeUsername = userM.Groups[1].Value;
        if (passM.Success) cfg.PppoePassword = passM.Groups[1].Value;
        if (mtuM.Success && int.TryParse(mtuM.Groups[1].Value, out var m)) cfg.Mtu = m;

        return cfg;
    }

    public async Task<(bool Success, string Message)> ApplyWanConfigAsync(WanConfig config)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var proto = ShellHelper.SanitizeIdentifier(config.Proto, "dhcp");
        var sb = new StringBuilder();
        sb.AppendLine($"uci set network.wan.proto={ShellHelper.EscapeArg(proto)}");

        if (proto.Equals("pppoe", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"uci set network.wan.username={ShellHelper.EscapeArg(config.PppoeUsername)}");
            sb.AppendLine($"uci set network.wan.password={ShellHelper.EscapeArg(config.PppoePassword)}");
            sb.AppendLine($"uci set network.wan.mtu={ShellHelper.EscapeArg(config.Mtu > 0 ? config.Mtu.ToString() : "1492")}");
        }
        else if (proto.Equals("static", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"uci set network.wan.ipaddr={ShellHelper.EscapeArg(config.StaticIp)}");
            sb.AppendLine($"uci set network.wan.netmask={ShellHelper.EscapeArg(config.StaticNetmask)}");
            sb.AppendLine($"uci set network.wan.gateway={ShellHelper.EscapeArg(config.StaticGateway)}");
            sb.AppendLine($"uci set network.wan.dns={ShellHelper.EscapeArg(config.StaticDns)}");
        }

        sb.AppendLine("uci commit network");
        sb.AppendLine("ifdown wan; sleep 1; ifup wan");

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(sb.ToString(), 15);
        if (code == 0) return (true, "Параметры WAN успешно обновлены и интерфейс переподключен!");
        return (false, $"Ошибка настройки WAN: {err}\n{outStr}");
    }

    public async Task<List<PortForwardRule>> GetPortForwardRulesAsync()
    {
        var list = new List<PortForwardRule>();
        if (!_ssh.IsConnected) return list;

        var (_, outStr, _) = await _ssh.ExecuteCommandAsync("uci show firewall", 5);
        var matches = Regex.Matches(outStr, @"firewall\.([^\s\.]+)=redirect");
        foreach (Match m in matches)
        {
            var sec = m.Groups[1].Value;
            var escSec = Regex.Escape(sec);
            var nameM = Regex.Match(outStr, @$"firewall\.{escSec}\.name='?([^'\r\n]+)'?");
            var srcPortM = Regex.Match(outStr, @$"firewall\.{escSec}\.src_dport='?([^'\r\n]+)'?");
            var destIpM = Regex.Match(outStr, @$"firewall\.{escSec}\.dest_ip='?([^'\r\n]+)'?");
            var destPortM = Regex.Match(outStr, @$"firewall\.{escSec}\.dest_port='?([^'\r\n]+)'?");
            var protoM = Regex.Match(outStr, @$"firewall\.{escSec}\.proto='?([^'\r\n]+)'?");
            var enM = Regex.Match(outStr, @$"firewall\.{escSec}\.enabled='?([01])'?");

            list.Add(new PortForwardRule
            {
                SectionId = sec,
                Name = nameM.Success ? nameM.Groups[1].Value : "Forward",
                SrcPort = srcPortM.Success ? srcPortM.Groups[1].Value : "80",
                DestIp = destIpM.Success ? destIpM.Groups[1].Value : "192.168.1.100",
                DestPort = destPortM.Success ? destPortM.Groups[1].Value : "80",
                Proto = protoM.Success ? protoM.Groups[1].Value : "tcp udp",
                Enabled = !enM.Success || enM.Groups[1].Value == "1"
            });
        }

        return list;
    }

    public async Task<(bool Success, string Message)> SavePortForwardRuleAsync(PortForwardRule rule)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var sb = new StringBuilder();
        var rawSec = string.IsNullOrWhiteSpace(rule.SectionId) ? "" : rule.SectionId;
        var sec = string.IsNullOrWhiteSpace(rawSec) ? "" : ShellHelper.SanitizeIdentifier(rawSec);

        if (string.IsNullOrWhiteSpace(sec))
        {
            sb.AppendLine("uci add firewall redirect");
            sb.AppendLine("SEC=@redirect[-1]");
        }
        else
        {
            sb.AppendLine($"SEC={sec}");
        }

        sb.AppendLine($"uci set firewall.$SEC.name={ShellHelper.EscapeArg(rule.Name)}");
        sb.AppendLine("uci set firewall.$SEC.src='wan'");
        sb.AppendLine("uci set firewall.$SEC.dest='lan'");
        sb.AppendLine($"uci set firewall.$SEC.proto={ShellHelper.EscapeArg(rule.Proto)}");
        sb.AppendLine($"uci set firewall.$SEC.src_dport={ShellHelper.EscapeArg(rule.SrcPort)}");
        sb.AppendLine($"uci set firewall.$SEC.dest_ip={ShellHelper.EscapeArg(rule.DestIp)}");
        sb.AppendLine($"uci set firewall.$SEC.dest_port={ShellHelper.EscapeArg(rule.DestPort)}");
        sb.AppendLine("uci set firewall.$SEC.target='DNAT'");
        sb.AppendLine($"uci set firewall.$SEC.enabled='{(rule.Enabled ? 1 : 0)}'");
        sb.AppendLine("uci commit firewall");
        sb.AppendLine("/etc/init.d/firewall restart");

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(sb.ToString(), 12);
        if (code == 0) return (true, $"Правило проброса '{rule.Name}' сохранено и брандмауэр перезапущен!");
        return (false, $"Ошибка проброса портов: {err}\n{outStr}");
    }

    public async Task<(bool Success, string Message)> DeletePortForwardRuleAsync(string sectionId)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var cleanSec = ShellHelper.SanitizeIdentifier(sectionId);
        var cmd = $"uci -q delete firewall.{cleanSec}; uci commit firewall; /etc/init.d/firewall restart";
        var (code, _, err) = await _ssh.ExecuteCommandAsync(cmd, 10);
        if (code == 0) return (true, "Правило успешно удалено!");
        return (false, $"Ошибка удаления: {err}");
    }

    public async Task<SystemAdminConfig> GetSystemAdminConfigAsync()
    {
        var cfg = new SystemAdminConfig();
        if (!_ssh.IsConnected) return cfg;

        var (_, sysOut, _) = await _ssh.ExecuteCommandAsync("uci show system", 5);
        var hostM = Regex.Match(sysOut, @"system\.@system\[0\]\.hostname='?([^'\r\n]+)'?");
        var tzM = Regex.Match(sysOut, @"system\.@system\[0\]\.timezone='?([^'\r\n]+)'?");
        if (hostM.Success) cfg.Hostname = hostM.Groups[1].Value;
        if (tzM.Success) cfg.Timezone = tzM.Groups[1].Value;

        return cfg;
    }

    public async Task<(bool Success, string Message)> ApplySystemAdminConfigAsync(SystemAdminConfig config)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var sb = new StringBuilder();
        sb.AppendLine($"uci set system.@system[0].hostname={ShellHelper.EscapeArg(config.Hostname)}");
        sb.AppendLine($"uci set system.@system[0].timezone={ShellHelper.EscapeArg(config.Timezone)}");
        sb.AppendLine("uci commit system");
        sb.AppendLine("/etc/init.d/system restart");

        if (!string.IsNullOrWhiteSpace(config.NewPassword))
        {
            var escPass = ShellHelper.EscapeArg(config.NewPassword);
            sb.AppendLine($"printf '%s\\n%s\\n' {escPass} {escPass} | passwd root 2>/dev/null");
        }

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(sb.ToString(), 10);
        if (code == 0) return (true, "Системные параметры успешно обновлены!");
        return (false, $"Ошибка сохранения системы: {err}\n{outStr}");
    }
}
