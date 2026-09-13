using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// One section of the Rapport view: ONE TABLE PER REPORT HEADLINE (#1188), the
/// headline being the task's <c>AreaRulePlanning.ItemPlanningTagId</c> — the
/// calendar modal's "Rapportoverskrift" select, the column the wizard labels
/// "the report table header tag", and the grouping key the old Rapport
/// (<c>BackendConfigurationReportService.GenerateReportV2</c>) always used.
///
/// <para>
/// <b>This REVERSES four documented decisions</b>, all of which had read the
/// prototype's literal <c>Rapportoverskrift</c> heading as a placeholder and
/// built the grouping around tags instead:
/// <list type="bullet">
/// <item>#1160 decision 5 — "Rapport groups by tag, then sub-groups by eForm
/// template. Not a union-of-fields." Now: one group per headline, and the
/// columns ARE the keyed union of every template answered in the group.</item>
/// <item>#1166 — "Mirror this shape; do not reuse the type." — the
/// <c>ComplianceReportTagGroupModel</c> → <c>ComplianceReportTemplateGroupModel</c>
/// pair built around tag → template. Both types are gone; this one replaces
/// them.</item>
/// <item>#1167 — "grouping is tag group → eForm template sub-group" and "use the
/// template name as the sub-report heading". Now: the heading is the headline
/// name and the template name is rendered nowhere.</item>
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
    /// ARP), or <c>null</c> for the ONE fallback group holding every row whose
    /// planning carries no headline. The fallback group is always LAST and its
    /// label ("Uden rapportoverskrift", key <c>WithoutReportHeadline</c>) is the
    /// consumer's — this API carries no Danish.
    /// </summary>
    public int? HeadlineTagId { get; set; }

    /// <summary>
    /// The headline's <c>PlanningTag.Name</c>. <c>null</c> when
    /// <see cref="HeadlineTagId"/> has no <c>PlanningTags</c> row (the two live in
    /// different databases with no foreign key; the consumer renders
    /// <c>#{id}</c>) and <c>null</c> for the fallback group.
    /// </summary>
    public string HeadlineName { get; set; }

    /// <summary>
    /// The section's caption: the DISTINCT union of the tag names across the
    /// group's cases (each case's <see cref="ComplianceReportCaseModel.Tags"/>),
    /// alphabetical (ordinal, case-insensitive), joined with <c>" - "</c> —
    /// hyphen-minus with a space either side, verbatim from the PDF. Empty when
    /// no case in the group carries a tag. The headline itself never appears in
    /// it, even when the legacy area-rule path also stored the headline as an
    /// <c>AreaRulePlanningTag</c>.
    /// </summary>
    public string TagsCaption { get; set; } = string.Empty;

    /// <summary>
    /// Every SDK <c>Case.CheckListId</c> answered in this group — the template
    /// ACTUALLY answered (#1160 finding 1), never <c>AreaRule.EformId</c>.
    /// Distinct, ordered by the template's translated name then id — the same
    /// order the template blocks take inside <see cref="Columns"/>.
    /// </summary>
    public List<int> CheckListIds { get; set; } = [];

    /// <summary>
    /// The subset of <see cref="CheckListIds"/> whose column schema could NOT be
    /// derived (the SDK's <c>Advanced_TemplateFieldReadAll</c> threw — a
    /// translation gap, de-DE in particular). Their fields are absent from
    /// <see cref="Columns"/> because derivation FAILED, not because nobody
    /// answered; the consumer renders a per-template "columns unavailable"
    /// notice, and a whole-section one when every id is here.
    /// </summary>
    public List<int> SchemaUnavailableCheckListIds { get; set; } = [];

    /// <summary>
    /// The section's column schema: the UNION of the per-template schemas of
    /// every id in <see cref="CheckListIds"/>, templates in that list's order,
    /// fields in each template's own order. Keys are <c>f{fieldId}</c> and are
    /// collision-free by construction (a field belongs to exactly one template).
    /// A FRESH list per group — never a shared reference to the projector's
    /// cached schema, which every group answered on the same template reads.
    /// A case answered on template A carries no key for template B's columns
    /// and renders the empty glyph under them, in place.
    /// </summary>
    public List<ComplianceReportColumnModel> Columns { get; set; } = [];

    /// <summary>
    /// The group's rows, each case EXACTLY ONCE, in occurrence-date order with
    /// the compliance id as the tiebreak. Σ over every group equals the number
    /// of filtered rows with an answered <c>CheckListId</c>.
    /// </summary>
    public List<ComplianceReportCaseModel> Cases { get; set; } = [];
}
