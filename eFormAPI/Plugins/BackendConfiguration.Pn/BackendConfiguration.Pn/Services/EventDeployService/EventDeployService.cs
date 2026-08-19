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
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using SdkCore = eFormCore.Core;
using SdkDbContext = Microting.eForm.Infrastructure.MicrotingDbContext;
using SdkSite = Microting.eForm.Infrastructure.Data.Entities.Site;
using SdkLanguage = Microting.eForm.Infrastructure.Data.Entities.Language;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;

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

    // Canonical "case is answered" marker — same constant the calendar's
    // complete path writes (BackendConfigurationCalendarService:3310) and the
    // assignment reconciliation reads.
    private const int CompletedStatus = 100;

    // #1378 — upper bound on the best-effort cloud CaseDelete performed by
    // RetractSdkCaseAsync. Core.CaseDelete's "Parsing in progress" retry loop is
    // unbounded in practice (Thread.Sleep(i * 5000) for i = 1..101, ~7 hours),
    // and the eForm repair pass runs synchronously inside the HTTP save.
    private static readonly TimeSpan CloudCaseDeleteTimeout = TimeSpan.FromSeconds(30);

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
                        // WorkflowState != Removed matters: a case that was
                        // soft-removed (Core.CaseDeleteResult — what the eForm
                        // repair pass and every retraction path use) is NOT a
                        // deployment any more, and a Compliance row still
                        // pointing at it must not keep this site from
                        // re-deploying. Retracted is deliberately NOT excluded:
                        // in SDK semantics a retracted case is a COMPLETED one
                        // (SqlController.CaseReadByCaseId maps Retracted ->
                        // "Completed"), which must keep the guard true.
                        alreadyDeployed = await sdkDbContext.Cases
                            .AsNoTracking()
                            .AnyAsync(sc =>
                                candidateSdkCaseIds.Contains(sc.Id)
                                && sc.SiteId.HasValue
                                && sc.SiteId.Value == sdkSiteId
                                && sc.WorkflowState != Constants.WorkflowStates.Removed,
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
        //
        //    The probe itself lives in ResolveSiteLinkageAsync so the eForm
        //    repair pass (which creates SDK cases through
        //    CreateSdkCaseForRotationAsync WITHOUT going through this method)
        //    is protected by the very same check — see SwapCaseEformAsync.
        var linkage = await ResolveSiteLinkageAsync(
                areaRulePlanning,
                planning.Id,
                sdkSiteId,
                sdkDbContext,
                acceptPropertyWorker: true,
                ct)
            .ConfigureAwait(false);
        if (linkage == SiteEventLinkage.None)
        {
            throw new InvalidOperationException(
                $"EventDeployService refused to deploy planning {planning.Id} to sdkSiteId {sdkSiteId}: "
                + "the site is neither in the planning's (non-removed) PlanningSites, an active "
                + $"worker of property {areaRulePlanning.PropertyId}, nor a worker-tag member of "
                + $"event {areaRulePlanning.Id}. Deploying here would leak a case "
                + "to an unrelated worker (#932/#1377).");
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
                    eformId,
                    ct)
                .ConfigureAwait(false);
            return (
                existingRow?.Id ?? 0,
                planningCaseSite.MicrotingSdkCaseId,
                eformId);
        }

        // 6-7. Build mainElement + create the SDK case. Extracted so the eForm
        //      repair pass (RepairEformForOpenOccurrencesAsync) produces a
        //      byte-for-byte identical case for the replacement eForm.
        var caseId = await CreateSdkCaseForRotationAsync(
                planning,
                rotationDate,
                eformId,
                sdkCore,
                sdkDbContext,
                sdkSite,
                language,
                ct)
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
                eformId,
                ct)
            .ConfigureAwait(false);

        return (
            created?.Id ?? 0,
            planningCaseSite.MicrotingSdkCaseId,
            eformId);
    }

    /// <summary>
    /// Which live linkage — if any — ties a site to an event. Returned by
    /// <see cref="ResolveSiteLinkageAsync"/>; <see cref="None"/> means the site
    /// must never receive (or keep) a case for this event.
    /// </summary>
    private enum SiteEventLinkage
    {
        /// <summary>No live linkage at all.</summary>
        None = 0,

        /// <summary>A non-removed items-planning <c>PlanningSite</c> — the site is an explicit assignee.</summary>
        PlanningSite = 1,

        /// <summary>A live member of a worker tag assigned to the event (C4).</summary>
        WorkerTag = 2,

        /// <summary>
        /// Only an active worker of the event's PROPERTY. Enough to DEPLOY to
        /// (the on-demand "complete on behalf of any property worker" flow),
        /// but NOT an assignee of this event — see
        /// <see cref="SwapCaseEformAsync"/> for why the repair pass refuses it.
        /// </summary>
        PropertyWorker = 4
    }

    /// <summary>
    /// Single source of truth for "is this site legitimately tied to this
    /// event?" (#932 / #1377). Probes are ordered cheapest-and-most-common
    /// first and short-circuit, so the common assignee case costs one indexed
    /// <c>AnyAsync</c>.
    /// </summary>
    /// <param name="acceptPropertyWorker">
    /// <c>true</c> for the deploy path, which must also accept a bare active
    /// property worker (the worker pickers list every property worker, so an
    /// on-demand materialisation may legitimately target one). <c>false</c> for
    /// the eForm repair pass, which asks the stricter question "is this site
    /// still a live ASSIGNEE of the event?" — a site that was just unassigned
    /// stays an active PropertyWorker, so accepting that linkage there would
    /// hand the removed worker a brand-new case.
    /// </param>
    private async Task<SiteEventLinkage> ResolveSiteLinkageAsync(
        AreaRulePlanning areaRulePlanning,
        int planningId,
        int sdkSiteId,
        SdkDbContext sdkDbContext,
        bool acceptPropertyWorker,
        CancellationToken ct)
    {
        var siteIsAssignedToPlanning = await itemsPlanningPnDbContext.PlanningSites
            .AsNoTracking()
            .AnyAsync(ps =>
                    ps.PlanningId == planningId
                    && ps.SiteId == sdkSiteId
                    && ps.WorkflowState != Constants.WorkflowStates.Removed,
                ct)
            .ConfigureAwait(false);
        if (siteIsAssignedToPlanning)
        {
            return SiteEventLinkage.PlanningSite;
        }

        if (acceptPropertyWorker)
        {
            var siteIsActivePropertyWorker = await dbContext.PropertyWorkers
                .AsNoTracking()
                .AnyAsync(pw =>
                        pw.PropertyId == areaRulePlanning.PropertyId
                        && pw.WorkerId == sdkSiteId
                        && pw.WorkflowState != Constants.WorkflowStates.Removed,
                    ct)
                .ConfigureAwait(false);
            if (siteIsActivePropertyWorker)
            {
                return SiteEventLinkage.PropertyWorker;
            }
        }

        // A site that is a live member of any worker tag assigned to THIS event
        // (C4): occurrences must materialise against live tag membership, even
        // when the site is neither an explicit PlanningSite nor a property
        // worker.
        var eventTagIds = await dbContext.AreaRulePlanningWorkerTags
            .AsNoTracking()
            .Where(wt => wt.AreaRulePlanningId == areaRulePlanning.Id
                         && wt.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(wt => wt.TagId)
            .ToListAsync(ct).ConfigureAwait(false);
        if (eventTagIds.Count > 0)
        {
            var siteIsWorkerTagMember = await sdkDbContext.SiteTags
                .AsNoTracking()
                .AnyAsync(st => st.SiteId == sdkSiteId
                                && st.TagId != null && eventTagIds.Contains(st.TagId.Value)
                                && st.WorkflowState != Constants.WorkflowStates.Removed, ct)
                .ConfigureAwait(false);
            if (siteIsWorkerTagMember)
            {
                return SiteEventLinkage.WorkerTag;
            }
        }

        return SiteEventLinkage.None;
    }

    /// <summary>
    /// Per-pass memo for everything <see cref="CreateSdkCaseForRotationAsync"/>
    /// resolves that is invariant across the cases of a single repair pass:
    /// the ReadeForm result per (eForm, language), the planning's name
    /// translation per language and the planning's SDK folder uid.
    ///
    /// Only the eForm repair pass supplies one — the deploy paths create at
    /// most one case per call, so caching there would only add allocations.
    /// </summary>
    private sealed class RotationElementCache
    {
        public readonly Dictionary<(int EformId, int LanguageId), MainElement> MainElements = new();
        public readonly Dictionary<int, string> Translations = new();
        public string FolderId;
        public bool FolderResolved;
    }

    /// <summary>
    /// Builds the mainElement for one (planning, rotationDate, eform, site)
    /// tuple and creates the backing SDK case. Shared by
    /// <see cref="DeployForRotationAsync"/> (first deploy) and
    /// <see cref="RepairEformForOpenOccurrencesAsync"/> (redeploy after the
    /// event's eForm was changed), so a repaired occurrence is indistinguishable
    /// from one deployed with the new eForm in the first place.
    ///
    /// Returns the SDK <c>Case.Id</c>, or <c>null</c> when
    /// <c>CaseCreateLocalOnly</c> refused to create the case.
    /// </summary>
    private async Task<int?> CreateSdkCaseForRotationAsync(
        Planning planning,
        DateTime rotationDate,
        int eformId,
        SdkCore sdkCore,
        SdkDbContext sdkDbContext,
        SdkSite sdkSite,
        SdkLanguage language,
        CancellationToken ct,
        RotationElementCache cache = null)
    {
        // 6. Build mainElement. Mirrors ItemCaseCreateHandler.cs:113-153.
        //    KEY DIFFERENCE: EndDate is the rotation we're deploying
        //    (not planning.NextExecutionTime), so backfill of a future
        //    rotation date stays bounded to that day.
        //
        //    ReadeForm rebuilds the whole element tree from the SDK DB and is
        //    keyed only on (eForm, language), so a repair pass sweeping N cases
        //    would otherwise pay for it N times. The cached instance is never
        //    handed out directly: the block below MUTATES Label / dates /
        //    CheckListFolderName, so each call gets its own MainElement header
        //    via the copy constructor. The shared ElementList underneath is
        //    only ever written with the same pass-invariant label, and
        //    CaseCreateLocalOnly reads nothing but Id/Repeated/StartDate/EndDate.
        MainElement mainElement;
        if (cache != null && cache.MainElements.TryGetValue((eformId, language.Id), out var cachedElement))
        {
            mainElement = new MainElement(cachedElement);
        }
        else
        {
            mainElement = await sdkCore.ReadeForm(eformId, language).ConfigureAwait(false);
            if (cache != null)
            {
                cache.MainElements[(eformId, language.Id)] = new MainElement(mainElement);
            }
        }

        string translation;
        if (cache != null && cache.Translations.TryGetValue(language.Id, out var cachedTranslation))
        {
            translation = cachedTranslation;
        }
        else
        {
            var planningNameTranslation = await itemsPlanningPnDbContext.PlanningNameTranslation
                .FirstOrDefaultAsync(x =>
                        x.LanguageId == language.Id && x.PlanningId == planning.Id,
                    ct)
                .ConfigureAwait(false);
            translation = planningNameTranslation?.Name;
            if (cache != null)
            {
                cache.Translations[language.Id] = translation;
            }
        }

        string folderId;
        if (cache is { FolderResolved: true })
        {
            folderId = cache.FolderId;
        }
        else
        {
            folderId = string.Empty;
            if (planning.SdkFolderId.HasValue)
            {
                var folder = await sdkDbContext.Folders
                    .FirstOrDefaultAsync(x => x.Id == planning.SdkFolderId.Value, ct)
                    .ConfigureAwait(false);
                folderId = folder?.MicrotingUid?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            }
            if (cache != null)
            {
                cache.FolderId = folderId;
                cache.FolderResolved = true;
            }
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
        return await sdkCore.CaseCreateLocalOnly(
                mainElement, "", (int)sdkSite.MicrotingUid!, null)
            .ConfigureAwait(false);
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

    // #1378 — the repair pass runs SYNCHRONOUSLY inside the HTTP save (an
    // explicit product decision). The calendar deploy path never retracts
    // sibling PlanningCaseSites, so a long-lived daily event accumulates one
    // live (rotation x site) row after another, and every open one costs a
    // cloud CaseDelete + a local CaseDeleteResult + a ReadeForm + a CaseCreate.
    // Core.CaseDelete additionally has a blocking retry loop
    // (Thread.Sleep(i * 5000) for i = 1..101) whenever the server answers
    // "Parsing in progress: Can not delete check list!", so a single unlucky
    // case can stall the request for a very long time.
    //
    // We deliberately do NOT cap the pass: silently skipping cases would leave
    // workers on the OLD eForm, i.e. exactly the bug this pass exists to fix.
    // Instead the size of every pass is made observable — the count is logged
    // unconditionally and a pass above this threshold logs a structured
    // warning, so an oversized event shows up before it becomes a timeout.
    private const int LargeRepairPassWarningThreshold = 50;

    public async Task RepairEformForOpenOccurrencesAsync(
        AreaRulePlanning areaRulePlanning,
        int oldEformId,
        int newEformId,
        CancellationToken cancellationToken = default)
    {
        if (areaRulePlanning == null || newEformId <= 0 || oldEformId == newEformId)
        {
            return;
        }

        var planningId = areaRulePlanning.ItemPlanningId;
        if (planningId <= 0)
        {
            return;
        }

        // Tracked (NOT AsNoTracking): these rows are re-pointed IN PLACE below.
        // The calendar UI holds complianceId and the compliance view depends on
        // row stability, so a Compliance row is never deleted and recreated —
        // only its MicrotingSdkCaseId / MicrotingSdkeFormId change.
        var complianceRows = await dbContext.Compliances
            .Where(c => c.PlanningId == planningId
                        && c.WorkflowState != Constants.WorkflowStates.Removed
                        && c.MicrotingSdkCaseId > 0)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // CANCELLED DEPLOYMENTS — a PlanningCaseSite whose PARENT PlanningCase is
        // Removed/Retracted is NOT a live deployment, however live its own
        // WorkflowState looks. The task-wizard deactivation branch
        // (`case true when !areaRulePlanning.Status`) soft-deletes every
        // Compliance row and sets `planningCase.WorkflowState = Retracted`, but
        // deliberately leaves the PlanningCaseSite rows untouched — and for
        // calendar-created cases the cloud CaseDelete it performs is a verified
        // no-op (CaseCreateLocalOnly assigns a synthetic MicrotingUid the cloud
        // has never seen), so the SDK Case rows stay live too.
        //
        // The caller's "task is inactive after this save" guard does NOT cover
        // the reverse edit: deactivate now, then in a LATER save REACTIVATE the
        // event and change the eForm in one go. Without this filter the sweep
        // below would pick those stale rows up, retract them, and hand the
        // worker brand-new live cases — dated DateTime.UtcNow.Date — for
        // long-past, cancelled occurrences.
        var cancelledPlanningCaseIds = await itemsPlanningPnDbContext.PlanningCases
            .AsNoTracking()
            .Where(x => x.PlanningId == planningId)
            .Where(x => x.WorkflowState == Constants.WorkflowStates.Removed
                        || x.WorkflowState == Constants.WorkflowStates.Retracted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Every live PlanningCaseSite of the planning, loaded ONCE for the whole
        // pass. Both sweeps below narrow this list IN MEMORY instead of
        // re-querying (the previous shape ran two queries per Compliance row
        // plus one more per re-pointed PlanningCaseSite).
        // Tracked (NOT AsNoTracking): re-pointed in place by SwapCaseEformAsync.
        var planningCaseSites = await itemsPlanningPnDbContext.PlanningCaseSites
            .Where(x => x.PlanningId == planningId)
            .Where(x => x.MicrotingSdkCaseId > 0)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.WorkflowState != Constants.WorkflowStates.Retracted)
            .Where(x => !cancelledPlanningCaseIds.Contains(x.PlanningCaseId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A planning can have live PlanningCaseSites WITHOUT any Compliance row
        // (see the Compliance-independent sweep at the end of this method), so
        // "nothing to repair" needs both to be empty.
        if (complianceRows.Count == 0 && planningCaseSites.Count == 0)
        {
            logger.LogDebug(
                "EventDeployService.RepairEformForOpenOccurrencesAsync: planning {PlanningId} has no deployed occurrences; nothing to repair ({OldEformId} -> {NewEformId})",
                planningId, oldEformId, newEformId);
            return;
        }

        var planning = await itemsPlanningPnDbContext.Plannings
            .FirstOrDefaultAsync(p =>
                    p.Id == planningId
                    && p.WorkflowState != Constants.WorkflowStates.Removed,
                cancellationToken)
            .ConfigureAwait(false);
        if (planning == null)
        {
            logger.LogWarning(
                "EventDeployService.RepairEformForOpenOccurrencesAsync: planning {PlanningId} not found; skipping eForm repair",
                planningId);
            return;
        }

        var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();

        // PRE-FLIGHT — validate the NEW eForm ONCE, before anything destructive.
        // SwapCaseEformAsync retracts the old case FIRST and only then calls
        // ReadeForm, so an unreadable id (a removed checklist, a stale client
        // cache, a child element id) would strip EVERY open occurrence of its
        // case and recreate none of them. The callers only check
        // `eformId <= 0`, which does not catch that.
        //
        // The probe mirrors what SqlController.ReadeForm actually requires of a
        // main element: a non-removed CheckList that is a ROOT checklist
        // (ParentId null/0) — a child element id resolves to `mainCl == null`
        // there, ReadeForm returns null and CreateSdkCaseForRotationAsync
        // NullReferences on the first mutation. Aborting the whole pass leaves
        // every row exactly as it was and the workers on the old, working eForm.
        var newEformIsUsable = await sdkDbContext.CheckLists
            .AsNoTracking()
            .AnyAsync(x => x.Id == newEformId
                           && (x.ParentId == null || x.ParentId == 0)
                           && x.WorkflowState != Constants.WorkflowStates.Removed,
                cancellationToken)
            .ConfigureAwait(false);
        if (!newEformIsUsable)
        {
            logger.LogError(
                "EventDeployService.RepairEformForOpenOccurrencesAsync: refusing to repair planning {PlanningId} — the new eForm {NewEformId} is not a usable, non-removed root CheckList. Nothing was retracted; every occurrence keeps eForm {OldEformId}.",
                planningId, newEformId, oldEformId);
            return;
        }

        // Every SDK case the rows above point at, loaded once. Tracked — the
        // retraction path reads them back and the sibling probes below reuse
        // the same instances.
        var allCaseIds = planningCaseSites
            .Select(x => x.MicrotingSdkCaseId)
            .Concat(complianceRows.Select(c => c.MicrotingSdkCaseId))
            .Distinct()
            .ToList();
        var sdkCasesById = (await sdkDbContext.Cases
                .Where(c => allCaseIds.Contains(c.Id)
                            && c.SiteId.HasValue
                            && c.WorkflowState != Constants.WorkflowStates.Removed
                            && c.WorkflowState != Constants.WorkflowStates.Retracted)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(c => c.Id);

        // Parent PlanningCases, loaded once (was one query per re-pointed
        // PlanningCaseSite).
        var planningCasesById = (await itemsPlanningPnDbContext.PlanningCases
                .Where(x => x.PlanningId == planningId
                            && x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(x => x.Id);

        // #1378 — make the size of the synchronous pass observable before it runs.
        var pendingSwapCount = sdkCasesById.Values
            .Count(c => c.CheckListId != newEformId
                        && c.Status != CompletedStatus
                        && !c.DoneAt.HasValue);
        logger.LogInformation(
            "EventDeployService.RepairEformForOpenOccurrencesAsync: planning {PlanningId} {OldEformId} -> {NewEformId}: {DeployedCaseCount} live deployed case(s), {PendingSwapCount} open and due a swap",
            planningId, oldEformId, newEformId, sdkCasesById.Count, pendingSwapCount);
        if (pendingSwapCount > LargeRepairPassWarningThreshold)
        {
            logger.LogWarning(
                "EventDeployService.RepairEformForOpenOccurrencesAsync: planning {PlanningId} is about to swap {PendingSwapCount} SDK cases synchronously inside the save request (warning threshold {Threshold}). Each swap costs a cloud CaseDelete + a local retraction + ReadeForm + CaseCreate, so this request may run long. No work is skipped — silent truncation would leave workers on the old eForm.",
                planningId, pendingSwapCount, LargeRepairPassWarningThreshold);
        }

        // (SDK site, language) resolved once per site and reused across every
        // rotation — the same pair the deploy passes look up per pass.
        var siteContexts = new Dictionary<int, (SdkSite Site, SdkLanguage Language)>();

        // ReadeForm / name-translation / folder memo, shared by every case of
        // this pass (they are keyed on (planning, eForm, language), all of which
        // are invariant here).
        var elementCache = new RotationElementCache();

        // Every SDK case the Compliance-driven loop has already looked at —
        // swapped, skipped as completed, or skipped as already-new — plus the
        // replacements it created. The Compliance-independent sweep below picks
        // up exactly what is left, so no case is processed twice.
        var handledCaseIds = new HashSet<int>();

        var swappedCount = 0;
        var retractedCount = 0;
        var failedCount = 0;

        foreach (var compliance in complianceRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rotationDate = compliance.Deadline.Date;

            // Every PlanningCaseSite that backs this occurrence: the one owning
            // the case the Compliance row points at, plus its siblings under the
            // same PlanningCase (Compliance.PlanningCaseSiteId holds the
            // PlanningCaseId — see EnsureComplianceRowAsync). The sibling lookup
            // is what makes the repair per-SITE rather than per-occurrence: the
            // wizard's Pair path puts every assigned site under one shared
            // PlanningCase, so all of them are swapped here.
            //
            // This loop alone does NOT reach every site: the calendar deploy
            // path gives each site its OWN PlanningCase, and the
            // (PlanningId, Deadline) unique index lets only the FIRST site's
            // Compliance row survive, so sites 2..n are reachable from no
            // Compliance row at all. They are handled by the
            // Compliance-independent sweep at the end of this method.
            var occurrenceCaseSites = planningCaseSites
                .Where(x => x.MicrotingSdkCaseId == compliance.MicrotingSdkCaseId
                            || (compliance.PlanningCaseSiteId > 0
                                && x.PlanningCaseId == compliance.PlanningCaseSiteId))
                .ToList();

            var caseIds = occurrenceCaseSites
                .Select(x => x.MicrotingSdkCaseId)
                .Append(compliance.MicrotingSdkCaseId)
                .Distinct()
                .ToList();

            foreach (var caseId in caseIds)
            {
                // Not in the dictionary => not a live, site-owned SDK case.
                if (!sdkCasesById.TryGetValue(caseId, out var sdkCase))
                {
                    continue;
                }

                var sdkSiteId = sdkCase.SiteId!.Value;
                handledCaseIds.Add(sdkCase.Id);

                // Per-site independence: one worker's failed swap must never
                // abort the remaining workers or the remaining rotations.
                try
                {
                    // #934 — the swap must hold the SAME per-(planning, site)
                    // gate the deploy path acquires. Between RetractSdkCaseAsync
                    // and CreateSdkCaseForRotationAsync the occurrence looks NOT
                    // deployed to the deploy guard (the old case is Removed and
                    // the Compliance row has not been re-pointed yet), so a
                    // concurrent pass — the 5s StreamEventChanges poll or any
                    // ListEvents one-shot — would create its own case and leave
                    // two live cases for the same (planning, site, rotation).
                    //
                    // Re-entrancy: safe. The lock is a NON-reentrant
                    // SemaphoreSlim, and the only callers of this pass are
                    // BackendConfigurationTaskWizardService.UpdateTask and
                    // .ApplyEformChangeToSeries — neither runs inside a deploy
                    // lock, and SwapCaseEformAsync goes straight to
                    // CreateSdkCaseForRotationAsync rather than through
                    // DeployForRotationAsync, so no nested acquire exists.
                    using var swapLockHandle = await AcquireDeployLockAsync(
                            planningId, sdkSiteId, cancellationToken)
                        .ConfigureAwait(false);

                    var swap = await SwapCaseEformAsync(
                            areaRulePlanning,
                            planning,
                            rotationDate,
                            sdkCase,
                            occurrenceCaseSites,
                            planningCaseSites,
                            sdkCasesById,
                            planningCasesById,
                            oldEformId,
                            newEformId,
                            sdkCore,
                            sdkDbContext,
                            siteContexts,
                            elementCache,
                            cancellationToken)
                        .ConfigureAwait(false);

                    switch (swap.Outcome)
                    {
                        case CaseSwapOutcome.Swapped:
                            swappedCount++;
                            handledCaseIds.Add(swap.NewSdkCaseId);

                            // Only the site the Compliance row actually points at
                            // owns that row; sibling sites keep their
                            // PlanningCaseSite as the only linkage (same shape as
                            // the original deploy).
                            if (compliance.MicrotingSdkCaseId == sdkCase.Id)
                            {
                                compliance.MicrotingSdkCaseId = swap.NewSdkCaseId;
                                compliance.MicrotingSdkeFormId = newEformId;
                                await compliance.Update(dbContext).ConfigureAwait(false);
                            }

                            logger.LogInformation(
                                "EventDeployService.RepairEformForOpenOccurrencesAsync: planning {PlanningId} rotation {RotationDate} site {SdkSiteId} swapped eForm {OldEformId} -> {NewEformId} (case {OldSdkCaseId} -> {NewSdkCaseId}, compliance {ComplianceId})",
                                planningId, rotationDate, sdkSiteId, oldEformId, newEformId,
                                sdkCase.Id, swap.NewSdkCaseId, compliance.Id);
                            break;

                        case CaseSwapOutcome.RetractedWithoutReplacement:
                            retractedCount++;
                            if (compliance.MicrotingSdkCaseId == sdkCase.Id)
                            {
                                // The row the calendar renders now points at a
                                // REMOVED case, and this pass deliberately
                                // manufactures no replacement (the site is not a
                                // live assignee — see SwapCaseEformAsync). Left
                                // as is, the occurrence is dead: the stuck-row
                                // recovery branch keys on SdkCaseId == 0, so
                                // nothing ever redeploys it and no worker can
                                // complete it again. Release it instead — the
                                // on-demand materialisation path
                                // (EnsureComplianceForOccurrenceAsync) and the
                                // nightly stuck-row branch can then redeploy it
                                // for a site that IS legitimately linked, on the
                                // new eForm. The row itself is never deleted:
                                // the calendar UI holds complianceId.
                                compliance.MicrotingSdkCaseId = 0;
                                await compliance.Update(dbContext).ConfigureAwait(false);
                            }

                            logger.LogWarning(
                                "EventDeployService.RepairEformForOpenOccurrencesAsync: planning {PlanningId} rotation {RotationDate} site {SdkSiteId} is no longer a live assignee — case {SdkCaseId} retracted and NOT recreated on eForm {NewEformId} (compliance {ComplianceId} released for redeploy).",
                                planningId, rotationDate, sdkSiteId, sdkCase.Id, newEformId, compliance.Id);
                            break;

                        case CaseSwapOutcome.Failed:
                            failedCount++;
                            if (compliance.MicrotingSdkCaseId == sdkCase.Id)
                            {
                                // The old case is gone and no replacement exists.
                                // Hand the row back to the stuck-row recovery
                                // branch of EnsureDeployedAsync (candidate filter
                                // `!t.IsFromCompliance || t.SdkCaseId == 0`);
                                // leaving MicrotingSdkCaseId pointing at the
                                // removed case would make the occurrence look
                                // deployed forever and never redeploy.
                                compliance.MicrotingSdkCaseId = 0;
                                await compliance.Update(dbContext).ConfigureAwait(false);
                            }

                            logger.LogWarning(
                                "EventDeployService.RepairEformForOpenOccurrencesAsync: planning {PlanningId} rotation {RotationDate} site {SdkSiteId} could not be re-created on eForm {NewEformId} after case {SdkCaseId} was retracted; compliance {ComplianceId} released for redeploy by the next deploy pass.",
                                planningId, rotationDate, sdkSiteId, newEformId, sdkCase.Id, compliance.Id);
                            break;

                        case CaseSwapOutcome.Skipped:
                        default:
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Cancellation aborts the whole pass — never swallowed as a
                    // per-site failure by the general catch below.
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "EventDeployService.RepairEformForOpenOccurrencesAsync: eForm swap threw {ExceptionType} for planning {PlanningId} rotation {RotationDate} site {SdkSiteId} (case {SdkCaseId}) — continuing with the remaining sites.",
                        ex.GetType().Name, planningId, rotationDate, sdkSiteId, sdkCase.Id);
                }
            }
        }

        // COMPLIANCE-INDEPENDENT SWEEP — the sites the loop above cannot see.
        //
        // DeployForRotationAsync creates a fresh PlanningCase + PlanningCaseSite
        // per (planning, rotation, SITE), while Compliance carries a UNIQUE
        // index on (PlanningId, Deadline)
        // (BackendConfigurationPnDbContext.OnModelCreating -> HasIndex(PlanningId,
        // Deadline).IsUnique(), migration
        // 20230703112050_AddingIndexOnCompliancePlanningIdAndDeadline). For a
        // multi-site occurrence only the FIRST site's Compliance INSERT wins;
        // EnsureComplianceRowAsync swallows the duplicate-key and hands back the
        // winner, so sites 2..n own a live SDK case that no Compliance row
        // references. Without this sweep those workers would keep the OLD eForm
        // on their device — the exact partial fix this repair pass exists to
        // avoid.
        //
        // Resolving such a case back to its rotation is impossible from the
        // schema (nothing on PlanningCase/PlanningCaseSite records a date, and
        // the SDK Case row has no deadline column either) — but it is also
        // unnecessary. CaseCreateLocalOnly persists only the columns
        // SqlController.CaseCreate writes (Status, Type, CheckListId,
        // MicrotingUid, MicrotingCheckUid, CaseUid, SiteId, Custom, FolderId);
        // mainElement.StartDate/EndDate are validated as "must be a future
        // date" and then discarded. A replacement created with today as the
        // nominal rotation is therefore identical, in every persisted column,
        // to one created with the true rotation date, and the clamp inside
        // CreateSdkCaseForRotationAsync keeps the EndDate validation satisfied.
        //
        // KNOWN AND ACCEPTED: #934 can leave several PlanningCaseSite rows
        // pointing at the SAME SDK case. All of them are re-pointed together
        // (they are matched by case id), but a row left over from a case this
        // pass retracted WITHOUT a replacement — an unassigned site, or a failed
        // create — stays behind referencing a removed case. Those rows are inert
        // (every reader filters on the live SDK case) and are NOT a bug in this
        // pass; cleaning them up belongs to the duplicate-PlanningCaseSite issue.
        var nominalRotationDate = DateTime.UtcNow.Date;

        var unreachableCaseSites = planningCaseSites
            .Where(x => !handledCaseIds.Contains(x.MicrotingSdkCaseId))
            .ToList();

        var unreachableCaseIds = unreachableCaseSites
            .Select(x => x.MicrotingSdkCaseId)
            .Distinct()
            .ToList();

        foreach (var caseId in unreachableCaseIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!sdkCasesById.TryGetValue(caseId, out var sdkCase))
            {
                continue;
            }

            var sdkSiteId = sdkCase.SiteId!.Value;

            // Same per-site independence as the Compliance-driven loop.
            try
            {
                // Same #934 gate as the Compliance-driven loop above — see the
                // comment there for the window it closes and why re-entrancy is
                // impossible on this call chain.
                using var swapLockHandle = await AcquireDeployLockAsync(
                        planningId, sdkSiteId, cancellationToken)
                    .ConfigureAwait(false);

                var swap = await SwapCaseEformAsync(
                        areaRulePlanning,
                        planning,
                        nominalRotationDate,
                        sdkCase,
                        unreachableCaseSites,
                        planningCaseSites,
                        sdkCasesById,
                        planningCasesById,
                        oldEformId,
                        newEformId,
                        sdkCore,
                        sdkDbContext,
                        siteContexts,
                        elementCache,
                        cancellationToken)
                    .ConfigureAwait(false);

                switch (swap.Outcome)
                {
                    case CaseSwapOutcome.Swapped:
                        swappedCount++;
                        logger.LogInformation(
                            "EventDeployService.RepairEformForOpenOccurrencesAsync: planning {PlanningId} site {SdkSiteId} swapped eForm {OldEformId} -> {NewEformId} (case {OldSdkCaseId} -> {NewSdkCaseId}) via the Compliance-independent sweep — the site owns no Compliance row for its rotation",
                            planningId, sdkSiteId, oldEformId, newEformId, sdkCase.Id, swap.NewSdkCaseId);
                        break;

                    case CaseSwapOutcome.RetractedWithoutReplacement:
                        retractedCount++;
                        logger.LogWarning(
                            "EventDeployService.RepairEformForOpenOccurrencesAsync: planning {PlanningId} site {SdkSiteId} is no longer a live assignee — case {SdkCaseId} retracted and NOT recreated on eForm {NewEformId} (Compliance-independent sweep).",
                            planningId, sdkSiteId, sdkCase.Id, newEformId);
                        break;

                    case CaseSwapOutcome.Failed:
                        failedCount++;
                        logger.LogWarning(
                            "EventDeployService.RepairEformForOpenOccurrencesAsync: planning {PlanningId} site {SdkSiteId} could not be re-created on eForm {NewEformId} after case {SdkCaseId} was retracted (Compliance-independent sweep); the site is left without a case until the next deploy pass.",
                            planningId, sdkSiteId, newEformId, sdkCase.Id);
                        break;

                    case CaseSwapOutcome.Skipped:
                    default:
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "EventDeployService.RepairEformForOpenOccurrencesAsync: Compliance-independent eForm swap threw {ExceptionType} for planning {PlanningId} site {SdkSiteId} (case {SdkCaseId}) — continuing with the remaining sites.",
                    ex.GetType().Name, planningId, sdkSiteId, sdkCase.Id);
            }
        }

        logger.LogInformation(
            "EventDeployService.RepairEformForOpenOccurrencesAsync: planning {PlanningId} finished {OldEformId} -> {NewEformId}: {SwappedCount} swapped, {RetractedCount} retracted without replacement, {FailedCount} failed",
            planningId, oldEformId, newEformId, swappedCount, retractedCount, failedCount);
    }

    /// <summary>
    /// What one <see cref="SwapCaseEformAsync"/> call actually did. A single
    /// <c>null</c> return used to conflate "nothing to do", "the site is gone"
    /// and "the replacement could not be created", which left the caller unable
    /// to compensate for the last one — the failure that strands an occurrence
    /// on a retracted case forever.
    /// </summary>
    private enum CaseSwapOutcome
    {
        /// <summary>
        /// Nothing changed: the case is completed, is already on the new eForm,
        /// or its site/language could not be resolved. The old case is still live.
        /// </summary>
        Skipped,

        /// <summary>Old case retracted, replacement created, plugin rows re-pointed.</summary>
        Swapped,

        /// <summary>
        /// Old case retracted ON PURPOSE with no replacement: the site is no
        /// longer a live assignee of the event.
        /// </summary>
        RetractedWithoutReplacement,

        /// <summary>
        /// The old case was retracted but the replacement could NOT be created.
        /// The caller MUST compensate (release the Compliance row for redeploy).
        /// </summary>
        Failed
    }

    private readonly record struct CaseSwapResult(CaseSwapOutcome Outcome, int NewSdkCaseId)
    {
        public static CaseSwapResult Skipped() => new(CaseSwapOutcome.Skipped, 0);
        public static CaseSwapResult Retracted() => new(CaseSwapOutcome.RetractedWithoutReplacement, 0);
        public static CaseSwapResult Failed() => new(CaseSwapOutcome.Failed, 0);
        public static CaseSwapResult Swapped(int newSdkCaseId) => new(CaseSwapOutcome.Swapped, newSdkCaseId);
    }

    /// <summary>
    /// Swaps ONE open SDK case onto <paramref name="newEformId"/>: retracts the
    /// old case, creates the replacement for the same site, and re-points the
    /// plugin-side <c>PlanningCaseSite</c> / <c>PlanningCase</c> rows in place.
    /// Shared by both sweeps of
    /// <see cref="RepairEformForOpenOccurrencesAsync"/> so a site reached
    /// through a Compliance row and a site reached only through its
    /// PlanningCaseSite are repaired identically.
    ///
    /// The Compliance row itself is NOT touched here — only the caller knows
    /// whether the case owns one, and what to do with it per
    /// <see cref="CaseSwapOutcome"/>.
    /// </summary>
    /// <param name="planningCaseSites">
    /// The rows THIS iteration may re-point: the sweep's own narrowed slice.
    /// </param>
    /// <param name="allPlanningCaseSites">
    /// Every live PlanningCaseSite of the planning. Used ONLY for the
    /// completed-sibling probe, which must see siblings the current sweep's
    /// slice excludes — the second sweep is handed just the rows the
    /// Compliance loop did not handle, so a completed sibling reached through a
    /// Compliance row would otherwise be invisible and the shared parent
    /// PlanningCase would be flipped onto an eForm that sibling never used.
    /// </param>
    private async Task<CaseSwapResult> SwapCaseEformAsync(
        AreaRulePlanning areaRulePlanning,
        Planning planning,
        DateTime rotationDate,
        SdkCase sdkCase,
        List<PlanningCaseSite> planningCaseSites,
        List<PlanningCaseSite> allPlanningCaseSites,
        Dictionary<int, SdkCase> sdkCasesById,
        Dictionary<int, PlanningCase> planningCasesById,
        int oldEformId,
        int newEformId,
        SdkCore sdkCore,
        SdkDbContext sdkDbContext,
        Dictionary<int, (SdkSite Site, SdkLanguage Language)> siteContexts,
        RotationElementCache elementCache,
        CancellationToken cancellationToken)
    {
        var sdkSiteId = sdkCase.SiteId!.Value;

        // Completed occurrences keep their historical eForm — the answered case
        // IS the record of what was filled in.
        if (sdkCase.Status == CompletedStatus || sdkCase.DoneAt.HasValue)
        {
            return CaseSwapResult.Skipped();
        }

        // Idempotence: already carrying the new eForm (e.g. a second save of the
        // same edit, or a sibling reached through two Compliance rows sharing a
        // PlanningCase).
        if (sdkCase.CheckListId == newEformId)
        {
            return CaseSwapResult.Skipped();
        }

        if (!siteContexts.TryGetValue(sdkSiteId, out var siteContext))
        {
            var sdkSite = await sdkDbContext.Sites
                .FirstOrDefaultAsync(s => s.Id == sdkSiteId, cancellationToken)
                .ConfigureAwait(false);
            var language = sdkSite == null
                ? null
                : await sdkDbContext.Languages
                    .FirstOrDefaultAsync(l => l.Id == sdkSite.LanguageId, cancellationToken)
                    .ConfigureAwait(false);
            siteContext = (sdkSite, language);
            siteContexts[sdkSiteId] = siteContext;
        }

        if (siteContext.Site == null
            || siteContext.Language == null
            || !siteContext.Site.MicrotingUid.HasValue)
        {
            logger.LogWarning(
                "EventDeployService.RepairEformForOpenOccurrencesAsync: sdk site {SdkSiteId} (or its language) not resolvable; leaving case {SdkCaseId} on eForm {OldEformId}",
                sdkSiteId, sdkCase.Id, oldEformId);
            return CaseSwapResult.Skipped();
        }

        // #932 / #1377 — the site-assignment guard that protects the deploy path
        // lives in DeployForRotationAsync, which this path does NOT go through:
        // it calls CreateSdkCaseForRotationAsync directly. Without the same
        // check here, an edit that unassigns a worker AND changes the eForm in
        // one save would hand the removed worker a BRAND-NEW case — the wizard's
        // unassign branch deletes the PlanningSites and CaseDeletes the cloud
        // case, but leaves the PlanningCaseSite live, so the sweep above still
        // finds it.
        //
        // Note the deliberately STRICTER question than the deploy path's: a bare
        // active PropertyWorker is NOT accepted here, because an unassigned
        // worker stays an active property worker. A site that is no longer a
        // live assignee gets its case retracted and NOT recreated — leaving the
        // old eForm live on a device the event no longer belongs to would be
        // just as wrong as giving it the new one.
        var linkage = await ResolveSiteLinkageAsync(
                areaRulePlanning,
                planning.Id,
                sdkSiteId,
                sdkDbContext,
                acceptPropertyWorker: false,
                cancellationToken)
            .ConfigureAwait(false);
        if (linkage == SiteEventLinkage.None)
        {
            await RetractSdkCaseAsync(sdkCore, sdkDbContext, sdkCase, cancellationToken)
                .ConfigureAwait(false);

            // Do not leave the plugin-side row live on a case that no longer
            // exists: the next repair pass would pick it up and retract again.
            foreach (var orphanedCaseSite in planningCaseSites
                         .Where(x => x.MicrotingSdkCaseId == sdkCase.Id)
                         .ToList())
            {
                orphanedCaseSite.WorkflowState = Constants.WorkflowStates.Retracted;
                await orphanedCaseSite.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            return CaseSwapResult.Retracted();
        }

        // ORDERING — retract the OLD case first, then create the new one. The
        // SDK has no "swap the checklist of a live case" operation:
        // Cases.CheckListId is frozen by ReadeForm + CaseCreateLocalOnly, so a
        // replacement case is the only way. Retract-then-create is chosen over
        // create-then-retract because a failure between the two steps must never
        // leave TWO live cases for the same site and deadline: the worker's
        // device would show both forms and either could be completed, producing
        // a duplicate (and possibly wrong-eForm) answer that the Compliance row
        // can only reference one of. This is also the ordering the canonical
        // re-pairing path uses (PairItemWithSiteHelper.Pair deletes the site's
        // existing cases before creating the replacement).
        //
        // A failure between the two steps therefore leaves the site with NO case
        // for the rotation. That is NOT self-healing on its own: the deploy
        // idempotence guard would still see the Compliance row as deployed
        // (it keys on the row, not on the case's workflow state). Recovery is
        // explicit instead — CaseSwapOutcome.Failed tells the caller to release
        // the Compliance row (MicrotingSdkCaseId = 0), which is what the
        // stuck-row branch of EnsureDeployedAsync keys on.
        await RetractSdkCaseAsync(sdkCore, sdkDbContext, sdkCase, cancellationToken)
            .ConfigureAwait(false);

        int? newCaseId;
        try
        {
            newCaseId = await CreateSdkCaseForRotationAsync(
                    planning,
                    rotationDate,
                    newEformId,
                    sdkCore,
                    sdkDbContext,
                    siteContext.Site,
                    siteContext.Language,
                    cancellationToken,
                    elementCache)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The old case is already gone, so this must reach the caller as
            // Failed (compensation needed) rather than as a bare exception the
            // caller cannot tell apart from a failure that changed nothing.
            logger.LogWarning(ex,
                "EventDeployService.RepairEformForOpenOccurrencesAsync: creating the replacement case threw {ExceptionType} for planning {PlanningId} rotation {RotationDate} site {SdkSiteId} (old case {SdkCaseId} already retracted)",
                ex.GetType().Name, planning.Id, rotationDate, sdkSiteId, sdkCase.Id);
            return CaseSwapResult.Failed();
        }

        if (newCaseId == null)
        {
            logger.LogWarning(
                "EventDeployService.RepairEformForOpenOccurrencesAsync: CaseCreateLocalOnly returned null for planning {PlanningId} rotation {RotationDate} site {SdkSiteId}; the occurrence is left without a case",
                planning.Id, rotationDate, sdkSiteId);
            return CaseSwapResult.Failed();
        }

        // Re-point the plugin-side rows IN PLACE onto the new case.
        foreach (var planningCaseSite in planningCaseSites
                     .Where(x => x.MicrotingSdkCaseId == sdkCase.Id)
                     .ToList())
        {
            planningCaseSite.MicrotingSdkCaseId = newCaseId.Value;
            planningCaseSite.MicrotingSdkeFormId = newEformId;
            await planningCaseSite.Update(itemsPlanningPnDbContext).ConfigureAwait(false);

            if (!planningCasesById.TryGetValue(planningCaseSite.PlanningCaseId, out var planningCase)
                || planningCase.MicrotingSdkeFormId == newEformId)
            {
                continue;
            }

            // The wizard shape shares ONE PlanningCase across every assigned
            // site, and a completed sibling keeps its historical eForm forever.
            // Flipping the shared parent onto the new eForm in that case would
            // claim an eForm the completed sibling never used, so leave the
            // parent on the old id and let the per-site rows carry the truth.
            //
            // Searched over ALL of the planning's live rows, never the current
            // sweep's slice: the Compliance-independent sweep is handed only the
            // rows the Compliance loop skipped, so a completed sibling reached
            // through a Compliance row is absent from that slice — and the guard
            // would be blind to exactly the case it exists to catch.
            var completedSiblingOnOldEform = allPlanningCaseSites.Any(x =>
                x.PlanningCaseId == planningCaseSite.PlanningCaseId
                && x.MicrotingSdkCaseId != newCaseId.Value
                && sdkCasesById.TryGetValue(x.MicrotingSdkCaseId, out var siblingCase)
                && siblingCase.CheckListId == oldEformId
                && (siblingCase.Status == CompletedStatus || siblingCase.DoneAt.HasValue));
            if (completedSiblingOnOldEform)
            {
                logger.LogInformation(
                    "EventDeployService.RepairEformForOpenOccurrencesAsync: PlanningCase {PlanningCaseId} keeps eForm {OldEformId} — a completed sibling site still uses it; only the swapped PlanningCaseSite moves to {NewEformId}",
                    planningCase.Id, oldEformId, newEformId);
                continue;
            }

            planningCase.MicrotingSdkeFormId = newEformId;
            await planningCase.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
        }

        return CaseSwapResult.Swapped(newCaseId.Value);
    }

    /// <summary>
    /// Retracts one live SDK case so its site can be redeployed with a
    /// different eForm. Mirrors the canonical retraction
    /// (<c>CalendarAssignmentReconciliationService.RetractSiteForOccurrenceAsync</c>
    /// / <c>PairItemWithSiteHelper.Pair</c>): best-effort cloud delete, then a
    /// guaranteed local removal.
    /// </summary>
    private async Task RetractSdkCaseAsync(
        SdkCore sdkCore,
        SdkDbContext sdkDbContext,
        SdkCase sdkCase,
        CancellationToken cancellationToken)
    {
        if (sdkCase.MicrotingUid.HasValue)
        {
            try
            {
                // #1378 — Core.CaseDelete answers a "Parsing in progress: Can
                // not delete check list!" response with
                // `for (i = 1; i < 102; i++) Thread.Sleep(i * 5000)`, i.e. up to
                // ~7 HOURS of blocked thread, per case. This pass runs
                // SYNCHRONOUSLY inside the HTTP save, so one unlucky case would
                // pin the request thread indefinitely.
                //
                // Bound it: the cloud delete keeps running on its own (its first
                // await hands our thread back, and every retry is a pool
                // continuation), we just stop waiting. Falling through is safe —
                // and for calendar cases it is the NORMAL path — because
                // Core.CaseDeleteResult below is what actually removes the local
                // row, and it is the only step the repair depends on.
                var cloudDelete = sdkCore.CaseDelete(sdkCase.MicrotingUid.Value);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var finished = await Task
                    .WhenAny(cloudDelete, Task.Delay(CloudCaseDeleteTimeout, timeoutCts.Token))
                    .ConfigureAwait(false);
                if (finished == cloudDelete)
                {
                    // Cancels the timer task; it is never awaited, and a
                    // cancelled Task.Delay raises no unobserved exception.
                    timeoutCts.Cancel();
                    // Re-await so a fault surfaces in the catch below.
                    await cloudDelete.ConfigureAwait(false);
                }
                else
                {
                    logger.LogWarning(
                        "EventDeployService.RetractSdkCaseAsync: cloud CaseDelete for case {SdkCaseId} (microtingUid {MicrotingUid}) did not answer within {TimeoutSeconds}s; abandoning the wait and falling through to the local retraction",
                        sdkCase.Id, sdkCase.MicrotingUid.Value, CloudCaseDeleteTimeout.TotalSeconds);

                    // The abandoned task must not fault unobserved.
                    _ = cloudDelete.ContinueWith(
                        t => logger.LogWarning(t.Exception,
                            "EventDeployService.RetractSdkCaseAsync: abandoned cloud CaseDelete for case {SdkCaseId} faulted after the timeout",
                            sdkCase.Id),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
            catch (Exception ex)
            {
                // Mirrors PairItemWithSiteHelper's catch: a cloud-side delete
                // failure must not stop the local retraction, otherwise the
                // occurrence would keep pointing at the old eForm forever.
                logger.LogWarning(ex,
                    "EventDeployService.RetractSdkCaseAsync: cloud CaseDelete failed for case {SdkCaseId} (microtingUid {MicrotingUid}); falling back to local retraction",
                    sdkCase.Id, sdkCase.MicrotingUid.Value);
            }
        }

        // Core.CaseDelete only removes the local Case row when the cloud DELETE
        // answered Success. Calendar cases are created with CaseCreateLocalOnly,
        // which assigns a synthetic MicrotingUid the cloud has never seen, so
        // for them the cloud call is a guaranteed no-op and the local row would
        // survive — leaving the OLD eForm live on the worker's device, i.e. the
        // exact bug this repair pass exists to fix. Core.CaseDeleteResult
        // soft-deletes by Case.Id with no cloud round-trip.
        var stillLive = await sdkDbContext.Cases
            .AsNoTracking()
            .AnyAsync(c => c.Id == sdkCase.Id
                           && c.WorkflowState != Constants.WorkflowStates.Removed
                           && c.WorkflowState != Constants.WorkflowStates.Retracted,
                cancellationToken)
            .ConfigureAwait(false);
        if (stillLive)
        {
            await sdkCore.CaseDeleteResult(sdkCase.Id).ConfigureAwait(false);
        }
    }

    /// <param name="eformId">
    /// The eForm the SDK case was actually created from (the AreaRule-preferred
    /// id resolved by the callers). It is NOT read off
    /// <c>planning.RelatedEFormId</c>: that column is written by the task
    /// wizard on a different code path and can lag behind
    /// <c>AreaRule.EformId</c>, which would give the Compliance row an eForm id
    /// the backing SDK case never used.
    /// </param>
    private async Task<Compliance?> EnsureComplianceRowAsync(
        AreaRulePlanning areaRulePlanning,
        Planning planning,
        DateTime rotationDate,
        PlanningCaseSite planningCaseSite,
        int eformId,
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
            MicrotingSdkeFormId = eformId,
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
                    existing.MicrotingSdkeFormId = eformId;
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
