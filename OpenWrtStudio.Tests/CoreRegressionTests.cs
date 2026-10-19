using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using OpenWrtStudio.ViewModels;

namespace OpenWrtStudio.Tests;

public class CoreRegressionTests
{
    private const string ReleaseUrl = "https://github.com/RichkovGit/OpenWrtStudio/releases/download/v2.6.0/";

    [Fact]
    public async Task InterfaceFallback_ReportsRealAddressesAndLinkState()
    {
        var ssh = new FakeSsh
        {
            Command = command => command.StartsWith("ubus")
                ? (1, "", "ubus unavailable")
                : (0, "2: br-home: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500 state UP\n" +
                      "3: eth7@if4: <BROADCAST,MULTICAST> mtu 1500 state DOWN\n" +
                      "2: br-home inet 10.37.0.1/23 brd 10.37.1.255 scope global br-home\n", "")
        };
        var interfaces = await new RouterService(ssh).GetInterfacesAsync();
        Assert.Equal(2, interfaces.Count);
        var home = Assert.Single(interfaces, item => item.Name == "br-home");
        Assert.Equal("10.37.0.1", home.IpAddress);
        Assert.Equal("/23", home.Netmask);
        Assert.True(home.IsUp);
        Assert.False(Assert.Single(interfaces, item => item.Name == "eth7").IsUp);
        Assert.DoesNotContain(interfaces, item => item.IpAddress == "192.168.1.1");
    }

    [Fact]
    public async Task IfconfigFallback_ParsesBusyBoxOutput()
    {
        var ssh = new FakeSsh
        {
            Command = command => command.StartsWith("ubus") ? (1, "", "") :
                (0, "br-lan    Link encap:Ethernet HWaddr 00:11:22:33:44:55\n" +
                    "          inet addr:172.20.4.1 Bcast:172.20.4.255 Mask:255.255.255.0\n" +
                    "          UP BROADCAST RUNNING MULTICAST MTU:1500\n", "")
        };
        var iface = Assert.Single(await new RouterService(ssh).GetInterfacesAsync());
        Assert.Equal("172.20.4.1", iface.IpAddress);
        Assert.True(iface.IsUp);
    }

    [Fact]
    public async Task RouterMemory_UsesKernelAvailableMemory()
    {
        var ssh = new FakeSsh
        {
            Command = command => command.Contains("SECTION:BOARD")
                ? (0, "===SECTION:MEMINFO===\nMemTotal: 100000 kB\nMemFree: 1000 kB\n" +
                      "Buffers: 1000 kB\nCached: 1000 kB\nMemAvailable: 40000 kB\n===SECTION:END===\n", "")
                : (0, "{\"interface\":[]}", "")
        };
        var info = await new RouterService(ssh).GetRouterInfoAsync();
        Assert.Equal(60000, info.RamUsedKB);
        Assert.Equal(60, info.RamUsagePercent);
    }

    [Fact]
    public async Task FailedRebootWithEmptyStderr_IsNotReportedSuccessful()
    {
        var ssh = new FakeSsh { Command = _ => (1, "", "") };
        Assert.False((await new RouterService(ssh).RebootAsync()).Success);
        Assert.False(ssh.Disconnected);
    }

    [Theory]
    [InlineData("../network")]
    [InlineData("dnsmasq; reboot")]
    [InlineData("")]
    [InlineData("..")]
    public async Task InvalidServiceName_DoesNotExecuteCommand(string service)
    {
        var ssh = new FakeSsh();
        Assert.False((await new RouterService(ssh).RestartServiceAsync(service)).Success);
        Assert.Empty(ssh.Commands);
    }

