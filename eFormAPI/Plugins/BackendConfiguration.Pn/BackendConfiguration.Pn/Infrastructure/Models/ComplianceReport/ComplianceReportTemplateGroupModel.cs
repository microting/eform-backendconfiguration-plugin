using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// One eForm template inside a tag group: its column schema and the cases
/// answered against it.
/// </summary>
public class ComplianceReportTemplateGroupModel
{
    /// <summary>
    /// SDK <c>Case.CheckListId</c> — the template ACTUALLY answered (#1160
    /// finding 1). Never <c>AreaRule.EformId</c>, which tracks current
    /// configuration and, measured against live data, mismatched 34 rows and was
    /// NULL for ~16 % of them.
    /// </summary>
    public int CheckListId { get; set; }

    public string CheckListName { get; set; }

    /// <summary>
    /// Every <c>CheckListId</c> merged into this group. Structurally-identical
    /// cloned templates are a KNOWN refinement that is deliberately NOT
    /// implemented here (#1166 §8), so today this list always holds exactly one
    /// id — <see cref="CheckListId"/> itself. It exists so the future merge can
    /// land without another contract change on #1167/#1168/#1169.
    /// </summary>
    public List<int> MergedCheckListIds { get; set; } = [];

    /// <summary>
    /// The ordered column schema, in the template's own field order.
    /// <see cref="ComplianceReportColumnModel.Key"/> is stable;
    /// <see cref="ComplianceReportColumnModel.Label"/> is translated.
    /// </summary>
    public List<ComplianceReportColumnModel> Columns { get; set; } = [];

    /// <summary>
    /// True when deriving this template's column schema FAILED, so
    /// <see cref="Columns"/> is empty because the schema could not be read — not
    /// because the template has no answerable fields and not because nobody
    /// answered. Without this flag the two are indistinguishable in the response:
    /// both render as a group with zero columns and no cells, and the user would
    /// see an empty table with nothing saying anything went wrong.
    ///
    /// <para>
    /// The root cause is in the SDK, not here:
    /// <c>Core.Advanced_TemplateFieldReadAll</c> →
    /// <c>SqlController.TemplateFieldReadAll</c> does a bare <c>FirstAsync</c> on
    /// <c>CheckListTranslations</c> (<c>SqlController.cs:668-670</c>) for every
    /// field with a null <c>ParentFieldId</c>, which throws for a language with a
    /// translation gap — de-DE in particular. Fixing that is a separate SDK
    /// release; until then one unreadable template degrades to this flag rather
    /// than failing the whole report. #1167 renders "columns unavailable".
    /// </para>
    /// </summary>
    public bool SchemaUnavailable { get; set; }

    public List<ComplianceReportCaseModel> Cases { get; set; } = [];
}
