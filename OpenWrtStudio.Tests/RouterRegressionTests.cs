using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;

namespace OpenWrtStudio.Tests;

public class RouterRegressionTests
{
    [Fact]
    public async Task WifiSave_PreservesAnonymousSectionAndQuotesShellMetacharacters()
    {
        var ssh = new RecordingSsh();
        var service = new RouterConfigService(ssh);
        var result = await service.ApplyWifiConfigAsync(new()
        {
            new() { Device = "radio0", Interfaces = new()
            {
                new() { SectionId = "@wifi-iface[0]", Ssid = "Owner's $(touch /tmp/unwanted)", Hidden = true }
            }}
        });

        Assert.True(result.Success);
        var command = Assert.Single(ssh.Commands);
        Assert.StartsWith("set -e", command);
        Assert.Contains("'wireless.@wifi-iface[0].ssid'='Owner'\\''s $(touch /tmp/unwanted)'", command);
        Assert.Contains("'wireless.@wifi-iface[0].hidden'='1'", command);
        Assert.DoesNotContain("wireless.wifi-iface0", command);
    }

    [Fact]
    public async Task WifiLoad_PreservesApostrophesAndHiddenFlag()
    {
        var ssh = new RecordingSsh
        {
            Respond = _ => (0, "wireless.radio0=wifi-device\nwireless.@wifi-iface[0]=wifi-iface\n"
                + "wireless.@wifi-iface[0].device='radio0'\n"
                + "wireless.@wifi-iface[0].ssid='Owner'\\''s Wi-Fi'\n"
                + "wireless.@wifi-iface[0].key='it'\\''s secret'\n"
                + "wireless.@wifi-iface[0].hidden='1'\n", "")
        };

        var radios = await new RouterConfigService(ssh).GetWifiConfigsAsync();
        var iface = Assert.Single(Assert.Single(radios).Interfaces);
        Assert.Equal("Owner's Wi-Fi", iface.Ssid);
        Assert.Equal("it's secret", iface.Key);
        Assert.True(iface.Hidden);
    }

    [Fact]
    public async Task WifiOpenNetwork_RemovesPreviouslySavedPassword()
    {
        var ssh = new RecordingSsh();
        await new RouterConfigService(ssh).ApplyWifiConfigAsync(new()
        {
            new() { Interfaces = new() { new() { Encryption = "none", Key = "old password" } } }
        });
        var command = Assert.Single(ssh.Commands);
        Assert.Contains("uci -q delete 'wireless.default_radio0.key' || true", command);
        Assert.DoesNotContain("old password", command);
    }

    [Theory]
    [InlineData("@redirect[0]")]
    [InlineData("port_forward_1")]
    public async Task PortForwardDelete_UsesOriginalSectionAndStopsOnFailure(string section)
    {
        var ssh = new RecordingSsh { Respond = _ => (1, "", "delete failed") };
        var result = await new RouterConfigService(ssh).DeletePortForwardRuleAsync(section);
        Assert.False(result.Success);
        Assert.Equal($"uci delete 'firewall.{section}' && uci commit firewall && /etc/init.d/firewall restart", Assert.Single(ssh.Commands));
    }

    [Fact]
    public async Task PortForwardSave_PreservesAnonymousSection()
    {
        var ssh = new RecordingSsh();
        var result = await new RouterConfigService(ssh).SavePortForwardRuleAsync(new() { SectionId = "@redirect[2]" });
        Assert.True(result.Success);
        var command = Assert.Single(ssh.Commands);
        Assert.Contains("SEC='@redirect[2]'", command);
        Assert.Contains("uci set \"firewall.$SEC.name\"=", command);
        Assert.DoesNotContain("uci add firewall", command);
    }

    [Fact]
    public async Task PortForwardSave_NewRuleUsesReturnedSectionId()
    {
        var ssh = new RecordingSsh();
        await new RouterConfigService(ssh).SavePortForwardRuleAsync(new());
        Assert.Contains("SEC=$(uci add firewall redirect)", Assert.Single(ssh.Commands));
    }

