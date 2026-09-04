using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;

/// <summary>
/// Server-side CSV / Excel / PDF export for all three Compliance view modes
/// (#1169). ONE entry point: the view mode and the output format are both fields
/// on the request, so the three views × three formats are nine combinations of one
/// endpoint rather than nine endpoints.
/// </summary>
public interface IBackendConfigurationComplianceExportService
{
    /// <summary>
    /// Renders the current filter set to a downloadable file.
    ///
    /// <para>
    /// The data comes from <c>BackendConfigurationComplianceReportService</c> —
    /// <c>Overview</c>, <c>Index</c> (unpaged) or <c>EformColumns</c> according to
    /// the view mode — so the file carries the SAME numbers the screen shows.
    /// Nothing is re-queried and nothing is re-aggregated here; in particular the
    /// weighted totals, the null-not-zero compliance percentage and the
    /// strictly-before-today overdue count are taken from <c>Overview</c>'s output.
    /// </para>
    ///
    /// <para>
    /// The export always covers the FULL filtered set, never one page.
    /// </para>
    ///
    /// <para>
    /// Failure modes returned as a failed <c>OperationDataResult</c> rather than an
    /// exception: an unknown view mode or format, a failure inside the underlying
    /// report service (its message is passed through), and — for <c>pdf</c> only —
    /// an unavailable or wedged <c>soffice</c>.
    /// </para>
    /// </summary>
    Task<OperationDataResult<ComplianceExportFileModel>> Export(
        ComplianceReportExportRequestModel requestModel, CancellationToken cancellationToken = default);
}
