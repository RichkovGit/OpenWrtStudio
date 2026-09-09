using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;

namespace OpenWrtStudio.ViewModels;

public partial class RouterSettingsViewModel : ObservableObject
{
    private readonly IRouterConfigService _routerConfigService;
    private readonly ISshService _ssh;

    [ObservableProperty] private int _selectedSectionIndex = 0; // 0: Wi-Fi, 1: LAN/DHCP, 2: WAN, 3: Firewall, 4: System
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "Готов к настройке роутера.";

    // --- Wi-Fi ---
    [ObservableProperty] private ObservableCollection<WifiRadioConfig> _wifiRadios = new();

    // --- LAN / DHCP ---
    [ObservableProperty] private LanDhcpConfig _lanConfig = new();
    [ObservableProperty] private ObservableCollection<StaticDhcpLease> _staticLeases = new();
    [ObservableProperty] private string _newLeaseName = "PC";
    [ObservableProperty] private string _newLeaseMac = "AA:BB:CC:DD:EE:FF";
    [ObservableProperty] private string _newLeaseIp = "192.168.1.50";

    // --- WAN ---
    [ObservableProperty] private WanConfig _wanConfig = new();

    // --- Firewall ---
    [ObservableProperty] private ObservableCollection<PortForwardRule> _portForwards = new();
    [ObservableProperty] private PortForwardRule _newRule = new();

    // --- System ---
    [ObservableProperty] private SystemAdminConfig _systemConfig = new();

    public RouterSettingsViewModel(IRouterConfigService routerConfigService, ISshService ssh)
    {
        _routerConfigService = routerConfigService;
        _ssh = ssh;
    }

    public async Task InitializeAsync()
    {
        await LoadAllConfigsAsync();
    }

    [RelayCommand]
    public async Task LoadAllConfigsAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Подключитесь к роутеру по SSH для чтения настроек.";
            return;
        }

        IsLoading = true;
        StatusMessage = "Загрузка конфигурации роутера (Wi-Fi, LAN, WAN, Брандмауэр)...";
        try
        {
            var radios = await _routerConfigService.GetWifiConfigsAsync();
            WifiRadios = new ObservableCollection<WifiRadioConfig>(radios);

            LanConfig = await _routerConfigService.GetLanDhcpConfigAsync();
            StaticLeases = new ObservableCollection<StaticDhcpLease>(LanConfig.StaticLeases);

            WanConfig = await _routerConfigService.GetWanConfigAsync();

            var forwards = await _routerConfigService.GetPortForwardRulesAsync();
            PortForwards = new ObservableCollection<PortForwardRule>(forwards);

            SystemConfig = await _routerConfigService.GetSystemAdminConfigAsync();

            StatusMessage = "Все настройки роутера успешно загружены!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки конфигурации: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SaveWifiAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Сохранение настроек Wi-Fi и перезапуск радиомодулей...";
        var (success, msg) = await _routerConfigService.ApplyWifiConfigAsync(new System.Collections.Generic.List<WifiRadioConfig>(WifiRadios));
        StatusMessage = msg;
        IsLoading = false;
    }

    [RelayCommand]
    public async Task SaveLanAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Применение параметров локальной сети и DHCP...";
        LanConfig.StaticLeases = new System.Collections.Generic.List<StaticDhcpLease>(StaticLeases);
        var (success, msg) = await _routerConfigService.ApplyLanDhcpConfigAsync(LanConfig);
        StatusMessage = msg;
        IsLoading = false;
    }

    [RelayCommand]
    public void AddStaticLease()
    {
        if (string.IsNullOrWhiteSpace(NewLeaseMac) || string.IsNullOrWhiteSpace(NewLeaseIp)) return;

        StaticLeases.Add(new StaticDhcpLease
        {
            Name = NewLeaseName.Trim(),
            Mac = NewLeaseMac.Trim().ToUpperInvariant(),
            Ip = NewLeaseIp.Trim()
        });
        NewLeaseName = "Device";
        NewLeaseMac = "";
        NewLeaseIp = "";
    }

    [RelayCommand]
    public void DeleteStaticLease(StaticDhcpLease? lease)
    {
        if (lease != null) StaticLeases.Remove(lease);
    }

    [RelayCommand]
    public async Task SaveWanAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Сохранение настроек WAN и перезапуск подключения...";
        var (success, msg) = await _routerConfigService.ApplyWanConfigAsync(WanConfig);
        StatusMessage = msg;
        IsLoading = false;
    }

    [RelayCommand]
    public async Task AddPortForwardAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Добавление правила проброса портов...";
        var (success, msg) = await _routerConfigService.SavePortForwardRuleAsync(NewRule);
        StatusMessage = msg;
        IsLoading = false;

        if (success)
        {
            NewRule = new PortForwardRule();
            var forwards = await _routerConfigService.GetPortForwardRulesAsync();
            PortForwards = new ObservableCollection<PortForwardRule>(forwards);
        }
    }

    [RelayCommand]
    public async Task DeletePortForwardAsync(PortForwardRule? rule)
    {
        if (rule == null || !_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = $"Удаление правила {rule.Name}...";
        var (success, msg) = await _routerConfigService.DeletePortForwardRuleAsync(rule.SectionId);
        StatusMessage = msg;
        IsLoading = false;

        if (success)
        {
            PortForwards.Remove(rule);
        }
    }

    [RelayCommand]
    public async Task SaveSystemAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Сохранение системных параметров...";
        var (success, msg) = await _routerConfigService.ApplySystemAdminConfigAsync(SystemConfig);
        StatusMessage = msg;
        IsLoading = false;
    }

    [RelayCommand]
    public async Task DownloadBackupAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Создание полного бэкапа настроек роутера (sysupgrade -b)...";
        var (code, outStr, err) = await _ssh.ExecuteCommandAsync("sysupgrade -b /tmp/backup.tar.gz && ls -lh /tmp/backup.tar.gz", 20);
        if (code != 0)
        {
            StatusMessage = $"Ошибка создания бэкапа: {err}\n{outStr}";
            IsLoading = false;
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Tar GZip Backup (*.tar.gz)|*.tar.gz|All files (*.*)|*.*",
            FileName = $"openwrt_backup_{DateTime.Now:yyyyMMdd_HHmm}.tar.gz"
        };

        if (dlg.ShowDialog() == true)
        {
            var (downSuccess, content) = await _ssh.DownloadFileContentAsync("/tmp/backup.tar.gz");
            if (downSuccess)
            {
                File.WriteAllText(dlg.FileName, content);
                StatusMessage = $"Бэкап сохранен на ПК: {dlg.FileName}";
            }
            else
            {
                StatusMessage = $"Ошибка сохранения файла бэкапа: {content}";
            }
        }
        IsLoading = false;
    }
}
