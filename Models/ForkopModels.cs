using System.Collections.Generic;

namespace OpenWrtStudio.Models;

public class ForkopFeedInfo
{
    public bool IsConfigured { get; set; }
    public string Architecture { get; set; } = "unknown";
    public string FeedUrl { get; set; } = "";
    public string FeedKeyStatus { get; set; } = "Не установлен";
    public string Branch { get; set; } = "master";
}

public class ForkopComponentItem
{
    public string Name { get; set; } = "";
    public string PackageName { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsInstalled { get; set; }
    public bool IsRunning { get; set; }
    public string Version { get; set; } = "";
    public string Category { get; set; } = "Core";
}

public class ForkopDiagResult
{
    public bool HasTransparentProxyRules { get; set; }
    public string FirewallType { get; set; } = "nftables (fw4)";
    public bool DnsHijackActive { get; set; }
    public bool DirectRussiaOk { get; set; }
    public bool ProxyBypassOk { get; set; }
    public string Details { get; set; } = "";
    public List<string> RuleChainsFound { get; set; } = new();
}
