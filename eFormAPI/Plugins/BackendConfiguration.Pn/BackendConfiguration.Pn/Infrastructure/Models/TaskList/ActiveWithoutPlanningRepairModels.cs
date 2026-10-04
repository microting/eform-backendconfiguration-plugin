using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.TaskList;

/// <summary>
/// #1376 — what the one-time cleanup of active task rows without a live planning
/// would do (dry run). Lists are ordered by AreaRulePlanning id, so two dry runs over
/// the same data give the same <see cref="PlanHash"/>.
/// </summary>
public class ActiveWithoutPlanningRepairPlanModel
{
    /// <summary>
    /// SHA-256 over every row in the plan (deletions with their cases to retract, and
    /// the skipped rows). The real run refuses unless it is handed this hash, so what
    /// runs is exactly what was reviewed.
    /// </summary>
    public string PlanHash { get; set; } = string.Empty;

    /// <summary>Rows the run soft-deletes, after retracting their open device cases.</summary>
    public List<ActiveWithoutPlanningRowModel> Deletions { get; set; } = [];

    /// <summary>Rows left alone, each with its reason (<c>LegacySibling</c>).</summary>
    public List<ActiveWithoutPlanningRowModel> Skipped { get; set; } = [];
}

public class ActiveWithoutPlanningRowModel
{
    public int AreaRulePlanningId { get; set; }
    public int AreaRuleId { get; set; }
    public int PropertyId { get; set; }
    public int AreaId { get; set; }
    public int? AreaType { get; set; }
    public int ItemPlanningId { get; set; }

    /// <summary><c>NoPlanning</c> (id 0), <c>PlanningRemoved</c> or <c>PlanningMissing</c>.</summary>
    public string PlanningState { get; set; } = string.Empty;

    /// <summary>Why a row is skipped; null on a deletion.</summary>
    public string SkipReason { get; set; }

    /// <summary>Open (not completed, not removed) SDK cases the run retracts before the delete.</summary>
    public List<ActiveWithoutPlanningCaseModel> CasesToRetract { get; set; } = [];
}

/// <summary>
/// One device deployment to retract: an SDK case, or — for a deployment that exists only
/// as a CheckListSite (no case with a MicrotingUid) — that CheckListSite.
/// </summary>
public class ActiveWithoutPlanningCaseModel
{
    /// <summary>The SDK case id; 0 for a CheckListSite-only deployment.</summary>
    public int SdkCaseId { get; set; }

    /// <summary>The SDK CheckListSite id of a CheckListSite-only deployment; null for a case.</summary>
    public int? CheckListSiteId { get; set; }

    public int MicrotingUid { get; set; }
}

public class ActiveWithoutPlanningRepairRunResultModel
{
    public ActiveWithoutPlanningRepairPlanModel Plan { get; set; } = new();
    public List<int> DeletedAreaRulePlanningIds { get; set; } = [];
    public int RetractedCases { get; set; }

    /// <summary>Rows not written because they changed after the plan was computed.</summary>
    public List<string> SkippedAsChanged { get; set; } = [];

    public List<string> Failures { get; set; } = [];
}
