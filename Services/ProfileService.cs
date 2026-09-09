using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public event EventHandler<List<ConnectionProfile>>? ProfilesSaved;

    public ProfileService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "OpenWrtStudio");
        Directory.CreateDirectory(dir);
        _storagePath = Path.Combine(dir, "profiles.dat");
    }

    public async Task<List<ConnectionProfile>> LoadProfilesAsync()
    {
        if (!File.Exists(_storagePath))
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

        try
        {
            var bytes = await File.ReadAllBytesAsync(_storagePath);
            byte[] decrypted;
            try
            {
                decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            }
            catch
            {
                decrypted = bytes;
            }

            var json = Encoding.UTF8.GetString(decrypted);
            var list = JsonSerializer.Deserialize<List<ConnectionProfile>>(json);
            return list ?? new List<ConnectionProfile>();
        }
        catch
        {
            return new List<ConnectionProfile>();
        }
    }

    public async Task SaveProfilesAsync(List<ConnectionProfile> profiles)
    {
        try
        {
            var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
            var bytes = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(_storagePath, encrypted);
            ProfilesSaved?.Invoke(this, profiles);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save profiles: {ex.Message}");
        }
    }

    public async Task<ConnectionProfile?> GetDefaultProfileAsync()
    {
        var profiles = await LoadProfilesAsync();
        return profiles.Count > 0 ? profiles[0] : null;
    }
}
