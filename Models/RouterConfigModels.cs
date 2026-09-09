using System.Collections.Generic;

namespace OpenWrtStudio.Models;

public class WifiRadioConfig
{
    public string Device { get; set; } = "radio0";
    public string Band { get; set; } = "2.4 GHz"; // 2.4 GHz, 5 GHz, 6 GHz
    public string Channel { get; set; } = "auto";
    public string HtMode { get; set; } = "HT40"; // HT20, HT40, VHT80, HE80, HE160
    public bool Disabled { get; set; } = false;
    public List<WifiIfaceConfig> Interfaces { get; set; } = new();
}

public class WifiIfaceConfig
{
    public string SectionId { get; set; } = "default_radio0";
    public string Device { get; set; } = "radio0";
    public string Ssid { get; set; } = "OpenWrt_WiFi";
    public string Encryption { get; set; } = "psk2"; // none, psk2, sae, sae-mixed
    public string Key { get; set; } = "";
    public bool IsGuest { get; set; } = false;
    public bool Disabled { get; set; } = false;
    public bool Hidden { get; set; } = false;
}

public class LanDhcpConfig
{
    public string IpAddress { get; set; } = "192.168.1.1";
    public string Netmask { get; set; } = "255.255.255.0";
    public int DhcpStart { get; set; } = 100;
    public int DhcpLimit { get; set; } = 150;
    public string LeaseTime { get; set; } = "12h";
    public List<StaticDhcpLease> StaticLeases { get; set; } = new();
}

public class StaticDhcpLease
{
    public string SectionId { get; set; } = "";
    public string Name { get; set; } = "PC";
    public string Mac { get; set; } = "AA:BB:CC:DD:EE:FF";
    public string Ip { get; set; } = "192.168.1.50";
}

public class WanConfig
{
    public string Proto { get; set; } = "pppoe"; // pppoe, dhcp, static
    public string PppoeUsername { get; set; } = "";
    public string PppoePassword { get; set; } = "";
    public int Mtu { get; set; } = 1492;
    public string StaticIp { get; set; } = "";
    public string StaticNetmask { get; set; } = "255.255.255.0";
    public string StaticGateway { get; set; } = "";
    public string StaticDns { get; set; } = "77.88.8.8 1.1.1.1";
}

public class PortForwardRule
{
    public string SectionId { get; set; } = "";
    public string Name { get; set; } = "Web Server";
    public string SrcPort { get; set; } = "8080";
    public string DestIp { get; set; } = "192.168.1.100";
    public string DestPort { get; set; } = "80";
    public string Proto { get; set; } = "tcp"; // tcp, udp, tcp udp
    public bool Enabled { get; set; } = true;
}

public class SystemAdminConfig
{
    public string Hostname { get; set; } = "OpenWrt";
    public string Timezone { get; set; } = "UTC-3";
    public string NtpServers { get; set; } = "0.openwrt.pool.ntp.org 1.openwrt.pool.ntp.org";
    public string NewPassword { get; set; } = "";
}
