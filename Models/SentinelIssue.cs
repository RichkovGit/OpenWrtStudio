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

/// <summary>Result of one recovery method executed by the Fix-All orchestrator.</summary>
public class SentinelFixStepResult
{
    public string MethodId { get; set; } = "";
    public string MethodName { get; set; } = "";
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>Progress notification raised before and after each Fix-All step.</summary>
public class SentinelFixProgress
{
    public int StepIndex { get; set; }
    public int TotalSteps { get; set; }
    public string MethodName { get; set; } = "";
    public string Status { get; set; } = "";
}

/// <summary>Aggregated outcome of the one-click Fix-All run.</summary>
public class SentinelFixAllReport
{
    public List<SentinelFixStepResult> Steps { get; set; } = new();
    public bool Resolved { get; set; }
    public bool Cancelled { get; set; }
    public bool RebootIssued { get; set; }
    public string Summary { get; set; } = "";
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
