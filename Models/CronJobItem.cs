namespace OpenWrtStudio.Models;

public class CronJobItem
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public string Schedule { get; set; } = "0 4 * * *";
    public string Command { get; set; } = "reboot";
    public string Description { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public string PresetType { get; set; } = "custom"; // reboot, wifi_off, wifi_on, sub_update, drop_cache, ping_watchdog, custom

    public string ToCrontabLine()
    {
        var prefix = IsEnabled ? "" : "# ";
        var comment = string.IsNullOrWhiteSpace(Description) ? "" : $" # {Description}";
        return $"{prefix}{Schedule} {Command}{comment}";
    }

    public static CronJobItem FromCrontabLine(string line)
    {
        var item = new CronJobItem();
        var trimmed = line.Trim();
        if (trimmed.StartsWith("#"))
        {
            item.IsEnabled = false;
            trimmed = trimmed.TrimStart('#', ' ').Trim();
        }

        // Check for comment at end
        int commentIdx = trimmed.IndexOf('#');
        if (commentIdx > 0)
        {
            item.Description = trimmed.Substring(commentIdx + 1).Trim();
            trimmed = trimmed.Substring(0, commentIdx).Trim();
        }

        // Split into 5 time parts + command
        var parts = trimmed.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 6)
        {
            item.Schedule = $"{parts[0]} {parts[1]} {parts[2]} {parts[3]} {parts[4]}";
            item.Command = string.Join(" ", parts, 5, parts.Length - 5);
        }
        else
        {
            item.Command = trimmed;
        }

        return item;
    }
}
