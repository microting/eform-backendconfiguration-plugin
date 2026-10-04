using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using CalendarService = BackendConfiguration.Pn.Services.BackendConfigurationCalendarService.BackendConfigurationCalendarService;

namespace BackendConfiguration.Pn.Services.CalendarOrphanComplianceRepair;

/// <summary>
/// #1383 — closes orphaned off-pattern compliances of monthly "Nth weekday" rules.
///
/// <para>An orphan is a live <c>Compliance</c> whose SDK case is removed or completed.
/// The #1294 re-anchor repair leaves orphans alone. When such an orphan sits on an
/// off-pattern date (typically the legacy day-of-month), GetTasksForWeek paints it
/// AND the rule's own occurrence of that month, so one monthly period shows two red
/// tiles.</para>
///
/// <para>The repair soft-deletes the orphan's compliance only when every one of
/// these holds (anything else goes on the <c>Kept</c> list with its reasons):</para>
/// <list type="bullet">
/// <item>the planning has exactly one live rule, a Month rule with an ordinal week,
/// and its weekday list does not disagree with its weekday;</item>
/// <item>the compliance's date is not one the week view paints for the rule, and the
/// week view paints exactly one other date in that month (the on-pattern occurrence that
/// stays) — <c>GetWeekViewOccurrences</c>, so RepeatUntil and the end bounds count;</item>
/// <item>the date is before today (Copenhagen);</item>
/// <item>no live occurrence exception of the rule touches that month;</item>
/// <item>the compliance's own SDK case is missing, removed or retracted and was never
/// completed — a completed orphan already renders as its period's single completed
/// tile, and is history;</item>
/// <item>the occurrence resolves, through the PlanningCaseSite that carries the
/// compliance's own SDK case, to exactly one existing PlanningCase of the compliance's
/// planning, and a stored <c>Compliance.PlanningCaseSiteId</c> agrees with it (it holds
/// the PlanningCase id on newer rows and the PlanningCaseSite id on 2022–2025 rows);</item>
/// <item>nothing else of that occurrence is completed or still open: not that
/// PlanningCase, not any of its PlanningCaseSites, not their SDK cases.</item>
/// </list>
///
/// <para>The repair is opt-in and reviewed: it only runs from the admin endpoint, only
/// with the hash of a dry run, and nothing triggers it at startup. Right before each
/// write the planning is evaluated again from the database, and a row that no longer
/// qualifies is skipped, not written. Re-running is safe: closed rows drop out of the
/// plan, and an empty plan is refused.</para>
/// </summary>
public class CalendarOrphanComplianceRepairService(
    BackendConfigurationPnDbContext dbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IEFormCoreService coreHelper,
    ILogger<CalendarOrphanComplianceRepairService> logger) : ICalendarOrphanComplianceRepairService
{
    private const int MonthRepeatType = 3;
    private const int CompletedStatus = 100;

    internal const string ReasonSeveralRules = "PlanningHasSeveralRules";
    internal const string ReasonCsvDisagrees = "WeekdayCsvDisagreesWithDayOfWeek";
    internal const string ReasonCaseCompleted = "SdkCaseCompleted";
    internal const string ReasonNotPast = "DeadlineNotInThePast";
    internal const string ReasonNoOnPatternOccurrence = "NoOnPatternOccurrenceInMonth";
    internal const string ReasonExceptionInMonth = "OccurrenceExceptionInMonth";
    internal const string ReasonCompletedElsewhere = "OccurrenceCompletedElsewhere";
    internal const string ReasonOpenElsewhere = "OccurrenceStillOpenElsewhere";
    internal const string ReasonNoPlanningCaseLink = "NoPlanningCaseLink";
    internal const string ReasonPlanningCaseLinkMismatch = "PlanningCaseLinkMismatch";

    /// <summary>Clock seam for "today in Copenhagen" (same shape as the #1294 repair's).</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>
    /// Test seam: runs with the planning id right before the real run re-evaluates that
    /// planning, i.e. after the plan-hash check and before its writes.
    /// </summary>
    internal Func<int, Task> BeforePlanningRecheck { get; set; } = _ => Task.CompletedTask;

    public async Task<OperationDataResult<OrphanOffPatternComplianceRepairPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plan = await ComputeAsync(null, cancellationToken).ConfigureAwait(false);
            LogPlan(plan, isRun: false);
            return new OperationDataResult<OrphanOffPatternComplianceRepairPlanModel>(true, plan);
        }
        catch (Exception e)
        {
            logger.LogError(e, "CalendarOrphanComplianceRepair: dry run failed");
            return new OperationDataResult<OrphanOffPatternComplianceRepairPlanModel>(false,
                $"Orphan compliance dry run failed: {e.Message}");
        }
    }

    public async Task<OperationDataResult<OrphanOffPatternComplianceRepairRunResultModel>> RunAsync(
        string planHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHash))
        {
            return Refused("planHash is required: run the dry run, review it, and pass its PlanHash.");
        }

        try
        {
            var plan = await ComputeAsync(null, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(planHash, plan.PlanHash, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "CalendarOrphanComplianceRepair: refused — plan hash {Given} does not match the current plan {Current}; nothing was written",
                    planHash, plan.PlanHash);
                return Refused("The data changed since the reviewed dry run (plan hash mismatch). Run the dry run again and review it.");
            }
            if (plan.Closures.Count == 0)
            {
                return Refused("The plan has nothing to close; nothing was written.");
            }

            // From here on the run writes; a closed browser tab must not stop it halfway.
            LogPlan(plan, isRun: true);
            var result = await ApplyAsync(plan).ConfigureAwait(false);
            logger.LogInformation(
                "CalendarOrphanComplianceRepair: finished — {Closed} compliances closed, {Skipped} skipped, {Failures} failures",
                result.ClosedCompliances, result.Skipped.Count, result.Failures.Count);
            return new OperationDataResult<OrphanOffPatternComplianceRepairRunResultModel>(result.Failures.Count == 0, result);
        }
        catch (Exception e)
        {
            logger.LogError(e, "CalendarOrphanComplianceRepair: run failed");
            return new OperationDataResult<OrphanOffPatternComplianceRepairRunResultModel>(false,
                $"Orphan compliance repair failed: {e.Message}");
        }
    }

    private static OperationDataResult<OrphanOffPatternComplianceRepairRunResultModel> Refused(string message)
        => new(false, message);

    // ── Plan ────────────────────────────────────────────────────────────────

    private sealed record SdkCaseInfo(int Id, string WorkflowState, int? Status, DateTime? DoneAt)
    {
        public bool IsLive => WorkflowState is not (Constants.WorkflowStates.Removed or Constants.WorkflowStates.Retracted);
        public bool IsCompleted => Status == CompletedStatus || DoneAt.HasValue;
    }

    /// <summary>
    /// Read-only (every query is AsNoTracking): the whole plan, or — with
    /// <paramref name="onlyPlanningId"/> — the plan of one planning, which the real run
    /// recomputes right before writing that planning's closures.
    /// </summary>
    private async Task<OrphanOffPatternComplianceRepairPlanModel> ComputeAsync(int? onlyPlanningId, CancellationToken ct)
    {
        var plan = new OrphanOffPatternComplianceRepairPlanModel
        {
            Today = ComplianceFutureTaskGuard.TodayInCopenhagen(UtcNow())
        };

        // Plannings with a live Month-Nth rule. EF's C# null semantics keep legacy rows
        // whose WorkflowState is NULL.
        var planningIds = await dbContext.AreaRulePlannings
            .AsNoTracking()
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.RepeatType == MonthRepeatType && x.RepeatOrdinalWeek > 0 && x.ItemPlanningId > 0)
            .Where(x => onlyPlanningId == null || x.ItemPlanningId == onlyPlanningId)
            .Select(x => x.ItemPlanningId)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);
        if (planningIds.Count == 0)
        {
            plan.PlanHash = HashOf(plan);
            return plan;
        }

        // Every live rule of those plannings, of any kind: a planning shared by several
        // rules renders once per rule, so "the period's other tile" is not one thing there.
        var rulesByPlanning = (await dbContext.AreaRulePlannings
                .AsNoTracking()
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => planningIds.Contains(x.ItemPlanningId))
                .OrderBy(x => x.Id)
                .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(x => x.ItemPlanningId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var plannings = await itemsPlanningPnDbContext.Plannings
            .AsNoTracking()
            .Where(x => planningIds.Contains(x.Id) && x.RepeatType == RepeatType.Month)
            .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);

        var monthPlanningIds = plannings.Keys.ToList();
        var compliances = await dbContext.Compliances
            .AsNoTracking()
            .Where(c => monthPlanningIds.Contains(c.PlanningId))
            .Where(c => c.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(c => c.MicrotingSdkCaseId > 0)
            .OrderBy(c => c.Id)
            .Select(c => new ComplianceRow(c.Id, c.PlanningId, c.PropertyId, c.Deadline, c.MicrotingSdkCaseId,
                c.PlanningCaseSiteId))
            .ToListAsync(ct).ConfigureAwait(false);

        // Off-pattern rows first (in memory): the dates the calendar's week view paints
        // for the rule in the row's month — the same enumerator GetTasksForWeek uses, so
        // Planning.RepeatUntil, one-offs and the end bounds count exactly as they render.
        // A row on one of those dates is the period's own tile and is never listed.
        var ruleDatesByMonth = new Dictionary<(int PlanningId, int Year, int Month), List<DateTime>>();
        var offPatternRows = new List<(ComplianceRow Compliance, DateTime? OnPatternDate)>();
        foreach (var compliance in compliances)
        {
            var key = (compliance.PlanningId, compliance.Deadline.Year, compliance.Deadline.Month);
            if (!ruleDatesByMonth.TryGetValue(key, out var ruleDates))
            {
                var monthStart = new DateTime(key.Year, key.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                ruleDates = CalendarService.GetWeekViewOccurrences(plannings[compliance.PlanningId],
                    rulesByPlanning[compliance.PlanningId][0], monthStart, monthStart.AddMonths(1).AddDays(-1));
                ruleDatesByMonth[key] = ruleDates;
            }
            if (ruleDates.Any(d => d.Date == compliance.Deadline.Date))
            {
                continue;
            }
            offPatternRows.Add((compliance, ruleDates.Count == 1 ? ruleDates[0].Date : null));
        }
        if (offPatternRows.Count == 0)
        {
            plan.PlanHash = HashOf(plan);
            return plan;
        }

        // Of those, the orphans: SDK case missing, removed/retracted, or completed. A live
        // open case is the #1294 repair's business and is not listed.
        var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();
        var ownCases = await LoadSdkCasesAsync(sdkDbContext,
            offPatternRows.Select(x => x.Compliance.MicrotingSdkCaseId), ct).ConfigureAwait(false);
        var offPattern = offPatternRows
            .Select(x => (x.Compliance, OwnCase: ownCases.GetValueOrDefault(x.Compliance.MicrotingSdkCaseId),
                x.OnPatternDate))
            .Where(x => x.OwnCase is not { IsLive: true, IsCompleted: false })
            .ToList();
        if (offPattern.Count == 0)
        {
            plan.PlanHash = HashOf(plan);
            return plan;
        }

        // Live exceptions of the rules involved, as (rule, month) pairs.
        var ruleIds = offPattern.Select(x => rulesByPlanning[x.Compliance.PlanningId][0].Id).Distinct().ToList();
        var exceptionMonths = (await dbContext.CalendarOccurrenceExceptions
                .AsNoTracking()
                .Where(e => ruleIds.Contains(e.AreaRulePlanningId))
                .Where(e => e.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(e => new { e.AreaRulePlanningId, e.OriginalDate, e.NewDate })
                .ToListAsync(ct).ConfigureAwait(false))
            .SelectMany(e => e.NewDate.HasValue
                ? new[] { (e.AreaRulePlanningId, MonthOf(e.OriginalDate)), (e.AreaRulePlanningId, MonthOf(e.NewDate.Value)) }
                : new[] { (e.AreaRulePlanningId, MonthOf(e.OriginalDate)) })
            .ToHashSet();

        // The rest of each occurrence: its PlanningCase, that case's PlanningCaseSites and
        // their SDK cases. The PlanningCase is ALWAYS resolved from the compliance's own SDK
        // case (the PlanningCaseSite that carries it), never from Compliance.PlanningCaseSiteId
        // alone: from 2022 to late 2025 that column held the PlanningCaseSite's own id, and
        // only newer rows hold the PlanningCase id. The stored value must agree with the
        // resolution (either meaning); a row whose occurrence cannot be pinned to exactly one
        // PlanningCase of its own planning cannot be shown to be dead on every site, so it
        // is kept.
        var ownCaseIds = offPattern.Select(x => x.Compliance.MicrotingSdkCaseId).Distinct().ToList();
        var ownSitesByCase = (await itemsPlanningPnDbContext.PlanningCaseSites
                .AsNoTracking()
                .Where(x => ownCaseIds.Contains(x.MicrotingSdkCaseId))
                .Select(x => new { x.Id, x.PlanningCaseId, x.MicrotingSdkCaseId })
                .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(x => x.MicrotingSdkCaseId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var resolvedPlanningCaseIds = ownSitesByCase.Values.SelectMany(x => x).Select(x => x.PlanningCaseId)
            .Where(id => id > 0).Distinct().ToList();
        var planningCases = await itemsPlanningPnDbContext.PlanningCases
            .AsNoTracking()
            .Where(x => resolvedPlanningCaseIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PlanningId, x.Status, x.MicrotingSdkCaseDoneAt })
            .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);

        (int? PlanningCaseId, string Problem) PlanningCaseOf(ComplianceRow c)
        {
            var ownSites = ownSitesByCase.GetValueOrDefault(c.MicrotingSdkCaseId) ?? [];
            var ids = ownSites.Select(x => x.PlanningCaseId).Where(id => id > 0).Distinct().ToList();
            if (ids.Count != 1 || !planningCases.TryGetValue(ids[0], out var planningCase))
            {
                return (null, ReasonNoPlanningCaseLink);
            }
            var storedAgrees = c.PlanningCaseSiteId <= 0
                               || c.PlanningCaseSiteId == planningCase.Id
                               || ownSites.Any(x => x.Id == c.PlanningCaseSiteId);
            return planningCase.PlanningId == c.PlanningId && storedAgrees
                ? (planningCase.Id, null)
                : (null, ReasonPlanningCaseLinkMismatch);
        }

        var planningCaseIds = planningCases.Keys.ToList();
        var sitesByPlanningCase = (await itemsPlanningPnDbContext.PlanningCaseSites
                .AsNoTracking()
                .Where(x => planningCaseIds.Contains(x.PlanningCaseId))
                .Select(x => new { x.PlanningCaseId, x.Status, x.MicrotingSdkCaseDoneAt, x.MicrotingSdkCaseId })
                .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(x => x.PlanningCaseId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var siblingCases = await LoadSdkCasesAsync(sdkDbContext,
            sitesByPlanningCase.Values.SelectMany(x => x).Select(x => x.MicrotingSdkCaseId).Where(id => id > 0), ct)
            .ConfigureAwait(false);

        foreach (var (compliance, ownCase, onPatternDate) in offPattern)
        {
            var rules = rulesByPlanning[compliance.PlanningId];
            var rule = rules[0];
            var reasons = new List<string>();
            if (rules.Count > 1)
            {
                reasons.Add(ReasonSeveralRules);
            }
            if (!string.IsNullOrEmpty(rule.RepeatWeekdaysCsv)
                && !(CalendarService.ParseWeekdaysCsv(rule.RepeatWeekdaysCsv) is [var csvDay] && csvDay == rule.DayOfWeek))
            {
                reasons.Add(ReasonCsvDisagrees);
            }
            if (ownCase is { IsCompleted: true })
            {
                reasons.Add(ReasonCaseCompleted);
            }
            if (compliance.Deadline.Date >= plan.Today)
            {
                reasons.Add(ReasonNotPast);
            }
            if (onPatternDate == null)
            {
                reasons.Add(ReasonNoOnPatternOccurrence);
            }
            if (exceptionMonths.Contains((rule.Id, MonthOf(compliance.Deadline))))
            {
                reasons.Add(ReasonExceptionInMonth);
            }

            var (planningCaseId, linkProblem) = PlanningCaseOf(compliance);
            if (linkProblem != null)
            {
                reasons.Add(linkProblem);
            }
            var sites = planningCaseId is { } pcId ? sitesByPlanningCase.GetValueOrDefault(pcId) ?? [] : [];
            var otherCases = sites
                .Where(s => s.MicrotingSdkCaseId > 0 && s.MicrotingSdkCaseId != compliance.MicrotingSdkCaseId)
                .Select(s => siblingCases.GetValueOrDefault(s.MicrotingSdkCaseId))
                .Where(c => c != null)
                .ToList();
            if ((planningCaseId is { } completedId
                 && (planningCases[completedId].Status == CompletedStatus
                     || planningCases[completedId].MicrotingSdkCaseDoneAt.HasValue))
                || sites.Any(s => s.Status == CompletedStatus || s.MicrotingSdkCaseDoneAt.HasValue)
                || otherCases.Any(c => c.IsCompleted))
            {
                reasons.Add(ReasonCompletedElsewhere);
            }
            if (otherCases.Any(c => c.IsLive && !c.IsCompleted))
            {
                reasons.Add(ReasonOpenElsewhere);
            }

            if (reasons.Count > 0)
            {
                plan.Kept.Add(new OrphanOffPatternComplianceKeptModel
                {
                    ComplianceId = compliance.Id, PlanningId = compliance.PlanningId,
                    SdkCaseId = compliance.MicrotingSdkCaseId, Deadline = compliance.Deadline,
                    OnPatternDate = onPatternDate, Reasons = reasons
                });
                continue;
            }

            plan.Closures.Add(new OrphanOffPatternComplianceClosureModel
            {
                ComplianceId = compliance.Id, PlanningId = compliance.PlanningId, PropertyId = compliance.PropertyId,
                AreaRulePlanningId = rule.Id,
                SdkCaseId = compliance.MicrotingSdkCaseId, Deadline = compliance.Deadline,
                OnPatternDate = onPatternDate!.Value,
                SdkCaseState = ownCase == null ? "Missing"
                    : ownCase.WorkflowState == Constants.WorkflowStates.Retracted ? "Retracted" : "Removed"
            });
        }

        plan.PlanHash = HashOf(plan);
        return plan;
    }

    private sealed record ComplianceRow(int Id, int PlanningId, int PropertyId, DateTime Deadline, int MicrotingSdkCaseId,
        int PlanningCaseSiteId);

    private static (int Year, int Month) MonthOf(DateTime date) => (date.Year, date.Month);

    private static async Task<Dictionary<int, SdkCaseInfo>> LoadSdkCasesAsync(
        Microting.eForm.Infrastructure.MicrotingDbContext sdkDbContext, IEnumerable<int> caseIds, CancellationToken ct)
    {
        var ids = caseIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }
        return await sdkDbContext.Cases
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new SdkCaseInfo(x.Id, x.WorkflowState, x.Status, x.DoneAt))
            .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
    }

    private static string HashOf(OrphanOffPatternComplianceRepairPlanModel plan)
    {
        // The plan date is part of the plan: "in the past" is judged against it, so a
        // reviewed hash is only valid the day it was made.
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"T|{plan.Today:yyyy-MM-dd}\n");
        foreach (var c in plan.Closures)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"C|{c.ComplianceId}|{c.PlanningId}|{c.PropertyId}|{c.AreaRulePlanningId}|{c.SdkCaseId}|{c.Deadline:O}|{c.OnPatternDate:O}|{c.SdkCaseState}\n");
        }
        foreach (var k in plan.Kept)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"K|{k.ComplianceId}|{k.PlanningId}|{k.SdkCaseId}|{k.Deadline:O}|{k.OnPatternDate:O}|{string.Join(",", k.Reasons)}\n");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    // ── Apply ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Closes the plan's rows planning by planning. Before each planning's writes the
    /// planning is evaluated again from the database; a row that is no longer a closure
    /// with the same deadline, case and on-pattern date is skipped and logged. The write
    /// goes through the entity's own <c>Delete</c>, so its version row is written as
    /// always. Runs without a cancellation token on purpose: once writing has started it
    /// must not stop halfway.
    /// </summary>
    private async Task<OrphanOffPatternComplianceRepairRunResultModel> ApplyAsync(
        OrphanOffPatternComplianceRepairPlanModel plan)
    {
        var result = new OrphanOffPatternComplianceRepairRunResultModel { Plan = plan };
        var closedPropertyIds = new SortedSet<int>();
        foreach (var group in plan.Closures.GroupBy(c => c.PlanningId))
        {
            await BeforePlanningRecheck(group.Key).ConfigureAwait(false);
            var now = await ComputeAsync(group.Key, CancellationToken.None).ConfigureAwait(false);
            foreach (var closure in group)
            {
                var what = $"compliance {closure.ComplianceId}";
                var stillQualifies = now.Closures.Any(c => c.ComplianceId == closure.ComplianceId
                                                           && c.Deadline == closure.Deadline
                                                           && c.SdkCaseId == closure.SdkCaseId
                                                           && c.OnPatternDate == closure.OnPatternDate);
                if (!stillQualifies)
                {
                    logger.LogInformation(
                        "CalendarOrphanComplianceRepair: {What} skipped: it no longer qualifies (the next dry run re-evaluates it)",
                        what);
                    result.Skipped.Add($"{what}: no longer qualifies since the plan was computed");
                    continue;
                }

                try
                {
                    var row = await dbContext.Compliances.FirstAsync(x => x.Id == closure.ComplianceId)
                        .ConfigureAwait(false);
                    await row.Delete(dbContext).ConfigureAwait(false);
                    result.ClosedCompliances++;
                    closedPropertyIds.Add(closure.PropertyId);
                    logger.LogInformation(
                        "CalendarOrphanComplianceRepair: compliance {ComplianceId} (planning {PlanningId}, SDK case {SdkCaseId} {CaseState}) on {Deadline:yyyy-MM-dd} closed; the month keeps its occurrence on {OnPattern:yyyy-MM-dd}",
                        closure.ComplianceId, closure.PlanningId, closure.SdkCaseId, closure.SdkCaseState,
                        closure.Deadline, closure.OnPatternDate);
                }
                catch (Exception e)
                {
                    // Forget every pending change — the row's and, when the row itself was
                    // saved but its version insert failed, that version row — or every later
                    // SaveChanges of this context would re-send it (and fail again).
                    dbContext.ChangeTracker.Clear();
                    logger.LogError(e, "CalendarOrphanComplianceRepair: closing {What} failed (the next dry run re-evaluates it)", what);
                    result.Failures.Add($"{what}: {e.Message}");
                }
            }
        }

        foreach (var propertyId in closedPropertyIds)
        {
            try
            {
                await RecomputePropertyComplianceStatusAsync(propertyId).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                dbContext.ChangeTracker.Clear();
                logger.LogError(e, "CalendarOrphanComplianceRepair: recomputing the compliance status of property {PropertyId} failed", propertyId);
                result.Failures.Add($"compliance status of property {propertyId}: {e.Message}");
            }
        }
        return result;
    }

    /// <summary>
    /// A closed orphan was an overdue compliance of its property, so the property's
    /// traffic light (ComplianceStatus / ComplianceStatusThirty) is recomputed as
    /// <c>BackendConfigurationCompliancesService</c> does after a compliance is completed,
    /// except that ComplianceStatusThirty is always set from current data: the closed orphan
    /// left it at 2, and with a compliance still due within 30 days it becomes 1 (as the
    /// service plugin's periodic recompute sets it), not a stale 2.
    /// </summary>
    private async Task RecomputePropertyComplianceStatusAsync(int propertyId)
    {
        var property = await dbContext.Properties.SingleAsync(x => x.Id == propertyId).ConfigureAwait(false);
        var utcNow = UtcNow();
        if (await HiddenOverdueRule.ExcludeNeverOverdue(dbContext.Compliances.AsNoTracking(), dbContext, utcNow)
                .AnyAsync(x => x.Deadline < utcNow && x.PropertyId == propertyId
                               && x.WorkflowState != Constants.WorkflowStates.Removed).ConfigureAwait(false))
        {
            property.ComplianceStatus = 2;
            property.ComplianceStatusThirty = 2;
            await property.Update(dbContext).ConfigureAwait(false);
            return;
        }

        var dueWithinThirtyDays = await dbContext.Compliances.AsNoTracking()
            .AnyAsync(x => x.Deadline < utcNow.AddDays(30) && x.PropertyId == propertyId
                           && x.WorkflowState != Constants.WorkflowStates.Removed).ConfigureAwait(false);
        property.ComplianceStatusThirty = dueWithinThirtyDays ? 1 : 0;
        // No overdue compliance is left (the check above), so the property is not red.
        property.ComplianceStatus = 0;
        await property.Update(dbContext).ConfigureAwait(false);
    }

    /// <summary>
    /// One summary line; the items themselves only at Debug for a dry run (a GET must not
    /// flood the log), and at Information for the real run.
    /// </summary>
    private void LogPlan(OrphanOffPatternComplianceRepairPlanModel plan, bool isRun)
    {
        var mode = isRun ? "run" : "dry run";
        var itemLevel = isRun ? LogLevel.Information : LogLevel.Debug;
        logger.LogInformation(
            "CalendarOrphanComplianceRepair ({Mode}): {Closures} closures, {Kept} kept, today {Today:yyyy-MM-dd}, plan {PlanHash}",
            mode, plan.Closures.Count, plan.Kept.Count, plan.Today, plan.PlanHash);
        foreach (var kept in plan.Kept)
        {
            logger.Log(itemLevel,
                "CalendarOrphanComplianceRepair ({Mode}): KEPT compliance {ComplianceId} planning {PlanningId} case {SdkCaseId} on {Deadline:yyyy-MM-dd} ({Reasons})",
                mode, kept.ComplianceId, kept.PlanningId, kept.SdkCaseId, kept.Deadline, string.Join(", ", kept.Reasons));
        }
    }
}
