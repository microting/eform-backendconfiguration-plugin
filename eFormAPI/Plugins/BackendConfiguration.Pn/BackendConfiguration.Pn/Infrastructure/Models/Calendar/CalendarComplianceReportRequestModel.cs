using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarComplianceReportRequestModel
{
    public int? PropertyId { get; set; }
    public List<int> BoardIds { get; set; } = [];
    /// <summary>Items-planning PlanningTag ids (same ids the sidebar tag filter uses).</summary>
    public List<int> TagIds { get; set; } = [];
    /// <summary>
    /// Employee filter — SDK Site ids. Matches an AreaRulePlanning either through its
    /// explicit PlanningSites rows OR through an AreaRulePlanningWorkerTag whose worker
    /// tag ("team") one of these sites is a live member of (#1232) — a team-assigned
    /// event has no PlanningSites row for its members at all.
    /// </summary>
    public List<int> SiteIds { get; set; } = [];
    /// <summary>"open" | "done" | "all"</summary>
    public string Status { get; set; } = "open";
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
}
