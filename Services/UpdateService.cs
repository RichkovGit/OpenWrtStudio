using System;
using System.Diagnostics;
using System.IO;
using System.Net;
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
        _httpClient.Timeout = TimeSpan.FromSeconds(20);

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersion = ver != null ? new Version(ver.Major, ver.Minor, ver.Build) : new Version(2, 5, 0);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"OpenWrtStudio-Updater/{CurrentVersion}");
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
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // If 404 (e.g. no releases created yet), return current version as latest
                return result;
            }
            response.EnsureSuccessStatusCode();

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

            if (root.TryGetProperty("published_at", out var pubElem) && pubElem.ValueKind == JsonValueKind.String && pubElem.TryGetDateTime(out var dt))
            {
                result.PublishedAt = dt;
            }

            // Parse Version
            var rawVer = result.TagName.TrimStart('v', 'V').Trim();
            if (Version.TryParse(rawVer, out var parsedVersion))
            {
                result.LatestVersion = new Version(parsedVersion.Major, parsedVersion.Minor, Math.Max(0, parsedVersion.Build));
            }
            else throw new InvalidDataException("GitHub вернул некорректный номер версии.");

            // Find best asset (Installer .exe preferred, or portable zip)
            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                string? bestUrl = null;
                string? bestName = null;
                long bestSize = 0;
                int bestRank = 0;

                foreach (var asset in assetsElem.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var assetName) || assetName.ValueKind != JsonValueKind.String ||
                        !asset.TryGetProperty("browser_download_url", out var assetUrl) || assetUrl.ValueKind != JsonValueKind.String)
                        continue;
                    var name = assetName.GetString() ?? "";
                    var url = assetUrl.GetString() ?? "";
                    if (!IsTrustedReleaseUrl(url, out _)) continue;
                    var size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var parsedSize) ? parsedSize : 0;
                    int rank = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) ? 3 : 2)
                        : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    if (rank > bestRank)
                    {
                        bestRank = rank;
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
            throw;
        }

        return result;
    }

    public async Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!IsTrustedReleaseUrl(downloadUrl, out var uri))
            throw new ArgumentException("Обновления можно загружать только из официальных релизов OpenWrtStudio на GitHub.", nameof(downloadUrl));
        var ext = Path.GetExtension(uri!.AbsolutePath).ToLowerInvariant();
        if (ext is not ".exe" and not ".zip")
            throw new ArgumentException("Неподдерживаемый формат обновления.", nameof(downloadUrl));
        var targetFile = Path.Combine(Path.GetTempPath(), $"OpenWrtStudio_Update_{Guid.NewGuid():N}{ext}");
        try
        {
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using (var fileStream = new FileStream(targetFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 8192, true))
            {
                var buffer = new byte[16384];
                long totalRead = 0;
                int read;
                while ((read = await contentStream.ReadAsync(buffer.AsMemory(), ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    totalRead += read;
                    if (totalBytes > 0)
                        progress?.Report(Math.Min(99, (double)totalRead / totalBytes.Value * 100));
                }
                if (totalRead == 0 || (totalBytes.HasValue && totalRead != totalBytes.Value))
                    throw new InvalidDataException("Обновление загружено не полностью. Повторите скачивание.");
                await fileStream.FlushAsync(ct);
                fileStream.Position = 0;
                var signature = new byte[2];
                var signatureLength = await fileStream.ReadAsync(signature.AsMemory(), ct);
                if (signatureLength != 2 || (ext == ".exe"
                    ? signature[0] != 'M' || signature[1] != 'Z'
                    : signature[0] != 'P' || signature[1] != 'K'))
                    throw new InvalidDataException("Сервер вернул некорректный файл обновления.");
            }
            ct.ThrowIfCancellationRequested();
            progress?.Report(100.0);
            return targetFile;
        }
        catch
        {
            if (File.Exists(targetFile)) File.Delete(targetFile);
            throw;
        }
    }

    private static bool IsTrustedReleaseUrl(string url, out Uri? uri) =>
        Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        uri.AbsolutePath.StartsWith($"/{RepoOwnerAndName}/releases/download/", StringComparison.OrdinalIgnoreCase);

    public void LaunchInstallerAndExit(string installerPath)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("Installer file not found", installerPath);

        if (Path.GetExtension(installerPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(installerPath)}\"") { UseShellExecute = true });
            return; // Portable archives require extraction; keep the current app open.
        }
        if (!Path.GetExtension(installerPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Неподдерживаемый формат программы установки.", nameof(installerPath));

        var psi = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        };

        Process.Start(psi);
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Application.Current.Shutdown();
        });
    }
}
