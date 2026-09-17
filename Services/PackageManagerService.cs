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
    Task<(bool Success, string Message)> RescueLuciAsync();
}

public class PackageManagerService : IPackageManagerService
{
    private readonly ISshService _ssh;
    private string? _detectedPm;

    public PackageManagerService(ISshService ssh)
    {
        _ssh = ssh;
    }

    public async Task<string> DetectPackageManagerAsync()
    {
        if (_detectedPm != null && _ssh.IsConnected) return _detectedPm;

        var (code, outStr, _) = await _ssh.ExecuteCommandAsync(
            "if command -v apk >/dev/null 2>&1; then echo 'apk'; elif command -v opkg >/dev/null 2>&1; then echo 'opkg'; else echo 'none'; fi", 5);
        var res = outStr?.Trim().ToLowerInvariant() ?? "";
        if (res.Contains("apk")) _detectedPm = "apk";
        else if (res.Contains("opkg")) _detectedPm = "opkg";
        else _detectedPm = "opkg";

        return _detectedPm;
    }

    public async Task<(bool Success, string Output)> UpdateListsAsync()
    {
        var pm = await DetectPackageManagerAsync();
        var cmd = pm == "apk" ? "apk update" : "opkg update";
        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(cmd, 45);
        return code == 0
            ? (true, outStr)
            : (false, string.IsNullOrWhiteSpace(err) ? outStr : err);
    }

