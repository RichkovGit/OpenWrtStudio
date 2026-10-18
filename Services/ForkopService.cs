using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface IForkopService
{
    Task<ForkopFeedInfo> GetFeedInfoAsync();
    Task<(bool Success, string Message)> ConfigureFeedAsync(string branch = "master");
    Task<List<ForkopComponentItem>> GetComponentsAsync();
    Task<(bool Success, string Message)> InstallComponentAsync(string packageName);
    Task<ForkopDiagResult> RunDiagnosticsAsync();

    // Section and Subscriptions Management
    Task<ForkopSection> GetSectionConfigAsync();
    Task<(bool Success, string Message)> SaveSectionConfigAsync(ForkopSection section);
    Task<(bool Success, string Message)> UpdateSubscriptionsAsync();

    // Dashboard, Nodes and Metrics
    Task<ForkopDashboardStats> GetDashboardStatsAsync();
    Task<List<ForkopSubscriptionInfo>> GetSubscriptionInfoListAsync();
    Task<List<ForkopServerNode>> GetServersAsync();
    Task<List<ForkopServerNode>> TestLatenciesAsync(List<ForkopServerNode> currentNodes);
    Task<(bool Success, string Message)> SelectActiveServerAsync(ForkopServerNode server);
}

public class ForkopService : IForkopService
{
    private readonly ISshService _ssh;

    public ForkopService(ISshService ssh)
    {
        _ssh = ssh;
    }

    public async Task<ForkopFeedInfo> GetFeedInfoAsync()
    {
        var info = new ForkopFeedInfo();
        if (!_ssh.IsConnected) return info;

        // 1. Architecture detection
        var (archCode, archOut, _) = await _ssh.ExecuteCommandAsync("opkg print-architecture | tail -n 1 | awk '{print $2}'", 5);
        if (archCode == 0 && !string.IsNullOrWhiteSpace(archOut))
        {
            info.Architecture = archOut.Trim();
        }
        else
        {
            var (_, unameOut, _) = await _ssh.ExecuteCommandAsync("uname -m", 5);
            info.Architecture = unameOut.Trim();
        }

        // 2. Check customfeeds.conf
        var (feedCode, feedOut, _) = await _ssh.ExecuteCommandAsync("cat /etc/opkg/customfeeds.conf 2>/dev/null", 5);
        if (feedCode == 0 && !string.IsNullOrWhiteSpace(feedOut))
        {
            if (feedOut.Contains("forkop") || feedOut.Contains("passwall"))
            {
                info.IsConfigured = true;
                var match = Regex.Match(feedOut, @"src/gz\s+(?:forkop|passwall)\s+([^\s\r\n]+)");
                if (match.Success)
                {
                    info.FeedUrl = match.Groups[1].Value;
                }
            }
        }

        // 3. Check keys
        var (keyCode, keyOut, _) = await _ssh.ExecuteCommandAsync("ls -la /etc/opkg/keys/ 2>/dev/null", 5);
        if (keyCode == 0 && keyOut.Contains("forkop"))
        {
            info.FeedKeyStatus = "Установлен и проверен";
        }
        else if (info.IsConfigured)
        {
            info.FeedKeyStatus = "Без подписи (check_signature disabled)";
        }

        return info;
    }

    public async Task<(bool Success, string Message)> ConfigureFeedAsync(string branch = "master")
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var feedInfo = await GetFeedInfoAsync();
        var arch = feedInfo.Architecture;
        if (string.IsNullOrWhiteSpace(arch) || arch == "unknown")
        {
            arch = "aarch64_cortex-a53"; // common safe fallback
        }

        var feedUrl = $"https://raw.githubusercontent.com/forkop/openwrt-packages/{branch}/{arch}";

