using System.Text.Json;
using System.IO;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;

namespace OpenWrtStudio.Core.Tests;

public class VpnTopologyTests
{
    public static string Snapshot()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/vpn_topology.json")));
        var f = json.RootElement;
        return $"===SECTION_CACHE===\n{string.Join("\n", f.GetProperty("cache").EnumerateArray().Select(x => x.GetRawText()))}\n===SINGBOX_CONFIG===\n{f.GetProperty("config").GetRawText()}\n===CLASH_PROXIES===\n{{\"proxies\":{f.GetProperty("proxies").GetRawText()}}}";
    }
    [Fact]
    public void ReadsActualGroupsFromAllCachesAndHidesInfrastructure()
    {
        var nodes = ForkopNodeParser.Parse(Snapshot());
        Assert.Equal(5, nodes.Count(n => n.Protocol == "URLTest"));
        Assert.Contains(nodes, n => n.Name == "Авто Hella (Запасной 3)");
        Assert.Contains(nodes, n => n.Name == "Приоритет: Shrimp -> Stealth -> Blanc -> Hella -> Free" && n.Protocol == "Priority");
        Assert.DoesNotContain(nodes, n => new[] { "direct-out", "bypass-out", "GLOBAL", "REJECT" }.Contains(n.GroupTag));
        Assert.Equal(nodes.Count, nodes.Select(n => n.GroupTag).Distinct().Count());
    }
    [Fact]
    public void ActiveSelectionFollowsTheRoutingRootNotLastSelectorOrNameSubstring()
    {
        var nodes = ForkopNodeParser.Parse(Snapshot());
        Assert.Equal(new[] { "auto-shrimp", "main-out", "node-b", "priority-route" }, nodes.Where(n => n.IsActive).Select(n => n.GroupTag).OrderBy(x => x));
        Assert.False(nodes.Single(n => n.GroupTag == "node-a").IsActive);
        Assert.Equal(167, nodes.Single(n => n.GroupTag == "priority-route").LatencyMs);
        Assert.Null(nodes.Single(n => n.GroupTag == "node-b-plus").LatencyMs);
        Assert.Equal("main-out", nodes.Single(n => n.GroupTag == "node-a").SelectionGroupTag);
    }
    [Fact]
    public void EmptyMalformedOrOfflineSnapshotsNeverInventGroupsOrActiveNodes()
    {
        Assert.Empty(ForkopNodeParser.Parse(""));
        Assert.Empty(ForkopNodeParser.Parse("===CLASH_PROXIES===\n{bad"));
        var configOnly = "===SINGBOX_CONFIG===\n{\"outbounds\":[{\"tag\":\"custom\",\"type\":\"urltest\",\"outbounds\":[\"node\"]},{\"tag\":\"node\",\"type\":\"vless\",\"server\":\"example.org\"}]}";
        var nodes = ForkopNodeParser.Parse(configOnly);
        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, n => { Assert.False(n.IsActive); Assert.Null(n.LatencyMs); Assert.Empty(n.SelectionGroupTag); });
    }
    [Fact]
    public async Task DisconnectedServiceReturnsNoSampleNodesSubscriptionsOrLatency()
    {
        using var ssh = new SshService(); var service = new ForkopService(ssh);
        Assert.Empty(await service.GetServersAsync()); Assert.Empty(await service.GetSubscriptionInfoListAsync());
        var existing = new ForkopServerNode { LatencyMs = 123 };
        await service.TestLatenciesAsync(new() { existing }); Assert.Null(existing.LatencyMs);
        Assert.False((await service.SelectActiveServerAsync(existing)).Success);
    }
    [Fact]
    public void LatencyChangesNotifyTheDisplayedValueIncludingTimeout()
    {
        var node = new ForkopServerNode(); var names = new List<string?>();
        node.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        node.LatencyMs = 100; node.LatencyMs = null;
        Assert.Equal(2, names.Count(n => n == nameof(node.LatencyDisplay)));
        Assert.Equal("N/A", node.LatencyDisplay);
    }
}
