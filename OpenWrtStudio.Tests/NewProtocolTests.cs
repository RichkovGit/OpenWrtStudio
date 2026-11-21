using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;
using YamlDotNet.Serialization;

namespace OpenWrtStudio.Tests;

public class NewProtocolTests
{
    // ---------- Shadowsocks / SS-2022 ----------

    [Fact]
    public void ShadowsocksConfig_JsonContainsServerCredentialsAndLocals()
    {
        var config = new ShadowsocksConfig
        {
            ServerAddress = "ss.example", ServerPort = 8388,
            Method = "2022-blake3-aes-256-gcm", Password = "base64key",
            LocalAddress = "0.0.0.0", SocksPort = 1080, HttpPort = 3128, EnableHttp = true
        };
        using var doc = JsonDocument.Parse(VpnProtocolService.GenerateShadowsocksConfigJson(config));
        var root = doc.RootElement;
        Assert.Equal("ss.example", root.GetProperty("server").GetString());
        Assert.Equal(8388, root.GetProperty("server_port").GetInt32());
        Assert.Equal("2022-blake3-aes-256-gcm", root.GetProperty("method").GetString());
        Assert.Equal("base64key", root.GetProperty("password").GetString());
        var locals = root.GetProperty("locals");
        Assert.Equal(2, locals.GetArrayLength());
        Assert.Equal("socks", locals[0].GetProperty("protocol").GetString());
        Assert.Equal(1080, locals[0].GetProperty("local_port").GetInt32());
        Assert.Equal("http", locals[1].GetProperty("protocol").GetString());
        Assert.Equal(3128, locals[1].GetProperty("local_port").GetInt32());
    }

    [Fact]
    public void ShadowsocksConfig_HttpDisabled_OnlySocksLocalRemains()
    {
        var config = new ShadowsocksConfig { ServerAddress = "ss.example", Password = "p", EnableHttp = false };
        using var doc = JsonDocument.Parse(VpnProtocolService.GenerateShadowsocksConfigJson(config));
        Assert.Equal(1, doc.RootElement.GetProperty("locals").GetArrayLength());
    }

    [Fact]
    public async Task ShadowsocksApply_CreatesInitScriptWhenPackageShipsBinaryOnly()
    {
        var ssh = new ScriptedSsh
        {
            OnCommand = cmd =>
            {
                if (cmd.Contains("[ -f /etc/init.d/shadowsocks-rust ]")) return (0, "missing", "");
                if (cmd.StartsWith("pidof sslocal")) return (0, "4242", "");
                return (0, "", "");
            }
        };
        var config = new ShadowsocksConfig { ServerAddress = "ss.example", Password = "p" };
        var result = await new VpnProtocolService(ssh).ApplyShadowsocksConfigAsync(config);
        Assert.True(result.Success);
        Assert.Contains("4242", result.Message);
        Assert.Contains(ssh.Uploads, u => u.Path == "/etc/shadowsocks-rust/config.json");
        var init = Assert.Single(ssh.Uploads, u => u.Path == "/etc/init.d/shadowsocks-rust");
        Assert.Contains("#!/bin/sh /etc/rc.common", init.Content);
        Assert.Contains("procd_set_param command /usr/bin/sslocal -c /etc/shadowsocks-rust/config.json", init.Content);
        Assert.Contains(ssh.Commands, c => c.Contains("chmod +x /etc/init.d/shadowsocks-rust"));
        Assert.Contains(ssh.Commands, c => c.Contains("/etc/init.d/shadowsocks-rust restart"));
    }

    [Fact]
    public async Task ShadowsocksApply_InvalidFields_DoNotTouchRouter()
    {
        var cases = new[]
        {
            new ShadowsocksConfig(), // empty server
            new ShadowsocksConfig { ServerAddress = "s", Password = "p", ServerPort = 0 },
            new ShadowsocksConfig { ServerAddress = "s", Password = "" },
            new ShadowsocksConfig { ServerAddress = "s", Password = "p", Method = "" },
            new ShadowsocksConfig { ServerAddress = "s", Password = "p", SocksPort = 70000 }
        };
        foreach (var config in cases)
        {
            var ssh = new ScriptedSsh();
            var result = await new VpnProtocolService(ssh).ApplyShadowsocksConfigAsync(config);
            Assert.False(result.Success);
            Assert.Empty(ssh.Commands);
            Assert.Empty(ssh.Uploads);
        }
    }

