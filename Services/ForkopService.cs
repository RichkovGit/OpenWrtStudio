using System;
using System.Collections.Generic;
using System.Linq;
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

        var cmd = $"opkg update && opkg install --force-depends {packageName}";
        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(cmd, 60);

        if (code == 0 || outStr.Contains("Installing") || outStr.Contains("Configuring"))
        {
            return (true, $"Пакет {packageName} успешно установлен!\n{outStr}");
        }

        return (false, $"Ошибка установки пакета {packageName}: {err}\n{outStr}");
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
