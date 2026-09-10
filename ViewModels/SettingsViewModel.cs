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
    private readonly IUpdateService _updateService;
    private readonly INotificationService _notifications;
    private readonly IThemeService _themeService;

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

    [ObservableProperty]
    private ObservableCollection<ColorThemeItem> _colorThemes = new();

    [ObservableProperty]
    private ColorThemeItem? _selectedColorTheme;

    [ObservableProperty]
    private string _themeStatusText = "";

    // OTA Updates
    [ObservableProperty]
    private UpdateInfo? _updateInfo;

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _updateStatusText = "Нажмите «Проверить обновления» для связи с GitHub.";

    [ObservableProperty]
    private bool _autoCheckUpdates = true;

    public string CurrentAppVersionString => $"v{_updateService.CurrentVersion.Major}.{_updateService.CurrentVersion.Minor}.{_updateService.CurrentVersion.Build}";

    public SettingsViewModel(
        IProfileService profileService,
        ISshService ssh,
        IUpdateService updateService,
        INotificationService notifications,
        IThemeService themeService)
    {
        _profileService = profileService;
        _ssh = ssh;
        _updateService = updateService;
        _notifications = notifications;
        _themeService = themeService;
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

        // Initialize theme options
        ColorThemes = new ObservableCollection<ColorThemeItem>(_themeService.AvailableThemes);
        SelectedColorTheme = _themeService.CurrentTheme;
        IsDarkMode = _themeService.IsDarkMode;
        foreach (var t in ColorThemes)
        {
            t.IsSelected = t.Id == SelectedColorTheme?.Id;
        }

        if (AutoCheckUpdates)
        {
            _ = CheckForUpdatesAsync();
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
    public void SelectColorTheme(ColorThemeItem? theme)
    {
        if (theme == null) return;
        SelectedColorTheme = theme;
        foreach (var t in ColorThemes)
        {
            t.IsSelected = t.Id == theme.Id;
        }
        _themeService.ApplyTheme(theme.Id, IsDarkMode);
        ThemeStatusText = $"Применена цветовая схема: «{theme.Name}»";
    }

    [RelayCommand]
    public void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        _themeService.ApplyTheme(SelectedColorTheme?.Id ?? "cyan", IsDarkMode);
        ThemeStatusText = IsDarkMode ? "Активирована тёмная тема" : "Активирована светлая тема";
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatusText = "Проверка обновлений на GitHub (RichkovGit/OpenWrtStudio)...";
        try
        {
            var info = await _updateService.CheckForUpdatesAsync();
            UpdateInfo = info;
            if (info.IsUpdateAvailable)
            {
                UpdateStatusText = $"🎉 Найдена новая версия {info.TagName}! ({info.FormattedSize})";
                _notifications.ShowNotification("Доступно обновление!", $"Вышла новая версия OpenWrt Studio {info.TagName}. Нажмите для перехода.", NotificationSeverity.Info, "ota_available");
            }
            else
            {
                UpdateStatusText = $"У вас установлена актуальная версия ({CurrentAppVersionString}).";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Ошибка проверки: {ex.Message}";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    public async Task DownloadAndApplyUpdateAsync()
    {
        if (UpdateInfo == null || string.IsNullOrWhiteSpace(UpdateInfo.DownloadUrl))
        {
            await CheckForUpdatesAsync();
            if (UpdateInfo == null || !UpdateInfo.IsUpdateAvailable) return;
        }

        IsDownloadingUpdate = true;
        DownloadProgress = 0;
        UpdateStatusText = $"Скачивание обновления {UpdateInfo.TagName}...";

        try
        {
            var progress = new Progress<double>(p => DownloadProgress = Math.Round(p, 1));
            var downloadedFile = await _updateService.DownloadUpdateAsync(UpdateInfo.DownloadUrl, progress);
            UpdateStatusText = "Запуск программы установки...";
            await Task.Delay(800);
            _updateService.LaunchInstallerAndExit(downloadedFile);
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Ошибка скачивания: {ex.Message}";
            IsDownloadingUpdate = false;
        }
    }

    [RelayCommand]
    public void OpenGitHubRepo()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/RichkovGit/OpenWrtStudio") { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    public void OpenAuthorGitHub()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/RichkovGit") { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    public void OpenBugReport()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/RichkovGit/OpenWrtStudio/issues") { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    public void OpenAuthorTelegram()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://t.me/RichkovChannel") { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    public void OpenAppTelegram()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://t.me/OpenWrtStudio") { UseShellExecute = true });
        }
        catch { }
    }
}
