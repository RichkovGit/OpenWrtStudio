using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface IPackageManagerService
{
    Task<(bool Success, string Output)> UpdateListsAsync();
    Task<List<PackageItem>> GetInstalledPackagesAsync();
    Task<List<PackageItem>> SearchPackagesAsync(string query);
    Task<(bool Success, string Output)> InstallPackageAsync(string name, string? customInstallCmd = null);
    Task<(bool Success, string Output)> RemovePackageAsync(string name);
    Task<List<PackageItem>> GetCuratedPackagesAsync();
    Task<List<CustomFeed>> GetCustomFeedsAsync();
    Task<(bool Success, string Message)> AddCustomFeedAsync(CustomFeed feed);
    Task<(bool Success, string Message)> RemoveCustomFeedAsync(string feedName);
}

public class PackageManagerService : IPackageManagerService
{
    private readonly ISshService _ssh;

    public PackageManagerService(ISshService ssh)
    {
        _ssh = ssh;
    }

    public async Task<(bool Success, string Output)> UpdateListsAsync()
    {
        var (code, outStr, err) = await _ssh.ExecuteCommandAsync("opkg update", 45);
        return code == 0
            ? (true, outStr)
            : (false, string.IsNullOrWhiteSpace(err) ? outStr : err);
    }

    public async Task<List<PackageItem>> GetInstalledPackagesAsync()
    {
        var list = new List<PackageItem>();
        var (code, outStr, _) = await _ssh.ExecuteCommandAsync("opkg list-installed", 15);
        if (code != 0 || string.IsNullOrWhiteSpace(outStr)) return list;

        foreach (var line in outStr.Split('\n'))
        {
            var parts = line.Split(" - ", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                list.Add(new PackageItem
                {
                    Name = parts[0].Trim(),
                    Version = parts[1].Trim(),
                    InstalledVersion = parts[1].Trim(),
                    IsInstalled = true,
                    Category = DetermineCategory(parts[0].Trim())
                });
            }
        }
        return list;
    }

    public async Task<List<PackageItem>> SearchPackagesAsync(string query)
    {
        var list = new List<PackageItem>();
        if (string.IsNullOrWhiteSpace(query)) return list;

        var safeQuery = Regex.Replace(query, @"[^a-zA-Z0-9_\-\.]", "");
        var (code, outStr, _) = await _ssh.ExecuteCommandAsync($"opkg list | grep -i '{safeQuery}' | head -n 100", 15);
        if (code != 0 || string.IsNullOrWhiteSpace(outStr)) return list;

        foreach (var line in outStr.Split('\n'))
        {
            var parts = line.Split(" - ", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var name = parts[0].Trim();
                var version = parts[1].Trim();
                var desc = parts.Length >= 3 ? parts[2].Trim() : "";

                list.Add(new PackageItem
                {
                    Name = name,
                    Version = version,
                    Description = desc,
                    Category = DetermineCategory(name)
                });
            }
        }
        return list;
    }

    public async Task<(bool Success, string Output)> InstallPackageAsync(string name, string? customInstallCmd = null)
    {
        var cmd = !string.IsNullOrWhiteSpace(customInstallCmd)
            ? customInstallCmd
            : $"opkg install {name}";

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(cmd, 60);
        return code == 0
            ? (true, outStr)
            : (false, string.IsNullOrWhiteSpace(err) ? outStr : err);
    }

    public async Task<(bool Success, string Output)> RemovePackageAsync(string name)
    {
        var (code, outStr, err) = await _ssh.ExecuteCommandAsync($"opkg remove {name} --autoremove", 40);
        return code == 0
            ? (true, outStr)
            : (false, string.IsNullOrWhiteSpace(err) ? outStr : err);
    }

