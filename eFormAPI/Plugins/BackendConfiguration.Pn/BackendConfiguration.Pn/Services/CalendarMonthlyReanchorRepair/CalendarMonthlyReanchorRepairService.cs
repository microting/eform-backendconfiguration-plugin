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
using CalendarService = BackendConfiguration.Pn.Services.BackendConfigurationCalendarService.BackendConfigurationCalendarService;
using SdkDbContext = Microting.eForm.Infrastructure.MicrotingDbContext;

namespace BackendConfiguration.Pn.Services.CalendarMonthlyReanchorRepair;

/// <summary>
/// #1294 — one-time repair of the monthly rules the calendar conversion
/// (<c>CalendarConfigurationBackfillService.NormalizeRecurrence</c>) re-encoded
/// as "1st &lt;weekday&gt;" without touching the data the legacy "day N of the
/// month" rule had already produced.
///
/// <para>The conversion left three things behind, repaired here in this order,
/// per planning:</para>
/// <list type="number">
/// <item>The ordinal was hardcoded to 1, so "on the 17th" became "1st Wednesday".
/// The legacy week is restored from <c>Planning.StartDate</c> — the same date the
/// conversion took the weekday from — through the canonical
/// <c>OrdinalWeekOf</c> (29th–31st give 5, which every Month producer and the
/// scheduler spill to the month's last such weekday, #1289). Only for rules nobody
/// edited since the conversion; see <see cref="ComputeAsync"/>. Done FIRST, so the
/// compliances below are moved once, straight onto the restored pattern.</item>
/// <item><c>Planning.DayOfWeek</c> kept a stale legacy weekday (the scheduler
/// snaps with it) and <c>Planning.NextExecutionTime</c> the legacy day: mirrored
/// from the ARP and re-snapped within its month, on every Month-Nth rule.</item>
/// <item>The open compliance of the running period kept the legacy deadline, so
/// the calendar painted it on the old day AND the rule's own occurrence on the new
/// day. It is moved to the rule's day in the same month.</item>
/// </list>
///
/// <para>Nothing is deleted. Rows a move could hurt are put on the review list
/// instead; orphans (compliance whose SDK case is removed or completed) are
/// skipped — they belong to #1325's cleanup.</para>
///
/// <para>The real run is opt-in and reviewed: it only runs from the admin endpoint,
/// only with the hash of a dry run, and is tracked by a
/// <c>PluginConfigurationValues</c> marker whose value moves
/// <c>running</c> → <c>done</c> | <c>partial</c>. A <c>partial</c> run (or a
/// <c>running</c> one abandoned for over an hour) may be run again: the plan is
/// recomputed from the data, so rows already repaired simply drop out of it.</para>
/// </summary>
public class CalendarMonthlyReanchorRepairService(
    BackendConfigurationPnDbContext dbContext,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    IEFormCoreService coreHelper,
    ILogger<CalendarMonthlyReanchorRepairService> logger) : ICalendarMonthlyReanchorRepairService
{
    /// <summary>
    /// The run's marker. Same "BackendConfigurationBaseSettings:" prefix as
    /// <c>LegacyStartHourRepairMarkerName</c>, for the reason given there: the section is
    /// not bound, so the marker is inert configuration. Public so the integration
    /// fixture can clear it between tests.
    /// </summary>
    public const string MarkerName = "BackendConfigurationBaseSettings:MonthlyReanchorRepaired";

    public const string MarkerRunning = "running";
    public const string MarkerDone = "done";
    public const string MarkerPartial = "partial";

    /// <summary>A <c>running</c> marker older than this belongs to a crashed run and may be re-claimed.</summary>
    internal static readonly TimeSpan AbandonedRunAfter = TimeSpan.FromHours(1);

    private const int MonthRepeatType = 3;
    private const int CompletedStatus = 100;

    internal const string ReasonCollision = "Collision";
    internal const string ReasonOccurrenceException = "OccurrenceExceptionOnOldDate";
    internal const string ReasonOccurrenceExceptionOnTarget = "OccurrenceExceptionOnTargetDate";
    internal const string ReasonBeforeStartDate = "TargetBeforeComplianceStartDate";
    internal const string ReasonBeforeToday = "TargetBeforeToday";
    internal const string ReasonOldDeadlineBeforeToday = "OverdueDeadline";
    internal const string ReasonMovedToExpiredFolder = "MovedToExpiredFolder";
    internal const string ReasonCloudCaseEndDate = "CloudCaseEndsBeforeTarget";
    internal const string ReasonSiblingCaseCompleted = "SiblingSiteCaseCompleted";
    internal const string ReasonCsvDisagrees = "WeekdayCsvDisagreesWithDayOfWeek";
    internal const string ReasonEditedAfterConversion = "EditedAfterConversion";
    internal const string ReasonSystemWriteAfterConversion = "SystemWriteAfterConversion";
    internal const string ReasonWeekdayChanged = "WeekdayChangedSinceConversion";
    internal const string ReasonCaseRemoved = "SdkCaseRemoved";
    internal const string ReasonCaseCompleted = "SdkCaseCompleted";

    /// <summary>Clock seam for "today in Copenhagen" (same shape as the #1300 seams).</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>
    /// Test seam: runs right before every write of the real run, with the entity about to
    /// be written. Throwing from it is indistinguishable from a failing write.
    /// </summary>
    internal Func<object, Task> OnBeforeWrite { get; set; } = _ => Task.CompletedTask;

    public async Task<OperationDataResult<MonthlyReanchorRepairPlanModel>> DryRunAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var work = await ComputeAsync(cancellationToken).ConfigureAwait(false);
            var marker = await ReadMarkerAsync(cancellationToken).ConfigureAwait(false);
            work.Model.MarkerState = MarkerStateOf(marker?.Value);
            work.Model.AlreadyExecuted = marker?.Value == MarkerDone;
            LogPlan(work.Model, isRun: false);
            return new OperationDataResult<MonthlyReanchorRepairPlanModel>(true, work.Model);
        }
        catch (Exception e)
        {
            logger.LogError(e, "CalendarMonthlyReanchorRepair: dry run failed");
            return new OperationDataResult<MonthlyReanchorRepairPlanModel>(false,
                $"Monthly re-anchor dry run failed: {e.Message}");
        }
    }

    public async Task<OperationDataResult<MonthlyReanchorRepairRunResultModel>> RunAsync(
        string planHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHash))
        {
            return Refused("planHash is required: run the dry run, review it, and pass its PlanHash.");
        }

        string claimToken = null;
        try
        {
            var marker = await ReadMarkerAsync(cancellationToken).ConfigureAwait(false);
            if (marker != null && !IsClaimable(marker.Value, marker.UpdatedAt))
            {
                return Refused(IsRunning(marker.Value)
                    ? "A monthly re-anchor run is already in progress."
                    : "The monthly re-anchor repair has already been executed on this installation.");
            }

            var work = await ComputeAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(planHash, work.Model.PlanHash, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "CalendarMonthlyReanchorRepair: refused — plan hash {Given} does not match the current plan {Current}; nothing was written",
                    planHash, work.Model.PlanHash);
                return Refused("The data changed since the reviewed dry run (plan hash mismatch). Run the dry run again and review it.");
            }
            var planIsEmpty = work.Model.OrdinalRestorations.Count == 0 && work.Model.PlanningUpdates.Count == 0
                              && work.Model.ComplianceMoves.Count == 0;
            if (planIsEmpty && marker == null)
            {
                // Never burn the marker on a run that writes nothing.
                return Refused("The plan has nothing to write; the repair was not started.");
            }

            claimToken = await ClaimMarkerAsync(marker != null).ConfigureAwait(false);
            if (claimToken == null)
            {
                return Refused("Another monthly re-anchor run claimed the repair first.");
            }

            MonthlyReanchorRepairRunResultModel result;
            var claimLost = false;
            if (planIsEmpty)
            {
                // A resumed partial run with nothing left to do: finish it, or the marker
                // would stay "partial" for ever.
                result = new MonthlyReanchorRepairRunResultModel { Plan = work.Model };
            }
            else
            {
                // From here on the run writes; a closed browser tab must not stop it halfway.
                LogPlan(work.Model, isRun: true);
                (result, claimLost) = await ApplyAsync(work, claimToken).ConfigureAwait(false);
            }

            if (!claimLost)
            {
                // Work that arrived AFTER the plan was computed (not the rows this run
                // skipped or failed — those are in the plan) was never reviewed: it needs
                // another dry run + run, so the marker stays "partial".
                result.ArrivedDuringRun.AddRange(await WritesArrivedDuringRunAsync(work).ConfigureAwait(false));

                // Anything not written (failed, skipped as changed, not attempted) or not yet
                // reviewed is left for the next dry run + run, which "partial" allows.
                var state = result.Failures.Count == 0 && result.Skipped.Count == 0 && result.ArrivedDuringRun.Count == 0
                    ? MarkerDone
                    : MarkerPartial;
                if (!await FinishMarkerAsync(claimToken, state).ConfigureAwait(false))
                {
                    RecordClaimLost(result);
                }
            }
            logger.LogInformation(
                "CalendarMonthlyReanchorRepair: finished — {Restored} ordinals restored, {Plannings} plannings updated, {Moved} compliances moved, {Skipped} skipped, {Arrived} new writes arrived during the run, {Failures} failures",
                result.RestoredOrdinals, result.UpdatedPlannings, result.MovedCompliances,
                result.Skipped.Count, result.ArrivedDuringRun.Count, result.Failures.Count);
            return new OperationDataResult<MonthlyReanchorRepairRunResultModel>(result.Failures.Count == 0, result);
        }
        catch (Exception e)
        {
            logger.LogError(e, "CalendarMonthlyReanchorRepair: run failed");
            if (claimToken != null)
            {
                try
                {
                    await FinishMarkerAsync(claimToken, MarkerPartial).ConfigureAwait(false);
                }
                catch (Exception markerError)
                {
                    logger.LogError(markerError, "CalendarMonthlyReanchorRepair: could not mark the run partial");
                }
            }
            return new OperationDataResult<MonthlyReanchorRepairRunResultModel>(false,
                $"Monthly re-anchor repair failed: {e.Message}");
        }
    }

    private static OperationDataResult<MonthlyReanchorRepairRunResultModel> Refused(string message)
        => new(false, message);

    // ── Marker ──────────────────────────────────────────────────────────────

    private sealed record MarkerRow(string Value, DateTime? UpdatedAt);

    private Task<MarkerRow> ReadMarkerAsync(CancellationToken ct)
        => dbContext.PluginConfigurationValues
            .AsNoTracking()
            .Where(x => x.Name == MarkerName)
            .Select(x => new MarkerRow(x.Value, x.UpdatedAt))
            .FirstOrDefaultAsync(ct);

    /// <summary>A running marker holds the owning run's claim id, as running:{id} (a new Guid per run).</summary>
    private static bool IsRunning(string value)
        => value != null && value.StartsWith(MarkerRunning, StringComparison.Ordinal);

    /// <summary>The marker's state without the claim token.</summary>
    private static string MarkerStateOf(string value) => IsRunning(value) ? MarkerRunning : value;

    private static bool IsClaimable(string value, DateTime? updatedAt)
        => value == MarkerPartial
           || (IsRunning(value) && updatedAt < DateTime.UtcNow - AbandonedRunAfter);

    /// <summary>
    /// Atomic claim, one statement each way, so two concurrent requests can never both
    /// run: INSERT … WHERE NOT EXISTS when there is no marker (same statement shape as
    /// <c>CalendarConfigurationBackfillService.RepairLegacyMidnightConfigurationsAsync</c>,
    /// which explains why a unique index is not an option), or a conditional UPDATE of a
    /// <c>partial</c> / abandoned <c>running</c> marker. Exactly one caller gets a row.
    ///
    /// The marker value becomes running:{id} with an id only this run knows;
    /// the final write (<see cref="FinishMarkerAsync"/>) is conditional on it, so a run
    /// whose abandoned claim was re-claimed by another run can never overwrite that run's
    /// marker. Returns the token, or null when another caller won.
    /// </summary>
    private async Task<string> ClaimMarkerAsync(bool markerExists)
    {
        var now = DateTime.UtcNow;
        var token = $"{MarkerRunning}:{Guid.NewGuid():N}";
        var affected = markerExists
            ? await dbContext.Database.ExecuteSqlRawAsync(
                @"UPDATE `PluginConfigurationValues`
                     SET `Value` = {1}, `UpdatedAt` = {2}, `Version` = `Version` + 1
                   WHERE `Name` = {0}
                     AND (`Value` = {3} OR (`Value` LIKE {4} AND `UpdatedAt` < {5}))",
                [MarkerName, token, now, MarkerPartial, MarkerRunning + "%", now - AbandonedRunAfter],
                CancellationToken.None).ConfigureAwait(false)
            : await dbContext.Database.ExecuteSqlRawAsync(
                @"INSERT INTO `PluginConfigurationValues`
                      (`Name`, `Value`, `CreatedAt`, `UpdatedAt`, `Version`,
                       `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`)
                  SELECT {0}, {1}, {2}, {2}, 1, {3}, 1, 0 FROM DUAL
                  WHERE NOT EXISTS (
                      SELECT 1 FROM `PluginConfigurationValues` `existing`
                      WHERE `existing`.`Name` = {0})",
                [MarkerName, token, now, Constants.WorkflowStates.Created],
                CancellationToken.None).ConfigureAwait(false);
        return affected == 1 ? token : null;
    }

    private const string ClaimLostMessage =
        "this run's claim was taken over by another run (abandoned-run reclaim); it stopped writing and left the marker to that run";

    private void RecordClaimLost(MonthlyReanchorRepairRunResultModel result)
    {
        logger.LogError("CalendarMonthlyReanchorRepair: {ClaimLost}", ClaimLostMessage);
        result.Failures.Add(ClaimLostMessage);
    }

    /// <summary>
    /// Renews this run's lease (the marker's UpdatedAt) while it still holds the claim, so a
    /// long run is never taken for abandoned. False when the claim was taken over.
    /// </summary>
    private async Task<bool> RenewClaimAsync(string claimToken)
        => await dbContext.Database.ExecuteSqlRawAsync(
            @"UPDATE `PluginConfigurationValues`
                 SET `UpdatedAt` = {2}
               WHERE `Name` = {0} AND `Value` = {1}",
            [MarkerName, claimToken, DateTime.UtcNow],
            CancellationToken.None).ConfigureAwait(false) == 1;

    /// <summary>The writes a plan makes, as stable keys (rule / planning / compliance ids).</summary>
    private static HashSet<string> WriteKeys(MonthlyReanchorRepairPlanModel plan)
        => plan.OrdinalRestorations.Select(r => $"ordinal of AreaRulePlanning {r.AreaRulePlanningId}")
            .Concat(plan.PlanningUpdates.Select(p => $"planning {p.PlanningId}"))
            .Concat(plan.ComplianceMoves.Select(m => $"compliance {m.ComplianceId}"))
            .ToHashSet();

    /// <summary>
    /// Recomputes the plan after the run (while the claim is still held) and returns the
    /// writes it now wants that were NOT in the executed plan — work that arrived after the
    /// plan was computed and so was never reviewed.
    /// </summary>
    private async Task<List<string>> WritesArrivedDuringRunAsync(Work executed)
    {
        var executedKeys = WriteKeys(executed.Model);
        var now = await ComputeAsync(CancellationToken.None).ConfigureAwait(false);
        var arrived = WriteKeys(now.Model).Where(k => !executedKeys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        foreach (var key in arrived)
        {
            logger.LogWarning(
                "CalendarMonthlyReanchorRepair: {Write} became eligible during the run; not written — it needs a new reviewed dry run",
                key);
        }
        return arrived;
    }

    /// <summary>
    /// Ends this run's claim — only while the marker still holds THIS run's token. False
    /// when another run re-claimed it (this run was taken for abandoned).
    /// </summary>
    private async Task<bool> FinishMarkerAsync(string claimToken, string state)
        => await dbContext.Database.ExecuteSqlRawAsync(
            @"UPDATE `PluginConfigurationValues`
                 SET `Value` = {1}, `UpdatedAt` = {2}, `Version` = `Version` + 1
               WHERE `Name` = {0} AND `Value` = {3}",
            [MarkerName, state, DateTime.UtcNow, claimToken],
            CancellationToken.None).ConfigureAwait(false) == 1;

    // ── Plan ────────────────────────────────────────────────────────────────

    private sealed class Work
    {
        public readonly MonthlyReanchorRepairPlanModel Model = new();
        public readonly List<PlanningWork> Plannings = [];
    }

    /// <summary>Everything the run writes for one planning, in write order.</summary>
    private sealed class PlanningWork
    {
        public required AreaRulePlanning Arp { get; init; }
        public required Planning Planning { get; init; }

        // Step 1 — with the values the plan saw, so the write can check they still hold.
        public int? RestoreOrdinalTo { get; set; }
        public int SeenArpOrdinal { get; init; }
        public int SeenArpDayOfWeek { get; init; }
        public DateTime? SeenArpUpdatedAt { get; init; }

        // Step 2.
        public bool UpdatePlanning { get; set; }
        public DayOfWeek? SeenPlanningDayOfWeek { get; init; }
        public int? SeenPlanningOrdinal { get; init; }
        public DateTime? SeenNextExecutionTime { get; init; }
        public DateTime SeenPlanningStartDate { get; init; }
        public RepeatType SeenPlanningRepeatType { get; init; }
        public int SeenPlanningRepeatEvery { get; init; }
        public int NewDayOfWeek { get; set; }
        public int NewOrdinal { get; set; }
        public DateTime? NewNextExecutionTime { get; set; }

        // Step 3 — computed from this pattern (the ordinal after step 1).
        public int PatternOrdinal { get; set; }
        public int PatternDayOfWeek { get; set; }
        public List<MoveWork> Moves { get; } = [];
    }

    private sealed record MoveWork(Compliance Compliance, DateTime OldDeadline, DateTime NewDeadline, int SdkCaseId);

    /// <summary>
    /// What the move guard looks at besides the compliance row itself — loaded for the whole
    /// plan when planning, and again for one row right before its write.
    /// </summary>
    private sealed class GuardData
    {
        /// <summary>(PlanningId, date) of every compliance, any WorkflowState (the unique index spans them).</summary>
        public HashSet<(int PlanningId, DateTime Date)> Occupied { get; init; } = [];
        /// <summary>(PlanningId, date) of every live exception's OriginalDate AND NewDate.</summary>
        public HashSet<(int PlanningId, DateTime Date)> ExceptionDates { get; init; } = [];
        /// <summary>PlanningCaseId → the SDK case ids of its live PlanningCaseSites.</summary>
        public Dictionary<int, List<int>> SiblingCaseIds { get; init; } = [];
        public Dictionary<int, SdkCaseInfo> SdkCases { get; init; } = [];
    }

    private sealed record SdkCaseInfo(int Id, string WorkflowState, int? Status, DateTime? DoneAt, int? SiteId, int? MicrotingUid)
    {
        public bool IsLive => WorkflowState is not (Constants.WorkflowStates.Removed or Constants.WorkflowStates.Retracted);
        public bool IsCompleted => Status == CompletedStatus || DoneAt.HasValue;
    }

    /// <summary>
    /// Read-only: computes every write of the repair against tracked entities, so the
    /// real run applies exactly this and the dry run reports exactly this.
    ///
    /// <para><b>Converted, and not edited since.</b> <c>NormalizeRecurrence</c> writes no
    /// timestamp of its own; the conversion's only persisted trace is the
    /// <c>CalendarConfiguration</c> the backfill creates right AFTER updating the ARP,
    /// with <c>CreatedByUserId == 0</c> (CreateTask/UpdateTask always stamp a real user —
    /// the same signature the legacy-midnight repair relies on). So the conversion time
    /// is that row's <c>CreatedAt</c>, and a rule is untouched since when its
    /// <c>UpdatedAt</c> is not later. Any later ARP write keeps the rule's current
    /// ordinal and lists it under <c>OrdinalsKept</c> — as a person's edit, or as a
    /// system write when <c>UpdatedByUserId</c> is 0 (e.g. a stale-marker row the
    /// backfill normalised after its configuration already existed).</para>
    /// </summary>
    private async Task<Work> ComputeAsync(CancellationToken ct)
    {
        var work = new Work();
        var today = ComplianceFutureTaskGuard.TodayInCopenhagen(UtcNow());
        work.Model.Today = today;

        // Every Month-Nth rule. EF's C# null semantics keep legacy rows whose
        // WorkflowState is NULL (a raw `<> 'removed'` in SQL would drop them).
        var arps = await dbContext.AreaRulePlannings
            .Include(x => x.AreaRule)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.RepeatType == MonthRepeatType && x.RepeatOrdinalWeek > 0 && x.ItemPlanningId > 0)
            .OrderBy(x => x.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        if (arps.Count == 0)
        {
            work.Model.PlanHash = HashOf(work);
            return work;
        }

        // One rule per planning — the lowest-Id live ARP, the one the compliance
        // pages resolve a planning to as well.
        var ruleByPlanning = arps.GroupBy(x => x.ItemPlanningId).ToDictionary(g => g.Key, g => g.First());
        var planningIds = ruleByPlanning.Keys.ToList();
        var plannings = await itemsPlanningPnDbContext.Plannings
            .Where(x => planningIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);

        var ruleArpIds = ruleByPlanning.Values.Select(x => x.Id).ToList();
        var convertedAt = (await dbContext.CalendarConfigurations
                .AsNoTracking()
                .Where(x => ruleArpIds.Contains(x.AreaRulePlanningId))
                .Where(x => x.CreatedByUserId == 0)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => new { x.AreaRulePlanningId, x.CreatedAt })
                .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(x => x.AreaRulePlanningId)
            .ToDictionary(g => g.Key, g => g.Min(x => x.CreatedAt));

        // The pattern each planning follows AFTER step 1, as a detached ARP the shared
        // Month producer (NewPatternDateForPeriodOf) can read.
        var patternByPlanning = new Dictionary<int, AreaRulePlanning>();

        foreach (var (planningId, arp) in ruleByPlanning)
        {
            if (!plannings.TryGetValue(planningId, out var planning) || planning.RepeatType != RepeatType.Month)
            {
                continue;
            }

            var pw = new PlanningWork
            {
                Arp = arp, Planning = planning,
                SeenArpOrdinal = arp.RepeatOrdinalWeek!.Value, SeenArpDayOfWeek = arp.DayOfWeek,
                SeenArpUpdatedAt = arp.UpdatedAt,
                SeenPlanningDayOfWeek = planning.DayOfWeek, SeenPlanningOrdinal = planning.RepeatOrdinalWeek,
                SeenNextExecutionTime = planning.NextExecutionTime,
                SeenPlanningStartDate = planning.StartDate, SeenPlanningRepeatType = planning.RepeatType,
                SeenPlanningRepeatEvery = planning.RepeatEvery
            };

            // Informational: a Month rule whose CSV names another weekday than DayOfWeek
            // would flip to the CSV's weekday on the next unrelated dialog save
            // (CalendarService.NthWeekdayRuleDayOfWeek trusts a single-weekday CSV).
            if (!string.IsNullOrEmpty(arp.RepeatWeekdaysCsv)
                && !(CalendarService.ParseWeekdaysCsv(arp.RepeatWeekdaysCsv) is [var csvDay] && csvDay == arp.DayOfWeek))
            {
                work.Model.ReviewItems.Add(new MonthlyReanchorReviewItemModel
                {
                    Kind = "Rule", PlanningId = planningId, AreaRulePlanningId = arp.Id,
                    Reasons = [ReasonCsvDisagrees]
                });
            }

            // ── Step 1: restore the legacy week of a converted "1st <weekday>" rule.
            var ordinal = arp.RepeatOrdinalWeek!.Value;
            if (ordinal == 1 && arp.DayOfMonth == 0 && arp.AreaRule is { CreatedInGuide: true }
                && convertedAt.TryGetValue(arp.Id, out var conversion))
            {
                var legacyOrdinal = CalendarService.OrdinalWeekOf(planning.StartDate);
                if (legacyOrdinal != ordinal)
                {
                    var keptReason = ReasonOrdinalKept(arp, planning, conversion);
                    if (keptReason == null)
                    {
                        pw.RestoreOrdinalTo = legacyOrdinal;
                        work.Model.OrdinalRestorations.Add(new MonthlyReanchorOrdinalRestorationModel
                        {
                            AreaRulePlanningId = arp.Id, PlanningId = planningId, DayOfWeek = arp.DayOfWeek,
                            OldOrdinal = ordinal, NewOrdinal = legacyOrdinal,
                            LegacyStartDate = planning.StartDate, ConvertedAt = conversion
                        });
                        ordinal = legacyOrdinal;
                    }
                    else
                    {
                        work.Model.OrdinalsKept.Add(new MonthlyReanchorOrdinalKeptModel
                        {
                            AreaRulePlanningId = arp.Id, PlanningId = planningId,
                            CurrentOrdinal = ordinal, LegacyOrdinal = legacyOrdinal, ConvertedAt = conversion,
                            UpdatedAt = arp.UpdatedAt, UpdatedByUserId = arp.UpdatedByUserId, Reason = keptReason
                        });
                    }
                }
            }

            var pattern = new AreaRulePlanning
            {
                Id = arp.Id,
                RepeatType = MonthRepeatType,
                RepeatOrdinalWeek = ordinal,
                DayOfWeek = arp.DayOfWeek,
                RepeatWeekdaysCsv = arp.RepeatWeekdaysCsv
            };
            patternByPlanning[planningId] = pattern;
            pw.PatternOrdinal = ordinal;
            pw.PatternDayOfWeek = arp.DayOfWeek;

            // ── Step 2: mirror the weekday/ordinal and re-snap NextExecutionTime.
            // Pulling the next run into the past would make the scheduler fire at once and
            // deploy the NEXT period early — a person decides that (same rule UpdateTask
            // applies after a weekday-only edit; the helper is shared).
            var (newNext, refusedNext) = CalendarService.ResnapNextExecutionTime(planning, pattern, today);
            if (refusedNext.HasValue)
            {
                work.Model.ReviewItems.Add(new MonthlyReanchorReviewItemModel
                {
                    Kind = "NextExecutionTime", PlanningId = planningId, AreaRulePlanningId = arp.Id,
                    CurrentDate = planning.NextExecutionTime!.Value, TargetDate = refusedNext.Value,
                    Reasons = [ReasonBeforeToday]
                });
            }

            if (planning.DayOfWeek != (DayOfWeek)arp.DayOfWeek || planning.RepeatOrdinalWeek != ordinal
                || newNext.HasValue)
            {
                pw.UpdatePlanning = true;
                pw.NewDayOfWeek = arp.DayOfWeek;
                pw.NewOrdinal = ordinal;
                pw.NewNextExecutionTime = newNext;
                work.Model.PlanningUpdates.Add(new MonthlyReanchorPlanningUpdateModel
                {
                    PlanningId = planningId, AreaRulePlanningId = arp.Id,
                    OldDayOfWeek = (int?)planning.DayOfWeek, NewDayOfWeek = arp.DayOfWeek,
                    OldOrdinal = planning.RepeatOrdinalWeek, NewOrdinal = ordinal,
                    OldNextExecutionTime = planning.NextExecutionTime,
                    NewNextExecutionTime = newNext ?? planning.NextExecutionTime
                });
            }

            work.Plannings.Add(pw);
        }

        await PlanComplianceMovesAsync(work, patternByPlanning, ct).ConfigureAwait(false);

        work.Model.PlanHash = HashOf(work);
        return work;
    }

    /// <summary>
    /// Why a converted rule keeps its current ordinal, or null when its legacy week may be
    /// restored (weekday still the conversion's, and no write since the conversion).
    /// </summary>
    private static string ReasonOrdinalKept(AreaRulePlanning arp, Planning planning, DateTime convertedAt)
    {
        if (arp.DayOfWeek != (int)planning.StartDate.DayOfWeek)
        {
            return ReasonWeekdayChanged;
        }
        if (!(arp.UpdatedAt > convertedAt))
        {
            return null;
        }
        return arp.UpdatedByUserId == 0 ? ReasonSystemWriteAfterConversion : ReasonEditedAfterConversion;
    }

    /// <summary>
    /// Step 3. A compliance's SDK case carries no date at all (the device renders the
    /// occurrence at <c>Compliance.Deadline</c> through GetTasksForWeek, and
    /// <c>CaseCreateLocalOnly</c> validates <c>mainElement.EndDate</c> and discards it),
    /// so moving the deadline moves the device task with it — no case is re-created.
    /// The one EndDate that does exist lives in the Microting cloud, for cases the
    /// items-planning scheduler deployed with a real <c>CaseCreate</c>
    /// (EndDate = the NextExecutionTime of that deploy = the old deadline). The SDK Core
    /// API cannot re-date it, so a LATER target for such a case goes on the review list
    /// instead of silently outliving the case on the device.
    /// </summary>
    private async Task PlanComplianceMovesAsync(
        Work work,
        IReadOnlyDictionary<int, AreaRulePlanning> patternByPlanning,
        CancellationToken ct)
    {
        var today = work.Model.Today;
        var planningIds = patternByPlanning.Keys.ToList();
        if (planningIds.Count == 0)
        {
            return;
        }

        // Tracked: the real run moves these rows in place (the calendar holds complianceId).
        var compliances = await dbContext.Compliances
            .Where(c => planningIds.Contains(c.PlanningId))
            .Where(c => c.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(c => c.MicrotingSdkCaseId > 0)
            .OrderBy(c => c.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        if (compliances.Count == 0)
        {
            return;
        }

        // The LIVE SDK case of every row (decision 4: the plan must join it), and the rest
        // of what the move guard reads. A row's own date never equals its target (on-pattern
        // rows are skipped), so it cannot collide with itself; targets claimed by earlier
        // moves of this plan are added to Occupied as they are planned.
        var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();
        var guard = await LoadGuardDataAsync(planningIds,
            compliances.Select(c => (c.MicrotingSdkCaseId, c.PlanningCaseSiteId)), sdkDbContext, ct)
            .ConfigureAwait(false);
        var sdkCases = guard.SdkCases;

        var workByPlanning = work.Plannings.ToDictionary(x => x.Planning.Id);

        foreach (var compliance in compliances)
        {
            var pw = workByPlanning[compliance.PlanningId];
            var pattern = patternByPlanning[compliance.PlanningId];

            if (!sdkCases.TryGetValue(compliance.MicrotingSdkCaseId, out var sdkCase) || !sdkCase.IsLive)
            {
                AddOrphan(work, compliance, ReasonCaseRemoved);
                continue;
            }
            if (sdkCase.IsCompleted)
            {
                AddOrphan(work, compliance, ReasonCaseCompleted);
                continue;
            }

            var target = CalendarService.NewPatternDateForPeriodOf(pw.Planning, pattern, compliance.Deadline);
            if (target == null || target.Value.Date == compliance.Deadline.Date)
            {
                continue; // on pattern already
            }
            var targetDate = target.Value.Date;
            var reasons = ReasonsNotToMove(compliance.PlanningId, compliance.Deadline, compliance.StartDate,
                compliance.MovedToExpiredFolder, compliance.PlanningCaseSiteId, sdkCase.Id, targetDate, today, guard);

            if (reasons.Count > 0)
            {
                work.Model.ReviewItems.Add(new MonthlyReanchorReviewItemModel
                {
                    Kind = "Compliance", ComplianceId = compliance.Id, PlanningId = compliance.PlanningId,
                    AreaRulePlanningId = pattern.Id, SdkCaseId = sdkCase.Id, SdkSiteId = sdkCase.SiteId,
                    CurrentDate = compliance.Deadline, TargetDate = targetDate, Reasons = reasons
                });
                continue;
            }

            var newDeadline = SameTimeOfDay(targetDate, compliance.Deadline);
            guard.Occupied.Add((compliance.PlanningId, targetDate));
            pw.Moves.Add(new MoveWork(compliance, compliance.Deadline, newDeadline, sdkCase.Id));
            work.Model.ComplianceMoves.Add(new MonthlyReanchorComplianceMoveModel
            {
                ComplianceId = compliance.Id, PlanningId = compliance.PlanningId, AreaRulePlanningId = pattern.Id,
                SdkCaseId = sdkCase.Id, SdkSiteId = sdkCase.SiteId,
                OldDeadline = compliance.Deadline, NewDeadline = newDeadline
            });
        }
    }

    /// <summary>
    /// Loads what the move guard reads for <paramref name="planningIds"/> and the given
    /// rows (their SDK case and PlanningCase): every compliance date of the plannings, every
    /// live exception date of any of their rules (OriginalDate AND NewDate — an occurrence
    /// moved ONTO a date occupies it just as much; GetTasksForWeek and ListEvents key
    /// overrides on the compliance's deadline), the sibling sites' cases, and the SDK cases.
    /// </summary>
    private async Task<GuardData> LoadGuardDataAsync(IReadOnlyCollection<int> planningIds,
        IEnumerable<(int SdkCaseId, int PlanningCaseId)> rows, SdkDbContext sdkDbContext, CancellationToken ct)
    {
        var rowList = rows.ToList();
        var occupied = (await dbContext.Compliances
                .AsNoTracking()
                .Where(c => planningIds.Contains(c.PlanningId))
                .Select(c => new { c.PlanningId, c.Deadline })
                .ToListAsync(ct).ConfigureAwait(false))
            .Select(x => (x.PlanningId, x.Deadline.Date))
            .ToHashSet();

        var arpPlanning = await dbContext.AreaRulePlannings
            .AsNoTracking()
            .Where(x => planningIds.Contains(x.ItemPlanningId))
            .Select(x => new { x.Id, x.ItemPlanningId })
            .ToDictionaryAsync(x => x.Id, x => x.ItemPlanningId, ct).ConfigureAwait(false);
        var arpIds = arpPlanning.Keys.ToList();
        var exceptions = await dbContext.CalendarOccurrenceExceptions
            .AsNoTracking()
            .Where(e => arpIds.Contains(e.AreaRulePlanningId))
            .Where(e => e.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(e => new { e.AreaRulePlanningId, e.OriginalDate, e.NewDate })
            .ToListAsync(ct).ConfigureAwait(false);
        var exceptionDates = new HashSet<(int PlanningId, DateTime Date)>();
        foreach (var e in exceptions)
        {
            var planningId = arpPlanning[e.AreaRulePlanningId];
            exceptionDates.Add((planningId, e.OriginalDate.Date));
            if (e.NewDate.HasValue)
            {
                exceptionDates.Add((planningId, e.NewDate.Value.Date));
            }
        }

        var planningCaseIds = rowList.Where(r => r.PlanningCaseId > 0).Select(r => r.PlanningCaseId).Distinct().ToList();
        var siblingCaseIds = planningCaseIds.Count == 0
            ? new Dictionary<int, List<int>>()
            : (await itemsPlanningPnDbContext.PlanningCaseSites
                    .AsNoTracking()
                    .Where(x => planningCaseIds.Contains(x.PlanningCaseId) && x.MicrotingSdkCaseId > 0)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed
                                && x.WorkflowState != Constants.WorkflowStates.Retracted)
                    .Select(x => new { x.PlanningCaseId, x.MicrotingSdkCaseId })
                    .ToListAsync(ct).ConfigureAwait(false))
                .GroupBy(x => x.PlanningCaseId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.MicrotingSdkCaseId).Distinct().ToList());

        var sdkCases = await LoadSdkCasesAsync(sdkDbContext,
            rowList.Select(r => r.SdkCaseId).Concat(siblingCaseIds.Values.SelectMany(x => x)), ct).ConfigureAwait(false);

        return new GuardData
        {
            Occupied = occupied, ExceptionDates = exceptionDates, SiblingCaseIds = siblingCaseIds, SdkCases = sdkCases
        };
    }

    /// <summary>
    /// Every reason an open compliance must NOT be moved to <paramref name="targetDate"/> —
    /// one rule set, applied when planning and again right before the write. Empty = move.
    /// </summary>
    private static List<string> ReasonsNotToMove(int planningId, DateTime oldDeadline, DateTime startDate,
        bool movedToExpiredFolder, int planningCaseId, int ownCaseId, DateTime targetDate, DateTime today,
        GuardData guard)
    {
        var siblings = guard.SiblingCaseIds.TryGetValue(planningCaseId, out var ids)
            ? ids.Where(id => id != ownCaseId).ToList()
            : [];
        var reasons = new List<string>();
        if (guard.Occupied.Contains((planningId, targetDate)))
        {
            reasons.Add(ReasonCollision);
        }
        if (guard.ExceptionDates.Contains((planningId, oldDeadline.Date)))
        {
            reasons.Add(ReasonOccurrenceException);
        }
        if (guard.ExceptionDates.Contains((planningId, targetDate)))
        {
            reasons.Add(ReasonOccurrenceExceptionOnTarget);
        }
        if (targetDate < startDate.Date)
        {
            reasons.Add(ReasonBeforeStartDate);
        }
        if (targetDate < today)
        {
            reasons.Add(ReasonBeforeToday);
        }
        // Overdue: the cloud case already sits in the expired folder (or is about to), and
        // the hour-9 expiry job never looks at it again after a move.
        if (oldDeadline.Date < today)
        {
            reasons.Add(ReasonOldDeadlineBeforeToday);
        }
        if (movedToExpiredFolder)
        {
            reasons.Add(ReasonMovedToExpiredFolder);
        }
        if (siblings.Any(id => guard.SdkCases.TryGetValue(id, out var c) && c.IsLive && c.IsCompleted))
        {
            reasons.Add(ReasonSiblingCaseCompleted);
        }
        if (CloudCaseEndDateRule.MoveWouldOutliveCloudCase(oldDeadline, targetDate,
                siblings.Append(ownCaseId)
                    .Where(id => guard.SdkCases.TryGetValue(id, out var c) && c.IsLive)
                    .Select(id => guard.SdkCases[id].MicrotingUid)))
        {
            reasons.Add(ReasonCloudCaseEndDate);
        }
        return reasons;
    }

    private static async Task<Dictionary<int, SdkCaseInfo>> LoadSdkCasesAsync(
        SdkDbContext sdkDbContext, IEnumerable<int> caseIds, CancellationToken ct)
    {
        var ids = caseIds.Distinct().ToList();
        return await sdkDbContext.Cases
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new SdkCaseInfo(x.Id, x.WorkflowState, x.Status, x.DoneAt, x.SiteId, x.MicrotingUid))
            .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
    }

    private static void AddOrphan(Work work, Compliance compliance, string reason)
        => work.Model.SkippedOrphans.Add(new MonthlyReanchorSkippedOrphanModel
        {
            ComplianceId = compliance.Id, PlanningId = compliance.PlanningId,
            SdkCaseId = compliance.MicrotingSdkCaseId, Deadline = compliance.Deadline, Reason = reason
        });

    private static DateTime SameTimeOfDay(DateTime date, DateTime template)
        => DateTime.SpecifyKind(date.Date, template.Kind).Add(template.TimeOfDay);

    private static string HashOf(Work work)
    {
        // The Copenhagen plan date is part of the plan: "before today" and "overdue" are
        // judged against it, so a reviewed hash is only valid the day it was made.
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"T|{work.Model.Today:yyyy-MM-dd}\n");
        foreach (var r in work.Model.OrdinalRestorations)
        {
            sb.Append(CultureInfo.InvariantCulture, $"R|{r.AreaRulePlanningId}|{r.OldOrdinal}|{r.NewOrdinal}\n");
        }
        foreach (var p in work.Model.PlanningUpdates)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"P|{p.PlanningId}|{p.OldDayOfWeek}|{p.NewDayOfWeek}|{p.OldOrdinal}|{p.NewOrdinal}|{p.OldNextExecutionTime:O}|{p.NewNextExecutionTime:O}\n");
        }
        foreach (var m in work.Model.ComplianceMoves)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"M|{m.ComplianceId}|{m.SdkCaseId}|{m.OldDeadline:O}|{m.NewDeadline:O}\n");
        }
        // Everything else the reviewer signs off on, in a stable order: a change that only
        // alters what is kept, reviewed or skipped also invalidates the reviewed hash.
        foreach (var k in work.Model.OrdinalsKept.OrderBy(k => k.AreaRulePlanningId))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"K|{k.AreaRulePlanningId}|{k.PlanningId}|{k.CurrentOrdinal}|{k.LegacyOrdinal}|{k.Reason}\n");
        }
        foreach (var r in work.Model.ReviewItems
                     .OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.PlanningId)
                     .ThenBy(r => r.ComplianceId ?? 0).ThenBy(r => r.AreaRulePlanningId))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"V|{r.Kind}|{r.PlanningId}|{r.AreaRulePlanningId}|{r.ComplianceId}|{r.SdkCaseId}|{r.CurrentDate:O}|{r.TargetDate:O}|{string.Join(",", r.Reasons)}\n");
        }
        foreach (var o in work.Model.SkippedOrphans.OrderBy(o => o.ComplianceId))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"O|{o.ComplianceId}|{o.PlanningId}|{o.SdkCaseId}|{o.Deadline:O}|{o.Reason}\n");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    // ── Apply ───────────────────────────────────────────────────────────────

    private enum WriteOutcome { Written, Skipped, Failed }

    /// <summary>
    /// Applies the plan planning by planning, in write order. Every write first re-reads
    /// the row and checks it still holds the values the plan was computed from (and, for a
    /// compliance, that its SDK cases are still live and uncompleted); a row that changed
    /// in between is skipped and logged, not failed. The write itself goes through the
    /// entity's own <c>Update</c>, so its version/audit row is written as always.
    ///
    /// Later steps of a planning were computed from its earlier steps' result (the
    /// restored ordinal decides the targets), so when one of them is not written the rest
    /// of that planning is left for the next run. Runs without a cancellation token on
    /// purpose: once writing has started it must not stop halfway.
    /// </summary>
    private async Task<(MonthlyReanchorRepairRunResultModel Result, bool ClaimLost)> ApplyAsync(
        Work work, string claimToken)
    {
        var result = new MonthlyReanchorRepairRunResultModel { Plan = work.Model };
        SdkDbContext sdkDbContext = null;
        var writesSinceRenewal = 0;
        try
        {
            for (var index = 0; index < work.Plannings.Count; index++)
            {
                var pw = work.Plannings[index];

                // The lease is renewed after every planning and every RenewLeaseEvery
                // compliance moves; a lost claim stops this run before its next write.
                if (index > 0 && !await RenewClaimAsync(claimToken).ConfigureAwait(false))
                {
                    RecordClaimLost(result);
                    ReportNotAttemptedAfterClaimLost(result, work.Plannings.Skip(index));
                    return (result, true);
                }

                if (pw.RestoreOrdinalTo is { } newOrdinal)
                {
                    var outcome = await WriteAsync(dbContext, pw.Arp, result, $"ordinal of AreaRulePlanning {pw.Arp.Id}",
                        async () =>
                        {
                            var now = await dbContext.AreaRulePlannings.AsNoTracking()
                                .Where(x => x.Id == pw.Arp.Id)
                                .Select(x => new { x.RepeatOrdinalWeek, x.DayOfWeek, x.UpdatedAt, x.WorkflowState })
                                .FirstOrDefaultAsync().ConfigureAwait(false);
                            return now != null && now.WorkflowState != Constants.WorkflowStates.Removed
                                   && now.RepeatOrdinalWeek == pw.SeenArpOrdinal
                                   && now.DayOfWeek == pw.SeenArpDayOfWeek
                                   && now.UpdatedAt == pw.SeenArpUpdatedAt
                                ? null
                                : ChangedSincePlan;
                        },
                        () =>
                        {
                            pw.Arp.RepeatOrdinalWeek = newOrdinal;
                            return pw.Arp.Update(dbContext);
                        }).ConfigureAwait(false);
                    if (outcome != WriteOutcome.Written)
                    {
                        ReportNotAttempted(result, pw, includePlanning: true);
                        continue;
                    }
                    result.RestoredOrdinals++;
                    logger.LogInformation(
                        "CalendarMonthlyReanchorRepair: AreaRulePlanning {ArpId} (planning {PlanningId}) ordinal {OldOrdinal} -> {NewOrdinal} (weekday {DayOfWeek})",
                        pw.Arp.Id, pw.Planning.Id, pw.SeenArpOrdinal, newOrdinal, pw.Arp.DayOfWeek);
                }

                if (pw.UpdatePlanning)
                {
                    var planning = pw.Planning;
                    var outcome = await WriteAsync(itemsPlanningPnDbContext, planning, result, $"planning {planning.Id}",
                        async () => await PlanningChangedSincePlanAsync(pw, checkMirroredFields: true)
                            .ConfigureAwait(false),
                        () =>
                        {
                            planning.DayOfWeek = (DayOfWeek)pw.NewDayOfWeek;
                            planning.RepeatOrdinalWeek = pw.NewOrdinal;
                            if (pw.NewNextExecutionTime.HasValue)
                            {
                                planning.NextExecutionTime = pw.NewNextExecutionTime;
                            }
                            return planning.Update(itemsPlanningPnDbContext);
                        }).ConfigureAwait(false);
                    if (outcome != WriteOutcome.Written)
                    {
                        ReportNotAttempted(result, pw, includePlanning: false);
                        continue;
                    }
                    result.UpdatedPlannings++;
                    logger.LogInformation(
                        "CalendarMonthlyReanchorRepair: planning {PlanningId} DayOfWeek {OldDow} -> {NewDow}, ordinal {OldOrdinal} -> {NewOrdinal}, NextExecutionTime {OldNext:yyyy-MM-dd} -> {NewNext:yyyy-MM-dd}",
                        planning.Id, pw.SeenPlanningDayOfWeek, planning.DayOfWeek, pw.SeenPlanningOrdinal,
                        pw.NewOrdinal, pw.SeenNextExecutionTime, planning.NextExecutionTime);
                }

                for (var moveIndex = 0; moveIndex < pw.Moves.Count; moveIndex++)
                {
                    var move = pw.Moves[moveIndex];
                    if (++writesSinceRenewal >= RenewLeaseEvery)
                    {
                        writesSinceRenewal = 0;
                        if (!await RenewClaimAsync(claimToken).ConfigureAwait(false))
                        {
                            RecordClaimLost(result);
                            foreach (var dropped in pw.Moves.Skip(moveIndex))
                            {
                                result.Skipped.Add($"compliance {dropped.Compliance.Id}: not attempted: the run lost its claim");
                            }
                            ReportNotAttemptedAfterClaimLost(result, work.Plannings.Skip(index + 1));
                            return (result, true);
                        }
                    }
                    if (sdkDbContext == null)
                    {
                        var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
                        sdkDbContext = sdkCore.DbContextHelper.GetDbContext();
                    }
                    var compliance = move.Compliance;
                    var sdk = sdkDbContext;
                    var outcome = await WriteAsync(dbContext, compliance, result, $"compliance {compliance.Id}",
                        () => MoveNoLongerSafeAsync(pw, move, sdk),
                        () =>
                        {
                            compliance.Deadline = move.NewDeadline;
                            return compliance.Update(dbContext);
                        }).ConfigureAwait(false);
                    if (outcome == WriteOutcome.Written)
                    {
                        result.MovedCompliances++;
                        logger.LogInformation(
                            "CalendarMonthlyReanchorRepair: compliance {ComplianceId} (planning {PlanningId}, SDK case {SdkCaseId}) deadline {OldDeadline:yyyy-MM-dd} -> {NewDeadline:yyyy-MM-dd}",
                            compliance.Id, compliance.PlanningId, move.SdkCaseId, move.OldDeadline, move.NewDeadline);
                    }
                }
            }

            // The last planning's writes are done; one more renewal confirms the claim
            // was held throughout before the run reports success.
            if (!await RenewClaimAsync(claimToken).ConfigureAwait(false))
            {
                RecordClaimLost(result);
                return (result, true);
            }
        }
        finally
        {
            if (sdkDbContext != null)
            {
                await sdkDbContext.DisposeAsync().ConfigureAwait(false);
            }
        }

        return (result, false);
    }

    /// <summary>Renew the lease at least this often within one planning's compliance moves.</summary>
    private const int RenewLeaseEvery = 25;

    private void ReportNotAttemptedAfterClaimLost(MonthlyReanchorRepairRunResultModel result,
        IEnumerable<PlanningWork> remaining)
    {
        foreach (var pw in remaining)
        {
            if (pw.RestoreOrdinalTo.HasValue)
            {
                result.Skipped.Add($"ordinal of AreaRulePlanning {pw.Arp.Id}: not attempted: the run lost its claim");
            }
            if (pw.UpdatePlanning)
            {
                result.Skipped.Add($"planning {pw.Planning.Id}: not attempted: the run lost its claim");
            }
            result.Skipped.AddRange(pw.Moves.Select(m => $"compliance {m.Compliance.Id}: not attempted: the run lost its claim"));
        }
    }

    private const string ChangedSincePlan = "changed since the plan was computed";

    /// <summary>
    /// Null when the planning row still is what the plan saw; else why not. Also compares
    /// the fields the targets were computed from (StartDate — the start month's anchor —
    /// RepeatType, RepeatEvery) and, for the planning's own write, the ones it overwrites.
    /// </summary>
    private async Task<string> PlanningChangedSincePlanAsync(PlanningWork pw, bool checkMirroredFields)
    {
        var now = await itemsPlanningPnDbContext.Plannings.AsNoTracking()
            .Where(x => x.Id == pw.Planning.Id)
            .Select(x => new
            {
                x.DayOfWeek, x.RepeatOrdinalWeek, x.NextExecutionTime, x.StartDate, x.RepeatType, x.RepeatEvery,
                x.WorkflowState
            })
            .FirstOrDefaultAsync().ConfigureAwait(false);
        if (now == null
            || now.StartDate != pw.SeenPlanningStartDate
            || now.RepeatType != pw.SeenPlanningRepeatType
            || now.RepeatEvery != pw.SeenPlanningRepeatEvery)
        {
            return $"planning {ChangedSincePlan}";
        }
        if (checkMirroredFields
            && (now.DayOfWeek != pw.SeenPlanningDayOfWeek
                || now.RepeatOrdinalWeek != pw.SeenPlanningOrdinal
                || now.NextExecutionTime != pw.SeenNextExecutionTime))
        {
            return $"planning {ChangedSincePlan}";
        }
        return null;
    }

    /// <summary>
    /// Right before a compliance move: re-reads the row, its rule and planning, and every
    /// input of the move guard, and applies the SAME rules the plan applied
    /// (<see cref="ReasonsNotToMove"/>) against today. Null = still safe to move; else the
    /// reason the row is skipped.
    /// </summary>
    private async Task<string> MoveNoLongerSafeAsync(PlanningWork pw, MoveWork move, SdkDbContext sdk)
    {
        var row = await dbContext.Compliances.AsNoTracking()
            .Where(x => x.Id == move.Compliance.Id)
            .Select(x => new
            {
                x.Deadline, x.WorkflowState, x.MicrotingSdkCaseId, x.MovedToExpiredFolder, x.StartDate,
                x.PlanningCaseSiteId
            })
            .FirstOrDefaultAsync().ConfigureAwait(false);
        if (row == null || row.WorkflowState == Constants.WorkflowStates.Removed
            || row.Deadline != move.OldDeadline || row.MicrotingSdkCaseId != move.SdkCaseId)
        {
            return ChangedSincePlan;
        }

        var rule = await dbContext.AreaRulePlannings.AsNoTracking()
            .Where(x => x.Id == pw.Arp.Id)
            .Select(x => new { x.RepeatOrdinalWeek, x.DayOfWeek, x.WorkflowState })
            .FirstOrDefaultAsync().ConfigureAwait(false);
        if (rule == null || rule.WorkflowState == Constants.WorkflowStates.Removed
            || rule.RepeatOrdinalWeek != pw.PatternOrdinal || rule.DayOfWeek != pw.PatternDayOfWeek)
        {
            return $"rule {ChangedSincePlan}";
        }
        var planningChanged = await PlanningChangedSincePlanAsync(pw, checkMirroredFields: false).ConfigureAwait(false);
        if (planningChanged != null)
        {
            return planningChanged;
        }

        var guard = await LoadGuardDataAsync([pw.Planning.Id], [(row.MicrotingSdkCaseId, row.PlanningCaseSiteId)],
            sdk, CancellationToken.None).ConfigureAwait(false);
        if (!guard.SdkCases.TryGetValue(row.MicrotingSdkCaseId, out var own) || !own.IsLive || own.IsCompleted)
        {
            return "SDK case no longer open";
        }
        var reasons = ReasonsNotToMove(pw.Planning.Id, row.Deadline, row.StartDate, row.MovedToExpiredFolder,
            row.PlanningCaseSiteId, row.MicrotingSdkCaseId, move.NewDeadline.Date,
            ComplianceFutureTaskGuard.TodayInCopenhagen(UtcNow()), guard);
        return reasons.Count == 0 ? null : string.Join(", ", reasons);
    }

    /// <summary>
    /// Accounts for the planned writes a planning drops after one of its steps was not
    /// written, so the result names every planned write that did not happen.
    /// </summary>
    private void ReportNotAttempted(MonthlyReanchorRepairRunResultModel result, PlanningWork pw, bool includePlanning)
    {
        var why = $"not attempted: an earlier step of planning {pw.Planning.Id} was not written";
        var dropped = new List<string>();
        if (includePlanning && pw.UpdatePlanning)
        {
            dropped.Add($"planning {pw.Planning.Id}");
        }
        dropped.AddRange(pw.Moves.Select(m => $"compliance {m.Compliance.Id}"));
        foreach (var what in dropped)
        {
            result.Skipped.Add($"{what}: {why}");
            logger.LogWarning("CalendarMonthlyReanchorRepair: {What} {Why}", what, why);
        }
    }

    /// <summary>
    /// One row's write: <paramref name="whyNot"/> returning a reason → skipped (logged,
    /// with that reason); an exception → failed (logged, recorded) and the row's pending
    /// change is discarded — otherwise the still-Modified entity would be re-sent (and fail
    /// again) by every later SaveChanges of the same context.
    /// </summary>
    private async Task<WriteOutcome> WriteAsync(DbContext context, object entity,
        MonthlyReanchorRepairRunResultModel result, string what, Func<Task<string>> whyNot, Func<Task> write)
    {
        try
        {
            var reason = await whyNot().ConfigureAwait(false);
            if (reason != null)
            {
                logger.LogInformation(
                    "CalendarMonthlyReanchorRepair: {What} skipped: {Reason} (the next dry run re-evaluates it)",
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
            logger.LogError(e, "CalendarMonthlyReanchorRepair: writing {What} failed; left unchanged", what);
            result.Failures.Add($"{what}: {e.Message}");
            return WriteOutcome.Failed;
        }
    }

    /// <summary>
    /// One summary line; the items themselves only at Debug for a dry run (a GET must not
    /// flood the log), and as warnings / information for the real run.
    /// </summary>
    private void LogPlan(MonthlyReanchorRepairPlanModel plan, bool isRun)
    {
        var mode = isRun ? "run" : "dry run";
        var reviewLevel = isRun ? LogLevel.Warning : LogLevel.Debug;
        var keptLevel = isRun ? LogLevel.Information : LogLevel.Debug;
        logger.LogInformation(
            "CalendarMonthlyReanchorRepair ({Mode}): {Restorations} ordinal restorations, {Kept} ordinals kept, {Plannings} planning updates, {Moves} compliance moves, {Review} for review, {Orphans} orphans skipped, today {Today:yyyy-MM-dd}, plan {PlanHash}",
            mode, plan.OrdinalRestorations.Count, plan.OrdinalsKept.Count, plan.PlanningUpdates.Count,
            plan.ComplianceMoves.Count, plan.ReviewItems.Count, plan.SkippedOrphans.Count, plan.Today, plan.PlanHash);
        foreach (var item in plan.ReviewItems)
        {
            logger.Log(reviewLevel,
                "CalendarMonthlyReanchorRepair ({Mode}): REVIEW {Kind} planning {PlanningId} compliance {ComplianceId} case {SdkCaseId}: {Current:yyyy-MM-dd} -> {Target:yyyy-MM-dd} not moved ({Reasons})",
                mode, item.Kind, item.PlanningId, item.ComplianceId, item.SdkCaseId, item.CurrentDate, item.TargetDate,
                string.Join(", ", item.Reasons));
        }
        foreach (var kept in plan.OrdinalsKept)
        {
            logger.Log(keptLevel,
                "CalendarMonthlyReanchorRepair ({Mode}): AreaRulePlanning {ArpId} keeps ordinal {Current} (legacy week {Legacy}): {Reason}",
                mode, kept.AreaRulePlanningId, kept.CurrentOrdinal, kept.LegacyOrdinal, kept.Reason);
        }
    }
}