    public async Task<List<PackageItem>> GetInstalledPackagesAsync()
    {
        var list = new List<PackageItem>();
        var pm = await DetectPackageManagerAsync();

        if (pm == "apk")
        {
            var (code, outStr, _) = await _ssh.ExecuteCommandAsync("apk info -v", 15);
            if (code != 0 || string.IsNullOrWhiteSpace(outStr)) return list;

            var versionPattern = new Regex(@"^(.+?)-([0-9].*)$");
            foreach (var line in outStr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var match = versionPattern.Match(trimmed);
                var name = match.Success ? match.Groups[1].Value : trimmed;
                var version = match.Success ? match.Groups[2].Value : "";

                list.Add(new PackageItem
                {
                    Name = name,
                    Version = version,
                    InstalledVersion = version,
                    IsInstalled = true,
                    Category = DetermineCategory(name)
                });
            }
        }
        else
        {
            var (code, outStr, _) = await _ssh.ExecuteCommandAsync("opkg list-installed", 15);
            if (code != 0 || string.IsNullOrWhiteSpace(outStr)) return list;

            foreach (var line in outStr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
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
        }
        return list;
    }

    public async Task<List<PackageItem>> SearchPackagesAsync(string query)
    {
        var list = new List<PackageItem>();
        if (string.IsNullOrWhiteSpace(query)) return list;

        var safeQuery = Regex.Replace(query, @"[^a-zA-Z0-9_\-\.]", "");
        var pm = await DetectPackageManagerAsync();

        if (pm == "apk")
        {
            var (code, outStr, _) = await _ssh.ExecuteCommandAsync($"apk search -v '{safeQuery}' 2>/dev/null | head -n 100", 15);
            if (code != 0 || string.IsNullOrWhiteSpace(outStr)) return list;

            var versionPattern = new Regex(@"^(.+?)-([0-9].*)$");
            foreach (var line in outStr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Format: pkg-1.2.3 - Description or pkg-1.2.3
                var parts = trimmed.Split(" - ", StringSplitOptions.RemoveEmptyEntries);
                var pkgWithVer = parts[0].Trim();
                var desc = parts.Length > 1 ? string.Join(" - ", parts.Skip(1)).Trim() : "";

                var match = versionPattern.Match(pkgWithVer);
                var name = match.Success ? match.Groups[1].Value : pkgWithVer;
                var version = match.Success ? match.Groups[2].Value : "";

                list.Add(new PackageItem
                {
                    Name = name,
                    Version = version,
                    Description = desc,
                    Category = DetermineCategory(name)
                });
            }
        }
        else
        {
            var (code, outStr, _) = await _ssh.ExecuteCommandAsync($"opkg list | grep -i '{safeQuery}' | head -n 100", 15);
            if (code != 0 || string.IsNullOrWhiteSpace(outStr)) return list;

            foreach (var line in outStr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
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
        }
        return list;
    }

    public async Task<(bool Success, string Output)> InstallPackageAsync(string name, string? customInstallCmd = null)
    {
        var pm = await DetectPackageManagerAsync();
        string cmd;

        var lowerName = name.ToLowerInvariant();

        // 1. Specialized handling for community packages absent in default OpenWrt repos
        if (lowerName.Contains("argon"))
        {
            if (pm == "apk")
            {
                cmd = "(apk add luci-theme-argon luci-app-argon-config 2>/dev/null) || " +
                      "(curl -sL -k -o /tmp/luci-theme-argon.apk https://github.com/jerrykuku/luci-theme-argon/releases/download/v2.4.7/luci-theme-argon-2.4.7-r1.apk && " +
                      "curl -sL -k -o /tmp/luci-app-argon-config.apk https://github.com/jerrykuku/luci-theme-argon/releases/download/v2.4.7/luci-app-argon-config-2.4.7-r1.apk && " +
                      "apk add --allow-untrusted /tmp/luci-theme-argon.apk /tmp/luci-app-argon-config.apk)";
            }
            else
            {
                cmd = "(opkg install luci-theme-argon luci-app-argon-config 2>/dev/null) || " +
                      "(curl -sL -k -o /tmp/luci-theme-argon.ipk https://github.com/jerrykuku/luci-theme-argon/releases/download/v2.4.7/luci-theme-argon_2.4.7_all.ipk && " +
                      "curl -sL -k -o /tmp/luci-app-argon-config.ipk https://github.com/jerrykuku/luci-theme-argon/releases/download/v2.4.7/luci-app-argon-config_2.4.7_all.ipk && " +
                      "opkg install /tmp/luci-theme-argon.ipk /tmp/luci-app-argon-config.ipk)";
            }
        }
        else if (lowerName.Contains("diskman"))
        {
            if (pm == "apk")
            {
                cmd = "apk add parted e2fsprogs smartmontools blkid lsblk luci-compat 2>/dev/null || true; " +
                      "mkdir -p /tmp/diskman_pkg && cd /tmp/diskman_pkg && " +
                      "curl -sL -k -o diskman.ipk https://github.com/lisaac/luci-app-diskman/releases/download/v0.2.11/luci-app-diskman_v0.2.11_all.ipk && " +
                      "tar -zxf diskman.ipk && tar -C / -zxf data.tar.gz";
            }
            else
            {
                cmd = "opkg update 2>/dev/null || true; " +
                      "opkg install parted e2fsprogs smartmontools blkid lsblk luci-compat 2>/dev/null || true; " +
                      "mkdir -p /tmp/diskman_pkg && cd /tmp/diskman_pkg && " +
                      "curl -sL -k -o diskman.ipk https://github.com/lisaac/luci-app-diskman/releases/download/v0.2.11/luci-app-diskman_v0.2.11_all.ipk && " +
                      "(opkg install diskman.ipk 2>/dev/null || (tar -zxf diskman.ipk && tar -C / -zxf data.tar.gz))";
            }
        }
        else if (lowerName.Contains("theme-design"))
        {
            return (false, "Тема luci-theme-design устарела (эпоха OpenWrt 18/19 на Lua) и несовместима с современным LuCI в OpenWrt 23.x / 24.x / 25.x (вызывает сбой диспетчера страниц ucode). Используйте официальные современные темы: luci-theme-argon, luci-theme-material или luci-theme-openwrt-2020.");
        }
        else if (pm == "apk")
        {
            if (!string.IsNullOrWhiteSpace(customInstallCmd))
            {
                // Translate opkg commands to apk equivalents
                cmd = customInstallCmd
                    .Replace("opkg install --force-depends", "apk add")
                    .Replace("opkg install", "apk add")
                    .Replace("opkg update", "apk update")
                    .Replace("opkg remove --autoremove", "apk del")
                    .Replace("opkg remove", "apk del");
            }
            else
            {
                cmd = $"apk add {name}";
            }
        }
        else
        {
            cmd = !string.IsNullOrWhiteSpace(customInstallCmd)
                ? customInstallCmd
                : $"opkg install {name}";
        }

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(cmd, 120);

        if (code == 0)
        {
            // Execute post-install uci-defaults and clear LuCI index cache so themes & apps appear immediately
            if (lowerName.StartsWith("luci-") || lowerName.Contains("theme") || lowerName.Contains("argon") || lowerName.Contains("diskman"))
            {
                await PostInstallLuciCleanupAsync();
            }
            return (true, outStr);
        }

        return (false, string.IsNullOrWhiteSpace(err) ? outStr : err);
    }

    private async Task PostInstallLuciCleanupAsync()
    {
        var cleanupScript = "for f in /etc/uci-defaults/*; do [ -f \"$f\" ] && ( sh \"$f\" 2>/dev/null || . \"$f\" 2>/dev/null ) && rm -f \"$f\" 2>/dev/null; done; " +
                            "rm -rf /tmp/luci-indexcache /tmp/luci-modulecache/ 2>/dev/null; " +
                            "/etc/init.d/rpcd restart 2>/dev/null || true; " +
                            "/etc/init.d/uhttpd restart 2>/dev/null || true";
        await _ssh.ExecuteCommandAsync(cleanupScript, 30);
    }

    public async Task<(bool Success, string Output)> RemovePackageAsync(string name)
    {
        var pm = await DetectPackageManagerAsync();
        var cmd = pm == "apk" ? $"apk del {name}" : $"opkg remove {name} --autoremove";
        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(cmd, 40);
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
            var installed = await GetInstalledPackagesAsync();
            var installedMap = new HashSet<string>(installed.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var item in curated)
            {
                if (installedMap.Contains(item.Name))
                {
                    item.IsInstalled = true;
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
                Name = "luci-theme-openwrt-2020",
                Category = "Темы оформления",
                Description = "Официальная современная тема OpenWrt 2020 со светлым адаптивным интерфейсом.",
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
        var pm = await DetectPackageManagerAsync();

        if (pm == "apk")
        {
            var (code, outStr, _) = await _ssh.ExecuteCommandAsync("cat /etc/apk/repositories.d/customfeeds.list /etc/apk/repositories 2>/dev/null", 5);
            if (code == 0 && !string.IsNullOrWhiteSpace(outStr))
            {
                foreach (var line in outStr.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    var isEnabled = !trimmed.StartsWith("#");
                    var cleanLine = trimmed.TrimStart('#', ' ');

                    if (cleanLine.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || cleanLine.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        var feedName = "custom_feed";
                        try
                        {
                            var uri = new Uri(cleanLine);
                            feedName = uri.Segments.Length > 0 ? uri.Segments[^1].Trim('/') : "custom_feed";
                            if (string.IsNullOrEmpty(feedName) || feedName == "packages.adb")
                            {
                                feedName = uri.Host;
                            }
                        }
                        catch
                        {
                            // ignore url parse error
                        }

                        feeds.Add(new CustomFeed
                        {
                            Name = feedName,
                            Url = cleanLine,
                            IsEnabled = isEnabled,
                            Description = "Пользовательский репозиторий apk"
                        });
                    }
                }
            }
        }
        else
        {
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
        var pm = await DetectPackageManagerAsync();
        string appendCmd;
        if (pm == "apk")
        {
            appendCmd = $"mkdir -p /etc/apk/repositories.d && echo '{feed.Url.Trim()}' >> /etc/apk/repositories.d/customfeeds.list";
        }
        else
        {
            var line = $"src/gz {feed.Name} {feed.Url}\n";
            appendCmd = $"mkdir -p /etc/opkg && echo '{line.Trim()}' >> /etc/opkg/customfeeds.conf";
        }

        var (code, _, err) = await _ssh.ExecuteCommandAsync(appendCmd, 10);
        return code == 0
            ? (true, $"Репозиторий {feed.Name} успешно добавлен!")
            : (false, $"Ошибка: {err}");
    }

    public async Task<(bool Success, string Message)> RemoveCustomFeedAsync(string feedName)
    {
        var pm = await DetectPackageManagerAsync();
        string sedCmd;
        if (pm == "apk")
        {
            sedCmd = $"sed -i '/{feedName}/d' /etc/apk/repositories.d/customfeeds.list 2>/dev/null || sed -i '/{feedName}/d' /etc/apk/repositories 2>/dev/null";
        }
        else
        {
            sedCmd = $"sed -i '/{feedName}/d' /etc/opkg/customfeeds.conf";
        }

        var (code, _, err) = await _ssh.ExecuteCommandAsync(sedCmd, 10);
        return code == 0
            ? (true, $"Репозиторий {feedName} удален.")
            : (false, $"Ошибка: {err}");
    }

    public async Task<(bool Success, string Message)> RescueLuciAsync()
    {
        if (!_ssh.IsConnected)
        {
            return (false, "Роутер не подключен по SSH.");
        }

        var rescueScript = "rm -rf /usr/lib/lua/luci/controller/design-config.lua /usr/lib/lua/luci/view/themes/design /www/luci-static/design 2>/dev/null || true; " +
                           "if [ -d /www/luci-static/argon ]; then uci set luci.main.mediaurlbase=/luci-static/argon; else uci set luci.main.mediaurlbase=/luci-static/bootstrap; fi; " +
                           "uci commit luci; " +
                           "rm -rf /tmp/luci-indexcache* /tmp/luci-modulecache* 2>/dev/null; " +
                           "/etc/init.d/rpcd restart 2>/dev/null || true; " +
                           "/etc/init.d/uhttpd restart 2>/dev/null || true";

        var (code, outStr, err) = await _ssh.ExecuteCommandAsync(rescueScript, 20);
        if (code == 0)
        {
            return (true, "Веб-интерфейс LuCI успешно восстановлен! Несовместимые контроллеры удалены, тема сброшена на рабочую (Argon/Bootstrap). Обновите вкладку в браузере (F5).");
        }

        return (false, $"Ошибка при восстановлении LuCI: {err}\n{outStr}");
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
