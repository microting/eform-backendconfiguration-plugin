using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// Request contract for <c>POST api/backend-configuration-pn/compliance-report/overview</c>
/// — the Oversigt view's per-property aggregation (#1162).
///
/// <para>
/// This is <see cref="ComplianceReportRequestModel"/>'s filter set MINUS four
/// properties, and every omission is deliberate:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>No <c>Status</c>.</b> Oversigt counts done and not-done rows together —
///     that is exactly why the UI disables the status control and shows
///     "Oversigt viser både udførte og ikke udførte opgaver". Omitting the property
///     is stronger than accepting and ignoring it: a caller cannot come to believe
///     the filter works. Internally the candidate set is built with the
///     <c>Status = "all"</c> behaviour, which still honours the
///     "soft-removed and not done ⇒ never returned" rule.
///   </item>
///   <item>
///     <b>No <c>PageIndex</c>/<c>PageSize</c>.</b> One row per property; the
///     response is unpaged by decision, not by omission (#1162 §4).
///   </item>
///   <item>
///     <b>No <c>Sort</c>/<c>IsSortDsc</c>.</b> Sorting is client-side in #1164 —
///     a handful of rows, no round-trip. The server returns a documented stable
///     order (PropertyName ascending, ordinal) so the response is diffable in CI.
///   </item>
/// </list>
/// </summary>
public class ComplianceReportOverviewRequestModel
{
    public int? PropertyId { get; set; }

    public List<int> BoardIds { get; set; } = [];

    /// <summary>Items-planning PlanningTag ids (same ids the sidebar tag filter uses).</summary>
    public List<int> TagIds { get; set; } = [];

    /// <summary>Employee filter — SDK Site ids, via AreaRulePlanning.PlanningSites.</summary>
    public List<int> SiteIds { get; set; } = [];

    public DateTime DateFrom { get; set; }

    public DateTime DateTo { get; set; }
}
