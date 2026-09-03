using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// Request contract for <c>POST api/backend-configuration-pn/compliance-report/index</c>.
///
/// The filter half is moved verbatim from
/// <see cref="Calendar.CalendarComplianceReportRequestModel"/> — the multi-select
/// contract was already a list per filter and is unchanged. What is new here is
/// paging and sorting.
/// </summary>
public class ComplianceReportRequestModel
{
    // --- unchanged, moved verbatim from CalendarComplianceReportRequestModel ---
    public int? PropertyId { get; set; }
    public List<int> BoardIds { get; set; } = [];
    /// <summary>Items-planning PlanningTag ids (same ids the sidebar tag filter uses).</summary>
    public List<int> TagIds { get; set; } = [];
    public List<int> SiteIds { get; set; } = [];
    /// <summary>"open" | "done" | "all". Ignored by the Oversigt aggregation (#1162).</summary>
    public string Status { get; set; } = "open";
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }

    // --- new ---
    /// <summary>0-based page index. Ignored when <see cref="PageSize"/> is &lt;= 0.</summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// Rows per page. &lt;= 0 means "no paging, return everything" — capped
    /// server-side at 5000 rows (BackendConfigurationComplianceReportService
    /// .MaxRowsReturned), which is returned silently with a warning logged.
    /// The unpaged path exists for #1167 (Rapport groups over the whole filtered
    /// set) and #1169 (export).
    /// </summary>
    public int PageSize { get; set; } = 25;

    /// <summary>
    /// Sort key: taskDate | title | propertyName | boardName | completed | doneAt.
    /// Matched case-insensitively. Null / empty / unknown falls back to
    /// <c>taskDate</c> — an unknown key never throws.
    /// </summary>
    public string Sort { get; set; }

    /// <summary>Descending when true. Defaults to true, which is taskDate's natural direction.</summary>
    public bool IsSortDsc { get; set; } = true;
}
