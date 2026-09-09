using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenWrtStudio.Models;

public partial class ForkopSection : ObservableObject
{
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private string _name = "VPN Прокси";
    [ObservableProperty] private string _action = "Connection";
    [ObservableProperty] private string _selectedInterface = "br-lan";
    
    [ObservableProperty] private ObservableCollection<string> _connectionUrls = new();
    [ObservableProperty] private ObservableCollection<string> _subscriptionUrls = new();
    [ObservableProperty] private ObservableCollection<string> _rulesets = new();
}

public partial class ForkopSubscriptionInfo : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _trafficUsed = "0 B";
    [ObservableProperty] private string _trafficTotal = "∞";
    [ObservableProperty] private string _expireDate = "";
    [ObservableProperty] private int _serverCount;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private double _trafficPercentage;
}

public partial class ForkopServerNode : ObservableObject
{
    [ObservableProperty] private string _id = Guid.NewGuid().ToString();
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _protocol = "VLESS";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _provider = "";
    [ObservableProperty] private string _serverAddress = "";
    [ObservableProperty] private int _serverPort;
    [ObservableProperty] private string _groupTag = "";
    [ObservableProperty] private int? _latencyMs;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isAutoGroup;

    public string LatencyDisplay => LatencyMs.HasValue ? $"{LatencyMs.Value}ms" : "N/A";
}

public partial class ForkopDashboardStats : ObservableObject
{
    [ObservableProperty] private string _inSpeed = "1.42 KB/s";
    [ObservableProperty] private string _outSpeed = "0 B/s";
    [ObservableProperty] private string _totalIn = "1.56 MB";
    [ObservableProperty] private string _totalOut = "395 KB";
    [ObservableProperty] private int _activeConnections = 35;
    [ObservableProperty] private string _memoryUsage = "23.7 MB";
    [ObservableProperty] private bool _isForkopRunning = true;
    [ObservableProperty] private bool _isSingboxRunning = true;
}

public partial class ForkopFeedInfo : ObservableObject
{
    [ObservableProperty] private string _architecture = "unknown";
    [ObservableProperty] private bool _isConfigured;
    [ObservableProperty] private string _feedUrl = "";
    [ObservableProperty] private string _feedKeyStatus = "Не проверен";
    [ObservableProperty] private string _branch = "master";
}

public partial class ForkopComponentItem : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _packageName = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _category = "Core";
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private bool _isRunning;
}

public partial class ForkopDiagResult : ObservableObject
{
    [ObservableProperty] private bool _hasTransparentProxyRules;
    [ObservableProperty] private string _firewallType = "nftables";
    [ObservableProperty] private bool _dnsHijackActive;
    [ObservableProperty] private System.Collections.Generic.List<string> _ruleChainsFound = new();
    [ObservableProperty] private bool _directRussiaOk;
    [ObservableProperty] private bool _proxyBypassOk;
    [ObservableProperty] private string _details = "";
}