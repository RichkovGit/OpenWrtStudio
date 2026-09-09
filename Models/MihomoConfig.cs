using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace OpenWrtStudio.Models;

public class MihomoRootConfig
{
    [YamlMember(Alias = "mixed-port")]
    public int MixedPort { get; set; } = 7890;

    [YamlMember(Alias = "port")]
    public int Port { get; set; } = 0;

    [YamlMember(Alias = "socks-port")]
    public int SocksPort { get; set; } = 0;

    [YamlMember(Alias = "redir-port")]
    public int RedirPort { get; set; } = 7891;

    [YamlMember(Alias = "tproxy-port")]
    public int TproxyPort { get; set; } = 7892;

    [YamlMember(Alias = "allow-lan")]
    public bool AllowLan { get; set; } = true;

    [YamlMember(Alias = "bind-address")]
    public string BindAddress { get; set; } = "*";

    [YamlMember(Alias = "mode")]
    public string Mode { get; set; } = "rule"; // rule, global, direct

    [YamlMember(Alias = "log-level")]
    public string LogLevel { get; set; } = "info"; // silent, error, warning, info, debug

    [YamlMember(Alias = "ipv6")]
    public bool Ipv6 { get; set; } = false;

    [YamlMember(Alias = "external-controller")]
    public string ExternalController { get; set; } = "0.0.0.0:9090";

    [YamlMember(Alias = "secret")]
    public string Secret { get; set; } = "";

    [YamlMember(Alias = "external-ui")]
    public string ExternalUi { get; set; } = "metacubexd"; // metacubexd, yacd, zashboard

    [YamlMember(Alias = "tcp-concurrent")]
    public bool TcpConcurrent { get; set; } = true;

    [YamlMember(Alias = "unified-delay")]
    public bool UnifiedDelay { get; set; } = true;

    [YamlMember(Alias = "find-process-mode")]
    public string FindProcessMode { get; set; } = "strict"; // off, strict, always

    [YamlMember(Alias = "geodata-mode")]
    public bool GeodataMode { get; set; } = true;

    [YamlMember(Alias = "geo-auto-update")]
    public bool GeoAutoUpdate { get; set; } = true;

    [YamlMember(Alias = "geo-update-interval")]
    public int GeoUpdateInterval { get; set; } = 24;

    [YamlMember(Alias = "tun")]
    public MihomoTunConfig Tun { get; set; } = new();

    [YamlMember(Alias = "dns")]
    public MihomoDnsConfig Dns { get; set; } = new();

    [YamlMember(Alias = "sniffer")]
    public MihomoSnifferConfig Sniffer { get; set; } = new();

    [YamlMember(Alias = "proxy-groups")]
    public List<MihomoProxyGroup> ProxyGroups { get; set; } = new();

    [YamlMember(Alias = "proxy-providers")]
    public Dictionary<string, MihomoProxyProvider> ProxyProviders { get; set; } = new();

    [YamlMember(Alias = "rule-providers")]
    public Dictionary<string, MihomoRuleProvider> RuleProviders { get; set; } = new();

    [YamlMember(Alias = "rules")]
    public List<string> Rules { get; set; } = new();

    [YamlMember(Alias = "proxies")]
    public List<Dictionary<string, object>> Proxies { get; set; } = new();
}

public class MihomoTunConfig
{
    [YamlMember(Alias = "enable")]
    public bool Enable { get; set; } = true;

    [YamlMember(Alias = "stack")]
    public string Stack { get; set; } = "mixed"; // system, gvisor, mixed

    [YamlMember(Alias = "device")]
    public string Device { get; set; } = "mihomo0";

    [YamlMember(Alias = "auto-route")]
    public bool AutoRoute { get; set; } = true;

    [YamlMember(Alias = "auto-detect-interface")]
    public bool AutoDetectInterface { get; set; } = true;

    [YamlMember(Alias = "auto-redirect")]
    public bool AutoRedirect { get; set; } = false;

    [YamlMember(Alias = "strict-route")]
    public bool StrictRoute { get; set; } = true;

    [YamlMember(Alias = "dns-hijack")]
    public List<string> DnsHijack { get; set; } = new() { "any:53", "tcp://any:53" };

    [YamlMember(Alias = "endpoint-independent-nat")]
    public bool EndpointIndependentNat { get; set; } = true;

    [YamlMember(Alias = "mtu")]
    public int Mtu { get; set; } = 9000;
}

public class MihomoDnsConfig
{
    [YamlMember(Alias = "enable")]
    public bool Enable { get; set; } = true;

    [YamlMember(Alias = "listen")]
    public string Listen { get; set; } = "0.0.0.0:1053";

    [YamlMember(Alias = "ipv6")]
    public bool Ipv6 { get; set; } = false;

    [YamlMember(Alias = "enhanced-mode")]
    public string EnhancedMode { get; set; } = "fake-ip"; // fake-ip, redir-host

    [YamlMember(Alias = "fake-ip-range")]
    public string FakeIpRange { get; set; } = "198.18.0.1/16";

    [YamlMember(Alias = "fake-ip-filter")]
    public List<string> FakeIpFilter { get; set; } = new()
    {
        "*.lan",
        "*.local",
        "localhost.ptlogin2.qq.com",
        "+.msftconnecttest.com",
        "+.msftncsi.com",
        "time.*.com",
        "time.*.gov",
        "pool.ntp.org",
        "+.ntp.org"
    };

    [YamlMember(Alias = "default-nameserver")]
    public List<string> DefaultNameserver { get; set; } = new()
    {
        "77.88.8.8",
        "1.1.1.1",
        "8.8.8.8"
    };

