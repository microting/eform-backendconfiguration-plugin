using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// Request contract for <c>POST api/backend-configuration-pn/compliance-report/export</c>
/// — one endpoint serving all three view modes × three output formats (#1169).
///
/// <para>
/// <b>The filter set travels WITH the export request</b> (#1169 §3, variant 2 of the
/// two recommended). The server re-runs the same filters through the same
/// <c>BuildCandidateSet</c> the screen used, so the file always agrees with the
/// filter bar even when the page still shows the pre-fetch placeholder and even
/// when the user changed a filter without pressing "Opdater tabel". No download
/// control has to be disabled, and the export never depends on a fetch having
/// happened first.
/// </para>
///
/// <para>
/// <b>Paging is deliberately absent.</b> The export always covers the FULL filtered
/// set (#1169 §3, behaviour 1 — kept): it calls <c>Index</c> with
/// <c>PageSize = 0</c>. The service's own <c>MaxRowsReturned</c> ceiling still
/// applies, silently and logged, exactly as it does for the Rapport view.
/// </para>
///
/// <para>
/// POST, not GET, because the filter set is a structured object with four
/// multi-select lists — the same body the other three compliance-report actions
/// take. The response is a file stream with a
/// <c>Content-Disposition: attachment</c>, so the client saves it from a blob
/// rather than navigating to it.
/// </para>
/// </summary>
public class ComplianceReportExportRequestModel
{
    // --- the filter set, identical to ComplianceReportRequestModel's ---

    public int? PropertyId { get; set; }

    public List<int> BoardIds { get; set; } = [];

    /// <summary>Items-planning PlanningTag ids (same ids the sidebar tag filter uses).</summary>
    public List<int> TagIds { get; set; } = [];

    /// <summary>Employee filter — SDK Site ids, via AreaRulePlanning.PlanningSites.</summary>
    public List<int> SiteIds { get; set; } = [];

    /// <summary>
    /// "open" | "done" | "all". IGNORED for <c>ViewMode == "overview"</c>, which
    /// counts done and not-done together — the Oversigt aggregation has no
    /// <c>Status</c> on its own request model at all, and the UI disables the
    /// control there.
    /// </summary>
    public string Status { get; set; } = "open";

    public DateTime DateFrom { get; set; }

    public DateTime DateTo { get; set; }

    // --- export-specific ---

    /// <summary>
    /// <c>overview</c> | <c>details</c> | <c>report</c>. Matched
    /// case-insensitively; anything else (including null) is rejected with a
    /// failed <c>OperationDataResult</c> rather than silently defaulting, because
    /// a wrong default would hand the user a file of the wrong shape without
    /// saying so.
    /// </summary>
    public string ViewMode { get; set; }

    /// <summary>
    /// <c>csv</c> | <c>xlsx</c> | <c>pdf</c>. Matched case-insensitively; anything
    /// else is rejected, never defaulted.
    ///
    /// <para>
    /// There is no <c>docx</c> arm even though PDF is produced by converting one:
    /// the docx is an implementation detail of the PDF path, and #1169's
    /// acceptance criteria name exactly three formats.
    /// </para>
    /// </summary>
    public string Format { get; set; }

    /// <summary>
    /// Image appendix — #1169 §6, decided and stated: <b>opt-in, default off</b>.
    ///
    /// <para>
    /// Meaningless for <c>csv</c> and <c>xlsx</c> (a spreadsheet cell cannot hold a
    /// photograph) and IGNORED there. Meaningless for <c>overview</c> and
    /// <c>details</c>, which carry no case images, and ignored there too. It has an
    /// effect only for <c>report</c> + <c>pdf</c>.
    /// </para>
    ///
    /// <para>
    /// Default <c>false</c> because the measured cost is 111 appendix sheets out of
    /// 135 for a single quarter of completed work. When it IS on, at most
    /// <c>ComplianceExportDocumentBuilder.MaxAppendixImagesPerCase</c> images per case
    /// are embedded and the document says so in the appendix heading.
    /// </para>
    /// </summary>
    public bool IncludeImageAppendix { get; set; }
}
