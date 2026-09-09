using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface IRouterCapabilityService
{
    Task<RouterCapabilityReport> ScanCapabilitiesAsync();
}

public class RouterCapabilityService : IRouterCapabilityService
{
    private readonly ISshService _ssh;

    public RouterCapabilityService(ISshService ssh)
    {
        _ssh = ssh;
    }

    public async Task<RouterCapabilityReport> ScanCapabilitiesAsync()
    {
        var report = new RouterCapabilityReport();
        if (!_ssh.IsConnected) return report;

        const string scanScript = @"
echo '===MODEL==='
cat /tmp/sysinfo/model 2>/dev/null || cat /proc/cpuinfo | grep -m1 'model name' | cut -d: -f2
echo '===OS_INFO==='
cat /etc/openwrt_release 2>/dev/null
echo '===KERNEL==='
uname -r
echo '===ARCH==='
opkg print-architecture 2>/dev/null | tail -n 1 | awk '{print $2}' || uname -m
echo '===PACKAGES==='
opkg list-installed
echo '===RUNNING==='
ps
echo '===INTERFACES==='
ip link show
";

        var (code, output, _) = await _ssh.ExecuteCommandAsync(scanScript, 12);
        if (code != 0 && string.IsNullOrWhiteSpace(output)) return report;

        // Parse Model
        var modelSec = ExtractSection(output, "MODEL");
        report.RouterModel = string.IsNullOrWhiteSpace(modelSec) ? "OpenWrt Router" : modelSec.Trim();

        // Parse Kernel & Arch
        report.KernelVersion = ExtractSection(output, "KERNEL").Trim();
        report.Architecture = ExtractSection(output, "ARCH").Trim();

        // Parse Release
        var osSec = ExtractSection(output, "OS_INFO");
        var distMatch = Regex.Match(osSec, @"DISTRIB_DESCRIPTION='([^']+)'");
        report.OpenWrtVersion = distMatch.Success ? distMatch.Groups[1].Value : "OpenWrt";

        var pkgsSec = ExtractSection(output, "PACKAGES");
        var runningSec = ExtractSection(output, "RUNNING");
        var ifaceSec = ExtractSection(output, "INTERFACES");

        report.Protocols = new List<VpnProtocolCapability>
        {
            CreateCapability(
                VpnProtocolType.AmneziaWg,
                "AmneziaWG (AWG)",
                "Обфусцированный WireGuard с защитой от глубокого анализа пакетов (DPI)",
                "kmod-amneziawg",
                "/etc/config/network",
                "ShieldKeyhole24",
                "opkg update && opkg install kmod-amneziawg amneziawg-tools luci-proto-amneziawg",
                hasObf: true,
                pkgsSec, runningSec, ifaceSec, checkIface: "awg"),

            CreateCapability(
                VpnProtocolType.WireGuard,
                "WireGuard (Стандартный)",
                "Быстрый современный протокол VPN для шифрования трафика",
                "kmod-wireguard",
                "/etc/config/network",
                "LockClosed24",
                "opkg update && opkg install kmod-wireguard wireguard-tools luci-proto-wireguard",
                hasObf: false,
                pkgsSec, runningSec, ifaceSec, checkIface: "wg"),

            CreateCapability(
                VpnProtocolType.SingBox,
                "Sing-box",
                "Универсальный швейцарский нож: VLESS Reality, Hysteria 2, TUIC, ShadowTLS",
                "sing-box",
                "/etc/sing-box/config.json",
                "Box24",
                "opkg update && opkg install sing-box",
                hasObf: true,
                pkgsSec, runningSec, ifaceSec, checkProcess: "sing-box"),

            CreateCapability(
                VpnProtocolType.Mihomo,
                "Mihomo (Clash.Meta)",
                "Маршрутизатор правил трафика с веб-панелями (MetaCubeX/YACD) и DNS Fake-IP",
                "mihomo",
                "/etc/mihomo/config.yaml",
                "ArrowRouting24",
                "opkg update && opkg install mihomo || opkg install openwrt-mihomo",
                hasObf: true,
                pkgsSec, runningSec, ifaceSec, checkProcess: "mihomo"),

            CreateCapability(
                VpnProtocolType.PassWall,
                "PassWall 2",
                "Мощный комбайн обхода цензуры с разделением трафика через SmartDNS и ChinaDNS",
                "luci-app-passwall",
                "/etc/config/passwall",
                "Flash24",
                "opkg update && opkg install luci-app-passwall passwall",
                hasObf: true,
                pkgsSec, runningSec, ifaceSec, checkProcess: "passwall"),

            CreateCapability(
                VpnProtocolType.OpenVpn,
                "OpenVPN",
                "Классический протокол с поддержкой SSL/TLS и пользовательских сертификатов",
                "openvpn-openssl",
                "/etc/config/openvpn",
                "Key24",
                "opkg update && opkg install openvpn-openssl luci-app-openvpn",
                hasObf: false,
                pkgsSec, runningSec, ifaceSec, checkProcess: "openvpn"),

            CreateCapability(
                VpnProtocolType.Tailscale,
                "Tailscale",
                "Zero-config WireGuard Mesh VPN для объединения устройств без внешнего IP",
                "tailscale",
                "/var/lib/tailscale",
                "Globe24",
                "opkg update && opkg install tailscale",
                hasObf: false,
                pkgsSec, runningSec, ifaceSec, checkProcess: "tailscaled"),

            CreateCapability(
                VpnProtocolType.ZeroTier,
                "ZeroTier",
                "Виртуальный Ethernet-коммутатор (L2 SDN) для объединения домашних сетей",
                "zerotier",
                "/etc/config/zerotier",
                "NetworkCheck24",
                "opkg update && opkg install zerotier",
                hasObf: false,
                pkgsSec, runningSec, ifaceSec, checkProcess: "zerotier-one"),

            CreateCapability(
                VpnProtocolType.Xray,
                "Xray Core",
                "Ядро поддержки протоколов VLESS Reality, VMess и XTLS",
                "xray-core",
                "/etc/xray/config.json",
                "ShieldCheckmark24",
                "opkg update && opkg install xray-core",
                hasObf: true,
                pkgsSec, runningSec, ifaceSec, checkProcess: "xray")
        };

        return report;
    }

