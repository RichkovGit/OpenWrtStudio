using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using OpenWrtStudio.ViewModels;
using Xunit;

namespace OpenWrtStudio.Tests;

public class BugFixRegressionTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Condition was not reached in time.");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task ModemProfile_PasswordContainingEquals_IsNotTruncated()
    {
        var ssh = new ScriptedSsh
        {
            OnCommand = cmd => cmd.StartsWith("uci show network.wwan")
                ? (0, "network.wwan=interface\nnetwork.wwan.proto='qmi'\nnetwork.wwan.device='/dev/cdc-wdm0'\nnetwork.wwan.apn='internet'\nnetwork.wwan.password='p=a=s'", "")
                : (0, "", "")
        };
        var profile = await new UsbConfigService(ssh).GetModemProfileAsync();
        Assert.Equal("qmi", profile.Protocol);
        Assert.Equal("p=a=s", profile.Password);
        Assert.Equal("internet", profile.Apn);
    }

    [Fact]
    public async Task SambaShare_NewSection_IsPackageQualified()
    {
        var ssh = new ScriptedSsh
        {
            OnCommand = cmd => cmd.Contains("which smbd") ? (0, "/usr/sbin/smbd", "") : (0, "", "")
        };
        var result = await new UsbConfigService(ssh).ConfigureSambaShareAsync("/mnt/sda1");
        Assert.True(result.Success);
        var script = ssh.Commands.Last();
        // 'uci add' returns a bare cfg id; without the samba4. prefix every later uci set fails.
        Assert.Contains("SECTION=\"samba4.$(uci add samba4 sambashare)\"", script);
        Assert.DoesNotContain("SECTION=$(uci add samba4 sambashare);", script);
    }

    [Fact]
    public async Task ForkopSection_SubscriptionUrlWithQueryString_IsPreserved()
    {
        const string url = "https://vpn.example/sub?token=abc=def&flag=1";
        var ssh = new ScriptedSsh
        {
            OnCommand = cmd => cmd.StartsWith("uci show forkop")
                ? (0, $"forkop.vpn_proxy=section\nforkop.vpn_proxy.enabled='1'\nforkop.vpn_proxy.sub_url='{url}'\n", "")
                : (0, "", "")
        };
        var section = await new ForkopService(ssh).GetSectionConfigAsync();
        Assert.True(section.IsEnabled);
        Assert.Contains(url, section.SubscriptionUrls);
    }

    [Fact]
    public async Task ForkopUpdateSubscriptions_NonZeroExitWithEmptyStderr_IsFailure()
    {
        var ssh = new ScriptedSsh { OnCommand = _ => (1, "", "") };
        var result = await new ForkopService(ssh).UpdateSubscriptionsAsync();
        Assert.False(result.Success);
    }

    [Fact]
    public async Task AwgPeerLookup_InterfaceNameIsNotAPrefixMatch()
    {
        const string uci =
            "network.awg0=interface\nnetwork.awg0.proto='amneziawg'\nnetwork.awg0.private_key='KEY0'\n" +
            "network.awg01=interface\nnetwork.awg01.proto='amneziawg'\nnetwork.awg01.private_key='KEY01'\n" +
            "network.peerA=wireguard_awg01\nnetwork.peerA.interface='awg01'\nnetwork.peerA.public_key='PUB01'\n" +
            "network.peerB=wireguard_awg0\nnetwork.peerB.interface='awg0'\nnetwork.peerB.public_key='PUB0'\n";
        var ssh = new ScriptedSsh
        {
            OnCommand = cmd => cmd.StartsWith("uci show network") ? (0, uci, "") : (0, "", "")
        };
        var configs = await new VpnProtocolService(ssh).GetAmneziaWgConfigsAsync();
        var awg0 = Assert.Single(configs, c => c.InterfaceName == "awg0");
        var awg01 = Assert.Single(configs, c => c.InterfaceName == "awg01");
        Assert.Equal("PUB0", awg0.PeerPublicKey);
        Assert.Equal("PUB01", awg01.PeerPublicKey);
    }

    [Fact]
    public async Task PackageManager_AfterReconnect_RedetectsManager()
    {
        var pm = "apk";
        var ssh = new ScriptedSsh
        {
            OnCommand = cmd => cmd.Contains("command -v apk") ? (0, pm, "") : (0, "", "")
        };
        var service = new PackageManagerService(ssh);
        Assert.Equal("apk", await service.DetectPackageManagerAsync());
        ssh.SetConnected(false);
        ssh.SetConnected(true);
        pm = "opkg";
        Assert.Equal("opkg", await service.DetectPackageManagerAsync());
        Assert.Equal(2, ssh.Commands.Count(c => c.Contains("command -v apk")));
    }

    private const string ClientBatchMarker = "===SECTION:DHCP===";

    private static string ClientBatch(string dhcpLine) =>
        "===SECTION:WAN_INFO===\n===SECTION:DHCP===\n" + dhcpLine +
        "\n===SECTION:ARP===\n===SECTION:IP_NEIGH===\n===SECTION:STATIC===\n===SECTION:FIREWALL===\n" +
        "===SECTION:WIRELESS_IW===\n===SECTION:WIRELESS_IWINFO===\n===SECTION:HOSTAPD_UBUS===\n===SECTION:END===\n";

    [Fact]
    public async Task ClientCache_Disconnect_DoesNotLeakDevicesIntoNextRouter()
    {
        var dhcp = "1700000000 aa:bb:cc:dd:ee:01 192.168.1.50 OldPhone *";
        var ssh = new ScriptedSsh
        {
            OnCommand = cmd => cmd.Contains(ClientBatchMarker) ? (0, ClientBatch(dhcp), "") : (0, "", "")
        };
        var service = new ClientManagerService(ssh);
        var first = await service.GetClientsAsync();
        Assert.Contains(first, c => c.MacAddress == "aa:bb:cc:dd:ee:01");

        ssh.SetConnected(false);
        ssh.SetConnected(true);
        dhcp = "";
        var second = await service.GetClientsAsync();
        Assert.DoesNotContain(second, c => c.MacAddress == "aa:bb:cc:dd:ee:01");
    }

    [Fact]
    public async Task KickClient_Failure_IsReportedAsFailure()
    {
        var ssh = new ScriptedSsh { OnCommand = _ => (1, "", "hostapd unavailable") };
        var result = await new ClientManagerService(ssh).KickClientAsync("aa:bb:cc:dd:ee:01");
        Assert.False(result.Success);
        Assert.Contains("Ошибка", result.Message);
    }

    [Fact]
    public async Task BlockClient_Failure_IsReportedAsFailure()
    {
        var ssh = new ScriptedSsh { OnCommand = _ => (1, "", "uci failed") };
        var result = await new ClientManagerService(ssh).BlockClientInternetAsync("aa:bb:cc:dd:ee:01", true);
        Assert.False(result.Success);
        Assert.Contains("Ошибка", result.Message);
    }

    [Fact]
    public async Task ClientsViewModel_FailedBlock_DoesNotMarkClientBlocked()
    {
        var vm = new ClientsViewModel(new FailingClientManager(), new ScriptedSsh());
        var client = new NetworkClient { MacAddress = "aa:bb:cc:dd:ee:01", IsBlocked = false };
        await vm.ToggleBlockInternetAsync(client);
        Assert.False(client.IsBlocked);
        Assert.Contains("Ошибка", vm.StatusMessage);
    }

    [Fact]
    public async Task Monitor_RestartWhilePollInFlight_NeverPollsConcurrently()
    {
        var router = new GatedRouter { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var service = new BackgroundMonitorService(new ScriptedSsh(), router, new SentinelStub(), new NotificationService());
        service.StartMonitoring(1);
        await router.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        service.StopMonitoring();
        service.StartMonitoring(1);
        router.Gate.SetResult(new RouterInfo());
        await WaitUntilAsync(() => Volatile.Read(ref router.PollCount) >= 2);
        Assert.Equal(1, router.MaxConcurrent);
        service.StopMonitoring();
    }

    private const string HealthyProbe =
        "===WAN_STATUS===\n{ \"up\": true, \"proto\": \"dhcp\", \"ipv4-address\": [ { \"address\": \"10.0.0.2\", \"mask\": 24 } ] }\n" +
        "===CARRIER===\n1\n===PING===\nping:ok\n===DNS_LOCAL===\nName: ya.ru\nAddress: 77.88.55.242\n" +
        "===DNS_PUBLIC===\nName: ya.ru\nAddress: 77.88.55.242\n===SERVICES===\ndnsmasq:ok\npppd:down\nmihomo:down\nsingbox:down\n===TUN===\ntun:none\n";

    [Fact]
    public async Task SentinelProbe_ConcurrentRequests_DoNotStackOnSsh()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ssh = new ScriptedSsh
        {
            OnCommandAsync = async cmd =>
            {
                if (cmd.Contains("===WAN_STATUS==="))
                {
                    await gate.Task;
                    return (0, HealthyProbe, "");
                }
                return (0, "", "");
            }
        };
        var sentinel = new SentinelService(ssh);
        var first = sentinel.ProbeHealthAsync();
        await WaitUntilAsync(() => ssh.Commands.Count >= 1);
        var second = await sentinel.ProbeHealthAsync().WaitAsync(TimeSpan.FromSeconds(3));
        gate.SetResult(true);
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, ssh.Commands.Count(c => c.Contains("===WAN_STATUS===")));
    }

    [Fact]
    public async Task PortForward_InvalidFields_AreRejectedWithoutRemoteCommand()
    {
        var cases = new[]
        {
            new PortForwardRule { SrcPort = "abc" },
            new PortForwardRule { SrcPort = "0" },
            new PortForwardRule { DestPort = "70000" },
            new PortForwardRule { DestIp = "999.1.1.1" },
            new PortForwardRule { Proto = "tcp; reboot" }
        };
        foreach (var rule in cases)
        {
            var ssh = new ScriptedSsh();
            var result = await new RouterConfigService(ssh).SavePortForwardRuleAsync(rule);
            Assert.False(result.Success);
            Assert.Empty(ssh.Commands);
        }
    }

    private static SingboxQuickConfig ValidVless() => new()
    {
        ServerAddress = "vpn.example", ServerPort = 443, Protocol = "vless", Uuid = "uuid-1", EnableReality = false
    };

    [Fact]
    public async Task SingboxApply_RestartFailure_IsNotReportedAsSuccess()
    {
        var ssh = new ScriptedSsh
        {
            OnCommand = cmd => cmd.Contains("/etc/init.d/sing-box restart") ? (1, "", "init script not found") : (0, "", "")
        };
        var result = await new VpnProtocolService(ssh).ApplySingboxConfigAsync(ValidVless());
        Assert.False(result.Success);
        Assert.Contains("не перезапущена", result.Message);
    }

    [Fact]
    public async Task SingboxApply_InvalidConfig_DoesNotTouchRouter()
    {
        var ssh = new ScriptedSsh();
        var result = await new VpnProtocolService(ssh).ApplySingboxConfigAsync(new SingboxQuickConfig { ServerAddress = "" });
        Assert.False(result.Success);
        Assert.Empty(ssh.Commands);
        Assert.Empty(ssh.Uploads);
    }

    [Theory]
    [InlineData("shadowsocks")]
    [InlineData("trojan")]
    [InlineData("tuic")]
    public async Task SingboxOutbound_ExtendedProtocols_ContainRequiredCredentials(string protocol)
    {
        var ssh = new ScriptedSsh();
        var config = ValidVless();
        config.Protocol = protocol;
        config.Password = "secret-pass";
        config.Uuid = "uuid-1";
        config.Method = "2022-blake3-aes-128-gcm";
        var result = await new VpnProtocolService(ssh).ApplySingboxConfigAsync(config);
        Assert.True(result.Success);
        var (path, json) = Assert.Single(ssh.Uploads);
        Assert.Equal("/etc/sing-box/config.json", path);
        using var doc = JsonDocument.Parse(json);
        var outbound = doc.RootElement.GetProperty("outbounds")[0];
        Assert.Equal(protocol, outbound.GetProperty("type").GetString());
        Assert.Equal("secret-pass", outbound.GetProperty("password").GetString());
        if (protocol == "shadowsocks")
            Assert.Equal("2022-blake3-aes-128-gcm", outbound.GetProperty("method").GetString());
        if (protocol == "trojan")
            Assert.True(outbound.GetProperty("tls").GetProperty("enabled").GetBoolean());
        if (protocol == "tuic")
        {
            Assert.Equal("uuid-1", outbound.GetProperty("uuid").GetString());
            Assert.Equal("bbr", outbound.GetProperty("congestion_control").GetString());
            Assert.Equal("h3", outbound.GetProperty("tls").GetProperty("alpn")[0].GetString());
        }
    }

    [Fact]
    public async Task SingboxApply_MixedInbound_IsHonored()
    {
        var ssh = new ScriptedSsh();
        var config = ValidVless();
        config.InboundType = "mixed";
        config.MixedPort = 2080;
        Assert.True((await new VpnProtocolService(ssh).ApplySingboxConfigAsync(config)).Success);
        using var doc = JsonDocument.Parse(Assert.Single(ssh.Uploads).Content);
        var inbound = doc.RootElement.GetProperty("inbounds")[0];
        Assert.Equal("mixed", inbound.GetProperty("type").GetString());
        Assert.Equal(2080, inbound.GetProperty("listen_port").GetInt32());
    }

    [Fact]
    public async Task StaticLease_NameWithApostrophe_IsNotTruncated()
    {
        var staticUci = "dhcp.@host[0]=host\ndhcp.@host[0].mac='aa:bb:cc:dd:ee:02'\n" +
            "dhcp.@host[0].name='John'\\''s PC'\ndhcp.@host[0].ip='192.168.1.60'\n";
        var ssh = new ScriptedSsh
        {
            OnCommand = cmd => cmd.Contains(ClientBatchMarker)
                ? (0, "===SECTION:WAN_INFO===\n===SECTION:DHCP===\n===SECTION:ARP===\n===SECTION:IP_NEIGH===\n===SECTION:STATIC===\n" +
                      staticUci +
                      "===SECTION:FIREWALL===\n===SECTION:WIRELESS_IW===\n===SECTION:WIRELESS_IWINFO===\n===SECTION:HOSTAPD_UBUS===\n===SECTION:END===\n", "")
                : (0, "", "")
        };
        var clients = await new ClientManagerService(ssh).GetClientsAsync();
        var client = Assert.Single(clients, c => c.MacAddress == "aa:bb:cc:dd:ee:02");
        Assert.Equal("John's PC", client.CustomName);
        Assert.Equal("192.168.1.60", client.IpAddress);
    }

    [Fact]
    public void DecodeUciValue_HandlesQuotedEscapesAndPlainTokens()
    {
        Assert.Equal("John's PC", ShellHelper.DecodeUciValue("'John'\\''s PC'"));
        Assert.Equal("plain", ShellHelper.DecodeUciValue("'plain'"));
        Assert.Equal("unquoted", ShellHelper.DecodeUciValue("unquoted"));
        Assert.Equal("a\\b", ShellHelper.DecodeUciValue("'a\\b'"));
        Assert.Equal("", ShellHelper.DecodeUciValue(""));
    }

    private sealed class FailingClientManager : IClientManagerService
    {
        public Task<List<NetworkClient>> GetClientsAsync() => Task.FromResult(new List<NetworkClient>());
        public Task<(bool Success, string Message)> KickClientAsync(string mac) => Task.FromResult((false, "Ошибка сброса"));
        public Task<(bool Success, string Message)> BlockClientInternetAsync(string mac, bool block) => Task.FromResult((false, "Ошибка блокировки"));
        public Task<(bool Success, string Message)> SetStaticLeaseAsync(string mac, string ip, string name) => Task.FromResult((false, "err"));
        public Task<(bool Success, string Message)> DeleteStaticLeaseAsync(string mac) => Task.FromResult((false, "err"));
    }

    private sealed class SentinelStub : ISentinelService
    {
        public bool IsMonitoring { get; set; } = false;
        public SentinelIssue? CurrentIssue => null;
        public event EventHandler<SentinelIssue?>? IssueChanged { add { } remove { } }
        public Task<SentinelIssue?> ProbeHealthAsync() => Task.FromResult<SentinelIssue?>(null);
        public Task<(bool Success, string Message)> ApplyRecoveryMethodAsync(RecoveryMethod method) => Task.FromResult((true, ""));
        public Task<SentinelFixAllReport> FixAllAsync(SentinelIssue issue, IProgress<SentinelFixProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new SentinelFixAllReport());
    }

    private sealed class GatedRouter : IRouterService
    {
        public TaskCompletionSource<RouterInfo>? Gate { get; init; }
        public TaskCompletionSource<bool> FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PollCount;
        public int Concurrent;
        public int MaxConcurrent;

        public async Task<RouterInfo> GetRouterInfoAsync()
        {
            var current = Interlocked.Increment(ref Concurrent);
            int observed;
            do
            {
                observed = Volatile.Read(ref MaxConcurrent);
                if (current <= observed) break;
            } while (Interlocked.CompareExchange(ref MaxConcurrent, current, observed) != observed);
            Interlocked.Increment(ref PollCount);
            FirstStarted.TrySetResult(true);
            var gate = Gate;
            if (gate != null) await gate.Task;
            Interlocked.Decrement(ref Concurrent);
            return new RouterInfo();
        }

        public Task<List<NetworkInterfaceItem>> GetInterfacesAsync() => Task.FromResult(new List<NetworkInterfaceItem>());
        public Task<(bool, string)> RebootAsync() => Task.FromResult((true, ""));
        public Task<(bool, string)> RestartNetworkAsync() => Task.FromResult((true, ""));
        public Task<(bool, string)> RestartDnsAsync() => Task.FromResult((true, ""));
        public Task<(bool, string)> FlushDnsCacheAsync() => Task.FromResult((true, ""));
        public Task<(bool, string)> RestartServiceAsync(string name) => Task.FromResult((true, ""));
    }
}
