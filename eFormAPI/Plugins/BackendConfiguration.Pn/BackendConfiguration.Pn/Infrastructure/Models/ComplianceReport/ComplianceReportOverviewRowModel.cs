namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// One property's compliance summary, and — reusing the same shape — the weighted
/// totals row. Ported from the prototype's row shape in
/// <c>lorem-ipsum/kalender/compliance-overview.js:31-46</c>.
///
/// <para>
/// The server returns NUMBERS and <c>null</c> only. Formatting ("–"), banding
/// ("is-low"/"is-mid"/"is-high") and the thresholds COMPLIANCE_MID_MIN /
/// COMPLIANCE_HIGH_MIN / OVERDUE_URGENT_MIN are presentation and belong to #1164.
/// </para>
/// </summary>
public class ComplianceReportOverviewRowModel
{
    /// <summary>0 on the totals row.</summary>
    public int PropertyId { get; set; }

    /// <summary>
    /// <c>null</c> on the totals row — #1164 supplies the "I alt" label. The API
    /// deliberately carries no Danish display string.
    /// </summary>
    public string PropertyName { get; set; }

    /// <summary>
    /// Every matching row, due or not. Computed but deliberately NOT rendered by
    /// #1164 (its OVERVIEW_COLUMNS is exactly propertyName / overdue /
    /// compliancePct, and a prototype test pins the "Opgaver i alt" and "Udført"
    /// headers as absent). Returned anyway so a wrong percentage is debuggable
    /// without re-querying, and so the C# model does not diverge from the
    /// prototype's row shape.
    /// </summary>
    public int Total { get; set; }

    /// <summary>Completed rows, due or not. Deliberately unrendered — see <see cref="Total"/>.</summary>
    public int Done { get; set; }

    /// <summary>
    /// Not completed AND dated STRICTLY BEFORE today. A task due <i>today</i> and
    /// not done raises <see cref="DueTotal"/> (and so lowers the percentage) but is
    /// NOT overdue.
    /// </summary>
    public int Overdue { get; set; }

    /// <summary>Rows that have fallen due: <c>!(startOfDay(taskDate) &gt; today)</c>.</summary>
    public int DueTotal { get; set; }

    /// <summary>Due rows that are also completed — the numerator of <see cref="CompliancePct"/>.</summary>
    public int DueDone { get; set; }

    /// <summary>
    /// <c>round(DueDone / DueTotal * 100)</c>, away-from-zero (JS
    /// <c>Math.round</c> semantics). <c>null</c> — never <c>0</c>, never NaN —
    /// when <see cref="DueTotal"/> is 0: a property whose work is simply not due
    /// yet has no percentage, and rendering it as a red 0 % would be a lie.
    /// </summary>
    public int? CompliancePct { get; set; }
}
