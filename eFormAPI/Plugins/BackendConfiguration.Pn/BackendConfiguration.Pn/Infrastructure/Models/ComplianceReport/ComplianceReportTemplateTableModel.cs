using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// One eForm's table inside a report headline (#1276): the cases of ONE SDK
/// template answered under the headline, rendered against THAT template's column
/// schema only.
///
/// <para>
/// #1188 rendered a headline as one flat table over the UNION of every template
/// answered under it, which on the customer's data meant a single 16-column table
/// carrying <c>KOMMENTAR</c> and <c>Udført dato</c> twice, most of its cells the
/// en dash because each row only ever answers one template's half. #1188's own
/// source required only "one table per report headline, not per tag"; the union
/// was its design choice, and "one grid per template under one heading" was the
/// alternative it rejected. #1276 takes that alternative — which is also how the
/// old Rapport (<c>BackendConfigurationReportService.GenerateReportV2</c>) grouped:
/// headline → eForm. The headline grouping, its tags caption and its ordering are
/// #1188's and are unchanged; only the table split is reversed.
/// </para>
/// </summary>
public class ComplianceReportTemplateTableModel
{
    /// <summary>
    /// The SDK <c>Case.CheckListId</c> every case in <see cref="Cases"/> was
    /// answered on — the template ACTUALLY answered (#1160 finding 1), never
    /// <c>AreaRule.EformId</c>. Each case carries the same id on its own
    /// <see cref="ComplianceReportCaseModel.CheckListId"/>.
    /// </summary>
    public int CheckListId { get; set; }

    /// <summary>
    /// The template's translated name (the user's language, else any language —
    /// the projector's <c>CheckListTranslations</c> fallback), which the table is
    /// titled with. EMPTY, never null, when no translation carries a name; the
    /// consumer then renders <c>#{CheckListId}</c>, the same neutral form a
    /// nameless headline gets.
    /// </summary>
    public string CheckListName { get; set; } = string.Empty;

    /// <summary>
    /// True when this template's column schema could NOT be derived (the SDK's
    /// <c>Advanced_TemplateFieldReadAll</c> threw — a translation gap, de-DE in
    /// particular). <see cref="Columns"/> is then empty because derivation FAILED,
    /// not because nobody answered anything; the consumer renders a "columns
    /// unavailable" notice on this table.
    /// </summary>
    public bool SchemaUnavailable { get; set; }

    /// <summary>
    /// This template's answer columns and NO other template's: keys
    /// <c>f{fieldId}</c>, fields in the template's own order. A FRESH list of
    /// FRESH column objects per table — never the projector's cached schema list,
    /// which every table answered on the same template (in any headline) reads, so
    /// a consumer mutating one table's columns cannot reach another's.
    /// </summary>
    public List<ComplianceReportColumnModel> Columns { get; set; } = [];

    /// <summary>
    /// The cases answered on this template under this headline, each case EXACTLY
    /// ONCE across the whole response, in occurrence-date order with the
    /// compliance id as the tiebreak (the order the whole response is built in).
    /// Σ over every table of every group equals the number of filtered rows with an
    /// answered <c>CheckListId</c>.
    /// </summary>
    public List<ComplianceReportCaseModel> Cases { get; set; } = [];
}
