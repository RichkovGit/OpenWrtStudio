using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface ICronSchedulerService
{
    Task<List<CronJobItem>> GetCronJobsAsync();
    Task<(bool Success, string Message)> SaveCronJobsAsync(List<CronJobItem> jobs);
    Task<(bool Success, string Message)> ExecuteJobNowAsync(CronJobItem job);
    Task<(bool Success, string Message)> EnableCronServiceAsync();
    Task<bool> IsCronRunningAsync();
}

public class CronSchedulerService : ICronSchedulerService
{
    private readonly ISshService _ssh;

    public CronSchedulerService(ISshService ssh)
    {
        _ssh = ssh;
    }

    public async Task<List<CronJobItem>> GetCronJobsAsync()
    {
        var list = new List<CronJobItem>();
        if (!_ssh.IsConnected) return list;

        var (code, outStr, _) = await _ssh.ExecuteCommandAsync("cat /etc/crontabs/root 2>/dev/null", 5);
        if (code != 0 || string.IsNullOrWhiteSpace(outStr)) return list;

        var lines = outStr.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            var item = CronJobItem.FromCrontabLine(trimmed);
            if (!string.IsNullOrWhiteSpace(item.Command))
            {
                list.Add(item);
            }
        }

        return list;
    }

    public async Task<(bool Success, string Message)> SaveCronJobsAsync(List<CronJobItem> jobs)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var sb = new StringBuilder();
        foreach (var j in jobs)
        {
            sb.AppendLine(j.ToCrontabLine());
        }

        var content = sb.ToString();
        var (upSuccess, upMsg) = await _ssh.UploadFileContentAsync(content, "/etc/crontabs/root");
        if (!upSuccess) return (false, $"Не удалось сохранить /etc/crontabs/root: {upMsg}");

        // Restart cron service to reload jobs
        var (code, _, err) = await _ssh.ExecuteCommandAsync("/etc/init.d/cron enable && /etc/init.d/cron restart", 10);
        if (code == 0) return (true, "Расписания успешно сохранены и служба планировщика перезапущена!");
        return (false, $"Расписание сохранено, но возникла ошибка перезапуска cron: {err}");
    }

    public async Task<(bool Success, string Message)> ExecuteJobNowAsync(CronJobItem job)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(job.Command, 20);
        if (code == 0) return (true, $"Задача успешно выполнена!\n{outStr}");
        return (false, $"Ошибка выполнения команды: {err}\n{outStr}");
    }

    public async Task<(bool Success, string Message)> EnableCronServiceAsync()
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync("/etc/init.d/cron enable && /etc/init.d/cron start", 10);
        if (code == 0) return (true, "Служба cron успешно включена и запущена!");
        return (false, $"Ошибка запуска cron: {err}\n{outStr}");
    }

    public async Task<bool> IsCronRunningAsync()
    {
        if (!_ssh.IsConnected) return false;
        var (code, outStr, _) = await _ssh.ExecuteCommandAsync("pidof crond 2>/dev/null || pgrep crond 2>/dev/null", 5);
        return code == 0 && !string.IsNullOrWhiteSpace(outStr);
    }
}
