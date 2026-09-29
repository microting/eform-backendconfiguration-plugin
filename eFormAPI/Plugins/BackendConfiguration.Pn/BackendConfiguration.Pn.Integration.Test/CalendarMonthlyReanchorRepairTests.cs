/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
*/

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.CalendarConfigurationBackfillService;
using BackendConfiguration.Pn.Services.CalendarMonthlyReanchorRepair;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1294 — the one-time monthly re-anchor repair
/// (<see cref="CalendarMonthlyReanchorRepairService"/>).
///
/// Every rule is produced the way production got it: a legacy task-wizard
/// planning ("every 12 months", day-of-month anchor) is run through the REAL
/// <see cref="CalendarConfigurationBackfillService"/>, which converts it to
/// "1st &lt;weekday&gt;" and writes the conversion's CalendarConfiguration. The
/// conversion now mirrors Planning.DayOfWeek itself, so the fixture then puts the
/// stale legacy weekday back — the state every installation converted before
/// #1294 is in.
///
/// "Today" is pinned (2026-09-29, Copenhagen) through the service's clock seam, so
/// the 2027 dates stay in the future and "before today" is deterministic.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarMonthlyReanchorRepairTests : TestBaseSetup
{
    private static readonly DateTime Now = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>SDK Case.Status of a live, unanswered case (SqlController.CaseCreate).</summary>
    private const int OpenCaseStatus = 33;

    private CalendarConfigurationBackfillService _backfill = null!;
    private CalendarMonthlyReanchorRepairService _sut = null!;
    private Site _site = null!;
    private int _nextUid = 2_000_000_100;

    private static DateTime D(int y, int m, int d) => new(y, m, d, 0, 0, 0);

    [SetUp]
    public async Task SetUpRepair()
    {
        var ctx = BackendConfigurationPnDbContext!;
        ctx.Compliances.RemoveRange(ctx.Compliances);
        ctx.CalendarOccurrenceExceptionSites.RemoveRange(ctx.CalendarOccurrenceExceptionSites);
        await ctx.SaveChangesAsync();
        ctx.CalendarOccurrenceExceptions.RemoveRange(ctx.CalendarOccurrenceExceptions);
        ctx.CalendarConfigurations.RemoveRange(ctx.CalendarConfigurations);
        await ctx.SaveChangesAsync();
        ctx.CalendarBoards.RemoveRange(ctx.CalendarBoards);
        ctx.AreaRulePlannings.RemoveRange(ctx.AreaRulePlannings);
        await ctx.SaveChangesAsync();
        ctx.AreaRules.RemoveRange(ctx.AreaRules);
        await ctx.SaveChangesAsync();
        ctx.Areas.RemoveRange(ctx.Areas);
        ctx.Properties.RemoveRange(ctx.Properties);
        ctx.PluginConfigurationValues.RemoveRange(ctx.PluginConfigurationValues.Where(x =>
            x.Name == CalendarMonthlyReanchorRepairService.MarkerName
            || x.Name == CalendarConfigurationBackfillService.LegacyStartHourRepairMarkerName));
        await ctx.SaveChangesAsync();

        // Items-planning children before their Planning (FK_PlanningCases_Plannings_PlanningId
        // and friends): SiblingSitesCaseCompleted seeds PlanningCase + PlanningCaseSites, and
        // the fixture's tests run in any order on one shared database.
        var ip = ItemsPlanningPnDbContext!;
        ip.PlanningCaseSites.RemoveRange(ip.PlanningCaseSites);
        await ip.SaveChangesAsync();
        ip.PlanningCases.RemoveRange(ip.PlanningCases);
        await ip.SaveChangesAsync();
        ip.Plannings.RemoveRange(ip.Plannings);
        await ip.SaveChangesAsync();

        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var language = await MicrotingDbContext!.Languages.FirstAsync();
        _site = new Site
        {
            Name = "Device A", MicrotingUid = Random.Shared.Next(100_000, 900_000), LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(_site);
        await MicrotingDbContext.SaveChangesAsync();

        _sut = new CalendarMonthlyReanchorRepairService(ctx, ItemsPlanningPnDbContext, coreHelper,
            TestContextLogger<CalendarMonthlyReanchorRepairService>.Instance)
        {
            UtcNow = () => Now
        };
        _backfill = new CalendarConfigurationBackfillService(ctx, ItemsPlanningPnDbContext,
            TestContextLogger<CalendarConfigurationBackfillService>.Instance);
    }

    // ── Seeding ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A legacy wizard task "every 12 months" anchored on <paramref name="legacyStart"/>
    /// (a Wednesday in every case below). Not yet converted — call <see cref="ConvertAsync"/>.
    /// </summary>
    private async Task<(int ArpId, int PlanningId)> SeedLegacyYearlyTaskAsync(DateTime legacyStart, DateTime? nextExecution)
    {
        var property = new Property
        {
            Name = $"Example Property {Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);
        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);

        var planning = new Planning
        {
            Enabled = true, RepeatType = RepeatType.Month, RepeatEvery = 12, StartDate = legacyStart,
            DayOfMonth = Math.Min(legacyStart.Day, 28), DayOfWeek = DayOfWeek.Friday,
            NextExecutionTime = nextExecution, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = legacyStart, Status = true,
            RepeatType = 3, RepeatEvery = 12, ComplianceEnabled = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);
        return (arp.Id, planning.Id);
    }

    /// <summary>
    /// Produces the state every installation converted BEFORE #1294 is in: runs the real
    /// conversion, then undoes the rule fixes #1294 added to it — the legacy week (back to
    /// the hardcoded 1st) and the Planning.DayOfWeek mirror (back to the stale Friday).
    /// The conversion leaves NextExecutionTime and compliances alone, as it always did.
    /// ExecuteUpdate leaves AreaRulePlanning.UpdatedAt alone, so the rule still counts as
    /// "not edited since the conversion".
    /// </summary>
    private async Task ConvertAsync()
    {
        await _backfill.RunIfNeededAsync();
        await BackendConfigurationPnDbContext!.AreaRulePlannings
            .Where(x => x.RepeatType == 3)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RepeatOrdinalWeek, 1));
        await ItemsPlanningPnDbContext!.Plannings
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.RepeatOrdinalWeek, 1)
                .SetProperty(p => p.DayOfWeek, DayOfWeek.Friday));
        ForgetTrackedRows();
    }

    /// <summary>
    /// ExecuteUpdate bypasses the change tracker, and a query returns an already-tracked
    /// instance unchanged — so the repair would read the backfill's in-memory values
    /// instead of the database. Production runs the repair on a fresh request scope.
    /// </summary>
    private void ForgetTrackedRows()
    {
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        ItemsPlanningPnDbContext!.ChangeTracker.Clear();
    }

    private async Task<Case> SeedCaseAsync(int status = OpenCaseStatus, DateTime? doneAt = null,
        string workflowState = Constants.WorkflowStates.Created, int? microtingUid = null)
    {
        var sdkCase = new Case
        {
            SiteId = _site.Id, Status = status, DoneAt = doneAt, MicrotingUid = microtingUid ?? _nextUid++,
            WorkflowState = workflowState
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkCase;
    }

    private async Task<Compliance> SeedComplianceAsync(int planningId, DateTime deadline, DateTime startDate,
        int sdkCaseId, string workflowState = Constants.WorkflowStates.Created)
    {
        var compliance = new Compliance
        {
            ItemName = "Yearly check", PlanningId = planningId, Deadline = deadline, StartDate = startDate,
            MicrotingSdkCaseId = sdkCaseId, WorkflowState = workflowState, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance;
    }

    private async Task<MonthlyReanchorRepairPlanModel> DryRunAsync()
    {
        var result = await _sut.DryRunAsync();
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    private async Task<MonthlyReanchorRepairRunResultModel> RunAsync()
    {
        var plan = await DryRunAsync();
        var result = await _sut.RunAsync(plan.PlanHash);
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    private async Task<AreaRulePlanning> ArpAsync(int id)
        => await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == id);

    private async Task<Planning> PlanningAsync(int id)
        => await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().SingleAsync(x => x.Id == id);

    private async Task<string?> MarkerAsync()
        => await BackendConfigurationPnDbContext!.PluginConfigurationValues.AsNoTracking()
            .Where(x => x.Name == CalendarMonthlyReanchorRepairService.MarkerName)
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

    private async Task<DateTime> DeadlineAsync(int complianceId)
        => (await BackendConfigurationPnDbContext!.Compliances.AsNoTracking().SingleAsync(x => x.Id == complianceId)).Deadline;

    // ── The issue's fixture ─────────────────────────────────────────────────

    /// <summary>
    /// Legacy "every 12 months on the 3rd" (start Wed 3 Jan 2024). The open compliance of
    /// the running period is dated Sun 3 Jan 2027; the converted rule says "1st Wednesday"
    /// = Wed 6 Jan 2027. The calendar painted both. After the repair there is one date.
    /// </summary>
    [Test]
    public async Task ConvertedYearlyRule_OpenComplianceAndPlanningMoveToTheRulesDay_AndSecondRunIsANoOp()
    {
        var (arpId, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var sdkCase = await SeedCaseAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), sdkCase.Id);

        var result = await RunAsync();

        var arp = await ArpAsync(arpId);
        var planning = await PlanningAsync(planningId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Failures, Is.Empty);
            Assert.That(arp.RepeatOrdinalWeek, Is.EqualTo(1), "3 Jan is in the 1st week — nothing to restore");
            Assert.That(arp.DayOfWeek, Is.EqualTo((int)DayOfWeek.Wednesday));
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Wednesday), "mirrored from the ARP");
            Assert.That(planning.RepeatOrdinalWeek, Is.EqualTo(1));
            Assert.That(planning.NextExecutionTime, Is.EqualTo(D(2027, 1, 6)), "re-snapped within its month");
            Assert.That(result.Plan.ComplianceMoves.Single().ComplianceId, Is.EqualTo(compliance.Id));
            Assert.That(result.Plan.ComplianceMoves.Single().SdkSiteId, Is.EqualTo(_site.Id));
            Assert.That(result.Plan.ReviewItems, Is.Empty);
        });
        Assert.That(await DeadlineAsync(compliance.Id), Is.EqualTo(D(2027, 1, 6)));
        Assert.That(await BackendConfigurationPnDbContext!.PluginConfigurationValues
            .CountAsync(x => x.Name == CalendarMonthlyReanchorRepairService.MarkerName), Is.EqualTo(1));

        // Second run: refused by the marker, and there is nothing left to do anyway.
        var again = await DryRunAsync();
        Assert.Multiple(() =>
        {
            Assert.That(again.AlreadyExecuted, Is.True);
            Assert.That(again.OrdinalRestorations, Is.Empty);
            Assert.That(again.PlanningUpdates, Is.Empty);
            Assert.That(again.ComplianceMoves, Is.Empty);
        });
        var second = await _sut.RunAsync(again.PlanHash);
        Assert.That(second.Success, Is.False, "the marker makes the repair one-time");
        Assert.That(await DeadlineAsync(compliance.Id), Is.EqualTo(D(2027, 1, 6)));
    }

    // ── Step 1: the legacy week ─────────────────────────────────────────────

    /// <summary>
    /// "On the 17th" was converted to "1st Wednesday". The legacy week is restored
    /// (17 Jan 2024 is the 3rd Wednesday) BEFORE targets are computed, so the open
    /// compliance of Sun 17 Jan 2027 goes straight to the 3rd Wednesday, 20 Jan 2027.
    /// </summary>
    [Test]
    public async Task ConvertedRule_LegacyWeekIsRestored_AndTheComplianceMovesOnceToTheRestoredPattern()
    {
        var (arpId, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 17), nextExecution: D(2027, 1, 17));
        await ConvertAsync();
        Assert.That((await ArpAsync(arpId)).RepeatOrdinalWeek, Is.EqualTo(1), "the conversion's hardcoded 1st week");
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 17), D(2026, 1, 17), (await SeedCaseAsync()).Id);

        var result = await RunAsync();

        var arp = await ArpAsync(arpId);
        var planning = await PlanningAsync(planningId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Plan.OrdinalRestorations.Single().NewOrdinal, Is.EqualTo(3));
            Assert.That(arp.RepeatOrdinalWeek, Is.EqualTo(3));
            Assert.That(planning.RepeatOrdinalWeek, Is.EqualTo(3), "the scheduler reads the planning's ordinal");
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Wednesday));
            Assert.That(planning.NextExecutionTime, Is.EqualTo(D(2027, 1, 20)));
        });
        Assert.That(await DeadlineAsync(compliance.Id), Is.EqualTo(D(2027, 1, 20)));
    }

    /// <summary>
    /// Legacy day 31 → ordinal 5, which spills to the month's last Wednesday exactly as
    /// the scheduler (SearchListJob) does: January 2027 has four, so Wed 27 Jan.
    /// </summary>
    [Test]
    public async Task ConvertedRule_OnTheLegacy31st_RestoresOrdinalFive_WhichSpillsToTheLastWeekday()
    {
        var (arpId, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 31), nextExecution: D(2027, 1, 31));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 31), D(2026, 1, 31), (await SeedCaseAsync()).Id);

        await RunAsync();

        await Assert.MultipleAsync(async () =>
        {
            Assert.That((await ArpAsync(arpId)).RepeatOrdinalWeek, Is.EqualTo(5));
            Assert.That((await PlanningAsync(planningId)).NextExecutionTime, Is.EqualTo(D(2027, 1, 27)));
            Assert.That(await DeadlineAsync(compliance.Id), Is.EqualTo(D(2027, 1, 27)));
        });
    }

    /// <summary>
    /// A rule written after its conversion (a person's edit, or any other pass) keeps
    /// its ordinal; it is reported, and its open compliance follows the CURRENT pattern.
    /// </summary>
    [TestCase(1, CalendarMonthlyReanchorRepairService.ReasonEditedAfterConversion)]
    [TestCase(0, CalendarMonthlyReanchorRepairService.ReasonSystemWriteAfterConversion)]
    public async Task RuleWrittenAfterConversion_KeepsItsOrdinal(int updatedByUserId, string expectedReason)
    {
        var (arpId, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 17), nextExecution: D(2027, 1, 17));
        await ConvertAsync();
        var convertedAt = (await BackendConfigurationPnDbContext!.CalendarConfigurations.AsNoTracking()
            .SingleAsync(x => x.AreaRulePlanningId == arpId)).CreatedAt;
        await BackendConfigurationPnDbContext.AreaRulePlannings.Where(x => x.Id == arpId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.UpdatedAt, convertedAt.AddMinutes(5))
                .SetProperty(x => x.UpdatedByUserId, updatedByUserId));
        ForgetTrackedRows();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 17), D(2026, 1, 17), (await SeedCaseAsync()).Id);

        var result = await RunAsync();

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Plan.OrdinalRestorations, Is.Empty);
            Assert.That(result.Plan.OrdinalsKept.Single().Reason, Is.EqualTo(expectedReason));
            Assert.That((await ArpAsync(arpId)).RepeatOrdinalWeek, Is.EqualTo(1));
            Assert.That(await DeadlineAsync(compliance.Id), Is.EqualTo(D(2027, 1, 6)), "1st Wednesday, as the rule now says");
        });
    }

    // ── Rows left for review / skipped ──────────────────────────────────────

    private async Task AssertOnReviewAndUntouched(MonthlyReanchorRepairRunResultModel result, Compliance compliance,
        string reason)
    {
        var item = result.Plan.ReviewItems.SingleOrDefault(x => x.ComplianceId == compliance.Id);
        Assert.That(item, Is.Not.Null, "the row must be on the review list");
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(item!.Reasons, Does.Contain(reason));
            Assert.That(result.Plan.ComplianceMoves.Any(x => x.ComplianceId == compliance.Id), Is.False);
            Assert.That(await DeadlineAsync(compliance.Id), Is.EqualTo(compliance.Deadline), "not moved");
        });
    }

    [Test]
    public async Task TargetTakenByAnotherComplianceOfThePlanning_IsNotMoved_AndIsReviewed()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        // A removed row still holds the unique (PlanningId, Deadline) slot.
        await SeedComplianceAsync(planningId, D(2027, 1, 6), D(2026, 1, 6), (await SeedCaseAsync()).Id,
            Constants.WorkflowStates.Removed);

        var result = await RunAsync();

        await AssertOnReviewAndUntouched(result, compliance, CalendarMonthlyReanchorRepairService.ReasonCollision);
    }

    [Test]
    public async Task OccurrenceExceptionOnTheOldDate_IsNotMoved_AndIsReviewed()
    {
        var (arpId, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        await new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId, OriginalDate = D(2027, 1, 3), NewDate = D(2027, 1, 4),
            CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);

        var result = await RunAsync();

        await AssertOnReviewAndUntouched(result, compliance, CalendarMonthlyReanchorRepairService.ReasonOccurrenceException);
    }

    [Test]
    public async Task TargetBeforeTheCompliancesStartDate_IsNotMoved_AndIsReviewed()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        // Target is Wed 6 Jan 2027, before the row's own StartDate of 10 Jan.
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 31), D(2027, 1, 10), (await SeedCaseAsync()).Id);

        var result = await RunAsync();

        await AssertOnReviewAndUntouched(result, compliance, CalendarMonthlyReanchorRepairService.ReasonBeforeStartDate);
    }

    /// <summary>
    /// Wed 30 Sep 2026 → the 1st Wednesday of September (2 Sep) is before today (29 Sep):
    /// an open task must not be pushed into the past. Its NextExecutionTime is not pulled
    /// into the past either (that would make the scheduler fire at once).
    /// </summary>
    [Test]
    public async Task TargetBeforeToday_IsNotMoved_AndNeitherIsTheNextExecutionTime()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2026, 9, 30));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2026, 9, 30), D(2025, 9, 30), (await SeedCaseAsync()).Id);

        var result = await RunAsync();

        await AssertOnReviewAndUntouched(result, compliance, CalendarMonthlyReanchorRepairService.ReasonBeforeToday);
        var planning = await PlanningAsync(planningId);
        Assert.Multiple(() =>
        {
            Assert.That(planning.NextExecutionTime, Is.EqualTo(D(2026, 9, 30)));
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Wednesday), "the weekday is still mirrored");
            Assert.That(result.Plan.ReviewItems.Any(x => x.Kind == "NextExecutionTime" && x.PlanningId == planningId),
                Is.True);
        });
    }

    /// <summary>
    /// A case the items-planning scheduler deployed through the cloud carries an EndDate
    /// (= the old deadline) the SDK cannot change; moving the deadline LATER would
    /// outlive it on the device.
    /// </summary>
    [Test]
    public async Task CloudDeployedCase_IsNotMovedLater_AndIsReviewed()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3),
            (await SeedCaseAsync(microtingUid: 4711)).Id);

        var result = await RunAsync();

        await AssertOnReviewAndUntouched(result, compliance, CalendarMonthlyReanchorRepairService.ReasonCloudCaseEndDate);
    }

    [TestCase(true, TestName = "Orphan_CompletedCase_IsSkipped_NotReviewed")]
    [TestCase(false, TestName = "Orphan_RemovedCase_IsSkipped_NotReviewed")]
    public async Task Orphan_IsSkipped_NotReviewed(bool completed)
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var sdkCase = completed
            ? await SeedCaseAsync(status: 100, doneAt: D(2026, 9, 1))
            : await SeedCaseAsync(workflowState: Constants.WorkflowStates.Removed);
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), sdkCase.Id);

        var result = await RunAsync();

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Plan.SkippedOrphans.Single().ComplianceId, Is.EqualTo(compliance.Id));
            Assert.That(result.Plan.ReviewItems.Any(x => x.ComplianceId == compliance.Id), Is.False,
                "orphans belong to #1325's cleanup, not to the review list");
            Assert.That(result.Plan.ComplianceMoves, Is.Empty);
            Assert.That(await DeadlineAsync(compliance.Id), Is.EqualTo(D(2027, 1, 3)));
        });
    }

    // ── Dry run / opt-in ────────────────────────────────────────────────────

    [Test]
    public async Task DryRun_WritesNothing_AndTheRunExecutesExactlyThatPlan()
    {
        var (arpA, planningA) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        var (arpB, planningB) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 17), nextExecution: D(2027, 1, 17));
        await ConvertAsync();
        var complianceA = await SeedComplianceAsync(planningA, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        var complianceB = await SeedComplianceAsync(planningB, D(2027, 1, 17), D(2026, 1, 17), (await SeedCaseAsync()).Id);
        var arpVersionsBefore = (await ArpAsync(arpA)).Version + (await ArpAsync(arpB)).Version;

        var plan = await DryRunAsync();
        var replan = await DryRunAsync();

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(plan.PlanHash, Is.EqualTo(replan.PlanHash), "the plan is deterministic");
            Assert.That(plan.OrdinalRestorations.Select(x => x.AreaRulePlanningId), Is.EqualTo(new[] { arpB }));
            Assert.That(plan.PlanningUpdates.Select(x => x.PlanningId), Is.EquivalentTo(new[] { planningA, planningB }));
            Assert.That(plan.ComplianceMoves.Select(x => (x.ComplianceId, x.NewDeadline)),
                Is.EquivalentTo(new[] { (complianceA.Id, D(2027, 1, 6)), (complianceB.Id, D(2027, 1, 20)) }));
            // Nothing written.
            Assert.That((await ArpAsync(arpA)).Version + (await ArpAsync(arpB)).Version, Is.EqualTo(arpVersionsBefore));
            Assert.That((await ArpAsync(arpB)).RepeatOrdinalWeek, Is.EqualTo(1));
            Assert.That((await PlanningAsync(planningA)).DayOfWeek, Is.EqualTo(DayOfWeek.Friday));
            Assert.That((await PlanningAsync(planningB)).NextExecutionTime, Is.EqualTo(D(2027, 1, 17)));
            Assert.That(await DeadlineAsync(complianceA.Id), Is.EqualTo(D(2027, 1, 3)));
            Assert.That(await DeadlineAsync(complianceB.Id), Is.EqualTo(D(2027, 1, 17)));
            Assert.That(await BackendConfigurationPnDbContext!.PluginConfigurationValues
                .AnyAsync(x => x.Name == CalendarMonthlyReanchorRepairService.MarkerName), Is.False);
        });

        var run = await _sut.RunAsync(plan.PlanHash);

        Assert.That(run.Success, Is.True, run.Message);
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(run.Model.Plan.PlanHash, Is.EqualTo(plan.PlanHash));
            Assert.That(run.Model.MovedCompliances, Is.EqualTo(2));
            Assert.That(run.Model.RestoredOrdinals, Is.EqualTo(1));
            Assert.That(run.Model.UpdatedPlannings, Is.EqualTo(2));
            Assert.That(await DeadlineAsync(complianceA.Id), Is.EqualTo(D(2027, 1, 6)));
            Assert.That(await DeadlineAsync(complianceB.Id), Is.EqualTo(D(2027, 1, 20)));
        });
    }

    [Test]
    public async Task Run_WithAPlanHashThatNoLongerMatches_IsRefused_AndWritesNothing()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);

        var run = await _sut.RunAsync("not-the-reviewed-plan");

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(run.Success, Is.False);
            Assert.That(await DeadlineAsync(compliance.Id), Is.EqualTo(D(2027, 1, 3)));
            Assert.That((await PlanningAsync(planningId)).DayOfWeek, Is.EqualTo(DayOfWeek.Friday));
            Assert.That(await BackendConfigurationPnDbContext!.PluginConfigurationValues
                .AnyAsync(x => x.Name == CalendarMonthlyReanchorRepairService.MarkerName), Is.False,
                "a refused run must not burn the one-time marker");
        });
    }

    // ── The conversion itself: fixes the rule, moves no data ────────────────

    /// <summary>
    /// A legacy "on the 17th" task converted NOW: the conversion writes the legacy week
    /// (3rd Wednesday) and mirrors weekday + ordinal into the Planning — the rule only.
    /// The open compliance and NextExecutionTime stay on the legacy Sunday until a
    /// reviewed run moves them; the dry run lists exactly that.
    /// </summary>
    [Test]
    public async Task Conversion_OfALegacy17th_FixesTheRuleOnly_AndTheDryRunListsTheMove()
    {
        var (arpId, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 17), nextExecution: D(2027, 1, 17));
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 17), D(2026, 1, 17), (await SeedCaseAsync()).Id);

        await _backfill.RunIfNeededAsync();
        ForgetTrackedRows();

        var arp = await ArpAsync(arpId);
        var planning = await PlanningAsync(planningId);
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(arp.RepeatOrdinalWeek, Is.EqualTo(3));
            Assert.That(arp.DayOfWeek, Is.EqualTo((int)DayOfWeek.Wednesday));
            Assert.That(planning.RepeatOrdinalWeek, Is.EqualTo(3));
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Wednesday));
            Assert.That(planning.NextExecutionTime, Is.EqualTo(D(2027, 1, 17)), "data is not moved at startup");
            Assert.That(await DeadlineAsync(compliance.Id), Is.EqualTo(D(2027, 1, 17)), "data is not moved at startup");
        });

        var plan = await DryRunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(plan.OrdinalRestorations, Is.Empty, "the conversion already wrote the legacy week");
            Assert.That(plan.ComplianceMoves.Single().NewDeadline, Is.EqualTo(D(2027, 1, 20)));
            Assert.That(plan.PlanningUpdates.Single().NewNextExecutionTime, Is.EqualTo(D(2027, 1, 20)));
        });
    }

    // ── Review list: more reasons ───────────────────────────────────────────

    /// <summary>
    /// GetTasksForWeek (and the device's ListEvents) look an exception up by the
    /// compliance's deadline, so a live exception on the TARGET date — here a deleted
    /// occurrence — would hide the moved row.
    /// </summary>
    [Test]
    public async Task OccurrenceExceptionOnTheTargetDate_IsNotMoved_AndIsReviewed()
    {
        var (arpId, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        await new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId, OriginalDate = D(2027, 1, 6), IsDeleted = true,
            CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);

        var result = await RunAsync();

        await AssertOnReviewAndUntouched(result, compliance,
            CalendarMonthlyReanchorRepairService.ReasonOccurrenceExceptionOnTarget);
    }

    [Test]
    public async Task ComplianceInTheExpiredFolder_IsNotMoved_AndIsReviewed()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        await BackendConfigurationPnDbContext!.Compliances.Where(x => x.Id == compliance.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.MovedToExpiredFolder, true));
        ForgetTrackedRows();

        var result = await RunAsync();

        await AssertOnReviewAndUntouched(result, compliance, CalendarMonthlyReanchorRepairService.ReasonMovedToExpiredFolder);
    }

    /// <summary>An overdue open row (deadline 28 Sep, today 29 Sep) is left for review.</summary>
    [Test]
    public async Task OverdueCompliance_IsNotMoved_AndIsReviewed()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2026, 9, 28), D(2025, 9, 28), (await SeedCaseAsync()).Id);

        var result = await RunAsync();

        await AssertOnReviewAndUntouched(result, compliance, CalendarMonthlyReanchorRepairService.ReasonOldDeadlineBeforeToday);
    }

    /// <summary>
    /// One worker already answered this occurrence (a sibling site's case under the same
    /// PlanningCase is completed): the occurrence is history for that worker, not moved.
    /// </summary>
    [Test]
    public async Task SiblingSitesCaseCompleted_IsNotMoved_AndIsReviewed()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var ownCase = await SeedCaseAsync();
        var siblingCase = await SeedCaseAsync(status: 100, doneAt: D(2026, 9, 20));
        var planningCase = new PlanningCase
        {
            PlanningId = planningId, Status = 66, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        foreach (var sdkCase in new[] { ownCase, siblingCase })
        {
            await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(new PlanningCaseSite
            {
                PlanningId = planningId, PlanningCaseId = planningCase.Id, MicrotingSdkSiteId = _site.Id,
                MicrotingSdkeFormId = 0, MicrotingSdkCaseId = sdkCase.Id, Status = 66,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
        }
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), ownCase.Id);
        await BackendConfigurationPnDbContext!.Compliances.Where(x => x.Id == compliance.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PlanningCaseSiteId, planningCase.Id));
        ForgetTrackedRows();

        var result = await RunAsync();

        await AssertOnReviewAndUntouched(result, compliance, CalendarMonthlyReanchorRepairService.ReasonSiblingCaseCompleted);
    }

    /// <summary>
    /// A rule whose weekday CSV names another day than DayOfWeek would flip on the next
    /// unrelated dialog save; which weekday is meant is unclear, so the rule is listed and
    /// NOTHING of its planning is written — not the planning, not its off-pattern compliance.
    /// </summary>
    [Test]
    public async Task RuleWhoseCsvDisagreesWithItsWeekday_IsListedForReview_AndNothingIsWritten()
    {
        var (arpId, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        // Off-pattern (Sun 3 Jan 2027 vs the rule's Wed 6 Jan): moved if the rule were clear.
        await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        await BackendConfigurationPnDbContext!.AreaRulePlannings.Where(x => x.Id == arpId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RepeatWeekdaysCsv, "1"));
        ForgetTrackedRows();

        var plan = await DryRunAsync();

        var item = plan.ReviewItems.Single(x => x.Kind == "Rule");
        Assert.Multiple(() =>
        {
            Assert.That(item.AreaRulePlanningId, Is.EqualTo(arpId));
            Assert.That(item.Reasons, Does.Contain(CalendarMonthlyReanchorRepairService.ReasonCsvDisagrees));
            Assert.That(plan.OrdinalRestorations, Is.Empty);
            Assert.That(plan.PlanningUpdates, Is.Empty);
            Assert.That(plan.ComplianceMoves, Is.Empty);
        });
    }

    // ── Partial runs, resume, conditional writes, refusals ─────────────────

    /// <summary>
    /// The ordinal restore of rule B fails: B's later steps (planning, compliance) were
    /// computed from the restored ordinal and are NOT written; rule A completes. The
    /// marker is "partial", and a fresh dry run + run finishes B and marks it "done".
    /// </summary>
    [Test]
    public async Task FailedStep_SkipsThatPlanningsLaterSteps_AndAPartialRunCanBeResumed()
    {
        var (_, planningA) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        var (arpB, planningB) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 17), nextExecution: D(2027, 1, 17));
        await ConvertAsync();
        var complianceA = await SeedComplianceAsync(planningA, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        var complianceB = await SeedComplianceAsync(planningB, D(2027, 1, 17), D(2026, 1, 17), (await SeedCaseAsync()).Id);
        _sut.OnBeforeWrite = entity => entity is AreaRulePlanning { Id: var id } && id == arpB
            ? throw new InvalidOperationException("simulated write failure")
            : Task.CompletedTask;

        var plan = await DryRunAsync();
        var first = await _sut.RunAsync(plan.PlanHash);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(first.Success, Is.False, "a run with failures reports failure");
            Assert.That(first.Model.Failures, Has.Count.EqualTo(1));
            Assert.That(first.Model.Skipped.Where(x => x.Contains("not attempted")).ToList(), Has.Count.EqualTo(2),
                "B's planning row and compliance are reported as not attempted");
            Assert.That(await DeadlineAsync(complianceA.Id), Is.EqualTo(D(2027, 1, 6)), "A completes");
            Assert.That((await ArpAsync(arpB)).RepeatOrdinalWeek, Is.EqualTo(1), "B's restore failed");
            Assert.That((await PlanningAsync(planningB)).DayOfWeek, Is.EqualTo(DayOfWeek.Friday),
                "B's planning step is skipped after its failed restore");
            Assert.That(await DeadlineAsync(complianceB.Id), Is.EqualTo(D(2027, 1, 17)),
                "B's compliance is not moved to a date computed from the unwritten ordinal");
            Assert.That(await MarkerAsync(), Is.EqualTo(CalendarMonthlyReanchorRepairService.MarkerPartial));
        });

        _sut.OnBeforeWrite = _ => Task.CompletedTask;
        var resumePlan = await DryRunAsync();
        Assert.Multiple(() =>
        {
            Assert.That(resumePlan.MarkerState, Is.EqualTo(CalendarMonthlyReanchorRepairService.MarkerPartial));
            Assert.That(resumePlan.ComplianceMoves.Select(x => x.ComplianceId), Is.EqualTo(new[] { complianceB.Id }),
                "A's rows are done and drop out of the recomputed plan");
        });
        var resumed = await _sut.RunAsync(resumePlan.PlanHash);

        Assert.That(resumed.Success, Is.True, resumed.Message);
        await Assert.MultipleAsync(async () =>
        {
            Assert.That((await ArpAsync(arpB)).RepeatOrdinalWeek, Is.EqualTo(3));
            Assert.That(await DeadlineAsync(complianceB.Id), Is.EqualTo(D(2027, 1, 20)));
            Assert.That(await MarkerAsync(), Is.EqualTo(CalendarMonthlyReanchorRepairService.MarkerDone));
        });
    }

    /// <summary>
    /// A row that changes between planning and writing (here: someone re-dates the
    /// compliance while the run is under way) is skipped and logged, not overwritten.
    /// </summary>
    [Test]
    public async Task RowChangedAfterPlanning_IsSkipped_NotOverwritten()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        var complianceId = compliance.Id;
        _sut.OnBeforeWrite = async entity =>
        {
            if (entity is Planning)
            {
                await BackendConfigurationPnDbContext!.Compliances.Where(x => x.Id == complianceId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Deadline, D(2027, 1, 4)));
            }
        };

        var plan = await DryRunAsync();
        var run = await _sut.RunAsync(plan.PlanHash);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(run.Success, Is.True, "a skipped row is not a failure");
            Assert.That(run.Model.Skipped, Has.Count.EqualTo(1));
            Assert.That(run.Model.MovedCompliances, Is.Zero);
            Assert.That(await DeadlineAsync(complianceId), Is.EqualTo(D(2027, 1, 4)), "the concurrent change wins");
            Assert.That(await MarkerAsync(), Is.EqualTo(CalendarMonthlyReanchorRepairService.MarkerPartial),
                "a skipped row keeps the repair open for another run");
        });

        // A follow-up dry run re-evaluates the row from its new date and a run completes.
        _sut.OnBeforeWrite = _ => Task.CompletedTask;
        ForgetTrackedRows();
        var followUpPlan = await DryRunAsync();
        var followUp = await _sut.RunAsync(followUpPlan.PlanHash);

        Assert.That(followUp.Success, Is.True, followUp.Message);
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await DeadlineAsync(complianceId), Is.EqualTo(D(2027, 1, 6)));
            Assert.That(await MarkerAsync(), Is.EqualTo(CalendarMonthlyReanchorRepairService.MarkerDone));
        });
    }

    private async Task SeedMarkerAsync(string value, DateTime updatedAt)
        => await BackendConfigurationPnDbContext!.Database.ExecuteSqlRawAsync(
            @"INSERT INTO `PluginConfigurationValues`
                  (`Name`, `Value`, `CreatedAt`, `UpdatedAt`, `Version`, `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`)
              VALUES ({0}, {1}, {2}, {2}, 1, {3}, 1, 0)",
            CalendarMonthlyReanchorRepairService.MarkerName, value, updatedAt, Constants.WorkflowStates.Created);

    /// <summary>A fresh "running" marker belongs to a run in progress: refused, nothing written.</summary>
    [Test]
    public async Task Run_WhileAnotherRunIsInProgress_IsRefused()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        await SeedMarkerAsync(CalendarMonthlyReanchorRepairService.MarkerRunning, DateTime.UtcNow);

        var plan = await DryRunAsync();
        var run = await _sut.RunAsync(plan.PlanHash);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(run.Success, Is.False);
            Assert.That(run.Message, Does.Contain("in progress"));
            Assert.That(await DeadlineAsync(compliance.Id), Is.EqualTo(D(2027, 1, 3)));
            Assert.That(await MarkerAsync(), Is.EqualTo(CalendarMonthlyReanchorRepairService.MarkerRunning));
        });
    }

    /// <summary>A "running" marker older than an hour is a crashed run: it is re-claimed and completed.</summary>
    [Test]
    public async Task Run_AfterAnAbandonedRun_ReclaimsAndCompletes()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        await SeedMarkerAsync(CalendarMonthlyReanchorRepairService.MarkerRunning, DateTime.UtcNow.AddHours(-2));

        var plan = await DryRunAsync();
        var run = await _sut.RunAsync(plan.PlanHash);

        Assert.That(run.Success, Is.True, run.Message);
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await DeadlineAsync(compliance.Id), Is.EqualTo(D(2027, 1, 6)));
            Assert.That(await MarkerAsync(), Is.EqualTo(CalendarMonthlyReanchorRepairService.MarkerDone));
        });
    }

    [Test]
    public async Task Run_WithAnEmptyPlan_IsRefused_AndDoesNotSetTheMarker()
    {
        var plan = await DryRunAsync();

        var run = await _sut.RunAsync(plan.PlanHash);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(run.Success, Is.False);
            Assert.That(await MarkerAsync(), Is.Null);
        });
    }

    [Test]
    public async Task Run_WithoutAPlanHash_IsRefused()
    {
        var run = await _sut.RunAsync(string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(run.Success, Is.False);
            Assert.That(run.Message, Does.Contain("planHash is required"));
        });
    }

    // ── Copilot review round ────────────────────────────────────────────────

    /// <summary>
    /// Another occurrence moved ONTO the target (exception NewDate = target, OriginalDate
    /// elsewhere) occupies it: the row is reviewed, not moved.
    /// </summary>
    [Test]
    public async Task OccurrenceMovedOntoTheTargetDate_IsNotMoved_AndIsReviewed()
    {
        var (arpId, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        await new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId, OriginalDate = D(2028, 1, 5), NewDate = D(2027, 1, 6),
            CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);

        var result = await RunAsync();

        await AssertOnReviewAndUntouched(result, compliance,
            CalendarMonthlyReanchorRepairService.ReasonOccurrenceExceptionOnTarget);
    }

    /// <summary>
    /// A "partial" run whose recomputed plan is empty (everything was finished in the
    /// meantime) is finalized to "done" instead of being refused for ever.
    /// </summary>
    [Test]
    public async Task PartialRun_WithNothingLeftToDo_IsFinalizedToDone()
    {
        await SeedMarkerAsync(CalendarMonthlyReanchorRepairService.MarkerPartial, DateTime.UtcNow);

        var plan = await DryRunAsync();
        var run = await _sut.RunAsync(plan.PlanHash);

        Assert.That(run.Success, Is.True, run.Message);
        Assert.That(await MarkerAsync(), Is.EqualTo(CalendarMonthlyReanchorRepairService.MarkerDone));
    }

    /// <summary>
    /// A run whose claim is re-claimed while it runs (as an abandoned-run reclaim by
    /// another run would do) must not overwrite the new owner's marker when it finishes.
    /// </summary>
    [Test]
    public async Task ReclaimedRun_OldOwnerCannotFinalizeTheMarker()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        const string otherOwner = "running:another-run";
        _sut.OnBeforeWrite = async _ =>
            await BackendConfigurationPnDbContext!.Database.ExecuteSqlRawAsync(
                "UPDATE `PluginConfigurationValues` SET `Value` = {0} WHERE `Name` = {1}",
                otherOwner, CalendarMonthlyReanchorRepairService.MarkerName);

        var plan = await DryRunAsync();
        var run = await _sut.RunAsync(plan.PlanHash);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(run.Success, Is.False, "a run that lost its claim reports it");
            Assert.That(run.Model.Failures, Has.Some.Contains("claim"));
            Assert.That(await MarkerAsync(), Is.EqualTo(otherOwner), "the new owner's claim is untouched");
        });
    }

    /// <summary>A reviewed plan hash is valid only on the (Copenhagen) day it was made.</summary>
    [Test]
    public async Task PlanHash_DependsOnThePlanDate()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);

        var today = await DryRunAsync();
        _sut.UtcNow = () => Now.AddDays(1);
        var tomorrow = await DryRunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tomorrow.ComplianceMoves.Select(x => (x.ComplianceId, x.NewDeadline)),
                Is.EqualTo(today.ComplianceMoves.Select(x => (x.ComplianceId, x.NewDeadline))), "same writes");
            Assert.That(tomorrow.PlanHash, Is.Not.EqualTo(today.PlanHash), "but a different day");
        });
    }

    /// <summary>
    /// Every move guard is re-evaluated right before the write: an exception created on the
    /// target after the plan was computed makes the row skipped, not moved.
    /// </summary>
    [Test]
    public async Task ExceptionAddedAfterPlanning_OnTheTarget_IsSkipped_NotMoved()
    {
        var (arpId, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        var compliance = await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        _sut.OnBeforeWrite = async entity =>
        {
            if (entity is Planning)
            {
                await new CalendarOccurrenceException
                {
                    AreaRulePlanningId = arpId, OriginalDate = D(2027, 1, 6), IsDeleted = true,
                    CreatedByUserId = 1, UpdatedByUserId = 1
                }.Create(BackendConfigurationPnDbContext!);
            }
        };

        var plan = await DryRunAsync();
        var run = await _sut.RunAsync(plan.PlanHash);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(run.Success, Is.True, "a skipped row is not a failure");
            Assert.That(run.Model.Skipped, Has.Some.Contains(CalendarMonthlyReanchorRepairService.ReasonOccurrenceExceptionOnTarget));
            Assert.That(run.Model.MovedCompliances, Is.Zero);
            Assert.That(await DeadlineAsync(compliance.Id), Is.EqualTo(D(2027, 1, 3)));
            Assert.That(await MarkerAsync(), Is.EqualTo(CalendarMonthlyReanchorRepairService.MarkerPartial));
        });
    }

    // ── Copilot review round 2 ──────────────────────────────────────────────

    /// <summary>
    /// The claim is taken over while planning A is written: the lease renewal before
    /// planning B fails, so the run stops — B is not written — and reports the takeover.
    /// </summary>
    [Test]
    public async Task ClaimReplacedMidRun_StopsWritingFurtherPlannings()
    {
        var (_, planningA) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        var (_, planningB) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 17), nextExecution: D(2027, 1, 17));
        await ConvertAsync();
        await SeedComplianceAsync(planningA, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        var complianceB = await SeedComplianceAsync(planningB, D(2027, 1, 17), D(2026, 1, 17), (await SeedCaseAsync()).Id);
        const string otherOwner = "running:another-run";
        var swapped = false;
        _sut.OnBeforeWrite = async entity =>
        {
            if (!swapped && entity is Planning { Id: var id } && id == planningA)
            {
                swapped = true;
                await BackendConfigurationPnDbContext!.Database.ExecuteSqlRawAsync(
                    "UPDATE `PluginConfigurationValues` SET `Value` = {0} WHERE `Name` = {1}",
                    otherOwner, CalendarMonthlyReanchorRepairService.MarkerName);
            }
        };

        var plan = await DryRunAsync();
        var run = await _sut.RunAsync(plan.PlanHash);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(run.Success, Is.False);
            Assert.That(run.Model.Failures, Has.Some.Contains("claim"));
            Assert.That(run.Model.Skipped, Has.Some.Contains("lost its claim"));
            Assert.That(await DeadlineAsync(complianceB.Id), Is.EqualTo(D(2027, 1, 17)), "B is not written");
            Assert.That((await PlanningAsync(planningB)).DayOfWeek, Is.EqualTo(DayOfWeek.Friday), "B's planning neither");
            Assert.That(await MarkerAsync(), Is.EqualTo(otherOwner));
        });
    }

    /// <summary>A change that only alters the review list (no write) still changes the plan hash.</summary>
    [Test]
    public async Task PlanHash_CoversTheReviewList()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        var before = await DryRunAsync();

        // An overdue open row (deadline 28 Sep, today 29 Sep): a review item, no write.
        await SeedComplianceAsync(planningId, D(2026, 9, 28), D(2025, 9, 28), (await SeedCaseAsync()).Id);
        ForgetTrackedRows();
        var after = await DryRunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(after.ComplianceMoves.Select(x => (x.ComplianceId, x.NewDeadline)),
                Is.EqualTo(before.ComplianceMoves.Select(x => (x.ComplianceId, x.NewDeadline))), "same writes");
            Assert.That(after.ReviewItems, Has.Count.EqualTo(before.ReviewItems.Count + 1));
            Assert.That(after.PlanHash, Is.Not.EqualTo(before.PlanHash), "but a different reviewed plan");
        });
    }

    /// <summary>
    /// An eligible compliance created while the run is applying was never reviewed: it is
    /// not written, the marker stays "partial", and the next dry run lists it.
    /// </summary>
    [Test]
    public async Task WorkArrivingDuringTheRun_LeavesThePartialMarker_AndTheNextDryRunListsIt()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        var arrivedCaseId = (await SeedCaseAsync()).Id;
        Compliance arrived = null!;
        _sut.OnBeforeWrite = async entity =>
        {
            if (arrived == null && entity is Planning)
            {
                // Sun 2 Jan 2028; the rule's 1st Wednesday that month is 5 Jan.
                arrived = await SeedComplianceAsync(planningId, D(2028, 1, 2), D(2027, 1, 2), arrivedCaseId);
            }
        };

        var plan = await DryRunAsync();
        var run = await _sut.RunAsync(plan.PlanHash);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(run.Success, Is.True, run.Message);
            Assert.That(run.Model.MovedCompliances, Is.EqualTo(1), "the reviewed move is written");
            Assert.That(run.Model.ArrivedDuringRun, Has.Some.Contains($"compliance {arrived.Id}"));
            Assert.That(await DeadlineAsync(arrived.Id), Is.EqualTo(D(2028, 1, 2)), "the unreviewed one is not");
            Assert.That(await MarkerAsync(), Is.EqualTo(CalendarMonthlyReanchorRepairService.MarkerPartial));
        });

        _sut.OnBeforeWrite = _ => Task.CompletedTask;
        var next = await DryRunAsync();
        Assert.That(next.ComplianceMoves.Select(x => (x.ComplianceId, x.NewDeadline)),
            Is.EqualTo(new[] { (arrived.Id, D(2028, 1, 5)) }));
    }

    /// <summary>
    /// A row the plan loaded on-pattern and another writer moves off-pattern during the run
    /// is new work too: the final scan reads the database, not the run's tracked copy.
    /// </summary>
    [Test]
    public async Task RowMovedOffPatternDuringTheRun_IsSeenByTheFinalScan()
    {
        var (_, planningId) = await SeedLegacyYearlyTaskAsync(D(2024, 1, 3), nextExecution: D(2027, 1, 3));
        await ConvertAsync();
        await SeedComplianceAsync(planningId, D(2027, 1, 3), D(2026, 1, 3), (await SeedCaseAsync()).Id);
        // Wed 5 Jan 2028 = the rule's 1st Wednesday that month: on-pattern, not in the plan.
        var onPattern = await SeedComplianceAsync(planningId, D(2028, 1, 5), D(2027, 1, 5), (await SeedCaseAsync()).Id);
        ForgetTrackedRows();
        var moved = false;
        _sut.OnBeforeWrite = async entity =>
        {
            if (!moved && entity is Planning)
            {
                moved = true;
                await BackendConfigurationPnDbContext!.Compliances.Where(x => x.Id == onPattern.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Deadline, D(2028, 1, 2)));
            }
        };

        var plan = await DryRunAsync();
        var run = await _sut.RunAsync(plan.PlanHash);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(run.Success, Is.True, run.Message);
            Assert.That(plan.ComplianceMoves.Select(x => x.ComplianceId), Does.Not.Contain(onPattern.Id));
            Assert.That(run.Model.ArrivedDuringRun, Has.Some.Contains($"compliance {onPattern.Id}"));
            Assert.That(await MarkerAsync(), Is.EqualTo(CalendarMonthlyReanchorRepairService.MarkerPartial));
        });
    }
}
