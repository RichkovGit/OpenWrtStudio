using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface IDiagnosticsService
{
    Task<List<HealthCheckItem>> RunHealthAuditAsync();
    Task<(bool Success, string Message)> ExecuteFixAsync(HealthCheckItem item);
    Task RunPingAsync(string host, int count, Action<PingResultItem> onProgress, CancellationToken ct);
    Task RunTracerouteAsync(string host, Action<TracerouteHop> onHop, CancellationToken ct);
    Task<List<LogEntry>> GetSystemLogsAsync(int lines = 200);
    Task<List<LogEntry>> GetKernelLogsAsync(int lines = 150);
}

public class DiagnosticsService : IDiagnosticsService
{
    private readonly ISshService _ssh;

    public DiagnosticsService(ISshService ssh)
    {
        _ssh = ssh;
    }

    public async Task<List<HealthCheckItem>> RunHealthAuditAsync()
    {
        var items = new List<HealthCheckItem>();
        if (!_ssh.IsConnected)
        {
            items.Add(new HealthCheckItem
            {
                Title = "Связь с роутером",
                Category = "Соединение",
                Status = HealthStatus.Danger,
                StatusText = "Не подключен",
                Description = "Нет связи по SSH с роутером.",
                Recommendation = "Проверьте IP-адрес роутера и учетные данные."
            });
            return items;
        }

        // 1. WAN & Internet Check
        var (pingCode, pingOut, _) = await _ssh.ExecuteCommandAsync("ping -c 2 -W 2 1.1.1.1 2>/dev/null", 6);
        if (pingCode == 0)
        {
            items.Add(new HealthCheckItem
            {
                Title = "Доступ к интернету (WAN)",
                Category = "Сеть",
                Status = HealthStatus.Good,
                StatusText = "Подключен к интернету",
                Description = "Внешние серверы отвечают стабильно."
            });
        }
        else
        {
            items.Add(new HealthCheckItem
            {
                Title = "Доступ к интернету (WAN)",
                Category = "Сеть",
                Status = HealthStatus.Danger,
                StatusText = "Нет связи с интернетом",
                Description = "Пакеты до внешних IP (1.1.1.1) не доходят.",
                Recommendation = "Перезапустите сетевой интерфейс или проверьте кабель провайдера.",
                FixCommand = "/etc/init.d/network restart"
            });
        }

        // 2. DNS Resolution Check
        var (dnsCode, dnsOut, _) = await _ssh.ExecuteCommandAsync("nslookup google.com 127.0.0.1 2>/dev/null || nslookup ya.ru 127.0.0.1 2>/dev/null", 6);
        if (dnsCode == 0 && (dnsOut.Contains("Address") || dnsOut.Contains("Name:")))
        {
            items.Add(new HealthCheckItem
            {
                Title = "Служба резолвинга DNS",
                Category = "DNS",
                Status = HealthStatus.Good,
                StatusText = "DNS работает корректно",
                Description = "Локальный DNS-резолвер успешно преобразует доменные имена."
            });
        }
        else
        {
            items.Add(new HealthCheckItem
            {
                Title = "Служба резолвинга DNS",
                Category = "DNS",
                Status = HealthStatus.Danger,
                StatusText = "Сбой DNS",
                Description = "Роутер не может разрешить доменные имена через локальный DNS (dnsmasq).",
                Recommendation = "Перезапустить dnsmasq и сбросить кэш.",
                FixCommand = "/etc/init.d/dnsmasq restart"
            });
        }

        // 3. Overlay (Flash memory) Check
        var (dfCode, dfOut, _) = await _ssh.ExecuteCommandAsync("df -k /overlay 2>/dev/null", 5);
        if (dfCode == 0)
        {
            var m = Regex.Match(dfOut, @"(\d+)\s+(\d+)\s+(\d+)%");
            if (m.Success && int.TryParse(m.Groups[3].Value, out var percent))
            {
                if (percent >= 95)
                {
                    items.Add(new HealthCheckItem
                    {
                        Title = "Память Overlay (Flash)",
                        Category = "Хранилище",
                        Status = HealthStatus.Danger,
                        StatusText = $"Критически заполнено ({percent}%)",
                        Description = "Хранилище роутера почти исчерпано. Это может привести к сбою настроек или окирпичиванию при перезагрузке.",
                        Recommendation = "Очистить временные списки пакетов opkg и удалить неиспользуемые пакеты.",
                        FixCommand = "rm -rf /tmp/opkg-lists/* /var/opkg-lists/*"
                    });
                }
                else if (percent >= 85)
                {
                    items.Add(new HealthCheckItem
                    {
                        Title = "Память Overlay (Flash)",
                        Category = "Хранилище",
                        Status = HealthStatus.Warning,
                        StatusText = $"Заполнено на {percent}%",
                        Description = "Свободного места остается мало.",
                        Recommendation = "Рекомендуется следить за установкой крупных плагинов.",
                        FixCommand = "rm -rf /tmp/opkg-lists/*"
                    });
                }
                else
                {
                    items.Add(new HealthCheckItem
                    {
                        Title = "Память Overlay (Flash)",
                        Category = "Хранилище",
                        Status = HealthStatus.Good,
                        StatusText = $"Свободно (занято {percent}%)",
                        Description = "Достаточно свободного места для работы системы."
                    });
                }
            }
        }

        // 4. Memory (RAM) pressure
        var (memCode, memOut, _) = await _ssh.ExecuteCommandAsync("cat /proc/meminfo", 5);
        if (memCode == 0)
        {
            long total = 0, free = 0, buffers = 0, cached = 0;
            foreach (var line in memOut.Split('\n'))
            {
                if (line.StartsWith("MemTotal:")) total = ParseKb(line);
                else if (line.StartsWith("MemFree:")) free = ParseKb(line);
                else if (line.StartsWith("Buffers:")) buffers = ParseKb(line);
                else if (line.StartsWith("Cached:")) cached = ParseKb(line);
            }

            long available = free + buffers + cached;
            if (total > 0)
            {
                double availPercent = (double)available / total * 100;
                if (availPercent < 10)
                {
                    items.Add(new HealthCheckItem
                    {
                        Title = "Оперативная память (RAM)",
                        Category = "Система",
                        Status = HealthStatus.Warning,
                        StatusText = $"Нехватка ОЗУ (свободно {availPercent:0}%)",
                        Description = "Память почти исчерпана, возможны перезапуски процессов через OOM killer.",
                        Recommendation = "Очистить кэши оперативной памяти ядра.",
                        FixCommand = "sync; echo 3 > /proc/sys/vm/drop_caches"
                    });
                }
                else
                {
                    items.Add(new HealthCheckItem
                    {
                        Title = "Оперативная память (RAM)",
                        Category = "Система",
                        Status = HealthStatus.Good,
                        StatusText = $"Норма (свободно {availPercent:0}%)",
                        Description = $"Доступно {available / 1024} МБ из {total / 1024} МБ."
                    });
                }
            }
        }

        // 5. Mihomo / Clash Service Check
        var (mihomoCode, mihomoOut, _) = await _ssh.ExecuteCommandAsync("pgrep -x mihomo 2>/dev/null || pgrep -x clash 2>/dev/null", 5);
        if (mihomoCode == 0 && !string.IsNullOrWhiteSpace(mihomoOut))
        {
            items.Add(new HealthCheckItem
            {
                Title = "Ядро Mihomo / Clash",
                Category = "Прокси",
                Status = HealthStatus.Good,
                StatusText = "Служба запущена",
                Description = $"Процесс Mihomo активен (PID: {mihomoOut.Trim().Split('\n')[0]})."
            });
        }
        else
        {
            // Check if installed
            var (instCode, _, _) = await _ssh.ExecuteCommandAsync("which mihomo 2>/dev/null || test -f /etc/init.d/mihomo", 5);
            if (instCode == 0)
            {
                items.Add(new HealthCheckItem
                {
                    Title = "Ядро Mihomo / Clash",
                    Category = "Прокси",
                    Status = HealthStatus.Warning,
                    StatusText = "Установлен, но остановлен",
                    Description = "Mihomo установлен на роутере, однако в данный момент не запущен.",
                    Recommendation = "Запустить службу Mihomo.",
                    FixCommand = "/etc/init.d/mihomo start"
                });
            }
            else
            {
                items.Add(new HealthCheckItem
                {
                    Title = "Ядро Mihomo",
                    Category = "Прокси",
                    Status = HealthStatus.Info,
                    StatusText = "Не установлен",
                    Description = "Mihomo не обнаружен на роутере. Вы можете установить его из раздела 'Плагины' или конструктора.",
                    Recommendation = "Перейдите во вкладку 'Плагины' для установки."
                });
            }
        }

        // 6. Firewall Check
        var (fwCode, _, _) = await _ssh.ExecuteCommandAsync("/etc/init.d/firewall status 2>/dev/null || iptables -L -n 2>/dev/null || nft list ruleset 2>/dev/null", 5);
        if (fwCode == 0)
        {
            items.Add(new HealthCheckItem
            {
                Title = "Брандмауэр (Firewall)",
                Category = "Безопасность",
                Status = HealthStatus.Good,
                StatusText = "Активен",
                Description = "Правила маршрутизации и фильтрации трафика активны."
            });
        }
        else
        {
            items.Add(new HealthCheckItem
            {
                Title = "Брандмауэр (Firewall)",
                Category = "Безопасность",
                Status = HealthStatus.Warning,
                StatusText = "Сбой брандмауэра",
                Description = "Служба firewall может быть отключена или не отвечает.",
                Recommendation = "Перезапустить брандмауэр.",
                FixCommand = "/etc/init.d/firewall restart"
            });
        }

        return items;
    }

