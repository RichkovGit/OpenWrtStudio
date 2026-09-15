using System.Linq;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;

namespace OpenWrtStudio.Tests;

public class SentinelAnalysisTests
{
    [Fact]
    public void CarrierDown_IdentifiedAsProviderPhysicalIssue()
    {
        var probeOutput = @"
===WAN_STATUS===
{ ""up"": false, ""proto"": ""dhcp"" }
===CARRIER===
0
===DNS_LOCAL===
nslookup: can't resolve 'ya.ru'
===DNS_PUBLIC===
nslookup: can't resolve 'ya.ru'
===SERVICES===
dnsmasq:ok
pppd:down
mihomo:ok
";

        // Use reflection to call private static AnalyzeProbeOutput
        var method = typeof(SentinelService).GetMethod("AnalyzeProbeOutput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var issue = method.Invoke(null, new object[] { probeOutput }) as SentinelIssue;
        Assert.NotNull(issue);
        Assert.True(issue.IsProviderIssue);
        Assert.Contains("Carrier Down", issue.Title);
        Assert.NotEmpty(issue.RecoveryMethods);
    }

    [Fact]
    public void LocalDnsFailureWithPublicDnsAlive_IdentifiedAsRouterIssue()
    {
        var probeOutput = @"
===WAN_STATUS===
{ ""up"": true, ""proto"": ""pppoe"" }
===CARRIER===
1
===DNS_LOCAL===
nslookup: can't resolve 'ya.ru'
===DNS_PUBLIC===
Name: ya.ru
Address: 77.88.55.242
===SERVICES===
dnsmasq:down
pppd:ok
mihomo:ok
";

        var method = typeof(SentinelService).GetMethod("AnalyzeProbeOutput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var issue = method.Invoke(null, new object[] { probeOutput }) as SentinelIssue;
        Assert.NotNull(issue);
        Assert.False(issue.IsProviderIssue); // Router issue, not provider!
        Assert.Contains("dnsmasq", issue.Title, System.StringComparison.OrdinalIgnoreCase);

        // Verify that multiple recovery methods are provided to the user
        Assert.True(issue.RecoveryMethods.Count >= 3);
        Assert.Contains(issue.RecoveryMethods, m => m.Command.Contains("dnsmasq restart"));
        Assert.Contains(issue.RecoveryMethods, m => m.Command.Contains("1.1.1.1"));
    }

    [Fact]
    public void PppoeSessionDown_IdentifiedAsPppoeIssue()
    {
        var probeOutput = @"
===WAN_STATUS===
{ ""up"": false, ""proto"": ""pppoe"" }
===CARRIER===
1
===DNS_LOCAL===
nslookup: can't resolve 'ya.ru'
===DNS_PUBLIC===
nslookup: can't resolve 'ya.ru'
===SERVICES===
dnsmasq:ok
pppd:down
mihomo:ok
";

        var method = typeof(SentinelService).GetMethod("AnalyzeProbeOutput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var issue = method.Invoke(null, new object[] { probeOutput }) as SentinelIssue;
        Assert.NotNull(issue);
        Assert.True(issue.IsProviderIssue);
        Assert.Contains("PPPoE", issue.Title);
        Assert.Contains(issue.RecoveryMethods, m => m.Command.Contains("ifdown wan; sleep 2; ifup wan"));
    }

    [Fact]
    public void HealthyPppoeConnection_ReturnsNull()
    {
        var probeOutput = @"
===WAN_STATUS===
{
	""up"": true,
	""pending"": false,
	""available"": true,
	""proto"": ""pppoe"",
	""ipv4-address"": [
		{
			""address"": ""178.211.170.222"",
			""mask"": 32
		}
	]
}
===CARRIER===
1
===PING===
ping:ok
===DNS_LOCAL===
Name: ya.ru
Address: 77.88.55.242
===DNS_PUBLIC===
Name: ya.ru
Address: 77.88.55.242
===SERVICES===
dnsmasq:ok
pppd:ok
mihomo:down
singbox:ok
===TUN===
tun:none
";

        var method = typeof(SentinelService).GetMethod("AnalyzeProbeOutput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var issue = method.Invoke(null, new object[] { probeOutput }) as SentinelIssue;
        // MUST BE NULL because connection is completely healthy!
        Assert.Null(issue);
    }

    [Fact]
    public async System.Threading.Tasks.Task LiveProbeTest()
    {
        var ssh = new SshService();
        var conn = await ssh.ConnectAsync(new ConnectionProfile
        {
            Host = "192.168.10.1",
            Port = 22,
            Username = "root",
            Password = "danik05092005"
        });
        if (!conn.Success) return;
        var sentinel = new SentinelService(ssh);
        var issue = await sentinel.ProbeHealthAsync();
        // On user's live router where internet is working, issue MUST be null!
        Assert.Null(issue);
        await ssh.DisconnectAsync();
    }
}
