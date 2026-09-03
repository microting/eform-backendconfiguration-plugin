using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.BackendConfigurationComplianceReportService;

/// <summary>
/// Backend for the standalone Compliance page (#1160). Owns the compliance
/// report read model that used to live on
/// <c>BackendConfigurationCalendarService.GetComplianceReport</c>; that method is
/// now an unpaged delegate onto <see cref="Index"/> and is removed by #1170.
/// </summary>
public interface IBackendConfigurationComplianceReportService
{
    /// <summary>
    /// Returns one page of compliance-report rows for the given date window
    /// (deadline-scoped, occurrence-exception aware).
    ///
    /// Status classification: done = backing SDK case Status == 100; open = row
    /// not soft-removed and not done; soft-removed rows that are NOT done were
    /// user-deleted occurrences and are never returned, for any status.
    ///
    /// <c>Total</c> is the count of matching rows AFTER every filter — including
    /// the ones that can only run in memory (occurrence delete/move, effective
    /// board, status) — and BEFORE paging.
    /// </summary>
    /// <param name="requestModel">Filters, paging and sorting for the report.</param>
    /// <param name="enforceRowCap">
    /// Whether the <c>MaxRowsReturned</c> safety cap applies. It defaults to
    /// <c>true</c>, which is what the public <c>ComplianceReportController.Index</c>
    /// endpoint uses: the new page (#1163) and its unpaged consumers (#1167/#1169)
    /// degrade to a capped row set rather than pulling an unbounded one.
    ///
    /// The ONLY caller that passes <c>false</c> is the legacy delegate
    /// <c>BackendConfigurationCalendarService.GetComplianceReport</c>, which backs
    /// <c>POST calendar/compliance-report</c>. That endpoint predates the cap and
    /// its contract is "every matching row"; capping it would silently truncate a
    /// response that used to be complete (#1161 §11). #1170 removes the legacy
    /// delegate and this parameter together.
    ///
    /// Deliberately a method parameter and NOT a property on
    /// <c>ComplianceReportRequestModel</c>: it must never become part of the JSON
    /// request contract, where a client could switch the cap off.
    /// </param>
    Task<OperationDataResult<ComplianceReportPagedModel>> Index(
        ComplianceReportRequestModel requestModel, bool enforceRowCap = true);
}