    // ---------- Hysteria2 ----------

    [Fact]
    public void Hysteria2Config_YamlIsWellFormedAndComplete()
    {
        var config = new Hysteria2Config
        {
            ServerAddress = "hy.example", ServerPort = 443, Password = "secret",
            Sni = "sni.example", Insecure = false, UpMbps = 50, DownMbps = 200,
            SocksPort = 1080, HttpPort = 8080, EnableHttp = true
        };
        var yaml = VpnProtocolService.GenerateHysteria2ConfigYaml(config);
        var root = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object>>(yaml);
        Assert.Equal("hy.example:443", root["server"]);
        Assert.Equal("secret", root["auth"]);
        var tls = (Dictionary<object, object>)root["tls"];
        Assert.Equal("sni.example", tls["sni"]);
        Assert.Equal("false", tls["insecure"]);
        var bandwidth = (Dictionary<object, object>)root["bandwidth"];
        Assert.Equal("50 mbps", bandwidth["up"]);
        Assert.Equal("200 mbps", bandwidth["down"]);
        var socks = (Dictionary<object, object>)root["socks5"];
        Assert.Equal("0.0.0.0:1080", socks["listen"]);
        var http = (Dictionary<object, object>)root["http"];
        Assert.Equal("0.0.0.0:8080", http["listen"]);
    }

    [Fact]
    public void Hysteria2Config_SpecialCharactersInPassword_SurviveYamlRoundtrip()
    {
        const string password = "a\"b\\c: #d";
        var config = new Hysteria2Config { ServerAddress = "hy.example", Password = password, Sni = "" };
        var yaml = VpnProtocolService.GenerateHysteria2ConfigYaml(config);
        var root = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object>>(yaml);
        Assert.Equal(password, root["auth"]);
        var tls = (Dictionary<object, object>)root["tls"];
        Assert.False(tls.ContainsKey("sni")); // empty SNI must be omitted, not blank
    }

    [Fact]
    public async Task Hysteria2Apply_KeepsExistingInitScript()
    {
        var ssh = new ScriptedSsh
        {
            OnCommand = cmd =>
            {
                if (cmd.Contains("[ -f /etc/init.d/hysteria ]")) return (0, "exists", "");
                if (cmd.StartsWith("pidof hysteria")) return (0, "777", "");
                return (0, "", "");
            }
        };
        var config = new Hysteria2Config { ServerAddress = "hy.example", Password = "secret" };
        var result = await new VpnProtocolService(ssh).ApplyHysteria2ConfigAsync(config);
        Assert.True(result.Success);
        var upload = Assert.Single(ssh.Uploads);
        Assert.Equal("/etc/hysteria/config.yaml", upload.Path);
        Assert.Contains(ssh.Commands, c => c.Contains("/etc/init.d/hysteria restart"));
    }

    [Fact]
    public async Task Hysteria2Apply_InvalidFields_DoNotTouchRouter()
    {
        var cases = new[]
        {
            new Hysteria2Config(),
            new Hysteria2Config { ServerAddress = "s", Password = "" },
            new Hysteria2Config { ServerAddress = "s", Password = "p", UpMbps = 0 },
            new Hysteria2Config { ServerAddress = "s", Password = "p", ServerPort = 65536 }
        };
        foreach (var config in cases)
        {
            var ssh = new ScriptedSsh();
            Assert.False((await new VpnProtocolService(ssh).ApplyHysteria2ConfigAsync(config)).Success);
            Assert.Empty(ssh.Commands);
            Assert.Empty(ssh.Uploads);
        }
    }

    // ---------- TUIC v5 ----------

