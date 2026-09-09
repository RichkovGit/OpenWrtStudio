using System;

namespace OpenWrtStudio.Models;

public class UpdateInfo
{
    public Version CurrentVersion { get; set; } = new(2, 5, 0);
    public Version LatestVersion { get; set; } = new(2, 5, 0);
    public string TagName { get; set; } = "v2.5.0";
    public string Title { get; set; } = "";
    public string Changelog { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string AssetName { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public DateTime? PublishedAt { get; set; }

    public string FormattedSize
    {
        get
        {
            if (FileSizeBytes <= 0) return "";
            return $"{(FileSizeBytes / (1024.0 * 1024.0)):F1} МБ";
        }
    }
}
