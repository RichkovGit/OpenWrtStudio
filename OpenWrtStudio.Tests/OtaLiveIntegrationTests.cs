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
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"token {token}");
        }

        var v250 = new Version(2, 5, 0);
        var updateService = new UpdateService(httpClient, v250);

        var info = await updateService.CheckForUpdatesAsync();

        Assert.NotNull(info);
        Assert.StartsWith("v2.5.", info.TagName);
        Assert.True(info.IsUpdateAvailable, "Latest release must be recognized as newer than v2.5.0");
        Assert.NotNull(info.DownloadUrl);
        Assert.True(info.FileSizeBytes > 50_000_000, "Setup file size must be > 50 MB");
        Assert.NotEmpty(info.Changelog);
    }

    [Fact]
    public async Task Ota_LiveGitHub_Reports_UpToDate_For_FutureVersion()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenWrtStudio-OtaTest/3.0.0");
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"token {token}");
        }

        var vFuture = new Version(99, 0, 0);
        var updateService = new UpdateService(httpClient, vFuture);

        var info = await updateService.CheckForUpdatesAsync();

        Assert.NotNull(info);
        Assert.True(info.TagName.StartsWith("v2.5.") || info.TagName == "v99.0.0");
        Assert.False(info.IsUpdateAvailable, "v99.0.0 should be recognized as already up to date");
    }

    [Fact]
    public async Task Ota_DownloadStream_CanDownloadUpdateFile()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenWrtStudio-OtaTest/2.5.0");
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"token {token}");
        }

        var updateService = new UpdateService(httpClient, new Version(2, 5, 0));
        var info = await updateService.CheckForUpdatesAsync();

        Assert.NotNull(info.DownloadUrl);

        // Test downloading the first 64 KB using a clean download client (AWS S3 rejects GitHub Auth header)
        using var downloadClient = new HttpClient();
        downloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenWrtStudio-Downloader/2.5.0");
        var req = new HttpRequestMessage(HttpMethod.Get, info.DownloadUrl);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1024 * 64); // 64 KB
        var resp = await downloadClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        Assert.True(resp.IsSuccessStatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var buffer = new byte[2];
        var read = await stream.ReadAsync(buffer.AsMemory(0, 2));
        Assert.Equal(2, read);
        // Verify PE Header "MZ"
        Assert.Equal((byte)'M', buffer[0]);
        Assert.Equal((byte)'Z', buffer[1]);
    }
}
