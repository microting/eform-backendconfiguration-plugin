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
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using Backfill = BackendConfiguration.Pn.Services.CalendarConfigurationBackfillService.CalendarConfigurationBackfillService;
using CalendarService = BackendConfiguration.Pn.Services.BackendConfigurationCalendarService.BackendConfigurationCalendarService;

namespace BackendConfiguration.Pn.Services.CalendarWeeklyReanchorRepair;

/// <summary>
/// #1375 — one-time repair of the weekly rules the calendar conversion
/// (<c>CalendarConfigurationBackfillService.NormalizeRecurrence</c>) gave the weekday of
/// <c>Planning.StartDate</c>, while the legacy scheduler's cadence
/// (<c>NextExecutionTime</c>) and the open compliance it deployed sit on another weekday
/// (e.g. a Monday rule with Thursday deploys): the calendar drew such a task twice a week.
///
/// <para>The rule is moved onto the cadence's weekday, exactly as the conversion now
/// does (<c>CalendarConfigurationBackfillService.ConvertedWeekday</c>): first the
/// planning (<c>DayOfWeek</c>, the mirror of the rule's weekday; and, for an every-N-weeks
/// rule whose cadence sits in the weeks the rule does not draw, <c>NextExecutionTime</c>
/// moved forward onto the rule), then the rule (<c>DayOfWeek</c>,
/// <c>RepeatWeekdaysCsv</c>). The cadence's weekday is the one the open compliance
/// already carries, so no compliance is moved and nothing is deleted.</para>
///
/// <para>Only converted rules nobody edited since the conversion, whose weekday is still
/// the conversion's, and whose open compliances agree with the cadence are written; the
/// rest goes on the review list. Multi-day weekly rules are not this defect and are not
/// looked at.</para>
///
/// <para>Same opt-in contract as the monthly repair (#1294): admin endpoint only, the
/// real run needs the hash of a reviewed dry run, every row is re-read right before its
/// write and skipped when it changed, every write is logged, and a marker moves
/// <c>running</c> → <c>done</c> | <c>partial</c>.</para>
/// </summary>
public class CalendarWeeklyReanchorRepairService(
    BackendConfigurationPnDbContext dbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IEFormCoreService coreHelper,
    ILogger<CalendarWeeklyReanchorRepairService> logger) : ICalendarWeeklyReanchorRepairService
{
    /// <summary>The run's marker; same inert prefix as the monthly repair's. Public for the fixture.</summary>
    public const string MarkerName = "BackendConfigurationBaseSettings:WeeklyReanchorRepaired";

    private const int WeekRepeatType = 2;
    private const int CompletedStatus = 100;

    internal const string ReasonCsvDisagrees = "WeekdayCsvDisagreesWithDayOfWeek";
    internal const string ReasonEditedAfterConversion = "EditedAfterConversion";
    internal const string ReasonSystemWriteAfterConversion = "SystemWriteAfterConversion";
    internal const string ReasonWeekdayChanged = "WeekdayChangedSinceConversion";
    internal const string ReasonOpenComplianceOnAnotherWeekday = "OpenComplianceOnAnotherWeekday";

    private const string ChangedSincePlan = "changed since the plan was computed";

    private readonly CalendarRepairRunMarker _marker = new(dbContext, MarkerName);

    /// <summary>Clock seam for "today in Copenhagen".</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>Test seam: runs right before every write of the real run, with the entity about to be written.</summary>
    internal Func<object, Task> OnBeforeWrite { get; set; } = _ => Task.CompletedTask;

    public async Task<OperationDataResult<WeeklyReanchorRepairPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var work = await ComputeAsync(cancellationToken).ConfigureAwait(false);
            var marker = await _marker.ReadAsync(cancellationToken).ConfigureAwait(false);
            work.Model.MarkerState = CalendarRepairRunMarker.StateOf(marker?.Value);
            work.Model.AlreadyExecuted = marker?.Value == CalendarRepairRunMarker.Done;
            LogPlan(work.Model, isRun: false);
            return new OperationDataResult<WeeklyReanchorRepairPlanModel>(true, work.Model);
        }
        catch (Exception e)
        {
            logger.LogError(e, "CalendarWeeklyReanchorRepair: dry run failed");
            return new OperationDataResult<WeeklyReanchorRepairPlanModel>(false,
                $"Weekly re-anchor dry run failed: {e.Message}");
        }
    }

    public async Task<OperationDataResult<WeeklyReanchorRepairRunResultModel>> RunAsync(
        string planHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHash))
        {
            return Refused("planHash is required: run the dry run, review it, and pass its PlanHash.");
        }

        string claimToken = null;
        try
        {
            var marker = await _marker.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (marker != null && !CalendarRepairRunMarker.IsClaimable(marker))
            {
                return Refused(CalendarRepairRunMarker.IsRunning(marker.Value)
                    ? "A weekly re-anchor run is already in progress."
                    : "The weekly re-anchor repair has already been executed on this installation.");
            }

            var work = await ComputeAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(planHash, work.Model.PlanHash, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "CalendarWeeklyReanchorRepair: refused — plan hash {Given} does not match the current plan {Current}; nothing was written",
                    planHash, work.Model.PlanHash);
                return Refused("The data changed since the reviewed dry run (plan hash mismatch). Run the dry run again and review it.");
            }
            var planIsEmpty = work.Rules.Count == 0;
            if (planIsEmpty && marker == null)
            {
                // Never burn the marker on a run that writes nothing.
                return Refused("The plan has nothing to write; the repair was not started.");
            }

            claimToken = await _marker.ClaimAsync(marker != null).ConfigureAwait(false);
            if (claimToken == null)
            {
                return Refused("Another weekly re-anchor run claimed the repair first.");
            }

            WeeklyReanchorRepairRunResultModel result;
            var claimLost = false;
            if (planIsEmpty)
            {
                // A resumed partial run with nothing left to do: finish it.
                result = new WeeklyReanchorRepairRunResultModel { Plan = work.Model };
            }
            else
            {
                // From here on the run writes; a closed browser tab must not stop it halfway.
                LogPlan(work.Model, isRun: true);
                (result, claimLost) = await ApplyAsync(work, claimToken).ConfigureAwait(false);
            }

            if (!claimLost)
            {
                result.ArrivedDuringRun.AddRange(await WritesArrivedDuringRunAsync(work).ConfigureAwait(false));
                var state = result.Failures.Count == 0 && result.Skipped.Count == 0 && result.ArrivedDuringRun.Count == 0
                    ? CalendarRepairRunMarker.Done
                    : CalendarRepairRunMarker.Partial;
                if (!await _marker.FinishAsync(claimToken, state).ConfigureAwait(false))
                {
                    RecordClaimLost(result);
                }
            }
            logger.LogInformation(
                "CalendarWeeklyReanchorRepair: finished — {Rules} rules updated, {Skipped} skipped, {Arrived} new writes arrived during the run, {Failures} failures",
                result.UpdatedRules, result.Skipped.Count, result.ArrivedDuringRun.Count, result.Failures.Count);
            return new OperationDataResult<WeeklyReanchorRepairRunResultModel>(result.Failures.Count == 0, result);
        }
        catch (Exception e)
        {
            logger.LogError(e, "CalendarWeeklyReanchorRepair: run failed");
            if (claimToken != null)
            {
                try
                {
                    await _marker.FinishAsync(claimToken, CalendarRepairRunMarker.Partial).ConfigureAwait(false);
                }
                catch (Exception markerError)
                {
                    logger.LogError(markerError, "CalendarWeeklyReanchorRepair: could not mark the run partial");
                }
            }
            return new OperationDataResult<WeeklyReanchorRepairRunResultModel>(false,
                $"Weekly re-anchor repair failed: {e.Message}");
        }
    }

    private static OperationDataResult<WeeklyReanchorRepairRunResultModel> Refused(string message)
        => new(false, message);

    private const string ClaimLostMessage =
        "this run's claim was taken over by another run (abandoned-run reclaim); it stopped writing and left the marker to that run";

    private void RecordClaimLost(WeeklyReanchorRepairRunResultModel result)
    {
        logger.LogError("CalendarWeeklyReanchorRepair: {ClaimLost}", ClaimLostMessage);
        result.Failures.Add(ClaimLostMessage);
    }

    // ── Plan ────────────────────────────────────────────────────────────────

    private sealed class Work
    {
        public readonly WeeklyReanchorRepairPlanModel Model = new();
        public readonly List<RuleWork> Rules = [];
    }

    /// <summary>One rule's writes, with the values the plan saw so each write can check they still hold.</summary>
    private sealed class RuleWork
    {
        public required AreaRulePlanning Arp { get; init; }
        public required Planning Planning { get; init; }
        public required DayOfWeek Target { get; init; }
        public DateTime? NewNextExecutionTime { get; init; }

        public int SeenArpDayOfWeek { get; init; }
        public string SeenArpCsv { get; init; }
        public DateTime? SeenArpUpdatedAt { get; init; }
        public DayOfWeek? SeenPlanningDayOfWeek { get; init; }
        public DateTime? SeenNextExecutionTime { get; init; }
        public DateTime? SeenLastExecutedTime { get; init; }
        public DateTime SeenPlanningStartDate { get; init; }
        public int SeenPlanningRepeatEvery { get; init; }
    }

    /// <summary>
    /// Read-only: computes every write of the repair against tracked entities, so the real
    /// run applies exactly this and the dry run reports exactly this.
    ///
    /// <para><b>Converted, and not edited since</b> — the same signature the monthly repair
    /// relies on: the conversion's <c>CalendarConfiguration</c> (<c>CreatedByUserId == 0</c>)
    /// is created right after it writes the rule, and a rule untouched since has an
    /// <c>UpdatedAt</c> no later than that row's <c>CreatedAt</c> and still carries the
    /// weekday of <c>Planning.StartDate</c>.</para>
    /// </summary>
    private async Task<Work> ComputeAsync(CancellationToken ct)
    {
        var work = new Work();
        var today = ComplianceFutureTaskGuard.TodayInCopenhagen(UtcNow());
        work.Model.Today = today;

        // Single-weekday weekly rules of converted wizard tasks; one rule per planning (the
        // lowest-Id live ARP, as the monthly repair and the compliance pages resolve it).
        var arps = (await dbContext.AreaRulePlannings
                .Include(x => x.AreaRule)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.RepeatType == WeekRepeatType && x.ItemPlanningId > 0)
                .Where(x => x.AreaRule.CreatedInGuide)
                .OrderBy(x => x.Id)
                .ToListAsync(ct).ConfigureAwait(false))
            .Where(x => CalendarService.ParseWeekdaysCsv(x.RepeatWeekdaysCsv).Length <= 1)
            .GroupBy(x => x.ItemPlanningId)
            .Select(g => g.First())
            .ToList();
        var arpIds = arps.Select(x => x.Id).ToList();
        var convertedAt = (await dbContext.CalendarConfigurations
                .AsNoTracking()
                .Where(x => arpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.CreatedByUserId == 0)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => new { x.AreaRulePlanningId, x.CreatedAt })
                .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(x => x.AreaRulePlanningId)
            .ToDictionary(g => g.Key, g => g.Min(x => x.CreatedAt));
        arps = arps.Where(x => convertedAt.ContainsKey(x.Id)).ToList();

        var planningIds = arps.Select(x => x.ItemPlanningId).ToList();
        var plannings = await itemsPlanningPnDbContext.Plannings
            .Where(x => planningIds.Contains(x.Id))
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.RepeatType == RepeatType.Week)
            .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
        var openCompliances = await OpenCompliancesAsync(plannings.Keys.ToList(), ct).ConfigureAwait(false);

        foreach (var arp in arps)
        {
            if (!plannings.TryGetValue(arp.ItemPlanningId, out var planning))
            {
                continue;
            }

            var target = Backfill.ConvertedWeekday(planning);
            // A next run already in the past (typically a deactivated task) is left as it is,
            // as in the monthly repair: the scheduler skips it, and reactivation takes its
            // dates from the dialog.
            // The aligned run is compared with the current one, not just its weekday: an
            // every-N-weeks cadence can sit on the target weekday in the weeks the rule does
            // not draw.
            DateTime? newNext = null;
            if (planning.NextExecutionTime is { } n && n.Date >= today)
            {
                var aligned = CalendarService.NextWeeklyOccurrenceOnOrAfter(planning.StartDate, planning.RepeatEvery,
                    target, n < planning.StartDate ? planning.StartDate : n);
                if (aligned != n)
                {
                    newNext = aligned;
                }
            }
            var targetCsv = ((int)target).ToString(CultureInfo.InvariantCulture);
            if (arp.DayOfWeek == (int)target && arp.RepeatWeekdaysCsv == targetCsv
                && planning.DayOfWeek == target && newNext == null)
            {
                continue; // aligned
            }

            var reasons = new List<string>();
            if (!string.IsNullOrEmpty(arp.RepeatWeekdaysCsv)
                && CalendarService.ParseWeekdaysCsv(arp.RepeatWeekdaysCsv) is [var csvDay] && csvDay != arp.DayOfWeek)
            {
                reasons.Add(ReasonCsvDisagrees);
            }
            if (arp.DayOfWeek != (int)planning.StartDate.DayOfWeek)
            {
                reasons.Add(ReasonWeekdayChanged);
            }
            if (arp.UpdatedAt > convertedAt[arp.Id])
            {
                reasons.Add(arp.UpdatedByUserId == 0 ? ReasonSystemWriteAfterConversion : ReasonEditedAfterConversion);
            }
            var offWeekday = openCompliances.TryGetValue(planning.Id, out var open)
                ? open.Where(c => c.Deadline.DayOfWeek != target).Select(c => c.Id).OrderBy(id => id).ToList()
                : [];
            if (offWeekday.Count > 0)
            {
                reasons.Add(ReasonOpenComplianceOnAnotherWeekday);
            }

            if (reasons.Count > 0)
            {
                work.Model.ReviewItems.Add(new WeeklyReanchorReviewItemModel
                {
                    AreaRulePlanningId = arp.Id, PlanningId = planning.Id, RuleDayOfWeek = arp.DayOfWeek,
                    TargetDayOfWeek = (int)target, ComplianceIds = offWeekday, Reasons = reasons
                });
                continue;
            }

            work.Rules.Add(new RuleWork
            {
                Arp = arp, Planning = planning, Target = target, NewNextExecutionTime = newNext,
                SeenArpDayOfWeek = arp.DayOfWeek, SeenArpCsv = arp.RepeatWeekdaysCsv, SeenArpUpdatedAt = arp.UpdatedAt,
                SeenPlanningDayOfWeek = planning.DayOfWeek, SeenNextExecutionTime = planning.NextExecutionTime,
                SeenLastExecutedTime = planning.LastExecutedTime, SeenPlanningStartDate = planning.StartDate,
                SeenPlanningRepeatEvery = planning.RepeatEvery
            });
            work.Model.RuleUpdates.Add(new WeeklyReanchorRuleUpdateModel
            {
                AreaRulePlanningId = arp.Id, PlanningId = planning.Id,
                OldRuleDayOfWeek = arp.DayOfWeek, OldWeekdaysCsv = arp.RepeatWeekdaysCsv,
                OldPlanningDayOfWeek = (int?)planning.DayOfWeek, NewDayOfWeek = (int)target,
                OldNextExecutionTime = planning.NextExecutionTime,
                NewNextExecutionTime = newNext ?? planning.NextExecutionTime
            });
        }

        work.Model.PlanHash = HashOf(work.Model);
        return work;
    }

    /// <summary>
    /// The open compliances of <paramref name="planningIds"/>: not removed, and backed by a
    /// live, uncompleted SDK case — the rows the calendar draws as this week's occurrence.
    /// </summary>
    private async Task<Dictionary<int, List<Compliance>>> OpenCompliancesAsync(List<int> planningIds, CancellationToken ct)
    {
        if (planningIds.Count == 0)
        {
            return [];
        }
        var rows = await dbContext.Compliances
            .AsNoTracking()
            .Where(c => planningIds.Contains(c.PlanningId))
            .Where(c => c.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(c => c.MicrotingSdkCaseId > 0)
            .ToListAsync(ct).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return [];
        }
        var caseIds = rows.Select(c => c.MicrotingSdkCaseId).Distinct().ToList();
        var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();
        var openCaseIds = (await sdkDbContext.Cases
                .AsNoTracking()
                .Where(x => caseIds.Contains(x.Id))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                            && x.WorkflowState != Constants.WorkflowStates.Retracted)
                .Where(x => x.Status != CompletedStatus && x.DoneAt == null)
                .Select(x => x.Id)
                .ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet();
        return rows.Where(c => openCaseIds.Contains(c.MicrotingSdkCaseId))
            .GroupBy(c => c.PlanningId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static string HashOf(WeeklyReanchorRepairPlanModel plan)
    {
        // The plan date is part of the plan ("in the past" is judged against it), so a
        // reviewed hash is only valid the day it was made.
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"T|{plan.Today:yyyy-MM-dd}\n");
        foreach (var r in plan.RuleUpdates)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"U|{r.AreaRulePlanningId}|{r.PlanningId}|{r.OldRuleDayOfWeek}|{r.OldWeekdaysCsv}|{r.OldPlanningDayOfWeek}|{r.NewDayOfWeek}|{r.OldNextExecutionTime:O}|{r.NewNextExecutionTime:O}\n");
        }
        foreach (var v in plan.ReviewItems)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"V|{v.AreaRulePlanningId}|{v.PlanningId}|{v.RuleDayOfWeek}|{v.TargetDayOfWeek}|{string.Join(",", v.ComplianceIds)}|{string.Join(",", v.Reasons)}\n");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    // ── Apply ───────────────────────────────────────────────────────────────

    private enum WriteOutcome { Written, Skipped, Failed }

    /// <summary>
    /// Applies the plan rule by rule: the planning first, then the rule (the order the
    /// conversion writes them in). Every write first re-reads its row and checks it still
    /// holds the values the plan saw; a changed row is skipped and logged, and the rule's
    /// remaining write is then not attempted. Runs without a cancellation token on
    /// purpose: once writing has started it must not stop halfway.
    /// </summary>
    private async Task<(WeeklyReanchorRepairRunResultModel Result, bool ClaimLost)> ApplyAsync(
        Work work, string claimToken)
    {
        var result = new WeeklyReanchorRepairRunResultModel { Plan = work.Model };
        for (var index = 0; index < work.Rules.Count; index++)
        {
            var rw = work.Rules[index];
            if (index > 0 && !await _marker.RenewAsync(claimToken).ConfigureAwait(false))
            {
                RecordClaimLost(result);
                result.Skipped.AddRange(work.Rules.Skip(index)
                    .Select(r => $"AreaRulePlanning {r.Arp.Id}: not attempted: the run lost its claim"));
                return (result, true);
            }

            var planning = rw.Planning;
            var planningOutcome = await WriteAsync(itemsPlanningPnDbContext, planning, result, $"planning {planning.Id}",
                () => PlanningChangedSincePlanAsync(rw),
                () =>
                {
                    planning.DayOfWeek = rw.Target;
                    if (rw.NewNextExecutionTime.HasValue)
                    {
                        planning.NextExecutionTime = rw.NewNextExecutionTime;
                    }
                    return planning.Update(itemsPlanningPnDbContext);
                }).ConfigureAwait(false);
            if (planningOutcome != WriteOutcome.Written)
            {
                result.Skipped.Add($"AreaRulePlanning {rw.Arp.Id}: not attempted: planning {planning.Id} was not written");
                continue;
            }
            logger.LogInformation(
                "CalendarWeeklyReanchorRepair: planning {PlanningId} DayOfWeek {OldDow} -> {NewDow}, NextExecutionTime {OldNext:yyyy-MM-dd} -> {NewNext:yyyy-MM-dd}",
                planning.Id, rw.SeenPlanningDayOfWeek, planning.DayOfWeek, rw.SeenNextExecutionTime, planning.NextExecutionTime);

            var arp = rw.Arp;
            var arpOutcome = await WriteAsync(dbContext, arp, result, $"AreaRulePlanning {arp.Id}",
                async () =>
                {
                    var now = await dbContext.AreaRulePlannings.AsNoTracking()
                        .Where(x => x.Id == arp.Id)
                        .Select(x => new { x.DayOfWeek, x.RepeatWeekdaysCsv, x.RepeatType, x.UpdatedAt, x.WorkflowState })
                        .FirstOrDefaultAsync().ConfigureAwait(false);
                    return now != null && now.WorkflowState != Constants.WorkflowStates.Removed
                                       && now.RepeatType == WeekRepeatType
                                       && now.DayOfWeek == rw.SeenArpDayOfWeek
                                       && now.RepeatWeekdaysCsv == rw.SeenArpCsv
                                       && now.UpdatedAt == rw.SeenArpUpdatedAt
                        ? null
                        : ChangedSincePlan;
                },
                () =>
                {
                    arp.DayOfWeek = (int)rw.Target;
                    arp.RepeatWeekdaysCsv = ((int)rw.Target).ToString(CultureInfo.InvariantCulture);
                    return arp.Update(dbContext);
                }).ConfigureAwait(false);
            if (arpOutcome != WriteOutcome.Written)
            {
                continue;
            }
            result.UpdatedRules++;
            logger.LogInformation(
                "CalendarWeeklyReanchorRepair: AreaRulePlanning {ArpId} (planning {PlanningId}) weekday {OldDow} -> {NewDow}, weekday list {OldCsv} -> {NewCsv}",
                arp.Id, planning.Id, rw.SeenArpDayOfWeek, arp.DayOfWeek, rw.SeenArpCsv, arp.RepeatWeekdaysCsv);
        }

        // One more renewal confirms the claim was held throughout before the run reports success.
        if (!await _marker.RenewAsync(claimToken).ConfigureAwait(false))
        {
            RecordClaimLost(result);
            return (result, true);
        }
        return (result, false);
    }

    /// <summary>Null when the planning row still holds every value the plan was computed from.</summary>
    private async Task<string> PlanningChangedSincePlanAsync(RuleWork rw)
    {
        var now = await itemsPlanningPnDbContext.Plannings.AsNoTracking()
            .Where(x => x.Id == rw.Planning.Id)
            .Select(x => new
            {
                x.DayOfWeek, x.NextExecutionTime, x.LastExecutedTime, x.StartDate, x.RepeatType, x.RepeatEvery,
                x.WorkflowState
            })
            .FirstOrDefaultAsync().ConfigureAwait(false);
        return now != null && now.WorkflowState != Constants.WorkflowStates.Removed
                           && now.RepeatType == RepeatType.Week
                           && now.DayOfWeek == rw.SeenPlanningDayOfWeek
                           && now.NextExecutionTime == rw.SeenNextExecutionTime
                           && now.LastExecutedTime == rw.SeenLastExecutedTime
                           && now.StartDate == rw.SeenPlanningStartDate
                           && now.RepeatEvery == rw.SeenPlanningRepeatEvery
            ? null
            : $"planning {ChangedSincePlan}";
    }

    /// <summary>
    /// Recomputes the plan after the run (while the claim is still held) and returns the
    /// rules it now wants to update that were NOT in the executed plan — work that arrived
    /// after the plan was computed and so was never reviewed.
    /// </summary>
    private async Task<List<string>> WritesArrivedDuringRunAsync(Work executed)
    {
        var executedArpIds = executed.Model.RuleUpdates.Select(r => r.AreaRulePlanningId).ToHashSet();
        dbContext.ChangeTracker.Clear();
        itemsPlanningPnDbContext.ChangeTracker.Clear();
        var now = await ComputeAsync(CancellationToken.None).ConfigureAwait(false);
        var arrived = now.Model.RuleUpdates
            .Where(r => !executedArpIds.Contains(r.AreaRulePlanningId))
            .Select(r => $"AreaRulePlanning {r.AreaRulePlanningId}")
            .ToList();
        foreach (var key in arrived)
        {
            logger.LogWarning(
                "CalendarWeeklyReanchorRepair: {Write} became eligible during the run; not written — it needs a new reviewed dry run",
                key);
        }
        return arrived;
    }

    /// <summary>
    /// One row's write: <paramref name="whyNot"/> returning a reason → skipped (logged);
    /// an exception → failed (logged, recorded) and the row's pending change discarded, so
    /// a later SaveChanges of the same context does not re-send it.
    /// </summary>
    private async Task<WriteOutcome> WriteAsync(DbContext context, object entity,
        WeeklyReanchorRepairRunResultModel result, string what, Func<Task<string>> whyNot, Func<Task> write)
    {
        try
        {
            var reason = await whyNot().ConfigureAwait(false);
            if (reason != null)
            {
                logger.LogInformation(
                    "CalendarWeeklyReanchorRepair: {What} skipped: {Reason} (the next dry run re-evaluates it)",
                    what, reason);
                result.Skipped.Add($"{what}: {reason}");
                return WriteOutcome.Skipped;
            }
            await OnBeforeWrite(entity).ConfigureAwait(false);
            await write().ConfigureAwait(false);
            return WriteOutcome.Written;
        }
        catch (Exception e)
        {
            var entry = context.Entry(entity);
            entry.CurrentValues.SetValues(entry.OriginalValues);
            entry.State = EntityState.Unchanged;
            logger.LogError(e, "CalendarWeeklyReanchorRepair: writing {What} failed; left unchanged", what);
            result.Failures.Add($"{what}: {e.Message}");
            return WriteOutcome.Failed;
        }
    }

    /// <summary>One summary line; the review items at Debug for a dry run and as warnings for the real run.</summary>
    private void LogPlan(WeeklyReanchorRepairPlanModel plan, bool isRun)
    {
        var mode = isRun ? "run" : "dry run";
        logger.LogInformation(
            "CalendarWeeklyReanchorRepair ({Mode}): {Rules} rule updates, {Review} for review, today {Today:yyyy-MM-dd}, plan {PlanHash}",
            mode, plan.RuleUpdates.Count, plan.ReviewItems.Count, plan.Today, plan.PlanHash);
        foreach (var item in plan.ReviewItems)
        {
            logger.Log(isRun ? LogLevel.Warning : LogLevel.Debug,
                "CalendarWeeklyReanchorRepair ({Mode}): REVIEW AreaRulePlanning {ArpId} planning {PlanningId}: weekday {Current} -> {Target} not written ({Reasons}); open compliances on another weekday: {Compliances}",
                mode, item.AreaRulePlanningId, item.PlanningId, item.RuleDayOfWeek, item.TargetDayOfWeek,
                string.Join(", ", item.Reasons), string.Join(", ", item.ComplianceIds));
        }
    }
}
