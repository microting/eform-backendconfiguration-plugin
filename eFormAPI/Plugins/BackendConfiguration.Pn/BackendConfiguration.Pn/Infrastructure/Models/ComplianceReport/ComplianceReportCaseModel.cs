using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>One answered occurrence inside a headline group (#1188).</summary>
public class ComplianceReportCaseModel
{
    public int ComplianceId { get; set; }

    /// <summary>The backing SDK case. Always &gt; 0 — a row without one carries no
    /// answers and never reaches a headline group.</summary>
    public int SdkCaseId { get; set; }

    /// <summary>
    /// The template THIS row was answered on — the SDK <c>Case.CheckListId</c>.
    /// Required since #1188 because a section spans templates: the consumer's
    /// <c>Rediger</c> route and its <c>canEdit</c> gate need the row's OWN
    /// template, which the section no longer identifies. Always set (a row
    /// without one forms no group); nullable only so the JSON shape states it.
    /// </summary>
    public int? CheckListId { get; set; }

    /// <summary>
    /// The row's tag names — every live <c>AreaRulePlanningTag</c> on the row's
    /// planning, resolved to <c>PlanningTag.Name</c> (an id with no
    /// <c>PlanningTags</c> row is kept as <c>#{id}</c> rather than dropped),
    /// sorted ordinal-case-insensitively, and EXCLUDING the group's headline id.
    /// The exclusion is required, not defensive: the legacy area-rule path
    /// (<c>BackendConfigurationTaskWizardService.UpdateTags</c>) also stores the
    /// headline as an ARP tag, which would otherwise render "Flydelag - Flydelag".
    /// The export's <c>Delrapport</c> cell is this list joined <c>" - "</c>.
    /// </summary>
    public List<string> Tags { get; set; } = [];

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
