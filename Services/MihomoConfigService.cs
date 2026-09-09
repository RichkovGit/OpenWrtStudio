using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenWrtStudio.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenWrtStudio.Services;

public interface IMihomoConfigService
{
    string GenerateYaml(MihomoRootConfig config);
    MihomoRootConfig ParseYaml(string yamlContent);
    MihomoRootConfig CreateAntizapretSmartPreset();
    MihomoRootConfig CreateFullTunnelPreset();
    void InjectSubscription(MihomoRootConfig config, string providerName, string subUrl, int updateIntervalHours = 24);
    Task<(bool Success, string Message)> DeployToRouterAsync(string yamlContent, string remotePath = "/etc/mihomo/config.yaml", bool restartService = true);
    Task<(bool Success, string CurrentYaml)> FetchConfigFromRouterAsync(string remotePath = "/etc/mihomo/config.yaml");
    Task<(bool Success, string Message)> UpdateSubscriptionOnRouterAsync(string providerName, string subUrl);
}

public class MihomoConfigService : IMihomoConfigService
{
    private readonly ISshService _ssh;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public MihomoConfigService(ISshService ssh)
    {
        _ssh = ssh;
        _serializer = new SerializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public string GenerateYaml(MihomoRootConfig config)
    {
        var yaml = _serializer.Serialize(config);
        return yaml;
    }

    public MihomoRootConfig ParseYaml(string yamlContent)
    {
        try
        {
            var config = _deserializer.Deserialize<MihomoRootConfig>(yamlContent);
            return config ?? new MihomoRootConfig();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Ошибка парсинга YAML: {ex.Message}", ex);
        }
    }

    public MihomoRootConfig CreateAntizapretSmartPreset()
    {
        var config = new MihomoRootConfig
        {
            MixedPort = 7890,
            RedirPort = 7891,
            TproxyPort = 7892,
            AllowLan = true,
            BindAddress = "*",
            Mode = "rule",
            LogLevel = "info",
            Ipv6 = false,
            ExternalController = "0.0.0.0:9090",
            ExternalUi = "metacubexd",
            TcpConcurrent = true,
            UnifiedDelay = true,
            FindProcessMode = "strict",
            GeodataMode = true,
            GeoAutoUpdate = true,
            GeoUpdateInterval = 24
        };

        // TUN mode
        config.Tun = new MihomoTunConfig
        {
            Enable = true,
            Stack = "mixed",
            Device = "mihomo0",
            AutoRoute = true,
            AutoDetectInterface = true,
            AutoRedirect = false,
            StrictRoute = true,
            EndpointIndependentNat = true,
            Mtu = 9000,
            DnsHijack = new List<string> { "any:53", "tcp://any:53" }
        };

        // DNS Engine
        config.Dns = new MihomoDnsConfig
        {
            Enable = true,
            Listen = "0.0.0.0:1053",
            Ipv6 = false,
            EnhancedMode = "fake-ip",
            FakeIpRange = "198.18.0.1/16",
            FakeIpFilter = new List<string>
            {
                "*.lan",
                "*.local",
                "router.asus.com",
                "openwrt.lan",
                "+.msftconnecttest.com",
                "+.msftncsi.com",
                "time.*.com",
                "pool.ntp.org"
            },
            DefaultNameserver = new List<string>
            {
                "77.88.8.8",
                "1.1.1.1",
                "8.8.8.8"
            },
            Nameserver = new List<string>
            {
                "https://common.dot.dns.yandex.net/dns-query",
                "https://1.1.1.1/dns-query",
                "https://dns.google/dns-query"
            },
            Fallback = new List<string>
            {
                "tls://1.1.1.1:853",
                "tls://8.8.8.8:853"
            },
            FallbackFilter = new MihomoFallbackFilter
            {
                GeoIp = true,
                GeoIpCode = "RU",
                IpCidr = new List<string> { "240.0.0.0/4" }
            },
            NameserverPolicy = new Dictionary<string, string>
            {
                { "geosite:ru", "https://common.dot.dns.yandex.net/dns-query" },
                { "geosite:category-games-ru", "https://common.dot.dns.yandex.net/dns-query" }
            }
        };

        // Sniffer
        config.Sniffer = new MihomoSnifferConfig
        {
            Enable = true,
            ParsePureIp = true,
            OverrideDestination = false,
            Sniff = new Dictionary<string, MihomoSniffProtocol>
            {
                { "HTTP", new MihomoSniffProtocol { Ports = new List<int> { 80, 8080 } } },
                { "TLS", new MihomoSniffProtocol { Ports = new List<int> { 443, 8443 } } },
                { "QUIC", new MihomoSniffProtocol { Ports = new List<int> { 443 } } }
            },
            SkipDomain = new List<string> { "+.apple.com", "Mijia Cloud" }
        };

        // Proxy Groups
        config.ProxyGroups = new List<MihomoProxyGroup>
        {
            new()
            {
                Name = "PROXY",
                Type = "select",
                Proxies = new List<string> { "AUTO-SPEED", "DIRECT" }
            },
            new()
            {
                Name = "AUTO-SPEED",
                Type = "url-test",
                Url = "https://cp.cloudflare.com/generate_204",
                Interval = 300,
                Tolerance = 50,
                Proxies = new List<string> { "DIRECT" }
            },
            new()
            {
                Name = "YOUTUBE-DISCORD",
                Type = "select",
                Proxies = new List<string> { "PROXY", "AUTO-SPEED", "DIRECT" }
            },
            new()
            {
                Name = "TELEGRAM",
                Type = "select",
                Proxies = new List<string> { "PROXY", "DIRECT" }
            },
            new()
            {
                Name = "OPENAI-AI",
                Type = "select",
                Proxies = new List<string> { "PROXY", "DIRECT" }
            }
        };

        // Rule Providers (Антизапрет и популярные категории)
        config.RuleProviders = new Dictionary<string, MihomoRuleProvider>
        {
            {
                "antizapret", new MihomoRuleProvider
                {
                    Type = "http",
                    Behavior = "domain",
                    Url = "https://raw.githubusercontent.com/runetfreedom/russia-blocked-geosite/release/antizapret.yaml",
                    Path = "./ruleset/antizapret.yaml",
                    Interval = 86400,
                    Format = "yaml"
                }
            },
            {
                "youtube", new MihomoRuleProvider
                {
                    Type = "http",
                    Behavior = "classical",
                    Url = "https://raw.githubusercontent.com/Loyalsoldier/clash-rules/release/youtube.txt",
                    Path = "./ruleset/youtube.yaml",
                    Interval = 86400,
                    Format = "text"
                }
            }
        };

        // Visual Rules
        config.Rules = new List<string>
        {
            "GEOSITE,category-gov-ru,DIRECT",
            "GEOSITE,yandex,DIRECT",
            "GEOSITE,vk,DIRECT",
            "GEOSITE,sberbank,DIRECT",
            "GEOIP,RU,DIRECT,no-resolve",
            "RULE-SET,antizapret,PROXY",
            "GEOSITE,youtube,YOUTUBE-DISCORD",
            "GEOSITE,discord,YOUTUBE-DISCORD",
            "GEOSITE,openai,OPENAI-AI",
            "GEOSITE,telegram,TELEGRAM",
            "GEOSITE,instagram,PROXY",
            "GEOSITE,twitter,PROXY",
            "MATCH,DIRECT"
        };

        return config;
    }

    public MihomoRootConfig CreateFullTunnelPreset()
    {
        var config = CreateAntizapretSmartPreset();
        config.Rules.Clear();
        config.Rules.Add("IP-CIDR,192.168.0.0/16,DIRECT,no-resolve");
        config.Rules.Add("IP-CIDR,10.0.0.0/8,DIRECT,no-resolve");
        config.Rules.Add("IP-CIDR,172.16.0.0/12,DIRECT,no-resolve");
        config.Rules.Add("IP-CIDR,127.0.0.0/8,DIRECT,no-resolve");
        config.Rules.Add("MATCH,PROXY");
        return config;
    }

    public async Task<(bool Success, string Message)> DeployToRouterAsync(string yamlContent, string remotePath = "/etc/mihomo/config.yaml", bool restartService = true)
    {
        if (!_ssh.IsConnected)
        {
            return (false, "Нет подключения к роутеру по SSH");
        }

        // Validate YAML syntax before upload
        try
        {
            _deserializer.Deserialize<object>(yamlContent);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка в синтаксисе YAML: {ex.Message}");
        }

        // 1. Backup old config if exists
        await _ssh.ExecuteCommandAsync($"cp {remotePath} {remotePath}.bak 2>/dev/null", 5);

        // 2. Upload file via SFTP
        var (uploadSuccess, uploadMsg) = await _ssh.UploadFileContentAsync(yamlContent, remotePath);
        if (!uploadSuccess)
        {
            return (false, $"Ошибка загрузки файла на роутер: {uploadMsg}");
        }

        if (restartService)
        {
            // 3. Restart daemon
            var (code, outStr, err) = await _ssh.ExecuteCommandAsync("/etc/init.d/mihomo restart 2>/dev/null || /etc/init.d/openwrt-mihomo restart 2>/dev/null", 15);
            
            // 4. Verify running
            await Task.Delay(1000);
            var (checkCode, checkOut, _) = await _ssh.ExecuteCommandAsync("pgrep -x mihomo 2>/dev/null || pgrep -x clash 2>/dev/null", 5);
            if (checkCode == 0 && !string.IsNullOrWhiteSpace(checkOut))
            {
                return (true, $"Конфигурация успешно загружена в {remotePath}, и служба Mihomo перезапущена (PID: {checkOut.Trim()})!");
            }

            return (true, $"Конфигурация загружена. Статус перезапуска: {(string.IsNullOrEmpty(err) ? "команда отправлена" : err)}");
        }

        return (true, $"Конфигурация успешно сохранена в {remotePath}");
    }

    public async Task<(bool Success, string CurrentYaml)> FetchConfigFromRouterAsync(string remotePath = "/etc/mihomo/config.yaml")
    {
        if (!_ssh.IsConnected)
        {
            return (false, "Нет подключения к роутеру");
        }

        // Try standard paths if primary doesn't exist
        var pathsToTry = new[] { remotePath, "/etc/openwrt-mihomo/config.yaml", "/etc/openclash/config/config.yaml" };
        foreach (var p in pathsToTry)
        {
            var (success, content) = await _ssh.DownloadFileContentAsync(p);
            if (success && !string.IsNullOrWhiteSpace(content))
            {
                return (true, content);
            }
        }

        return (false, "Конфигурационный файл Mihomo не найден на роутере");
    }

    public void InjectSubscription(MihomoRootConfig config, string providerName, string subUrl, int updateIntervalHours = 24)
    {
        if (string.IsNullOrWhiteSpace(providerName)) providerName = "my-subscription";
        providerName = providerName.Trim().Replace(" ", "-");

        var provider = new MihomoProxyProvider
        {
            Type = "http",
            Url = subUrl.Trim(),
            Interval = updateIntervalHours * 3600,
            Path = $"./proxy_providers/{providerName}.yaml",
            HealthCheck = new MihomoHealthCheck
            {
                Enable = true,
                Interval = 300,
                Url = "https://www.gstatic.com/generate_204"
            }
        };

        config.ProxyProviders[providerName] = provider;

        // Ensure existing groups use this provider
        foreach (var group in config.ProxyGroups)
        {
            group.Use ??= new List<string>();
            if (!group.Use.Contains(providerName))
            {
                group.Use.Add(providerName);
            }
        }
    }

    public async Task<(bool Success, string Message)> UpdateSubscriptionOnRouterAsync(string providerName, string subUrl)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");
        if (string.IsNullOrWhiteSpace(providerName)) providerName = "my-subscription";
        providerName = providerName.Trim().Replace(" ", "-");

        var cmd = $"mkdir -p /etc/mihomo/proxy_providers /etc/openwrt-mihomo/proxy_providers 2>/dev/null; " +
                  $"curl -fsSL -k \"{subUrl.Trim()}\" -o /etc/mihomo/proxy_providers/{providerName}.yaml || " +
                  $"wget -q -O /etc/mihomo/proxy_providers/{providerName}.yaml \"{subUrl.Trim()}\"; " +
                  $"ls -lh /etc/mihomo/proxy_providers/{providerName}.yaml 2>/dev/null";

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(cmd, 25);
        if (code == 0 && outStr.Contains(providerName))
        {
            await _ssh.ExecuteCommandAsync("/etc/init.d/mihomo reload 2>/dev/null || /etc/init.d/mihomo restart 2>/dev/null || /etc/init.d/openwrt-mihomo restart 2>/dev/null", 10);
            return (true, $"Подписка '{providerName}' успешно загружена на роутер и служба обновлена!\n{outStr.Trim()}");
        }
        return (false, $"Не удалось скачать подписку: {err}");
    }
}
