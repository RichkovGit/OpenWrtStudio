using System.Collections.Generic;

namespace OpenWrtStudio.Models;

public enum VpnProtocolType
{
    AmneziaWg,
    WireGuard,
    SingBox,
    Mihomo,
    OpenVpn,
    PassWall,
    Tailscale,
    ZeroTier,
    Shadowsocks,
    Xray
}

public class VpnProtocolCapability
{
    public VpnProtocolType Type { get; set; }
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string MainPackage { get; set; } = "";
    public bool IsInstalled { get; set; }
    public bool IsRunning { get; set; }
    public string Version { get; set; } = "";
    public string ConfigPath { get; set; } = "";
    public string Icon { get; set; } = "ShieldKeyhole24";
    public string InstallCommand { get; set; } = "";
    public bool HasObfuscation { get; set; }
}

public class RouterCapabilityReport
{
    public string RouterModel { get; set; } = "";
    public string OpenWrtVersion { get; set; } = "";
    public string KernelVersion { get; set; } = "";
    public string Architecture { get; set; } = "";
    public List<VpnProtocolCapability> Protocols { get; set; } = new();
}
