using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using OpenWrtStudio.Services;
using Xunit;

namespace OpenWrtStudio.Tests;

public class OtaLiveIntegrationTests
{
    [Fact]
    public async Task Ota_LiveGitHub_Reports_v2_5_1_AsAvailableUpdate_For_v2_5_0()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenWrtStudio-OtaTest/2.5.0");

        var v250 = new Version(2, 5, 0);
        var updateService = new UpdateService(httpClient, v250);

        var info = await updateService.CheckForUpdatesAsync();

        Assert.NotNull(info);
        Assert.Equal("v2.5.1", info.TagName);
        Assert.True(info.IsUpdateAvailable, "v2.5.1 must be recognized as newer than v2.5.0");
        Assert.NotNull(info.DownloadUrl);
        Assert.Contains("OpenWrtStudio_Setup_v2.5.1.exe", info.DownloadUrl);
        Assert.True(info.FileSizeBytes > 50_000_000, "Setup file size must be > 50 MB");
        Assert.NotEmpty(info.Changelog);
    }

    [Fact]
    public async Task Ota_LiveGitHub_Reports_UpToDate_For_v2_5_1()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenWrtStudio-OtaTest/2.5.1");

        var v251 = new Version(2, 5, 1);
        var updateService = new UpdateService(httpClient, v251);

        var info = await updateService.CheckForUpdatesAsync();

        Assert.NotNull(info);
        Assert.Equal("v2.5.1", info.TagName);
        Assert.False(info.IsUpdateAvailable, "v2.5.1 should be recognized as already up to date");
    }

    [Fact]
    public async Task Ota_DownloadStream_CanDownloadUpdateFile()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenWrtStudio-OtaTest/2.5.0");

        var updateService = new UpdateService(httpClient, new Version(2, 5, 0));
        var info = await updateService.CheckForUpdatesAsync();

        Assert.NotNull(info.DownloadUrl);

        // Test downloading the first 100 KB to verify connectivity and stream reading
        var req = new HttpRequestMessage(HttpMethod.Get, info.DownloadUrl);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1024 * 64); // 64 KB
        var resp = await httpClient.SendAsync(req);
        Assert.True(resp.IsSuccessStatusCode);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
        // Verify PE Header "MZ"
        Assert.Equal((byte)'M', bytes[0]);
        Assert.Equal((byte)'Z', bytes[1]);
    }
}
