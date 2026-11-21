using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenWrtStudio.Models;

public partial class AmneziaWgConfig : ObservableObject
{
    [ObservableProperty] private string _interfaceName = "awg0";
    [ObservableProperty] private string _privateKey = "";
    [ObservableProperty] private string _address = "10.8.0.2/24";
    [ObservableProperty] private int _listenPort = 51820;
    [ObservableProperty] private string _dns = "1.1.1.1, 8.8.8.8";
    [ObservableProperty] private int _mtu = 1420;

    // AmneziaWG Specific Obfuscation Parameters (0 = disabled / standard WireGuard)
    [ObservableProperty] private int _jc = 0;        // Junk packet count
    [ObservableProperty] private int _jmin = 0;     // Junk packet min size
    [ObservableProperty] private int _jmax = 0;     // Junk packet max size
    [ObservableProperty] private int _s1 = 0;       // Init packet junk size
    [ObservableProperty] private int _s2 = 0;       // Response packet junk size
    [ObservableProperty] private uint _h1 = 0;       // Init packet header
    [ObservableProperty] private uint _h2 = 0;       // Response packet header
    [ObservableProperty] private uint _h3 = 0;       // Underload packet header
    [ObservableProperty] private uint _h4 = 0;       // Transport packet header

    // Peer
    [ObservableProperty] private string _peerPublicKey = "";
    [ObservableProperty] private string _peerPresharedKey = "";
    [ObservableProperty] private string _endpoint = ""; // host:port
    [ObservableProperty] private string _allowedIPs = "0.0.0.0/0, ::/0";
    [ObservableProperty] private int _persistentKeepalive = 25;

    // Status & Runtime
    [ObservableProperty] private bool _isActive = false;
    [ObservableProperty] private string _statusText = "Не активен";
    [ObservableProperty] private string _transferRx = "0 B";
    [ObservableProperty] private string _transferTx = "0 B";
    [ObservableProperty] private string _latestHandshake = "Нет";
}

public class SingboxQuickConfig
{
    public string InboundType { get; set; } = "tun"; // tun, mixed
    public int MixedPort { get; set; } = 2080;
    public string ServerAddress { get; set; } = "";
    public int ServerPort { get; set; } = 443;
    public string Protocol { get; set; } = "vless"; // vless, hysteria2, shadowsocks, trojan, tuic
    public string Uuid { get; set; } = "";
    public string Password { get; set; } = "";
    public string Method { get; set; } = "aes-256-gcm"; // shadowsocks cipher
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

/// <summary>Standalone Shadowsocks / SS-2022 client (shadowsocks-rust sslocal).</summary>
public class ShadowsocksConfig
{
    public string ServerAddress { get; set; } = "";
    public int ServerPort { get; set; } = 8388;
    public string Method { get; set; } = "2022-blake3-aes-128-gcm";
    public string Password { get; set; } = "";
    public string LocalAddress { get; set; } = "0.0.0.0";
    public int SocksPort { get; set; } = 1080;
    public int HttpPort { get; set; } = 3128;
    public bool EnableHttp { get; set; } = true;
    public string Mode { get; set; } = "tcp_and_udp"; // tcp_only, tcp_and_udp, udp_only
}

/// <summary>Standalone Hysteria2 client (apernet/hysteria v2, YAML config).</summary>
public class Hysteria2Config
{
    public string ServerAddress { get; set; } = "";
    public int ServerPort { get; set; } = 443;
    public string Password { get; set; } = "";
    public string Sni { get; set; } = "";
    public bool Insecure { get; set; } = false;
    public int UpMbps { get; set; } = 50;
    public int DownMbps { get; set; } = 200;
    public string LocalAddress { get; set; } = "0.0.0.0";
    public int SocksPort { get; set; } = 1080;
    public int HttpPort { get; set; } = 8080;
    public bool EnableHttp { get; set; } = true;
}

/// <summary>Standalone TUIC v5 client (EAimTY tuic-client 1.x, JSON config).</summary>
public class TuicConfig
{
    public string ServerAddress { get; set; } = "";
    public int ServerPort { get; set; } = 443;
    public string Uuid { get; set; } = "";
    public string Password { get; set; } = "";
    public string IpOverride { get; set; } = "";
    public bool DisableSni { get; set; } = false;
    public string CongestionControl { get; set; } = "bbr"; // bbr, cubic, new_reno
    public string UdpRelayMode { get; set; } = "native"; // native, quic
    public bool ZeroRttHandshake { get; set; } = false;
    public string LocalAddress { get; set; } = "0.0.0.0";
    public int LocalPort { get; set; } = 1080;
}

public class MeshVpnConfig
{
    public string TailscaleAuthKey { get; set; } = "";
    public bool TailscaleAcceptRoutes { get; set; } = true;
    public bool TailscaleExitNode { get; set; } = false;
    public string ZeroTierNetworkId { get; set; } = "";
    public bool ZeroTierAutoConnect { get; set; } = true;
}
