using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface IUsbConfigService
{
    Task<List<UsbDeviceItem>> GetUsbDevicesAsync();
    Task<List<UsbDiskPartition>> GetStorageDisksAsync();
    Task<UsbModemProfile> GetModemProfileAsync();
    Task<(bool Success, string Message)> SaveModemConfigAsync(UsbModemProfile profile);
    Task<(bool Success, string Message)> RestartModemInterfaceAsync(string interfaceName = "wwan");
    Task<(bool Success, string Message)> MountDiskAsync(string deviceNode, string target);
    Task<(bool Success, string Message)> UnmountDiskAsync(string mountPointOrDevice);
    Task<(bool Success, string Message)> ConfigureSambaShareAsync(string path, string shareName = "USB_Storage");
    Task<(bool HasApk, bool HasOpkg, bool HasBlockMount, bool HasModemDrivers, bool HasStorageDrivers)> CheckPackagesStatusAsync();
    Task<(bool Success, string Message)> InstallModemPackagesAsync();
    Task<(bool Success, string Message)> InstallStoragePackagesAsync();
}

public class UsbConfigService : IUsbConfigService
{
    private readonly ISshService _ssh;

    public UsbConfigService(ISshService ssh)
    {
        _ssh = ssh;
    }

    public async Task<List<UsbDeviceItem>> GetUsbDevicesAsync()
    {
        var list = new List<UsbDeviceItem>();
        if (!_ssh.IsConnected) return list;

        // 1. Try lsusb
        var (exitCode, output, _) = await _ssh.ExecuteCommandAsync("lsusb 2>/dev/null", 5);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                // Bus 001 Device 002: ID 12d1:1506 Huawei Technologies Co., Ltd. Modem
                var match = Regex.Match(line, @"Bus\s+(\d+)\s+Device\s+(\d+):\s+ID\s+([0-9a-fA-F]{4}):([0-9a-fA-F]{4})\s*(.*)");
                if (match.Success)
                {
                    var vid = match.Groups[3].Value;
                    var pid = match.Groups[4].Value;
                    var desc = match.Groups[5].Value.Trim();
                    var type = "USB Устройство";
                    if (desc.Contains("Modem", StringComparison.OrdinalIgnoreCase) || desc.Contains("Huawei", StringComparison.OrdinalIgnoreCase) || desc.Contains("ZTE", StringComparison.OrdinalIgnoreCase) || desc.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase))
                        type = "Модем";
                    else if (desc.Contains("Storage", StringComparison.OrdinalIgnoreCase) || desc.Contains("Flash", StringComparison.OrdinalIgnoreCase) || desc.Contains("Disk", StringComparison.OrdinalIgnoreCase))
                        type = "Накопитель";

                    list.Add(new UsbDeviceItem
                    {
                        Id = $"{vid}:{pid}",
                        VendorId = vid,
                        ProductId = pid,
                        Name = desc,
                        DeviceType = type,
                        Product = desc,
                        IsSupported = true
                    });
                }
            }
        }

        // 2. Also check /sys/bus/usb/devices/
        if (list.Count == 0)
        {
            var (sysExit, sysOut, _) = await _ssh.ExecuteCommandAsync("for d in /sys/bus/usb/devices/*; do [ -f \"$d/idVendor\" ] && echo \"$(cat $d/idVendor):$(cat $d/idProduct)|$(cat $d/manufacturer 2>/dev/null)|$(cat $d/product 2>/dev/null)\"; done", 5);
            if (sysExit == 0 && !string.IsNullOrWhiteSpace(sysOut))
            {
                var lines = sysOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Trim().Split('|');
                    if (parts.Length >= 3 && parts[0].Contains(':'))
                    {
                        var ids = parts[0].Split(':');
                        var vid = ids[0];
                        var pid = ids.Length > 1 ? ids[1] : "";
                        var man = parts[1].Trim();
                        var prod = parts[2].Trim();
                        var name = $"{man} {prod}".Trim();
                        if (string.IsNullOrEmpty(name)) name = $"USB Device {vid}:{pid}";

                        var type = "USB Устройство";
                        if (name.Contains("Modem", StringComparison.OrdinalIgnoreCase) || name.Contains("LTE", StringComparison.OrdinalIgnoreCase))
                            type = "Модем";
                        else if (name.Contains("Storage", StringComparison.OrdinalIgnoreCase) || name.Contains("Flash", StringComparison.OrdinalIgnoreCase) || name.Contains("Disk", StringComparison.OrdinalIgnoreCase))
                            type = "Накопитель";

                        list.Add(new UsbDeviceItem
                        {
                            Id = $"{vid}:{pid}",
                            VendorId = vid,
                            ProductId = pid,
                            Manufacturer = man,
                            Product = prod,
                            Name = name,
                            DeviceType = type,
                            IsSupported = true
                        });
                    }
                }
            }
        }

        return list;
    }

    public async Task<List<UsbDiskPartition>> GetStorageDisksAsync()
    {
        var list = new List<UsbDiskPartition>();
        if (!_ssh.IsConnected) return list;

        // 1. Get block info / blkid
        var (bExit, bOut, _) = await _ssh.ExecuteCommandAsync("block info 2>/dev/null || blkid 2>/dev/null", 5);
        var partitions = new Dictionary<string, UsbDiskPartition>();

        if (bExit == 0 && !string.IsNullOrWhiteSpace(bOut))
        {
            var lines = bOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                // /dev/sda1: UUID="xxxx" LABEL="USB_DISK" TYPE="ext4"
                var devMatch = Regex.Match(line, @"^(/dev/sd[a-z][0-9]*):");
                if (devMatch.Success)
                {
                    var devNode = devMatch.Groups[1].Value;
                    var fsMatch = Regex.Match(line, @"TYPE=""([^""]+)""");
                    var labelMatch = Regex.Match(line, @"LABEL=""([^""]+)""");
                    var uuidMatch = Regex.Match(line, @"UUID=""([^""]+)""");

                    var part = new UsbDiskPartition
                    {
                        DeviceNode = devNode,
                        FileSystem = fsMatch.Success ? fsMatch.Groups[1].Value : "unknown",
                        Label = labelMatch.Success ? labelMatch.Groups[1].Value : "",
                        Uuid = uuidMatch.Success ? uuidMatch.Groups[1].Value : "",
                        AutoMount = true
                    };
                    partitions[devNode] = part;
                }
            }
        }

        // 2. Query mount points and disk usage via df -h
        var (dfExit, dfOut, _) = await _ssh.ExecuteCommandAsync("df -h", 5);
        if (dfExit == 0 && !string.IsNullOrWhiteSpace(dfOut))
        {
            var lines = dfOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                // /dev/sda1 14.2G 1.2G 13.0G 9% /mnt/sda1
                var m = Regex.Match(line, @"^(/dev/sd[a-z][0-9]*)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\d+)%\s+(.+)");
                if (m.Success)
                {
                    var dev = m.Groups[1].Value;
                    var total = m.Groups[2].Value;
                    var used = m.Groups[3].Value;
                    var free = m.Groups[4].Value;
                    var pct = double.TryParse(m.Groups[5].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0;
                    var mnt = m.Groups[6].Value.Trim();

                    if (!partitions.TryGetValue(dev, out var part))
                    {
                        part = new UsbDiskPartition { DeviceNode = dev };
                        partitions[dev] = part;
                    }

                    part.TotalSize = total;
                    part.UsedSize = used;
                    part.FreeSize = free;
                    part.UsagePercentage = pct;
                    part.MountPoint = mnt;
                    part.IsMounted = true;
                }
            }
        }

        // 3. Check samba shares
        var (sambaExit, sambaOut, _) = await _ssh.ExecuteCommandAsync("cat /etc/config/samba4 2>/dev/null", 5);
        if (sambaExit == 0 && !string.IsNullOrWhiteSpace(sambaOut))
        {
            foreach (var part in partitions.Values)
            {
                if (!string.IsNullOrEmpty(part.MountPoint) && sambaOut.Contains(part.MountPoint))
                {
                    part.SambaShared = true;
                }
            }
        }

        list.AddRange(partitions.Values);
        return list;
    }

    public async Task<UsbModemProfile> GetModemProfileAsync()
    {
        var profile = new UsbModemProfile();
        if (!_ssh.IsConnected) return profile;

        // 1. Read network config for interface wwan or modem
        var (exitCode, output, _) = await _ssh.ExecuteCommandAsync("uci show network.wwan 2>/dev/null", 5);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var kv = line.Trim().Split('=');
                if (kv.Length == 2)
                {
                    var key = kv[0].Replace("network.wwan.", "").Trim();
                    var val = kv[1].Trim('\'', '"', ' ');

                    switch (key)
                    {
                        case "proto": profile.Protocol = val; break;
                        case "device": profile.DeviceNode = val; break;
                        case "apn": profile.Apn = val; break;
                        case "pincode": profile.PinCode = val; break;
                        case "auth": profile.AuthType = val; break;
                        case "username": profile.Username = val; break;
                        case "password": profile.Password = val; break;
                        case "pdptype": profile.PdpType = val; break;
                    }
                }
            }
        }

        // 2. Check if wwan interface is up via ubus
        var (uExit, uOut, _) = await _ssh.ExecuteCommandAsync("ubus call network.interface.wwan status 2>/dev/null", 5);
        if (uExit == 0 && !string.IsNullOrWhiteSpace(uOut))
        {
            profile.IsUp = uOut.Contains("\"up\": true");
            profile.Status = profile.IsUp ? "Подключено (Онлайн)" : "Интерфейс остановлен";

            var ipMatch = Regex.Match(uOut, @"""address"":\s*""([^""]+)""");
            if (ipMatch.Success)
            {
                profile.IpAddress = ipMatch.Groups[1].Value;
            }
        }
        else
        {
            profile.Status = "Интерфейс не настроен в системе";
        }

        // 3. Query signal if uqmi or umbim available
        if (profile.Protocol == "qmi")
        {
            var (sigExit, sigOut, _) = await _ssh.ExecuteCommandAsync($"uqmi -d {profile.DeviceNode} --get-signal-info 2>/dev/null", 5);
            if (sigExit == 0 && !string.IsNullOrWhiteSpace(sigOut))
            {
                var rssiMatch = Regex.Match(sigOut, @"""rssi"":\s*(-?\d+)");
                if (rssiMatch.Success)
                {
                    profile.SignalStrength = $"{rssiMatch.Groups[1].Value} dBm";
                }
            }
        }

        return profile;
    }

    public async Task<(bool Success, string Message)> SaveModemConfigAsync(UsbModemProfile profile)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var cmd = $"uci set network.{profile.InterfaceName}=interface && " +
                  $"uci set network.{profile.InterfaceName}.proto='{profile.Protocol}' && " +
                  $"uci set network.{profile.InterfaceName}.device='{profile.DeviceNode}' && " +
                  $"uci set network.{profile.InterfaceName}.apn='{profile.Apn}' && " +
                  $"uci set network.{profile.InterfaceName}.pdptype='{profile.PdpType}' && " +
                  $"uci set network.{profile.InterfaceName}.auth='{profile.AuthType}' && ";

        if (!string.IsNullOrEmpty(profile.PinCode))
            cmd += $"uci set network.{profile.InterfaceName}.pincode='{profile.PinCode}' && ";
        if (!string.IsNullOrEmpty(profile.Username))
            cmd += $"uci set network.{profile.InterfaceName}.username='{profile.Username}' && ";
        if (!string.IsNullOrEmpty(profile.Password))
            cmd += $"uci set network.{profile.InterfaceName}.password='{profile.Password}' && ";

        cmd += "uci commit network";

        var (exitCode, _, error) = await _ssh.ExecuteCommandAsync(cmd, 10);
        if (exitCode != 0)
        {
            return (false, $"Ошибка сохранения: {error}");
        }

        return (true, "Конфигурация модема успешно сохранена");
    }

    public async Task<(bool Success, string Message)> RestartModemInterfaceAsync(string interfaceName = "wwan")
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var cmd = $"ifdown {interfaceName} 2>/dev/null; sleep 1; ifup {interfaceName} 2>/dev/null || /etc/init.d/network reload";
        var (exitCode, outMsg, errMsg) = await _ssh.ExecuteCommandAsync(cmd, 15);
        if (exitCode == 0)
        {
            return (true, "Интерфейс модема перезапущен");
        }
        return (false, $"Ошибка перезапуска интерфейса: {errMsg}");
    }

    public async Task<(bool Success, string Message)> MountDiskAsync(string deviceNode, string target)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var cmd = $"mkdir -p '{target}' && mount '{deviceNode}' '{target}'";
        var (exitCode, _, errMsg) = await _ssh.ExecuteCommandAsync(cmd, 10);
        if (exitCode == 0)
        {
            // Also update /etc/config/fstab for automount
            await _ssh.ExecuteCommandAsync("block detect > /etc/config/fstab 2>/dev/null", 5);
            return (true, $"Диск {deviceNode} успешно смонтирован в {target}");
        }
        return (false, $"Не удалось смонтировать: {errMsg}");
    }

    public async Task<(bool Success, string Message)> UnmountDiskAsync(string mountPointOrDevice)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var (exitCode, _, errMsg) = await _ssh.ExecuteCommandAsync($"umount '{mountPointOrDevice}'", 10);
        if (exitCode == 0)
        {
            return (true, $"Диск {mountPointOrDevice} успешно размонтирован");
        }
        return (false, $"Ошибка размонтирования: {errMsg}");
    }

    public async Task<(bool Success, string Message)> ConfigureSambaShareAsync(string path, string shareName = "USB_Storage")
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var checkCmd = "which smbd 2>/dev/null || which samba-tool 2>/dev/null || [ -f /etc/init.d/samba4 ]";
        var (checkExit, _, _) = await _ssh.ExecuteCommandAsync(checkCmd, 5);
        if (checkExit != 0)
        {
            return (false, "Пакет Samba4 не установлен. Установите пакеты общего доступа через кнопку ниже.");
        }

        var cmd = "uci add samba4 sambashare >/dev/null 2>&1 || true && " +
                  $"uci set samba4.@sambashare[-1].name='{shareName}' && " +
                  $"uci set samba4.@sambashare[-1].path='{path}' && " +
                  "uci set samba4.@sambashare[-1].read_only='no' && " +
                  "uci set samba4.@sambashare[-1].guest_ok='yes' && " +
                  "uci set samba4.@sambashare[-1].create_mask='0666' && " +
                  "uci set samba4.@sambashare[-1].dir_mask='0777' && " +
                  "uci commit samba4 && /etc/init.d/samba4 restart 2>/dev/null";

        var (exitCode, _, errMsg) = await _ssh.ExecuteCommandAsync(cmd, 15);
        if (exitCode == 0)
        {
            return (true, $"Сетевой доступ Samba настроен для {path} (Имя: {shareName})");
        }
        return (false, $"Ошибка настройки Samba: {errMsg}");
    }

    public async Task<(bool HasApk, bool HasOpkg, bool HasBlockMount, bool HasModemDrivers, bool HasStorageDrivers)> CheckPackagesStatusAsync()
    {
        if (!_ssh.IsConnected) return (false, false, false, false, false);

        var (exitCode, output, _) = await _ssh.ExecuteCommandAsync(
            "echo \"APK:$(which apk 2>/dev/null)\"; " +
            "echo \"OPKG:$(which opkg 2>/dev/null)\"; " +
            "echo \"BLOCK:$(which block 2>/dev/null)\"; " +
            "echo \"MODEM:$(which uqmi 2>/dev/null || which umbim 2>/dev/null)\"; " +
            "echo \"STORAGE:$(lsmod | grep -E 'usb_storage|uas' 2>/dev/null)\"",
            5);

        bool hasApk = output.Contains("APK:/");
        bool hasOpkg = output.Contains("OPKG:/");
        bool hasBlock = output.Contains("BLOCK:/");
        bool hasModem = output.Contains("MODEM:/");
        bool hasStorage = output.Contains("STORAGE:usb_storage") || output.Contains("STORAGE:uas") || hasBlock;

        return (hasApk, hasOpkg, hasBlock, hasModem, hasStorage);
    }

    public async Task<(bool Success, string Message)> InstallModemPackagesAsync()
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var status = await CheckPackagesStatusAsync();
        string cmd;
        if (status.HasApk)
        {
            cmd = "apk update && apk add kmod-usb-net-qmi-wwan uqmi kmod-usb-net-cdc-mbim umbim kmod-usb-net-rndis kmod-usb-serial-option usb-modeswitch";
        }
        else if (status.HasOpkg)
        {
            cmd = "opkg update && opkg install kmod-usb-net-qmi-wwan uqmi kmod-usb-net-cdc-mbim umbim kmod-usb-net-rndis kmod-usb-serial-option usb-modeswitch";
        }
        else
        {
            return (false, "Не найден поддерживаемый пакетный менеджер (apk или opkg)");
        }

        var (exitCode, outMsg, errMsg) = await _ssh.ExecuteCommandAsync(cmd, 60);
        if (exitCode == 0)
        {
            return (true, "Пакеты драйверов USB-модемов успешно установлены");
        }
        return (false, $"Ошибка установки пакетов: {errMsg}\n{outMsg}");
    }

    public async Task<(bool Success, string Message)> InstallStoragePackagesAsync()
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var status = await CheckPackagesStatusAsync();
        string cmd;
        if (status.HasApk)
        {
            cmd = "apk update && apk add block-mount kmod-usb-storage kmod-fs-ext4 kmod-fs-ntfs3 kmod-fs-vfat e2fsprogs samba4-server";
        }
        else if (status.HasOpkg)
        {
            cmd = "opkg update && opkg install block-mount kmod-usb-storage kmod-fs-ext4 kmod-fs-ntfs3 kmod-fs-vfat e2fsprogs samba4-server";
        }
        else
        {
            return (false, "Не найден поддерживаемый пакетный менеджер (apk или opkg)");
        }

        var (exitCode, outMsg, errMsg) = await _ssh.ExecuteCommandAsync(cmd, 60);
        if (exitCode == 0)
        {
            return (true, "Пакеты для USB-дисков и файлового хранилища успешно установлены");
        }
        return (false, $"Ошибка установки пакетов: {errMsg}\n{outMsg}");
    }
}
