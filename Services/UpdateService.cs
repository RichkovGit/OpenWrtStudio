using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface IUpdateService
{
    Version CurrentVersion { get; }
    Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken ct = default);
    Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<double>? progress = null, CancellationToken ct = default);
    void LaunchInstallerAndExit(string installerPath);
}

public class UpdateService : IUpdateService
{
    private const string RepoOwnerAndName = "RichkovGit/OpenWrtStudio";
    private const string ReleasesApiUrl = $"https://api.github.com/repos/{RepoOwnerAndName}/releases/latest";

    private readonly HttpClient _httpClient;

    public Version CurrentVersion { get; }

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenWrtStudio-Updater/2.5.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(20);

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersion = ver != null ? new Version(ver.Major, ver.Minor, ver.Build) : new Version(2, 5, 0);
    }

    public UpdateService(HttpClient httpClient, Version currentVersion)
    {
        _httpClient = httpClient;
        CurrentVersion = currentVersion;
    }

    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var result = new UpdateInfo
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = CurrentVersion,
            TagName = $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}"
        };

        try
        {
            using var response = await _httpClient.GetAsync(ReleasesApiUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                // If 404 (e.g. no releases created yet), return current version as latest
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("tag_name", out var tagElem))
            {
                result.TagName = tagElem.GetString() ?? "";
            }

            if (root.TryGetProperty("name", out var nameElem))
            {
                result.Title = nameElem.GetString() ?? result.TagName;
            }

            if (root.TryGetProperty("body", out var bodyElem))
            {
                result.Changelog = bodyElem.GetString() ?? "";
            }

            if (root.TryGetProperty("published_at", out var pubElem) && pubElem.TryGetDateTime(out var dt))
            {
                result.PublishedAt = dt;
            }

            // Parse Version
            var rawVer = result.TagName.TrimStart('v', 'V').Trim();
            if (Version.TryParse(rawVer, out var parsedVersion))
            {
                result.LatestVersion = parsedVersion;
            }
            else if (rawVer.Count(c => c == '.') == 1 && Version.TryParse(rawVer + ".0", out var twoPartVer))
            {
                result.LatestVersion = twoPartVer;
            }

            // Find best asset (Installer .exe preferred, or portable zip)
            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                string? bestUrl = null;
                string? bestName = null;
                long bestSize = 0;

                foreach (var asset in assetsElem.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    var size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;

                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) || bestUrl == null)
                        {
                            bestUrl = url;
                            bestName = name;
                            bestSize = size;
                            if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase)) break;
                        }
                    }
                    else if (bestUrl == null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        bestUrl = url;
                        bestName = name;
                        bestSize = size;
                    }
                }

                if (bestUrl != null)
                {
                    result.DownloadUrl = bestUrl;
                    result.AssetName = bestName ?? "UpdatePackage";
                    result.FileSizeBytes = bestSize;
                }
            }

            result.IsUpdateAvailable = result.LatestVersion > CurrentVersion;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] CheckForUpdates error: {ex.Message}");
        }

        return result;
    }

    public async Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new ArgumentException("Download URL cannot be empty", nameof(downloadUrl));

        var tempDir = Path.GetTempPath();
        var ext = Path.GetExtension(downloadUrl);
        if (string.IsNullOrEmpty(ext)) ext = ".exe";
        var targetFile = Path.Combine(tempDir, $"OpenWrtStudio_Update_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");

        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[16384];
        long totalRead = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, read, ct);
            totalRead += read;

            if (totalBytes > 0 && progress != null)
            {
                var percentage = (double)totalRead / totalBytes * 100.0;
                progress.Report(percentage);
            }
        }

        progress?.Report(100.0);
        return targetFile;
    }

    public void LaunchInstallerAndExit(string installerPath)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("Installer file not found", installerPath);

        var psi = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        };

        Process.Start(psi);
        Application.Current.Dispatcher.Invoke(() =>
        {
            Application.Current.Shutdown();
        });
    }
}
