using System;
using System.Collections.Generic;

namespace OpenWrtStudio.Models;

public enum IssueSeverity
{
    Info,
    Warning,
    Critical
}

public class RecoveryMethod
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Command { get; set; } = "";
    public string Icon { get; set; } = "Wrench24";
    public bool IsRecommended { get; set; }
}

public class SentinelIssue
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string DiagnosticDetails { get; set; } = "";
    public IssueSeverity Severity { get; set; } = IssueSeverity.Warning;
    public bool IsProviderIssue { get; set; }
    public string ProviderStatusText { get; set; } = "";
    public DateTime DetectedAt { get; set; } = DateTime.Now;
    public List<RecoveryMethod> RecoveryMethods { get; set; } = new();
}
