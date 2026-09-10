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
cat /tmp/sysinfo/model 2>/dev/null || cat /proc/cpuinfo 2>/dev/null | grep -m1 'model name' | cut -d: -f2
echo '===OS_INFO==='
cat /etc/openwrt_release 2>/dev/null
echo '===KERNEL==='
uname -r 2>/dev/null
echo '===ARCH==='
opkg print-architecture 2>/dev/null | tail -n 1 | awk '{print $2}' || uname -m 2>/dev/null
echo '===PACKAGES==='
if command -v opkg >/dev/null 2>&1; then
    opkg list-installed 2>/dev/null
fi
if command -v apk >/dev/null 2>&1; then
    apk list -I 2>/dev/null || apk info -v 2>/dev/null
fi
echo '===BINARIES==='
for b in awg amneziawg-go wg sing-box mihomo clash passwall openvpn tailscale tailscaled zerotier zerotier-one xray xray-core; do
    p=$(command -v ""$b"" 2>/dev/null || which ""$b"" 2>/dev/null)
    [ -n ""$p"" ] && echo ""$b:$p""
    [ -f ""/usr/bin/$b"" ] && echo ""$b:/usr/bin/$b""
    [ -f ""/usr/sbin/$b"" ] && echo ""$b:/usr/sbin/$b""
    [ -f ""/usr/local/bin/$b"" ] && echo ""$b:/usr/local/bin/$b""
    [ -f ""/etc/mihomo/$b"" ] && echo ""$b:/etc/mihomo/$b""
