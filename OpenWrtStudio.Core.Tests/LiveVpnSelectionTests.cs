using System.Text.Json;
using System.IO;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;

namespace OpenWrtStudio.Core.Tests;

public sealed class LiveVpnSelectionFactAttribute : FactAttribute
{
    public LiveVpnSelectionFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OPENWRT_LIVE_VPN_SWITCH") != "1" ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENWRT_TEST_HOST")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENWRT_TEST_PASSWORD")))
            Skip = "Explicitly opt in with OPENWRT_LIVE_VPN_SWITCH=1 and router credentials; changes live VPN selection temporarily.";
    }
}

public class LiveVpnSelectionTests
{
    [LiveVpnSelectionFact]
    [Trait("Category", "LiveVpnSwitch")]
    public async Task PolicyAndManualNodeAreConfirmedByIndependentReadAndOriginalSelectionIsRestored()
    {
        using var ssh = new SshService();
        var connection = await ssh.ConnectAsync(new ConnectionProfile
        {
            Host = Environment.GetEnvironmentVariable("OPENWRT_TEST_HOST")!,
            Username = Environment.GetEnvironmentVariable("OPENWRT_TEST_USER") ?? "root",
            Password = Environment.GetEnvironmentVariable("OPENWRT_TEST_PASSWORD")!,
            Port = int.TryParse(Environment.GetEnvironmentVariable("OPENWRT_TEST_PORT"), out var port) ? port : 22
        });
        Assert.True(connection.Success, connection.Message);
        async Task<Dictionary<string, string>> ReadSelectors()
        {
            var (code, output, _) = await ssh.ExecuteCommandAsync("/usr/bin/forkop clash_api get_proxies", 15);
            Assert.Equal(0, code);
            using var doc = JsonDocument.Parse(output);
            return doc.RootElement.GetProperty("proxies").EnumerateObject()
                .Where(p => p.Value.TryGetProperty("type", out var t) && t.GetString() == "Selector")
                .ToDictionary(p => p.Name, p => p.Value.GetProperty("now").GetString()!, StringComparer.Ordinal);
        }
        var original = await ReadSelectors();
        Assert.NotEmpty(original);
        var theme = await ssh.ExecuteCommandAsync("uci -q get luci.main.mediaurlbase", 5);
        Assert.Equal(0, theme.ExitCode);
        var report = new List<object>();
        try
        {
            var service = new ForkopService(ssh);
            var nodes = await service.GetServersAsync();
            Assert.DoesNotContain(nodes, n => new[] { "GLOBAL", "direct-out", "bypass-out", "REJECT" }.Contains(n.GroupTag));
            var root = nodes.Single(n => n.GroupTag == "main-out");
            Assert.True(root.HasLiveState);
            var policy = nodes.First(n => n.Protocol == "URLTest" && n.SelectionGroupTag == root.GroupTag && n.GroupTag != root.CurrentNode);
            var manual = nodes.First(n => !n.IsAutoGroup && n.SelectionGroupTag == root.GroupTag && n.GroupTag != root.CurrentNode);
            foreach (var target in new[] { policy, manual })
            {
                var result = await service.SelectActiveServerAsync(target);
                Assert.True(result.Success, result.Message);
                var actual = await ReadSelectors();
                Assert.Equal(target.GroupTag, actual[target.SelectionGroupTag]);
                var refreshed = await service.GetServersAsync();
                Assert.Contains(refreshed, n => n.GroupTag == target.GroupTag && n.IsOnActivePath && n.HasLiveState);
                report.Add(new { Kind = target.IsAutoGroup ? "automatic-policy" : "manual-node", ConfirmedByRouter = true });
            }
            var beforeInvalid = await ReadSelectors();
            Assert.False((await service.SelectActiveServerAsync(new ForkopServerNode { GroupTag = "studio-nonexistent-test-node" })).Success);
            var afterInvalid = await ReadSelectors();
            Assert.Equal(beforeInvalid.OrderBy(p => p.Key), afterInvalid.OrderBy(p => p.Key));
            report.Add(new { Kind = "invalid-target", RejectedWithoutChangingSelectors = true });
        }
        finally
        {
            foreach (var pair in original.OrderBy(p => p.Key == "main-out" ? 1 : 0))
            {
                var result = await ssh.ExecuteCommandAsync($"/usr/bin/forkop clash_api set_group_proxy {ShellHelper.EscapeArg(pair.Key)} {ShellHelper.EscapeArg(pair.Value)}", 15);
                Assert.Equal(0, result.ExitCode);
            }
            var restored = await ReadSelectors();
            Assert.Equal(original.OrderBy(p => p.Key), restored.OrderBy(p => p.Key));
            var finalTheme = await ssh.ExecuteCommandAsync("uci -q get luci.main.mediaurlbase", 5);
            Assert.Equal(theme.Output.Trim(), finalTheme.Output.Trim());
            report.Add(new { OriginalSelectorsRestored = true, OriginalThemeUnchanged = true });
            var path = Environment.GetEnvironmentVariable("OPENWRT_VPN_REPORT_PATH");
            if (!string.IsNullOrEmpty(path))
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            await ssh.DisconnectAsync();
        }
    }
}
