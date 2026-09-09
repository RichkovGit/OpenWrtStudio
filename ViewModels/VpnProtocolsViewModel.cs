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

public partial class VpnProtocolsViewModel : ObservableObject
{
    private readonly IRouterCapabilityService _capService;
    private readonly IVpnProtocolService _vpnService;
    private readonly ISshService _ssh;

    [ObservableProperty] private RouterCapabilityReport _report = new();
    [ObservableProperty] private ObservableCollection<VpnProtocolCapability> _protocols = new();
    [ObservableProperty] private VpnProtocolCapability? _selectedProtocol;
    [ObservableProperty] private int _selectedProtocolIndex = 0; // 0: AWG, 1: WireGuard, 2: Sing-box, 3: OpenVPN, 4: Tailscale
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "Готов к настройке VPN протоколов.";

    // --- AmneziaWG & WireGuard Parameters ---
    [ObservableProperty] private AmneziaWgConfig _awgConfig = new();

    // --- Sing-box Parameters ---
    [ObservableProperty] private SingboxQuickConfig _singboxConfig = new();

    // --- OpenVPN Parameters ---
    [ObservableProperty] private OpenVpnClientConfig _openVpnConfig = new();

    // --- Mesh VPN Parameters ---
    [ObservableProperty] private MeshVpnConfig _meshConfig = new();

    public VpnProtocolsViewModel(IRouterCapabilityService capService, IVpnProtocolService vpnService, ISshService ssh)
    {
        _capService = capService;
        _vpnService = vpnService;
        _ssh = ssh;
    }

    public async Task InitializeAsync()
    {
        await ScanCapabilitiesAsync();
    }

    [RelayCommand]
    public async Task ScanCapabilitiesAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Подключитесь к роутеру для анализа установленных VPN протоколов.";
            return;
        }

        IsLoading = true;
        StatusMessage = "Сканирование установленных на роутере пакетов и VPN служб...";
        try
        {
            Report = await _capService.ScanCapabilitiesAsync();
            Protocols = new ObservableCollection<VpnProtocolCapability>(Report.Protocols);
            StatusMessage = $"Анализ завершен! Роутер: {Report.RouterModel}, система: {Report.OpenWrtVersion} ({Report.Architecture}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка анализа: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ImportAwgConf()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "WireGuard & AmneziaWG Config (*.conf)|*.conf|All files (*.*)|*.*",
            Title = "Импорт файла конфигурации AmneziaWG / WireGuard"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                var text = File.ReadAllText(dlg.FileName);
                AwgConfig = _vpnService.ParseAmneziaWgConf(text);
                StatusMessage = $"Конфигурация успешно импортирована из {Path.GetFileName(dlg.FileName)}! Все параметры и обфускация заполнены.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка разбора файла: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    public async Task ApplyAwgConfigAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Нет подключения к роутеру!";
            return;
        }

        IsLoading = true;
        StatusMessage = "Применение параметров AmneziaWG/WireGuard на роутере...";
        var (success, msg) = await _vpnService.ApplyAmneziaWgConfigAsync(AwgConfig);
        StatusMessage = msg;
        IsLoading = false;

        if (success)
        {
            await ScanCapabilitiesAsync();
        }
    }

    [RelayCommand]
    public async Task ApplySingboxConfigAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Развертывание конфигурации Sing-box на роутере...";
        var (success, msg) = await _vpnService.ApplySingboxConfigAsync(SingboxConfig);
        StatusMessage = msg;
        IsLoading = false;

        if (success)
        {
            await ScanCapabilitiesAsync();
        }
    }

    [RelayCommand]
    public void ImportOvpnFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "OpenVPN Config (*.ovpn)|*.ovpn|All files (*.*)|*.*",
            Title = "Выбор файла OpenVPN"
        };

        if (dlg.ShowDialog() == true)
        {
            OpenVpnConfig.RawOvpnContent = File.ReadAllText(dlg.FileName);
            OpenVpnConfig.ProfileName = Path.GetFileNameWithoutExtension(dlg.FileName).ToLowerInvariant().Replace(" ", "_");
            StatusMessage = $"Файл OpenVPN '{Path.GetFileName(dlg.FileName)}' готов к отправке!";
        }
    }

    [RelayCommand]
    public async Task ApplyOpenVpnConfigAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Установка профиля OpenVPN...";
        var (success, msg) = await _vpnService.ApplyOpenVpnConfigAsync(OpenVpnConfig);
        StatusMessage = msg;
        IsLoading = false;

        if (success)
        {
            await ScanCapabilitiesAsync();
        }
    }

    [RelayCommand]
    public async Task ApplyTailscaleConfigAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Подключение к сети Tailscale...";
        var (success, msg) = await _vpnService.ConfigureTailscaleAsync(MeshConfig);
        StatusMessage = msg;
        IsLoading = false;

        if (success)
        {
            await ScanCapabilitiesAsync();
        }
    }

    [RelayCommand]
    public async Task InstallProtocolAsync(VpnProtocolCapability? cap)
    {
        if (cap == null || !_ssh.IsConnected || string.IsNullOrWhiteSpace(cap.InstallCommand)) return;

        IsLoading = true;
        StatusMessage = $"Установка {cap.DisplayName}... Пожалуйста, подождите.";
        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(cap.InstallCommand, 60);
        if (code == 0 || outStr.Contains("Installing") || outStr.Contains("Configuring"))
        {
            StatusMessage = $"Пакеты для {cap.DisplayName} успешно установлены!";
        }
        else
        {
            StatusMessage = $"Ошибка установки {cap.DisplayName}: {err}\n{outStr}";
        }
        IsLoading = false;
        await ScanCapabilitiesAsync();
    }
}
