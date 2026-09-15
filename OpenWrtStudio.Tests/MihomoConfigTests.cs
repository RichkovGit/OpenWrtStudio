using System.Collections.Generic;
using System.Linq;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;

namespace OpenWrtStudio.Tests;

public class MihomoConfigTests
{
    private readonly MihomoConfigService _service;

    public MihomoConfigTests()
    {
        // Null SSH service since we only test YAML parsing and generation
        _service = new MihomoConfigService(null!);
    }

    [Fact]
    public void GenerateYaml_CreatesValidClashMetaStructure()
    {
        var config = _service.CreateAntizapretSmartPreset();
        var yaml = _service.GenerateYaml(config);

        Assert.NotNull(yaml);
        Assert.Contains("mixed-port: 7890", yaml);
        Assert.Contains("tun:", yaml);
        Assert.Contains("enable: true", yaml);
        Assert.Contains("dns:", yaml);
        Assert.Contains("enhanced-mode: fake-ip", yaml);
        Assert.Contains("sniffer:", yaml);
        Assert.Contains("proxy-groups:", yaml);
        Assert.Contains("rule-providers:", yaml);
        Assert.Contains("antizapret", yaml);
        Assert.Contains("rules:", yaml);
    }

    [Fact]
    public void ParseYaml_RoundtripsSuccessfully()
    {
        var original = _service.CreateAntizapretSmartPreset();
        var yaml = _service.GenerateYaml(original);
        var parsed = _service.ParseYaml(yaml);

        Assert.Equal(original.MixedPort, parsed.MixedPort);
        Assert.Equal(original.Mode, parsed.Mode);
        Assert.Equal(original.Tun.Enable, parsed.Tun.Enable);
        Assert.Equal(original.Tun.Stack, parsed.Tun.Stack);
        Assert.Equal(original.Dns.EnhancedMode, parsed.Dns.EnhancedMode);
        Assert.Equal(original.ProxyGroups.Count, parsed.ProxyGroups.Count);
        Assert.Equal(original.Rules.Count, parsed.Rules.Count);
    }

    [Fact]
    public void AntizapretPreset_ContainsDirectRuAndProxyBlocked()
    {
        var config = _service.CreateAntizapretSmartPreset();

        Assert.Contains(config.Rules, r => r.Contains("category-gov-ru") && r.Contains("DIRECT"));
        Assert.Contains(config.Rules, r => r.Contains("GEOIP,RU,DIRECT"));
        Assert.Contains(config.Rules, r => r.Contains("RULE-SET,antizapret,PROXY"));
        Assert.Contains(config.Rules, r => r.Contains("youtube") && r.Contains("YOUTUBE-DISCORD"));
        Assert.Contains(config.Rules, r => r.Contains("discord") && r.Contains("YOUTUBE-DISCORD"));
    }

    [Fact]
    public void FullTunnelPreset_RoutesMatchToProxy()
    {
        var config = _service.CreateFullTunnelPreset();

        Assert.Contains(config.Rules, r => r.Contains("MATCH,PROXY"));
        Assert.Contains(config.Rules, r => r.Contains("192.168.0.0/16,DIRECT"));
    }
}

public class OnlinePackageSearchTests
{
    private readonly OnlinePackageSearchService _searchService;

    public OnlinePackageSearchTests()
    {
        _searchService = new OnlinePackageSearchService();
    }

    [Theory]
    [InlineData("forkop")]
    [InlineData("amnezia")]
    [InlineData("sing-box")]
    [InlineData("argon")]
    [InlineData("passwall")]
    [InlineData("wireguard")]
    [InlineData("tailscale")]
    public async System.Threading.Tasks.Task Search_FindsRequestedPackages(string query)
    {
        var results = await _searchService.SearchOnlineAsync(query);

        Assert.NotEmpty(results);
        Assert.Contains(results, r =>
            r.Name.Contains(query, System.StringComparison.OrdinalIgnoreCase) ||
            r.Description.Contains(query, System.StringComparison.OrdinalIgnoreCase) ||
            r.Category.Contains(query, System.StringComparison.OrdinalIgnoreCase));
    }
}