    [Theory]
    [InlineData("@redirect[0];reboot")]
    [InlineData("@redirect[0].name")]
    [InlineData("")]
    public async Task InvalidPortForwardSection_IsRejectedWithoutRemoteCommand(string section)
    {
        var ssh = new RecordingSsh();
        var result = await new RouterConfigService(ssh).DeletePortForwardRuleAsync(section);
        Assert.False(result.Success);
        Assert.Empty(ssh.Commands);
    }

    [Fact]
    public async Task WanLoad_PreservesStaticSettingsAndDnsLists()
    {
        var ssh = new RecordingSsh { Respond = _ => (0,
            "network.wan.proto='static'\nnetwork.wan.ipaddr='10.0.0.2'\n"
            + "network.wan.netmask='255.255.255.0'\nnetwork.wan.gateway='10.0.0.1'\n"
            + "network.wan.dns='9.9.9.9' '1.1.1.1'\nnetwork.wan.password='owner'\\''s password'\n", "") };
        var config = await new RouterConfigService(ssh).GetWanConfigAsync();
        Assert.Equal("static", config.Proto);
        Assert.Equal("10.0.0.2", config.StaticIp);
        Assert.Equal("255.255.255.0", config.StaticNetmask);
        Assert.Equal("10.0.0.1", config.StaticGateway);
        Assert.Equal("9.9.9.9 1.1.1.1", config.StaticDns);
        Assert.Equal("owner's password", config.PppoePassword);
    }

    [Fact]
    public async Task LanSave_PersistsLeaseEditsAndDeletesAnonymousLeasesInReverse()
    {
        var ssh = new RecordingSsh { Respond = command => command == "uci -X show dhcp"
            ? (0, "dhcp.@host[0]=host\ndhcp.@host[1]=host\ndhcp.@host[2]=host\n", "")
            : (0, "", "") };
        var service = new RouterConfigService(ssh);
        var config = await service.GetLanDhcpConfigAsync();
        config.StaticLeases = new()
        {
            new() { SectionId = "@host[1]", Name = "Kept host", Mac = "00:11:22:33:44:55", Ip = "192.168.1.25" },
            new() { Name = "New host", Mac = "00-11-22-33-44-66", Ip = "192.168.1.26" }
        };
        ssh.Commands.Clear();
        var result = await service.ApplyLanDhcpConfigAsync(config);
        Assert.True(result.Success);
        Assert.Equal(2, ssh.Commands.Count);
        var command = ssh.Commands[1];
        Assert.Contains("SEC='@host[1]'", command);
        Assert.Contains("SEC=$(uci add dhcp host)", command);
        Assert.Contains("uci set \"dhcp.$SEC.mac\"='00:11:22:33:44:66'", command);
        Assert.Contains("uci set \"dhcp.$SEC.ip\"='192.168.1.26'", command);
        Assert.DoesNotContain("uci delete 'dhcp.@host[1]'", command);
        Assert.True(command.IndexOf("uci delete 'dhcp.@host[2]'", StringComparison.Ordinal)
                    < command.IndexOf("uci delete 'dhcp.@host[0]'", StringComparison.Ordinal));
        Assert.Contains("/etc/init.d/network reload", command);
    }

    [Fact]
    public async Task LanSave_FailedReadCannotEraseExistingLeases()
    {
        var failRead = false;
        var ssh = new RecordingSsh { Respond = _ => failRead ? (1, "", "read failed") : (0, "", "") };
        var service = new RouterConfigService(ssh);
        var config = await service.GetLanDhcpConfigAsync();
        ssh.Commands.Clear();
        failRead = true;
        var result = await service.ApplyLanDhcpConfigAsync(config);
        Assert.False(result.Success);
        Assert.Equal("uci -X show dhcp", Assert.Single(ssh.Commands));
    }

