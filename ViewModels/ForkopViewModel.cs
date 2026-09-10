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
    [ObservableProperty] private ObservableCollection<ForkopServerNode> _autoGroups = new();
    [ObservableProperty] private ObservableCollection<ForkopServerNode> _filteredServers = new();
    [ObservableProperty] private ForkopServerNode? _selectedServer;
    [ObservableProperty] private bool _isTestingLatency;
    [ObservableProperty] private bool _isUpdatingSubscriptions;

    // Search and Filters
    [ObservableProperty] private string _serverSearchQuery = "";
    [ObservableProperty] private string _selectedProviderFilter = "Все";
    [ObservableProperty] private string _selectedProtocolFilter = "Все";
    [ObservableProperty] private string _serverStatsText = "Всего серверов: 0";

    public ObservableCollection<string> AvailableProviders { get; } = new()
    {
        "Все", "Blanc", "Stealth", "Abuz", "Vless4U", "Прочие"
    };

    public ObservableCollection<string> AvailableProtocols { get; } = new()
    {
        "Все", "VLESS", "Hysteria2", "VMess", "Shadowsocks"
    };

    partial void OnServerSearchQueryChanged(string value) => ApplyServerFilter();
    partial void OnSelectedProviderFilterChanged(string value) => ApplyServerFilter();
    partial void OnSelectedProtocolFilterChanged(string value) => ApplyServerFilter();

    [RelayCommand]
    public void SetProviderFilter(string? provider)
    {
        SelectedProviderFilter = provider ?? "Все";
    }

    public void ApplyServerFilter()
    {
        var regularNodes = Servers.Where(s => !s.IsAutoGroup).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SelectedProviderFilter) && SelectedProviderFilter != "Все")
        {
            regularNodes = regularNodes.Where(s => 
                s.Provider.Equals(SelectedProviderFilter, StringComparison.OrdinalIgnoreCase) ||
                s.Name.StartsWith(SelectedProviderFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SelectedProtocolFilter) && SelectedProtocolFilter != "Все")
        {
            regularNodes = regularNodes.Where(s => 
                s.Protocol.Equals(SelectedProtocolFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(ServerSearchQuery))
        {
            var q = ServerSearchQuery.Trim();
            regularNodes = regularNodes.Where(s => 
                s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Protocol.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.ServerAddress.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var list = regularNodes.ToList();
        FilteredServers = new ObservableCollection<ForkopServerNode>(list);

        var totalRegular = Servers.Count(s => !s.IsAutoGroup);
        ServerStatsText = $"Всего серверов: {totalRegular} | Отображается: {FilteredServers.Count}";
    }

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
            AutoGroups = new ObservableCollection<ForkopServerNode>(nodes.Where(n => n.IsAutoGroup));
            ApplyServerFilter();
            SelectedServer = nodes.FirstOrDefault(n => n.IsActive) ?? nodes.FirstOrDefault();

            // 4. Section Config
            Section = await _forkopService.GetSectionConfigAsync();

            // 5. Components & Feed info
            FeedInfo = await _forkopService.GetFeedInfoAsync();
            var comps = await _forkopService.GetComponentsAsync();
            Components = new ObservableCollection<ForkopComponentItem>(comps);

            StatusMessage = $"ForkOP загружен. Узлов роутера: {Servers.Count} (Авто-групп: {AutoGroups.Count}, Серверов: {Servers.Count - AutoGroups.Count}).";
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
                var nodes = await _forkopService.GetServersAsync();
                Servers = new ObservableCollection<ForkopServerNode>(nodes);
                AutoGroups = new ObservableCollection<ForkopServerNode>(nodes.Where(n => n.IsAutoGroup));
                ApplyServerFilter();
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
            AutoGroups = new ObservableCollection<ForkopServerNode>(updated.Where(n => n.IsAutoGroup));
            ApplyServerFilter();
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
            s.IsActive = (s == node || s.Name == node.Name);
        }
        foreach (var a in AutoGroups)
        {
            a.IsActive = (a == node || a.Name == node.Name);
        }
        foreach (var f in FilteredServers)
        {
            f.IsActive = (f == node || f.Name == node.Name);
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

    // ==========================================
    // Available Actions (Доступные действия)
    // ==========================================
    [ObservableProperty] private string _logContent = "";
    [ObservableProperty] private bool _isLogViewerOpen;
    [ObservableProperty] private string _singboxConfigContent = "";
    [ObservableProperty] private bool _isConfigViewerOpen;
    [ObservableProperty] private bool _isForkopAutostartEnabled = true;

    [RelayCommand]
    public async Task RestartForkopAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Нет подключения к роутеру по SSH.";
            return;
        }

        IsLoading = true;
        StatusMessage = "Перезапуск службы ForkOP на роутере (/etc/init.d/forkop restart)...";
        try
        {
            var res = await _ssh.ExecuteCommandAsync("/etc/init.d/forkop restart", 15);
            StatusMessage = string.IsNullOrWhiteSpace(res.Error) ? "ForkOP успешно перезапущен." : $"ForkOP: {res.Output} {res.Error}";
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка перезапуска: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task StopForkopAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Нет подключения к роутеру по SSH.";
            return;
        }

        IsLoading = true;
        StatusMessage = "Остановка службы ForkOP (/etc/init.d/forkop stop)...";
        try
        {
            var res = await _ssh.ExecuteCommandAsync("/etc/init.d/forkop stop", 15);
            StatusMessage = "Служба ForkOP остановлена.";
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка остановки: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ToggleAutostartAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Нет подключения к роутеру по SSH.";
            return;
        }

        IsLoading = true;
        StatusMessage = "Переключение автостарта ForkOP (/etc/init.d/forkop disable/enable)...";
        try
        {
            var chk = await _ssh.ExecuteCommandAsync("/etc/init.d/forkop enabled && echo enabled || echo disabled", 5);
            var isEnabled = chk.Output.Contains("enabled");
            var action = isEnabled ? "disable" : "enable";
            await _ssh.ExecuteCommandAsync($"/etc/init.d/forkop {action}", 10);
            IsForkopAutostartEnabled = !isEnabled;
            StatusMessage = isEnabled ? "Автостарт ForkOP отключен." : "Автостарт ForkOP включен.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка автостарта: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task GlobalCheckAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Нет подключения к роутеру по SSH.";
            return;
        }

        StatusMessage = "Запуск глобальной проверки ForkOP...";
        await TestLatencyAsync();
    }

    [RelayCommand]
    public async Task ViewLogsAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Нет подключения к роутеру по SSH.";
            return;
        }

        IsLoading = true;
        StatusMessage = "Получение логов ForkOP и Sing-box с роутера...";
        try
        {
            var res = await _ssh.ExecuteCommandAsync("logread -e forkop -e sing-box | tail -n 120", 10);
            LogContent = string.IsNullOrWhiteSpace(res.Output) ? "Логи не найдены или пусты." : res.Output;
            IsLogViewerOpen = true;
            StatusMessage = "Логи успешно получены.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка чтения логов: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void CloseLogViewer()
    {
        IsLogViewerOpen = false;
    }

    [RelayCommand]
    public void CopyLogs()
    {
        if (!string.IsNullOrEmpty(LogContent))
        {
            System.Windows.Clipboard.SetText(LogContent);
            StatusMessage = "Логи скопированы в буфер обмена.";
        }
    }

    [RelayCommand]
    public async Task ViewSingboxConfigAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Нет подключения к роутеру по SSH.";
            return;
        }

        IsLoading = true;
        StatusMessage = "Чтение конфигурации /etc/sing-box/config.json...";
        try
        {
            var res = await _ssh.ExecuteCommandAsync("cat /etc/sing-box/config.json", 10);
            SingboxConfigContent = string.IsNullOrWhiteSpace(res.Output) ? "{}" : res.Output;
            IsConfigViewerOpen = true;
            StatusMessage = "Конфигурация Sing-box загружена.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка чтения конфига: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void CloseConfigViewer()
    {
        IsConfigViewerOpen = false;
    }

    [RelayCommand]
    public void CopyConfig()
    {
        if (!string.IsNullOrEmpty(SingboxConfigContent))
        {
            System.Windows.Clipboard.SetText(SingboxConfigContent);
            StatusMessage = "Конфигурация Sing-box скопирована в буфер обмена.";
        }
    }
}
