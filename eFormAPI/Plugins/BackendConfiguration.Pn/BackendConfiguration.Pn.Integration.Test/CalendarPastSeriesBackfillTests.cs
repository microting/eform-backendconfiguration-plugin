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

namespace BackendConfiguration.Pn.Integration.Test;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.EventDeployService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;
using BcCompliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;
using SdkSite = Microting.eForm.Infrastructure.Data.Entities.Site;
using CalendarSvc =
    BackendConfiguration.Pn.Services.BackendConfigurationCalendarService.BackendConfigurationCalendarService;

/// <summary>
/// #1122 §2/§3 — <see cref="CalendarPastSeriesBackfillService"/> and the
/// relocate-vs-retract period gate.
///
/// WHY this exists. Until #1122 it was impossible to give a task a start date in
/// the past; the platform simply refused. Lifting that guard opened two failure
/// modes that nothing else in the codebase defends against:
///
/// 1. THE SCHEDULER RUNS AWAY. ItemsPlanning's SearchListJob.ExecuteDeploy picks
///    plannings on three fields — NextExecutionTime (null counts as "due"),
///    StartDate &lt;= today, Enabled — and nothing else; ComplianceEnabled is not
///    in that filter. A planning re-anchored into the past therefore has ONE
///    missed occurrence back-deployed per hourly run, indefinitely. Worse,
///    ExecuteCleanUp re-arms NextExecutionTime = null for any planning whose
///    LastExecutedTime is null, so writing only NextExecutionTime is undone on
///    the next pass. Both fields, or neither works — that is what
///    <see cref="Backfill_PastAnchor_WritesBothSchedulerFieldsSoTheJobCannotBackDeploy"/>
///    and <see cref="Backfill_EndedSeries_ParksTheSchedulerOnTheSentinel"/> lock in.
///
/// 2. THE OVERDUE OCCURRENCES NEVER APPEAR. EnsureDeployedAsync deliberately
///    refuses to deploy anything before today, so the "røde opgaver" the user
///    asked for have to be materialised explicitly, one (occurrence, site) pair
///    at a time — and only when the event has compliance enabled, because an
///    event without compliance has no red tasks to create in the first place.
///
/// HOW the deploy side is verified: <see cref="IEventDeployService"/> is
/// substituted, and the assertions are about the FAN-OUT (which occurrence, for
/// which site, how many times) rather than about the SDK rows the real deploy
/// writes. That is the actual contract of the service under test — it is an
/// orchestrator over an enumerator and a deploy call — and the real deploy's own
/// idempotence and row shape are already covered by EventDeployServiceTest. The
/// same split is used by WorkerTagAssignmentTest for the reconciliation engine.
///
/// The frequency tests exist because the user was explicit: "det skal
/// naturligvis gælde for alle frekvenser og ikke kun år". Every expected date
/// set below is built by a small independent loop in the test, never by calling
/// the enumerator under test.
///
/// All dates are relative to UtcNow. Absolute dates rot: a hard-coded "past"
/// date eventually stops being in the past, and a hard-coded future one stops
/// being in the future.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarPastSeriesBackfillTests : TestBaseSetup
{
    /// <summary>
    /// Snapshotted ONCE per test in [SetUp], never re-derived. As a computed
    /// property this read <c>DateTime.UtcNow.Date</c> afresh on every access —
    /// including once while seeding and again while building <c>expected</c>
    /// AFTER the service call — so any test that straddled 00:00 UTC seeded
    /// against one day and asserted against the next. Sharpest in
    /// <see cref="Backfill_Daily_EnumeratesEveryDayFromTheAnchorUpToButNotIncludingToday"/>,
    /// where seed and expectation are 20-odd lines apart.
    /// </summary>
    private DateTime _today;

    private DateTime Today => _today;

    /// <summary>
    /// AreaRulePlanningWorkerTags is newer than the backend-config snapshot SQL
    /// the base [SetUp] replays, so it is never dropped and its rows accumulate
    /// while AreaRulePlanning ids restart at 1 — a previous fixture's worker-tag
    /// link would otherwise attach to this test's event and add phantom
    /// recipients to every resolved site set. Same guard as
    /// WorkerTagAssignmentTest.
    /// </summary>
    [SetUp]
    public async Task ClearAccumulatingTables()
    {
        _today = DateTime.UtcNow.Date;

        await BackendConfigurationPnDbContext!.Database
            .ExecuteSqlRawAsync("DELETE FROM `AreaRulePlanningWorkerTags`;");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record Seeded(AreaRulePlanning Arp, Planning Planning);

    /// <summary>
    /// Seeds Area/Property/AreaRule/Planning/AreaRulePlanning for one calendar
    /// event. <paramref name="repeatType"/> is written to BOTH the planning
    /// (which the enumerator reads) and the arp, as the wizard does.
    /// </summary>
    private async Task<Seeded> SeedEvent(
        DateTime startDate,
        int repeatType,
        int repeatEvery = 1,
        bool complianceEnabled = true,
        int? dayOfMonth = null,
        int? repeatEndMode = null,
        DateTime? repeatUntilDate = null,
        int? repeatOccurrences = null,
        string? repeatWeekdaysCsv = null,
        int? repeatOrdinalWeek = null)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"BackfillTest-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true,
            RepeatEvery = repeatEvery,
            RepeatType = (RepeatType)repeatType,
            StartDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc),
            DayOfWeek = startDate.DayOfWeek,
            DayOfMonth = dayOfMonth,
            RepeatOrdinalWeek = repeatOrdinalWeek,
            RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id,
            StartDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc),
            Status = true,
            RepeatType = repeatType, RepeatEvery = repeatEvery,
            DayOfWeek = (int)startDate.DayOfWeek,
            DayOfMonth = dayOfMonth ?? 0,
            RepeatEndMode = repeatEndMode,
            RepeatUntilDate = repeatUntilDate,
            RepeatOccurrences = repeatOccurrences,
            RepeatWeekdaysCsv = repeatWeekdaysCsv,
            RepeatOrdinalWeek = repeatOrdinalWeek,
            ComplianceEnabled = complianceEnabled,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Seeded(arp, planning);
    }

    /// <summary>
    /// Adds an explicit recipient. Explicit PlanningSites need no SDK Site row —
    /// the resolver reads them straight out of the backend-config DB — so the
    /// site id can be arbitrary and unique per test.
    /// </summary>
    private async Task<int> AddSite(int arpId, int siteId)
    {
        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arpId, SiteId = siteId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return siteId;
    }

    private async Task<(CalendarPastSeriesBackfillService Service, IEventDeployService Deploy)> BuildService()
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var resolver = new CalendarAssignmentResolver(BackendConfigurationPnDbContext!, coreHelper);
        var deploy = Substitute.For<IEventDeployService>();


        // Default: every pair materialises a brand-new row. Tests that care
        // about the already-present branch override this.
        deploy.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => new EnsureComplianceResult { Created = true, ComplianceId = 1, SdkCaseId = 1 });

        var service = new CalendarPastSeriesBackfillService(
            ItemsPlanningPnDbContext!, BackendConfigurationPnDbContext!, coreHelper,
            deploy, resolver,
            NullLogger<CalendarPastSeriesBackfillService>.Instance);

        return (service, deploy);
    }

    private async Task<Planning> ReloadPlanning(int planningId) =>
        await ItemsPlanningPnDbContext!.Plannings.AsNoTracking().FirstAsync(x => x.Id == planningId);

    /// <summary>The deadlines the substituted deploy service was actually asked for, ascending.</summary>
    private static List<DateTime> DeployedDeadlines(IEventDeployService deploy) =>
        deploy.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IEventDeployService.EnsureComplianceForOccurrenceAsync))
            .Select(c => ((DateTime)c.GetArguments()[1]!).Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

    // ═════════════════════════════════════════════════════════════════════════
    // Scheduler neutralisation
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Backfill_PastAnchor_WritesBothSchedulerFieldsSoTheJobCannotBackDeploy()
    {
        // Weekly, anchored three weeks back: the past occurrences are -21, -14
        // and -7, and the next one lands exactly on today.
        var seeded = await SeedEvent(Today.AddDays(-21), repeatType: (int)RepeatType.Week);
        await AddSite(seeded.Arp.Id, 90101);

        var (service, _) = await BuildService();
        var result = await service.BackfillPastSeriesAsync(seeded.Arp);

        var planning = await ReloadPlanning(seeded.Planning.Id);

        Assert.Multiple(() =>
        {
            Assert.That(planning.NextExecutionTime, Is.Not.Null,
                "a null NextExecutionTime is exactly what SearchListJob treats as 'due now' — it must never be left null on a past-anchored planning");
            Assert.That(planning.NextExecutionTime!.Value.Date, Is.EqualTo(Today),
                "the next occurrence of a weekly series anchored 21 days back falls on today");
            Assert.That(planning.LastExecutedTime, Is.Not.Null,
                "ExecuteCleanUp re-arms NextExecutionTime = null for every planning whose LastExecutedTime is null, so writing only NextExecutionTime would be undone within the hour");
            Assert.That(result.PastOccurrences, Is.EqualTo(3));
            Assert.That(result.NextExecutionTime!.Value.Date, Is.EqualTo(Today),
                "the reported value must be the value written");
        });
    }

    [Test]
    public async Task Backfill_EndedSeries_ParksTheSchedulerOnTheSentinel()
    {
        // Daily from 10 days ago, but the rule stopped 3 days ago. There IS no
        // next occurrence, and "no next occurrence" must not degrade to null.
        var seeded = await SeedEvent(
            Today.AddDays(-10), repeatType: (int)RepeatType.Day,
            repeatEndMode: 2, repeatUntilDate: Today.AddDays(-3));
        await AddSite(seeded.Arp.Id, 90201);

        var (service, deploy) = await BuildService();
        var result = await service.BackfillPastSeriesAsync(seeded.Arp);

        var planning = await ReloadPlanning(seeded.Planning.Id);
        var deployed = DeployedDeadlines(deploy);

        Assert.Multiple(() =>
        {
            Assert.That(planning.NextExecutionTime!.Value.Date, Is.EqualTo(Today.AddYears(50)),
                "an ended series is parked 50 years out; null would make the hourly job pick it up forever");
            Assert.That(planning.LastExecutedTime, Is.Not.Null);
            Assert.That(result.PastOccurrences, Is.EqualTo(8),
                "day -10 through day -3 inclusive is 8 occurrences — RepeatUntilDate is inclusive");
            Assert.That(deployed.First(), Is.EqualTo(Today.AddDays(-10)));
            Assert.That(deployed.Last(), Is.EqualTo(Today.AddDays(-3)),
                "nothing after RepeatUntilDate may be backfilled");
        });
    }

    [Test]
    public async Task Backfill_NonRecurringPastTask_BackfillsItsSingleOccurrenceAndParksTheSentinel()
    {
        // RepeatType 0 == "no repeat". The recurrence signal is RepeatType, NOT
        // RepeatEvery — the calendar sends RepeatEvery = 1 even for a one-off,
        // so a RepeatEvery-based check would treat this as a daily series.
        var seeded = await SeedEvent(Today.AddDays(-5), repeatType: 0);
        await AddSite(seeded.Arp.Id, 90301);

        var (service, deploy) = await BuildService();
        var result = await service.BackfillPastSeriesAsync(seeded.Arp);

        var planning = await ReloadPlanning(seeded.Planning.Id);
        var deployed = DeployedDeadlines(deploy);

        Assert.Multiple(() =>
        {
            Assert.That(result.PastOccurrences, Is.EqualTo(1),
                "a one-off has exactly one occurrence — its own anchor");
            Assert.That(deployed, Is.EqualTo(new List<DateTime> { Today.AddDays(-5) }));
            Assert.That(planning.NextExecutionTime!.Value.Date, Is.EqualTo(Today.AddYears(50)),
                "a one-off in the past has no future occurrence");
            Assert.That(planning.LastExecutedTime, Is.Not.Null);
        });
    }

    [Test]
    public async Task Backfill_FutureAnchor_IsACompleteNoOp()
    {
        var seeded = await SeedEvent(Today.AddDays(7), repeatType: (int)RepeatType.Week);
        await AddSite(seeded.Arp.Id, 90401);

        var (service, deploy) = await BuildService();
        var result = await service.BackfillPastSeriesAsync(seeded.Arp);

        var planning = await ReloadPlanning(seeded.Planning.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.PastOccurrences, Is.EqualTo(0));
            Assert.That(result.NextExecutionTime, Is.Null);
            Assert.That(planning.NextExecutionTime, Is.Null,
                "a forward re-anchor leaves the scheduler's own state alone — the caller may invoke the backfill unconditionally");
            Assert.That(planning.LastExecutedTime, Is.Null);
            Assert.That(DeployedDeadlines(deploy), Is.Empty);
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Compliance ON / OFF
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Backfill_ComplianceEnabled_MaterialisesEveryPastOccurrenceForEverySite()
    {
        var seeded = await SeedEvent(Today.AddDays(-3), repeatType: (int)RepeatType.Day);
        var siteA = await AddSite(seeded.Arp.Id, 90501);
        var siteB = await AddSite(seeded.Arp.Id, 90502);

        var (service, deploy) = await BuildService();
        var result = await service.BackfillPastSeriesAsync(seeded.Arp);

        // 3 occurrences (-3, -2, -1) x 2 sites.
        foreach (var offset in new[] { -3, -2, -1 })
        {
            var day = Today.AddDays(offset);
            foreach (var site in new[] { siteA, siteB })
            {
                await deploy.Received(1).EnsureComplianceForOccurrenceAsync(
                    Arg.Is<AreaRulePlanning>(a => a.Id == seeded.Arp.Id),
                    Arg.Is<DateTime>(d => d.Date == day),
                    site,
                    Arg.Any<CancellationToken>());
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(result.PastOccurrences, Is.EqualTo(3));
            Assert.That(result.Created, Is.EqualTo(6), "one Compliance row per (occurrence, site) pair");
            Assert.That(result.Failed, Is.EqualTo(0));
            Assert.That(result.ComplianceSkipped, Is.False);
        });
    }

    [Test]
    public async Task Backfill_ComplianceDisabled_CreatesNothingButStillNeutralisesScheduler()
    {
        var seeded = await SeedEvent(
            Today.AddDays(-3), repeatType: (int)RepeatType.Day, complianceEnabled: false);
        await AddSite(seeded.Arp.Id, 90601);

        var (service, deploy) = await BuildService();
        var result = await service.BackfillPastSeriesAsync(seeded.Arp);

        var planning = await ReloadPlanning(seeded.Planning.Id);

        await deploy.DidNotReceive().EnsureComplianceForOccurrenceAsync(
            Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        Assert.Multiple(() =>
        {
            Assert.That(result.ComplianceSkipped, Is.True);
            Assert.That(result.Created, Is.EqualTo(0),
                "an event without compliance has no red tasks, so there is nothing to back-create");
            Assert.That(planning.NextExecutionTime, Is.Not.Null,
                "the scheduler filter does not read ComplianceEnabled, so neutralisation is required even here");
            Assert.That(planning.LastExecutedTime, Is.Not.Null);
        });
    }

    [Test]
    public async Task Backfill_NullResultFromDeploy_CountsAsFailureNotSuccess()
    {
        var seeded = await SeedEvent(Today.AddDays(-2), repeatType: (int)RepeatType.Day);
        await AddSite(seeded.Arp.Id, 90701);

        var (service, deploy) = await BuildService();
        // null == missing planning / SDK site / language / eformId <= 0. Counting
        // it as a success would report overdue rows that do not exist.
        deploy.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((EnsureComplianceResult?)null);

        var result = await service.BackfillPastSeriesAsync(seeded.Arp);

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.EqualTo(2));
            Assert.That(result.Created, Is.EqualTo(0));
            Assert.That(result.AlreadyPresent, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Backfill_RunTwice_CreatesOnceAndReportsTheRestAsAlreadyPresent()
    {
        var seeded = await SeedEvent(Today.AddDays(-4), repeatType: (int)RepeatType.Day);
        await AddSite(seeded.Arp.Id, 90801);

        var (service, deploy) = await BuildService();

        // Stand in for EnsureComplianceForOccurrenceAsync's real idempotence
        // guard: first ask for a (deadline, site) pair creates, every later ask
        // for the same pair returns the existing row with Created == false.
        var seen = new HashSet<(DateTime, int)>();
        deploy.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var key = (ci.ArgAt<DateTime>(1).Date, ci.ArgAt<int>(2));
                var isNew = seen.Add(key);
                return new EnsureComplianceResult { Created = isNew, ComplianceId = 1, SdkCaseId = 1 };
            });

        var first = await service.BackfillPastSeriesAsync(seeded.Arp);
        var second = await service.BackfillPastSeriesAsync(seeded.Arp);

        Assert.Multiple(() =>
        {
            Assert.That(first.Created, Is.EqualTo(4));
            Assert.That(first.AlreadyPresent, Is.EqualTo(0));
            Assert.That(second.Created, Is.EqualTo(0),
                "re-running a backfill over an already-filled range must not duplicate a single occurrence");
            Assert.That(second.AlreadyPresent, Is.EqualTo(4));
            Assert.That(second.Failed, Is.EqualTo(0),
                "a re-run is a supported recovery from a partially failed run, not an error");
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // "det skal naturligvis gælde for alle frekvenser" — Day / Week / Month / Year
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Backfill_Daily_EnumeratesEveryDayFromTheAnchorUpToButNotIncludingToday()
    {
        var seeded = await SeedEvent(Today.AddDays(-6), repeatType: (int)RepeatType.Day);
        await AddSite(seeded.Arp.Id, 90901);

        var (service, deploy) = await BuildService();
        await service.BackfillPastSeriesAsync(seeded.Arp);

        var expected = Enumerable.Range(1, 6).Select(i => Today.AddDays(-i)).OrderBy(d => d).ToList();

        Assert.That(DeployedDeadlines(deploy), Is.EqualTo(expected),
            "today's occurrence is NOT overdue — it is the scheduler's job, which is why NextExecutionTime is armed to it");
    }

    [Test]
    public async Task Backfill_Weekly_EnumeratesTheAnchorWeekdayOnly()
    {
        var seeded = await SeedEvent(Today.AddDays(-28), repeatType: (int)RepeatType.Week);
        await AddSite(seeded.Arp.Id, 91001);

        var (service, deploy) = await BuildService();
        await service.BackfillPastSeriesAsync(seeded.Arp);

        var expected = new[] { -28, -21, -14, -7 }.Select(i => Today.AddDays(i)).OrderBy(d => d).ToList();

        Assert.That(DeployedDeadlines(deploy), Is.EqualTo(expected));
    }

    [Test]
    public async Task Backfill_WeeklyMultiWeekdayCsv_EnumeratesEveryListedWeekday()
    {
        // Anchor on a Monday four weeks back, repeating Mon+Wed. The CSV is
        // JS-style (0 = Sunday), so Monday = 1 and Wednesday = 3.
        var anchor = Today.AddDays(-28);
        anchor = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7)); // back to that week's Monday
        var seeded = await SeedEvent(anchor, repeatType: (int)RepeatType.Week, repeatWeekdaysCsv: "1,3");
        await AddSite(seeded.Arp.Id, 91101);

        var (service, deploy) = await BuildService();
        await service.BackfillPastSeriesAsync(seeded.Arp);

        var expected = new List<DateTime>();
        for (var d = anchor; d < Today; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday)
            {
                expected.Add(d);
            }
        }

        Assert.That(DeployedDeadlines(deploy), Is.EqualTo(expected),
            "a multi-weekday rule must backfill BOTH weekdays of every past week, not one day per week");
    }

    [Test]
    public async Task Backfill_MonthlyDayOfMonth_EnumeratesTheSameDayInEveryPastMonth()
    {
        // Day 10 keeps every month's occurrence away from the 28/29/30/31 clamp.
        var firstOfThisMonth = new DateTime(Today.Year, Today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var anchor = firstOfThisMonth.AddMonths(-3).AddDays(9);

        var seeded = await SeedEvent(anchor, repeatType: (int)RepeatType.Month, dayOfMonth: 10);
        await AddSite(seeded.Arp.Id, 91201);

        var (service, deploy) = await BuildService();
        await service.BackfillPastSeriesAsync(seeded.Arp);

        var expected = new List<DateTime>();
        for (var m = -3; m <= 0; m++)
        {
            var candidate = firstOfThisMonth.AddMonths(m).AddDays(9);
            if (candidate < Today)
            {
                expected.Add(candidate);
            }
        }

        Assert.That(DeployedDeadlines(deploy), Is.EqualTo(expected));
    }

    [Test]
    public async Task Backfill_Yearly_ProducesTheUsersOwnExample_OneOverdueOccurrencePerPastYear()
    {
        // The verbatim request: a yearly task re-anchored to 01.01 of a past
        // year must produce a red task on each 01.01 that has already passed.
        // RepeatType 4 (Year) exists only on the backend-config side; the
        // items-planning enum stops at Month, hence the raw 4.
        var anchor = new DateTime(Today.Year - 3, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var seeded = await SeedEvent(anchor, repeatType: 4, dayOfMonth: 1);
        await AddSite(seeded.Arp.Id, 91301);

        var (service, deploy) = await BuildService();
        var result = await service.BackfillPastSeriesAsync(seeded.Arp);

        var expected = new List<DateTime>();
        for (var y = Today.Year - 3; y <= Today.Year; y++)
        {
            var candidate = new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            if (candidate < Today)
            {
                expected.Add(candidate);
            }
        }

        var planning = await ReloadPlanning(seeded.Planning.Id);

        // On any day but 1 January the next occurrence is next year's; on
        // 1 January itself it is today. Derived rather than hard-coded so the
        // suite does not fail once a year.
        var thisJanuaryFirst = new DateTime(Today.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var expectedNext = thisJanuaryFirst >= Today
            ? thisJanuaryFirst
            : new DateTime(Today.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Multiple(() =>
        {
            Assert.That(DeployedDeadlines(deploy), Is.EqualTo(expected));
            Assert.That(result.Created, Is.EqualTo(expected.Count));
            Assert.That(planning.NextExecutionTime!.Value.Date, Is.EqualTo(expectedNext),
                "the first 01.01 that is not already past is the next occurrence");
        });
    }

    [Test]
    public async Task Backfill_AfterNBound_CountsTheNOccurrencesFromThePostEditAnchor()
    {
        // A CONSCIOUS choice, locked in here so it cannot drift: "repeat N
        // times" is counted from the anchor the series has AFTER the edit. A
        // daily rule re-anchored 10 days back with N = 4 therefore ends 4 days
        // after that anchor — days -10..-7 — and has no future occurrence at all.
        // The alternative (counting from a superseded anchor) is not
        // representable: no original-anchor column exists, and it would make the
        // backfill disagree with the week renderer, which already counts this way.
        var seeded = await SeedEvent(
            Today.AddDays(-10), repeatType: (int)RepeatType.Day,
            repeatEndMode: 1, repeatOccurrences: 4);
        await AddSite(seeded.Arp.Id, 91401);

        var (service, deploy) = await BuildService();
        var result = await service.BackfillPastSeriesAsync(seeded.Arp);

        var planning = await ReloadPlanning(seeded.Planning.Id);
        var expected = new[] { -10, -9, -8, -7 }.Select(i => Today.AddDays(i)).OrderBy(d => d).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(DeployedDeadlines(deploy), Is.EqualTo(expected));
            Assert.That(result.PastOccurrences, Is.EqualTo(4));
            Assert.That(planning.NextExecutionTime!.Value.Date, Is.EqualTo(Today.AddYears(50)),
                "all N occurrences are already in the past, so the series has ended");
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Preview projection (#1122 §4's entry point)
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Plan_WithProspectiveStartDate_ProjectsWithoutWriting()
    {
        // The event currently starts today; the admin is considering moving it
        // back 5 days. The preview must count what the apply would create and
        // must not touch the scheduler.
        var seeded = await SeedEvent(Today, repeatType: (int)RepeatType.Day);
        await AddSite(seeded.Arp.Id, 91501);
        await AddSite(seeded.Arp.Id, 91502);

        var (service, deploy) = await BuildService();
        var plan = await service.PlanPastSeriesBackfillAsync(seeded.Arp, Today.AddDays(-5));

        var planning = await ReloadPlanning(seeded.Planning.Id);

        Assert.Multiple(() =>
        {
            Assert.That(plan.AnchorIsInThePast, Is.True);
            Assert.That(plan.PastOccurrences.Count, Is.EqualTo(5));
            Assert.That(plan.SiteIds.Count, Is.EqualTo(2));
            Assert.That(plan.OverdueToCreate, Is.EqualTo(10),
                "the preview number and the apply's Created count are the same arithmetic on the same enumeration");
            Assert.That(planning.NextExecutionTime, Is.Null, "a preview writes nothing");
            Assert.That(planning.StartDate.Date, Is.EqualTo(Today),
                "the prospective anchor must not leak back into the tracked entity");
        });

        await deploy.DidNotReceive().EnsureComplianceForOccurrenceAsync(
            Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Plan_ComplianceDisabled_ReportsZeroOverdueToCreate()
    {
        var seeded = await SeedEvent(
            Today.AddDays(-5), repeatType: (int)RepeatType.Day, complianceEnabled: false);
        await AddSite(seeded.Arp.Id, 91601);

        var (service, _) = await BuildService();
        var plan = await service.PlanPastSeriesBackfillAsync(seeded.Arp);

        Assert.Multiple(() =>
        {
            Assert.That(plan.PastOccurrences.Count, Is.EqualTo(5),
                "the occurrences still exist as calendar dates; they just do not become red tasks");
            Assert.That(plan.OverdueToCreate, Is.EqualTo(0));
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // §3 — the relocate-vs-retract period gate
    // ═════════════════════════════════════════════════════════════════════════
    //
    // IsSameRecurrencePeriod is pure, so these need no database. They exist
    // because the gate decides between a non-destructive relocation and a
    // retraction that pulls live cases off workers' devices — getting it
    // backwards in either direction is expensive.

    private static Planning PatternPlanning(int repeatType, DateTime startDate, int? dayOfMonth = null) =>
        new()
        {
            RepeatType = (RepeatType)repeatType,
            RepeatEvery = 1,
            StartDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc),
            DayOfMonth = dayOfMonth
        };

    private static AreaRulePlanning PatternArp(int repeatType, DateTime startDate) =>
        new()
        {
            RepeatType = repeatType,
            RepeatEvery = 1,
            DayOfWeek = (int)startDate.DayOfWeek,
            StartDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc)
        };

    [Test]
    public void IsSameRecurrencePeriod_WeeklyWithinTheSameWeek_IsSamePeriod()
    {
        // Tuesday -> Thursday of the SAME week: the period grid is untouched and
        // only the weekday moves, which is precisely what #960's relocation is for.
        var monday = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var tuesday = monday.AddDays(1);
        var thursday = monday.AddDays(3);

        var planning = PatternPlanning((int)RepeatType.Week, thursday);
        var arp = PatternArp((int)RepeatType.Week, thursday);

        Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, tuesday, thursday), Is.True);
    }

    [Test]
    public void IsSameRecurrencePeriod_WeeklyIntoAnotherWeek_IsNotSamePeriod()
    {
        var tuesday = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);
        var nextThursday = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc);

        var planning = PatternPlanning((int)RepeatType.Week, nextThursday);
        var arp = PatternArp((int)RepeatType.Week, nextThursday);

        Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, tuesday, nextThursday), Is.False,
            "once the week changes there is no 'own period' left to relocate the old occurrence within");
    }

    [Test]
    public void IsSameRecurrencePeriod_MonthlyWithinTheSameMonth_IsSamePeriod()
    {
        var from = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc);

        var planning = PatternPlanning((int)RepeatType.Month, to, dayOfMonth: 20);
        var arp = PatternArp((int)RepeatType.Month, to);

        Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, from, to), Is.True);
    }

    [Test]
    public void IsSameRecurrencePeriod_MonthlyIntoAnotherMonth_IsNotSamePeriod()
    {
        var from = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc);

        var planning = PatternPlanning((int)RepeatType.Month, to, dayOfMonth: 20);
        var arp = PatternArp((int)RepeatType.Month, to);

        Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, from, to), Is.False);
    }

    [Test]
    public void IsSameRecurrencePeriod_YearlyAcrossYears_IsNotSamePeriod()
    {
        // The user's own example: 25.08.2026 -> 01.01.2026 is same-year, but
        // 2026 -> 2025 is not, and only the year identifies a yearly period.
        var from = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);
        var sameYear = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var earlierYear = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var planning = PatternPlanning(4, sameYear, dayOfMonth: 1);
        var arp = PatternArp(4, sameYear);

        Assert.Multiple(() =>
        {
            Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, from, sameYear), Is.True,
                "a yearly period IS the calendar year, so 25.08.2026 and 01.01.2026 share one");
            Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, from, earlierYear), Is.False);
        });
    }

    /// <summary>
    /// The kinds with NO single per-period anchor answer <c>null</c> — "cannot
    /// be represented" — and never <c>false</c>.
    ///
    /// This is the difference between a no-op and a data loss. On `stable` the
    /// relocate path already handled the same null with
    /// <c>if (newDate == null) continue;</c>, i.e. it did nothing. Reporting the
    /// null as "different period" instead sent every daily rule, every
    /// multi-weekday weekly rule and every non-recurring task down the RETRACT
    /// branch on any ordinary date edit — CaseDeleting live cases off workers'
    /// devices from the plain edit modal, not just from the new batch action.
    /// The caller therefore tests <c>== false</c>, and null falls back to
    /// relocate.
    /// </summary>
    [Test]
    public void IsSameRecurrencePeriod_KindsWithoutAPerPeriodAnchor_AnswerNullNotFalse()
    {
        var from = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 3, 6, 0, 0, 0, DateTimeKind.Utc);

        var daily = PatternPlanning((int)RepeatType.Day, to);
        var dailyArp = PatternArp((int)RepeatType.Day, to);
        var oneOff = PatternPlanning(0, to);
        var oneOffArp = PatternArp(0, to);

        // A weekly rule listing more than one weekday has no single occurrence
        // per week either, so it maps to null for the same reason.
        var multiDayWeekly = PatternPlanning((int)RepeatType.Week, to);
        var multiDayWeeklyArp = PatternArp((int)RepeatType.Week, to);
        multiDayWeeklyArp.RepeatWeekdaysCsv = "1,3";

        Assert.Multiple(() =>
        {
            Assert.That(CalendarSvc.IsSameRecurrencePeriod(daily, dailyArp, from, to), Is.Null,
                "a daily rule has no per-period anchor — that is 'unknown', not 'different period'");
            Assert.That(CalendarSvc.IsSameRecurrencePeriod(oneOff, oneOffArp, from, to), Is.Null);
            Assert.That(
                CalendarSvc.IsSameRecurrencePeriod(multiDayWeekly, multiDayWeeklyArp, from, to), Is.Null,
                "a multi-weekday weekly rule emits several occurrences per week, so no single date represents the week");
            Assert.That(CalendarSvc.IsSameRecurrencePeriod(daily, dailyArp, from, from), Is.True,
                "a time-only edit never leaves the period");
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // repeatEvery > 1 — the skip arithmetic
    // ═════════════════════════════════════════════════════════════════════════
    //
    // Every other test in this fixture uses repeatEvery: 1, which makes the
    // enumerator's "is this period a multiple of repeatEvery from the anchor?"
    // arithmetic vacuously true. A backfill that ignored repeatEvery would
    // materialise 2-3x too many red tasks and would be invisible to all of them.

    [Test]
    public async Task Backfill_DailyEveryThreeDays_SkipsTheDaysInBetween()
    {
        var seeded = await SeedEvent(
            Today.AddDays(-9), repeatType: (int)RepeatType.Day, repeatEvery: 3);
        await AddSite(seeded.Arp.Id, 91701);

        var (service, deploy) = await BuildService();
        await service.BackfillPastSeriesAsync(seeded.Arp);

        var expected = new[] { -9, -6, -3 }.Select(i => Today.AddDays(i)).OrderBy(d => d).ToList();

        Assert.That(DeployedDeadlines(deploy), Is.EqualTo(expected),
            "every third day from the anchor, not every day");
    }

    [Test]
    public async Task Backfill_WeeklyEveryTwoWeeks_SkipsTheOddWeeks()
    {
        var seeded = await SeedEvent(
            Today.AddDays(-42), repeatType: (int)RepeatType.Week, repeatEvery: 2);
        await AddSite(seeded.Arp.Id, 91801);

        var (service, deploy) = await BuildService();
        await service.BackfillPastSeriesAsync(seeded.Arp);

        var expected = new[] { -42, -28, -14 }.Select(i => Today.AddDays(i)).OrderBy(d => d).ToList();

        Assert.That(DeployedDeadlines(deploy), Is.EqualTo(expected),
            "a fortnightly rule must skip the weeks in between");
    }

    [Test]
    public async Task Backfill_MonthlyEveryTwoMonths_SkipsTheOddMonths()
    {
        // Day 10 keeps every candidate away from the 28/29/30/31 clamp.
        var firstOfThisMonth = new DateTime(Today.Year, Today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var anchor = firstOfThisMonth.AddMonths(-4).AddDays(9);

        var seeded = await SeedEvent(
            anchor, repeatType: (int)RepeatType.Month, repeatEvery: 2, dayOfMonth: 10);
        await AddSite(seeded.Arp.Id, 91901);

        var (service, deploy) = await BuildService();
        await service.BackfillPastSeriesAsync(seeded.Arp);

        var expected = new List<DateTime>();
        for (var m = -4; m <= 0; m += 2)
        {
            var candidate = firstOfThisMonth.AddMonths(m).AddDays(9);
            if (candidate < Today)
            {
                expected.Add(candidate);
            }
        }

        Assert.That(DeployedDeadlines(deploy), Is.EqualTo(expected),
            "-4 and -2 months, never -3 or -1");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The monthly Nth-weekday branch
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// "Second Tuesday of every month" is a whole separate branch of the shared
    /// enumerator (RepeatOrdinalWeek + DayOfWeek instead of DayOfMonth) and no
    /// backfill test reached it before — SeedEvent had no way to express it, so
    /// the parameter is new too. A backfill that fell through to the
    /// day-of-month branch would put every red task on the anchor's day number
    /// rather than on the Nth weekday.
    /// </summary>
    [Test]
    public async Task Backfill_MonthlyNthWeekday_EnumeratesTheNthWeekdayOfEveryPastMonth()
    {
        const int ordinal = 2;             // 2nd
        const int targetDow = 2;           // Tuesday (.NET/JS style, Sun = 0)

        var firstOfThisMonth = new DateTime(Today.Year, Today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var anchorMonth = firstOfThisMonth.AddMonths(-3);
        var anchor = NthWeekday(anchorMonth.Year, anchorMonth.Month, ordinal, targetDow);

        var seeded = await SeedEvent(
            anchor, repeatType: (int)RepeatType.Month, repeatOrdinalWeek: ordinal);
        await AddSite(seeded.Arp.Id, 92001);

        var (service, deploy) = await BuildService();
        await service.BackfillPastSeriesAsync(seeded.Arp);

        var expected = new List<DateTime>();
        for (var m = -3; m <= 0; m++)
        {
            var month = firstOfThisMonth.AddMonths(m);
            var candidate = NthWeekday(month.Year, month.Month, ordinal, targetDow);
            if (candidate >= anchor && candidate < Today)
            {
                expected.Add(candidate);
            }
        }

        Assert.That(DeployedDeadlines(deploy), Is.EqualTo(expected),
            "the Nth-weekday branch must place each occurrence on the weekday, not on the anchor's day number");
    }

    /// <summary>
    /// The Nth <paramref name="dayOfWeek"/> of a month, computed independently
    /// of the enumerator under test.
    /// </summary>
    private static DateTime NthWeekday(int year, int month, int nth, int dayOfWeek)
    {
        var first = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var offset = (dayOfWeek - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + (nth - 1) * 7);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // OverdueToCreate must equal what the apply creates
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The preview promise. <c>OverdueToCreate</c> used to be a bare
    /// <c>PastOccurrences.Count * SiteIds.Count</c>, but the apply runs
    /// retract-then-backfill and the retraction PRESERVES answered occurrences
    /// (invariant R2); EnsureComplianceForOccurrenceAsync's site-aware
    /// idempotence guard then returns Created = false for every (deadline, site)
    /// those rows already cover. Preview said 10 where the apply created 7.
    ///
    /// Seeds one answered occurrence for one of two sites on one of five past
    /// days: the grid is 5 x 2 = 10 and exactly one pair is already covered.
    /// </summary>
    [Test]
    public async Task Plan_OverdueToCreate_ExcludesPairsAnAnsweredOccurrenceAlreadyCovers()
    {
        var seeded = await SeedEvent(Today.AddDays(-5), repeatType: (int)RepeatType.Day);
        var siteA = await SeedSdkSite();
        var siteB = await SeedSdkSite();
        await AddSite(seeded.Arp.Id, siteA);
        await AddSite(seeded.Arp.Id, siteB);

        // Answered (status 100) on day -3 for site A only.
        await SeedAnsweredOccurrence(seeded.Planning.Id, Today.AddDays(-3), siteA, status: 100);
        // NOT answered, day -2, site B — retracted before the backfill runs, so
        // it must NOT be subtracted.
        await SeedAnsweredOccurrence(seeded.Planning.Id, Today.AddDays(-2), siteB, status: 66);

        var (service, _) = await BuildService();
        var plan = await service.PlanPastSeriesBackfillAsync(seeded.Arp);

        Assert.Multiple(() =>
        {
            Assert.That(plan.PastOccurrences.Count, Is.EqualTo(5));
            Assert.That(plan.SiteIds.Count, Is.EqualTo(2));
            Assert.That(plan.AlreadyCovered, Is.EqualTo(1),
                "only the ANSWERED (deadline, site) pair survives the retraction and short-circuits the guard");
            Assert.That(plan.OverdueToCreate, Is.EqualTo(9),
                "5 days x 2 sites, minus the one pair the apply will find already present");
        });
    }

    /// <summary>Creates an SDK Site and returns its generated id.</summary>
    private async Task<int> SeedSdkSite()
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new SdkSite
        {
            Name = $"backfill-site-{Guid.NewGuid()}", MicrotingUid = null,
            LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    /// <summary>
    /// Seeds an SDK Case for <paramref name="sdkSiteId"/> plus the Compliance row
    /// that points at it — the exact shape
    /// EnsureComplianceForOccurrenceAsync's idempotence guard looks for.
    /// </summary>
    private async Task SeedAnsweredOccurrence(int planningId, DateTime deadline, int sdkSiteId, int status)
    {
        var sdkCase = new SdkCase
        {
            SiteId = sdkSiteId, Status = status, MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext!.Compliances.AddAsync(new BcCompliance
        {
            PlanningId = planningId,
            Deadline = DateTime.SpecifyKind(deadline.Date, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.Date.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }
}
