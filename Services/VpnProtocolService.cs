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
    AmneziaWgConfig ParseAmneziaWgConf(string confContent);
    Task<(bool Success, string Message)> ApplyAmneziaWgConfigAsync(AmneziaWgConfig config);
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

        // Peer configuration
        sb.AppendLine($"uci set network.{peerName}=wireguard_{iface}");
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
