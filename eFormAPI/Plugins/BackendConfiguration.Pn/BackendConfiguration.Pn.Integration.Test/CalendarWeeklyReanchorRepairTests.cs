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

using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.CalendarConfigurationBackfillService;
using BackendConfiguration.Pn.Services.CalendarWeeklyReanchorRepair;
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
/// #1375 — the one-time weekly re-anchor repair
/// (<see cref="CalendarWeeklyReanchorRepairService"/>).
///
/// Every rule is produced the way production got it before #1375: a legacy weekly
/// task-wizard planning started on a Monday is run through the REAL
/// <see cref="CalendarConfigurationBackfillService"/> while it has no cadence (so it
/// converts to Monday, as every pre-#1375 conversion did), and its cadence is then put
/// on Thursdays underneath it — the reported shape: a Monday rule with Thursday deploys.
///
/// "Today" is pinned to Tuesday 2026-09-29 (Copenhagen) through the clock seam.
/// 2026-01-05, 2026-09-28 and 2026-10-05 are Mondays; 2026-10-01 and 2026-10-08 Thursdays.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarWeeklyReanchorRepairTests : TestBaseSetup
{
    private static readonly DateTime Now = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StartMonday = D(2026, 1, 5);
    private static readonly DateTime NextThursday = D(2026, 10, 1);

    private CalendarConfigurationBackfillService _backfill = null!;
    private CalendarWeeklyReanchorRepairService _sut = null!;
    private Site _site = null!;
    private int _nextUid = 2_000_375_100;

    private static DateTime D(int y, int m, int d) => new(y, m, d, 0, 0, 0);

    [SetUp]
    public async Task SetUpRepair()
    {
        var ctx = BackendConfigurationPnDbContext!;
        ctx.Compliances.RemoveRange(ctx.Compliances);
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
            x.Name == CalendarWeeklyReanchorRepairService.MarkerName
            || x.Name == CalendarConfigurationBackfillService.LegacyStartHourRepairMarkerName));
        await ctx.SaveChangesAsync();
        ItemsPlanningPnDbContext!.Plannings.RemoveRange(ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

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

        _sut = new CalendarWeeklyReanchorRepairService(ctx, ItemsPlanningPnDbContext, coreHelper,
            TestContextLogger<CalendarWeeklyReanchorRepairService>.Instance)
        {
            UtcNow = () => Now
        };
        _backfill = new CalendarConfigurationBackfillService(ctx, ItemsPlanningPnDbContext,
            TestContextLogger<CalendarConfigurationBackfillService>.Instance);
    }

    // ── Seeding ─────────────────────────────────────────────────────────────

    /// <summary>A legacy weekly wizard task started on <see cref="StartMonday"/>; not yet converted.</summary>
    private async Task<(int ArpId, int PlanningId)> SeedLegacyWeeklyTaskAsync(int repeatEvery = 1)
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
            Enabled = true, RepeatType = RepeatType.Week, RepeatEvery = repeatEvery, StartDate = StartMonday,
            RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = StartMonday, Status = true,
            RepeatType = 2, RepeatEvery = repeatEvery, ComplianceEnabled = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);
        return (arp.Id, planning.Id);
    }

    /// <summary>
    /// Converts every seeded task while none has a cadence (each becomes a Monday rule, the
    /// pre-#1375 result), then puts <paramref name="nextExecutionTime"/> under the planning.
    /// ExecuteUpdate leaves AreaRulePlanning.UpdatedAt alone, so the rule still counts as
    /// "not edited since the conversion".
    /// </summary>
    private async Task ConvertThenSetCadenceAsync(int planningId, DateTime nextExecutionTime)
    {
        await _backfill.RunIfNeededAsync();
        await ItemsPlanningPnDbContext!.Plannings
            .Where(p => p.Id == planningId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.NextExecutionTime, nextExecutionTime)
                .SetProperty(p => p.LastExecutedTime, nextExecutionTime.AddDays(-7)));
        ForgetTrackedRows();
    }

    /// <summary>ExecuteUpdate bypasses the change tracker; production runs the repair on a fresh scope.</summary>
    private void ForgetTrackedRows()
    {
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        ItemsPlanningPnDbContext!.ChangeTracker.Clear();
    }

    private async Task<Compliance> SeedOpenComplianceAsync(int planningId, DateTime deadline)
    {
        var sdkCase = new Case
        {
            SiteId = _site.Id, Status = 33, MicrotingUid = _nextUid++, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        var compliance = new Compliance
        {
            ItemName = "Weekly check", PlanningId = planningId, Deadline = deadline, StartDate = deadline.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance;
    }

    private async Task<WeeklyReanchorRepairPlanModel> DryRunAsync()
    {
        var result = await _sut.DryRunAsync();
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model;
    }

    private async Task<AreaRulePlanning> ArpAsync(int id)
        => await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == id);

    private async Task<Planning> PlanningAsync(int id)
        => await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().SingleAsync(x => x.Id == id);

    private async Task<string?> MarkerAsync()
        => await BackendConfigurationPnDbContext!.PluginConfigurationValues.AsNoTracking()
            .Where(x => x.Name == CalendarWeeklyReanchorRepairService.MarkerName)
            .Select(x => x.Value)
            .FirstOrDefaultAsync();

    private async Task AssertRuleAndPlanningOnMonday(int arpId, int planningId)
    {
        var arp = await ArpAsync(arpId);
        var planning = await PlanningAsync(planningId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.DayOfWeek, Is.EqualTo((int)DayOfWeek.Monday));
            Assert.That(arp.RepeatWeekdaysCsv, Is.EqualTo("1"));
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
        });
    }

    // ── Tests ───────────────────────────────────────────────────────────────

    [Test]
    public async Task DryRun_MondayRuleWithThursdayCadence_PlansThursdayAndWritesNothing()
    {
        var (arpId, planningId) = await SeedLegacyWeeklyTaskAsync();
        await ConvertThenSetCadenceAsync(planningId, NextThursday);
        await SeedOpenComplianceAsync(planningId, NextThursday);

        var plan = await DryRunAsync();

        var update = plan.RuleUpdates.Single();
        Assert.Multiple(() =>
        {
            Assert.That(update.AreaRulePlanningId, Is.EqualTo(arpId));
            Assert.That(update.PlanningId, Is.EqualTo(planningId));
            Assert.That(update.OldRuleDayOfWeek, Is.EqualTo((int)DayOfWeek.Monday));
            Assert.That(update.OldWeekdaysCsv, Is.EqualTo("1"));
            Assert.That(update.OldPlanningDayOfWeek, Is.EqualTo((int)DayOfWeek.Monday));
            Assert.That(update.NewDayOfWeek, Is.EqualTo((int)DayOfWeek.Thursday));
            Assert.That(update.NewNextExecutionTime, Is.EqualTo(NextThursday), "already on the cadence");
            Assert.That(plan.ReviewItems, Is.Empty);
            Assert.That(plan.PlanHash, Is.Not.Empty);
            Assert.That(plan.MarkerState, Is.Null);
        });
        await AssertRuleAndPlanningOnMonday(arpId, planningId);
        Assert.That(await MarkerAsync(), Is.Null, "a dry run claims nothing");
    }

    [Test]
    public async Task Run_WithReviewedHash_AlignsRuleAndPlanningOnTheCadence_AndASecondRunIsRefused()
    {
        var (arpId, planningId) = await SeedLegacyWeeklyTaskAsync();
        await ConvertThenSetCadenceAsync(planningId, NextThursday);
        var compliance = await SeedOpenComplianceAsync(planningId, NextThursday);
        var plan = await DryRunAsync();

        var result = await _sut.RunAsync(plan.PlanHash);

        Assert.That(result.Success, Is.True, result.Message);
        var arp = await ArpAsync(arpId);
        var planning = await PlanningAsync(planningId);
        Assert.Multiple(async () =>
        {
            Assert.That(result.Model.UpdatedRules, Is.EqualTo(1));
            Assert.That(result.Model.Skipped, Is.Empty);
            Assert.That(result.Model.Failures, Is.Empty);
            Assert.That(arp.DayOfWeek, Is.EqualTo((int)DayOfWeek.Thursday));
            Assert.That(arp.RepeatWeekdaysCsv, Is.EqualTo("4"));
            Assert.That(planning.DayOfWeek, Is.EqualTo(DayOfWeek.Thursday));
            Assert.That(planning.NextExecutionTime, Is.EqualTo(NextThursday));
            Assert.That((await BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
                .SingleAsync(x => x.Id == compliance.Id)).Deadline, Is.EqualTo(NextThursday), "no compliance moves");
            Assert.That(await MarkerAsync(), Is.EqualTo(CalendarRepairRunMarker.Done));
        });

        var again = await DryRunAsync();
        Assert.Multiple(() =>
        {
            Assert.That(again.AlreadyExecuted, Is.True);
            Assert.That(again.RuleUpdates, Is.Empty, "nothing left to align");
            Assert.That(again.ReviewItems, Is.Empty);
        });
        var secondRun = await _sut.RunAsync(again.PlanHash);
        Assert.That(secondRun.Success, Is.False, "the marker refuses a second run");
    }

    [Test]
    public async Task Run_WithoutOrWithStaleHash_IsRefusedAndWritesNothing()
    {
        var (arpId, planningId) = await SeedLegacyWeeklyTaskAsync();
        await ConvertThenSetCadenceAsync(planningId, NextThursday);
        var plan = await DryRunAsync();

        var missing = await _sut.RunAsync(string.Empty);

        // The data changes after the review: the scheduler deploys again a week on.
        await ItemsPlanningPnDbContext!.Plannings.Where(p => p.Id == planningId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.NextExecutionTime, NextThursday.AddDays(7)));
        ForgetTrackedRows();
        var stale = await _sut.RunAsync(plan.PlanHash);

        Assert.Multiple(() =>
        {
            Assert.That(missing.Success, Is.False, "a missing hash is refused");
            Assert.That(stale.Success, Is.False, "a hash of an outdated plan is refused");
        });
        await AssertRuleAndPlanningOnMonday(arpId, planningId);
        Assert.That(await MarkerAsync(), Is.Null);
    }

    [Test]
    public async Task Run_EmptyPlan_IsRefusedWithoutClaimingTheMarker()
    {
        var (_, planningId) = await SeedLegacyWeeklyTaskAsync();
        // Cadence already on the rule's Monday.
        await ConvertThenSetCadenceAsync(planningId, D(2026, 10, 5));

        var plan = await DryRunAsync();
        var result = await _sut.RunAsync(plan.PlanHash);

        Assert.Multiple(async () =>
        {
            Assert.That(plan.RuleUpdates, Is.Empty, "an already-aligned rule is left alone");
            Assert.That(plan.ReviewItems, Is.Empty);
            Assert.That(result.Success, Is.False);
            Assert.That(await MarkerAsync(), Is.Null);
        });
    }

    [Test]
    public async Task DryRun_MultiDayRule_IsNotLookedAt()
    {
        var (arpId, planningId) = await SeedLegacyWeeklyTaskAsync();
        await ConvertThenSetCadenceAsync(planningId, NextThursday);
        await BackendConfigurationPnDbContext!.AreaRulePlannings.Where(x => x.Id == arpId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RepeatWeekdaysCsv, "1,4"));
        ForgetTrackedRows();

        var plan = await DryRunAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(plan.RuleUpdates, Is.Empty);
            Assert.That(plan.ReviewItems, Is.Empty);
            Assert.That((await ArpAsync(arpId)).RepeatWeekdaysCsv, Is.EqualTo("1,4"));
        });
    }

    [Test]
    public async Task Run_EveryOtherWeekWithCadenceInTheOffWeeks_KeepsMondayAndMovesTheNextRunOntoTheRule()
    {
        // Thu 2026-10-08 is in the week of Mon 2026-10-05, 39 weeks after the anchor week:
        // the rule (every 2nd week from the week of 2026-01-05) does not draw that week.
        var (arpId, planningId) = await SeedLegacyWeeklyTaskAsync(repeatEvery: 2);
        await ConvertThenSetCadenceAsync(planningId, D(2026, 10, 8));

        var plan = await DryRunAsync();
        var result = await _sut.RunAsync(plan.PlanHash);

        Assert.That(result.Success, Is.True, result.Message);
        var update = plan.RuleUpdates.Single();
        Assert.Multiple(() =>
        {
            Assert.That(update.NewDayOfWeek, Is.EqualTo((int)DayOfWeek.Monday));
            Assert.That(update.NewNextExecutionTime, Is.EqualTo(D(2026, 10, 12)), "the rule's next Monday");
        });
        await AssertRuleAndPlanningOnMonday(arpId, planningId);
        Assert.That((await PlanningAsync(planningId)).NextExecutionTime, Is.EqualTo(D(2026, 10, 12)));
    }

    [Test]
    public async Task Run_EveryOtherWeekWithCadenceInTheOffWeeksOnTheRulesWeekday_MovesTheNextRunOntoTheRule()
    {
        // Mon 2026-10-05 is already on the rule's weekday, but 39 weeks after the anchor
        // week: the rule (every 2nd week from the week of 2026-01-05) does not draw it.
        var (arpId, planningId) = await SeedLegacyWeeklyTaskAsync(repeatEvery: 2);
        await ConvertThenSetCadenceAsync(planningId, D(2026, 10, 5));

        var plan = await DryRunAsync();
        var result = await _sut.RunAsync(plan.PlanHash);

        Assert.That(result.Success, Is.True, result.Message);
        var update = plan.RuleUpdates.Single();
        Assert.Multiple(() =>
        {
            Assert.That(update.NewDayOfWeek, Is.EqualTo((int)DayOfWeek.Monday));
            Assert.That(update.NewNextExecutionTime, Is.EqualTo(D(2026, 10, 12)), "the rule's next Monday");
        });
        await AssertRuleAndPlanningOnMonday(arpId, planningId);
        Assert.That((await PlanningAsync(planningId)).NextExecutionTime, Is.EqualTo(D(2026, 10, 12)));
    }

    [Test]
    public async Task DryRun_OpenComplianceOnAnotherWeekdayThanTheCadence_GoesOnTheReviewList()
    {
        var (arpId, planningId) = await SeedLegacyWeeklyTaskAsync();
        await ConvertThenSetCadenceAsync(planningId, NextThursday);
        var mondayRow = await SeedOpenComplianceAsync(planningId, D(2026, 9, 28));

        var plan = await DryRunAsync();

        var review = plan.ReviewItems.Single();
        Assert.Multiple(() =>
        {
            Assert.That(plan.RuleUpdates, Is.Empty);
            Assert.That(review.AreaRulePlanningId, Is.EqualTo(arpId));
            Assert.That(review.TargetDayOfWeek, Is.EqualTo((int)DayOfWeek.Thursday));
            Assert.That(review.ComplianceIds, Is.EqualTo(new[] { mondayRow.Id }));
            Assert.That(review.Reasons,
                Is.EqualTo(new[] { CalendarWeeklyReanchorRepairService.ReasonOpenComplianceOnAnotherWeekday }));
        });
    }

    [Test]
    public async Task DryRun_RuleEditedAfterConversion_GoesOnTheReviewList()
    {
        var (arpId, planningId) = await SeedLegacyWeeklyTaskAsync();
        await ConvertThenSetCadenceAsync(planningId, NextThursday);
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.SingleAsync(x => x.Id == arpId);
        arp.UpdatedByUserId = 7;
        await arp.Update(BackendConfigurationPnDbContext);
        ForgetTrackedRows();

        var plan = await DryRunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(plan.RuleUpdates, Is.Empty, "a person's later edit is not overwritten");
            Assert.That(plan.ReviewItems.Single().Reasons,
                Is.EqualTo(new[] { CalendarWeeklyReanchorRepairService.ReasonEditedAfterConversion }));
        });
    }

    [Test]
    public async Task Run_RowChangedAfterThePlan_IsSkippedAndTheMarkerStaysPartial()
    {
        var (arpId, planningId) = await SeedLegacyWeeklyTaskAsync();
        await ConvertThenSetCadenceAsync(planningId, NextThursday);
        var plan = await DryRunAsync();
        // A write lands between the plan and the planning's write.
        var first = true;
        _sut.OnBeforeWrite = async entity =>
        {
            if (first && entity is Planning)
            {
                first = false;
                await BackendConfigurationPnDbContext!.AreaRulePlannings.Where(x => x.Id == arpId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, DateTime.UtcNow.AddMinutes(1)));
            }
        };

        var result = await _sut.RunAsync(plan.PlanHash);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Model.UpdatedRules, Is.EqualTo(0));
            Assert.That(result.Model.Skipped.Single(), Does.StartWith($"AreaRulePlanning {arpId}"));
            Assert.That(await MarkerAsync(), Is.EqualTo(CalendarRepairRunMarker.Partial));
            Assert.That((await ArpAsync(arpId)).DayOfWeek, Is.EqualTo((int)DayOfWeek.Monday), "not overwritten");
        });
    }
}
