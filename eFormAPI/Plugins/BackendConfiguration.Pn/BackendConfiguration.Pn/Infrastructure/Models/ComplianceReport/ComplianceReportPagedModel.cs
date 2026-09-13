using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;

/// <summary>
/// Paged compliance-report response. Deliberately shaped like
/// <c>Microting.eFormApi.BasePn.Infrastructure.Models.Common.Paged&lt;T&gt;</c>
/// so the frontend paginator reads it the same way it reads
/// <c>CompliancesController.Index</c>.
/// </summary>
public class ComplianceReportPagedModel
{
    /// <summary>
    /// Rows matching the filters BEFORE paging — counted AFTER the in-memory
    /// occurrence-exception, board and status filters, never from a raw SQL
    /// count (see the service's phase C).
    /// </summary>
    public int Total { get; set; }

    public List<ComplianceReportRowModel> Entities { get; set; } = [];
}