    [Fact]
    public void TuicConfig_JsonMatchesTuicClientV5Schema()
    {
        var config = new TuicConfig
        {
            ServerAddress = "tuic.example", ServerPort = 443,
            Uuid = "00000000-0000-0000-0000-000000000001", Password = "secret",
            CongestionControl = "bbr", UdpRelayMode = "native", LocalPort = 1080
        };
        using var doc = JsonDocument.Parse(VpnProtocolService.GenerateTuicConfigJson(config));
        var relay = doc.RootElement.GetProperty("relay");
        Assert.Equal("tuic.example:443", relay.GetProperty("server").GetString());
        Assert.Equal(config.Uuid, relay.GetProperty("uuid").GetString());
        Assert.Equal("secret", relay.GetProperty("password").GetString());
        Assert.Equal("native", relay.GetProperty("udp_relay_mode").GetString());
        Assert.Equal("bbr", relay.GetProperty("congestion_control").GetString());
        Assert.Equal("h3", relay.GetProperty("alpn")[0].GetString());
        Assert.False(relay.GetProperty("disable_sni").GetBoolean());
        Assert.False(relay.TryGetProperty("ip", out _)); // no override -> no ip field (deny_unknown_fields)
        Assert.Equal("0.0.0.0:1080", doc.RootElement.GetProperty("local").GetProperty("server").GetString());
        Assert.Equal("info", doc.RootElement.GetProperty("log_level").GetString());
    }

    [Fact]
    public void TuicConfig_IpOverride_IsIncludedWhenSet()
    {
        var config = new TuicConfig { ServerAddress = "t", Uuid = "u", Password = "p", IpOverride = "192.0.2.10" };
        using var doc = JsonDocument.Parse(VpnProtocolService.GenerateTuicConfigJson(config));
        Assert.Equal("192.0.2.10", doc.RootElement.GetProperty("relay").GetProperty("ip").GetString());
    }

    [Fact]
    public async Task TuicApply_RestartFailure_IsReportedAsFailure()
    {
        var ssh = new ScriptedSsh
        {
            OnCommand = cmd => cmd.Contains("/etc/init.d/tuic-client restart") ? (1, "", "no init") : (0, "exists", "")
        };
        var config = new TuicConfig { ServerAddress = "tuic.example", Uuid = "u", Password = "p" };
        var result = await new VpnProtocolService(ssh).ApplyTuicConfigAsync(config);
        Assert.False(result.Success);
        Assert.Contains(ssh.Uploads, u => u.Path == "/etc/tuic/config.json");
    }

    [Fact]
    public async Task TuicApply_ProcessMissingAfterRestart_IsNotSuccess()
    {
        var ssh = new ScriptedSsh
        {
            OnCommand = cmd =>
            {
                if (cmd.Contains("[ -f /etc/init.d/tuic-client ]")) return (0, "exists", "");
                if (cmd.StartsWith("pidof tuic-client")) return (0, "", "");
                return (0, "", "");
            }
        };
        var config = new TuicConfig { ServerAddress = "tuic.example", Uuid = "u", Password = "p" };
        var result = await new VpnProtocolService(ssh).ApplyTuicConfigAsync(config);
        Assert.False(result.Success);
        Assert.Contains("не найден", result.Message);
    }

    [Fact]
    public async Task TuicApply_InvalidFields_DoNotTouchRouter()
    {
        var cases = new[]
        {
            new TuicConfig(),
            new TuicConfig { ServerAddress = "s", Uuid = "u", Password = "" },
            new TuicConfig { ServerAddress = "s", Uuid = "", Password = "p" },
            new TuicConfig { ServerAddress = "s", Uuid = "u", Password = "p", CongestionControl = "reno" },
            new TuicConfig { ServerAddress = "s", Uuid = "u", Password = "p", IpOverride = "not-an-ip" }
        };
        foreach (var config in cases)
        {
            var ssh = new ScriptedSsh();
            Assert.False((await new VpnProtocolService(ssh).ApplyTuicConfigAsync(config)).Success);
            Assert.Empty(ssh.Commands);
            Assert.Empty(ssh.Uploads);
        }
    }

    // ---------- Capability detection ----------