    public async Task<(bool Success, string Message)> ExecuteFixAsync(HealthCheckItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FixCommand))
        {
            return (false, "Для данной проблемы нет автоматической команды исправления.");
        }

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(item.FixCommand, 20);
        return code == 0
            ? (true, $"Команда успешно выполнена: {item.FixCommand}")
            : (false, $"Ошибка выполнения: {err}");
    }

    public async Task RunPingAsync(string host, int count, Action<PingResultItem> onProgress, CancellationToken ct)
    {
        var cmd = $"ping -c {count} -W 2 {host}";
        int seq = 1;
        await _ssh.RunStreamingCommandAsync(cmd, line =>
        {
            var m = Regex.Match(line, @"from\s+([^\s:]+).*?time=([\d\.]+)\s*ms");
            if (m.Success)
            {
                double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var ms);
                onProgress(new PingResultItem
                {
                    Seq = seq++,
                    Host = host,
                    Ip = m.Groups[1].Value,
                    TimeMs = ms,
                    Success = true,
                    RawText = line
                });
            }
            else if (line.Contains("time out") || line.Contains("Unreachable") || line.Contains("100% packet loss"))
            {
                onProgress(new PingResultItem
                {
                    Seq = seq++,
                    Host = host,
                    Success = false,
                    RawText = line
                });
            }
        }, ct);
    }

    public async Task RunTracerouteAsync(string host, Action<TracerouteHop> onHop, CancellationToken ct)
    {
        var cmd = $"traceroute -q 1 -w 2 -m 20 {host} 2>&1";
        await _ssh.RunStreamingCommandAsync(cmd, line =>
        {
            var match = Regex.Match(line, @"^\s*(\d+)\s+([^\s]+)\s+\(([\d\.]+)\)\s+([\d\.]+)\s*ms");
            if (match.Success)
            {
                int.TryParse(match.Groups[1].Value, out var hop);
                onHop(new TracerouteHop
                {
                    Hop = hop,
                    Host = match.Groups[2].Value,
                    Ip = match.Groups[3].Value,
                    Rtt1 = match.Groups[4].Value + " ms"
                });
            }
            else
            {
                var starMatch = Regex.Match(line, @"^\s*(\d+)\s+\*");
                if (starMatch.Success && int.TryParse(starMatch.Groups[1].Value, out var hopNum))
                {
                    onHop(new TracerouteHop
                    {
                        Hop = hopNum,
                        Host = "*",
                        Ip = "*",
                        Rtt1 = "Превышен интервал ожидания"
                    });
                }
            }
        }, ct);
    }

    public async Task<List<LogEntry>> GetSystemLogsAsync(int lines = 200)
    {
        var result = new List<LogEntry>();
        var (code, output, _) = await _ssh.ExecuteCommandAsync($"logread -l {lines} 2>/dev/null || tail -n {lines} /var/log/messages 2>/dev/null", 8);
        if (code != 0 || string.IsNullOrWhiteSpace(output)) return result;

        foreach (var line in output.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            result.Add(ParseLogLine(line));
        }

        return result;
    }

    public async Task<List<LogEntry>> GetKernelLogsAsync(int lines = 150)
    {
        var result = new List<LogEntry>();
        var (code, output, _) = await _ssh.ExecuteCommandAsync($"dmesg | tail -n {lines} 2>/dev/null", 8);
        if (code != 0 || string.IsNullOrWhiteSpace(output)) return result;

        foreach (var line in output.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            result.Add(new LogEntry
            {
                TimeText = "Kernel",
                Process = "kernel",
                Level = line.Contains("error", StringComparison.OrdinalIgnoreCase) ? "err" : "info",
                Message = line.Trim()
            });
        }

        return result;
    }

    private static LogEntry ParseLogLine(string line)
    {
        var entry = new LogEntry { Message = line.Trim() };
        var m = Regex.Match(line, @"^([A-Z][a-z]{2}\s+\w+\s+\d+\s+[\d:]+)\s+(\w+)\s+([^:]+):\s*(.*)$");
        if (m.Success)
        {
            entry.TimeText = m.Groups[1].Value;
            entry.Process = m.Groups[3].Value.Trim();
            entry.Message = m.Groups[4].Value.Trim();

            if (entry.Message.Contains("err", StringComparison.OrdinalIgnoreCase) || entry.Message.Contains("fail", StringComparison.OrdinalIgnoreCase))
                entry.Level = "err";
            else if (entry.Message.Contains("warn", StringComparison.OrdinalIgnoreCase))
                entry.Level = "warn";
            else
                entry.Level = "info";
        }
        return entry;
    }

    private static long ParseKb(string line)
    {
        var m = Regex.Match(line, @"\d+");
        return m.Success && long.TryParse(m.Value, out var v) ? v : 0;
    }
}
