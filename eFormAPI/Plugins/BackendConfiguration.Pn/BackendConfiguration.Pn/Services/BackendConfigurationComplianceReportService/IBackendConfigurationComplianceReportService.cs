using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.BackendConfigurationComplianceReportService;

/// <summary>
/// Backend for the standalone Compliance page (#1160). Owns the compliance
/// report read model that used to live on
/// <c>BackendConfigurationCalendarService.GetComplianceReport</c>, which #1170
/// removed together with the calendar's Compliance view mode.
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
    /// <remarks>
    /// The <c>MaxRowsReturned</c> safety cap always applies: the page (#1163) and
    /// its unpaged consumers (#1167/#1169) degrade to a capped row set rather than
    /// pulling an unbounded one. The opt-out that existed for the legacy
    /// <c>POST calendar/compliance-report</c> delegate went away with it (#1170).
    /// </remarks>
    /// <param name="requestModel">Filters, paging and sorting for the report.</param>
    Task<OperationDataResult<ComplianceReportPagedModel>> Index(
        ComplianceReportRequestModel requestModel);

    /// <summary>
    /// One compliance summary row per property, plus a WEIGHTED totals row, for
    /// the Oversigt view (#1162). Unpaged and unsorted by decision: one row per
    /// property, sorted client-side.
    ///
    /// <para>
    /// Runs the SAME candidate-set builder as <see cref="Index"/> with the
    /// <c>Status = "all"</c> behaviour — done and open together, still dropping
    /// user-deleted occurrences (soft-removed and not done). That sharing is the
    /// contract: if it were copied, an Oversigt percentage could disagree with the
    /// Detaljer row count for the same filters, and it would be reported as a data
    /// bug.
    /// </para>
    ///
    /// <para>
    /// Compliance is measured only over what has FALLEN DUE:
    /// <c>DueTotal</c> counts every row NOT strictly after today
    /// (<c>!(startOfDay(taskDate) &gt; today)</c> — a date that cannot be read counts as due),
    /// <c>DueDone</c> those also completed, and <c>CompliancePct</c> is
    /// <c>round(DueDone / DueTotal * 100)</c> — <c>null</c>, never 0, when
    /// <c>DueTotal</c> is 0. Future tasks never drag the number down.
    /// <c>Overdue</c> is not-completed AND STRICTLY before today.
    /// "Today" is <c>DateTime.UtcNow.Date</c>; see the implementation's doc
    /// comment for the timezone consequence.
    /// </para>
    /// </summary>
    Task<OperationDataResult<ComplianceReportOverviewModel>> Overview(
        ComplianceReportOverviewRequestModel requestModel);

    /// <summary>
    /// The Rapport view's read model (#1166, regrouped by #1188): the filtered
    /// compliance set grouped by REPORT HEADLINE
    /// (<c>AreaRulePlanning.ItemPlanningTagId</c> — the calendar modal's
    /// "Rapportoverskrift"), one flat table per headline whose columns are the
    /// UNION of every answered template's schema, one KEYED cell bag per case,
    /// and the group's tag names as a caption. Rows without a headline form one
    /// fallback group (<c>HeadlineTagId == null</c>), always last.
    ///
    /// <para>
    /// The template of a case is the SDK <c>Case.CheckListId</c> — never
    /// <c>AreaRule.EformId</c>, which tracks current configuration rather than what
    /// was answered (#1160 finding 1). It is carried on each case, because a
    /// section spans templates.
    /// </para>
    ///
    /// <para>
    /// Cells are a <c>Dictionary&lt;string,string&gt;</c> keyed on
    /// <c>$"f{Field.Id}"</c>. A missing key means UNANSWERED; there is no
    /// empty-string placeholder, and excluded field types get neither a column nor
    /// a cell. That is what makes the positional column-desync of #1160 finding 3
    /// inexpressible here.
    /// </para>
    ///
    /// <para>
    /// Runs the SAME candidate-set builder as <see cref="Index"/> and
    /// <see cref="Overview"/>, with the request's own <c>Status</c>. It is UNPAGED
    /// — <c>PageIndex</c>, <c>PageSize</c>, <c>Sort</c> and <c>IsSortDsc</c> are
    /// ignored — but the 5000-row safety cap still applies, silently and logged.
    /// Rows with no answered template (no SDK case, or a case with no
    /// <c>CheckListId</c>) carry no answers and form no group.
    /// </para>
    ///
    /// <para>
    /// Images are REFERENCES only: ids, a derived
    /// <c>{UploadedDataId}_700_{Checksum}{Extension}</c> display name, its
    /// <c>_300_</c> thumbnail counterpart and a geo link. No image bytes are
    /// read or returned.
    /// </para>
    /// </summary>
    Task<OperationDataResult<List<ComplianceReportHeadlineGroupModel>>> EformColumns(
        ComplianceReportRequestModel requestModel);
}
