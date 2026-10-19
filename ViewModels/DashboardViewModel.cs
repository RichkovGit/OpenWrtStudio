using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
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
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private int _connectionGeneration;
    private DateTimeOffset _snapshotCapturedAt;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    private bool _hasSnapshot;

    [ObservableProperty]
    private string _lastUpdatedText = "Данные появятся после подключения";

    public bool HasActionStatus => !string.IsNullOrWhiteSpace(ActionStatus);
    partial void OnActionStatusChanged(string value) => OnPropertyChanged(nameof(HasActionStatus));

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

    public DashboardViewModel(IRouterService routerService, ISshService ssh, Action<Action>? dispatchToUi = null)
    {
        _routerService = routerService;
        _ssh = ssh;
        IsConnected = ssh.IsConnected;
        _ssh.ConnectionChanged += (_, connected) =>
        {
            Interlocked.Increment(ref _connectionGeneration);
            void Reset()
            {
                IsConnected = connected;
                HasSnapshot = false;
                Info = new();
                Interfaces.Clear();
                ActionStatus = "";
                LastUpdatedText = "Данные появятся после подключения";
            }
            if (dispatchToUi != null)
                dispatchToUi(Reset);
            else if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
                dispatcher.Invoke(Reset);
            else Reset();
        };

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
        if (!_ssh.IsConnected || IsLoading || !await _refreshLock.WaitAsync(0)) return;
        var generation = _connectionGeneration;
        IsLoading = true;
        try
        {
            var updated = await _routerService.GetRouterInfoAsync();
            if (!_ssh.IsConnected || generation != _connectionGeneration) return;
            Info = updated;
            Interfaces = new ObservableCollection<NetworkInterfaceItem>(updated.Interfaces);
            HasSnapshot = true;
            _snapshotCapturedAt = DateTimeOffset.Now;
            LastUpdatedText = $"Обновлено в {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            if (_ssh.IsConnected && generation == _connectionGeneration)
                ActionStatus = $"Ошибка обновления: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _refreshLock.Release();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSnapshot))]
    private async Task ExportReportAsync()
    {
        if (!HasSnapshot) return;
        var report = SupportReportService.Build(Info, typeof(App).Assembly.GetName().Version!, _snapshotCapturedAt);
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить отчёт OpenWrt Studio",
            Filter = "Текстовый отчёт (*.txt)|*.txt",
            FileName = $"OpenWrtStudio-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, report);
            ActionStatus = "Отчёт сохранён. IP/MAC-адреса, пароли и ключи не включены.";
        }
        catch (Exception ex) { ActionStatus = $"Не удалось сохранить отчёт: {ex.Message}"; }
    }

    [RelayCommand]
    public async Task RebootAsync()
    {
        if (!_ssh.IsConnected || IsLoading) return;
        if (System.Windows.MessageBox.Show("Перезагрузить роутер? Соединение с интернетом временно прервётся.",
            "OpenWrt Studio", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) return;
        await RunOperationAsync("Перезагрузка роутера...", _routerService.RebootAsync);
    }

    [RelayCommand]
    public async Task RestartNetworkAsync()
    {
        await RunOperationAsync("Перезапуск сетевого стека...", _routerService.RestartNetworkAsync);
        await RefreshDashboardAsync();
    }

    [RelayCommand]
    public async Task RestartDnsAsync()
    {
        await RunOperationAsync("Перезапуск службы DNS...", _routerService.RestartDnsAsync);
    }

    [RelayCommand]
    public async Task FlushDnsCacheAsync()
    {
        await RunOperationAsync("Сброс кэша DNS...", _routerService.FlushDnsCacheAsync);
    }

    private async Task RunOperationAsync(string status, Func<Task<(bool Success, string Message)>> operation)
    {
        if (!_ssh.IsConnected || IsLoading) return;
        IsLoading = true;
        var generation = _connectionGeneration;
        ActionStatus = status;
        try
        {
            var result = await operation();
            if (generation == _connectionGeneration) ActionStatus = result.Message;
        }
        catch (Exception ex)
        {
            if (generation == _connectionGeneration) ActionStatus = $"Ошибка операции: {ex.Message}";
        }
        finally { IsLoading = false; }
    }
}
