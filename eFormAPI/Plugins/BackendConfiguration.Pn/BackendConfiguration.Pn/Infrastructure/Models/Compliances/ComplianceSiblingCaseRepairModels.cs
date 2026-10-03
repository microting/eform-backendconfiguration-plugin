using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Compliances;

/// <summary>
/// #1371 — what the sibling-completed-case repair would do (dry run). Ordered by
/// compliance id, so two dry runs over the same data produce the same <see cref="PlanHash"/>.
/// </summary>
public class ComplianceSiblingCaseRepairPlanModel
{
    /// <summary>
    /// SHA-256 over the repoints. The real run refuses to start unless it is handed the
    /// hash of the dry run that was reviewed, so what runs is exactly what was reviewed.
    /// </summary>
    public string PlanHash { get; set; } = string.Empty;

    public List<ComplianceSiblingCaseRepointModel> Repoints { get; set; } = [];
}

/// <summary>A removed compliance whose own case is not completed, repointed at the case a sibling worker completed.</summary>
public class ComplianceSiblingCaseRepointModel
{
    public int ComplianceId { get; set; }
    public int PlanningId { get; set; }
    public int PropertyId { get; set; }
    public DateTime Deadline { get; set; }
    public int OldSdkCaseId { get; set; }
    public int NewSdkCaseId { get; set; }
}

/// <summary>Outcome of the real run: the plan it executed plus what was actually written.</summary>
public class ComplianceSiblingCaseRepairRunResultModel
{
    public ComplianceSiblingCaseRepairPlanModel Plan { get; set; } = new();
    public int Repointed { get; set; }
    /// <summary>Planned rows that changed between the dry run and the write; left as they are.</summary>
    public List<string> Skipped { get; set; } = [];
    public List<string> Failures { get; set; } = [];
}
