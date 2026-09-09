using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;
using Wpf.Ui.Appearance;

namespace OpenWrtStudio.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IProfileService _profileService;
    private readonly ISshService _ssh;

    [ObservableProperty]
    private ObservableCollection<ConnectionProfile> _profiles = new();

    [ObservableProperty]
    private ConnectionProfile? _currentEditingProfile;

    [ObservableProperty]
    private string _testStatus = "";

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private bool _isDarkMode = true;

    public SettingsViewModel(IProfileService profileService, ISshService ssh)
    {
        _profileService = profileService;
        _ssh = ssh;
    }

    public async Task InitializeAsync()
    {
        var loaded = await _profileService.LoadProfilesAsync();
        Profiles = new ObservableCollection<ConnectionProfile>(loaded);
        if (Profiles.Count > 0)
        {
            CurrentEditingProfile = Profiles[0];
        }
        else
        {
            AddNewProfile();
        }
    }

    [RelayCommand]
    public void AddNewProfile()
    {
        var p = new ConnectionProfile
        {
            Name = "Новый роутер",
            Host = "192.168.1.1",
            Port = 22,
            Username = "root"
        };
        Profiles.Add(p);
        CurrentEditingProfile = p;
    }

    [RelayCommand]
    public async Task DeleteProfileAsync(ConnectionProfile? profile)
    {
        if (profile == null) return;
        Profiles.Remove(profile);
        if (CurrentEditingProfile == profile)
        {
            CurrentEditingProfile = Profiles.Count > 0 ? Profiles[0] : null;
        }
        await _profileService.SaveProfilesAsync(new(Profiles));
    }

    [RelayCommand]
    public async Task SaveProfilesAsync()
    {
        await _profileService.SaveProfilesAsync(new(Profiles));
        TestStatus = "Профили успешно сохранены!";
    }

    [RelayCommand]
    public void SelectPrivateKey()
    {
        if (CurrentEditingProfile == null) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Private Key (*.pem;*.id_*;*)|*.pem;*.id_*;*|All files (*.*)|*.*",
            Title = "Выберите закрытый SSH ключ"
        };
        if (dlg.ShowDialog() == true)
        {
            CurrentEditingProfile.PrivateKeyPath = dlg.FileName;
            CurrentEditingProfile.UseKeyAuth = true;
            OnPropertyChanged(nameof(CurrentEditingProfile));
        }
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        if (CurrentEditingProfile == null) return;

        IsTesting = true;
        TestStatus = "Проверка соединения...";

        var (success, msg) = await _ssh.TestConnectionAsync(CurrentEditingProfile);
        IsTesting = false;
        TestStatus = msg;
    }

    [RelayCommand]
    public void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        ApplicationThemeManager.Apply(IsDarkMode ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }
}
