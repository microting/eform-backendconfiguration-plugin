namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

/// <summary>
/// Result of an on-demand Compliance materialisation for a single calendar
/// occurrence. Returned by
/// <see cref="Services.EventDeployService.IEventDeployService.EnsureComplianceForOccurrenceAsync"/>.
/// </summary>
public class EnsureComplianceResult
{
    /// <summary>
    /// True when a new Compliance / PlanningCase / PlanningCaseSite / SDK
    /// Case set was created; false when an existing Compliance row was
    /// returned by the idempotence guard.
    /// </summary>
    public bool Created { get; set; }

    public int ComplianceId { get; set; }
    public int SdkCaseId { get; set; }
    public int TemplateId { get; set; }
}
