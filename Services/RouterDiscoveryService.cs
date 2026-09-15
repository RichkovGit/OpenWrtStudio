using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenWrtStudio.Models;
using Renci.SshNet;

namespace OpenWrtStudio.Services;

public interface IRouterDiscoveryService
{
    Task<List<string>> GetCandidateGatewayIpsAsync();
    Task<RouterDiscoveryResult?> DiscoverAndAuthenticateAsync(string username, string password, IProgress<string>? progress = null);
    Task<ConnectionProfile?> AutoAddDiscoveredRouterAsync(string username, string password, IProgress<string>? progress = null);
}

public class RouterDiscoveryService : IRouterDiscoveryService
{
    private readonly IProfileService _profileService;

    public RouterDiscoveryService(IProfileService profileService)
    {
        _profileService = profileService;
    }

    public Task<List<string>> GetCandidateGatewayIpsAsync()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var ipProps = ni.GetIPProperties();
                foreach (var gw in ipProps.GatewayAddresses)
                {
                    if (gw.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var ip = gw.Address.ToString();
                        if (ip != "0.0.0.0" && !ip.StartsWith("127."))
                        {
                            candidates.Add(ip);
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore network discovery errors
        }

        // Add standard OpenWrt fallback IPs
        var defaultPool = new[]
        {
            "192.168.10.1",
            "192.168.1.1",
            "192.168.0.1",
            "192.168.8.1",
            "192.168.2.1",
            "192.168.31.1",
            "openwrt.lan"
        };

        foreach (var ip in defaultPool)
        {
            candidates.Add(ip);
        }

        return Task.FromResult(candidates.ToList());
    }

    public async Task<RouterDiscoveryResult?> DiscoverAndAuthenticateAsync(string username, string password, IProgress<string>? progress = null)
    {
        var candidates = await GetCandidateGatewayIpsAsync();
        progress?.Report($"Поиск роутеров среди {candidates.Count} адресов...");

        // 1. Parallel port 22 check
        var reachableSshIps = new List<(string Ip, long Latency)>();
        var checkTasks = candidates.Select(async ip =>
        {
            var (isOpen, latency) = await TestTcpPortAsync(ip, 22, 1200);
            if (isOpen)
            {
                lock (reachableSshIps)
                {
                    reachableSshIps.Add((ip, latency));
                }
            }
        });

        await Task.WhenAll(checkTasks);

        if (reachableSshIps.Count == 0)
        {
            progress?.Report("Роутер с открытым SSH (порт 22) не обнаружен в локальной сети");
            return null;
        }

        // Sort by latency
        var sorted = reachableSshIps.OrderBy(x => x.Latency).ToList();

        // 2. Try authentication on responsive hosts
        foreach (var (ip, latency) in sorted)
        {
            progress?.Report($"Проверка учетных данных на {ip}...");
            var authResult = await TrySshAuthAsync(ip, 22, username, password, latency);
            if (authResult != null)
            {
                return authResult;
            }
        }

        return null;
    }

    public async Task<ConnectionProfile?> AutoAddDiscoveredRouterAsync(string username, string password, IProgress<string>? progress = null)
    {
        var result = await DiscoverAndAuthenticateAsync(username, password, progress);
        if (result == null || !result.AuthSucceeded)
        {
            return null;
        }

        var profiles = await _profileService.LoadProfilesAsync();
        var existing = profiles.FirstOrDefault(p => p.Host.Equals(result.IpAddress, StringComparison.OrdinalIgnoreCase));

        string profileName = $"{result.Hostname} ({result.Model}) - {result.IpAddress}";

        if (existing != null)
        {
            existing.Username = username;
            existing.Password = password;
            existing.Name = profileName;
            await _profileService.SaveProfilesAsync(profiles);
            progress?.Report($"Профиль обновлен: {profileName}");
            return existing;
        }

        var newProfile = new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = profileName,
            Host = result.IpAddress,
            Port = result.Port,
            Username = username,
            Password = password,
            AutoConnectOnStartup = true
        };

        profiles.Add(newProfile);
        await _profileService.SaveProfilesAsync(profiles);
        progress?.Report($"Роутер успешно добавлен: {profileName}");
        return newProfile;
    }

    private static async Task<(bool IsOpen, long LatencyMs)> TestTcpPortAsync(string host, int port, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            return (true, sw.ElapsedMilliseconds);
        }
        catch
        {
            return (false, -1);
        }
    }

    private static async Task<RouterDiscoveryResult?> TrySshAuthAsync(string host, int port, string username, string password, long latency)
    {
        return await Task.Run(() =>
        {
            try
            {
                var authMethod = new PasswordAuthenticationMethod(username, password);
                var connInfo = new ConnectionInfo(host, port, username, authMethod)
                {
                    Timeout = TimeSpan.FromSeconds(5)
                };

                using var ssh = new SshClient(connInfo);
                ssh.Connect();

                if (!ssh.IsConnected) return null;

                // Read sysinfo
                var modelCmd = ssh.RunCommand("cat /tmp/sysinfo/model 2>/dev/null || uname -m");
                var model = modelCmd.Result.Trim();
                if (string.IsNullOrEmpty(model)) model = "OpenWrt Router";

                var hostCmd = ssh.RunCommand("uci get system.@system[0].hostname 2>/dev/null || uname -n");
                var hostname = hostCmd.Result.Trim();
                if (string.IsNullOrEmpty(hostname)) hostname = "OpenWrt";

                var releaseCmd = ssh.RunCommand("cat /etc/openwrt_release 2>/dev/null | grep DISTRIB_RELEASE | cut -d\"'\" -f2");
                var release = releaseCmd.Result.Trim();

                ssh.Disconnect();

                return new RouterDiscoveryResult
                {
                    IpAddress = host,
                    Port = port,
                    Hostname = hostname,
                    Model = model,
                    FirmwareVersion = release,
                    SshAccessible = true,
                    AuthSucceeded = true,
                    LatencyMs = latency,
                    StatusMessage = $"Успешно обнаружен: {hostname} ({model})"
                };
            }
            catch (Renci.SshNet.Common.SshAuthenticationException)
            {
                return new RouterDiscoveryResult
                {
                    IpAddress = host,
                    Port = port,
                    SshAccessible = true,
                    AuthSucceeded = false,
                    LatencyMs = latency,
                    StatusMessage = $"Роутер найден на {host}, но пароль неверен"
                };
            }
            catch
            {
                return null;
            }
        });
    }
}
