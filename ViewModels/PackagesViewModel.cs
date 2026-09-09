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
        StatusMessage = "Выполняется 'opkg update', загрузка свежих индексов...";
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
        StatusMessage = string.IsNullOrWhiteSpace(SearchQuery)
            ? "Загрузка глобального интернет-каталога плагинов и репозиториев..."
            : $"Поиск '{SearchQuery}' в интернете и репозиториях OpenWrt/ForkOP...";

        try
        {
            var results = await _onlineSearchService.SearchOnlineAsync(SearchQuery);
            OnlineSearchResults = new ObservableCollection<OnlinePackageItem>(results);
            StatusMessage = $"Найдено пакетов: {OnlineSearchResults.Count}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка поиска: {ex.Message}";
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

        StatusMessage = $"Установка {pkg.Name} из {pkg.SourceRepo}... Пожалуйста, подождите.";
        var (success, output) = await _pkgService.InstallPackageAsync(pkg.Name, pkg.InstallCommand);

        if (success)
        {
            StatusMessage = $"Пакет {pkg.Name} успешно установлен!";
            await LoadCuratedAsync();
        }
        else
        {
            StatusMessage = $"Ошибка установки {pkg.Name}: {output}";
        }
    }

    [RelayCommand]
    public async Task InstallPackageAsync(PackageItem? pkg)
    {
        if (pkg == null || !_ssh.IsConnected) return;

        pkg.IsBusy = true;
        StatusMessage = $"Установка {pkg.Name}... Пожалуйста, подождите.";

        var (success, output) = await _pkgService.InstallPackageAsync(pkg.Name, pkg.InstallCommand);
        pkg.IsBusy = false;

        if (success)
        {
            pkg.IsInstalled = true;
            StatusMessage = $"Пакет {pkg.Name} успешно установлен!";
        }
        else
        {
            StatusMessage = $"Ошибка установки {pkg.Name}: {output}";
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
