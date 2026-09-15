using System;
using System.Linq;
using System.Threading.Tasks;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;

namespace OpenWrtStudio.Tests;

public class ForkopTests
{
    [Fact]
    public async Task ForkopService_GetSectionConfigAsync_ReturnsDefaultSubscriptionsAndRulesets()
    {
        var service = new ForkopService(new SshService());
        var section = await service.GetSectionConfigAsync();

        Assert.NotNull(section);
        Assert.True(section.IsEnabled);
        Assert.Equal("VPN Прокси", section.Name);
        Assert.Equal("Connection", section.Action);
        Assert.Equal("br-lan", section.SelectedInterface);

        Assert.True(section.SubscriptionUrls.Count >= 4);
        Assert.Contains(section.SubscriptionUrls, s => s.Contains("blancvpn"));
        Assert.Contains(section.SubscriptionUrls, s => s.Contains("stealthsurf"));
        Assert.Contains(section.SubscriptionUrls, s => s.Contains("abuzvpn"));
        Assert.Contains(section.SubscriptionUrls, s => s.Contains("vlessforu"));

        Assert.True(section.Rulesets.Count >= 10);
        Assert.Contains("Russia inside", section.Rulesets);
        Assert.Contains("Youtube", section.Rulesets);
        Assert.Contains("Discord", section.Rulesets);
        Assert.Contains("Telegram", section.Rulesets);
    }

    [Fact]
    public async Task ForkopService_GetSubscriptionInfoListAsync_ContainsAllLiveSubscriptions()
    {
        var service = new ForkopService(new SshService());
        var list = await service.GetSubscriptionInfoListAsync();

        Assert.NotEmpty(list);
        Assert.Contains(list, s => s.Name == "BlancVPN");
        Assert.Contains(list, s => s.Name == "StealthSurf" && s.TrafficUsed.Contains("230"));
        Assert.Contains(list, s => s.Name == "AbuzVPN" && s.TrafficTotal.Contains("32.2 GB"));
        Assert.Contains(list, s => s.Name.Contains("Vlessforu") && s.ServerCount == 207);
    }

    [Fact]
    public async Task ForkopService_GetServersAsync_ContainsAutoGroupsAndNodes()
    {
        var service = new ForkopService(new SshService());
        var servers = await service.GetServersAsync();

        Assert.NotEmpty(servers);
        var priorityGroup = servers.FirstOrDefault(s => s.Protocol == "Priority");
        Assert.NotNull(priorityGroup);
        Assert.True(priorityGroup.IsActive);
        Assert.True(priorityGroup.IsAutoGroup);

        var hysteriaNode = servers.FirstOrDefault(s => s.Protocol == "Hysteria2");
        Assert.NotNull(hysteriaNode);
        Assert.True(hysteriaNode.LatencyMs > 0);
        Assert.Equal($"{hysteriaNode.LatencyMs}ms", hysteriaNode.LatencyDisplay);
    }

    [Fact]
    public async Task ForkopService_TestLatenciesAsync_UpdatesAllNodeLatencies()
    {
        var service = new ForkopService(new SshService());
        var servers = await service.GetServersAsync();

        var updated = await service.TestLatenciesAsync(servers);
        Assert.Equal(servers.Count, updated.Count);
        Assert.All(updated.Where(s => s.Protocol != "URLTest" || s.Name.Contains("Stealth")), s => Assert.NotNull(s.LatencyMs));
    }

    [Fact]
    public void ForkopServerNode_LatencyDisplay_FormatsCorrectly()
    {
        var nodeWithPing = new ForkopServerNode { LatencyMs = 236 };
        Assert.Equal("236ms", nodeWithPing.LatencyDisplay);

        var nodeNull = new ForkopServerNode { LatencyMs = null };
        Assert.Equal("N/A", nodeNull.LatencyDisplay);
    }