done
echo '===INIT_SERVICES==='
ls /etc/init.d/ 2>/dev/null
echo '===KERNEL_MODULES==='
ls -d /sys/module/amneziawg /sys/module/wireguard 2>/dev/null
cat /proc/modules 2>/dev/null | awk '{print $1}'
echo '===CONFIGS==='
ls /etc/config/ 2>/dev/null
echo '===RUNNING==='
ps -w 2>/dev/null || ps 2>/dev/null
echo '===INTERFACES==='
ip link show 2>/dev/null || ifconfig 2>/dev/null
";

        var (code, output, _) = await _ssh.ExecuteCommandAsync(scanScript, 20);
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
        var binSec = ExtractSection(output, "BINARIES");
        var initSec = ExtractSection(output, "INIT_SERVICES");
        var modSec = ExtractSection(output, "KERNEL_MODULES");
        var cfgSec = ExtractSection(output, "CONFIGS");
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
                "opkg update && opkg install kmod-amneziawg amneziawg-tools luci-proto-amneziawg || apk add kmod-amneziawg amneziawg-tools",
                hasObf: true,
                pkgsSec, binSec, initSec, modSec, cfgSec, runningSec, ifaceSec,
                pkgCandidates: new[] { "kmod-amneziawg", "amneziawg-tools", "luci-proto-amneziawg", "luci-app-amneziawg", "amneziawg" },
                binCandidates: new[] { "awg", "amneziawg-go" },
                modCandidates: new[] { "amneziawg" },
                svcCandidates: new[] { "amneziawg" },
                cfgCandidates: new[] { "amneziawg" },
                runningCandidates: new[] { "awg", "amneziawg-go" },
                ifaceCandidates: new[] { "awg" }),

            CreateCapability(
                VpnProtocolType.WireGuard,
                "WireGuard (Стандартный)",
                "Быстрый современный протокол VPN для шифрования трафика",
                "kmod-wireguard",
                "/etc/config/network",
                "LockClosed24",
                "opkg update && opkg install kmod-wireguard wireguard-tools luci-proto-wireguard || apk add kmod-wireguard wireguard-tools",
                hasObf: false,
                pkgsSec, binSec, initSec, modSec, cfgSec, runningSec, ifaceSec,
                pkgCandidates: new[] { "kmod-wireguard", "wireguard-tools", "luci-proto-wireguard", "wireguard" },
                binCandidates: new[] { "wg" },
                modCandidates: new[] { "wireguard" },
                svcCandidates: new[] { "wireguard" },
                cfgCandidates: new[] { "wireguard" },
                runningCandidates: Array.Empty<string>(),
                ifaceCandidates: new[] { "wg" }),

            CreateCapability(
                VpnProtocolType.SingBox,
                "Sing-box",
                "Универсальный швейцарский нож: VLESS Reality, Hysteria 2, TUIC, ShadowTLS",
                "sing-box",
                "/etc/sing-box/config.json",
                "Box24",
                "opkg update && opkg install sing-box || apk add sing-box",
                hasObf: true,
                pkgsSec, binSec, initSec, modSec, cfgSec, runningSec, ifaceSec,
                pkgCandidates: new[] { "sing-box", "luci-app-sing-box", "sing-box-core" },
                binCandidates: new[] { "sing-box" },
                modCandidates: Array.Empty<string>(),
                svcCandidates: new[] { "sing-box" },
                cfgCandidates: new[] { "sing-box" },
                runningCandidates: new[] { "sing-box" }),

            CreateCapability(
                VpnProtocolType.Mihomo,
                "Mihomo (Clash.Meta)",
                "Маршрутизатор правил трафика с веб-панелями (MetaCubeX/YACD) и DNS Fake-IP",
                "mihomo",
                "/etc/mihomo/config.yaml",
                "ArrowRouting24",
                "opkg update && opkg install mihomo || opkg install openwrt-mihomo || apk add mihomo",
                hasObf: true,
                pkgsSec, binSec, initSec, modSec, cfgSec, runningSec, ifaceSec,
                pkgCandidates: new[] { "mihomo", "openwrt-mihomo", "luci-app-mihomo", "nikki", "luci-app-nikki", "openclash", "luci-app-openclash", "clash" },
                binCandidates: new[] { "mihomo", "clash" },
                modCandidates: Array.Empty<string>(),
                svcCandidates: new[] { "mihomo", "openwrt-mihomo", "nikki", "openclash" },
                cfgCandidates: new[] { "mihomo", "nikki", "openclash" },
                runningCandidates: new[] { "mihomo", "clash" }),

            CreateCapability(
                VpnProtocolType.PassWall,
                "PassWall 2",
                "Мощный комбайн обхода цензуры с разделением трафика через SmartDNS и ChinaDNS",
                "luci-app-passwall",
                "/etc/config/passwall",
                "Flash24",
                "opkg update && opkg install luci-app-passwall passwall || apk add passwall",
                hasObf: true,
                pkgsSec, binSec, initSec, modSec, cfgSec, runningSec, ifaceSec,
                pkgCandidates: new[] { "luci-app-passwall", "luci-app-passwall2", "passwall", "passwall2" },
                binCandidates: new[] { "passwall" },
                modCandidates: Array.Empty<string>(),
                svcCandidates: new[] { "passwall", "passwall2" },
                cfgCandidates: new[] { "passwall", "passwall2" },
                runningCandidates: new[] { "passwall" }),

            CreateCapability(
                VpnProtocolType.OpenVpn,
                "OpenVPN",
                "Классический протокол с поддержкой SSL/TLS и пользовательских сертификатов",
                "openvpn-openssl",
                "/etc/config/openvpn",
                "Key24",
                "opkg update && opkg install openvpn-openssl luci-app-openvpn || apk add openvpn",
                hasObf: false,
                pkgsSec, binSec, initSec, modSec, cfgSec, runningSec, ifaceSec,
                pkgCandidates: new[] { "openvpn-openssl", "openvpn-mbedtls", "openvpn-nossl", "openvpn", "luci-app-openvpn" },
                binCandidates: new[] { "openvpn" },
                modCandidates: Array.Empty<string>(),
                svcCandidates: new[] { "openvpn" },
                cfgCandidates: new[] { "openvpn" },
                runningCandidates: new[] { "openvpn" },
                ifaceCandidates: new[] { "tun", "tap" }),

            CreateCapability(
                VpnProtocolType.Tailscale,
                "Tailscale",
                "Zero-config WireGuard Mesh VPN для объединения устройств без внешнего IP",
                "tailscale",
                "/var/lib/tailscale",
                "Globe24",
                "opkg update && opkg install tailscale || apk add tailscale",
                hasObf: false,
                pkgsSec, binSec, initSec, modSec, cfgSec, runningSec, ifaceSec,
                pkgCandidates: new[] { "tailscale", "tailscaled", "luci-app-tailscale" },
                binCandidates: new[] { "tailscale", "tailscaled" },
                modCandidates: Array.Empty<string>(),
                svcCandidates: new[] { "tailscale", "tailscaled" },
                cfgCandidates: new[] { "tailscale" },
                runningCandidates: new[] { "tailscaled", "tailscale" },
                ifaceCandidates: new[] { "tailscale" }),

            CreateCapability(
                VpnProtocolType.ZeroTier,
                "ZeroTier",
                "Виртуальный Ethernet-коммутатор (L2 SDN) для объединения домашних сетей",
                "zerotier",
                "/etc/config/zerotier",
                "NetworkCheck24",
                "opkg update && opkg install zerotier || apk add zerotier",
                hasObf: false,
                pkgsSec, binSec, initSec, modSec, cfgSec, runningSec, ifaceSec,
                pkgCandidates: new[] { "zerotier", "zerotier-one", "luci-app-zerotier" },
                binCandidates: new[] { "zerotier-one", "zerotier" },
                modCandidates: Array.Empty<string>(),
                svcCandidates: new[] { "zerotier" },
                cfgCandidates: new[] { "zerotier" },
                runningCandidates: new[] { "zerotier-one", "zerotier" },
                ifaceCandidates: new[] { "zt" }),

            CreateCapability(
                VpnProtocolType.Xray,
                "Xray Core",
                "Ядро поддержки протоколов VLESS Reality, VMess и XTLS",
                "xray-core",
                "/etc/xray/config.json",
                "ShieldCheckmark24",
                "opkg update && opkg install xray-core || apk add xray-core",
                hasObf: true,
                pkgsSec, binSec, initSec, modSec, cfgSec, runningSec, ifaceSec,
                pkgCandidates: new[] { "xray-core", "xray", "luci-app-xray" },
                binCandidates: new[] { "xray", "xray-core" },
                modCandidates: Array.Empty<string>(),
                svcCandidates: new[] { "xray" },
                cfgCandidates: new[] { "xray" },
                runningCandidates: new[] { "xray" })
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
        string pkgsSec,
        string binSec,
        string initSec,
        string modSec,
        string cfgSec,
        string runningSec,
        string ifaceSec,
        string[] pkgCandidates,
        string[] binCandidates,
        string[] modCandidates,
        string[] svcCandidates,
        string[]? cfgCandidates = null,
        string[]? runningCandidates = null,
        string[]? ifaceCandidates = null)
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

        // 1. Check Package lists (opkg and apk formats)
        foreach (var p in pkgCandidates)
        {
            // opkg: "pkg - 1.2.3"
            var opkgMatch = Regex.Match(pkgsSec, @$"(?:^|\n){Regex.Escape(p)}\s+-\s+([^\s\r\n]+)");
            if (opkgMatch.Success)
            {
                cap.IsInstalled = true;
                cap.Version = opkgMatch.Groups[1].Value;
                break;
            }

            // apk: "pkg-1.2.3-r4" or "pkg 1.2.3"
            var apkMatch = Regex.Match(pkgsSec, @$"(?:^|\n){Regex.Escape(p)}[ -]([0-9][^\s\r\n]*)");
            if (apkMatch.Success)
            {
                cap.IsInstalled = true;
                cap.Version = apkMatch.Groups[1].Value;
                break;
            }

            // Simple presence in installed packages list
            if (Regex.IsMatch(pkgsSec, @$"(?:^|\n){Regex.Escape(p)}(?:\s|$)", RegexOptions.IgnoreCase))
            {
                cap.IsInstalled = true;
                break;
            }
        }

        // 2. Check Binaries
        if (!cap.IsInstalled)
        {
            foreach (var b in binCandidates)
            {
                if (Regex.IsMatch(binSec, @$"(?:^|\n){Regex.Escape(b)}:", RegexOptions.IgnoreCase))
                {
                    cap.IsInstalled = true;
                    break;
                }
            }
        }

        // 3. Check Kernel Modules
        if (!cap.IsInstalled)
        {
            foreach (var m in modCandidates)
            {
                if (Regex.IsMatch(modSec, @$"\b{Regex.Escape(m)}\b", RegexOptions.IgnoreCase))
                {
                    cap.IsInstalled = true;
                    break;
                }
            }
        }

        // 4. Check Init.d services
        if (!cap.IsInstalled)
        {
            foreach (var s in svcCandidates)
            {
                if (Regex.IsMatch(initSec, @$"\b{Regex.Escape(s)}\b", RegexOptions.IgnoreCase))
                {
                    cap.IsInstalled = true;
                    break;
                }
            }
        }

        // 5. Check Configs
        if (!cap.IsInstalled && cfgCandidates != null)
        {
            foreach (var c in cfgCandidates)
            {
                if (Regex.IsMatch(cfgSec, @$"\b{Regex.Escape(c)}\b", RegexOptions.IgnoreCase))
                {
                    cap.IsInstalled = true;
                    break;
                }
            }
        }

        // 6. Check Running processes
        if (runningCandidates != null)
        {
            foreach (var r in runningCandidates)
            {
                if (Regex.IsMatch(runningSec, @$"\b{Regex.Escape(r)}\b", RegexOptions.IgnoreCase))
                {
                    cap.IsRunning = true;
                    cap.IsInstalled = true; // Running process implies installed!
                    break;
                }
            }
        }

        // 7. Check Network interfaces
        if (ifaceCandidates != null)
        {
            foreach (var iface in ifaceCandidates)
            {
                if (Regex.IsMatch(ifaceSec, @$"\b{Regex.Escape(iface)}[0-9]*\b", RegexOptions.IgnoreCase))
                {
                    cap.IsRunning = true;
                    cap.IsInstalled = true; // Active interface implies installed!
                    break;
                }
            }
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
