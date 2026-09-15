using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Xunit;

namespace OpenWrtStudio.Tests;

public class UsbAndDiscoveryTests
{
    [Fact]
    public async Task GetCandidateGatewayIps_IncludesCommonOpenWrtIps()
    {
        var service = new RouterDiscoveryService(null!);
        var ips = await service.GetCandidateGatewayIpsAsync();

        Assert.NotNull(ips);
        Assert.Contains("192.168.1.1", ips);
        Assert.Contains("192.168.10.1", ips);
        Assert.Contains("192.168.0.1", ips);
        Assert.Contains("openwrt.lan", ips);
    }

    [Fact]
    public void UsbModemProfile_DefaultValues_AreConsistent()
    {
        var modem = new UsbModemProfile();
        Assert.Equal("wwan", modem.InterfaceName);
        Assert.Equal("qmi", modem.Protocol);
        Assert.Equal("/dev/cdc-wdm0", modem.DeviceNode);
        Assert.Equal("internet", modem.Apn);
        Assert.Equal("none", modem.AuthType);
        Assert.Equal("ipv4", modem.PdpType);
        Assert.False(modem.IsUp);
    }

    [Fact]
    public void UsbDiskPartition_ParsingSimulatedBlockInfo()
    {
        const string sampleBlock = @"/dev/sda1: UUID=""4e21c3fa-6c7b"" LABEL=""USB_DATA"" TYPE=""ext4""
/dev/sdb1: UUID=""ABCD-1234"" LABEL=""FLASH"" TYPE=""vfat""";

        var partitions = new List<UsbDiskPartition>();
        var lines = sampleBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var devMatch = Regex.Match(line, @"^(/dev/sd[a-z][0-9]*):");
            if (devMatch.Success)
            {
                var fsMatch = Regex.Match(line, @"TYPE=""([^""]+)""");
                var labelMatch = Regex.Match(line, @"LABEL=""([^""]+)""");
                var uuidMatch = Regex.Match(line, @"UUID=""([^""]+)""");

                partitions.Add(new UsbDiskPartition
                {
                    DeviceNode = devMatch.Groups[1].Value,
                    FileSystem = fsMatch.Success ? fsMatch.Groups[1].Value : "unknown",
                    Label = labelMatch.Success ? labelMatch.Groups[1].Value : "",
                    Uuid = uuidMatch.Success ? uuidMatch.Groups[1].Value : "",
                    AutoMount = true
                });
            }
        }

        Assert.Equal(2, partitions.Count);
        Assert.Equal("/dev/sda1", partitions[0].DeviceNode);
        Assert.Equal("ext4", partitions[0].FileSystem);
        Assert.Equal("USB_DATA", partitions[0].Label);
        Assert.Equal("/dev/sdb1", partitions[1].DeviceNode);
        Assert.Equal("vfat", partitions[1].FileSystem);
    }

    [Fact]
    public void UsbDevice_ClassificationLogic_WorksCorrectly()
    {
        var sampleModemDesc = "Huawei Technologies Co., Ltd. E3372 LTE/UMTS/GSM HiLink Modem/Networkcard";
        var sampleFlashDesc = "SanDisk Corp. Ultra Flair Flash Drive Storage";

        string Classify(string desc)
        {
            if (desc.Contains("Modem", StringComparison.OrdinalIgnoreCase) || desc.Contains("LTE", StringComparison.OrdinalIgnoreCase))
                return "Модем";
            if (desc.Contains("Storage", StringComparison.OrdinalIgnoreCase) || desc.Contains("Flash", StringComparison.OrdinalIgnoreCase))
                return "Накопитель";
            return "Другое";
        }

        Assert.Equal("Модем", Classify(sampleModemDesc));
        Assert.Equal("Накопитель", Classify(sampleFlashDesc));
    }
}