    [Fact]
    public async Task LanSave_NewLeaseKeepsGeneratedIdForRepeatedSaves()
    {
        var committed = false;
        const string savedHost = "dhcp.cfgabc123=host\ndhcp.cfgabc123.mac='aa:bb:cc:dd:ee:ff'\n";
        var ssh = new RecordingSsh { Respond = command =>
        {
            if (command == "uci -X show dhcp") return (0, committed ? savedHost : "", "");
            if (command.StartsWith("set -e", StringComparison.Ordinal))
            {
                committed = true;
                return (0, savedHost, "");
            }
            return (0, "", "");
        }};
        var service = new RouterConfigService(ssh);
        var config = await service.GetLanDhcpConfigAsync();
        config.StaticLeases.Add(new());
        ssh.Commands.Clear();
        Assert.True((await service.ApplyLanDhcpConfigAsync(config)).Success);
        Assert.Equal("cfgabc123", config.StaticLeases[0].SectionId);
        Assert.True((await service.ApplyLanDhcpConfigAsync(config)).Success);
        Assert.Contains("SEC='cfgabc123'", ssh.Commands[3]);
        Assert.DoesNotContain("uci add dhcp host", ssh.Commands[3]);
    }

    [Fact]
    public async Task LanSave_RefreshesEditedAnonymousIdsAfterCommit()
    {
        var ssh = new RecordingSsh { Respond = command => command == "uci -X show dhcp"
            ? (0, "dhcp.cfgbefore=host\n", "")
            : (0, "dhcp.cfgafter=host\ndhcp.cfgafter.mac='aa:bb:cc:dd:ee:ff'\n", "") };
        var service = new RouterConfigService(ssh);
        var config = await service.GetLanDhcpConfigAsync();
        config.StaticLeases = new() { new() { SectionId = "cfgbefore" } };
        Assert.True((await service.ApplyLanDhcpConfigAsync(config)).Success);
        Assert.Equal("cfgafter", config.StaticLeases[0].SectionId);
    }

    [Fact]
    public async Task LanSave_StaleLeaseCannotOverwriteAnotherSection()
    {
        var ssh = new RecordingSsh { Respond = _ => (0, "dhcp.@host[0]=host\n", "") };
        var service = new RouterConfigService(ssh);
        var config = await service.GetLanDhcpConfigAsync();
        config.StaticLeases = new() { new() { SectionId = "@host[1]" } };
        ssh.Commands.Clear();
        var result = await service.ApplyLanDhcpConfigAsync(config);
        Assert.False(result.Success);
        Assert.Single(ssh.Commands);
    }

    [Theory]
    [InlineData("uci show network.lan")]
    [InlineData("uci -X show dhcp")]
    public async Task LanLoad_FailurePropagatesAndUnloadedConfigCannotSave(string failedCommand)
    {
        var ssh = new RecordingSsh { Respond = command => command == failedCommand
            ? (1, "", "read failed") : (0, "", "") };
        var service = new RouterConfigService(ssh);
        await Assert.ThrowsAsync<IOException>(() => service.GetLanDhcpConfigAsync());
        var reads = ssh.Commands.Count;
        Assert.False((await service.ApplyLanDhcpConfigAsync(new())).Success);
        Assert.Equal(reads, ssh.Commands.Count);
    }

    [Fact]
    public async Task LanSave_ExternallyAddedHostCannotBeDeletedByOldSnapshot()
    {
        var liveDhcp = "";
        var ssh = new RecordingSsh { Respond = command => (0, command == "uci -X show dhcp" ? liveDhcp : "", "") };
        var service = new RouterConfigService(ssh);
        var config = await service.GetLanDhcpConfigAsync();
        ssh.Commands.Clear();
        liveDhcp = "dhcp.new_external_host=host\n";
        var result = await service.ApplyLanDhcpConfigAsync(config);
        Assert.False(result.Success);
        Assert.Equal("uci -X show dhcp", Assert.Single(ssh.Commands));
    }

