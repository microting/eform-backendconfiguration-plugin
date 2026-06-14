using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using SdkCore = eFormCore.Core;
using SdkDbContext = Microting.eForm.Infrastructure.MicrotingDbContext;
using SdkSite = Microting.eForm.Infrastructure.Data.Entities.Site;
using SdkLanguage = Microting.eForm.Infrastructure.Data.Entities.Language;

namespace BackendConfiguration.Pn.Services.EventDeployService;

/// <summary>
/// Inline deploy pipeline invoked synchronously by
/// <c>EventsGrpcService.ListEvents</c> BEFORE the read-side query, so future-day
/// rotations come back with a non-zero <c>complianceId</c> / <c>microtingSdkCaseId</c>
/// and populated <c>fields</c>. For every rotation in the requested window that
/// does not yet have a backing <see cref="Compliance"/> row, the pipeline creates:
///
/// <list type="bullet">
///   <item><c>PlanningCase</c> + <c>PlanningCaseSite</c> rows (mirrors
///     <c>ItemCaseCreateHandler.cs:83-194</c>).</item>
///   <item>SDK <c>Case</c> via <c>core.CaseCreate</c> (mirrors
///     <c>ItemCaseCreateHandler.cs:236-246</c>).</item>
///   <item><see cref="Compliance"/> row (mirrors
///     <c>EformParsedByServerHandler.cs:157-184</c>).</item>
/// </list>
///
/// Idempotence is enforced via the natural <c>(PlanningId, Deadline.Date)</c>
/// key on <see cref="Compliance"/> and via the canonical
/// <c>planningCaseSite.MicrotingSdkCaseId &gt;= 1</c> guard for the SDK case
/// (mirrors <c>ItemCaseCreateHandler.cs:205</c>).
///
/// Invariants the pipeline maintains (do NOT change without coordinating with
/// the scheduler microservice):
/// <list type="bullet">
///   <item>No Rebus publish.</item>
///   <item>No mutation of <c>Planning.LastExecutedTime</c>,
///     <c>DoneInPeriod</c>, <c>NextExecutionTime</c>, or
///     <c>PushMessageSent</c>.</item>
///   <item>Per-rotation try/catch — a single bad row never aborts the whole
///     pass.</item>
/// </list>
/// </summary>
public class EventDeployService(
    BackendConfigurationPnDbContext dbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IEFormCoreService coreHelper,
    IServiceProvider serviceProvider,
    ILogger<EventDeployService> logger) : IEventDeployService
{
    // #934 — per-(planning, site) in-process deploy locks. Concurrent deploy
    // passes (the 5s StreamEventChanges poll, ListEvents one-shots, and the
    // several window/board requests a single client fires per sync) all call
    // into the deploy path for the same rotation. The idempotence guard keys on
    // the Compliance row, which DeployForRotationAsync writes LAST (after the
    // slow CaseCreateLocalOnly), so two passes that both clear the guard before
    // either writes Compliance each create a PlanningCase + PlanningCaseSite —
    // only Compliance has a duplicate-key catch, so the PlanningCaseSites
    // duplicate. That is the "N identical PlanningCaseSites within seconds"
    // symptom in #934. Serializing the check-then-deploy per (planning, site)
    // closes the window: the first pass writes Compliance before the next
    // re-checks the guard, so the next short-circuits.
    //
    // Keyed on (planning, site) — NOT (planning, site, rotation) — so the
    // dictionary stays bounded by planning×site and never grows with the
    // calendar window over the life of the process. Different rotations of the
    // same planning+site therefore serialize against each other, which is
    // harmless: the rotation-aware Compliance guard still lets each distinct
    // day deploy exactly once, and deploys only run for not-yet-deployed
    // candidates.
    //
    // In-process only: a mobile client's stream is pinned to one pod and the
    // racing window/ListEvents calls share that pod, so this covers the
    // reported scenario. Cross-pod races remain bounded by the Compliance
    // duplicate-key catch; making duplicates physically impossible across pods
    // would need the (PlanningId, MicrotingSdkSiteId, OccurrenceDate) DB unique
    // constraint #934 mentions (deferred — requires a base migration).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DeployLocks = new();

    private static async Task<IDisposable> AcquireDeployLockAsync(
        int planningId, int sdkSiteId, CancellationToken ct)
    {
        var gate = DeployLocks.GetOrAdd($"{planningId}:{sdkSiteId}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return new LockReleaser(gate);
    }

    private sealed class LockReleaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    public async Task EnsureDeployedAsync(
        string propertyId,
        IReadOnlyCollection<string> boardIds,
        string fromDateKey,
        string toDateKey,
        int sdkSiteId,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(propertyId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var propertyIdInt))
        {
            // Caller already validated this for the gRPC read path; defensive
            // log + no-op rather than throwing keeps the read side resilient.
            logger.LogWarning(
                "EventDeployService.EnsureDeployedAsync: ignoring non-numeric propertyId={PropertyId}",
                propertyId);
            return;
        }

        if (string.IsNullOrWhiteSpace(fromDateKey) || string.IsNullOrWhiteSpace(toDateKey))
        {
            logger.LogDebug(
                "EventDeployService.EnsureDeployedAsync: empty window ({From}..{To}); nothing to deploy",
                fromDateKey, toDateKey);
            return;
        }

        // Enumerate rotations via the same calendar service the read side
        // uses. ActionableOnly=false so we also see compliance rows that
        // already exist (which we then skip) AND recurrence-only rows
        // (which are the ones we deploy).
        var model = new CalendarTaskRequestModel
        {
            PropertyId = propertyIdInt,
            WeekStart = fromDateKey,
            WeekEnd = toDateKey,
            BoardIds = ParseBoardIds(boardIds),
            TagNames = [],
            SiteIds = [],
            ActionableOnly = false
        };

        var calendarService = serviceProvider.GetRequiredService<IBackendConfigurationCalendarService>();
        var calendarResult = await calendarService.GetTasksForWeek(model).ConfigureAwait(false);
        if (!calendarResult.Success || calendarResult.Model == null)
        {
            logger.LogWarning(
                "EventDeployService.EnsureDeployedAsync: calendar enumeration failed ({Message}); skipping deploy pass",
                calendarResult.Message);
            return;
        }

        // Today's UTC date — never back-deploy missed rotations (the
        // scheduler microservice owns historical deploys; we only fill in
        // future-day gaps the read side wants to surface).
        var todayUtc = DateTime.UtcNow.Date;

        // Compose the to-deploy list once so the per-row try/catch below can
        // skip non-deployable rows without nesting.
        var candidates = calendarResult.Model
            .Where(t => t.PlanningId.HasValue)
            .Where(t => t.EformId.HasValue && t.EformId.Value > 0)
            .Where(t => !t.IsFromCompliance
                        || t.SdkCaseId.GetValueOrDefault() == 0) // recurrence-only OR stuck Compliance (SdkCaseId not yet assigned)
            .Select(t => new
            {
                Task = t,
                RotationDate = DateTime.TryParseExact(
                    t.TaskDate, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var d)
                    ? d.Date
                    : (DateTime?)null
            })
            .Where(x => x.RotationDate.HasValue && x.RotationDate.Value >= todayUtc)
            .ToList();

        // Site-aware narrowing (defect A in #935). The candidate iteration above
        // is property-wide because GetTasksForWeek is property-wide; deploying
        // for plannings that have no PlanningSite for the calling worker is
        // both wasted work AND makes the (PlanningId, Deadline.Date) idempotence
        // guard mis-fire (the first caller's deploy "claims" the slot, the
        // assignee then skips deploy and never gets their own SDK case).
        // Stuck-row recovery is per-site by construction: cross-site cleanup
        // (e.g. when a worker is removed from PlanningSites between the
        // original deploy and recovery) belongs to the scheduler microservice.
        var candidatePlanningIds = candidates
            .Select(x => x.Task.PlanningId!.Value)
            .Distinct()
            .ToList();
        var planningIdsAssignedToSite = await itemsPlanningPnDbContext.PlanningSites
            .AsNoTracking()
            .Where(ps => candidatePlanningIds.Contains(ps.PlanningId)
                         && ps.SiteId == sdkSiteId
                         && ps.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(ps => ps.PlanningId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var assignedPlanningIdSet = new HashSet<int>(planningIdsAssignedToSite);

        // C4 — also retain plannings where this site qualifies through a worker
        // tag assigned to the event (not just explicit PlanningSites), so
        // not-yet-deployed occurrences materialise against live tag membership.
        // Cheap backend-config check first: only when some candidate event
        // actually carries worker tags do we touch the SDK core to resolve the
        // calling site's tag membership. This preserves the no-op / no-tag fast
        // paths, which must never reach coreHelper.GetCore().
        if (candidatePlanningIds.Count > 0)
        {
            var candidateArpIdsWithWorkerTags = await dbContext.AreaRulePlannings
                .AsNoTracking()
                .Where(arp => candidatePlanningIds.Contains(arp.ItemPlanningId)
                              && arp.WorkflowState != Constants.WorkflowStates.Removed
                              && arp.AreaRulePlanningWorkerTags.Any(wt =>
                                  wt.WorkflowState != Constants.WorkflowStates.Removed))
                .Select(arp => arp.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            if (candidateArpIdsWithWorkerTags.Count > 0)
            {
                var sdkCoreForTags = await coreHelper.GetCore().ConfigureAwait(false);
                await using var sdkDbContextForTags = sdkCoreForTags.DbContextHelper.GetDbContext();
                var siteTagIds = await sdkDbContextForTags.SiteTags
                    .AsNoTracking()
                    .Where(st => st.SiteId == sdkSiteId
                                 && st.TagId != null
                                 && st.WorkflowState != Constants.WorkflowStates.Removed)
                    .Select(st => st.TagId!.Value)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                if (siteTagIds.Count > 0)
                {
                    var planningIdsViaTag = await dbContext.AreaRulePlannings
                        .AsNoTracking()
                        .Where(arp => candidateArpIdsWithWorkerTags.Contains(arp.Id)
                                      && arp.ItemPlanningId > 0
                                      && arp.AreaRulePlanningWorkerTags.Any(wt =>
                                          wt.WorkflowState != Constants.WorkflowStates.Removed
                                          && siteTagIds.Contains(wt.TagId)))
                        .Select(arp => arp.ItemPlanningId)
                        .ToListAsync(cancellationToken).ConfigureAwait(false);

                    foreach (var pid in planningIdsViaTag) assignedPlanningIdSet.Add(pid);
                }
            }
        }

        candidates = candidates
            .Where(x => assignedPlanningIdSet.Contains(x.Task.PlanningId!.Value))
            .ToList();

        if (candidates.Count == 0)
        {
            logger.LogDebug(
                "EventDeployService.EnsureDeployedAsync: no future-day recurrence rows to deploy in window {From}..{To}",
                fromDateKey, toDateKey);
            return;
        }

        var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();

        var sdkSite = await sdkDbContext.Sites
            .FirstOrDefaultAsync(s => s.Id == sdkSiteId, cancellationToken)
            .ConfigureAwait(false);
        if (sdkSite == null)
        {
            logger.LogWarning(
                "EventDeployService.EnsureDeployedAsync: SDK site {SdkSiteId} not found; aborting deploy pass",
                sdkSiteId);
            return;
        }

        // Site.LanguageId is non-nullable; safe to look up directly.
        var language = await sdkDbContext.Languages
            .FirstOrDefaultAsync(l => l.Id == sdkSite.LanguageId, cancellationToken)
            .ConfigureAwait(false);
        if (language == null)
        {
            logger.LogWarning(
                "EventDeployService.EnsureDeployedAsync: language {LanguageId} for sdk site {SdkSiteId} not found; aborting deploy pass",
                sdkSite.LanguageId, sdkSiteId);
            return;
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var task = candidate.Task;
            var rotationDate = candidate.RotationDate!.Value;
            var planningId = task.PlanningId!.Value;
            var eformId = task.EformId!.Value;

            // #934 — serialize the check-then-deploy for this (planning, site)
            // so concurrent passes cannot both clear the Compliance guard before
            // either writes it and each create a duplicate PlanningCaseSite.
            // Disposed at the end of the iteration (incl. after `continue` and
            // the catch below), releasing the gate.
            using var deployLockHandle = await AcquireDeployLockAsync(planningId, sdkSiteId, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                // 1. Idempotence guard — Compliance natural key + SDK site.
                //    Mirrors EformParsedByServerHandler.cs:157-164 (compliance
                //    is keyed on PlanningId + Deadline), but ALSO scopes the
                //    "already deployed" decision to the calling worker's site
                //    (defect A in #935): without the site filter, the first
                //    caller wins the (PlanningId, Deadline) slot and writes a
                //    Compliance pointing at THEIR SDK case, then subsequent
                //    callers — including the planning's actual assignee — hit
                //    this guard and skip deploy, so they see the tile but the
                //    linked SDK case belongs to a different site and the eForm
                //    never opens.
                //
                //    Two-step query because Compliance.MicrotingSdkCaseId
                //    references the SDK DbContext's Cases table:
                //      (1) From BC: candidate SdkCaseIds for (planning, day).
                //      (2) From SDK: does any of those rows have SiteId == us?
                //
                //    The Removed-Compliance branch stays site-agnostic: a
                //    soft-removed row globally means "this rotation has been
                //    completed-then-retracted" (canonical complete-and-remove
                //    semantics, e.g. mobile CompleteEvent + admin retract);
                //    we never re-deploy that, regardless of which site
                //    originally completed it.
                var alreadyDeployed = await dbContext.Compliances
                    .AsNoTracking()
                    .AnyAsync(c =>
                            c.PlanningId == planningId
                            && c.Deadline.Date == rotationDate
                            && c.WorkflowState == Constants.WorkflowStates.Removed,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!alreadyDeployed)
                {
                    var candidateSdkCaseIds = await dbContext.Compliances
                        .AsNoTracking()
                        .Where(c =>
                            c.PlanningId == planningId
                            && c.Deadline.Date == rotationDate
                            && c.WorkflowState != Constants.WorkflowStates.Removed
                            && c.MicrotingSdkCaseId > 0)
                        .Select(c => c.MicrotingSdkCaseId)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (candidateSdkCaseIds.Count > 0)
                    {
                        alreadyDeployed = await sdkDbContext.Cases
                            .AsNoTracking()
                            .AnyAsync(sc =>
                                candidateSdkCaseIds.Contains(sc.Id)
                                && sc.SiteId.HasValue
                                && sc.SiteId.Value == sdkSiteId,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                if (alreadyDeployed)
                {
                    continue;
                }

                // 2. Resolve the Planning + AreaRulePlanning needed for the
                //    deploy. The calendar row carries PlanningId/EformId but
                //    not AreaId/PropertyId for Compliance.
                var planning = await itemsPlanningPnDbContext.Plannings
                    .FirstOrDefaultAsync(p =>
                            p.Id == planningId
                            && p.WorkflowState != Constants.WorkflowStates.Removed,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (planning == null)
                {
                    logger.LogWarning(
                        "EventDeployService: planning {PlanningId} not found; skipping rotation {Rotation}",
                        planningId, rotationDate);
                    continue;
                }

                var areaRulePlanning = await dbContext.AreaRulePlannings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(arp =>
                            arp.ItemPlanningId == planningId
                            && arp.WorkflowState != Constants.WorkflowStates.Removed,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (areaRulePlanning == null)
                {
                    logger.LogWarning(
                        "EventDeployService: areaRulePlanning for planning {PlanningId} not found; skipping rotation {Rotation}",
                        planningId, rotationDate);
                    continue;
                }

                // 3-8. PlanningCase + PlanningCaseSite + SDK Case + Compliance.
                //      Extracted so the on-demand calendar materialisation path
                //      (EnsureComplianceForOccurrenceAsync) reuses byte-for-byte
                //      the same writes in the same order.
                await DeployForRotationAsync(
                        areaRulePlanning,
                        planning,
                        rotationDate,
                        eformId,
                        sdkSiteId,
                        sdkCore,
                        sdkDbContext,
                        sdkSite,
                        language,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation must abort the whole pass — not be swallowed as a
                // per-rotation failure by the general catch below. (The lock is
                // already released by the `using` as the exception unwinds.)
                throw;
            }
            catch (Exception ex)
            {
                // Defect B in #935 — recoverable per-rotation failure. The
                // stuck-row recovery path at the candidate filter above
                // (`!t.IsFromCompliance || t.SdkCaseId == 0`) will redeploy on
                // the next sync, so log at Warning with structured fields
                // (exception type + planning + rotation + site) for
                // observability without spamming Error stack traces.
                logger.LogWarning(ex,
                    "EventDeployService: per-rotation deploy threw {ExceptionType} for planningId={PlanningId} rotationDate={RotationDate} sdkSiteId={SdkSiteId} — continuing; recovery on next sync via stuck-row branch.",
                    ex.GetType().Name, planningId, rotationDate, sdkSiteId);
                // continue — do not abort the whole pass
            }
        }
    }

    /// <summary>
    /// Single source of truth for "create PlanningCase + PlanningCaseSite +
    /// SDK Case + Compliance row for one (planning, rotationDate, sdkSite)
    /// tuple". Used by both the nightly window deploy
    /// (<see cref="EnsureDeployedAsync"/>) and the on-demand calendar
    /// materialisation path
    /// (<see cref="EnsureComplianceForOccurrenceAsync"/>).
    ///
    /// Preserves the historical write order: PlanningCase → PlanningCaseSite
    /// → CaseCreate → Update PlanningCaseSite → EnsureComplianceRowAsync.
    /// Returns the ids of the created compliance + SDK case so the on-demand
    /// caller can hand them back to the calendar UI without a re-query.
    /// </summary>
    private async Task<(int ComplianceId, int SdkCaseId, int TemplateId)> DeployForRotationAsync(
        AreaRulePlanning areaRulePlanning,
        Planning planning,
        DateTime rotationDate,
        int eformId,
        int sdkSiteId,
        SdkCore sdkCore,
        SdkDbContext sdkDbContext,
        SdkSite sdkSite,
        SdkLanguage language,
        CancellationToken ct)
    {
        // 0. Defense-in-depth site-assignment guard (#932 / #1377). This is the
        //    single source of truth for every PlanningCaseSite write, so it is
        //    the right place to refuse a cross-worker leak. A PlanningCaseSite
        //    must only ever be written for a site that is legitimately tied to
        //    this event — either the task's own (non-removed) PlanningSites, OR
        //    an active worker of the event's PROPERTY (PropertyWorkers).
        //
        //    The on-demand calendar materialisation now lets a user complete a
        //    future/on-demand occurrence on behalf of ANY active property worker
        //    (the worker pickers list every property worker, same source as
        //    GetLinkedSites). Such a worker may not be in PlanningSites, so the
        //    guard accepts the property-worker case too — while still refusing a
        //    site that is neither, so a stray id can never leak a case to an
        //    unrelated worker.
        //
        //    The two callers uphold this: EnsureDeployedAsync site-narrows its
        //    candidates to PlanningSites (#935 defect A) and the on-demand
        //    EnsureComplianceForOccurrenceAsync caller validates the site is an
        //    active property worker. If either invariant ever regresses, fail
        //    loud here rather than silently deploying a case to an unrelated
        //    worker (the exact symptom #932 reports). In EnsureDeployedAsync this
        //    throw is swallowed by the per-rotation try/catch and recovered on
        //    the next sync; on the on-demand path it surfaces the bug to the
        //    caller.
        //
        //    Cost: at most two indexed AnyAsync (the PropertyWorkers probe is
        //    only evaluated when the site is not already a PlanningSite). Both
        //    are negligible against the SDK ReadeForm + CaseCreateLocalOnly
        //    round-trips that follow, and this method only runs for candidates
        //    that survived the idempotence guard (i.e. actually need deploying).
        var siteIsAssignedToPlanning = await itemsPlanningPnDbContext.PlanningSites
            .AsNoTracking()
            .AnyAsync(ps =>
                    ps.PlanningId == planning.Id
                    && ps.SiteId == sdkSiteId
                    && ps.WorkflowState != Constants.WorkflowStates.Removed,
                ct)
            .ConfigureAwait(false);
        if (!siteIsAssignedToPlanning)
        {
            var siteIsActivePropertyWorker = await dbContext.PropertyWorkers
                .AsNoTracking()
                .AnyAsync(pw =>
                        pw.PropertyId == areaRulePlanning.PropertyId
                        && pw.WorkerId == sdkSiteId
                        && pw.WorkflowState != Constants.WorkflowStates.Removed,
                    ct)
                .ConfigureAwait(false);
            var siteIsWorkerTagMember = false;
            if (!siteIsActivePropertyWorker)
            {
                // Also accept a site that is a live member of any worker tag
                // assigned to THIS event (C4): not-yet-deployed occurrences
                // must materialise against live tag membership, even when the
                // site is neither an explicit PlanningSite nor a property worker.
                var eventTagIds = await dbContext.AreaRulePlanningWorkerTags
                    .AsNoTracking()
                    .Where(wt => wt.AreaRulePlanningId == areaRulePlanning.Id
                                 && wt.WorkflowState != Constants.WorkflowStates.Removed)
                    .Select(wt => wt.TagId)
                    .ToListAsync(ct).ConfigureAwait(false);
                if (eventTagIds.Count > 0)
                {
                    siteIsWorkerTagMember = await sdkDbContext.SiteTags
                        .AsNoTracking()
                        .AnyAsync(st => st.SiteId == sdkSiteId
                                        && st.TagId != null && eventTagIds.Contains(st.TagId.Value)
                                        && st.WorkflowState != Constants.WorkflowStates.Removed, ct)
                        .ConfigureAwait(false);
                }
            }
            if (!siteIsActivePropertyWorker && !siteIsWorkerTagMember)
            {
                throw new InvalidOperationException(
                    $"EventDeployService refused to deploy planning {planning.Id} to sdkSiteId {sdkSiteId}: "
                    + "the site is neither in the planning's (non-removed) PlanningSites, an active "
                    + $"worker of property {areaRulePlanning.PropertyId}, nor a worker-tag member of "
                    + $"event {areaRulePlanning.Id}. Deploying here would leak a case "
                    + "to an unrelated worker (#932/#1377).");
            }
        }

        // 3. Resolve / create PlanningCase.
        //    Mirrors ItemCaseCreateHandler.cs:83-89, scoped to the
        //    rotation we're deploying (one PlanningCase per
        //    rotation deploy). We do NOT retract sibling PlanningCases
        //    here because we're filling a future-day gap, not
        //    re-deploying — the scheduler microservice owns that.
        var planningCase = new PlanningCase
        {
            PlanningId = planning.Id,
            Status = 66,
            MicrotingSdkeFormId = eformId
        };
        await planningCase.Create(itemsPlanningPnDbContext).ConfigureAwait(false);

        // 4. Resolve / create PlanningCaseSite.
        //    Mirrors ItemCaseCreateHandler.cs:179-194.
        var planningCaseSite = new PlanningCaseSite
        {
            MicrotingSdkSiteId = sdkSiteId,
            MicrotingSdkeFormId = eformId,
            Status = 66,
            PlanningId = planning.Id,
            PlanningCaseId = planningCase.Id
        };
        await planningCaseSite.Create(itemsPlanningPnDbContext).ConfigureAwait(false);

        // 5. SDK case idempotence guard — mirrors
        //    ItemCaseCreateHandler.cs:205. A freshly-created
        //    PlanningCaseSite has MicrotingSdkCaseId == 0, so this
        //    branch is taken on the deploy path.
        if (planningCaseSite.MicrotingSdkCaseId >= 1)
        {
            // Still ensure the Compliance row exists for this rotation
            // before returning.
            var existingRow = await EnsureComplianceRowAsync(
                    areaRulePlanning,
                    planning,
                    rotationDate,
                    planningCaseSite,
                    ct)
                .ConfigureAwait(false);
            return (
                existingRow?.Id ?? 0,
                planningCaseSite.MicrotingSdkCaseId,
                eformId);
        }

        // 6. Build mainElement. Mirrors ItemCaseCreateHandler.cs:113-153.
        //    KEY DIFFERENCE: EndDate is the rotation we're deploying
        //    (not planning.NextExecutionTime), so backfill of a future
        //    rotation date stays bounded to that day.
        var mainElement = await sdkCore.ReadeForm(eformId, language).ConfigureAwait(false);

        var planningNameTranslation = await itemsPlanningPnDbContext.PlanningNameTranslation
            .FirstOrDefaultAsync(x =>
                    x.LanguageId == language.Id && x.PlanningId == planning.Id,
                ct)
            .ConfigureAwait(false);
        var translation = planningNameTranslation?.Name;

        string folderId = string.Empty;
        if (planning.SdkFolderId.HasValue)
        {
            var folder = await sdkDbContext.Folders
                .FirstOrDefaultAsync(x => x.Id == planning.SdkFolderId.Value, ct)
                .ConfigureAwait(false);
            folderId = folder?.MicrotingUid?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }

        mainElement.Label = string.IsNullOrEmpty(planning.PlanningNumber) ? "" : planning.PlanningNumber;
        mainElement.StartDate = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(translation))
        {
            mainElement.Label += string.IsNullOrEmpty(mainElement.Label) ? $"{translation}" : $" - {translation}";
        }
        if (!string.IsNullOrEmpty(planning.BuildYear))
        {
            mainElement.Label += string.IsNullOrEmpty(mainElement.Label) ? $"{planning.BuildYear}" : $" - {planning.BuildYear}";
        }
        if (!string.IsNullOrEmpty(planning.Type))
        {
            mainElement.Label += string.IsNullOrEmpty(mainElement.Label) ? $"{planning.Type}" : $" - {planning.Type}";
        }

        if (mainElement.ElementList.Count == 1)
        {
            mainElement.ElementList[0].Label = mainElement.Label;
        }

        mainElement.CheckListFolderName = folderId;
        // EndDate must be in the future for CaseCreate to be accepted.
        // For nightly deploys, rotationDate is always today-or-later (the
        // candidate filter at line 132-ish enforces that). For on-demand
        // calendar materialisation (EnsureComplianceForOccurrenceAsync),
        // the user may click an event whose rotation date is in the past
        // — in that case clamp the EndDate to end-of-tomorrow UTC so the
        // SDK accepts the case, while keeping Compliance.Deadline at the
        // true rotationDate (the column is set inside
        // EnsureComplianceRowAsync independently of mainElement.EndDate).
        var endOfRotationDay = rotationDate.AddDays(1).AddTicks(-1);
        var endOfTomorrow = DateTime.UtcNow.Date.AddDays(2).AddTicks(-1);
        mainElement.EndDate = endOfRotationDay > endOfTomorrow ? endOfRotationDay : endOfTomorrow;

        // CaseCreateLocalOnly returns the SDK Case.Id directly (no
        // MicrotingUid → Id lookup needed) AND skips the cloud XML
        // deploy that the standard CaseCreate path performs. Mirrors
        // the fix from PR #829.
        var caseId = await sdkCore.CaseCreateLocalOnly(
            mainElement, "", (int)sdkSite.MicrotingUid!, null)
            .ConfigureAwait(false);

        if (caseId != null)
        {
            planningCaseSite.MicrotingSdkCaseId = (int)caseId;
            await planningCaseSite.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
        }

        // 8. Compliance row. Mirrors EformParsedByServerHandler.cs:170-182.
        var created = await EnsureComplianceRowAsync(
                areaRulePlanning,
                planning,
                rotationDate,
                planningCaseSite,
                ct)
            .ConfigureAwait(false);

        return (
            created?.Id ?? 0,
            planningCaseSite.MicrotingSdkCaseId,
            eformId);
    }

    public async Task<EnsureComplianceResult?> EnsureComplianceForOccurrenceAsync(
        AreaRulePlanning areaRulePlanning,
        DateTime deadline,
        int sdkSiteId,
        CancellationToken cancellationToken = default)
    {
        if (areaRulePlanning == null)
        {
            return null;
        }

        var deadlineDate = deadline.Date;

        // #934 — same per-(planning, site) serialization as the nightly path,
        // so an on-demand calendar materialisation racing the eager-deploy poll
        // for the same rotation cannot double-create a PlanningCaseSite. Held
        // for the whole check-then-deploy and released on any return/throw.
        using var deployLockHandle = await AcquireDeployLockAsync(
            areaRulePlanning.ItemPlanningId, sdkSiteId, cancellationToken).ConfigureAwait(false);

        // Site-aware idempotence: only treat this occurrence as already
        // materialised for THIS site if a non-removed compliance row exists
        // whose backing SDK case belongs to sdkSiteId. A row for a *different*
        // site must NOT short-circuit deployment of this site (retroactive
        // worker-tag add path).
        var candidateComplianceRows = await dbContext.Compliances
            .AsNoTracking()
            .Where(c =>
                c.PlanningId == areaRulePlanning.ItemPlanningId
                && c.Deadline.Date == deadlineDate
                && c.WorkflowState != Constants.WorkflowStates.Removed
                && c.MicrotingSdkCaseId > 0)
            .Select(c => new { c.Id, c.MicrotingSdkCaseId, c.MicrotingSdkeFormId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (candidateComplianceRows.Count > 0)
        {
            var candidateCaseIds = candidateComplianceRows.Select(x => x.MicrotingSdkCaseId).ToList();
            var sdkCoreForGuard = await coreHelper.GetCore().ConfigureAwait(false);
            await using var sdkDbContextForGuard = sdkCoreForGuard.DbContextHelper.GetDbContext();
            var matchedCaseId = await sdkDbContextForGuard.Cases
                .AsNoTracking()
                .Where(sc => candidateCaseIds.Contains(sc.Id)
                             && sc.SiteId.HasValue
                             && sc.SiteId.Value == sdkSiteId)
                .Select(sc => sc.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (matchedCaseId != 0)
            {
                var existing = candidateComplianceRows.First(x => x.MicrotingSdkCaseId == matchedCaseId);
                return new EnsureComplianceResult
                {
                    Created = false,
                    ComplianceId = existing.Id,
                    SdkCaseId = existing.MicrotingSdkCaseId,
                    TemplateId = existing.MicrotingSdkeFormId
                };
            }
        }

        var planning = await itemsPlanningPnDbContext.Plannings
            .FirstOrDefaultAsync(p =>
                    p.Id == areaRulePlanning.ItemPlanningId
                    && p.WorkflowState != Constants.WorkflowStates.Removed,
                cancellationToken)
            .ConfigureAwait(false);
        if (planning == null)
        {
            logger.LogWarning(
                "EventDeployService.EnsureComplianceForOccurrenceAsync: planning {PlanningId} not found for ARP {AreaRulePlanningId}",
                areaRulePlanning.ItemPlanningId, areaRulePlanning.Id);
            return null;
        }

        var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();

        var sdkSite = await sdkDbContext.Sites
            .FirstOrDefaultAsync(s => s.Id == sdkSiteId, cancellationToken)
            .ConfigureAwait(false);
        if (sdkSite == null)
        {
            logger.LogWarning(
                "EventDeployService.EnsureComplianceForOccurrenceAsync: SDK site {SdkSiteId} not found",
                sdkSiteId);
            return null;
        }

        var language = await sdkDbContext.Languages
            .FirstOrDefaultAsync(l => l.Id == sdkSite.LanguageId, cancellationToken)
            .ConfigureAwait(false);
        if (language == null)
        {
            logger.LogWarning(
                "EventDeployService.EnsureComplianceForOccurrenceAsync: language {LanguageId} for sdk site {SdkSiteId} not found",
                sdkSite.LanguageId, sdkSiteId);
            return null;
        }

        // EformId source matches the nightly path's task.EformId (which is
        // arp.AreaRule.EformId) — when AreaRule is loaded prefer that,
        // otherwise fall back to planning.RelatedEFormId. Both are set from
        // the same upstream value at task-wizard creation.
        var eformId = areaRulePlanning.AreaRule?.EformId is { } eid && eid > 0
            ? eid
            : planning.RelatedEFormId;

        if (eformId <= 0)
        {
            logger.LogWarning(
                "EventDeployService.EnsureComplianceForOccurrenceAsync: no usable EformId for ARP {AreaRulePlanningId} (planning {PlanningId}); skipping deploy",
                areaRulePlanning.Id, planning.Id);
            return null;
        }

        var (complianceId, sdkCaseId, templateId) = await DeployForRotationAsync(
                areaRulePlanning,
                planning,
                deadlineDate,
                eformId,
                sdkSiteId,
                sdkCore,
                sdkDbContext,
                sdkSite,
                language,
                cancellationToken)
            .ConfigureAwait(false);

        return new EnsureComplianceResult
        {
            Created = true,
            ComplianceId = complianceId,
            SdkCaseId = sdkCaseId,
            TemplateId = templateId
        };
    }

    private async Task<Compliance?> EnsureComplianceRowAsync(
        AreaRulePlanning areaRulePlanning,
        Planning planning,
        DateTime rotationDate,
        PlanningCaseSite planningCaseSite,
        CancellationToken cancellationToken)
    {
        // Defect B in #935 — refuse to write a Compliance row when the
        // PlanningCaseSite has no SDK case linkage (MicrotingSdkCaseId == 0).
        // The pre-existing code wrote a poisoned Compliance row whenever
        // Core.CaseCreateLocalOnly returned null inside DeployForRotationAsync,
        // and that poisoned row then satisfied the (PlanningId, Deadline.Date)
        // idempotence guard so subsequent syncs skipped redeploy and the user
        // saw a non-functional event tile. With this guard, the stuck-row
        // recovery branch in EnsureDeployedAsync (candidate filter
        // `!t.IsFromCompliance || t.SdkCaseId == 0`) is the only path that
        // ever produces a Compliance, and it only does so after a real SDK
        // case is in place.
        if (planningCaseSite.MicrotingSdkCaseId <= 0)
        {
            logger.LogWarning(
                "EventDeployService: skipping Compliance write — PlanningCaseSite {Id} has no SDK case linkage (planningId={PlanningId}, rotationDate={RotationDate}). The stuck-row recovery path will retry on next sync.",
                planningCaseSite.Id, planning.Id, rotationDate);
            return null;
        }

        // Race protection lives in the duplicate-key catch below (mirrors
        // EformParsedByServerHandler.cs:185-196). The outer idempotence guard
        // in EnsureDeployedAsync already filters out the common case before
        // any writes happen, so a second AnyAsync here would only add a DB
        // round-trip without changing behaviour.

        // The handler uses `planning.LastExecutedTime` for StartDate. For an
        // eager deploy that has not actually run yet, LastExecutedTime is the
        // scheduler's previous-rotation marker; fall back to UtcNow when it
        // is null so the StartDate column stays populated.
        var startDate = planning.LastExecutedTime ?? DateTime.UtcNow;

        var compliance = new Compliance
        {
            PropertyId = areaRulePlanning.PropertyId,
            PlanningId = planning.Id,
            AreaId = areaRulePlanning.AreaId,
            Deadline = new DateTime(rotationDate.Year, rotationDate.Month, rotationDate.Day, 0, 0, 0),
            StartDate = startDate,
            MicrotingSdkeFormId = planning.RelatedEFormId,
            MicrotingSdkCaseId = planningCaseSite.MicrotingSdkCaseId,
            // The handler mistakenly stores PlanningCaseId here (named
            // PlanningCaseSiteId on the column) — see
            // EformParsedByServerHandler.cs:179. Preserve that convention
            // so the round-trip matches the JSON oracle path.
            PlanningCaseSiteId = planningCaseSite.PlanningCaseId
        };

        try
        {
            await compliance.Create(dbContext).ConfigureAwait(false);
            return compliance;
        }
        catch (Exception ex)
        {
            // Duplicate-key races are tolerated — mirrors
            // EformParsedByServerHandler.cs:185-196.
            if (ex.InnerException is { HResult: -2147467259 })
            {
                logger.LogInformation(
                    "EventDeployService: compliance for planning {PlanningId} deadline {Deadline} already exists (race) — fetching winning row",
                    planning.Id, rotationDate);

                // Detach the failed-INSERT entity so the SaveChanges that
                // happens inside the revive's existing.Update(...) below
                // does not retry the same INSERT and re-hit the duplicate
                // key. EF Core leaves a failed Add tracked as Added until
                // explicitly detached.
                var addedEntry = dbContext.Entry(compliance);
                if (addedEntry.State == EntityState.Added)
                {
                    addedEntry.State = EntityState.Detached;
                }

                // Tracked (NOT AsNoTracking) so we can revive a half-deployed
                // row in place when this call has just produced a fresh SDK case.
                var existing = await dbContext.Compliances
                    .FirstOrDefaultAsync(c =>
                            c.PlanningId == planning.Id
                            && c.Deadline.Date == rotationDate.Date
                            && c.WorkflowState != Constants.WorkflowStates.Removed,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (existing != null
                    && existing.MicrotingSdkCaseId <= 0
                    && planningCaseSite.MicrotingSdkCaseId > 0)
                {
                    // Half-deployed row found AND we have a fresh SDK case from this
                    // call. Adopt the new SDK case to avoid orphaning it. Never
                    // overwrite a row that already has a valid SDK case id (that
                    // would be a real race winner; let them keep it).
                    existing.MicrotingSdkCaseId = planningCaseSite.MicrotingSdkCaseId;
                    existing.MicrotingSdkeFormId = planning.RelatedEFormId;
                    await existing.Update(dbContext).ConfigureAwait(false);
                }

                return existing;
            }
            throw;
        }
    }

    private static List<int> ParseBoardIds(IReadOnlyCollection<string> boardIds)
    {
        if (boardIds == null || boardIds.Count == 0) return [];
        var seen = new HashSet<int>();
        var result = new List<int>();
        foreach (var raw in boardIds)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                && seen.Add(id))
            {
                result.Add(id);
            }
        }
        return result;
    }
}
