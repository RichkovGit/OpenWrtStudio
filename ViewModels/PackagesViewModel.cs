using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenWrtStudio.Models;
using OpenWrtStudio.Services;

namespace OpenWrtStudio.ViewModels;

public partial class PackagesViewModel : ObservableObject
{
    private readonly IPackageManagerService _pkgService;
    private readonly IOnlinePackageSearchService _onlineSearchService;
    private readonly ISshService _ssh;

    [ObservableProperty]
    private ObservableCollection<PackageItem> _curatedPackages = new();

    [ObservableProperty]
    private ObservableCollection<PackageItem> _installedPackages = new();

    [ObservableProperty]
    private ObservableCollection<OnlinePackageItem> _onlineSearchResults = new();

    [ObservableProperty]
    private ObservableCollection<CustomFeed> _customFeeds = new();

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasStatusMessage;

    [ObservableProperty]
    private Wpf.Ui.Controls.InfoBarSeverity _statusSeverity = Wpf.Ui.Controls.InfoBarSeverity.Informational;

    private void SetStatus(string message, Wpf.Ui.Controls.InfoBarSeverity severity = Wpf.Ui.Controls.InfoBarSeverity.Informational)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        HasStatusMessage = !string.IsNullOrWhiteSpace(message);
    }

    // Feed creation
    [ObservableProperty]
    private string _newFeedName = "";

    [ObservableProperty]
    private string _newFeedUrl = "";

    public PackagesViewModel(IPackageManagerService pkgService, IOnlinePackageSearchService onlineSearchService, ISshService ssh)
    {
        _pkgService = pkgService;
        _onlineSearchService = onlineSearchService;
        _ssh = ssh;
    }

    public async Task InitializeAsync()
    {
        await LoadCuratedAsync();
        await LoadFeedsAsync();
        await SearchOnlineAsync();
    }

    [RelayCommand]
    public async Task LoadCuratedAsync()
    {
        StatusMessage = "Загрузка каталога тем и плагинов...";
        var list = await _pkgService.GetCuratedPackagesAsync();
        CuratedPackages = new ObservableCollection<PackageItem>(list);
        StatusMessage = $"В каталоге доступно {CuratedPackages.Count} популярных решений.";
    }

    [RelayCommand]
    public async Task LoadInstalledAsync()
    {
        if (!_ssh.IsConnected) return;
        StatusMessage = "Чтение списка установленных пакетов...";
        var list = await _pkgService.GetInstalledPackagesAsync();
        InstalledPackages = new ObservableCollection<PackageItem>(list);
        StatusMessage = $"Всего установлено пакетов: {InstalledPackages.Count}";
    }

    [RelayCommand]
    public async Task UpdateListsAsync()
    {
        if (!_ssh.IsConnected) return;

        IsUpdating = true;
        StatusMessage = "Выполняется обновление списков пакетов (apk / opkg)...";
        var (success, outStr) = await _pkgService.UpdateListsAsync();
        IsUpdating = false;

        if (success)
        {
            StatusMessage = "Списки пакетов успешно обновлены!";
            await LoadCuratedAsync();
        }
        else
        {
            StatusMessage = $"Ошибка обновления списков: {outStr}";
        }
    }

    [RelayCommand]
    public async Task SearchOnlineAsync()
    {
        IsSearching = true;
        SetStatus(string.IsNullOrWhiteSpace(SearchQuery)
            ? "Загрузка глобального интернет-каталога плагинов и репозиториев..."
            : $"Поиск '{SearchQuery}' в интернете и репозиториях OpenWrt/ForkOP...", Wpf.Ui.Controls.InfoBarSeverity.Informational);

        try
        {
            var results = await _onlineSearchService.SearchOnlineAsync(SearchQuery);
            OnlineSearchResults = new ObservableCollection<OnlinePackageItem>(results);
            SetStatus($"Найдено пакетов: {OnlineSearchResults.Count}", Wpf.Ui.Controls.InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка поиска: {ex.Message}", Wpf.Ui.Controls.InfoBarSeverity.Error);
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    public async Task InstallOnlinePackageAsync(OnlinePackageItem? pkg)
    {
        if (pkg == null || !_ssh.IsConnected) return;

        pkg.IsBusy = true;
        pkg.ButtonText = "Установка...";
        SetStatus($"Установка {pkg.Name} из {pkg.SourceRepo}... Пожалуйста, подождите.", Wpf.Ui.Controls.InfoBarSeverity.Informational);

        try
        {
            var (success, output) = await _pkgService.InstallPackageAsync(pkg.Name, pkg.InstallCommand);

            if (success)
            {
                pkg.IsInstalled = true;
                pkg.ButtonText = "Установлен";
                SetStatus($"Пакет {pkg.Name} успешно установлен и активирован в LuCI! Обновите страницу роутера в браузере.", Wpf.Ui.Controls.InfoBarSeverity.Success);
                await LoadCuratedAsync();
            }
            else
            {
                pkg.ButtonText = "Повторить";
                SetStatus($"Ошибка установки {pkg.Name}: {output}", Wpf.Ui.Controls.InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            pkg.ButtonText = "Повторить";
            SetStatus($"Исключение при установке {pkg.Name}: {ex.Message}", Wpf.Ui.Controls.InfoBarSeverity.Error);
        }
        finally
        {
            pkg.IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task InstallPackageAsync(PackageItem? pkg)
    {
        if (pkg == null || !_ssh.IsConnected) return;

        pkg.IsBusy = true;
        pkg.ButtonText = "Установка...";
        SetStatus($"Установка {pkg.Name}... Пожалуйста, подождите.", Wpf.Ui.Controls.InfoBarSeverity.Informational);

        try
        {
            var (success, output) = await _pkgService.InstallPackageAsync(pkg.Name, pkg.InstallCommand);

            if (success)
            {
                pkg.IsInstalled = true;
                pkg.ButtonText = "Установлен";
                SetStatus($"Пакет {pkg.Name} успешно установлен и активирован в LuCI! Обновите страницу роутера в браузере.", Wpf.Ui.Controls.InfoBarSeverity.Success);
            }
            else
            {
                pkg.ButtonText = "Повторить";
                SetStatus($"Ошибка установки {pkg.Name}: {output}", Wpf.Ui.Controls.InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            pkg.ButtonText = "Повторить";
            SetStatus($"Исключение при установке {pkg.Name}: {ex.Message}", Wpf.Ui.Controls.InfoBarSeverity.Error);
        }
        finally
        {
            pkg.IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RemovePackageAsync(PackageItem? pkg)
    {
        if (pkg == null || !_ssh.IsConnected) return;

        pkg.IsBusy = true;
        StatusMessage = $"Удаление {pkg.Name}...";

        var (success, output) = await _pkgService.RemovePackageAsync(pkg.Name);
        pkg.IsBusy = false;

        if (success)
        {
            pkg.IsInstalled = false;
            InstalledPackages.Remove(pkg);
            StatusMessage = $"Пакет {pkg.Name} удален.";
        }
        else
        {
            StatusMessage = $"Ошибка удаления {pkg.Name}: {output}";
        }
    }

    [RelayCommand]
    public async Task LoadFeedsAsync()
    {
        if (!_ssh.IsConnected) return;
        var feeds = await _pkgService.GetCustomFeedsAsync();
        CustomFeeds = new ObservableCollection<CustomFeed>(feeds);
    }

    [RelayCommand]
    public async Task AddFeedAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFeedName) || string.IsNullOrWhiteSpace(NewFeedUrl)) return;

        var feed = new CustomFeed { Name = NewFeedName.Trim(), Url = NewFeedUrl.Trim() };
        var (success, msg) = await _pkgService.AddCustomFeedAsync(feed);
        if (success)
        {
            CustomFeeds.Add(feed);
            NewFeedName = "";
            NewFeedUrl = "";
            StatusMessage = msg;
        }
        else
        {
            StatusMessage = msg;
        }
    }

    [RelayCommand]
    public async Task RemoveFeedAsync(CustomFeed? feed)
    {
        if (feed == null) return;
        var (success, msg) = await _pkgService.RemoveCustomFeedAsync(feed.Name);
        if (success)
        {
            CustomFeeds.Remove(feed);
            StatusMessage = msg;
        }
    }
}
