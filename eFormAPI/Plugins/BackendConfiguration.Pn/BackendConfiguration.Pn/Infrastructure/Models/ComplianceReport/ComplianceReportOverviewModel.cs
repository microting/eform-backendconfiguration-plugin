using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// Response for <c>POST api/backend-configuration-pn/compliance-report/overview</c>.
/// Unpaged by decision (#1162 §4): one row per property, and the largest installs
/// are nowhere near a page's worth. Client-side sorting in #1164 depends on having
/// the whole set.
/// </summary>
public class ComplianceReportOverviewModel
{
    /// <summary>
    /// One row per property that has at least one matching compliance row.
    /// A property with none produces NO row.
    ///
    /// Ordered by <c>PropertyName</c> ascending, ordinal, as a stable default —
    /// a reproducible order for tests, not a contract the client relies on
    /// (#1164 sorts client-side, default compliancePct ascending / worst first).
    /// </summary>
    public List<ComplianceReportOverviewRowModel> Rows { get; set; } = [];

    /// <summary>
    /// WEIGHTED totals: the counters are summed across rows and
    /// <c>CompliancePct</c> is <c>Totals.DueDone / Totals.DueTotal</c> — never the
    /// average of <c>Rows[].CompliancePct</c>. One property at 1/1 and one at 0/100
    /// gives <b>1</b>, not 50.
    ///
    /// ALWAYS non-null, including for an empty result (all-zero counters,
    /// <c>CompliancePct == null</c>) — #1164's empty state depends on it.
    /// </summary>
    public ComplianceReportOverviewRowModel Totals { get; set; } = new();
}
