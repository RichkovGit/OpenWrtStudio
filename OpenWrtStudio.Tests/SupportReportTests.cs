using OpenWrtStudio.Models;
using OpenWrtStudio.Services;

namespace OpenWrtStudio.Tests;

public class SupportReportTests
{
    [Fact]
    public void ReportIncludesResourcesWithoutNetworkIdentifiers()
    {
        var info = new RouterInfo
        {
            Model = "Router test", Hostname = "private-household", OpenWrtRelease = "24.10",
            RamTotalKB = 256000, RamUsedKB = 64000,
            Interfaces = new() { new() { IpAddress = "203.0.113.18", MacAddress = "AA:BB:CC:DD:EE:FF", Name = "secret-network", IsUp = true } }
        };
        var report = SupportReportService.Build(info, new Version(2, 5, 19), DateTimeOffset.UnixEpoch);
        Assert.Contains("2.5.19", report);
        Assert.Contains("64000 / 256000; 25.0%", report);
        Assert.Contains("Активных интерфейсов: 1", report);
        Assert.DoesNotContain(info.Hostname, report);
        Assert.DoesNotContain(info.Interfaces[0].IpAddress, report);
        Assert.DoesNotContain(info.Interfaces[0].MacAddress, report);
        Assert.DoesNotContain(info.Interfaces[0].Name, report);
    }
}
