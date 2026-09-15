using System.Collections.Generic;
using System.Linq;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;

namespace OpenWrtStudio.Tests;

public class NewFeaturesTests
{
    private readonly VpnProtocolService _vpnService;
    private readonly MihomoConfigService _mihomoService;

    public NewFeaturesTests()
    {
        _vpnService = new VpnProtocolService(null!);
        _mihomoService = new MihomoConfigService(null!);
    }

    [Fact]
    public void AmneziaWgConf_ParsesAllObfuscationParameters()
    {
        const string sampleConf = @"
[Interface]
PrivateKey = aaaaaabbbbbbccccccddddddeeeeeeffffffgggggg=
Address = 10.8.0.2/24
DNS = 1.1.1.1
Jc = 4
Jmin = 50
Jmax = 1000
S1 = 15
S2 = 30
H1 = 123456789
H2 = 987654321
H3 = 234567890
H4 = 345678901
MTU = 1420

[Peer]
PublicKey = zzzzzzyyyyyyxxxxxxwwwwwwvvvvvvuuuuuutttttt=
PresharedKey = pppppppppppppppppppppppppppppppppppppppp=
Endpoint = 198.51.100.1:51820
AllowedIPs = 0.0.0.0/0, ::/0
PersistentKeepalive = 25
";

        var config = _vpnService.ParseAmneziaWgConf(sampleConf);

        Assert.Equal("aaaaaabbbbbbccccccddddddeeeeeeffffffgggggg=", config.PrivateKey);
        Assert.Equal("10.8.0.2/24", config.Address);
        Assert.Equal("1.1.1.1", config.Dns);
        Assert.Equal(4, config.Jc);
        Assert.Equal(50, config.Jmin);
        Assert.Equal(1000, config.Jmax);
        Assert.Equal(15, config.S1);
        Assert.Equal(30, config.S2);
        Assert.Equal(123456789u, config.H1);
        Assert.Equal(987654321u, config.H2);
        Assert.Equal(234567890u, config.H3);
        Assert.Equal(345678901u, config.H4);
        Assert.Equal(1420, config.Mtu);

        Assert.Equal("zzzzzzyyyyyyxxxxxxwwwwwwvvvvvvuuuuuutttttt=", config.PeerPublicKey);
        Assert.Equal("pppppppppppppppppppppppppppppppppppppppp=", config.PeerPresharedKey);
        Assert.Equal("198.51.100.1:51820", config.Endpoint);
        Assert.Equal("0.0.0.0/0, ::/0", config.AllowedIPs);
        Assert.Equal(25, config.PersistentKeepalive);
    }

    [Fact]
    public void Mihomo_InjectSubscription_PopulatesProvidersAndGroups()
    {
        var config = _mihomoService.CreateAntizapretSmartPreset();
        const string subName = "FastVpnVless";
        const string subUrl = "https://vpn-provider.com/sub/token123";

        _mihomoService.InjectSubscription(config, subName, subUrl, updateIntervalHours: 12);

        // Check proxy provider entry
        Assert.NotNull(config.ProxyProviders);
        Assert.True(config.ProxyProviders.ContainsKey(subName));

        var provider = config.ProxyProviders[subName];
        Assert.Equal("http", provider.Type);
        Assert.Equal(subUrl, provider.Url);
        Assert.Equal("./proxy_providers/FastVpnVless.yaml", provider.Path);
        Assert.Equal(12 * 3600, provider.Interval);
        Assert.NotNull(provider.HealthCheck);
        Assert.True(provider.HealthCheck.Enable);

        // Check that proxy groups include this provider
        foreach (var group in config.ProxyGroups)
        {
            Assert.NotNull(group.Use);
            Assert.Contains(subName, group.Use);
        }

        // Test YAML generation with injected subscription
        var yaml = _mihomoService.GenerateYaml(config);
        Assert.Contains("proxy-providers:", yaml);
        Assert.Contains("FastVpnVless:", yaml);
        Assert.Contains("url: https://vpn-provider.com/sub/token123", yaml);
        Assert.Contains("use:", yaml);
    }

