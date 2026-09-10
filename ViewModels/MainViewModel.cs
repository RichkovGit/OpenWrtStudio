using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;

namespace OpenWrtStudio.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISshService _ssh;
    private readonly IProfileService _profileService;
    private readonly ISentinelService _sentinelService;

    [ObservableProperty]
    private string _title = "OpenWrt Studio";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Не подключено";

    [ObservableProperty]
    private string _connectedHost = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyStatus = "";

    [ObservableProperty]
    private ObservableCollection<ConnectionProfile> _profiles = new();

    [ObservableProperty]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private string _notificationMessage = "";

    [ObservableProperty]
    private bool _showNotification;

    [ObservableProperty]
    private bool _isNotificationError;

    // --- Sentinel Watchdog Integration ---
    [ObservableProperty]
    private SentinelIssue? _activeIssue;

    [ObservableProperty]
    private bool _hasActiveIssue;

    [ObservableProperty]
    private string _healingStatus = "";

    // --- Navigation ---
    [ObservableProperty]
    private int _selectedNavIndex = 0;

    [RelayCommand]
    public void NavigateTo(string target)
    {
        SelectedNavIndex = target switch
        {
            "Dashboard" or "0" => 0,
            "Diagnostics" or "1" => 1,
            "Packages" or "2" => 2,
            "Mihomo" or "3" => 3,
            "Settings" or "4" => 4,
            _ => 0
        };
    }

    // --- Mode Switcher (Simple vs Expert) ---
    [ObservableProperty]
    private bool _isExpertMode = false;

    [RelayCommand]
    public void ToggleExpertMode()
    {
        IsExpertMode = !IsExpertMode;
    }

    [RelayCommand]
    public void EnableSimpleMode()
    {
        IsExpertMode = false;
    }

    [RelayCommand]
    public void EnableExpertMode()
    {
        IsExpertMode = true;
    }

    // ViewModels references
    public DashboardViewModel DashboardVM { get; }
    public ClientsViewModel ClientsVM { get; }
    public RouterSettingsViewModel RouterSettingsVM { get; }
    public VpnProtocolsViewModel VpnVM { get; }
    public MihomoBuilderViewModel MihomoVM { get; }
    public ForkopViewModel ForkopVM { get; }
    public SchedulerViewModel SchedulerVM { get; }
    public DiagnosticsViewModel DiagnosticsVM { get; }
    public PackagesViewModel PackagesVM { get; }
    public SettingsViewModel SettingsVM { get; }

    public MainViewModel(
        ISshService ssh,
        IProfileService profileService,
        ISentinelService sentinelService,
        DashboardViewModel dashboardVM,
        ClientsViewModel clientsVM,
        RouterSettingsViewModel routerSettingsVM,
        VpnProtocolsViewModel vpnVM,
        MihomoBuilderViewModel mihomoVM,
        ForkopViewModel forkopVM,
        SchedulerViewModel schedulerVM,
        DiagnosticsViewModel diagnosticsVM,
        PackagesViewModel packagesVM,
        SettingsViewModel settingsVM)
    {
        _ssh = ssh;
        _profileService = profileService;
        _sentinelService = sentinelService;
        DashboardVM = dashboardVM;
        ClientsVM = clientsVM;
        RouterSettingsVM = routerSettingsVM;
        VpnVM = vpnVM;
        MihomoVM = mihomoVM;
        ForkopVM = forkopVM;
        SchedulerVM = schedulerVM;
        DiagnosticsVM = diagnosticsVM;
        PackagesVM = packagesVM;
        SettingsVM = settingsVM;

        _ssh.ConnectionChanged += OnConnectionChanged;
        _sentinelService.IssueChanged += OnSentinelIssueChanged;
        _profileService.ProfilesSaved += OnProfilesSaved;
    }

    private void OnProfilesSaved(object? sender, List<ConnectionProfile> list)
    {
        App.Current?.Dispatcher?.Invoke(() =>
        {
            var currentId = SelectedProfile?.Id;
            Profiles = new ObservableCollection<ConnectionProfile>(list);
            SelectedProfile = System.Linq.Enumerable.FirstOrDefault(Profiles, p => p.Id == currentId) ?? (Profiles.Count > 0 ? Profiles[0] : null);
        });
    }

    public async Task InitializeAsync()
    {
        var loaded = await _profileService.LoadProfilesAsync();
        Profiles = new ObservableCollection<ConnectionProfile>(loaded);
        if (Profiles.Count > 0)
        {
            SelectedProfile = Profiles[0];
            if (SelectedProfile.AutoConnectOnStartup)
            {
                await ConnectAsync();
            }
        }
    }

    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (SelectedProfile == null)
        {
            ShowAlert("Выберите или создайте профиль подключения в Настройках", true);
            return;
        }

        IsBusy = true;
        BusyStatus = $"Подключение к {SelectedProfile.Host}:{SelectedProfile.Port}...";

        var (success, msg) = await _ssh.ConnectAsync(SelectedProfile);
        IsBusy = false;
        BusyStatus = "";

        if (success)
        {
            ShowAlert($"Успешное подключение к {SelectedProfile.Host}!", false);
            await DashboardVM.RefreshDashboardAsync();
            await _sentinelService.ProbeHealthAsync();
            _ = ClientsVM.LoadClientsAsync();
            _ = VpnVM.ScanCapabilitiesAsync();
            _ = RouterSettingsVM.LoadAllConfigsAsync();
            _ = ForkopVM.LoadDataAsync();
            _ = SchedulerVM.LoadJobsAsync();
        }
        else
        {
            ShowAlert(msg, true);
        }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        IsBusy = true;
        BusyStatus = "Отключение...";
        await _ssh.DisconnectAsync();
        IsBusy = false;
        BusyStatus = "";
        HasActiveIssue = false;
        ActiveIssue = null;
        ShowAlert("Отключено от роутера", false);
    }

    [RelayCommand]
    public async Task ApplyHealingMethodAsync(RecoveryMethod? method)
    {
        if (method == null || !_ssh.IsConnected) return;

        HealingStatus = $"Применяется: {method.Name}...";
        var (success, msg) = await _sentinelService.ApplyRecoveryMethodAsync(method);
        HealingStatus = msg;
        ShowAlert(msg, !success);

        if (success)
        {
            await Task.Delay(2000);
            await DashboardVM.RefreshDashboardAsync(silent: true);
        }
    }

    [RelayCommand]
    public void DismissIssue()
    {
        HasActiveIssue = false;
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        App.Current?.Dispatcher?.Invoke(() =>
        {
            IsConnected = connected;
            ConnectionStatus = connected
                ? $"Подключено: {_ssh.CurrentProfile?.Host ?? "роутер"}"
                : "Не подключено";
            ConnectedHost = connected ? (_ssh.CurrentProfile?.Host ?? "") : "";

            if (!connected)
            {
                HasActiveIssue = false;
                ActiveIssue = null;
            }
        });
    }

    private void OnSentinelIssueChanged(object? sender, SentinelIssue? issue)
    {
        ActiveIssue = issue;
        HasActiveIssue = issue != null;

        if (issue != null)
        {
            var prefix = issue.IsProviderIssue ? "[Провайдер]" : "[Роутер]";
            ShowAlert($"{prefix} {issue.Title}: {issue.Description}", issue.Severity == IssueSeverity.Critical);
        }
    }

    public void ShowAlert(string message, bool isError)
    {
        NotificationMessage = message;
        IsNotificationError = isError;
        ShowNotification = true;

        Task.Delay(6000).ContinueWith(_ =>
        {
            App.Current?.Dispatcher?.Invoke(() =>
            {
                ShowNotification = false;
            });
        });
    }
}
