using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;

namespace OpenWrtStudio.ViewModels;

public partial class SchedulerViewModel : ObservableObject
{
    private readonly ICronSchedulerService _cronService;
    private readonly ISshService _ssh;

    [ObservableProperty] private ObservableCollection<CronJobItem> _jobs = new();
    [ObservableProperty] private CronJobItem? _selectedJob;
    [ObservableProperty] private string _newSchedule = "0 4 * * *";
    [ObservableProperty] private string _newCommand = "reboot";
    [ObservableProperty] private string _newDescription = "Ежедневная ночная перезагрузка в 04:00";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isCronRunning;
    [ObservableProperty] private string _statusMessage = "Готов к работе с расписанием задач.";

    public SchedulerViewModel(ICronSchedulerService cronService, ISshService ssh)
    {
        _cronService = cronService;
        _ssh = ssh;
    }

    public async Task InitializeAsync()
    {
        await LoadJobsAsync();
    }

    [RelayCommand]
    public async Task LoadJobsAsync()
    {
        if (!_ssh.IsConnected)
        {
            StatusMessage = "Подключитесь к роутеру по SSH для просмотра расписаний.";
            return;
        }

        IsLoading = true;
        StatusMessage = "Чтение задач из /etc/crontabs/root...";
        try
        {
            var list = await _cronService.GetCronJobsAsync();
            Jobs = new ObservableCollection<CronJobItem>(list);
            IsCronRunning = await _cronService.IsCronRunningAsync();
            StatusMessage = $"Загружено задач: {Jobs.Count}. Служба cron: {(IsCronRunning ? "Активна" : "Остановлена")}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки задач: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SaveJobsAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Сохранение задач в /etc/crontabs/root и перезапуск cron...";
        var (success, msg) = await _cronService.SaveCronJobsAsync(new System.Collections.Generic.List<CronJobItem>(Jobs));
        StatusMessage = msg;
        IsLoading = false;

        if (success)
        {
            IsCronRunning = true;
        }
    }

    [RelayCommand]
    public void AddCustomJob()
    {
        if (string.IsNullOrWhiteSpace(NewSchedule) || string.IsNullOrWhiteSpace(NewCommand)) return;

        Jobs.Add(new CronJobItem
        {
            Schedule = NewSchedule.Trim(),
            Command = NewCommand.Trim(),
            Description = NewDescription.Trim(),
            IsEnabled = true
        });

        StatusMessage = "Задача добавлена в список. Нажмите 'Сохранить на роутере' для применения.";
    }

    [RelayCommand]
    public void DeleteJob(CronJobItem? job)
    {
        if (job != null)
        {
            Jobs.Remove(job);
            StatusMessage = "Задача удалена из списка. Не забудьте нажать 'Сохранить на роутере'.";
        }
    }

    [RelayCommand]
    public async Task ExecuteNowAsync(CronJobItem? job)
    {
        if (job == null || !_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = $"Выполнение задачи: {job.Command}...";
        var (success, msg) = await _cronService.ExecuteJobNowAsync(job);
        StatusMessage = msg;
        IsLoading = false;
    }

    [RelayCommand]
    public async Task EnableCronServiceAsync()
    {
        if (!_ssh.IsConnected) return;

        IsLoading = true;
        StatusMessage = "Запуск и включение в автозагрузку службы cron...";
        var (success, msg) = await _cronService.EnableCronServiceAsync();
        StatusMessage = msg;
        IsLoading = false;
        if (success) IsCronRunning = true;
    }

    // --- Пресеты в 1 клик ---
    [RelayCommand]
    public void AddNightRebootPreset()
    {
        Jobs.Add(new CronJobItem
        {
            Schedule = "0 4 * * *",
            Command = "reboot",
            Description = "Ежедневная ночная перезагрузка в 04:00 для очистки буферов",
            PresetType = "reboot"
        });
        StatusMessage = "Добавлен пресет: Ночная перезагрузка роутера.";
    }

    [RelayCommand]
    public void AddNightWifiOffPreset()
    {
        Jobs.Add(new CronJobItem
        {
            Schedule = "0 1 * * *",
            Command = "wifi down",
            Description = "Отключение Wi-Fi на ночь в 01:00 (снижение излучения и защита сети)",
            PresetType = "wifi_off"
        });
        Jobs.Add(new CronJobItem
        {
            Schedule = "0 7 * * *",
            Command = "wifi up",
            Description = "Включение Wi-Fi утром в 07:00",
            PresetType = "wifi_on"
        });
        StatusMessage = "Добавлен пресет: Ночной режим Wi-Fi (отключение с 01:00 до 07:00).";
    }

    [RelayCommand]
    public void AddSubUpdatePreset()
    {
        Jobs.Add(new CronJobItem
        {
            Schedule = "0 */6 * * *",
            Command = "/etc/init.d/mihomo reload 2>/dev/null",
            Description = "Автоматическое обновление подписок прокси каждые 6 часов",
            PresetType = "sub_update"
        });
        StatusMessage = "Добавлен пресет: Авто-обновление подписок каждые 6 часов.";
    }

    [RelayCommand]
    public void AddDropCachesPreset()
    {
        Jobs.Add(new CronJobItem
        {
            Schedule = "0 3 * * *",
            Command = "sync; echo 3 > /proc/sys/vm/drop_caches",
            Description = "Сброс кэша оперативной памяти Linux каждую ночь в 03:00",
            PresetType = "drop_cache"
        });
        StatusMessage = "Добавлен пресет: Очистка кэша памяти роутера.";
    }

    [RelayCommand]
    public void AddPingWatchdogPreset()
    {
        Jobs.Add(new CronJobItem
        {
            Schedule = "*/5 * * * *",
            Command = "ping -c 1 77.88.8.8 >/dev/null 2>&1 || (ifdown wan; sleep 2; ifup wan)",
            Description = "Проверка пинга каждые 5 мин и авто-переподключение WAN при обрыве",
            PresetType = "ping_watchdog"
        });
        StatusMessage = "Добавлен пресет: Сторож пинга (Ping Watchdog).";
    }
}
