using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenWrtStudio.Models;

namespace OpenWrtStudio.Services;

public interface IProfileService
{
    event EventHandler<List<ConnectionProfile>>? ProfilesSaved;
    Task<List<ConnectionProfile>> LoadProfilesAsync();
    Task SaveProfilesAsync(List<ConnectionProfile> profiles);
    Task<ConnectionProfile?> GetDefaultProfileAsync();
}

public class ProfileService : IProfileService
{
    private readonly string _storagePath;
    private readonly SemaphoreSlim _storageLock = new(1, 1);
    public event EventHandler<List<ConnectionProfile>>? ProfilesSaved;

    public ProfileService() : this(Path.Combine(GetStorageDirectory(), "profiles.dat"))
    {
    }

    public ProfileService(string storagePath) => _storagePath = Path.GetFullPath(storagePath);

    private static string GetStorageDirectory()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("OPENWRT_STUDIO_DATA_DIR");
        return !string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.GetFullPath(overrideDirectory)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenWrtStudio");
    }

    public async Task<List<ConnectionProfile>> LoadProfilesAsync()
    {
        await _storageLock.WaitAsync();
        try
        {
            if (!File.Exists(_storagePath) && !File.Exists(_storagePath + ".bak"))
            {
                return new List<ConnectionProfile>
                {
                    new()
                    {
                        Name = "Мой OpenWrt (192.168.1.1)",
                        Host = "192.168.1.1",
                        Port = 22,
                        Username = "root"
                    }
                };
            }

            foreach (var path in new[] { _storagePath, _storagePath + ".bak" })
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    byte[] decrypted;
                    try
                    {
                        decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                    }
                    catch (CryptographicException)
                    {
                        decrypted = bytes; // Compatibility with legacy JSON profile storage.
                    }

                    var json = Encoding.UTF8.GetString(decrypted);
                    var list = JsonSerializer.Deserialize<List<ConnectionProfile>>(json);
                    return list ?? new List<ConnectionProfile>();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CryptographicException)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load profiles: {ex.Message}");
                }
            }
            return new List<ConnectionProfile>();
        }
        finally { _storageLock.Release(); }
    }

    public async Task SaveProfilesAsync(List<ConnectionProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        await _storageLock.WaitAsync();
        var temporaryPath = _storagePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
            var bytes = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(encrypted);
                await stream.FlushAsync();
            }
            if (File.Exists(_storagePath))
                File.Replace(temporaryPath, _storagePath, _storagePath + ".bak", true);
            else
                File.Move(temporaryPath, _storagePath);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            finally { _storageLock.Release(); }
        }
        // Propagate persistence errors so callers cannot display false success.
        ProfilesSaved?.Invoke(this, profiles);
    }

    public async Task<ConnectionProfile?> GetDefaultProfileAsync()
    {
        var profiles = await LoadProfilesAsync();
        return profiles.Count > 0 ? profiles[0] : null;
    }
}
