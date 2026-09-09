using System.Collections.Generic;

namespace OpenWrtStudio.Models;

public class PackageItem
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Общие";
    public bool IsInstalled { get; set; }
    public string? InstalledVersion { get; set; }
    public string Size { get; set; } = "";
    public bool IsCurated { get; set; }
    public string Icon { get; set; } = "Apps24";
    public string? InstallCommand { get; set; }
    public string? RepoUrl { get; set; }
    public bool IsBusy { get; set; }
}

public class CustomFeed
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public string Description { get; set; } = "";
}
