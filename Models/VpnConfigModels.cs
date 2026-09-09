using System.Collections.Generic;

namespace OpenWrtStudio.Models;

public class AmneziaWgConfig
{
    public string InterfaceName { get; set; } = "awg0";
    public string PrivateKey { get; set; } = "";
    public string Address { get; set; } = "10.8.0.2/24";
    public int ListenPort { get; set; } = 51820;
    public string Dns { get; set; } = "1.1.1.1, 8.8.8.8";
    public int Mtu { get; set; } = 1420;

    // AmneziaWG Specific Obfuscation Parameters
    public int Jc { get; set; } = 4;        // Junk packet count
    public int Jmin { get; set; } = 40;     // Junk packet min size
    public int Jmax { get; set; } = 70;     // Junk packet max size
    public int S1 { get; set; } = 15;       // Init packet junk size
    public int S2 { get; set; } = 25;       // Response packet junk size
    public uint H1 { get; set; } = 1;       // Init packet header
    public uint H2 { get; set; } = 2;       // Response packet header
    public uint H3 { get; set; } = 3;       // Underload packet header
    public uint H4 { get; set; } = 4;       // Transport packet header

    // Peer
    public string PeerPublicKey { get; set; } = "";
    public string PeerPresharedKey { get; set; } = "";
    public string Endpoint { get; set; } = ""; // host:port
    public string AllowedIPs { get; set; } = "0.0.0.0/0, ::/0";
    public int PersistentKeepalive { get; set; } = 25;
}

public class SingboxQuickConfig
{
    public string InboundType { get; set; } = "tun"; // tun, mixed
    public int MixedPort { get; set; } = 2080;
    public string ServerAddress { get; set; } = "";
    public int ServerPort { get; set; } = 443;
    public string Protocol { get; set; } = "vless"; // vless, hysteria2, shadowsocks, trojan
    public string Uuid { get; set; } = "";
    public string Password { get; set; } = "";
    public string Flow { get; set; } = "xtls-rprx-vision";
    public string ServerName { get; set; } = "www.microsoft.com";
    public string PublicKey { get; set; } = "";
    public string ShortId { get; set; } = "";
    public bool EnableReality { get; set; } = true;
    public bool RouteDirectRu { get; set; } = true;
}

public class OpenVpnClientConfig
{
    public string ProfileName { get; set; } = "vpn_client";
    public string ServerHost { get; set; } = "";
    public int Port { get; set; } = 1194;
    public string Protocol { get; set; } = "udp"; // udp, tcp
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string RawOvpnContent { get; set; } = "";
    public bool AuthUserPass { get; set; } = false;
}

public class MeshVpnConfig
{
    public string TailscaleAuthKey { get; set; } = "";
    public bool TailscaleAcceptRoutes { get; set; } = true;
    public bool TailscaleExitNode { get; set; } = false;
    public string ZeroTierNetworkId { get; set; } = "";
    public bool ZeroTierAutoConnect { get; set; } = true;
}
