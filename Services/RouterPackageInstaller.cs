using System;
using System.IO;
using System.Linq;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public static class RouterPackageInstaller
{
    public static string[][] Groups(VpnProtocolType type) => type switch
    {
        VpnProtocolType.Forkop => new[] { new[] { "forkop" } },
        VpnProtocolType.AmneziaWg => new[] { new[] { "kmod-amneziawg" }, new[] { "amneziawg-tools" } },
        VpnProtocolType.WireGuard => new[] { new[] { "kmod-wireguard" }, new[] { "wireguard-tools" } },
        VpnProtocolType.SingBox => new[] { new[] { "sing-box" } },
        VpnProtocolType.Mihomo => new[] { new[] { "mihomo", "openwrt-mihomo" } },
        VpnProtocolType.PassWall => new[] { new[] { "luci-app-passwall2", "luci-app-passwall" } },
        VpnProtocolType.OpenVpn => new[] { new[] { "openvpn-openssl", "openvpn-mbedtls" } },
        VpnProtocolType.Tailscale => new[] { new[] { "tailscale" } },
        VpnProtocolType.ZeroTier => new[] { new[] { "zerotier" } },
        VpnProtocolType.Xray => new[] { new[] { "xray-core", "xray" } },
        _ => Array.Empty<string[]>()
    };

    public static string BuildCommand(VpnProtocolType type)
    {
        var groups = Groups(type);
        if (groups.Length == 0) return "";
        using var stream = typeof(RouterPackageInstaller).Assembly.GetManifestResourceStream("OpenWrtStudio.Router.install_packages.sh")
            ?? throw new InvalidOperationException("Package installer resource is missing");
        using var reader = new StreamReader(stream);
        return "/bin/sh -c " + ShellHelper.EscapeArg(reader.ReadToEnd()) + " -- "
            + string.Join(" ", groups.Select(g => ShellHelper.EscapeArg(string.Join(" ", g))));
    }
}
