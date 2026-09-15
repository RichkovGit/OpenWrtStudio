using System;
using System.Collections.Generic;

namespace OpenWrtStudio.Models;

public class UsbDeviceItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string VendorId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Product { get; set; } = "";
    public string DevicePath { get; set; } = "";
    public string DeviceType { get; set; } = "Unknown"; // Modem, Storage, Network, Other
    public bool IsSupported { get; set; } = true;
}

public class UsbDiskPartition
{
    public string DeviceNode { get; set; } = ""; // e.g. /dev/sda1
    public string MountPoint { get; set; } = ""; // e.g. /mnt/sda1
    public string FileSystem { get; set; } = ""; // ext4, ntfs, vfat, exfat
    public string Label { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string TotalSize { get; set; } = "";
    public string UsedSize { get; set; } = "";
    public string FreeSize { get; set; } = "";
    public double UsagePercentage { get; set; } = 0.0;
    public bool IsMounted { get; set; } = false;
    public bool AutoMount { get; set; } = true;
    public bool SambaShared { get; set; } = false;
}

public class UsbModemProfile
{
    public string InterfaceName { get; set; } = "wwan";
    public string Protocol { get; set; } = "qmi"; // qmi, mbim, ncm, rndis, 3g
    public string DeviceNode { get; set; } = "/dev/cdc-wdm0";
    public string Apn { get; set; } = "internet";
    public string PinCode { get; set; } = "";
    public string AuthType { get; set; } = "none"; // none, pap, chap, both
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string PdpType { get; set; } = "ipv4"; // ipv4, ipv6, ipv4v6
    public string Status { get; set; } = "Не подключено";
    public string SignalStrength { get; set; } = "—";
    public string OperatorName { get; set; } = "—";
    public string IpAddress { get; set; } = "—";
    public bool IsUp { get; set; } = false;
}

public class RouterDiscoveryResult
{
    public string IpAddress { get; set; } = "";
    public string Hostname { get; set; } = "OpenWrt";
    public string Model { get; set; } = "Generic OpenWrt";
    public string FirmwareVersion { get; set; } = "";
    public int Port { get; set; } = 22;
    public bool SshAccessible { get; set; }
    public bool HttpAccessible { get; set; }
    public long LatencyMs { get; set; }
    public bool AuthSucceeded { get; set; }
    public string StatusMessage { get; set; } = "";
}
