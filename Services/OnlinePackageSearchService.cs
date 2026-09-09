using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public class OnlinePackageItem
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string SourceRepo { get; set; } = "Официальный OpenWrt";
    public string Category { get; set; } = "Сеть";
    public string RepoUrl { get; set; } = "";
    public string InstallCommand { get; set; } = "";
    public bool IsCurated { get; set; }
}

public interface IOnlinePackageSearchService
{
    Task<List<OnlinePackageItem>> SearchOnlineAsync(string query);
    List<OnlinePackageItem> GetPreloadedCatalog();
}

public class OnlinePackageSearchService : IOnlinePackageSearchService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly List<OnlinePackageItem> _knownCatalog;

    public OnlinePackageSearchService()
    {
        _knownCatalog = BuildComprehensiveOnlineCatalog();
    }

    public async Task<List<OnlinePackageItem>> SearchOnlineAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return _knownCatalog;
        }

        var q = query.Trim().ToLowerInvariant();

        // 1. Filter our known comprehensive community & official database
        var matches = _knownCatalog
            .Where(p => p.Name.ToLowerInvariant().Contains(q) ||
                        p.Description.ToLowerInvariant().Contains(q) ||
                        p.Category.ToLowerInvariant().Contains(q) ||
                        p.SourceRepo.ToLowerInvariant().Contains(q))
            .ToList();

        // 2. Try online query to openwrt package index or sirpdboy/forkop index
        try
        {
            var url = $"https://openwrt.org/docs/guide-user/services/webserver/start?do=search&id={Uri.EscapeDataString(query)}";
            // Also query GitHub API for luci-app / openwrt packages
            var ghUrl = $"https://api.github.com/search/repositories?q=openwrt+{Uri.EscapeDataString(query)}&sort=stars&per_page=8";
            using var req = new HttpRequestMessage(HttpMethod.Get, ghUrl);
            req.Headers.Add("User-Agent", "OpenWrtStudio/2.0");

            var resp = await _httpClient.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var repoName = item.GetProperty("name").GetString() ?? "";
                        var desc = item.TryGetProperty("description", out var dProp) ? (dProp.GetString() ?? "") : "";
                        var htmlUrl = item.GetProperty("html_url").GetString() ?? "";

                        if (!matches.Any(m => m.Name.Equals(repoName, StringComparison.OrdinalIgnoreCase)))
                        {
                            matches.Add(new OnlinePackageItem
                            {
                                Name = repoName,
                                Version = "latest (GitHub)",
                                Description = string.IsNullOrWhiteSpace(desc) ? "Сторонний пакет OpenWrt из сообщества GitHub" : desc,
                                SourceRepo = "GitHub Community",
                                Category = repoName.Contains("theme") ? "Темы LuCI" : "Плагины LuCI",
                                RepoUrl = htmlUrl,
                                InstallCommand = $"opkg install {repoName}"
                            });
                        }
                    }
                }
            }
        }
        catch
        {
            // Offline or rate-limited fallback: our rich local catalog still returns excellent results
        }

        return matches;
    }

    public List<OnlinePackageItem> GetPreloadedCatalog() => _knownCatalog;

    private static List<OnlinePackageItem> BuildComprehensiveOnlineCatalog()
    {
        return new List<OnlinePackageItem>
        {
            // --- FORKOP & КИТАЙСКИЕ РЕПОЗИТОРИИ ---
            new()
            {
                Name = "forkop-repo",
                Version = "latest",
                Description = "Пакет установки репозитория ForkOP (ForkCP) со сборкой PassWall, SSR+, SmartDNS и китайских пакетов.",
                SourceRepo = "ForkOP Custom Feed",
                Category = "Репозитории",
                InstallCommand = "echo 'src/gz forkop https://op.akarin.top/packages' >> /etc/opkg/customfeeds.conf && opkg update",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-passwall",
                Version = "4.7x",
                Description = "PassWall 2 — легендарный комбайн для VLESS Reality, Shadowsocks, Trojan, Hysteria 2, Xray.",
                SourceRepo = "ForkOP / ImmortalWrt",
                Category = "VPN и Прокси",
                InstallCommand = "opkg install luci-app-passwall",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-ssr-plus",
                Version = "latest",
                Description = "ShadowsocksR Plus+ — быстрый клиент Shadowsocks / V2Ray / Trojan / Xray с автопереключением нод.",
                SourceRepo = "ForkOP Feed",
                Category = "VPN и Прокси",
                InstallCommand = "opkg install luci-app-ssr-plus",
                IsCurated = true
            },

            // --- AMNEZIA WIREGUARD (AWG) ---
            new()
            {
                Name = "luci-app-amneziawg",
                Version = "1.0",
                Description = "Amnezia WireGuard — обфусцированный WireGuard против DPI блокировок и замедлений провайдеров в РФ.",
                SourceRepo = "AmneziaVPN Community",
                Category = "VPN и Прокси",
                InstallCommand = "opkg install kmod-amneziawg amneziawg-tools luci-app-amneziawg",
                IsCurated = true
            },

            // --- SING-BOX ---
            new()
            {
                Name = "luci-app-sing-box",
                Version = "1.9x",
                Description = "Sing-box — универсальная быстрая платформа прокси нового поколения: TUN, VLESS Reality, TUIC, Hysteria 2.",
                SourceRepo = "ImmortalWrt / SagerNet",
                Category = "VPN и Прокси",
                InstallCommand = "opkg install sing-box luci-app-sing-box",
                IsCurated = true
            },

            // --- MIHOMO (CLASH.META) ---
            new()
            {
                Name = "luci-app-mihomo",
                Version = "1.18+",
                Description = "Mihomo (Clash.Meta) — ядро с точечной маршрутизацией, мощным DNS движком, Fake-IP и перехватом трафика.",
                SourceRepo = "Mihomo Official Feed",
                Category = "VPN и Прокси",
                InstallCommand = "opkg install luci-app-mihomo || opkg install openwrt-mihomo",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-openclash",
                Version = "0.46+",
                Description = "OpenClash — клиент Clash / Mihomo для роутеров OpenWrt с визуальным контролем правил.",
                SourceRepo = "OpenClash Feed",
                Category = "VPN и Прокси",
                InstallCommand = "opkg install luci-app-openclash",
                IsCurated = true
            },

            // --- TAILSCALE & ZEROTIER ---
            new()
            {
                Name = "luci-app-tailscale",
                Version = "1.60+",
                Description = "Tailscale — P2P Mesh VPN на базе WireGuard для объединения роутера, ПК и смартфонов в одну сеть без белого IP.",
                SourceRepo = "Официальный OpenWrt",
                Category = "VPN и Прокси",
                InstallCommand = "opkg install tailscale luci-app-tailscale",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-zerotier",
                Version = "1.12+",
                Description = "ZeroTier One — глобальная виртуальная ячеистая сеть уровня Ethernet (L2 SDN).",
                SourceRepo = "Официальный OpenWrt",
                Category = "VPN и Прокси",
                InstallCommand = "opkg install zerotier luci-app-zerotier",
                IsCurated = true
            },

            // --- КЛАССИЧЕСКИЕ VPN ---
            new()
            {
                Name = "luci-app-wireguard",
                Version = "1.0",
                Description = "WireGuard — быстрый и безопасный VPN протокол в ядре Linux.",
                SourceRepo = "Официальный OpenWrt",
                Category = "VPN и Прокси",
                InstallCommand = "opkg install wireguard-tools luci-app-wireguard",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-openvpn",
                Version = "2.6+",
                Description = "OpenVPN — проверенный временем VPN-сервер и клиент с поддержкой SSL/TLS сертификатов.",
                SourceRepo = "Официальный OpenWrt",
                Category = "VPN и Прокси",
                InstallCommand = "opkg install openvpn-openssl luci-app-openvpn",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-ipsec-server",
                Version = "5.9+",
                Description = "StrongSwan IPsec / IKEv2 VPN-сервер для прямого подключения iPhone и Android без сторонних приложений.",
                SourceRepo = "Официальный OpenWrt",
                Category = "VPN и Прокси",
                InstallCommand = "opkg install strongswan luci-app-ipsec-server",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-softethervpn",
                Version = "5.0+",
                Description = "SoftEther VPN — высокоскоростной мультипротокольный клиент и сервер (L2TP, IPsec, OpenVPN, MS-SSTP).",
                SourceRepo = "Официальный OpenWrt",
                Category = "VPN и Прокси",
                InstallCommand = "opkg install softethervpn5-server luci-app-softethervpn",
                IsCurated = true
            },

            // --- DNS & АНТИРЕКЛАМА ---
            new()
            {
                Name = "luci-app-adguardhome",
                Version = "0.107+",
                Description = "AdGuard Home — мощный локальный DNS сервер с блокировкой рекламы, родительским контролем и DoH/DoT.",
                SourceRepo = "ImmortalWrt / Sirpdboy",
                Category = "DNS и Фильтрация",
                InstallCommand = "opkg install adguardhome luci-app-adguardhome",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-smartdns",
                Version = "latest",
                Description = "SmartDNS — локальный DNS-акселератор, опрашивающий сразу несколько серверов и отдающий самый быстрый IP.",
                SourceRepo = "ForkOP / ImmortalWrt",
                Category = "DNS и Фильтрация",
                InstallCommand = "opkg install smartdns luci-app-smartdns",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-mosdns",
                Version = "v5",
                Description = "MosDNS — продвинутый плагин-ориентированный DNS-роутер для разделения RU и мирового трафика.",
                SourceRepo = "Sirpdboy Feed",
                Category = "DNS и Фильтрация",
                InstallCommand = "opkg install mosdns luci-app-mosdns",
                IsCurated = true
            },

            // --- ТЕМЫ LUCI ---
            new()
            {
                Name = "luci-theme-argon",
                Version = "2.3+",
                Description = "Красивая современная тема LuCI с поддержкой ночного режима, обоев и анимаций.",
                SourceRepo = "JerryKuku GitHub / OpenWrt",
                Category = "Темы LuCI",
                InstallCommand = "opkg install luci-theme-argon luci-app-argon-config",
                IsCurated = true
            },
            new()
            {
                Name = "luci-theme-design",
                Version = "latest",
                Description = "Элегантная минималистичная тема Design с чистым плоским интерфейсом.",
                SourceRepo = "0x676e67 GitHub",
                Category = "Темы LuCI",
                InstallCommand = "opkg install luci-theme-design",
                IsCurated = true
            },
            new()
            {
                Name = "luci-theme-edge",
                Version = "latest",
                Description = "Контрастная темная тема Edge в стиле браузера Microsoft Edge.",
                SourceRepo = "ImmortalWrt",
                Category = "Темы LuCI",
                InstallCommand = "opkg install luci-theme-edge",
                IsCurated = true
            },

            // --- СИСТЕМНЫЕ СЕРВИСЫ ---
            new()
            {
                Name = "luci-app-ttyd",
                Version = "latest",
                Description = "Веб-консоль терминала прямо в веб-интерфейсе роутера.",
                SourceRepo = "Официальный OpenWrt",
                Category = "Система",
                InstallCommand = "opkg install ttyd luci-app-ttyd",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-dockerman",
                Version = "latest",
                Description = "Управление контейнерами Docker на OpenWrt роутере.",
                SourceRepo = "Официальный OpenWrt",
                Category = "Система",
                InstallCommand = "opkg install dockerd docker docker-compose luci-app-dockerman",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-diskman",
                Version = "latest",
                Description = "Графический менеджер жестких дисков, разделов и автомонтирования USB накопителей.",
                SourceRepo = "Lienol GitHub",
                Category = "Система",
                InstallCommand = "opkg install luci-app-diskman",
                IsCurated = true
            }
        };
    }
}
