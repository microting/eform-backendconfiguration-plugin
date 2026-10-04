using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

/// <summary>
/// #1383 — what the orphaned off-pattern compliance repair would close (dry run) or
/// closed (real run). Lists are ordered by compliance id, so two dry runs over the same
/// data produce the same <see cref="PlanHash"/>.
/// </summary>
public class OrphanOffPatternComplianceRepairPlanModel
{
    /// <summary>
    /// SHA-256 over the plan date, the closures and the kept orphans. The real run
    /// refuses to start unless it is handed the hash of the dry run that was reviewed.
    /// </summary>
    public string PlanHash { get; set; } = string.Empty;

    /// <summary>The Danish (Europe/Copenhagen) date "in the past" was judged against.</summary>
    public DateTime Today { get; set; }

    /// <summary>Orphans the real run soft-deletes.</summary>
    public List<OrphanOffPatternComplianceClosureModel> Closures { get; set; } = [];

    /// <summary>Off-pattern orphans deliberately left alone, with why.</summary>
    public List<OrphanOffPatternComplianceKeptModel> Kept { get; set; } = [];
}

/// <summary>A live off-pattern compliance whose occurrence is dead, next to its period's on-pattern occurrence.</summary>
public class OrphanOffPatternComplianceClosureModel
{
    public int ComplianceId { get; set; }
    public int PlanningId { get; set; }
    public int PropertyId { get; set; }
    public int AreaRulePlanningId { get; set; }
    public int SdkCaseId { get; set; }
    public DateTime Deadline { get; set; }
    /// <summary>The rule's own occurrence in the same month — the tile that stays.</summary>
    public DateTime OnPatternDate { get; set; }
    /// <summary>"Missing", "Removed" or "Retracted" — the state of the compliance's SDK case.</summary>
    public string SdkCaseState { get; set; } = string.Empty;
}

/// <summary>An off-pattern orphan the repair leaves in place.</summary>
public class OrphanOffPatternComplianceKeptModel
{
    public int ComplianceId { get; set; }
    public int PlanningId { get; set; }
    public int SdkCaseId { get; set; }
    public DateTime Deadline { get; set; }
    public DateTime? OnPatternDate { get; set; }
    public List<string> Reasons { get; set; } = [];
}

/// <summary>Outcome of the real run: the plan it executed plus what was actually written.</summary>
public class OrphanOffPatternComplianceRepairRunResultModel
{
    public OrphanOffPatternComplianceRepairPlanModel Plan { get; set; } = new();
    public int ClosedCompliances { get; set; }
    /// <summary>Planned closures not written because the row no longer qualified right before the write.</summary>
    public List<string> Skipped { get; set; } = [];
    public List<string> Failures { get; set; } = [];
}