    private static VpnProtocolCapability CreateCapability(
        VpnProtocolType type,
        string name,
        string desc,
        string pkg,
        string cfgPath,
        string icon,
        string installCmd,
        bool hasObf,
        string installedPkgs,
        string runningProcesses,
        string interfaces,
        string? checkProcess = null,
        string? checkIface = null)
    {
        var cap = new VpnProtocolCapability
        {
            Type = type,
            DisplayName = name,
            Description = desc,
            MainPackage = pkg,
            ConfigPath = cfgPath,
            Icon = icon,
            InstallCommand = installCmd,
            HasObfuscation = hasObf
        };

        var match = Regex.Match(installedPkgs, @$"(?:^|\n){Regex.Escape(pkg)}\s+-\s+([^\s\r\n]+)");
        if (match.Success)
        {
            cap.IsInstalled = true;
            cap.Version = match.Groups[1].Value;
        }

        if (checkProcess != null && runningProcesses.Contains(checkProcess))
        {
            cap.IsRunning = true;
        }
        else if (checkIface != null && interfaces.Contains(checkIface))
        {
            cap.IsRunning = true;
        }

        return cap;
    }

    private static string ExtractSection(string text, string tag)
    {
        var startTag = $"==={tag}===";
        int idx = text.IndexOf(startTag, StringComparison.Ordinal);
        if (idx < 0) return "";
        int start = idx + startTag.Length;
        int next = text.IndexOf("===", start, StringComparison.Ordinal);
        return next > 0 ? text.Substring(start, next - start).Trim() : text.Substring(start).Trim();
    }
}
