using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// One tag group of the Rapport view (#1166). Mirrors the grouping shape of
/// <c>Infrastructure/Models/Report/ReportEformModel.cs</c> — tag, then eForm
/// template (#1160 decision 5) — WITHOUT reusing the type.
///
/// <para>
/// Two reasons the existing type is mirrored rather than reused:
/// <c>ReportEformModel</c> is consumed by <c>ExcelService</c>, <c>WordService</c>
/// and the PDF path, so reshaping it would change three shipped exports; and its
/// <c>ReportEformItemModel.CaseFields</c> is a POSITIONAL
/// <c>List&lt;KeyValuePair&lt;string,string&gt;&gt;</c>, which is the root of
/// #1160 finding 3 and the one thing this path must not carry forward.
/// </para>
/// </summary>
public class ComplianceReportTagGroupModel
{
    /// <summary>
    /// Items-planning <c>PlanningTag</c> id, or <c>null</c> for the rows that carry
    /// no tag at all. The untagged group's LABEL ("Uden tag") belongs to #1167 —
    /// this API carries no Danish.
    /// </summary>
    public int? TagId { get; set; }

    public string TagName { get; set; }

    public List<ComplianceReportTemplateGroupModel> Templates { get; set; } = [];
}
