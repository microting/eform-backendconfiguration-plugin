using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;

/// <summary>
/// #1376 — what the removed-site assignment cleanup would do (dry run). Rows are ordered
/// by PlanningSite id, so two dry runs over the same data give the same <see cref="PlanHash"/>.
/// </summary>
public class RemovedSiteAssignmentCleanupPlanModel
{
    /// <summary>
    /// SHA-256 over the rows the real run would soft-delete. The run refuses unless it is
    /// handed the hash of the dry run that was reviewed.
    /// </summary>
    public string PlanHash { get; set; } = string.Empty;

    public List<RemovedSiteAssignmentModel> Assignments { get; set; } = [];
}

/// <summary>One live BC PlanningSite of an inactive legacy rule whose SDK site is gone.</summary>
public class RemovedSiteAssignmentModel
{
    public int PlanningSiteId { get; set; }
    public int AreaRulePlanningId { get; set; }
    public int PropertyId { get; set; }
    public int? AreaId { get; set; }
    public int SiteId { get; set; }

    /// <summary>"removed" (the SDK site is soft-deleted) or "missing" (no SDK site row).</summary>
    public string SiteState { get; set; } = string.Empty;

    /// <summary>The removed SDK site's UpdatedAt (when it was removed); null when missing.</summary>
    public DateTime? SiteRemovedAt { get; set; }
}

public class RemovedSiteAssignmentCleanupRunResultModel
{
    public RemovedSiteAssignmentCleanupPlanModel Plan { get; set; } = new();
    public int RemovedAssignments { get; set; }

    /// <summary>Planned rows left alone because they no longer qualified when re-read, with why.</summary>
    public List<string> Skipped { get; set; } = [];

    public List<string> Failures { get; set; } = [];
}
