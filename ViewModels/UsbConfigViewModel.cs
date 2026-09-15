using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;

namespace OpenWrtStudio.ViewModels;

public partial class UsbConfigViewModel : ObservableObject
{
    private readonly IUsbConfigService _usbService;
    private readonly ISshService _ssh;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasPhysicalUsb = false;

    [ObservableProperty]
    private bool _hasModemDrivers = false;

    [ObservableProperty]
    private bool _hasStorageDrivers = false;

    [ObservableProperty]
    private bool _hasBlockMount = false;

    [ObservableProperty]
    private ObservableCollection<UsbDeviceItem> _usbDevices = new();

    [ObservableProperty]
    private ObservableCollection<UsbDiskPartition> _disks = new();

    [ObservableProperty]
    private UsbModemProfile _modem = new();

    [ObservableProperty]
    private List<string> _availableProtocols = new() { "qmi", "mbim", "ncm", "rndis", "3g" };

    [ObservableProperty]
    private List<string> _availableAuthTypes = new() { "none", "pap", "chap" };

    [ObservableProperty]
    private List<string> _availablePdpTypes = new() { "ipv4", "ipv6", "ipv4v6" };

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    public UsbConfigViewModel(IUsbConfigService usbService, ISshService ssh)
    {
        _usbService = usbService;
        _ssh = ssh;
        _ssh.ConnectionChanged += async (_, isConn) =>
        {
            if (isConn) await LoadDataAsync();
            else ResetData();
        };
    }

    public async Task InitializeAsync()
    {
        if (_ssh.IsConnected)
        {
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    private void ResetData()
    {
        UsbDevices.Clear();
        Disks.Clear();
        Modem = new UsbModemProfile();
        HasPhysicalUsb = false;
        StatusMessage = "Подключитесь к роутеру для настройки USB устройств";
    }

    public async Task LoadDataAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Роутер не подключен";
            return;
        }

        IsLoading = true;
        StatusMessage = "Сканирование USB шины и дисков...";

        try
        {
            // 1. Check packages status
            var (hasApk, hasOpkg, hasBlock, hasModem, hasStorage) = await _usbService.CheckPackagesStatusAsync();
            HasBlockMount = hasBlock;
            HasModemDrivers = hasModem;
            HasStorageDrivers = hasStorage;

            // 2. Load USB hardware devices
            var devices = await _usbService.GetUsbDevicesAsync();
            UsbDevices = new ObservableCollection<UsbDeviceItem>(devices);
            HasPhysicalUsb = devices.Count > 0;

            // 3. Load Disks and Partitions
            var disks = await _usbService.GetStorageDisksAsync();
            Disks = new ObservableCollection<UsbDiskPartition>(disks);

            // 4. Load Modem profile
            var modemProfile = await _usbService.GetModemProfileAsync();
            Modem = modemProfile;

            if (HasPhysicalUsb)
            {
                StatusMessage = $"Обнаружено {devices.Count} USB-устройств, {disks.Count} разделов накопителей";
            }
            else
            {
                StatusMessage = "Физические USB-устройства не обнаружены (роутер без USB или порт свободен)";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сканирования: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SaveModemConfigAsync()
    {
        IsLoading = true;
        StatusMessage = "Сохранение настроек модема в /etc/config/network...";
        try
        {
            var (success, msg) = await _usbService.SaveModemConfigAsync(Modem);
            StatusMessage = msg;
            if (success)
            {
                await LoadDataAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RestartModemAsync()
    {
        IsLoading = true;
        StatusMessage = "Перезапуск интерфейса модема...";
        try
        {
            var (success, msg) = await _usbService.RestartModemInterfaceAsync(Modem.InterfaceName);
            StatusMessage = msg;
            await Task.Delay(1500);
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task MountDiskAsync(UsbDiskPartition? disk)
    {
        if (disk == null) return;
        IsLoading = true;
        var target = string.IsNullOrWhiteSpace(disk.MountPoint) ? $"/mnt/{disk.DeviceNode.Replace("/dev/", "")}" : disk.MountPoint;
        StatusMessage = $"Монтирование {disk.DeviceNode} в {target}...";
        try
        {
            var (success, msg) = await _usbService.MountDiskAsync(disk.DeviceNode, target, disk.FileSystem);
            StatusMessage = msg;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task UnmountDiskAsync(UsbDiskPartition? disk)
    {
        if (disk == null) return;
        IsLoading = true;
        var target = !string.IsNullOrEmpty(disk.MountPoint) ? disk.MountPoint : disk.DeviceNode;
        StatusMessage = $"Размонтирование {target}...";
        try
        {
            var (success, msg) = await _usbService.UnmountDiskAsync(target);
            StatusMessage = msg;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task EnableSambaAsync(UsbDiskPartition? disk)
    {
        if (disk == null || string.IsNullOrWhiteSpace(disk.MountPoint))
        {
            StatusMessage = "Сначала смонтируйте накопитель для общего доступа";
            return;
        }

        IsLoading = true;
        StatusMessage = $"Настройка общего доступа Samba для {disk.MountPoint}...";
        try
        {
            var shareName = !string.IsNullOrEmpty(disk.Label) ? disk.Label : "USB_Storage";
            var (success, msg) = await _usbService.ConfigureSambaShareAsync(disk.MountPoint, shareName);
            StatusMessage = msg;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task InstallModemDriversAsync()
    {
        IsLoading = true;
        StatusMessage = "Установка пакетов для USB-модемов (uqmi, mbim, modeswitch)...";
        try
        {
            var (success, msg) = await _usbService.InstallModemPackagesAsync();
            StatusMessage = msg;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task InstallStorageDriversAsync()
    {
        IsLoading = true;
        StatusMessage = "Установка пакетов для USB-накопителей (block-mount, kmod-usb-storage, ext4, ntfs, samba)...";
        try
        {
            var (success, msg) = await _usbService.InstallStoragePackagesAsync();
            StatusMessage = msg;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SetupHotplugAutomountAsync()
    {
        IsLoading = true;
        StatusMessage = "Настройка скрипта горячего подключения /etc/hotplug.d/block/20-automount...";
        try
        {
            var (success, msg) = await _usbService.EnsureHotplugAutomountScriptAsync();
            StatusMessage = msg;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
