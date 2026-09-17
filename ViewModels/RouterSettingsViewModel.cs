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
        StatusMessage = "Создание резервной копии настроек (sysupgrade -b)...";
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
            var (downSuccess, data, downErr) = await _ssh.DownloadBinaryFileAsync("/tmp/backup.tar.gz");
            if (downSuccess && data != null)
            {
                try
                {
                    File.WriteAllBytes(dlg.FileName, data);
                    StatusMessage = $"Бэкап успешно сохранен на ПК ({data.Length / 1024} КБ): {dlg.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Ошибка записи файла на диск: {ex.Message}";
                }
            }
            else
            {
                StatusMessage = $"Ошибка скачивания бэкапа: {downErr}";
            }
        }
        await _ssh.ExecuteCommandAsync("rm -f /tmp/backup.tar.gz", 5);
        IsLoading = false;
    }

    [RelayCommand]
    public async Task RestoreBackupAsync()
    {
        if (!_ssh.IsConnected) return;

        var dlg = new OpenFileDialog
        {
            Filter = "Tar GZip Backup (*.tar.gz)|*.tar.gz|All files (*.*)|*.*",
            Title = "Выберите файл резервной копии настроек OpenWrt"
        };

        if (dlg.ShowDialog() != true) return;

        IsLoading = true;
        StatusMessage = "Загрузка резервной копии на роутер...";

        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            var (upSuccess, upMsg) = await _ssh.UploadBinaryFileAsync(bytes, "/tmp/restore_backup.tar.gz");
            if (!upSuccess)
            {
                StatusMessage = $"Ошибка передачи файла на роутер: {upMsg}";
                IsLoading = false;
                return;
            }

            StatusMessage = "Применение резервной копии (sysupgrade -r)...";
            var (code, outStr, err) = await _ssh.ExecuteCommandAsync("sysupgrade -r /tmp/restore_backup.tar.gz && rm -f /tmp/restore_backup.tar.gz", 30);
            if (code == 0)
            {
                StatusMessage = "Резервная копия настроек успешно восстановлена! Для применения всех параметров рекомендуется перезагрузить роутер.";
            }
            else
            {
                StatusMessage = $"Ошибка восстановления: {err}\n{outStr}";
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
    public async Task DownloadOverlayBackupAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Создание полного архива программ и системы (Overlay)...";

        var createCmd = "rm -f /tmp/overlay_backup.tar.gz; " +
                        "if [ -d /overlay/upper ]; then tar -czf /tmp/overlay_backup.tar.gz -C /overlay/upper . 2>/dev/null; " +
                        "else tar -czf /tmp/overlay_backup.tar.gz -C /overlay . 2>/dev/null; fi; " +
                        "ls -lh /tmp/overlay_backup.tar.gz";

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(createCmd, 60);
        if (code != 0)
        {
            StatusMessage = $"Ошибка создания архива overlay: {err}\n{outStr}";
            IsLoading = false;
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Tar GZip Backup (*.tar.gz)|*.tar.gz|All files (*.*)|*.*",
            FileName = $"openwrt_overlay_full_{DateTime.Now:yyyyMMdd_HHmm}.tar.gz"
        };

        if (dlg.ShowDialog() == true)
        {
            var (downSuccess, data, downErr) = await _ssh.DownloadBinaryFileAsync("/tmp/overlay_backup.tar.gz");
            if (downSuccess && data != null)
            {
                try
                {
                    File.WriteAllBytes(dlg.FileName, data);
                    StatusMessage = $"Полный архив программ (Overlay) сохранен на ПК ({data.Length / (1024 * 1024):F1} МБ): {dlg.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Ошибка сохранения: {ex.Message}";
                }
            }
            else
            {
                StatusMessage = $"Ошибка скачивания: {downErr}";
            }
        }
        await _ssh.ExecuteCommandAsync("rm -f /tmp/overlay_backup.tar.gz", 5);
        IsLoading = false;
    }

    [RelayCommand]
    public async Task RestoreOverlayBackupAsync()
    {
        if (!_ssh.IsConnected) return;

        var dlg = new OpenFileDialog
        {
            Filter = "Tar GZip Backup (*.tar.gz)|*.tar.gz|All files (*.*)|*.*",
            Title = "Выберите полный архив Overlay для восстановления"
        };

        if (dlg.ShowDialog() != true) return;

        IsLoading = true;
        StatusMessage = "Загрузка архива Overlay на роутер...";

        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            var (upSuccess, upMsg) = await _ssh.UploadBinaryFileAsync(bytes, "/tmp/restore_overlay.tar.gz");
            if (!upSuccess)
            {
                StatusMessage = $"Ошибка передачи архива: {upMsg}";
                IsLoading = false;
                return;
            }

            StatusMessage = "Развертывание полного архива Overlay в файловую систему роутера...";
            var extractCmd = "if [ -d /overlay/upper ]; then tar -xzf /tmp/restore_overlay.tar.gz -C /overlay/upper/ 2>/dev/null; " +
                             "else tar -xzf /tmp/restore_overlay.tar.gz -C /overlay/ 2>/dev/null; fi; " +
                             "rm -f /tmp/restore_overlay.tar.gz && sync";

            var (code, outStr, err) = await _ssh.ExecuteCommandAsync(extractCmd, 90);
            if (code == 0)
            {
                StatusMessage = "Полный архив программ (Overlay) успешно развернут! Роутер перезагружается для вступления изменений в силу...";
                await _ssh.ExecuteCommandAsync("reboot", 5);
            }
            else
            {
                StatusMessage = $"Ошибка распаковки: {err}\n{outStr}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка восстановления: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RescueLuciAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Экстренное восстановление веб-интерфейса LuCI...";

        var rescueScript = "rm -rf /usr/lib/lua/luci/controller/design-config.lua /usr/lib/lua/luci/view/themes/design /www/luci-static/design 2>/dev/null || true; " +
                           "if [ -d /www/luci-static/argon ]; then uci set luci.main.mediaurlbase=/luci-static/argon; else uci set luci.main.mediaurlbase=/luci-static/bootstrap; fi; " +
                           "uci commit luci; " +
                           "rm -rf /tmp/luci-indexcache* /tmp/luci-modulecache* 2>/dev/null; " +
                           "/etc/init.d/rpcd restart 2>/dev/null || true; " +
                           "/etc/init.d/uhttpd restart 2>/dev/null || true";

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(rescueScript, 20);
        if (code == 0)
        {
            StatusMessage = "LuCI успешно восстановлен! Удалены сбойные файлы, тема сброшена на рабочую (Argon/Bootstrap). Обновите вкладку в браузере (F5).";
        }
        else
        {
            StatusMessage = $"Ошибка восстановления LuCI: {err}\n{outStr}";
        }

        IsLoading = false;
    }

    [RelayCommand]
    public async Task RebootRouterAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Перезагрузка роутера...";
        await _ssh.ExecuteCommandAsync("reboot", 5);
        StatusMessage = "Команда перезагрузки отправлена на роутер. Подождите 1-2 минуты для повторного подключения.";
        IsLoading = false;
    }
}
