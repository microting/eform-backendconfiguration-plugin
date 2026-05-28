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
}
