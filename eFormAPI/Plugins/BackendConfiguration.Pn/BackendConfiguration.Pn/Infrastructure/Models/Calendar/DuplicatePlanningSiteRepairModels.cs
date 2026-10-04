using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

/// <summary>
/// #1385 — what the duplicate PlanningSites repair would do (dry run). Groups and ids are
/// ordered, so two dry runs over the same data produce the same <see cref="PlanHash"/>.
/// </summary>
public class DuplicatePlanningSiteRepairPlanModel
{
    /// <summary>
    /// SHA-256 over every group below. The real run refuses unless it is handed the hash
    /// of the dry run that was reviewed, so what runs is exactly what was reviewed.
    /// </summary>
    public string PlanHash { get; set; } = string.Empty;

    /// <summary>How many rows the real run would soft-delete.</summary>
    public int RowsToRemove { get; set; }

    public List<DuplicatePlanningSiteGroupModel> Groups { get; set; } = [];
}

/// <summary>
/// One (planning, site) pair with more than one live items-planning PlanningSites row.
/// The lowest id is kept; the others are soft-deleted.
/// </summary>
public class DuplicatePlanningSiteGroupModel
{
    public int PlanningId { get; set; }
    public int SiteId { get; set; }
    public int KeptPlanningSiteId { get; set; }
    public List<int> RemovedPlanningSiteIds { get; set; } = [];
}

/// <summary>What the real run did.</summary>
public class DuplicatePlanningSiteRepairRunResultModel
{
    public int Removed { get; set; }

    /// <summary>Rows left alone because they changed after the plan was computed.</summary>
    public List<string> Skipped { get; set; } = [];

    public List<string> Failures { get; set; } = [];
}
