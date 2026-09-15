using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;

namespace OpenWrtStudio.ViewModels;

public partial class DiagnosticsViewModel : ObservableObject
{
    private readonly IDiagnosticsService _diagService;
    private readonly ISshService _ssh;
    private CancellationTokenSource? _pingCts;
    private CancellationTokenSource? _traceCts;

    // --- Health Check / Доктор роутера ---
    [ObservableProperty]
    private ObservableCollection<HealthCheckItem> _healthItems = new();

    [ObservableProperty]
    private bool _isAuditing;

    [ObservableProperty]
    private string _auditSummary = "Запустите аудит для автоматической проверки здоровья роутера и сети.";

    // --- Ping Tool ---
    [ObservableProperty]
    private string _pingHost = "1.1.1.1";

    [ObservableProperty]
    private int _pingCount = 5;

    [ObservableProperty]
    private string _selectedPingCountPreset = "5";

    [ObservableProperty]
    private ObservableCollection<string> _availableInterfaces = new() { "По умолчанию (Авто)" };

    [ObservableProperty]
    private string _selectedInterface = "По умолчанию (Авто)";

    [ObservableProperty]
    private int _packetSize = 56;

    [ObservableProperty]
    private string _extraPingArgs = "";

    [ObservableProperty]
    private ObservableCollection<PingResultItem> _pingResults = new();

    [ObservableProperty]
    private bool _isPinging;

    [ObservableProperty]
    private string _pingSummary = "";

    // --- Traceroute Tool ---
    [ObservableProperty]
    private string _traceHost = "google.com";

    [ObservableProperty]
    private ObservableCollection<TracerouteHop> _traceHops = new();

    [ObservableProperty]
    private bool _isTracing;

    // --- System & Kernel Logs ---
    [ObservableProperty]
    private ObservableCollection<LogEntry> _allLogs = new();

    [ObservableProperty]
    private ObservableCollection<LogEntry> _filteredLogs = new();

    [ObservableProperty]
    private string _logFilter = "";

    [ObservableProperty]
    private bool _isKernelLogSelected;

    [ObservableProperty]
    private bool _isLoadingLogs;

    // --- Embedded Terminal Console ---
    [ObservableProperty]
    private string _terminalInput = "";

    [ObservableProperty]
    private string _terminalOutput = "Добро пожаловать в терминал OpenWrt. Введите команду (например: ip a, logread, top, opkg update)...\n";

    [ObservableProperty]
    private bool _isExecutingTerminal;

    public DiagnosticsViewModel(IDiagnosticsService diagService, ISshService ssh)
    {
        _diagService = diagService;
        _ssh = ssh;
        _ssh.ConnectionChanged += (s, connected) =>
        {
            if (connected)
            {
                _ = LoadAvailableInterfacesAsync();
            }
        };
    }

    partial void OnSelectedPingCountPresetChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var clean = value.Trim();
        if (clean.Contains("∞") || clean.Contains("Бесконечно") || clean == "0")
        {
            PingCount = 0;
        }
        else
        {
            var match = System.Text.RegularExpressions.Regex.Match(clean, @"\d+");
            if (match.Success && int.TryParse(match.Value, out var n))
            {
                PingCount = n;
            }
        }
    }

    [RelayCommand]
    public async Task LoadAvailableInterfacesAsync()
    {
        if (!_ssh.IsConnected) return;
        try
        {
            var list = await _diagService.GetNetworkInterfacesAsync();
            AvailableInterfaces = new ObservableCollection<string>(list);
            if (!AvailableInterfaces.Contains(SelectedInterface))
            {
                SelectedInterface = AvailableInterfaces.FirstOrDefault() ?? "По умолчанию (Авто)";
            }
        }
        catch { }
    }

    [RelayCommand]
    public async Task RunHealthAuditAsync()
    {
        if (!_ssh.IsConnected) return;

        IsAuditing = true;
        AuditSummary = "Выполняется диагностика компонентов роутера, сети и служб...";
        HealthItems.Clear();
        _ = LoadAvailableInterfacesAsync();

        try
        {
            var results = await _diagService.RunHealthAuditAsync();
            HealthItems = new ObservableCollection<HealthCheckItem>(results);

            int dangerCount = results.Count(x => x.Status == HealthStatus.Danger);
            int warnCount = results.Count(x => x.Status == HealthStatus.Warning);
            int goodCount = results.Count(x => x.Status == HealthStatus.Good);

            if (dangerCount > 0)
                AuditSummary = $"Внимание! Обнаружено критических проблем: {dangerCount}, предупреждений: {warnCount}. Рекомендуется применить исправления.";
            else if (warnCount > 0)
                AuditSummary = $"Аудит завершен: обнаружено предупреждений: {warnCount}. Система функционирует стабильно.";
            else
                AuditSummary = $"Отлично! Все компоненты роутера в полном порядке ({goodCount} проверок пройдено).";
        }
        catch (Exception ex)
        {
            AuditSummary = $"Ошибка при выполнении аудита: {ex.Message}";
        }
        finally
        {
            IsAuditing = false;
        }
    }

    [RelayCommand]
    public async Task FixIssueAsync(HealthCheckItem? item)
    {
        if (item == null || !_ssh.IsConnected) return;

        item.StatusText = "Исправление...";
        var (success, msg) = await _diagService.ExecuteFixAsync(item);
        if (success)
        {
            item.Status = HealthStatus.Good;
            item.StatusText = "Исправлено";
            item.Description += " [Исправление успешно применено!]";
            item.FixCommand = "";
        }
        else
        {
            item.StatusText = "Ошибка исправления";
            item.Description += $" [Ошибка: {msg}]";
        }
    }

    [RelayCommand]
    public async Task StartPingAsync()
    {
        if (!_ssh.IsConnected || string.IsNullOrWhiteSpace(PingHost)) return;

        IsPinging = true;
        PingResults.Clear();

        var countText = PingCount == 0 ? "непрерывно (∞)" : $"{PingCount} пакетов";
        var ifaceText = SelectedInterface != "По умолчанию (Авто)" && !string.IsNullOrWhiteSpace(SelectedInterface) 
            ? $" через {SelectedInterface}" 
            : "";
        PingSummary = $"Отправка {countText} на {PingHost}{ifaceText}...";

        _pingCts = new CancellationTokenSource();
        try
        {
            await _diagService.RunPingAsync(
                PingHost.Trim(), 
                PingCount, 
                SelectedInterface, 
                PacketSize, 
                ExtraPingArgs, 
                result =>
                {
                    App.Current?.Dispatcher?.Invoke(() =>
                    {
                        PingResults.Add(result);
                        if (PingCount == 0)
                        {
                            var succ = PingResults.Count(p => p.Success);
                            var l = (PingResults.Count - succ) * 100.0 / PingResults.Count;
                            PingSummary = $"Непрерывный пинг: отправлено {PingResults.Count}, получено {succ} (потери: {l:0}%)";
                        }
                    });
                }, 
                _pingCts.Token);

            var successCount = PingResults.Count(p => p.Success);
            var loss = PingCount > 0 ? (PingCount - successCount) * 100.0 / PingCount : 0;
            PingSummary = $"Пинг завершен: получено {successCount} из {PingResults.Count} (потери: {loss:0}%)";
        }
        catch (OperationCanceledException)
        {
            PingSummary = "Пинг прерван пользователем.";
        }
        catch (Exception ex)
        {
            if (!_pingCts.IsCancellationRequested)
                PingSummary = $"Ошибка пинга: {ex.Message}";
        }
        finally
        {
            IsPinging = false;
        }
    }

    [RelayCommand]
    public void StopPing()
    {
        if (!IsPinging) return;
        _pingCts?.Cancel();
        IsPinging = false;

        var successCount = PingResults.Count(p => p.Success);
        var total = PingResults.Count;
        var loss = total > 0 ? (total - successCount) * 100.0 / total : 0;
        PingSummary = $"Пинг остановлен пользователем (получено {successCount} из {total}, потери: {loss:0}%).";

        _ = Task.Run(async () =>
        {
            try
            {
                await _ssh.ExecuteCommandAsync("killall -INT ping 2>/dev/null", 2);
            }
            catch { }
        });
    }

    [RelayCommand]
    public async Task StartTracerouteAsync()
    {
        if (!_ssh.IsConnected || string.IsNullOrWhiteSpace(TraceHost)) return;

        IsTracing = true;
        TraceHops.Clear();
        _traceCts = new CancellationTokenSource();

        try
        {
            await _diagService.RunTracerouteAsync(TraceHost.Trim(), hop =>
            {
                App.Current?.Dispatcher?.Invoke(() =>
                {
                    TraceHops.Add(hop);
                });
            }, _traceCts.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Traceroute error: {ex.Message}");
        }
        finally
        {
            IsTracing = false;
        }
    }

    [RelayCommand]
    public void StopTraceroute()
    {
        if (!IsTracing) return;
        _traceCts?.Cancel();
        IsTracing = false;

        _ = Task.Run(async () =>
        {
            try
            {
                await _ssh.ExecuteCommandAsync("killall -INT traceroute 2>/dev/null", 2);
            }
            catch { }
        });
    }

    [RelayCommand]
    public async Task RefreshLogsAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoadingLogs = true;
        try
        {
            var logs = IsKernelLogSelected
                ? await _diagService.GetKernelLogsAsync(150)
                : await _diagService.GetSystemLogsAsync(200);

            AllLogs = new ObservableCollection<LogEntry>(logs);
            ApplyLogFilter();
        }
        finally
        {
            IsLoadingLogs = false;
        }
    }

    partial void OnLogFilterChanged(string value) => ApplyLogFilter();
    partial void OnIsKernelLogSelectedChanged(bool value) => _ = RefreshLogsAsync();

    private void ApplyLogFilter()
    {
        if (string.IsNullOrWhiteSpace(LogFilter))
        {
            FilteredLogs = new ObservableCollection<LogEntry>(AllLogs);
        }
        else
        {
            var q = LogFilter.Trim().ToLowerInvariant();
            var matched = AllLogs.Where(l =>
                l.Message.ToLowerInvariant().Contains(q) ||
                l.Process.ToLowerInvariant().Contains(q) ||
                l.Level.ToLowerInvariant().Contains(q));
            FilteredLogs = new ObservableCollection<LogEntry>(matched);
        }
    }

    [RelayCommand]
    public async Task ExecuteTerminalCommandAsync()
    {
        if (!_ssh.IsConnected || string.IsNullOrWhiteSpace(TerminalInput)) return;

        var cmd = TerminalInput.Trim();
        TerminalInput = "";
        IsExecutingTerminal = true;
        TerminalOutput += $"\n# {cmd}\n";

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(cmd, 30);
        if (!string.IsNullOrEmpty(outStr))
        {
            TerminalOutput += outStr + "\n";
        }
        if (!string.IsNullOrEmpty(err))
        {
            TerminalOutput += $"[stderr]: {err}\n";
        }
        IsExecutingTerminal = false;
    }

    [RelayCommand]
    public void ClearTerminal()
    {
        TerminalOutput = "";
    }
}
