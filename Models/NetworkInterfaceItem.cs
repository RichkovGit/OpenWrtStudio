namespace OpenWrtStudio.Models;

public class NetworkInterfaceItem
{
    public string Name { get; set; } = "lan";
    public string Device { get; set; } = "br-lan";
    public string IpAddress { get; set; } = "192.168.1.1";
    public string Netmask { get; set; } = "255.255.255.0";
    public string Gateway { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public bool IsUp { get; set; } = true;
    public long RxBytes { get; set; }
    public long TxBytes { get; set; }

    public string RxFormatted { get => FormatBytes(RxBytes); set { } }
    public string TxFormatted { get => FormatBytes(TxBytes); set { } }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 Б";
        string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
