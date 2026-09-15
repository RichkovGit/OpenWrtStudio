using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;

namespace OpenWrtStudio.Tests;

public class UpdateServiceTests
{
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;
        private readonly HttpStatusCode _statusCode;

        public FakeHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseContent = responseContent;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task CheckForUpdatesAsync_NewerVersionAvailable_SetsIsUpdateAvailableTrue()
    {
        const string fakeJson = "{\"tag_name\":\"v2.6.0\",\"name\":\"OpenWrt Studio v2.6.0\",\"body\":\"### New Features\\n- Added new cool features\",\"published_at\":\"2026-10-01T12:00:00Z\",\"assets\":[{\"name\":\"OpenWrtStudio_Setup_v2.6.exe\",\"browser_download_url\":\"https://github.com/RichkovGit/OpenWrtStudio/releases/download/v2.6.0/OpenWrtStudio_Setup_v2.6.exe\",\"size\":76543210},{\"name\":\"OpenWrtStudio_v2.6_Portable.zip\",\"browser_download_url\":\"https://github.com/RichkovGit/OpenWrtStudio/releases/download/v2.6.0/OpenWrtStudio_v2.6_Portable.zip\",\"size\":75000000}]}";

        var handler = new FakeHttpMessageHandler(fakeJson);
        var client = new HttpClient(handler);
        var currentVersion = new Version(2, 5, 0);
        var service = new UpdateService(client, currentVersion);

        var updateInfo = await service.CheckForUpdatesAsync();

        Assert.True(updateInfo.IsUpdateAvailable);
        Assert.Equal(new Version(2, 6, 0), updateInfo.LatestVersion);
        Assert.Equal("v2.6.0", updateInfo.TagName);
        Assert.Equal("OpenWrt Studio v2.6.0", updateInfo.Title);
        Assert.Contains("Setup", updateInfo.AssetName);
        Assert.Equal("https://github.com/RichkovGit/OpenWrtStudio/releases/download/v2.6.0/OpenWrtStudio_Setup_v2.6.exe", updateInfo.DownloadUrl);
        Assert.True(updateInfo.FileSizeBytes > 0);
        Assert.Contains("МБ", updateInfo.FormattedSize);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_SameOrOlderVersion_SetsIsUpdateAvailableFalse()
    {
        const string fakeJson = "{\"tag_name\":\"v2.5.0\",\"name\":\"OpenWrt Studio v2.5.0\",\"body\":\"Initial 2.5 release\",\"assets\":[]}";

        var handler = new FakeHttpMessageHandler(fakeJson);
        var client = new HttpClient(handler);
        var currentVersion = new Version(2, 5, 0);
        var service = new UpdateService(client, currentVersion);

        var updateInfo = await service.CheckForUpdatesAsync();

        Assert.False(updateInfo.IsUpdateAvailable);
        Assert.Equal(new Version(2, 5, 0), updateInfo.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ApiNotFound_ReturnsCurrentVersionGracefully()
    {
        var handler = new FakeHttpMessageHandler("", HttpStatusCode.NotFound);
        var client = new HttpClient(handler);
        var currentVersion = new Version(2, 5, 0);
        var service = new UpdateService(client, currentVersion);

        var updateInfo = await service.CheckForUpdatesAsync();

        Assert.False(updateInfo.IsUpdateAvailable);
        Assert.Equal(new Version(2, 5, 0), updateInfo.LatestVersion);
    }

    [Fact]
    public void UpdateInfo_FormatsFileSizesCorrectly()
    {
        var info1 = new UpdateInfo { FileSizeBytes = 1048576 * 50 };
        Assert.Contains("50", info1.FormattedSize);
        Assert.Contains("МБ", info1.FormattedSize);

        var info3 = new UpdateInfo { FileSizeBytes = 0 };
        Assert.Equal("", info3.FormattedSize);
    }
}
