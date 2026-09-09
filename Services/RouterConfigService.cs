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
            var radio = new WifiRadioConfig { Device = rName };

            var bandMatch = Regex.Match(outStr, @$"wireless\.{rName}\.band='?([^'\r\n]+)'?");
            var chanMatch = Regex.Match(outStr, @$"wireless\.{rName}\.channel='?([^'\r\n]+)'?");
            var htMatch = Regex.Match(outStr, @$"wireless\.{rName}\.htmode='?([^'\r\n]+)'?");
            var disMatch = Regex.Match(outStr, @$"wireless\.{rName}\.disabled='?([01])'?");

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
            var devMatch = Regex.Match(outStr, @$"wireless\.{ifName}\.device='?([^'\r\n]+)'?");
            var ssidMatch = Regex.Match(outStr, @$"wireless\.{ifName}\.ssid='?([^'\r\n]+)'?");
            var encMatch = Regex.Match(outStr, @$"wireless\.{ifName}\.encryption='?([^'\r\n]+)'?");
            var keyMatch = Regex.Match(outStr, @$"wireless\.{ifName}\.key='?([^'\r\n]+)'?");
            var disMatch = Regex.Match(outStr, @$"wireless\.{ifName}\.disabled='?([01])'?");

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
            sb.AppendLine($"uci set wireless.{r.Device}.channel='{r.Channel}'");
            sb.AppendLine($"uci set wireless.{r.Device}.htmode='{r.HtMode}'");
            sb.AppendLine($"uci set wireless.{r.Device}.disabled='{(r.Disabled ? 1 : 0)}'");

            foreach (var iface in r.Interfaces)
            {
                var id = string.IsNullOrWhiteSpace(iface.SectionId) ? $"default_{r.Device}" : iface.SectionId;
                sb.AppendLine($"uci set wireless.{id}.ssid='{iface.Ssid}'");
                sb.AppendLine($"uci set wireless.{id}.encryption='{iface.Encryption}'");
                if (!string.IsNullOrWhiteSpace(iface.Key))
                {
                    sb.AppendLine($"uci set wireless.{id}.key='{iface.Key}'");
                }
                sb.AppendLine($"uci set wireless.{id}.disabled='{(iface.Disabled ? 1 : 0)}'");
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
            var nameM = Regex.Match(dhcpOut, @$"dhcp\.{secId}\.name='?([^'\r\n]+)'?");
            var macM = Regex.Match(dhcpOut, @$"dhcp\.{secId}\.mac='?([^'\r\n]+)'?");
            var ipLM = Regex.Match(dhcpOut, @$"dhcp\.{secId}\.ip='?([^'\r\n]+)'?");

            cfg.StaticLeases.Add(new StaticDhcpLease
            {
                SectionId = secId,
                Name = nameM.Success ? nameM.Groups[1].Value : "Host",
                Mac = macM.Success ? macM.Groups[1].Value : "",
                Ip = ipLM.Success ? ipLM.Groups[1].Value : ""
            });
        }

        return cfg;
    }

    public async Task<(bool Success, string Message)> ApplyLanDhcpConfigAsync(LanDhcpConfig config)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var sb = new StringBuilder();
        sb.AppendLine($"uci set network.lan.ipaddr='{config.IpAddress}'");
        sb.AppendLine($"uci set network.lan.netmask='{config.Netmask}'");
        sb.AppendLine($"uci set dhcp.lan.start='{config.DhcpStart}'");
        sb.AppendLine($"uci set dhcp.lan.limit='{config.DhcpLimit}'");
        sb.AppendLine($"uci set dhcp.lan.leasetime='{config.LeaseTime}'");

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

        var sb = new StringBuilder();
        sb.AppendLine($"uci set network.wan.proto='{config.Proto}'");

        if (config.Proto.Equals("pppoe", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"uci set network.wan.username='{config.PppoeUsername}'");
            sb.AppendLine($"uci set network.wan.password='{config.PppoePassword}'");
            sb.AppendLine($"uci set network.wan.mtu='{config.Mtu}'");
        }
        else if (config.Proto.Equals("static", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"uci set network.wan.ipaddr='{config.StaticIp}'");
            sb.AppendLine($"uci set network.wan.netmask='{config.StaticNetmask}'");
            sb.AppendLine($"uci set network.wan.gateway='{config.StaticGateway}'");
            sb.AppendLine($"uci set network.wan.dns='{config.StaticDns}'");
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
            var nameM = Regex.Match(outStr, @$"firewall\.{sec}\.name='?([^'\r\n]+)'?");
            var srcPortM = Regex.Match(outStr, @$"firewall\.{sec}\.src_dport='?([^'\r\n]+)'?");
            var destIpM = Regex.Match(outStr, @$"firewall\.{sec}\.dest_ip='?([^'\r\n]+)'?");
            var destPortM = Regex.Match(outStr, @$"firewall\.{sec}\.dest_port='?([^'\r\n]+)'?");
            var protoM = Regex.Match(outStr, @$"firewall\.{sec}\.proto='?([^'\r\n]+)'?");
            var enM = Regex.Match(outStr, @$"firewall\.{sec}\.enabled='?([01])'?");

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
        var sec = string.IsNullOrWhiteSpace(rule.SectionId) ? "" : rule.SectionId;

        if (string.IsNullOrWhiteSpace(sec))
        {
            sb.AppendLine("uci add firewall redirect");
            sb.AppendLine("SEC=@redirect[-1]");
        }
        else
        {
            sb.AppendLine($"SEC={sec}");
        }

        sb.AppendLine($"uci set firewall.$SEC.name='{rule.Name}'");
        sb.AppendLine("uci set firewall.$SEC.src='wan'");
        sb.AppendLine("uci set firewall.$SEC.dest='lan'");
        sb.AppendLine($"uci set firewall.$SEC.proto='{rule.Proto}'");
        sb.AppendLine($"uci set firewall.$SEC.src_dport='{rule.SrcPort}'");
        sb.AppendLine($"uci set firewall.$SEC.dest_ip='{rule.DestIp}'");
        sb.AppendLine($"uci set firewall.$SEC.dest_port='{rule.DestPort}'");
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

        var cmd = $"uci -q delete firewall.{sectionId}; uci commit firewall; /etc/init.d/firewall restart";
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
        sb.AppendLine($"uci set system.@system[0].hostname='{config.Hostname}'");
        sb.AppendLine($"uci set system.@system[0].timezone='{config.Timezone}'");
        sb.AppendLine("uci commit system");
        sb.AppendLine("/etc/init.d/system restart");

        if (!string.IsNullOrWhiteSpace(config.NewPassword))
        {
            sb.AppendLine($"echo -e \"{config.NewPassword}\\n{config.NewPassword}\" | passwd root 2>/dev/null");
        }

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(sb.ToString(), 10);
        if (code == 0) return (true, "Системные параметры успешно обновлены!");
        return (false, $"Ошибка сохранения системы: {err}\n{outStr}");
    }
}
