using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;

namespace OpenWrtStudio.ViewModels;

public partial class ForkopViewModel : ObservableObject
{
    private readonly IForkopService _forkopService;
    private readonly ISshService _ssh;

    // Tabs: 0 = Дашборд, 1 = Секции и Подписки, 2 = Серверы, 3 = Компоненты и Диагностика
    [ObservableProperty] private int _selectedTabIndex;

    // Dashboard & Live Metrics
    [ObservableProperty] private ForkopDashboardStats _dashboardStats = new();
    [ObservableProperty] private ObservableCollection<ForkopSubscriptionInfo> _subscriptions = new();
    [ObservableProperty] private ObservableCollection<ForkopServerNode> _servers = new();
    [ObservableProperty] private ForkopServerNode? _selectedServer;
    [ObservableProperty] private bool _isTestingLatency;
    [ObservableProperty] private bool _isUpdatingSubscriptions;

    // Section: VPN Proxy
    [ObservableProperty] private ForkopSection _section = new();
    [ObservableProperty] private string _newSubscriptionUrlInput = "";
    [ObservableProperty] private string _newConnectionUrlInput = "";
    [ObservableProperty] private string _newRulesetInput = "";

    public ObservableCollection<string> AvailableInterfaces { get; } = new()
    {
        "br-lan", "eth0", "wan", "tun0", "awg0", "wg0"
    };

    public ObservableCollection<string> AvailableActions { get; } = new()
    {
        "Connection", "Direct", "Block"
    };

    public ObservableCollection<string> QuickRulesetSuggestions { get; } = new()
    {
        "Youtube", "Discord", "Telegram", "Meta", "Twitter (X)", "Google AI", "Russia inside", "HDRəzka", "Roblox", "Supercell", "GitHub"
    };

    // Components & Repo & Diagnostics
    [ObservableProperty] private ForkopFeedInfo _feedInfo = new();
    [ObservableProperty] private ObservableCollection<ForkopComponentItem> _components = new();
    [ObservableProperty] private ForkopComponentItem? _selectedComponent;
    [ObservableProperty] private ForkopDiagResult _diagResult = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isDiagnosing;
    [ObservableProperty] private string _statusMessage = "Готов к работе с ForkOP.";

    public ForkopViewModel(IForkopService forkopService, ISshService ssh)
    {
        _forkopService = forkopService;
        _ssh = ssh;
    }

    public async Task InitializeAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        StatusMessage = "Загрузка конфигурации, подписок и серверов ForkOP...";
        try
        {
            // 1. Dashboard Stats
            DashboardStats = await _forkopService.GetDashboardStatsAsync();

            // 2. Subscriptions
            var subs = await _forkopService.GetSubscriptionInfoListAsync();
            Subscriptions = new ObservableCollection<ForkopSubscriptionInfo>(subs);

            // 3. Servers & Nodes
            var nodes = await _forkopService.GetServersAsync();
            Servers = new ObservableCollection<ForkopServerNode>(nodes);
            SelectedServer = nodes.FirstOrDefault(n => n.IsActive) ?? nodes.FirstOrDefault();

            // 4. Section Config
            Section = await _forkopService.GetSectionConfigAsync();

            // 5. Components & Feed info
            FeedInfo = await _forkopService.GetFeedInfoAsync();
            var comps = await _forkopService.GetComponentsAsync();
            Components = new ObservableCollection<ForkopComponentItem>(comps);

            StatusMessage = $"ForkOP загружен. Активных соединений: {DashboardStats.ActiveConnections}, Серверов: {Servers.Count}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task UpdateSubscriptionsAsync()
    {
        IsUpdatingSubscriptions = true;
        StatusMessage = "Обновление подписок на роутере...";
        try
        {
            var (success, msg) = await _forkopService.UpdateSubscriptionsAsync();
            StatusMessage = msg;
            if (success)
            {
                var subs = await _forkopService.GetSubscriptionInfoListAsync();
                Subscriptions = new ObservableCollection<ForkopSubscriptionInfo>(subs);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Сбой обновления подписок: {ex.Message}";
        }
        finally
        {
            IsUpdatingSubscriptions = false;
        }
    }

    [RelayCommand]
    public async Task TestLatencyAsync()
    {
        IsTestingLatency = true;
        StatusMessage = "Тестирование задержки до всех серверов и узлов...";
        try
        {
            var updated = await _forkopService.TestLatenciesAsync(Servers.ToList());
            Servers = new ObservableCollection<ForkopServerNode>(updated);
            StatusMessage = "Тестирование задержки завершено успешно.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка замера задержки: {ex.Message}";
        }
        finally
        {
            IsTestingLatency = false;
        }
    }

    [RelayCommand]
    public async Task SelectServerAsync(ForkopServerNode? node)
    {
        if (node == null) return;

        foreach (var s in Servers)
        {
            s.IsActive = (s == node);
        }
        SelectedServer = node;

        StatusMessage = $"Выбор активного сервера: {node.Name}...";
        var (success, msg) = await _forkopService.SelectActiveServerAsync(node);
        StatusMessage = msg;
    }

    // Section editing commands
    [RelayCommand]
    public void AddSubscriptionUrl()
    {
        var url = NewSubscriptionUrlInput.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!Section.SubscriptionUrls.Contains(url))
        {
            Section.SubscriptionUrls.Add(url);
            NewSubscriptionUrlInput = "";
            StatusMessage = "URL подписки добавлен в список.";
        }
    }

    [RelayCommand]
    public void RemoveSubscriptionUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        Section.SubscriptionUrls.Remove(url);
        StatusMessage = "URL подписки удален из списка.";
    }

