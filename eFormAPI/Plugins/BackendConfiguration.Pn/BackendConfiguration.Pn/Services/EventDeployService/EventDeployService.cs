using System;
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

            try
            {
                // 1. Idempotence guard — Compliance natural key.
                //    Mirrors EformParsedByServerHandler.cs:157-164 (compliance
                //    is keyed on PlanningId + Deadline; we additionally scope
                //    to the requested sdk site below when locating the
                //    PlanningCaseSite).
                //    The slot is "taken — skip" if it's soft-removed (user
                //    already completed this rotation; the SDK Case still
                //    exists, just the Compliance was retracted) OR fully
                //    deployed (MicrotingSdkCaseId > 0). We re-deploy ONLY for
                //    the genuine stuck-row shape: Created + MicrotingSdkCaseId
                //    == 0, where an earlier deploy left a Compliance row
                //    behind without an SDK Case (e.g. the SDK 10.0.27 EndDate
                //    validation bug). EnsureComplianceRowAsync revives that
                //    row in place via the duplicate-key catch.
                var alreadyDeployed = await dbContext.Compliances
                    .AsNoTracking()
                    .AnyAsync(c =>
                            c.PlanningId == planningId
                            && c.Deadline.Date == rotationDate
                            && (c.WorkflowState == Constants.WorkflowStates.Removed
                                || c.MicrotingSdkCaseId > 0),
                        cancellationToken)
                    .ConfigureAwait(false);
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
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "EventDeployService: failed to deploy planningId={PlanningId} rotation={Rotation} sdkSiteId={SdkSiteId} — continuing with the rest",
                    planningId, rotationDate, sdkSiteId);
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
        // Use end-of-rotation-day UTC so the SDK Case is created any time during the
        // deadline day, not only when the deploy fires BEFORE 00:00 UTC.
        // rotationDate is parsed by EnsureDeployedAsync with
        // AssumeUniversal | AdjustToUniversal then .Date, so it lands at 00:00 UTC.
        // The downstream guard (`mainElement.EndDate > DateTime.UtcNow`)
        // otherwise silently skips CaseCreate for any same-day deploy, leaving
        // Compliance rows with MicrotingSdkCaseId=0.
        mainElement.EndDate = rotationDate.AddDays(1).AddTicks(-1);

        // 7. Only call CaseCreate when EndDate is in the future
        //    (mirrors ItemCaseCreateHandler.cs:236). The EndDate value
        //    itself — end-of-rotation-day UTC (23:59:59.9999999) — is
        //    what makes the guard pass for same-day deploys; this is
        //    now a clock-skew belt + safety net for future changes to
        //    rotationDate semantics.
        if (mainElement.EndDate > DateTime.UtcNow)
        {
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

        // Idempotence guard: another caller (nightly batch, parallel request)
        // may already have materialised this occurrence. Match the natural
        // key the nightly path uses (PlanningId + Deadline.Date, non-removed)
        // plus the canonical "case actually exists" filter
        // (MicrotingSdkCaseId > 0) so we never hand back a half-deployed row.
        var existing = await dbContext.Compliances
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                    c.PlanningId == areaRulePlanning.ItemPlanningId
                    && c.Deadline.Date == deadlineDate
                    && c.WorkflowState != Constants.WorkflowStates.Removed
                    && c.MicrotingSdkCaseId > 0,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing != null)
        {
            return new EnsureComplianceResult
            {
                Created = false,
                ComplianceId = existing.Id,
                SdkCaseId = existing.MicrotingSdkCaseId,
                TemplateId = existing.MicrotingSdkeFormId
            };
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

        try
        {
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
