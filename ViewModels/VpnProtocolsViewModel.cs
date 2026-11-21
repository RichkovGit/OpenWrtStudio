using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    [ObservableProperty] private ObservableCollection<AmneziaWgConfig> _existingAwgConfigs = new();
    [ObservableProperty] private AmneziaWgConfig? _selectedAwgConfig;
    [ObservableProperty] private AmneziaWgConfig _awgConfig = new();

    // --- Sing-box Parameters ---
    [ObservableProperty] private SingboxQuickConfig _singboxConfig = new();

    // --- OpenVPN Parameters ---
    [ObservableProperty] private OpenVpnClientConfig _openVpnConfig = new();

    // --- Mesh VPN Parameters ---
    [ObservableProperty] private MeshVpnConfig _meshConfig = new();

    // --- Standalone Protocols (Shadowsocks / Hysteria2 / TUIC) ---
    [ObservableProperty] private ShadowsocksConfig _shadowsocksConfig = new();
    [ObservableProperty] private Hysteria2Config _hysteria2Config = new();
    [ObservableProperty] private TuicConfig _tuicConfig = new();

    public List<string> ShadowsocksMethods { get; } = new()
    {
        "2022-blake3-aes-128-gcm",
        "2022-blake3-aes-256-gcm",
        "2022-blake3-chacha20-poly1305",
        "aes-128-gcm",
        "aes-256-gcm",
        "chacha20-ietf-poly1305"
    };

    public List<string> TuicCongestionControls { get; } = new() { "bbr", "cubic", "new_reno" };
    public List<string> TuicUdpRelayModes { get; } = new() { "native", "quic" };

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
            await LoadAwgConfigsAsync();
            StatusMessage = $"Анализ завершен! Роутер: {Report.RouterModel}, система: {Report.OpenWrtVersion} ({Report.Architecture}). Найдено туннелей AWG: {ExistingAwgConfigs.Count}.";
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
    public async Task LoadAwgConfigsAsync()
    {
        if (!_ssh.IsConnected) return;

        try
        {
            var list = await _vpnService.GetAmneziaWgConfigsAsync();
            ExistingAwgConfigs = new ObservableCollection<AmneziaWgConfig>(list);
            if (ExistingAwgConfigs.Count > 0)
            {
                var match = ExistingAwgConfigs.FirstOrDefault(c => c.InterfaceName == AwgConfig.InterfaceName) ?? ExistingAwgConfigs[0];
                SelectedAwgConfig = match;
            }
            else
            {
                SelectedAwgConfig = null;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки туннелей AWG: {ex.Message}";
        }
    }

    partial void OnSelectedAwgConfigChanged(AmneziaWgConfig? value)
    {
        if (value == null) return;
        AwgConfig = value;
    }

    [RelayCommand]
    public void CreateNewAwg()
    {
        SelectedAwgConfig = null;
        AwgConfig = new AmneziaWgConfig
        {
            InterfaceName = $"awg{ExistingAwgConfigs.Count}",
            Address = "10.8.0.2/24",
            ListenPort = 51820 + ExistingAwgConfigs.Count,
            Jc = 4,
            Jmin = 40,
            Jmax = 70,
            S1 = 15,
            S2 = 25,
            H1 = 1,
            H2 = 2,
            H3 = 3,
            H4 = 4,
            AllowedIPs = "0.0.0.0/0, ::/0",
            PersistentKeepalive = 25
        };
        StatusMessage = "Создание нового интерфейса AmneziaWG. Заполните параметры или импортируйте .conf файл.";
    }

    [RelayCommand]
    public async Task DeleteAwgConfigAsync()
    {
        if (!_ssh.IsConnected) return;
        if (string.IsNullOrWhiteSpace(AwgConfig.InterfaceName)) return;

        IsLoading = true;
        StatusMessage = $"Удаление интерфейса {AwgConfig.InterfaceName} с роутера...";
        var (success, msg) = await _vpnService.DeleteAmneziaWgConfigAsync(AwgConfig.InterfaceName);
        StatusMessage = msg;
        IsLoading = false;

        if (success)
        {
            await LoadAwgConfigsAsync();
            await ScanCapabilitiesAsync();
        }
    }

    [RelayCommand]
    public async Task RestartAwgConfigAsync()
    {
        if (!_ssh.IsConnected) return;
        if (string.IsNullOrWhiteSpace(AwgConfig.InterfaceName)) return;

        IsLoading = true;
        StatusMessage = $"Перезапуск интерфейса {AwgConfig.InterfaceName}...";
        var (success, msg) = await _vpnService.RestartAmneziaWgAsync(AwgConfig.InterfaceName);
        StatusMessage = msg;
        IsLoading = false;

        if (success)
        {
            await LoadAwgConfigsAsync();
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
            await LoadAwgConfigsAsync();
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
            try
            {
                OpenVpnConfig.RawOvpnContent = File.ReadAllText(dlg.FileName);
                OpenVpnConfig.ProfileName = Path.GetFileNameWithoutExtension(dlg.FileName).ToLowerInvariant().Replace(" ", "_");
                StatusMessage = $"Файл OpenVPN '{Path.GetFileName(dlg.FileName)}' готов к отправке!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка чтения файла: {ex.Message}";
            }
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
    public async Task ApplyShadowsocksConfigAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Нет подключения к роутеру!";
            return;
        }

        IsLoading = true;
        StatusMessage = "Развертывание автономного Shadowsocks (sslocal) на роутере...";
        var (success, msg) = await _vpnService.ApplyShadowsocksConfigAsync(ShadowsocksConfig);
        StatusMessage = msg;
        IsLoading = false;
        if (success) await ScanCapabilitiesAsync();
    }

    [RelayCommand]
    public async Task ApplyHysteria2ConfigAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Нет подключения к роутеру!";
            return;
        }

        IsLoading = true;
        StatusMessage = "Развертывание автономного Hysteria2 на роутере...";
        var (success, msg) = await _vpnService.ApplyHysteria2ConfigAsync(Hysteria2Config);
        StatusMessage = msg;
        IsLoading = false;
        if (success) await ScanCapabilitiesAsync();
    }

    [RelayCommand]
    public async Task ApplyTuicConfigAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Нет подключения к роутеру!";
            return;
        }

        IsLoading = true;
        StatusMessage = "Развертывание автономного TUIC v5 на роутере...";
        var (success, msg) = await _vpnService.ApplyTuicConfigAsync(TuicConfig);
        StatusMessage = msg;
        IsLoading = false;
        if (success) await ScanCapabilitiesAsync();
    }

    [RelayCommand]
    public async Task CheckStandaloneStatusAsync(string? protocol)
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Нет подключения к роутеру!";
            return;
        }
        var type = protocol?.ToLowerInvariant() switch
        {
            "shadowsocks" => VpnProtocolType.Shadowsocks,
            "hysteria2" => VpnProtocolType.Hysteria2,
            "tuic" => VpnProtocolType.Tuic,
            _ => VpnProtocolType.SingBox
        };
        var (online, status) = await _vpnService.GetProtocolStatusAsync(type);
        var running = status.Contains("running", StringComparison.OrdinalIgnoreCase);
        StatusMessage = $"{protocol}: {(online && running ? "служба активна" : "служба не запущена")} ({status.Trim()}).";
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
