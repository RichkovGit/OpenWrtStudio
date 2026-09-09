using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Threading;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface ISentinelService
{
    bool IsMonitoring { get; set; }
    SentinelIssue? CurrentIssue { get; }
    event EventHandler<SentinelIssue?>? IssueChanged;

    Task<SentinelIssue?> ProbeHealthAsync();
    Task<(bool Success, string Message)> ApplyRecoveryMethodAsync(RecoveryMethod method);
}

public class SentinelService : ISentinelService
{
    private readonly ISshService _ssh;
    private readonly DispatcherTimer _watchdogTimer;

    public bool IsMonitoring { get; set; } = true;
    public SentinelIssue? CurrentIssue { get; private set; }
    public event EventHandler<SentinelIssue?>? IssueChanged;

    public SentinelService(ISshService ssh)
    {
        _ssh = ssh;
        _watchdogTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(12)
        };
        _watchdogTimer.Tick += async (_, _) =>
        {
            if (_ssh.IsConnected && IsMonitoring)
            {
                await ProbeHealthAsync();
            }
        };
        _watchdogTimer.Start();
    }

    public async Task<SentinelIssue?> ProbeHealthAsync()
    {
        if (!_ssh.IsConnected)
        {
            SetCurrentIssue(null);
            return null;
        }

        const string probeScript = @"
echo '===WAN_STATUS==='
ubus call network.interface.wan status 2>/dev/null
echo '===CARRIER==='
cat /sys/class/net/eth*/carrier 2>/dev/null || cat /sys/class/net/wan/carrier 2>/dev/null
echo '===PING==='
ping -c 1 -W 2 77.88.8.8 >/dev/null 2>&1 && echo 'ping:ok' || (ping -c 1 -W 2 1.1.1.1 >/dev/null 2>&1 && echo 'ping:ok' || echo 'ping:fail')
echo '===DNS_LOCAL==='
nslookup ya.ru 127.0.0.1 2>/dev/null || nslookup google.com 127.0.0.1 2>/dev/null
echo '===DNS_PUBLIC==='
nslookup ya.ru 77.88.8.8 2>/dev/null || nslookup ya.ru 1.1.1.1 2>/dev/null || nslookup google.com 8.8.8.8 2>/dev/null
echo '===SERVICES==='
(pidof dnsmasq || pgrep dnsmasq) >/dev/null 2>&1 && echo 'dnsmasq:ok' || echo 'dnsmasq:down'
(pidof pppd || pgrep pppd) >/dev/null 2>&1 && echo 'pppd:ok' || echo 'pppd:down'
(pidof mihomo || pgrep mihomo) >/dev/null 2>&1 && echo 'mihomo:ok' || echo 'mihomo:down'
(pidof sing-box || pgrep sing-box) >/dev/null 2>&1 && echo 'singbox:ok' || echo 'singbox:down'
echo '===TUN==='
(ip link show mihomo0 2>/dev/null || ip link show tun0 2>/dev/null) && echo 'tun:active' || echo 'tun:none'
";

        var (code, output, _) = await _ssh.ExecuteCommandAsync(probeScript, 8);
        if (code != 0 && string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        try
        {
            var issue = AnalyzeProbeOutput(output);
            SetCurrentIssue(issue);
            return issue;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sentinel probe error: {ex.Message}");
            return null;
        }
    }

    private static SentinelIssue? AnalyzeProbeOutput(string output)
    {
        bool wanUp = false;
        string proto = "dhcp";
        string gw = "";
        bool carrierOk = true;

        if (output.Contains("===WAN_STATUS==="))
        {
            var wanSection = ExtractSection(output, "WAN_STATUS");
            if (!string.IsNullOrWhiteSpace(wanSection) && wanSection.StartsWith("{"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(wanSection);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("up", out var upProp)) wanUp = upProp.GetBoolean();
                    if (root.TryGetProperty("proto", out var protoProp)) proto = protoProp.GetString() ?? "dhcp";
                    if (root.TryGetProperty("route", out var rProp) && rProp.GetArrayLength() > 0)
                    {
                        var first = rProp[0];
                        if (first.TryGetProperty("nexthop", out var nh)) gw = nh.GetString() ?? "";
                    }
                    if (root.TryGetProperty("ipv4-address", out var ipProp) && ipProp.GetArrayLength() > 0)
                    {
                        wanUp = true;
                    }
                }
                catch { }
            }
        }

        var carrierSec = ExtractSection(output, "CARRIER");
        if (carrierSec.Contains("0") && !carrierSec.Contains("1"))
        {
            carrierOk = false;
        }

        if (wanUp)
        {
            carrierOk = true;
        }

        var pingSec = ExtractSection(output, "PING");
        bool pingOk = pingSec.Contains("ping:ok") || output.Contains("ping:ok");

        var dnsLocalSec = ExtractSection(output, "DNS_LOCAL");
        bool dnsLocalOk = dnsLocalSec.Contains("Address") || dnsLocalSec.Contains("Name:");

        var dnsPubSec = ExtractSection(output, "DNS_PUBLIC");
        bool dnsPubOk = dnsPubSec.Contains("Address") || dnsPubSec.Contains("Name:");

        var srvSec = ExtractSection(output, "SERVICES");
        bool dnsmasqRunning = srvSec.Contains("dnsmasq:ok");
        bool pppdRunning = srvSec.Contains("pppd:ok");
        bool mihomoRunning = srvSec.Contains("mihomo:ok");
        bool singboxRunning = srvSec.Contains("singbox:ok");

        var tunSec = ExtractSection(output, "TUN");
        bool tunActive = tunSec.Contains("tun:active") || output.Contains("tun:active");

        bool externalInternetOk = pingOk || dnsPubOk;

        // 1. Если внешний интернет доступен — провайдер, кабель и PPPoE работают безупречно!
        if (externalInternetOk)
        {
            // Проверяем локальные сбои службы DNS (dnsmasq) на роутере
            if (!dnsLocalOk && (dnsPubOk || !dnsmasqRunning))
            {
                return new SentinelIssue
                {
                    Title = "Локальный сбой DNS (dnsmasq)",
                    Description = "Интернет у провайдера работает, но локальная служба DNS на роутере зависла или не отвечает клиентам сети.",
                    DiagnosticDetails = $"Локальный DNS (127.0.0.1): СБОЙ. Публичный DNS: ДОСТУПЕН. dnsmasq: {(dnsmasqRunning ? "работает" : "остановлен")}.",
                    Severity = IssueSeverity.Critical,
                    IsProviderIssue = false, // Проблема на стороне роутера
                    ProviderStatusText = "Провайдер работает отлично (внешняя сеть доступна)",
                    RecoveryMethods = new List<RecoveryMethod>
                    {
                        new()
                        {
                            Id = "restart_dnsmasq",
                            Name = "Метод 1: Перезапустить службу DNS и сбросить кэш",
                            Description = "Быстрый перезапуск демона dnsmasq и сброс испорченного кэша.",
                            Command = "/etc/init.d/dnsmasq restart",
                            Icon = "Broom24",
                            IsRecommended = true
                        },
                        new()
                        {
                            Id = "emergency_dns",
                            Name = "Метод 2: Экстренный переход на резервный DNS (Яндекс/Cloudflare)",
                            Description = "Принудительно подставить надежные DNS в обход зависшего локального резолвера.",
                            Command = "echo -e 'nameserver 77.88.8.8\\nnameserver 1.1.1.1' > /tmp/resolv.conf.auto; killall -HUP dnsmasq",
                            Icon = "Flash24"
                        },
                        new()
                        {
                            Id = "restart_net_fw",
                            Name = "Метод 3: Перезапуск сетевого стека и брандмауэра",
                            Description = "Перезапуск всех подсистем сети и фильтрации.",
                            Command = "/etc/init.d/network restart; /etc/init.d/firewall restart",
                            Icon = "ShieldKeyhole24"
                        },
                        new()
                        {
                            Id = "reboot_clean",
                            Name = "Метод 4: Полная перезагрузка роутера",
                            Description = "Штатная перезагрузка роутера.",
                            Command = "reboot",
                            Icon = "Power24"
                        }
                    }
                };
            }

            // Никаких проблем нет — полная норма
            return null;
        }

        // 2. Физический обрыв кабеля провайдера
        if (!carrierOk && !wanUp)
        {
            return new SentinelIssue
            {
                Title = "Нет сигнала в кабеле WAN (Carrier Down)",
                Description = "Сетевой кабель в WAN-порту роутера отключен или поврежден на линии провайдера.",
                DiagnosticDetails = "Физический линк WAN: 0. Нет несущей частоты.",
                Severity = IssueSeverity.Critical,
                IsProviderIssue = true,
                ProviderStatusText = "Проблема на физической линии (кабель / коммутатор провайдера)",
                RecoveryMethods = new List<RecoveryMethod>
                {
                    new()
                    {
                        Id = "restart_net",
                        Name = "Перезапустить сетевой контроллер",
                        Description = "Сбросить и заново инициализировать драйвер Ethernet порта.",
                        Command = "/etc/init.d/network restart",
                        Icon = "ArrowSync24",
                        IsRecommended = true
                    },
                    new()
                    {
                        Id = "check_cable",
                        Name = "Проверить статус портов коммутатора",
                        Description = "Вывести статус портов Ethernet.",
                        Command = "swconfig dev switch0 show 2>/dev/null || ip link show",
                        Icon = "PlugDisconnected24"
                    }
                }
            };
        }

        // 3. Блокировка трафика в виртуальном тоннеле TUN (Mihomo / Sing-box зависли)
        if (tunActive && !mihomoRunning && !singboxRunning)
        {
            return new SentinelIssue
            {
                Title = "Зависание или аварийное падение ядра прокси",
                Description = "Трафик роутера направлен в TUN-интерфейс, однако служба прокси (Mihomo/Sing-box) остановлена. Это блокирует весь доступ в интернет!",
                DiagnosticDetails = "Виртуальный адаптер TUN активен, но фоновый процесс ядра отсутствует.",
                Severity = IssueSeverity.Warning,
                IsProviderIssue = false,
                ProviderStatusText = "Провайдер доступен, трафик застрял в TUN интерфейсе роутера",
                RecoveryMethods = new List<RecoveryMethod>
                {
                    new()
                    {
                        Id = "start_proxy",
                        Name = "Метод 1: Перезапустить ядро прокси",
                        Description = "Перезапустить системную службу прокси.",
                        Command = "/etc/init.d/mihomo restart 2>/dev/null || /etc/init.d/openwrt-mihomo restart 2>/dev/null || /etc/init.d/sing-box restart 2>/dev/null",
                        Icon = "Play24",
                        IsRecommended = true
                    },
                    new()
                    {
                        Id = "disable_tun",
                        Name = "Метод 2: Экстренно вернуть прямой интернет",
                        Description = "Временно отключить TUN-перехват и сбросить брандмауэр для прямого интернета.",
                        Command = "ip link set dev mihomo0 down 2>/dev/null; ip link set dev tun0 down 2>/dev/null; /etc/init.d/firewall restart",
                        Icon = "ArrowRouting24"
                    }
                }
            };
        }

        // 4. Сбой PPPoE сессии (когда внешний интернет упал и сессия оборвана)
        if (proto.Contains("pppoe", StringComparison.OrdinalIgnoreCase) && (!wanUp || !pppdRunning))
        {
            return new SentinelIssue
            {
                Title = "Сбой PPPoE сессии",
                Description = "Авторизация PPPoE у провайдера оборвалась или сервер доступа провайдера (BRAS) отклоняет подключение.",
                DiagnosticDetails = $"WAN up: {wanUp}, pppd демон: {(pppdRunning ? "работает" : "остановлен")}.",
                Severity = IssueSeverity.Critical,
                IsProviderIssue = true,
                ProviderStatusText = "Сервер PPPoE провайдера не отвечает (возможна задолженность или авария на линии)",
                RecoveryMethods = new List<RecoveryMethod>
                {
                    new()
                    {
                        Id = "reconnect_pppoe",
                        Name = "Переподключить PPPoE сессию (Мягко)",
                        Description = "Отправить запрос на разрыв и немедленный повторный вход в сессию.",
                        Command = "ifdown wan; sleep 2; ifup wan",
                        Icon = "ArrowSync24",
                        IsRecommended = true
                    },
                    new()
                    {
                        Id = "restart_pppd",
                        Name = "Перезапуск демона pppd и сети",
                        Description = "Полный перезапуск системной службы PPPoE клиента.",
                        Command = "killall -9 pppd 2>/dev/null; /etc/init.d/network restart",
                        Icon = "ArrowClockwise24"
                    },
                    new()
                    {
                        Id = "reboot_router",
                        Name = "Перезагрузить роутер",
                        Description = "Полная перезагрузка роутера.",
                        Command = "reboot",
                        Icon = "Power24"
                    }
                }
            };
        }

        // 5. Сбой DHCP / статического интерфейса WAN
        if (!wanUp)
        {
            return new SentinelIssue
            {
                Title = "WAN интерфейс отключен или не получил IP",
                Description = "Интерфейс подключения к провайдеру не получил IP-адрес по DHCP или отключен.",
                DiagnosticDetails = $"WAN proto: {proto}, up: false.",
                Severity = IssueSeverity.Critical,
                IsProviderIssue = true,
                ProviderStatusText = "DHCP-сервер провайдера не выдает сетевой адрес",
                RecoveryMethods = new List<RecoveryMethod>
                {
                    new()
                    {
                        Id = "renew_dhcp",
                        Name = "Обновить аренду IP (DHCP Renew)",
                        Description = "Запросить новый IP-адрес у провайдера.",
                        Command = "ifdown wan; sleep 2; ifup wan",
                        Icon = "ArrowSync24",
                        IsRecommended = true
                    },
                    new()
                    {
                        Id = "restart_net",
                        Name = "Перезапустить сетевой стек",
                        Description = "Перезапустить сетевую службу OpenWrt.",
                        Command = "/etc/init.d/network restart",
                        Icon = "ArrowClockwise24"
                    }
                }
            };
        }

        // 6. Магистральный сбой у провайдера (WAN поднят, IP получен, но ни один внешний сервер не отвечает)
        return new SentinelIssue
        {
            Title = "Сбой магистрали провайдера (Нет ответа от внешних серверов)",
            Description = "IP-адрес от провайдера получен успешно, однако внешний шлюз не передает пакеты в глобальную сеть.",
            DiagnosticDetails = "WAN IP активен, но внешние узлы недоступны.",
            Severity = IssueSeverity.Critical,
            IsProviderIssue = true,
            ProviderStatusText = "Авария на магистральном шлюзе или технические работы у провайдера",
            RecoveryMethods = new List<RecoveryMethod>
            {
                new()
                {
                    Id = "reconnect_wan",
                    Name = "Метод 1: Переподключить внешний интерфейс",
                    Description = "Сбросить соединение для переназначения на другой внешний шлюз/маршрут.",
                    Command = "ifdown wan; sleep 2; ifup wan",
                    Icon = "ArrowSync24",
                    IsRecommended = true
                },
                new()
                {
                    Id = "flush_arp",
                    Name = "Метод 2: Сбросить таблицу маршрутизации и ARP",
                    Description = "Очистить кэш шлюза провайдера на роутере.",
                    Command = "ip neigh flush all 2>/dev/null; /etc/init.d/network reload",
                    Icon = "Broom24"
                },
                new()
                {
                    Id = "reboot_router",
                    Name = "Метод 3: Перезагрузить роутер",
                    Description = "Перезагрузка устройства.",
                    Command = "reboot",
                    Icon = "Power24"
                }
            }
        };
    }

    public async Task<(bool Success, string Message)> ApplyRecoveryMethodAsync(RecoveryMethod method)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения по SSH");

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(method.Command, 25);
        if (code == 0)
        {
            await Task.Delay(2000);
            await ProbeHealthAsync();
            return (true, $"Метод '{method.Name}' успешно применен!");
        }
        return (false, $"Ошибка выполнения: {err}");
    }

    private void SetCurrentIssue(SentinelIssue? issue)
    {
        CurrentIssue = issue;
        App.Current?.Dispatcher?.Invoke(() =>
        {
            IssueChanged?.Invoke(this, issue);
        });
    }

    private static string ExtractSection(string text, string tag)
    {
        var startTag = $"==={tag}===";
        int idx = text.IndexOf(startTag, StringComparison.Ordinal);
        if (idx < 0) return "";
        int start = idx + startTag.Length;
        int next = text.IndexOf("===", start, StringComparison.Ordinal);
        return next > 0 ? text.Substring(start, next - start).Trim() : text.Substring(start).Trim();
    }
}