    private const string ScanWithNewProtocols =
        "===MODEL===\nTest Router\n===OS_INFO===\nDISTRIB_DESCRIPTION='OpenWrt 24.10'\n===KERNEL===\n6.6.0\n===ARCH===\nx86_64\n" +
        "===PACKAGES===\nshadowsocks-rust-sslocal - 1.24.0\nhysteria - 2.12.3\ntuic-client - 1.0.0\n" +
        "===BINARIES===\nsslocal:/usr/bin/sslocal\nhysteria:/usr/bin/hysteria\ntuic-client:/usr/bin/tuic-client\n" +
        "===INIT_SERVICES===\nshadowsocks-rust\nhysteria\ntuic-client\n" +
        "===KERNEL_MODULES===\n===CONFIGS===\nshadowsocks-rust\nhysteria\ntuic\n" +
        "===RUNNING===\n 4242 root 0:00 /usr/bin/sslocal -c /etc/shadowsocks-rust/config.json\n 777 root 0:00 /usr/bin/hysteria client --config /etc/hysteria/config.yaml\n 888 root 0:00 /usr/bin/tuic-client -c /etc/tuic/config.json\n" +
        "===INTERFACES===\n";

    [Fact]
    public async Task CapabilityScan_DetectsStandaloneProtocols()
    {
        var ssh = new ScriptedSsh { OnCommand = _ => (0, ScanWithNewProtocols, "") };
        var report = await new RouterCapabilityService(ssh).ScanCapabilitiesAsync();

        var ss = Assert.Single(report.Protocols, p => p.Type == VpnProtocolType.Shadowsocks);
        var hy = Assert.Single(report.Protocols, p => p.Type == VpnProtocolType.Hysteria2);
        var tuic = Assert.Single(report.Protocols, p => p.Type == VpnProtocolType.Tuic);
        Assert.True(ss.IsInstalled); Assert.True(ss.IsRunning);
        Assert.True(hy.IsInstalled); Assert.True(hy.IsRunning);
        Assert.True(tuic.IsInstalled); Assert.True(tuic.IsRunning);
        Assert.Equal("1.24.0", ss.Version);
        Assert.Equal("2.12.3", hy.Version);
        Assert.Equal("1.0.0", tuic.Version);
        // The scan script must actually look for the new binaries.
        Assert.Contains("sslocal", ssh.Commands[0]);
        Assert.Contains("hysteria", ssh.Commands[0]);
        Assert.Contains("tuic-client", ssh.Commands[0]);
    }

    [Fact]
    public async Task CapabilityScan_EmptyRouter_DoesNotInventProtocols()
    {
        const string empty =
            "===MODEL===\nTest Router\n===OS_INFO===\n===KERNEL===\n===ARCH===\n===PACKAGES===\n" +
            "===BINARIES===\n===INIT_SERVICES===\n===KERNEL_MODULES===\n===CONFIGS===\n===RUNNING===\n===INTERFACES===\n";
        var ssh = new ScriptedSsh { OnCommand = _ => (0, empty, "") };
        var report = await new RouterCapabilityService(ssh).ScanCapabilitiesAsync();
        Assert.All(report.Protocols.Where(p => p.Type is VpnProtocolType.Shadowsocks or VpnProtocolType.Hysteria2 or VpnProtocolType.Tuic),
            p => { Assert.False(p.IsInstalled); Assert.False(p.IsRunning); });
    }

    [Theory]
    [InlineData(VpnProtocolType.Shadowsocks, "sslocal")]
    [InlineData(VpnProtocolType.Hysteria2, "hysteria")]
    [InlineData(VpnProtocolType.Tuic, "tuic-client")]
    public async Task ProtocolStatus_NewProtocols_UsePidofChecks(VpnProtocolType type, string binary)
    {
        var ssh = new ScriptedSsh { OnCommand = _ => (0, "running", "") };
        var (ok, status) = await new VpnProtocolService(ssh).GetProtocolStatusAsync(type);
        Assert.True(ok);
        Assert.Equal("running", status);
        Assert.Contains($"pidof {binary}", Assert.Single(ssh.Commands));
    }
}
