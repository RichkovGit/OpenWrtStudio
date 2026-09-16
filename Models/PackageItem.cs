using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenWrtStudio.Models;

public partial class PackageItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _version = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _category = "Общие";

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private string? _installedVersion;

    [ObservableProperty]
    private string _size = "";

    [ObservableProperty]
    private bool _isCurated;

    [ObservableProperty]
    private string _icon = "Apps24";

    [ObservableProperty]
    private string? _installCommand;

    [ObservableProperty]
    private string? _repoUrl;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _buttonText = "Установить в 1 клик";
}

public class CustomFeed
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public string Description { get; set; } = "";
}