    public async Task<List<PackageItem>> GetCuratedPackagesAsync()
    {
        var curated = GetDefaultCuratedList();

        // Check which ones are already installed
        try
        {
            var (code, outStr, _) = await _ssh.ExecuteCommandAsync("opkg list-installed", 10);
            if (code == 0 && !string.IsNullOrWhiteSpace(outStr))
            {
                var installedMap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in outStr.Split('\n'))
                {
                    var parts = line.Split(" - ");
                    if (parts.Length > 0)
                    {
                        installedMap.Add(parts[0].Trim());
                    }
                }

                foreach (var item in curated)
                {
                    if (installedMap.Contains(item.Name))
                    {
                        item.IsInstalled = true;
                    }
                }
            }
        }
        catch
        {
            // fallback
        }

        return curated;
    }

    private static List<PackageItem> GetDefaultCuratedList()
    {
        return new List<PackageItem>
        {
            // --- ТЕМЫ LUCI ---
            new()
            {
                Name = "luci-theme-argon",
                Category = "Темы оформления",
                Description = "Самая популярная и красивая тема для OpenWrt LuCI с поддержкой темного режима и градиентов.",
                Icon = "Color24",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-argon-config",
                Category = "Темы оформления",
                Description = "Панель детальной настройки темы Argon: сменяемые фоновые обои, размытие (blur), логотипы.",
                Icon = "ColorBackground24",
                IsCurated = true
            },
            new()
            {
                Name = "luci-theme-material",
                Category = "Темы оформления",
                Description = "Легковесная быстрая тема LuCI в строгом стиле Material Design от Google.",
                Icon = "DesignIdeas24",
                IsCurated = true
            },
            new()
            {
                Name = "luci-theme-design",
                Category = "Темы оформления",
                Description = "Минималистичный чистый дизайн интерфейса OpenWrt с фокусом на читаемость.",
                Icon = "StyleGuide24",
                IsCurated = true
            },
            new()
            {
                Name = "luci-theme-edge",
                Category = "Темы оформления",
                Description = "Темная контрастная тема в стиле Microsoft Edge для комфортной работы ночью.",
                Icon = "DarkTheme24",
                IsCurated = true
            },

            // --- ПРОКСИ, VPN И ОБХОД БЛОКИРОВОК ---
            new()
            {
                Name = "luci-app-mihomo",
                Category = "Прокси и маршрутизация",
                Description = "Управление ядром Mihomo (Clash.Meta) через веб-панель OpenWrt. TUN-режим, правила, DNS.",
                Icon = "ShieldKeyhole24",
                IsCurated = true,
                InstallCommand = "opkg install luci-app-mihomo || opkg install openwrt-mihomo"
            },
            new()
            {
                Name = "mihomo",
                Category = "Прокси и маршрутизация",
                Description = "Бинарное ядро Mihomo (Clash.Meta) для точечной маршрутизации, TUN режима и высокой скорости.",
                Icon = "Cpu24",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-passwall",
                Category = "Прокси и маршрутизация",
                Description = "PassWall — мощный комбайн для обхода блокировок: VLESS, Shadowsocks, Trojan, Xray, Hysteria.",
                Icon = "GlobeShield24",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-openclash",
                Category = "Прокси и маршрутизация",
                Description = "OpenClash — клиент Clash для роутеров OpenWrt с широкими возможностями перенаправления трафика.",
                Icon = "NetworkCheck24",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-wireguard",
                Category = "Прокси и маршрутизация",
                Description = "Быстрый современный криптографический VPN-клиент и сервер WireGuard для OpenWrt.",
                Icon = "LockClosed24",
                IsCurated = true
            },

            // --- СИСТЕМНЫЕ СЕРВИСЫ И УТИЛИТЫ ---
            new()
            {
                Name = "luci-app-ttyd",
                Category = "Системные утилиты",
                Description = "Удобный веб-терминал командной строки прямо в браузере внутри интерфейса LuCI.",
                Icon = "WindowConsole20",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-dockerman",
                Category = "Системные утилиты",
                Description = "Полноценный графический интерфейс для управления контейнерами Docker на роутере.",
                Icon = "Box24",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-adblock",
                Category = "Системные утилиты",
                Description = "Блокировка рекламы, фишинга и трекеров на уровне всей локальной сети без плагинов в браузере.",
                Icon = "ShieldProhibited24",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-diskman",
                Category = "Системные утилиты",
                Description = "Управление жесткими дисками, форматирование, создание разделов и автомонтирование USB.",
                Icon = "HardDrive24",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-samba4",
                Category = "Системные утилиты",
                Description = "Файловый сервер Samba4 (Windows сетевые папки) для общего доступа к файлам с флешек и HDD.",
                Icon = "FolderShared24",
                IsCurated = true
            },
            new()
            {
                Name = "luci-app-upnp",
                Category = "Системные утилиты",
                Description = "Служба Universal Plug and Play (UPnP/NAT-PMP) для автоматического проброса портов играми.",
                Icon = "ArrowRouting24",
                IsCurated = true
            }
        };
    }

    public async Task<List<CustomFeed>> GetCustomFeedsAsync()
    {
        var feeds = new List<CustomFeed>();
        var (code, outStr, _) = await _ssh.ExecuteCommandAsync("cat /etc/opkg/customfeeds.conf 2>/dev/null", 5);
        if (code == 0 && !string.IsNullOrWhiteSpace(outStr))
        {
            foreach (var line in outStr.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                var isEnabled = !trimmed.StartsWith("#");
                var cleanLine = trimmed.TrimStart('#', ' ');

                var m = Regex.Match(cleanLine, @"^src/gz\s+([^\s]+)\s+(https?://[^\s]+)");
                if (m.Success)
                {
                    feeds.Add(new CustomFeed
                    {
                        Name = m.Groups[1].Value,
                        Url = m.Groups[2].Value,
                        IsEnabled = isEnabled,
                        Description = "Пользовательский репозиторий"
                    });
                }
            }
        }

        if (feeds.Count == 0)
        {
            // Preset recommendations
            feeds.Add(new CustomFeed
            {
                Name = "forkop_packages",
                Url = "https://op.akarin.top/packages",
                IsEnabled = false,
                Description = "Репозиторий ForkOP с пакетами PassWall, SSR, Mihomo"
            });
            feeds.Add(new CustomFeed
            {
                Name = "immortalwrt_luci",
                Url = "https://downloads.immortalwrt.org/snapshots/packages/x86_64/luci",
                IsEnabled = false,
                Description = "Репозиторий ImmortalWrt (богатый набор luci-app и тем)"
            });
            feeds.Add(new CustomFeed
            {
                Name = "sirpdboy_packages",
                Url = "https://github.com/sirpdboy/sirpdboy-package",
                IsEnabled = false,
                Description = "Популярный набор китайских сетевых утилит и тем"
            });
        }

        return feeds;
    }

    public async Task<(bool Success, string Message)> AddCustomFeedAsync(CustomFeed feed)
    {
        var line = $"src/gz {feed.Name} {feed.Url}\n";
        var appendCmd = $"echo '{line.Trim()}' >> /etc/opkg/customfeeds.conf";
        var (code, _, err) = await _ssh.ExecuteCommandAsync(appendCmd, 10);
        return code == 0
            ? (true, $"Репозиторий {feed.Name} успешно добавлен!")
            : (false, $"Ошибка: {err}");
    }

    public async Task<(bool Success, string Message)> RemoveCustomFeedAsync(string feedName)
    {
        var sedCmd = $"sed -i '/{feedName}/d' /etc/opkg/customfeeds.conf";
        var (code, _, err) = await _ssh.ExecuteCommandAsync(sedCmd, 10);
        return code == 0
            ? (true, $"Репозиторий {feedName} удален.")
            : (false, $"Ошибка: {err}");
    }

    private static string DetermineCategory(string packageName)
    {
        if (packageName.StartsWith("luci-theme-")) return "Темы оформления";
        if (packageName.Contains("passwall") || packageName.Contains("clash") || packageName.Contains("mihomo") ||
            packageName.Contains("v2ray") || packageName.Contains("xray") || packageName.Contains("wireguard"))
            return "Прокси и маршрутизация";
        if (packageName.StartsWith("luci-app-")) return "Плагины LuCI";
        if (packageName.StartsWith("kmod-")) return "Драйверы ядра";
        return "Система и утилиты";
    }
}
