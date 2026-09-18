using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// One section of the Rapport view — one per REPORT HEADLINE (#1188), the
/// headline being the task's <c>AreaRulePlanning.ItemPlanningTagId</c> — the
/// calendar modal's "Rapportoverskrift" select, the column the wizard labels
/// "the report table header tag", and the grouping key the old Rapport
/// (<c>BackendConfigurationReportService.GenerateReportV2</c>) always used —
/// holding one table per eForm template
/// (#1276, see <see cref="ComplianceReportTemplateTableModel"/>).
///
/// <para>
/// <b>#1188 REVERSED four documented decisions</b>, all of which had read the
/// prototype's literal <c>Rapportoverskrift</c> heading as a placeholder and
/// built the grouping around tags instead:
/// <list type="bullet">
/// <item>#1160 decision 5 — "Rapport groups by tag, then sub-groups by eForm
/// template. Not a union-of-fields." #1188 made it one group per headline over
/// the keyed union of its templates' columns; #1276 puts the template
/// sub-grouping back under the headline.</item>
/// <item>#1166 — "Mirror this shape; do not reuse the type." — the
/// <c>ComplianceReportTagGroupModel</c> → <c>ComplianceReportTemplateGroupModel</c>
/// pair built around tag → template. Both types are gone; this one replaces
/// them.</item>
/// <item>#1167 — "grouping is tag group → eForm template sub-group" and "use the
/// template name as the sub-report heading". The heading is the headline name;
/// since #1276 the template name titles each table under it.</item>
/// <item>PR #1178 / <c>ComplianceExportDocumentBuilder.cs</c> ("ONE COMPOSITE
/// column, <c>{tag} – {template}</c>") — the export's <c>Delrapport</c> cell is
/// now the row's own <see cref="ComplianceReportCaseModel.Tags"/> joined
/// <c>" - "</c>, and there is no headline column (#1188 decision 4).</item>
/// </list>
/// The source is the customer PDF (page 4 "Tabel_Rapport": "one table per
/// Rapportoverskrift, not per tag; the task's tags shown one after another
/// separated by [-]").
/// </para>
///
/// <para>
/// The reasons the old type was not <c>ReportEformModel</c> still hold and are
/// not repeated here; what is carried forward from #1166 is the KEYED cell bag
/// (<see cref="ComplianceReportCaseModel.Cells"/>) rather than the positional
/// <c>CaseFields</c> list that caused #1160 finding 3.
/// </para>
/// </summary>
public class ComplianceReportHeadlineGroupModel
{
    /// <summary>
    /// The headline's items-planning <c>PlanningTag</c> id
    /// (<c>AreaRulePlanning.ItemPlanningTagId</c> of the row's lowest-Id live
    /// ARP). Never <c>null</c> in a response since #1301: rows whose planning
    /// carries no headline are excluded from the report (and its export) rather
    /// than collected in a fallback group. Kept nullable for wire compatibility.
    /// </summary>
    public int? HeadlineTagId { get; set; }

    /// <summary>
    /// The headline's <c>PlanningTag.Name</c>. <c>null</c> when
    /// <see cref="HeadlineTagId"/> has no <c>PlanningTags</c> row (the two live in
    /// different databases with no foreign key; the consumer renders
    /// <c>#{id}</c>).
    /// </summary>
    public string HeadlineName { get; set; }

    /// <summary>
    /// The section's caption: the DISTINCT union of the tag names across the
    /// group's cases in every table (each case's
    /// <see cref="ComplianceReportCaseModel.Tags"/>),
    /// alphabetical (ordinal, case-insensitive), joined with <c>" - "</c> —
    /// hyphen-minus with a space either side, verbatim from the PDF. Empty when
    /// no case in the group carries a tag. The headline itself never appears in
    /// it, even when the legacy area-rule path also stored the headline as an
    /// <c>AreaRulePlanningTag</c>. One caption per SECTION, above all its tables.
    /// </summary>
    public string TagsCaption { get; set; } = string.Empty;

    /// <summary>
    /// One table per SDK <c>Case.CheckListId</c> answered in this group (#1276),
    /// ordered by the template's translated name (ordinal, case-insensitive), then
    /// by id — so two same-named cloned templates stay two tables, lower id first.
    /// Never empty: a group exists only because a case landed in it.
    /// </summary>
    public List<ComplianceReportTemplateTableModel> Templates { get; set; } = [];
}