    [Fact]
    public async Task MissingPrivateKey_IsRejectedBeforeAnyNetworkConnection()
    {
        using var ssh = new SshService();
        var result = await ssh.TestConnectionAsync(new ConnectionProfile
        {
            Host = "invalid.example", UseKeyAuth = true,
            PrivateKeyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".key")
        });
        Assert.False(result.Success);
        Assert.Contains("ключа", result.Message);
    }

    [Fact]
    public async Task ProfileWrites_AreEncryptedAndRecoverPreviousVersionAfterCorruption()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenWrtStudioTests-" + Guid.NewGuid());
        try
        {
            var path = Path.Combine(directory, "profiles.dat");
            var service = new ProfileService(path);
            await service.SaveProfilesAsync(new() { new() { Name = "Previous", Password = "router-secret" } });
            Assert.DoesNotContain("router-secret", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)));
            Assert.Equal("router-secret", Assert.Single(await service.LoadProfilesAsync()).Password);
            await service.SaveProfilesAsync(new() { new() { Name = "Latest" } });
            Assert.Equal("Latest", Assert.Single(await service.LoadProfilesAsync()).Name);
            await File.WriteAllTextAsync(path, "truncated");
            Assert.Equal("Previous", Assert.Single(await service.LoadProfilesAsync()).Name);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FailedProfileWrite_PropagatesAndDoesNotRaiseSavedEvent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenWrtStudioTests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var service = new ProfileService(directory); // A directory cannot be replaced by a file.
            var saved = false;
            service.ProfilesSaved += (_, _) => saved = true;
            await Assert.ThrowsAnyAsync<IOException>(() => service.SaveProfilesAsync(new() { new() }));
            Assert.False(saved);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Updater_PrefersExecutableEvenWhenZipComesFirst()
    {
        var json = """
            {"tag_name":"v2.6","published_at":null,"assets":[
              {"name":"Portable.zip","browser_download_url":"URLPortable.zip"},
              {"name":"OpenWrtStudio.exe","browser_download_url":"URLOpenWrtStudio.exe"},
              {"name":"Setup.exe","browser_download_url":"https://untrusted.example/Setup.exe"}
            ]}
            """.Replace("URL", ReleaseUrl);
        using var client = new HttpClient(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json) }));
        var info = await new UpdateService(client, new Version(2, 5, 18)).CheckForUpdatesAsync();
        Assert.Equal(new Version(2, 6, 0), info.LatestVersion);
        Assert.Equal("OpenWrtStudio.exe", info.AssetName);
        Assert.True(info.IsUpdateAvailable);
    }

    [Fact]
    public async Task Updater_ApiFailureIsNotReportedAsUpToDate()
    {
        using var client = new HttpClient(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new UpdateService(client, new Version(2, 5, 18)).CheckForUpdatesAsync());
    }

    [Fact]
    public async Task Updater_CancellationPropagates()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var client = new HttpClient(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new UpdateService(client, new Version(2, 5, 18)).CheckForUpdatesAsync(source.Token));
    }

    [Theory]
    [InlineData("http://github.com/RichkovGit/OpenWrtStudio/releases/download/v2.6/Setup.exe")]
    [InlineData("https://github.com/another/repo/releases/download/v2.6/Setup.exe")]
    [InlineData("https://github.com.evil.example/RichkovGit/OpenWrtStudio/releases/download/v2.6/Setup.exe")]
    [InlineData("https://github.com/RichkovGit/OpenWrtStudio/releases/download/v2.6/Setup.cmd")]
    public async Task Updater_RejectsUntrustedDownloadsBeforeNetworkAccess(string url)
    {
        using var client = new HttpClient(new FakeHttp(_ => throw new InvalidOperationException("Network must not be used")));
        await Assert.ThrowsAsync<ArgumentException>(() => new UpdateService(client, new Version(2, 5, 18)).DownloadUpdateAsync(url));
    }

    [Fact]
    public async Task Updater_QueryStringDoesNotBecomeFileExtension_AndFilesAreUnique()
    {
        using var client = new HttpClient(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[] { (byte)'M', (byte)'Z', 1, 2 }) }));
        var service = new UpdateService(client, new Version(2, 5, 18));
        var first = await service.DownloadUpdateAsync(ReleaseUrl + "Setup.exe?download=1");
        string? second = null;
        try
        {
            second = await service.DownloadUpdateAsync(ReleaseUrl + "Setup.exe?download=1");
            Assert.EndsWith(".exe", first);
            Assert.NotEqual(first, second);
            Assert.Equal(new byte[] { (byte)'M', (byte)'Z', 1, 2 }, await File.ReadAllBytesAsync(first));
        }
        finally { File.Delete(first); if (second != null) File.Delete(second); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Updater_RejectsInvalidOrTruncatedFiles_AndDeletesPartialDownload(bool truncated)
    {
        var before = Directory.GetFiles(Path.GetTempPath(), "OpenWrtStudio_Update_*").ToHashSet();
        using var client = new HttpClient(new FakeHttp(_ =>
        {
            var content = new ByteArrayContent(truncated ? new byte[] { (byte)'M', (byte)'Z' } : Encoding.UTF8.GetBytes("<html>error</html>"));
            if (truncated) content.Headers.ContentLength = 100;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateService(client, new Version(2, 5, 18)).DownloadUpdateAsync(ReleaseUrl + "Setup.exe"));
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "OpenWrtStudio_Update_*").Except(before));
    }

    [Fact]
    public async Task Monitor_PollsImmediatelyAndRespectsDisabledSentinel()
    {
        var router = new FakeRouter();
        var sentinel = new FakeSentinel { IsMonitoring = false };
        using var service = new BackgroundMonitorService(new FakeSsh(), router, sentinel, new NotificationService());
        var polled = new TaskCompletionSource<RouterInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.OnRouterStatePolled += info => polled.TrySetResult(info);
        service.StartMonitoring(25);
        Assert.NotNull(await polled.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        service.StopMonitoring();
        Assert.False(service.IsRunning);
        Assert.Equal(0, sentinel.ProbeCount);
    }

    [Fact]
    public async Task Monitor_StopSuppressesResultsOfAnInFlightPoll()
    {
        var router = new FakeRouter { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var service = new BackgroundMonitorService(new FakeSsh(), router, new FakeSentinel(), new NotificationService());
        var polled = false;
        service.OnRouterStatePolled += _ => polled = true;
        service.StartMonitoring();
        await router.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        service.StopMonitoring();
        router.Pending.SetResult(new RouterInfo());
        await Task.Delay(100);
        Assert.False(polled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Monitor_RejectsInvalidPollingIntervals(int interval)
    {
        using var service = new BackgroundMonitorService(new FakeSsh(), new FakeRouter(), new FakeSentinel(), new NotificationService());
        Assert.Throws<ArgumentOutOfRangeException>(() => service.StartMonitoring(interval));
    }

    [Fact]
    public async Task Dashboard_ConcurrentRefreshesUseOnePollAndKeepLoadingUntilItFinishes()
    {
        var router = new FakeRouter { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var dashboard = new DashboardViewModel(router, new FakeSsh(), action => action());
        var first = dashboard.RefreshDashboardAsync();
        await router.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await dashboard.RefreshDashboardAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, router.PollCount);
        Assert.True(dashboard.IsLoading);
        router.Pending.SetResult(new RouterInfo { Model = "Actual router" });
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(dashboard.HasSnapshot);
        Assert.Equal("Actual router", dashboard.Info.Model);
        Assert.False(dashboard.IsLoading);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dashboard_DisconnectDiscardsOldSnapshotEvenAfterReconnecting(bool reconnect)
    {
        var router = new FakeRouter { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var ssh = new FakeSsh();
        var dashboard = new DashboardViewModel(router, ssh, action => action());
        var refresh = dashboard.RefreshDashboardAsync();
        await router.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        ssh.SetConnected(false);
        if (reconnect) ssh.SetConnected(true);
        router.Pending.SetResult(new RouterInfo { Model = "Previous connection", Interfaces = new() { new() { Name = "old-wan" } } });
        await refresh.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(dashboard.HasSnapshot);
        Assert.Empty(dashboard.Interfaces);
        Assert.NotEqual("Previous connection", dashboard.Info.Model);
        Assert.Equal(reconnect, dashboard.IsConnected);
        Assert.False(dashboard.IsLoading);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dashboard_DisconnectDiscardsOldRefreshError(bool reconnect)
    {
        var router = new FakeRouter { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var ssh = new FakeSsh();
        var dashboard = new DashboardViewModel(router, ssh, action => action());
        var refresh = dashboard.RefreshDashboardAsync();
        await router.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        ssh.SetConnected(false);
        if (reconnect) ssh.SetConnected(true);
        router.Pending.SetException(new IOException("Previous connection error"));
        await refresh.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(dashboard.ActionStatus);
        Assert.False(dashboard.HasSnapshot);
        Assert.False(dashboard.IsLoading);
    }

    [Fact]
    public async Task Dashboard_CurrentRefreshErrorIsVisibleAndReleasesLoadingState()
    {
        var router = new FakeRouter { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var dashboard = new DashboardViewModel(router, new FakeSsh(), action => action());
        var refresh = dashboard.RefreshDashboardAsync();
        router.Pending.SetException(new IOException("Router request failed"));
        await refresh.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Contains("Router request failed", dashboard.ActionStatus);
        Assert.False(dashboard.HasSnapshot);
        Assert.False(dashboard.IsLoading);
    }

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(handler(request));
        }
    }

    private sealed class FakeRouter : IRouterService
    {
        public TaskCompletionSource<RouterInfo>? Pending { get; init; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PollCount { get; private set; }
        public Task<RouterInfo> GetRouterInfoAsync() { PollCount++; Started.TrySetResult(true); return Pending?.Task ?? Task.FromResult(new RouterInfo()); }
        public Task<List<NetworkInterfaceItem>> GetInterfacesAsync() => Task.FromResult(new List<NetworkInterfaceItem>());
        public Task<(bool, string)> RebootAsync() => Task.FromResult((true, ""));
        public Task<(bool, string)> RestartNetworkAsync() => Task.FromResult((true, ""));
        public Task<(bool, string)> RestartDnsAsync() => Task.FromResult((true, ""));
        public Task<(bool, string)> FlushDnsCacheAsync() => Task.FromResult((true, ""));
        public Task<(bool, string)> RestartServiceAsync(string name) => Task.FromResult((true, ""));
    }

    private sealed class FakeSentinel : ISentinelService
    {
        public bool IsMonitoring { get; set; } = true;
        public SentinelIssue? CurrentIssue => null;
        public event EventHandler<SentinelIssue?>? IssueChanged { add { } remove { } }
        public int ProbeCount { get; private set; }
        public Task<SentinelIssue?> ProbeHealthAsync() { ProbeCount++; return Task.FromResult<SentinelIssue?>(null); }
        public Task<(bool, string)> ApplyRecoveryMethodAsync(RecoveryMethod method) => Task.FromResult((true, ""));
    }

    private sealed class FakeSsh : ISshService
    {
        public bool IsConnected { get; set; } = true;
        public bool Disconnected { get; private set; }
        public List<string> Commands { get; } = new();
        public Func<string, (int, string, string)> Command { get; init; } = _ => (0, "", "");
        public ConnectionProfile? CurrentProfile => null;
        public event EventHandler<bool>? ConnectionChanged;
        public void SetConnected(bool connected) { IsConnected = connected; ConnectionChanged?.Invoke(this, connected); }
        public Task<(bool, string)> ConnectAsync(ConnectionProfile profile) => Task.FromResult((true, ""));
        public Task DisconnectAsync() { Disconnected = true; IsConnected = false; return Task.CompletedTask; }
        public Task<(int, string, string)> ExecuteCommandAsync(string command, int timeoutSeconds = 30) { Commands.Add(command); return Task.FromResult(Command(command)); }
        public Task RunStreamingCommandAsync(string command, Action<string> onLineReceived, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<(bool, string)> UploadFileContentAsync(string content, string remotePath) => Task.FromResult((true, ""));
        public Task<(bool, string)> DownloadFileContentAsync(string remotePath) => Task.FromResult((true, ""));
        public Task<(bool, byte[]?, string)> DownloadBinaryFileAsync(string remotePath) => Task.FromResult<(bool, byte[]?, string)>((false, null, ""));
        public Task<(bool, string)> UploadBinaryFileAsync(byte[] data, string remotePath) => Task.FromResult((true, ""));
        public Task<(bool, string)> TestConnectionAsync(ConnectionProfile profile) => Task.FromResult((true, ""));
    }
}
