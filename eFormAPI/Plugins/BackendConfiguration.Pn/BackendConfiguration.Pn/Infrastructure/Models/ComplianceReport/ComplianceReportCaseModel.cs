using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>One answered occurrence inside a template group.</summary>
public class ComplianceReportCaseModel
{
    public int ComplianceId { get; set; }

    /// <summary>The backing SDK case. Always &gt; 0 — a row without one carries no
    /// answers and never reaches a template group.</summary>
    public int SdkCaseId { get; set; }

    public int PropertyId { get; set; }
    public string PropertyName { get; set; }
    public string Title { get; set; }

    /// <summary>Effective occurrence date, <c>yyyy-MM-dd</c> (exception
    /// <c>NewDate</c> applied), formatted with the invariant culture.</summary>
    public string TaskDate { get; set; }

    public bool Completed { get; set; }

    /// <summary>
    /// The case's completion timestamp — <c>DoneAtUserModifiable ?? DoneAt</c>.
    /// This is the prototype's <c>Udført dato</c> column: CASE METADATA, not an
    /// eForm answer (#1160 finding 7). It never comes from an answer field, and
    /// the header is not renamed.
    /// </summary>
    public DateTime? DoneAt { get; set; }

    public List<string> WorkerNames { get; set; } = [];

    /// <summary>
    /// Answers, keyed by <see cref="ComplianceReportColumnModel.Key"/>.
    ///
    /// <para>
    /// A MISSING key means unanswered. There is deliberately no empty-string
    /// placeholder and no positional slot: the desync of #1160 finding 3 —
    /// headers built from a filtered field list, cells emitted from an unfiltered
    /// one, plus an <c>else</c> that appends a blank cell for every unanswered
    /// field — cannot be expressed in a keyed bag. Excluded field types get
    /// neither a column nor a cell.
    /// </para>
    /// </summary>
    public Dictionary<string, string> Cells { get; set; } = new();

    public int ImagesCount { get; set; }

    public List<ComplianceReportImageModel> Images { get; set; } = [];
}
