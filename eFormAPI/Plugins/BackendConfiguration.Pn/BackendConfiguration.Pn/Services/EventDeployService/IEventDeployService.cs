using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.EventDeployService;

/// <summary>
/// Eagerly deploys SDK cases + Compliance rows for rotations inside the
/// requested date window so that flutter-eform can complete future events
/// (today+1, today+2) via the existing CompleteEvent path. Runs inline in
/// the gRPC handler; does NOT publish Rebus messages and does NOT mutate
/// scheduler-owned Planning state.
/// </summary>
public interface IEventDeployService
{
    Task EnsureDeployedAsync(
        string propertyId,
        IReadOnlyCollection<string> boardIds,
        string fromDateKey,
        string toDateKey,
        int sdkSiteId,
        CancellationToken cancellationToken);

    /// <summary>
    /// On-demand materialisation of a single Compliance row + backing
    /// PlanningCase / PlanningCaseSite / SDK Case for one calendar
    /// occurrence. Invoked from the angular calendar "complete from
    /// indicator" flow when the nightly batch has not yet deployed the
    /// occurrence the user clicked.
    ///
    /// Idempotent: if a Compliance row already exists for
    /// (<paramref name="areaRulePlanning"/>.ItemPlanningId, <paramref name="deadline"/>.Date)
    /// with a non-zero <c>MicrotingSdkCaseId</c>, returns it without
    /// performing any writes.
    ///
    /// Returns <c>null</c> when materialisation could not proceed (planning
    /// missing, SDK site missing, language missing). Per-call faults during
    /// the SDK write path bubble through the existing duplicate-key tolerant
    /// path in <c>EnsureComplianceRowAsync</c>; everything else throws so the
    /// caller can surface it.
    /// </summary>
    Task<EnsureComplianceResult?> EnsureComplianceForOccurrenceAsync(
        AreaRulePlanning areaRulePlanning,
        DateTime deadline,
        int sdkSiteId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Propagates an eForm change to every already-deployed occurrence of an
    /// event that has not been completed yet. The deployment rows
    /// (<c>PlanningCase</c> / <c>PlanningCaseSite</c> / <c>Compliance</c> and
    /// the SDK <c>Case</c>) freeze the eForm at deploy time, so without this
    /// pass an occurrence deployed before the edit still completes with the
    /// creation-time eForm even though the calendar shows the new one.
    ///
    /// For each non-removed <c>Compliance</c> row of
    /// <paramref name="areaRulePlanning"/>'s planning, and for each SITE
    /// deployed for that occurrence, the old SDK case is retracted and a
    /// replacement is created from <paramref name="newEformId"/>. Completed
    /// cases (<c>Status == 100</c> or a done timestamp) keep their historical
    /// eForm and are never touched, and a case already pointing at
    /// <paramref name="newEformId"/> is skipped, so the pass is idempotent.
    ///
    /// The <c>Compliance</c> row is updated IN PLACE (same <c>Compliance.Id</c>,
    /// new <c>MicrotingSdkCaseId</c> + <c>MicrotingSdkeFormId</c>) — the
    /// calendar UI holds <c>complianceId</c> and the compliance view depends on
    /// row stability.
    ///
    /// A site that is no longer a live ASSIGNEE of the event (its
    /// items-planning <c>PlanningSite</c> is gone and it is not a member of any
    /// worker tag on the event) gets its case RETRACTED and deliberately NOT
    /// recreated — otherwise unassigning a worker and changing the eForm in the
    /// same save would hand the removed worker a brand-new case.
    ///
    /// Each site is independent: a site whose swap fails is logged and left
    /// without a case for that rotation (never with a wrong one), and its
    /// <c>Compliance.MicrotingSdkCaseId</c> is reset to 0 so the stuck-row
    /// recovery branch of <see cref="EnsureDeployedAsync"/> redeploys it.
    ///
    /// KNOWN AND ACCEPTED: a <c>PlanningCaseSite</c> left over from a case that
    /// was retracted WITHOUT a replacement (unassigned site, or a failed
    /// create) stays behind pointing at a removed SDK case. Such rows are inert
    /// — every reader joins through the live SDK case — and are not a defect in
    /// this pass; they belong to the duplicate-<c>PlanningCaseSite</c> issue.
    ///
    /// The caller must NOT invoke this for an event that is inactive after the
    /// save: an inactive event's <c>PlanningCaseSite</c> rows are left
    /// non-removed by the wizard's deactivation branch, and the sweep would
    /// revive them into live cases.
    ///
    /// No-op when <paramref name="oldEformId"/> equals
    /// <paramref name="newEformId"/> or the event has no deployed occurrence.
    /// </summary>
    Task RepairEformForOpenOccurrencesAsync(
        AreaRulePlanning areaRulePlanning,
        int oldEformId,
        int newEformId,
        CancellationToken cancellationToken = default);
}