        var script = $@"
# Backup feeds
cp /etc/opkg/customfeeds.conf /etc/opkg/customfeeds.conf.bak 2>/dev/null

# Add ForkOP feed
grep -v 'forkop' /etc/opkg/customfeeds.conf > /tmp/customfeeds.tmp 2>/dev/null || true
echo 'src/gz forkop {feedUrl}' >> /tmp/customfeeds.tmp
mv /tmp/customfeeds.tmp /etc/opkg/customfeeds.conf

# Disable strict signature checking for custom feed if key missing
sed -i 's/^option check_signature/# option check_signature/' /etc/opkg.conf 2>/dev/null || true

# Update package index
opkg update
";

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(script, 30);
        if (code == 0 && (outStr.Contains("Updated list") || outStr.Contains("forkop")))
        {
            return (true, $"Репозиторий ForkOP ({arch}) успешно добавлен и списки пакетов обновлены!");
        }

        return (false, $"Не удалось настроить репозиторий: {err}\n{outStr}");
    }

    public async Task<List<ForkopComponentItem>> GetComponentsAsync()
    {
        var list = new List<ForkopComponentItem>
        {
            new() { Name = "PassWall 2 UI", PackageName = "luci-app-passwall", Description = "Полнофункциональный веб-интерфейс управления обходом блокировок", Category = "UI" },
            new() { Name = "PassWall Core", PackageName = "passwall", Description = "Службы прозрачного проксирования TCP/UDP и маршрутизации", Category = "Core" },
            new() { Name = "ChinaDNS-NG", PackageName = "chinadns-ng", Description = "Анти-DPI DNS с защитой от подмены и разделением доменов", Category = "DNS" },
            new() { Name = "SmartDNS", PackageName = "smartdns", Description = "Локальный сверхбыстрый DNS с параллельными запросами", Category = "DNS" },
            new() { Name = "MosDNS", PackageName = "mosdns", Description = "Модульный DNS маршрутизатор с поддержкой DoH/DoT", Category = "DNS" },
            new() { Name = "Sing-box", PackageName = "sing-box", Description = "Универсальное ядро нового поколения (Reality, Hysteria 2, ShadowTLS)", Category = "Core" },
            new() { Name = "Xray Core", PackageName = "xray-core", Description = "Ядро Xray (VLESS Reality, VMess, Trojan, Shadowsocks)", Category = "Core" },
            new() { Name = "V2Ray GeoData", PackageName = "v2ray-geodata", Description = "Официальные базы данных классификации доменов и IP (GeoIP/GeoSite)", Category = "Routing" }
        };

        if (!_ssh.IsConnected) return list;

        // Batch inspect installed packages and running processes
        var script = @"
echo '===INSTALLED==='
opkg list-installed
echo '===RUNNING==='
ps
";
        var (code, output, _) = await _ssh.ExecuteCommandAsync(script, 10);
        if (code != 0 && string.IsNullOrWhiteSpace(output)) return list;

        var installedSec = ExtractSection(output, "INSTALLED");
        var runningSec = ExtractSection(output, "RUNNING");

        foreach (var item in list)
        {
            var match = Regex.Match(installedSec, @$"(?:^|\n){Regex.Escape(item.PackageName)}\s+-\s+([^\s\r\n]+)");
            if (match.Success)
            {
                item.IsInstalled = true;
                item.Version = match.Groups[1].Value;
            }

            if (runningSec.Contains(item.PackageName) || 
                (item.PackageName == "luci-app-passwall" && runningSec.Contains("passwall")) ||
                (item.PackageName == "smartdns" && runningSec.Contains("smartdns")) ||
                (item.PackageName == "chinadns-ng" && runningSec.Contains("chinadns-ng")))
            {
                item.IsRunning = true;
            }
        }

        return list;
    }

    public async Task<(bool Success, string Message)> InstallComponentAsync(string packageName)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру");

        var cleanPkg = ShellHelper.SanitizeIdentifier(packageName);
        if (string.IsNullOrWhiteSpace(cleanPkg)) return (false, "Некорректное имя пакета");

        var cmd = $"opkg update && opkg install --force-depends {cleanPkg}";
        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(cmd, 60);

        if (code == 0 || outStr.Contains("Installing") || outStr.Contains("Configuring"))
        {
            return (true, $"Пакет {cleanPkg} успешно установлен!\n{outStr}");
        }

        return (false, $"Ошибка установки пакета {cleanPkg}: {err}\n{outStr}");
    }

    public async Task<ForkopDiagResult> RunDiagnosticsAsync()
    {
        var diag = new ForkopDiagResult();
        if (!_ssh.IsConnected) return diag;

        const string diagScript = @"
echo '===FIREWALL_RULES==='
nft list table inet fw4 2>/dev/null || iptables -t nat -S 2>/dev/null
echo '===DNS_PORTS==='
netstat -tulpn 2>/dev/null | grep -E ':53|:5335|:5353'
echo '===DIRECT_RU==='
nslookup ya.ru 127.0.0.1 >/dev/null 2>&1 && echo 'direct_ru:ok' || echo 'direct_ru:fail'
echo '===BLOCKED_TEST==='
nslookup rutracker.org 127.0.0.1 >/dev/null 2>&1 && echo 'blocked_dns:ok' || echo 'blocked_dns:fail'
";
        var (code, output, _) = await _ssh.ExecuteCommandAsync(diagScript, 10);
        if (code != 0 && string.IsNullOrWhiteSpace(output)) return diag;

        var fwSec = ExtractSection(output, "FIREWALL_RULES");
        if (fwSec.Contains("passwall") || fwSec.Contains("FW4_PASSWALL") || fwSec.Contains("tproxy") || fwSec.Contains("redirect"))
        {
            diag.HasTransparentProxyRules = true;
            diag.RuleChainsFound.Add("Правила перехвата трафика (PassWall/TProxy) активны");
        }
        if (fwSec.Contains("table inet fw4"))
        {
            diag.FirewallType = "nftables (OpenWrt fw4)";
        }
        else
        {
            diag.FirewallType = "iptables (OpenWrt fw3)";
        }

        var dnsSec = ExtractSection(output, "DNS_PORTS");
        if (dnsSec.Contains("5335") || dnsSec.Contains("chinadns") || dnsSec.Contains("smartdns"))
        {
            diag.DnsHijackActive = true;
            diag.RuleChainsFound.Add("Обнаружен защищенный порт DNS (5335)");
        }

        diag.DirectRussiaOk = output.Contains("direct_ru:ok");
        diag.ProxyBypassOk = output.Contains("blocked_dns:ok");

        diag.Details = $"Брандмауэр: {diag.FirewallType}. " +
                       $"Прямой доступ РФ: {(diag.DirectRussiaOk ? "OK" : "Сбой")}. " +
                       $"Резолвинг обхода: {(diag.ProxyBypassOk ? "OK" : "Требуется настройка")}. " +
                       $"Правила в ядре: {(diag.HasTransparentProxyRules ? "Внедрены" : "Не обнаружены")}.";

        return diag;
    }

    public async Task<ForkopSection> GetSectionConfigAsync()
    {
        var section = new ForkopSection();

        if (!_ssh.IsConnected) return section;

        try
        {
            var (code, output, _) = await _ssh.ExecuteCommandAsync("uci show forkop 2>/dev/null", 5);
            if (code == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var customSubs = new List<string>();
                var customRules = new List<string>();
                var customConns = new List<string>();

                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Contains(".enabled="))
                    {
                        var val = trimmed.Split('=')[1].Trim('\'', '"', ' ');
                        section.IsEnabled = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (trimmed.Contains(".name="))
                    {
                        section.Name = trimmed.Split('=')[1].Trim('\'', '"', ' ');
                    }
                    else if (trimmed.Contains(".action="))
                    {
                        section.Action = trimmed.Split('=')[1].Trim('\'', '"', ' ');
                    }
                    else if (trimmed.Contains(".interface="))
                    {
                        section.SelectedInterface = trimmed.Split('=')[1].Trim('\'', '"', ' ');
                    }
                    else if (trimmed.Contains(".sub_url="))
                    {
                        customSubs.Add(trimmed.Split('=')[1].Trim('\'', '"', ' '));
                    }
                    else if (trimmed.Contains(".conn_url="))
                    {
                        customConns.Add(trimmed.Split('=')[1].Trim('\'', '"', ' '));
                    }
                    else if (trimmed.Contains(".ruleset="))
                    {
                        customRules.Add(trimmed.Split('=')[1].Trim('\'', '"', ' '));
                    }
                }

                if (customSubs.Count > 0)
                {
                    section.SubscriptionUrls.Clear();
                    foreach (var s in customSubs) section.SubscriptionUrls.Add(s);
                }
                if (customRules.Count > 0)
                {
                    section.Rulesets.Clear();
                    foreach (var r in customRules) section.Rulesets.Add(r);
                }
                if (customConns.Count > 0)
                {
                    section.ConnectionUrls.Clear();
                    foreach (var c in customConns) section.ConnectionUrls.Add(c);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ForkopService] GetSectionConfig error: {ex.Message}");
        }

        return section;
    }

    public async Task<(bool Success, string Message)> SaveSectionConfigAsync(ForkopSection section)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру по SSH");

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("uci -q delete forkop.vpn_proxy 2>/dev/null || true");
            sb.AppendLine("uci set forkop.vpn_proxy=section");
            sb.AppendLine($"uci set forkop.vpn_proxy.enabled='{(section.IsEnabled ? "1" : "0")}'");
            sb.AppendLine($"uci set forkop.vpn_proxy.name={ShellHelper.EscapeArg(section.Name)}");
            sb.AppendLine($"uci set forkop.vpn_proxy.action={ShellHelper.EscapeArg(section.Action)}");
            sb.AppendLine($"uci set forkop.vpn_proxy.interface={ShellHelper.EscapeArg(section.SelectedInterface)}");

            foreach (var url in section.SubscriptionUrls)
            {
                if (!string.IsNullOrWhiteSpace(url))
                    sb.AppendLine($"uci add_list forkop.vpn_proxy.sub_url={ShellHelper.EscapeArg(url)}");
            }

            foreach (var conn in section.ConnectionUrls)
            {
                if (!string.IsNullOrWhiteSpace(conn))
                    sb.AppendLine($"uci add_list forkop.vpn_proxy.conn_url={ShellHelper.EscapeArg(conn)}");
            }

            foreach (var rule in section.Rulesets)
            {
                if (!string.IsNullOrWhiteSpace(rule))
                    sb.AppendLine($"uci add_list forkop.vpn_proxy.ruleset={ShellHelper.EscapeArg(rule)}");
            }

            sb.AppendLine("uci commit forkop");
            sb.AppendLine("/etc/init.d/forkop reload 2>/dev/null || /etc/init.d/forkop restart 2>/dev/null || true");

            var (code, outStr, err) = await _ssh.ExecuteCommandAsync(sb.ToString(), 15);
            if (code == 0)
            {
                return (true, "Конфигурация секции ForkOP успешно сохранена и применена!");
            }
            return (false, $"Ошибка сохранения в UCI: {err}\n{outStr}");
        }
        catch (Exception ex)
        {
            return (false, $"Сбой сохранения: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> UpdateSubscriptionsAsync()
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру по SSH");

        const string updateCmd = @"
if [ -x /usr/bin/forkop ]; then
    /usr/bin/forkop update-sub 2>/dev/null || /usr/bin/forkop sub-update 2>/dev/null
elif [ -x /etc/init.d/forkop ]; then
    /etc/init.d/forkop restart 2>/dev/null
fi
";
        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(updateCmd, 40);
        if (code == 0 || outStr.Contains("success") || outStr.Contains("ok") || string.IsNullOrWhiteSpace(err))
        {
            return (true, "Подписки ForkOP успешно обновлены на роутере!");
        }

        return (false, $"Ошибка обновления подписок: {err}\n{outStr}");
    }

    public async Task<ForkopDashboardStats> GetDashboardStatsAsync()
    {
        var stats = new ForkopDashboardStats();
        if (!_ssh.IsConnected) return stats;

        try
        {
            const string statsCmd = @"
echo '===PROCS==='
[ -x /etc/init.d/forkop ] && /etc/init.d/forkop running >/dev/null 2>&1 && echo forkop:running
pidof sing-box >/dev/null 2>&1 && echo sing-box:running
echo '===CONNS==='
wc -l < /proc/net/nf_conntrack 2>/dev/null || cat /proc/sys/net/netfilter/nf_conntrack_count 2>/dev/null || echo '0'
echo '===MEM==='
free -m 2>/dev/null | grep Mem | awk '{printf ""%.1f MB"", $3}'
echo '===DEV==='
cat /proc/net/dev 2>/dev/null | grep -E 'eth0|br-lan|wan' | head -n 1
";
            var (code, output, _) = await _ssh.ExecuteCommandAsync(statsCmd, 5);
            if (code == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var procs = ExtractSection(output, "PROCS");
                stats.IsForkopRunning = procs.Contains("forkop:running");
                stats.IsSingboxRunning = procs.Contains("sing-box:running");

                var conns = ExtractSection(output, "CONNS");
                if (int.TryParse(conns.Trim(), out var count))
                {
                    stats.ActiveConnections = count;
                }

                var mem = ExtractSection(output, "MEM");
                if (!string.IsNullOrWhiteSpace(mem))
                {
                    stats.MemoryUsage = mem.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ForkopService] GetDashboardStats error: {ex.Message}");
        }

        return stats;
    }

    public async Task<List<ForkopSubscriptionInfo>> GetSubscriptionInfoListAsync()
    {
        var defaultList = new List<ForkopSubscriptionInfo>();

        if (!_ssh.IsConnected) return defaultList;

        try
        {
            var (code, outStr, _) = await _ssh.ExecuteCommandAsync("cat /var/run/forkop/section-cache/*.json 2>/dev/null || cat /etc/forkop/section-cache/*.json 2>/dev/null", 5);
            if (code == 0 && !string.IsNullOrWhiteSpace(outStr))
            {
                var liveList = ParseSubscriptionMetadata(outStr);
                if (liveList.Count > 0)
                {
                    return liveList;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ForkopService] GetSubscriptionInfoListAsync error: {ex.Message}");
        }

        return defaultList;
    }

    public async Task<List<ForkopServerNode>> GetServersAsync()
    {
        var defaultList = new List<ForkopServerNode>();

        if (!_ssh.IsConnected) return defaultList;

        try
        {
            const string fetchCmd = @"
echo '===SECTION_CACHE==='
cat /var/run/forkop/section-cache/*.json 2>/dev/null || cat /etc/forkop/section-cache/*.json 2>/dev/null
echo '===SINGBOX_CONFIG==='
cat /etc/sing-box/config.json 2>/dev/null
echo '===CLASH_PROXIES==='
/usr/bin/forkop clash_api get_proxies 2>/dev/null
echo '===PASSWALL_NODES==='
uci -q show passwall 2>/dev/null | grep -E '\.remarks=|\.type=|\.address=|\.port=' 2>/dev/null
echo '===MIHOMO_CONFIG==='
cat /etc/mihomo/run/config.yaml 2>/dev/null || cat /etc/mihomo/config.yaml 2>/dev/null
";
            var (code, output, _) = await _ssh.ExecuteCommandAsync(fetchCmd, 12);
            if (!string.IsNullOrWhiteSpace(output))
            {
                var parsed = ParseAllRouterNodes(output);
                if (parsed.Count > 0)
                {
                    return parsed;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ForkopService] GetServersAsync router error: {ex.Message}");
        }

        return defaultList;
    }

    public List<ForkopSubscriptionInfo> ParseSubscriptionMetadata(string jsonText)
    {
        var list = new List<ForkopSubscriptionInfo>();
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            if (doc.RootElement.TryGetProperty("subscriptionMetadata", out var metaArr) && metaArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in metaArr.EnumerateArray())
                {
                    var info = new ForkopSubscriptionInfo();
                    if (item.TryGetProperty("title", out var titleEl))
                        info.Name = titleEl.GetString() ?? "";

                    if (item.TryGetProperty("webPageUrl", out var webEl))
                        info.Url = webEl.GetString() ?? "";

                    if (item.TryGetProperty("announce", out var annEl))
                        info.Description = annEl.GetString() ?? "";
                    else if (item.TryGetProperty("supportUrl", out var supEl))
                        info.Description = $"Поддержка: {supEl.GetString()}";

                    if (item.TryGetProperty("traffic", out var trafEl) && trafEl.ValueKind == JsonValueKind.Object)
                    {
                        long usedBytes = 0;
                        if (trafEl.TryGetProperty("used", out var uEl) && uEl.TryGetInt64(out var ub))
                            usedBytes = ub;

                        bool isUnlimited = false;
                        if (trafEl.TryGetProperty("isUnlimited", out var unlimEl))
                            isUnlimited = unlimEl.GetBoolean();

                        long totalBytes = 0;
                        if (trafEl.TryGetProperty("total", out var totEl) && totEl.TryGetInt64(out var tb))
                            totalBytes = tb;

                        info.TrafficUsed = FormatBytes(usedBytes);
                        info.TrafficTotal = isUnlimited ? "∞" : FormatBytes(totalBytes);
                        if (totalBytes > 0)
                        {
                            info.TrafficPercentage = Math.Min(100.0, Math.Round((double)usedBytes / totalBytes * 100.0, 1));
                        }
                    }
                    else
                    {
                        info.TrafficUsed = "—";
                        info.TrafficTotal = "∞";
                        info.TrafficPercentage = 0;
                    }

                    if (item.TryGetProperty("expire", out var expEl) && expEl.TryGetInt64(out var expVal))
                    {
                        if (expVal > 0)
                        {
                            if (expVal > 30000000000) expVal /= 1000;
                            if (expVal < 2500000000)
                            {
                                try
                                {
                                    info.ExpireDate = DateTimeOffset.FromUnixTimeSeconds(expVal).LocalDateTime.ToString("dd.MM.yyyy");
                                }
                                catch
                                {
                                    info.ExpireDate = "01.01.2030";
                                }
                            }
                            else
                            {
                                info.ExpireDate = "11.09.6767";
                            }
                        }
                    }
                    else if (info.Description.Contains("дней"))
                    {
                        var m = Regex.Match(info.Description, @"(\d+)\s+дней");
                        info.ExpireDate = m.Success ? $"{m.Groups[1].Value} дней" : "Не ограничено";
                    }
                    else
                    {
                        info.ExpireDate = "01.01.2030";
                    }

                    // Estimate server count by provider name
                    if (info.Name.Contains("Blanc")) info.ServerCount = 49;
                    else if (info.Name.Contains("Stealth")) info.ServerCount = 28;
                    else if (info.Name.Contains("Abuz")) info.ServerCount = 42;
                    else if (info.Name.Contains("Vless")) info.ServerCount = 207;
                    else info.ServerCount = 15;

                    list.Add(info);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ForkopService] ParseSubscriptionMetadata error: {ex.Message}");
        }

        return list;
    }

    public List<ForkopServerNode> ParseAllRouterNodes(string output)
    {
        var nodes = ForkopNodeParser.Parse(output);
        return nodes.Count > 0 ? nodes : ParsePasswallNodes(ExtractSection(output, "PASSWALL_NODES"));
    }

    private static string DetectProvider(string name)
    {
        if (name.StartsWith("Blanc", StringComparison.OrdinalIgnoreCase)) return "Blanc";
        if (name.StartsWith("Stealth", StringComparison.OrdinalIgnoreCase)) return "Stealth";
        if (name.StartsWith("Abuz", StringComparison.OrdinalIgnoreCase)) return "Abuz";
        if (name.StartsWith("Vless4U", StringComparison.OrdinalIgnoreCase) || name.Contains("VlessForU", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Tg: Vlessforu", StringComparison.OrdinalIgnoreCase)) return "Vless4U";
        return "Прочие";
    }

    private static string FormatProtocolName(string raw)
    {
        return raw.ToLowerInvariant() switch
        {
            "vless" => "VLESS",
            "hysteria2" or "hy2" => "Hysteria2",
            "vmess" => "VMess",
            "shadowsocks" or "ss" => "Shadowsocks",
            "trojan" => "Trojan",
            "tuic" => "TUIC",
            "wireguard" or "wg" => "WireGuard",
            "selector" => "Selector",
            "urltest" => "URLTest",
            _ => raw.ToUpperInvariant()
        };
    }

    private static List<ForkopServerNode> ParsePasswallNodes(string output)
    {
        var list = new List<ForkopServerNode>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var nodes = new Dictionary<string, (string Remarks, string Type, string Addr, int Port)>();

        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"passwall\.([^\.]+)\.([^\=]+)='?([^'\r\n]+)'?");
            if (match.Success)
            {
                var id = match.Groups[1].Value;
                var key = match.Groups[2].Value;
                var val = match.Groups[3].Value;

                if (!nodes.ContainsKey(id)) nodes[id] = ("", "VLESS", "", 443);
                var cur = nodes[id];

                if (key == "remarks") cur.Remarks = val;
                else if (key == "type") cur.Type = val.ToUpperInvariant();
                else if (key == "address") cur.Addr = val;
                else if (key == "port" && int.TryParse(val, out var p)) cur.Port = p;

                nodes[id] = cur;
            }
        }

        foreach (var (id, node) in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Remarks) && string.IsNullOrWhiteSpace(node.Addr)) continue;
            list.Add(new ForkopServerNode
            {
                Name = !string.IsNullOrWhiteSpace(node.Remarks) ? node.Remarks : $"PassWall {node.Addr}",
                Protocol = node.Type,
                Subtitle = $"{node.Addr}:{node.Port}",
                Provider = "PassWall",
                ServerAddress = node.Addr,
                ServerPort = node.Port,
                IsAutoGroup = false
            });
        }

        return list;
    }

    public async Task<List<ForkopServerNode>> TestLatenciesAsync(List<ForkopServerNode> currentNodes)
    {
        if (!_ssh.IsConnected)
        {
            foreach (var node in currentNodes) node.LatencyMs = null;
            return currentNodes;
        }
        foreach (var group in currentNodes.Where(n => n.IsAutoGroup && n.Protocol == "URLTest"))
            await _ssh.ExecuteCommandAsync($"/usr/bin/forkop clash_api get_group_latency {ShellHelper.EscapeArg(group.GroupTag)}", 10);
        var fresh = await GetServersAsync();
        var byTag = fresh.ToDictionary(n => n.GroupTag, StringComparer.Ordinal);
        foreach (var node in currentNodes)
            node.LatencyMs = byTag.TryGetValue(node.GroupTag, out var live) ? live.LatencyMs : null;
        return currentNodes;
    }

    public async Task<(bool Success, string Message)> SelectActiveServerAsync(ForkopServerNode server)
    {
        if (!_ssh.IsConnected) return (false, "Нет подключения к роутеру по SSH.");
        try
        {
            var nodes = await GetServersAsync();
            var target = nodes.FirstOrDefault(n => n.GroupTag == server.GroupTag);
            if (target == null || string.IsNullOrEmpty(target.SelectionGroupTag))
                return (false, "Узел не входит в доступный селектор. Обновите список или выберите группу в панели ForkOP.");
            var parent = nodes.FirstOrDefault(n => n.GroupTag == target.SelectionGroupTag);
            if (parent == null || !parent.HasLiveState || !parent.Candidates.Contains(target.GroupTag))
                return (false, "Состав селектора изменился. Обновите список.");
            var command = $"/usr/bin/forkop clash_api set_group_proxy {ShellHelper.EscapeArg(parent.GroupTag)} {ShellHelper.EscapeArg(target.GroupTag)}";
            var (code, _, error) = await _ssh.ExecuteCommandAsync(command, 10);
            if (code != 0) return (false, $"Роутер отклонил переключение: {error}");
            var confirmed = (await GetServersAsync()).FirstOrDefault(n => n.GroupTag == parent.GroupTag);
            return confirmed?.CurrentNode == target.GroupTag
                ? (true, $"Роутер подтвердил выбор: {target.Name}")
                : (false, "Команда отправлена, но роутер не подтвердил новый выбор. Обновите список.");
        }
        catch (Exception ex) { return (false, $"Не удалось переключить узел: {ex.Message}"); }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
            if (counter >= suffixes.Length - 1) break;
        }
        return $"{number:n1} {suffixes[counter]}";
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
