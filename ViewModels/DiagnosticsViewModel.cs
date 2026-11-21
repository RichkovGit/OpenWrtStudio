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
    private readonly ISentinelService _sentinel;
    private CancellationTokenSource? _pingCts;
    private CancellationTokenSource? _traceCts;
    private CancellationTokenSource? _fixAllCts;

    // --- Sentinel / авто-восстановление ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFixAll))]
    private SentinelIssue? _activeIssue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFixAll))]
    private bool _isFixingAll;

    [ObservableProperty] private string _fixAllStatus = "Sentinel следит за здоровьем роутера в фоне. Запустите проверку или исправьте всё одним кликом.";
    [ObservableProperty] private string _fixAllReportText = "";
    [ObservableProperty] private ObservableCollection<SentinelFixStepResult> _fixAllSteps = new();

    public bool HasActiveIssue => ActiveIssue != null;
    public bool CanFixAll => HasActiveIssue && !IsFixingAll;
    partial void OnActiveIssueChanged(SentinelIssue? value) => OnPropertyChanged(nameof(HasActiveIssue));

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

    public DiagnosticsViewModel(IDiagnosticsService diagService, ISshService ssh, ISentinelService sentinel)
    {
        _diagService = diagService;
        _ssh = ssh;
        _sentinel = sentinel;
        _ssh.ConnectionChanged += (s, connected) =>
        {
            if (connected)
            {
                _ = LoadAvailableInterfacesAsync();
            }
            else
            {
                ActiveIssue = null;
            }
        };
        _sentinel.IssueChanged += (s, issue) =>
        {
            ActiveIssue = _ssh.IsConnected ? issue : null;
        };
    }

    [RelayCommand]
    public async Task RefreshSentinelAsync()
    {
        if (!_ssh.IsConnected)
        {
            FixAllStatus = "Подключитесь к роутеру для проверки здоровья сети.";
            return;
        }
        FixAllStatus = "Sentinel: проверка WAN, DNS и служб...";
        var issue = await _sentinel.ProbeHealthAsync();
        ActiveIssue = issue;
        FixAllStatus = issue == null
            ? "Sentinel: критических сбоев не обнаружено."
            : $"Sentinel обнаружил проблему: {issue.Title}";
    }

    [RelayCommand]
    public async Task FixAllAsync()
    {
        var issue = ActiveIssue;
        if (issue == null || !_ssh.IsConnected || IsFixingAll) return;

        var cts = new CancellationTokenSource();
        System.Threading.Interlocked.Exchange(ref _fixAllCts, cts)?.Dispose();
        IsFixingAll = true;
        FixAllSteps.Clear();
        FixAllReportText = "";
        FixAllStatus = "Исправление: запуск методов восстановления...";

        try
        {
            var progress = new ActionProgress<SentinelFixProgress>(p =>
            {
                FixAllStatus = $"Шаг {p.StepIndex}/{p.TotalSteps}: {p.MethodName} — {p.Status}";
            });
            var report = await _sentinel.FixAllAsync(issue, progress, cts.Token);
            FixAllSteps = new ObservableCollection<SentinelFixStepResult>(report.Steps);
            FixAllReportText = BuildFixAllReportText(report);
            FixAllStatus = report.Summary;
            if (report.Resolved) ActiveIssue = null;
            else if (_ssh.IsConnected) ActiveIssue = _sentinel.CurrentIssue;
        }
        catch (OperationCanceledException)
        {
            FixAllStatus = "Исправление отменено пользователем.";
        }
        catch (Exception ex)
        {
            FixAllStatus = $"Ошибка исправления: {ex.Message}";
        }
        finally
        {
            IsFixingAll = false;
            if (ReferenceEquals(_fixAllCts, cts)) _fixAllCts = null;
            cts.Dispose();
        }
    }

    [RelayCommand]
    public void CancelFixAll()
    {
        try { _fixAllCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    internal static string BuildFixAllReportText(SentinelFixAllReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Отчёт авто-исправления Sentinel");
        sb.AppendLine(report.Summary);
        foreach (var step in report.Steps)
        {
            sb.AppendLine($"{(step.Success ? "[OK]" : "[ОШИБКА]")} {step.MethodName}: {step.Message}");
        }
        return sb.ToString();
    }

    internal static string BuildAuditReportText(IEnumerable<HealthCheckItem> items, string auditSummary)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Отчёт диагностики OpenWrt Studio");
        sb.AppendLine($"Время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(auditSummary);
        sb.AppendLine();
        foreach (var item in items)
        {
            sb.AppendLine($"[{item.Status}] {item.Title} ({item.Category}): {item.StatusText}");
            if (!string.IsNullOrWhiteSpace(item.Description)) sb.AppendLine($"    {item.Description}");
            if (!string.IsNullOrWhiteSpace(item.Recommendation)) sb.AppendLine($"    Совет: {item.Recommendation}");
        }
        return sb.ToString();
    }

    [RelayCommand]
    public void CopyDiagnosticReport()
    {
        try
        {
            var text = BuildAuditReportText(HealthItems, AuditSummary);
            if (!string.IsNullOrWhiteSpace(FixAllReportText))
                text += Environment.NewLine + FixAllReportText;
            System.Windows.Clipboard.SetText(text);
            FixAllStatus = "Отчёт диагностики скопирован в буфер обмена.";
        }
        catch (Exception ex)
        {
            FixAllStatus = $"Не удалось скопировать отчёт: {ex.Message}";
        }
    }

    private sealed class ActionProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public ActionProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
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

        var pingCts = new CancellationTokenSource();
        System.Threading.Interlocked.Exchange(ref _pingCts, pingCts)?.Dispose();
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
                pingCts.Token);

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
            if (!pingCts.IsCancellationRequested)
                PingSummary = $"Ошибка пинга: {ex.Message}";
        }
        finally
        {
            IsPinging = false;
            if (ReferenceEquals(_pingCts, pingCts)) _pingCts = null;
            pingCts.Dispose();
        }
    }

    [RelayCommand]
    public void StopPing()
    {
        if (!IsPinging) return;
        try { _pingCts?.Cancel(); } catch (ObjectDisposedException) { }
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
        var traceCts = new CancellationTokenSource();
        System.Threading.Interlocked.Exchange(ref _traceCts, traceCts)?.Dispose();

        try
        {
            await _diagService.RunTracerouteAsync(TraceHost.Trim(), hop =>
            {
                App.Current?.Dispatcher?.Invoke(() =>
                {
                    TraceHops.Add(hop);
                });
            }, traceCts.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Traceroute error: {ex.Message}");
        }
        finally
        {
            IsTracing = false;
            if (ReferenceEquals(_traceCts, traceCts)) _traceCts = null;
            traceCts.Dispose();
        }
    }

    [RelayCommand]
    public void StopTraceroute()
    {
        if (!IsTracing) return;
        try { _traceCts?.Cancel(); } catch (ObjectDisposedException) { }
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