    [Fact]
    public void CronJobItem_FromCrontabLine_ParsesCorrectly()
    {
        const string rawLine = "0 4 * * * reboot # Nightly router reboot";
        var job = CronJobItem.FromCrontabLine(rawLine);

        Assert.True(job.IsEnabled);
        Assert.Equal("0 4 * * *", job.Schedule);
        Assert.Equal("reboot", job.Command);
        Assert.Equal("Nightly router reboot", job.Description);
    }

    [Fact]
    public void CronJobItem_FromCrontabLine_HandlesDisabledJob()
    {
        const string rawLine = "# 0 1 * * * wifi down # Night quiet mode";
        var job = CronJobItem.FromCrontabLine(rawLine);

        Assert.False(job.IsEnabled);
        Assert.Equal("0 1 * * *", job.Schedule);
        Assert.Equal("wifi down", job.Command);
        Assert.Equal("Night quiet mode", job.Description);
    }

    [Fact]
    public void CronJobItem_ToCrontabLine_RoundtripsSuccessfully()
    {
        var job = new CronJobItem
        {
            IsEnabled = true,
            Schedule = "*/30 * * * *",
            Command = "sync && echo 3 > /proc/sys/vm/drop_caches",
            Description = "Clear RAM cache"
        };

        var line = job.ToCrontabLine();
        Assert.Equal("*/30 * * * * sync && echo 3 > /proc/sys/vm/drop_caches # Clear RAM cache", line);

        var parsed = CronJobItem.FromCrontabLine(line);
        Assert.True(parsed.IsEnabled);
        Assert.Equal(job.Schedule, parsed.Schedule);
        Assert.Equal(job.Command, parsed.Command);
        Assert.Equal(job.Description, parsed.Description);
    }

    [Fact]
    public void RouterConfigModels_InitializeWithCleanDefaults()
    {
        var wifi = new WifiIfaceConfig();
        Assert.Equal("OpenWrt_WiFi", wifi.Ssid);
        Assert.Equal("psk2", wifi.Encryption);
        Assert.False(wifi.Disabled);

        var lan = new LanDhcpConfig();
        Assert.Equal("192.168.1.1", lan.IpAddress);
        Assert.Equal("255.255.255.0", lan.Netmask);
        Assert.Equal(100, lan.DhcpStart);
        Assert.Equal(150, lan.DhcpLimit);

        var portFwd = new PortForwardRule();
        Assert.True(portFwd.Enabled);
        Assert.Equal("tcp", portFwd.Proto);
    }

    [Fact]
    public void NotificationService_ThrottlesRepeatNotificationsWithSameTag()
    {
        var service = new NotificationService();
        int eventCount = 0;
        service.OnNotificationRequested += (_, _, _) => eventCount++;

        // First call should fire
        service.ShowNotification("Title 1", "Message 1", NotificationSeverity.Info, tag: "wifi_alert", cooldownSeconds: 10);
        Assert.Equal(1, eventCount);

        // Immediate repeat with same tag should be throttled
        service.ShowNotification("Title 1", "Message 1", NotificationSeverity.Info, tag: "wifi_alert", cooldownSeconds: 10);
        Assert.Equal(1, eventCount);

        // Different tag should fire immediately
        service.ShowNotification("Title 2", "Message 2", NotificationSeverity.Warning, tag: "dns_alert", cooldownSeconds: 10);
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public void ApplicationIcon_FileExistsAndHasValidMultiSizeHeaders()
    {
        var icoPath = @"C:\Users\danny\.gemini\antigravity\scratch\OpenWrtStudio\Assets\app.ico";
        Assert.True(System.IO.File.Exists(icoPath), "app.ico must exist in Assets");

        var bytes = System.IO.File.ReadAllBytes(icoPath);
        Assert.True(bytes.Length > 1000, "ICO file should be substantial in size");

        // Validate ICO magic: 0x0000 0x0001
        Assert.Equal(0, bytes[0]);
        Assert.Equal(0, bytes[1]);
        Assert.Equal(1, bytes[2]);
        Assert.Equal(0, bytes[3]);

        // Validate count of images >= 6
        int count = BitConverter.ToUInt16(bytes, 4);
        Assert.True(count >= 6, $"Expected >= 6 icon sizes in ICO, got {count}");
    }
}
