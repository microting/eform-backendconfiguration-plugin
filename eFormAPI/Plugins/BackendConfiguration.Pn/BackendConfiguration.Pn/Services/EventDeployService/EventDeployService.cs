using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;

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
    IBackendConfigurationCalendarService calendarService,
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
            .Where(t => !t.IsFromCompliance) // rows already backed by a Compliance need no deploy
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
                var alreadyDeployed = await dbContext.Compliances
                    .AsNoTracking()
                    .AnyAsync(c =>
                            c.PlanningId == planningId
                            && c.Deadline.Date == rotationDate
                            && c.WorkflowState != Constants.WorkflowStates.Removed,
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
                    // before continuing.
                    await EnsureComplianceRowAsync(
                            areaRulePlanning,
                            planning,
                            rotationDate,
                            planningCaseSite,
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                // 6. Build mainElement. Mirrors ItemCaseCreateHandler.cs:113-153.
                //    KEY DIFFERENCE: EndDate is the rotation we're deploying
                //    (not planning.NextExecutionTime), so backfill of a future
                //    rotation date stays bounded to that day.
                var mainElement = await sdkCore.ReadeForm(eformId, language).ConfigureAwait(false);

                var planningNameTranslation = await itemsPlanningPnDbContext.PlanningNameTranslation
                    .FirstOrDefaultAsync(x =>
                            x.LanguageId == language.Id && x.PlanningId == planning.Id,
                        cancellationToken)
                    .ConfigureAwait(false);
                var translation = planningNameTranslation?.Name;

                string folderId = string.Empty;
                if (planning.SdkFolderId.HasValue)
                {
                    var folder = await sdkDbContext.Folders
                        .FirstOrDefaultAsync(x => x.Id == planning.SdkFolderId.Value, cancellationToken)
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
                // EndDate = the rotation date itself. Compare with the handler
                // which uses planning.NextExecutionTime — here we want the
                // deploy bounded to the rotation we're filling.
                mainElement.EndDate = rotationDate;

                // 7. Only call CaseCreate when EndDate is in the future
                //    (mirrors ItemCaseCreateHandler.cs:236). Defensive — our
                //    `rotationDate >= todayUtc` filter already covers this for
                //    same-day rotations, but a clock-skew check costs nothing.
                if (mainElement.EndDate > DateTime.UtcNow)
                {
                    var caseId = await sdkCore.CaseCreate(
                        mainElement, "", (int)sdkSite.MicrotingUid!, null)
                        .ConfigureAwait(false);

                    if (caseId != null)
                    {
                        var caseDto = await sdkCore.CaseLookupMUId((int)caseId).ConfigureAwait(false);
                        if (caseDto?.CaseId != null)
                        {
                            planningCaseSite.MicrotingSdkCaseId = (int)caseDto.CaseId;
                            await planningCaseSite.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
                        }
                    }
                }

                // 8. Compliance row. Mirrors EformParsedByServerHandler.cs:170-182.
                await EnsureComplianceRowAsync(
                        areaRulePlanning,
                        planning,
                        rotationDate,
                        planningCaseSite,
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

    private async Task EnsureComplianceRowAsync(
        AreaRulePlanning areaRulePlanning,
        Planning planning,
        DateTime rotationDate,
        PlanningCaseSite planningCaseSite,
        CancellationToken cancellationToken)
    {
        // Re-check inside the same logical step (the outer guard runs before
        // the SDK case create; another worker on the same site could have
        // raced past here in theory). Mirrors EformParsedByServerHandler.cs:157.
        var existing = await dbContext.Compliances
            .AsNoTracking()
            .AnyAsync(c =>
                    c.PlanningId == planning.Id
                    && c.Deadline.Date == rotationDate
                    && c.WorkflowState != Constants.WorkflowStates.Removed,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing) return;

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
        }
        catch (Exception ex)
        {
            // Duplicate-key races are tolerated — mirrors
            // EformParsedByServerHandler.cs:185-196.
            if (ex.InnerException is { HResult: -2147467259 })
            {
                logger.LogInformation(
                    "EventDeployService: compliance for planning {PlanningId} deadline {Deadline} already exists (race) — skipping",
                    planning.Id, rotationDate);
                return;
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
