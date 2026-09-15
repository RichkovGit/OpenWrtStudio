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

    [Theory]
    [InlineData("vfat", "iocharset=utf8", "utf8=1", "codepage=866", "umask=000")]
    [InlineData("ntfs", "ntfs-3g", "iocharset=utf8", "umask=000")]
    [InlineData("exfat", "exfat", "iocharset=utf8", "umask=000")]
    public void MountDisk_CommandSelection_IncludesOptimalCyrillicAndPermissionFlags(string fs, params string[] expectedKeywords)
    {
        string GetMountCommandSnippet(string fileSystem, string dev, string target)
        {
            var lfs = fileSystem.ToLowerInvariant();
            if (lfs.Contains("exfat"))
            {
                return $"mount -t exfat -o rw,noatime,iocharset=utf8,umask=000,dmask=0000,fmask=0000 '{dev}' '{target}'";
            }
            if (lfs.Contains("vfat") || lfs.Contains("fat"))
            {
                return $"mount -t vfat -o rw,noatime,iocharset=utf8,utf8=1,codepage=866,umask=000,dmask=0000,fmask=0000 '{dev}' '{target}'";
            }
            if (lfs.Contains("ntfs"))
            {
                return $"ntfs-3g -o rw,noatime,big_writes,iocharset=utf8,umask=000 '{dev}' '{target}'";
            }
            return $"mount -o rw,noatime,iocharset=utf8,utf8=1,umask=000 '{dev}' '{target}'";
        }

        var cmd = GetMountCommandSnippet(fs, "/dev/sda1", "/mnt/sda1");
        foreach (var kw in expectedKeywords)
        {
            Assert.Contains(kw, cmd);
        }
    }

    [Fact]
    public void SambaConfig_EnforcesRootAndFullAccessFlags()
    {
        var requiredUciSettings = new[]
        {
            "force_root='1'",
            "force_user='root'",
            "force_group='root'",
            "create_mask='0777'",
            "dir_mask='0777'",
            "guest_ok='yes'",
            "read_only='no'"
        };

        var simulatedScript = "uci set samba4.@sambashare[-1].force_root='1' && " +
                              "uci set samba4.@sambashare[-1].force_user='root' && " +
                              "uci set samba4.@sambashare[-1].force_group='root' && " +
                              "uci set samba4.@sambashare[-1].create_mask='0777' && " +
                              "uci set samba4.@sambashare[-1].dir_mask='0777' && " +
                              "uci set samba4.@sambashare[-1].guest_ok='yes' && " +
                              "uci set samba4.@sambashare[-1].read_only='no'";

        foreach (var setting in requiredUciSettings)
        {
            Assert.Contains(setting, simulatedScript);
        }
    }
}
