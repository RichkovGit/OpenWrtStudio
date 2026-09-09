using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;

namespace OpenWrtStudio.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IRouterService _routerService;
    private readonly ISshService _ssh;
    private readonly DispatcherTimer _pollTimer;

    [ObservableProperty]
    private RouterInfo _info = new();

    [ObservableProperty]
    private ObservableCollection<NetworkInterfaceItem> _interfaces = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _actionStatus = "";

    [ObservableProperty]
    private bool _autoRefresh = true;

    public DashboardViewModel(IRouterService routerService, ISshService ssh)
    {
        _routerService = routerService;
        _ssh = ssh;

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(6)
        };
        _pollTimer.Tick += async (_, _) =>
        {
            if (_ssh.IsConnected && AutoRefresh && !IsLoading)
            {
                await RefreshDashboardAsync(silent: true);
            }
        };
        _pollTimer.Start();
    }

    [RelayCommand]
    public async Task RefreshDashboardAsync(bool silent = false)
    {
        if (!_ssh.IsConnected) return;

        if (!silent) IsLoading = true;
        try
        {
            var updated = await _routerService.GetRouterInfoAsync();
            Info = updated;
            Interfaces = new ObservableCollection<NetworkInterfaceItem>(updated.Interfaces);
        }
        catch (Exception ex)
        {
            ActionStatus = $"Ошибка обновления: {ex.Message}";
        }
        finally
        {
            if (!silent) IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RebootAsync()
    {
        if (!_ssh.IsConnected) return;
        IsLoading = true;
        ActionStatus = "Перезагрузка роутера...";
        var (success, msg) = await _routerService.RebootAsync();
        ActionStatus = msg;
        IsLoading = false;
    }

    [RelayCommand]
    public async Task RestartNetworkAsync()
    {
        if (!_ssh.IsConnected) return;
        IsLoading = true;
        ActionStatus = "Перезапуск сетевого стека...";
        var (success, msg) = await _routerService.RestartNetworkAsync();
        ActionStatus = msg;
        IsLoading = false;
        await RefreshDashboardAsync();
    }

    [RelayCommand]
    public async Task RestartDnsAsync()
    {
        if (!_ssh.IsConnected) return;
        IsLoading = true;
        ActionStatus = "Перезапуск службы DNS...";
        var (success, msg) = await _routerService.RestartDnsAsync();
        ActionStatus = msg;
        IsLoading = false;
    }

    [RelayCommand]
    public async Task FlushDnsCacheAsync()
    {
        if (!_ssh.IsConnected) return;
        IsLoading = true;
        ActionStatus = "Сброс кэша DNS...";
        var (success, msg) = await _routerService.FlushDnsCacheAsync();
        ActionStatus = msg;
        IsLoading = false;
    }
}