    [YamlMember(Alias = "nameserver")]
    public List<string> Nameserver { get; set; } = new()
    {
        "https://dns.google/dns-query",
        "https://1.1.1.1/dns-query",
        "https://common.dot.dns.yandex.net/dns-query"
    };

    [YamlMember(Alias = "fallback")]
    public List<string> Fallback { get; set; } = new()
    {
        "tls://1.1.1.1:853",
        "tls://8.8.8.8:853"
    };

    [YamlMember(Alias = "fallback-filter")]
    public MihomoFallbackFilter FallbackFilter { get; set; } = new();

    [YamlMember(Alias = "nameserver-policy")]
    public Dictionary<string, string> NameserverPolicy { get; set; } = new()
    {
        { "geosite:ru", "https://common.dot.dns.yandex.net/dns-query" },
        { "geosite:category-games-ru", "https://common.dot.dns.yandex.net/dns-query" }
    };
}

public class MihomoFallbackFilter
{
    [YamlMember(Alias = "geoip")]
    public bool GeoIp { get; set; } = true;

    [YamlMember(Alias = "geoip-code")]
    public string GeoIpCode { get; set; } = "RU";

    [YamlMember(Alias = "ipcidr")]
    public List<string> IpCidr { get; set; } = new() { "240.0.0.0/4" };
}

public class MihomoSnifferConfig
{
    [YamlMember(Alias = "enable")]
    public bool Enable { get; set; } = true;

    [YamlMember(Alias = "parse-pure-ip")]
    public bool ParsePureIp { get; set; } = true;

    [YamlMember(Alias = "override-destination")]
    public bool OverrideDestination { get; set; } = false;

    [YamlMember(Alias = "sniff")]
    public Dictionary<string, MihomoSniffProtocol> Sniff { get; set; } = new()
    {
        { "HTTP", new MihomoSniffProtocol { Ports = new List<int> { 80, 8080 } } },
        { "TLS", new MihomoSniffProtocol { Ports = new List<int> { 443, 8443 } } },
        { "QUIC", new MihomoSniffProtocol { Ports = new List<int> { 443 } } }
    };

    [YamlMember(Alias = "skip-domain")]
    public List<string> SkipDomain { get; set; } = new()
    {
        "+.apple.com",
        "Mijia Cloud"
    };
}

public class MihomoSniffProtocol
{
    [YamlMember(Alias = "ports")]
    public List<int> Ports { get; set; } = new();
}

public class MihomoProxyGroup
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "PROXY";

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "select"; // select, url-test, fallback, load-balance

    [YamlMember(Alias = "proxies")]
    public List<string> Proxies { get; set; } = new();

    [YamlMember(Alias = "use")]
    public List<string>? Use { get; set; }

    [YamlMember(Alias = "url")]
    public string? Url { get; set; } = "https://cp.cloudflare.com/generate_204";

    [YamlMember(Alias = "interval")]
    public int? Interval { get; set; } = 300;

    [YamlMember(Alias = "tolerance")]
    public int? Tolerance { get; set; } = 50;
}

public class MihomoProxyProvider
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "http"; // http, file

    [YamlMember(Alias = "url")]
    public string Url { get; set; } = "";

    [YamlMember(Alias = "interval")]
    public int Interval { get; set; } = 86400;

    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "";

    [YamlMember(Alias = "health-check")]
    public MihomoHealthCheck HealthCheck { get; set; } = new();

    [YamlMember(Alias = "filter")]
    public string? Filter { get; set; }
}

public class MihomoHealthCheck
{
    [YamlMember(Alias = "enable")]
    public bool Enable { get; set; } = true;

    [YamlMember(Alias = "interval")]
    public int Interval { get; set; } = 300;

    [YamlMember(Alias = "url")]
    public string Url { get; set; } = "https://www.gstatic.com/generate_204";
}

public class MihomoRuleProvider
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "http"; // http, file

    [YamlMember(Alias = "behavior")]
    public string Behavior { get; set; } = "classical"; // domain, ipcidr, classical

    [YamlMember(Alias = "url")]
    public string Url { get; set; } = "";

    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "";

    [YamlMember(Alias = "interval")]
    public int Interval { get; set; } = 86400;

    [YamlMember(Alias = "format")]
    public string Format { get; set; } = "yaml"; // yaml, text, mrs
}

public class VisualRuleItem
{
    public string RuleType { get; set; } = "GEOSITE"; // GEOSITE, GEOIP, DOMAIN-SUFFIX, DOMAIN-KEYWORD, IP-CIDR, MATCH
    public string Payload { get; set; } = "";
    public string Target { get; set; } = "DIRECT"; // DIRECT, REJECT, group-name
    public bool NoResolve { get; set; } = false;

    public string ToRuleString()
    {
        if (RuleType == "MATCH")
        {
            return $"MATCH,{Target}";
        }

        string res = $"{RuleType},{Payload},{Target}";
        if (NoResolve) res += ",no-resolve";
        return res;
    }

    public static VisualRuleItem FromString(string ruleStr)
    {
        var parts = ruleStr.Split(',');
        var item = new VisualRuleItem();
        if (parts.Length >= 2)
        {
            item.RuleType = parts[0].Trim();
            if (item.RuleType.Equals("MATCH", System.StringComparison.OrdinalIgnoreCase))
            {
                item.Payload = "";
                item.Target = parts[1].Trim();
            }
            else if (parts.Length >= 3)
            {
                item.Payload = parts[1].Trim();
                item.Target = parts[2].Trim();
                if (parts.Length >= 4 && parts[3].Trim().Equals("no-resolve", System.StringComparison.OrdinalIgnoreCase))
                {
                    item.NoResolve = true;
                }
            }
        }
        return item;
    }
}