    [RelayCommand]
    public void AddConnectionUrl()
    {
        var conn = NewConnectionUrlInput.Trim();
        if (string.IsNullOrWhiteSpace(conn)) return;

        if (!Section.ConnectionUrls.Contains(conn))
        {
            Section.ConnectionUrls.Add(conn);
            NewConnectionUrlInput = "";
            StatusMessage = "URL подключения добавлен.";
        }
    }

    [RelayCommand]
    public void RemoveConnectionUrl(string? conn)
    {
        if (string.IsNullOrWhiteSpace(conn)) return;
        Section.ConnectionUrls.Remove(conn);
        StatusMessage = "URL подключения удален.";
    }

    [RelayCommand]
    public void AddRuleset(string? rule)
    {
        var r = (rule ?? NewRulesetInput).Trim();
        if (string.IsNullOrWhiteSpace(r)) return;

        if (!Section.Rulesets.Contains(r))
        {
            Section.Rulesets.Add(r);
            NewRulesetInput = "";
            StatusMessage = $"Правило '{r}' добавлено.";
        }
    }

    [RelayCommand]
    public void RemoveRuleset(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return;
        Section.Rulesets.Remove(rule);
        StatusMessage = $"Правило '{rule}' удалено.";
    }

    [RelayCommand]
    public async Task SaveSectionAsync()
    {
        IsLoading = true;
        StatusMessage = "Сохранение секции ForkOP в UCI и перезапуск службы...";
        try
        {
            var (success, msg) = await _forkopService.SaveSectionConfigAsync(Section);
            StatusMessage = msg;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сохранения: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ConfigureRepoAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Добавление репозитория ForkOP и обновление индексов пакетов...";
        var (success, msg) = await _forkopService.ConfigureFeedAsync(FeedInfo.Branch);
        StatusMessage = msg;
        IsLoading = false;

        if (success)
        {
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    public async Task InstallComponentAsync(ForkopComponentItem? item)
    {
        if (item == null || !_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = $"Установка {item.Name} ({item.PackageName})...";
        var (success, msg) = await _forkopService.InstallComponentAsync(item.PackageName);
        StatusMessage = msg;
        IsLoading = false;

        if (success)
        {
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    public async Task RunDiagnosticsAsync()
    {
        if (!_ssh.IsConnected) return;

        IsDiagnosing = true;
        StatusMessage = "Выполнение глубокой диагностики обхода блокировок...";
        try
        {
            DiagResult = await _forkopService.RunDiagnosticsAsync();
            StatusMessage = DiagResult.Details;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка диагностики: {ex.Message}";
        }
        finally
        {
            IsDiagnosing = false;
        }
    }
}