    [Fact]
    public void ForkopService_ParseAllRouterNodes_ParsesHundredsOfNodesCorrectly()
    {
        var service = new ForkopService(new SshService());
        var sampleOutput = @"
===CLASH_PROXIES===
{
  ""proxies"": {
    ""main-priority-main_priority-out"": { ""type"": ""Selector"", ""now"": ""Blanc 🇳🇱 Amsterdam"" },
    ""Blanc 🇳🇱 Amsterdam"": { ""type"": ""VLESS"", ""history"": [{ ""delay"": 210 }] },
    ""Stealth 🚀 Fast"": { ""type"": ""Hysteria2"", ""history"": [{ ""delay"": 236 }] },
    ""Abuz ⚡ Poland"": { ""type"": ""VLESS"", ""history"": [{ ""delay"": 279 }] },
    ""Vless4U 🇫🇮 Finland"": { ""type"": ""VLESS"", ""history"": [{ ""delay"": 190 }] }
  }
}
===SINGBOX_CONFIG===
{
  ""outbounds"": [
    { ""tag"": ""Blanc 🇳🇱 Amsterdam"", ""type"": ""vless"", ""server"": ""143.20.254.145"", ""server_port"": 443 },
    { ""tag"": ""Stealth 🚀 Fast"", ""type"": ""hysteria2"", ""server"": ""93.114.47.73"", ""server_port"": 443 },
    { ""tag"": ""Abuz ⚡ Poland"", ""type"": ""vless"", ""server"": ""93.114.47.95"", ""server_port"": 443 },
    { ""tag"": ""Vless4U 🇫🇮 Finland"", ""type"": ""vless"", ""server"": ""93.114.47.162"", ""server_port"": 443 }
  ]
}
===SECTION_CACHE===
{
  ""servers"": {
    ""Blanc 🇳🇱 Amsterdam"": ""143.20.254.145"",
    ""Stealth 🚀 Fast"": ""93.114.47.73""
  }
}
";
        var nodes = service.ParseAllRouterNodes(sampleOutput);
        Assert.NotEmpty(nodes);
        Assert.True(nodes.Count >= 8); // 4 auto groups + 4 servers
        Assert.Contains(nodes, n => n.IsAutoGroup && n.Protocol == "Priority");
        Assert.Contains(nodes, n => n.Name == "Blanc 🇳🇱 Amsterdam" && n.IsActive && n.LatencyMs == 210);
        Assert.Contains(nodes, n => n.Name == "Stealth 🚀 Fast" && n.Protocol == "Hysteria2" && n.Provider == "Stealth");
        Assert.Contains(nodes, n => n.Name == "Vless4U 🇫🇮 Finland" && n.Provider == "Vless4U");
    }

    [Fact]
    public void ForkopViewModel_ApplyServerFilter_FiltersByProviderAndQuery()
    {
        var vm = new OpenWrtStudio.ViewModels.ForkopViewModel(new ForkopService(new SshService()), new SshService());
        vm.Servers.Add(new ForkopServerNode { Name = "Priority Group", Protocol = "Priority", IsAutoGroup = true });
        vm.Servers.Add(new ForkopServerNode { Name = "Blanc 🇳🇱 Amsterdam", Protocol = "VLESS", Provider = "Blanc", Subtitle = "143.20.254.145:443" });
        vm.Servers.Add(new ForkopServerNode { Name = "Stealth 🚀 Fast", Protocol = "Hysteria2", Provider = "Stealth", Subtitle = "93.114.47.73:443" });
        vm.Servers.Add(new ForkopServerNode { Name = "Abuz ⚡ Poland", Protocol = "VLESS", Provider = "Abuz", Subtitle = "93.114.47.95:443" });

        // Initial filter
        vm.ApplyServerFilter();
        Assert.Equal(3, vm.FilteredServers.Count);

        // Filter by Provider "Blanc"
        vm.SelectedProviderFilter = "Blanc";
        Assert.Single(vm.FilteredServers);
        Assert.Equal("Blanc 🇳🇱 Amsterdam", vm.FilteredServers[0].Name);

        // Search query "Poland"
        vm.SelectedProviderFilter = "Все";
        vm.ServerSearchQuery = "Poland";
        Assert.Single(vm.FilteredServers);
        Assert.Equal("Abuz ⚡ Poland", vm.FilteredServers[0].Name);
    }
}