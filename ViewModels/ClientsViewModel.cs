using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;

namespace OpenWrtStudio.ViewModels;

public partial class ClientsViewModel : ObservableObject
{
    private readonly IClientManagerService _clientManager;
    private readonly ISshService _ssh;

    [ObservableProperty] private ObservableCollection<NetworkClient> _clients = new();
    [ObservableProperty] private ObservableCollection<NetworkClient> _filteredClients = new();

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _selectedFilter = "Все"; // Все, В сети, 5 GHz, 2.4 GHz, LAN, Офлайн, Заблокировано

    [ObservableProperty] private int _totalCount = 0;
    [ObservableProperty] private int _onlineCount = 0;
    [ObservableProperty] private int _wifi5Count = 0;
    [ObservableProperty] private int _wifi24Count = 0;
    [ObservableProperty] private int _lanCount = 0;
    [ObservableProperty] private int _offlineCount = 0;
    [ObservableProperty] private int _blockedCount = 0;

    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _statusMessage = "Готов к сканированию";

    // Modal / Dialog for Static Lease
    [ObservableProperty] private bool _isStaticDialogOpen = false;
    [ObservableProperty] private NetworkClient? _dialogClient;
    [ObservableProperty] private string _dialogCustomName = string.Empty;
    [ObservableProperty] private string _dialogStaticIp = string.Empty;

    public ClientsViewModel(IClientManagerService clientManager, ISshService ssh)
    {
        _clientManager = clientManager;
        _ssh = ssh;
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    public async Task LoadClientsAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Опрос роутера и беспроводных станций...";

        try
        {
            var list = await _clientManager.GetClientsAsync();
            var dispatcher = Application.Current?.Dispatcher;
            Action updateAction = () =>
            {
                Clients.Clear();
                foreach (var c in list) Clients.Add(c);
                TotalCount = Clients.Count;
                OnlineCount = Clients.Count(x => x.IsOnline);
                Wifi5Count = Clients.Count(x => x.IsOnline && x.Band == "5 GHz");
                Wifi24Count = Clients.Count(x => x.IsOnline && x.Band == "2.4 GHz");
                LanCount = Clients.Count(x => x.IsOnline && x.Band == "LAN");
                OfflineCount = Clients.Count(x => !x.IsOnline);
                BlockedCount = Clients.Count(x => x.IsBlocked);
                ApplyFilter();
            };

            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(updateAction);
            }
            else
            {
                updateAction();
            }

            StatusMessage = $"Обновлено: {DateTime.Now:HH:mm:ss} • В сети: {OnlineCount} из {TotalCount} (5 GHz: {Wifi5Count}, 2.4 GHz: {Wifi24Count}, LAN: {LanCount}, Офлайн: {OfflineCount})";
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

    private void ApplyFilter()
    {
        var q = SearchQuery?.Trim().ToLowerInvariant() ?? "";
        var filtered = Clients.Where(c =>
        {
            // Search filter
            var matchesSearch = string.IsNullOrWhiteSpace(q) ||
                (c.DisplayName?.ToLowerInvariant().Contains(q) ?? false) ||
                (c.IpAddress?.ToLowerInvariant().Contains(q) ?? false) ||
                (c.MacAddress?.ToLowerInvariant().Contains(q) ?? false);

            if (!matchesSearch) return false;

            // Category filter
            return SelectedFilter switch
            {
                "В сети" => c.IsOnline,
                "5 GHz" => c.IsOnline && c.Band == "5 GHz",
                "2.4 GHz" => c.IsOnline && c.Band == "2.4 GHz",
                "LAN" => c.IsOnline && c.Band == "LAN",
                "Офлайн" => !c.IsOnline,
                "Заблокировано" => c.IsBlocked,
                _ => true
            };
        }).ToList();

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() =>
            {
                FilteredClients.Clear();
                foreach (var item in filtered) FilteredClients.Add(item);
            });
        }
        else
        {
            FilteredClients.Clear();
            foreach (var item in filtered) FilteredClients.Add(item);
        }
    }

    [RelayCommand]
    public void SetFilter(string filter)
    {
        SelectedFilter = filter;
    }

    [RelayCommand]
    public async Task KickClientAsync(NetworkClient? client)
    {
        if (client == null) return;
        StatusMessage = $"Сброс соединения для {client.DisplayName}...";
        var res = await _clientManager.KickClientAsync(client.MacAddress);
        StatusMessage = res.Message;
        client.IsOnline = false;
        OnlineCount = Clients.Count(x => x.IsOnline);
        OfflineCount = Clients.Count(x => !x.IsOnline);
        Wifi5Count = Clients.Count(x => x.IsOnline && x.Band == "5 GHz");
        Wifi24Count = Clients.Count(x => x.IsOnline && x.Band == "2.4 GHz");
        LanCount = Clients.Count(x => x.IsOnline && x.Band == "LAN");
        ApplyFilter();
        await Task.Delay(2500);
        await LoadClientsAsync();
    }

    [RelayCommand]
    public async Task ToggleBlockInternetAsync(NetworkClient? client)
    {
        if (client == null) return;
        var nextBlock = !client.IsBlocked;
        StatusMessage = nextBlock ? $"Блокировка интернета для {client.DisplayName}..." : $"Разблокировка {client.DisplayName}...";
        
        var res = await _clientManager.BlockClientInternetAsync(client.MacAddress, nextBlock);
        StatusMessage = res.Message;
        client.IsBlocked = nextBlock;
        BlockedCount = Clients.Count(x => x.IsBlocked);
        ApplyFilter();
    }

    [RelayCommand]
    public void OpenStaticDialog(NetworkClient? client)
    {
        if (client == null) return;
        DialogClient = client;
        DialogCustomName = !string.IsNullOrWhiteSpace(client.CustomName) ? client.CustomName : (client.Hostname != "*" ? client.Hostname : "");
        DialogStaticIp = client.IpAddress != "N/A" ? client.IpAddress : "192.168.10.";
        IsStaticDialogOpen = true;
    }

    [RelayCommand]
    public void CloseStaticDialog()
    {
        IsStaticDialogOpen = false;
        DialogClient = null;
    }

    [RelayCommand]
    public async Task SaveStaticLeaseAsync()
    {
        if (DialogClient == null) return;
        StatusMessage = $"Фиксация статического IP {DialogStaticIp}...";
        var res = await _clientManager.SetStaticLeaseAsync(DialogClient.MacAddress, DialogStaticIp, DialogCustomName);
        StatusMessage = res.Message;
        IsStaticDialogOpen = false;
        await LoadClientsAsync();
    }

    [RelayCommand]
    public async Task DeleteStaticLeaseAsync(NetworkClient? client)
    {
        if (client == null) return;
        StatusMessage = $"Удаление фиксации статического IP для {client.DisplayName}...";
        var res = await _clientManager.DeleteStaticLeaseAsync(client.MacAddress);
        StatusMessage = res.Message;
        await LoadClientsAsync();
    }

    [RelayCommand]
    public void CopyIp(string? ip)
    {
        if (!string.IsNullOrWhiteSpace(ip) && ip != "N/A")
        {
            Clipboard.SetText(ip);
            StatusMessage = $"IP-адрес {ip} скопирован в буфер обмена";
        }
    }

    [RelayCommand]
    public void CopyMac(string? mac)
    {
        if (!string.IsNullOrWhiteSpace(mac))
        {
            Clipboard.SetText(mac);
            StatusMessage = $"MAC-адрес {mac} скопирован в буфер обмена";
        }
    }
}
