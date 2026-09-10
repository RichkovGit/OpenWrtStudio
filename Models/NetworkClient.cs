using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenWrtStudio.Models;

public partial class NetworkClient : ObservableObject
{
    [ObservableProperty] private string _macAddress = string.Empty;
    [ObservableProperty] private string _ipAddress = string.Empty;
    [ObservableProperty] private string _hostname = "Неизвестное устройство";
    [ObservableProperty] private string _customName = string.Empty;
    [ObservableProperty] private string _interfaceName = string.Empty;
    [ObservableProperty] private string _band = "LAN"; // "2.4 GHz", "5 GHz", "LAN"
    [ObservableProperty] private int _signalDbm = 0;
    [ObservableProperty] private string _rxBitrate = string.Empty;
    [ObservableProperty] private string _txBitrate = string.Empty;
    [ObservableProperty] private string _connectedDuration = string.Empty;
    [ObservableProperty] private bool _isOnline = true;
    [ObservableProperty] private bool _isBlocked = false;
    [ObservableProperty] private bool _isStaticLease = false;
    [ObservableProperty] private string _vendor = string.Empty;

    public string DisplayName => !string.IsNullOrWhiteSpace(CustomName) 
        ? CustomName 
        : (!string.IsNullOrWhiteSpace(Hostname) && Hostname != "*" ? Hostname : MacAddress);

    public string SignalQuality
    {
        get
        {
            if (Band == "LAN") return "Проводное соединение (1 Gbps)";
            if (SignalDbm >= -55) return $"Отличный ({SignalDbm} dBm)";
            if (SignalDbm >= -68) return $"Хороший ({SignalDbm} dBm)";
            if (SignalDbm >= -78) return $"Средний ({SignalDbm} dBm)";
            if (SignalDbm < 0) return $"Слабый ({SignalDbm} dBm)";
            return "N/A";
        }
    }

    public string BandBadgeColor => Band switch
    {
        "5 GHz" => "#00D2FF",
        "2.4 GHz" => "#10B981",
        _ => "#8B5CF6"
    };

    public string DeviceIcon => Band switch
    {
        "5 GHz" => "WifiSettings20",
        "2.4 GHz" => "Wifi220",
        _ => "Ethernet20"
    };
}
