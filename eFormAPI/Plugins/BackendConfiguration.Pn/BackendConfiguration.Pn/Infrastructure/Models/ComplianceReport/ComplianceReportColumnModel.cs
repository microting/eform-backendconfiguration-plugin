namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>One answer column of a template group.</summary>
public class ComplianceReportColumnModel
{
    /// <summary>
    /// The STABLE cell key, <c>$"f{FieldId}"</c> — see
    /// <c>ComplianceReportCaseModel.Cells</c>. Derived from the SDK
    /// <c>Field.Id</c>, which does not move when a translation, a label or the
    /// display order changes. NOT the display label, and NOT an array position.
    /// </summary>
    public string Key { get; set; }

    public int FieldId { get; set; }

    /// <summary>
    /// Translated field label, prefixed with the child checklist's name when the
    /// template has child checklists and the two differ — the same rule
    /// <c>BackendConfigurationReportService.GenerateReportV2</c> applies to its
    /// headers.
    /// </summary>
    public string Label { get; set; }

    /// <summary>
    /// The SDK <c>Constants.FieldTypes</c> value, so #1167 can right-align
    /// numbers, render dates and so on without re-deriving the type.
    /// </summary>
    public string FieldType { get; set; }
}