    [Fact]
    public async Task LanSave_AllHostsRemovedInLoadedEditorDeletesOnlyKnownHosts()
    {
        var ssh = new RecordingSsh { Respond = command => command == "uci -X show dhcp"
            ? (0, "dhcp.cfgknown=host\n", "") : (0, "", "") };
        var service = new RouterConfigService(ssh);
        var config = await service.GetLanDhcpConfigAsync();
        config.StaticLeases.Clear();
        ssh.Commands.Clear();
        Assert.True((await service.ApplyLanDhcpConfigAsync(config)).Success);
        Assert.Contains("uci delete 'dhcp.cfgknown'", ssh.Commands[1]);
    }

    [Theory]
    [InlineData("awg0; reboot")]
    [InlineData("awg0\nreboot")]
    [InlineData("$(reboot)")]
    [InlineData("1234567890123456")]
    public async Task AmneziaInvalidInterface_IsRejectedWithoutCommand(string iface)
    {
        var ssh = new RecordingSsh();
        var service = new VpnProtocolService(ssh);
        Assert.False((await service.ApplyAmneziaWgConfigAsync(new() { InterfaceName = iface })).Success);
        Assert.False((await service.DeleteAmneziaWgConfigAsync(iface)).Success);
        Assert.False((await service.RestartAmneziaWgAsync(iface)).Success);
        Assert.Empty(ssh.Commands);
    }

    [Fact]
    public async Task AmneziaApply_HandlesIpv6EndpointAndImportedDns()
    {
        var ssh = new RecordingSsh();
        var result = await new VpnProtocolService(ssh).ApplyAmneziaWgConfigAsync(new()
        {
            InterfaceName = "awg0", Endpoint = "[2001:db8::1]:51820", Dns = "1.1.1.1, 2606:4700:4700::1111"
        });
        Assert.True(result.Success);
        var command = Assert.Single(ssh.Commands);
        Assert.StartsWith("set -e", command);
        Assert.Contains("endpoint_host='2001:db8::1'", command);
        Assert.Contains("endpoint_port='51820'", command);
        Assert.Contains("network.awg0.dns='1.1.1.1'", command);
        Assert.Contains("network.awg0.dns='2606:4700:4700::1111'", command);
        Assert.Contains("=(wireguard|amneziawg)_awg0$", command);
        Assert.Contains("uci -X show network", command);
    }

    [Theory]
    [InlineData("vpn.example:0")]
    [InlineData("vpn.example:65536")]
    [InlineData("2001:db8::1:51820")]
    [InlineData("bad host:51820")]
    public async Task AmneziaInvalidEndpoint_IsRejectedWithoutCommand(string endpoint)
    {
        var ssh = new RecordingSsh();
        var result = await new VpnProtocolService(ssh).ApplyAmneziaWgConfigAsync(new() { Endpoint = endpoint });
        Assert.False(result.Success);
        Assert.Empty(ssh.Commands);
    }

    private sealed class RecordingSsh : ISshService
    {
        public bool IsConnected => true;
        public ConnectionProfile? CurrentProfile => null;
        public event EventHandler<bool>? ConnectionChanged { add { } remove { } }
        public List<string> Commands { get; } = new();
        public Func<string, (int ExitCode, string Output, string Error)> Respond { get; init; } = _ => (0, "", "");
        public Task<(int ExitCode, string Output, string Error)> ExecuteCommandAsync(string command, int timeoutSeconds = 30)
        {
            Commands.Add(command);
            return Task.FromResult(Respond(command));
        }
        public Task<(bool Success, string Message)> ConnectAsync(ConnectionProfile profile) => throw new NotSupportedException();
        public Task DisconnectAsync() => throw new NotSupportedException();
        public Task RunStreamingCommandAsync(string command, Action<string> onLineReceived, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> UploadFileContentAsync(string content, string remotePath) => throw new NotSupportedException();
        public Task<(bool Success, string Content)> DownloadFileContentAsync(string remotePath) => throw new NotSupportedException();
        public Task<(bool Success, byte[]? Data, string Error)> DownloadBinaryFileAsync(string remotePath) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> UploadBinaryFileAsync(byte[] data, string remotePath) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile) => throw new NotSupportedException();
    }
}
