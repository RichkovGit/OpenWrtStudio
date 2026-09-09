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

    [ObservableProperty] private ForkopFeedInfo _feedInfo = new();
    [ObservableProperty] private ObservableCollection<ForkopComponentItem> _components = new();
    [ObservableProperty] private ForkopComponentItem? _selectedComponent;
    [ObservableProperty] private ForkopDiagResult _diagResult = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isDiagnosing;
    [ObservableProperty] private string _statusMessage = "Готов к работе с компонентами ForkOP.";

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
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Подключитесь к роутеру по SSH для работы с ForkOP.";
            return;
        }

        IsLoading = true;
        StatusMessage = "Загрузка информации о репозиториях и компонентах ForkOP...";
        try
        {
            FeedInfo = await _forkopService.GetFeedInfoAsync();
            var comps = await _forkopService.GetComponentsAsync();
            Components = new ObservableCollection<ForkopComponentItem>(comps);
            StatusMessage = $"Компоненты загружены. Архитектура роутера: {FeedInfo.Architecture}.";
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
