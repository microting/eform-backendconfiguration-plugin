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
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using BcCompliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;
using SdkSite = Microting.eForm.Infrastructure.Data.Entities.Site;

/// <summary>
/// #1122 §3 — the relocate-vs-retract gate inside
/// <c>BackendConfigurationCalendarService.UpdateTask</c>, driven through the
/// REAL <see cref="CalendarOccurrenceRetractionService"/> and
/// <see cref="CalendarPastSeriesBackfillService"/>.
///
/// WHY A WHOLE FIXTURE. Every one of the 25 pre-existing calendar fixtures
/// substitutes both of those services, and TaskListBatchStartDateTest
/// substitutes the calendar service itself. The consequence is that the most
/// destructive code the #1122 change introduced — a branch that CaseDeletes live
/// cases off workers' devices — could be inverted, or have its bound removed,
/// without failing a single test. This fixture is the coverage that makes the
/// branch choice observable: each test asserts WHICH branch ran, by looking at
/// what happened to seeded Compliance rows and at whether the deploy service was
/// asked to materialise anything.
///
/// The three branch outcomes, one test each:
///   * same period, future anchor  -> RELOCATE (deadlines move, nothing is pulled)
///   * different period, future    -> RETRACT from today forward, history intact
///   * anchor in the past          -> RETRACT from the anchor forward, then BACKFILL
///
/// Plus the two ways the gate must NOT fire: a rule kind whose recurrence period
/// has no single representative date (daily, multi-weekday weekly) answers
/// "unknown", and unknown must fall back to relocate — which for those kinds is
/// the documented no-op it has always been. Reporting unknown as "different
/// period" turned that no-op into a mass CaseDelete reachable from the ordinary
/// single-task edit modal.
///
/// The task wizard is substituted but NOT inert: it performs the two anchor
/// writes the real one performs, because the gate and the backfill are both
/// meaningless if the anchor never moves.
///
/// Every SDK case is seeded with MicrotingUid = null so <c>core.CaseDelete</c> is
/// skipped (there is no cloud in CI) while all the local bookkeeping still runs —
/// same trick as CalendarOccurrenceRetractionTests. Rows accumulate across the
/// tests in this fixture, so every assertion is scoped to seeded ids.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarUpdateTaskRetractGateTests : TestBaseSetup
{
    /// <summary>Snapshotted once per test — never re-derive it mid-test.</summary>
    private DateTime _today;

    private DateTime Today => _today;

    private IUserService _userService = null!;
    private IEFormCoreService _coreHelper = null!;
    private IBackendConfigurationTaskWizardService _taskWizardService = null!;
    private IEventDeployService _deployService = null!;
    private CalendarOccurrenceRetractionService _retractionService = null!;
    private CalendarPastSeriesBackfillService _backfillService = null!;
    private BackendConfigurationCalendarService _service = null!;

    [SetUp]
    public async Task SetupGateFixture()
    {
        _today = DateTime.UtcNow.Date;

        // AreaRulePlanningWorkerTags is newer than the backend-config snapshot
        // SQL the base [SetUp] replays, so it is never dropped and its rows
        // accumulate while AreaRulePlanning ids restart low — a previous
        // fixture's link would otherwise add phantom recipients to the resolved
        // site set. Same guard as CalendarPastSeriesBackfillTests.
        await BackendConfigurationPnDbContext!.Database
            .ExecuteSqlRawAsync("DELETE FROM `AreaRulePlanningWorkerTags`;");

        var language = await MicrotingDbContext!.Languages.FirstAsync();

        _userService = Substitute.For<IUserService>();
        _userService.UserId.Returns(1);
        _userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));

        var core = await GetCore();
        _coreHelper = Substitute.For<IEFormCoreService>();
        _coreHelper.GetCore().Returns(Task.FromResult(core));

        _deployService = Substitute.For<IEventDeployService>();
        _deployService.EnsureComplianceForOccurrenceAsync(
                Arg.Any<AreaRulePlanning>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => new EnsureComplianceResult { Created = true, ComplianceId = 1, SdkCaseId = 1 });

        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        ConfigureWizardToPersistTheAnchor();

        _retractionService = new CalendarOccurrenceRetractionService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, _coreHelper,
            NullLogger<CalendarOccurrenceRetractionService>.Instance);

        _backfillService = new CalendarPastSeriesBackfillService(
            ItemsPlanningPnDbContext!, BackendConfigurationPnDbContext!, _coreHelper,
            _deployService,
            new CalendarAssignmentResolver(BackendConfigurationPnDbContext!, _coreHelper),
            NullLogger<CalendarPastSeriesBackfillService>.Instance);

        _service = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            _userService,
            BackendConfigurationPnDbContext!,
            _coreHelper,
            _deployService,
            ItemsPlanningPnDbContext!,
            _taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            _retractionService,
            _backfillService);
    }

    /// <summary>
    /// The substituted wizard does the two anchor writes the real one does
    /// (BackendConfigurationTaskWizardService.UpdateTask:821 and :1105). Without
    /// them planning.StartDate keeps the PRE-edit value and the backfill — which
    /// reads planning.StartDate by contract, never a request model — has nothing
    /// to enumerate.
    /// </summary>
    private void ConfigureWizardToPersistTheAnchor() =>
        _taskWizardService.UpdateTask(Arg.Any<TaskWizardCreateModel>())
            .Returns(ci =>
            {
                var model = ci.Arg<TaskWizardCreateModel>();

                var arp = BackendConfigurationPnDbContext!.AreaRulePlannings
                    .First(x => x.Id == model.Id);
                arp.StartDate = model.StartDate;
                BackendConfigurationPnDbContext.SaveChanges();

                var planning = ItemsPlanningPnDbContext!.Plannings
                    .First(x => x.Id == arp.ItemPlanningId);
                var anchor = arp.StartDate!.Value;
                planning.StartDate = new DateTime(
                    anchor.Year, anchor.Month, anchor.Day, 0, 0, 0, DateTimeKind.Utc);
                planning.DayOfMonth = BackendConfigurationTaskWizardService
                    .DeriveDayOfMonth(model.RepeatType, planning.StartDate);
                planning.DayOfWeek = planning.StartDate.DayOfWeek;
                planning.RepeatType = (RepeatType)(int)model.RepeatType;
                planning.RepeatEvery = model.RepeatEvery;
                ItemsPlanningPnDbContext.SaveChanges();

                return Task.FromResult(new OperationResult(true));
            });

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record Seeded(int ArpId, int PlanningId, int PropertyId, int SdkSiteId);

    private async Task<Seeded> SeedEvent(
        DateTime anchor,
        int repeatType,
        bool complianceEnabled = true,
        string? repeatWeekdaysCsv = null)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var sdkSite = new SdkSite
        {
            Name = $"gate-site-{Guid.NewGuid()}", MicrotingUid = null,
            LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"GateTest-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 0, CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var utcAnchor = DateTime.SpecifyKind(anchor.Date, DateTimeKind.Utc);

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = (RepeatType)repeatType,
            StartDate = utcAnchor, DayOfWeek = utcAnchor.DayOfWeek, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = utcAnchor, Status = true,
            RepeatType = repeatType, RepeatEvery = 1, DayOfWeek = (int)utcAnchor.DayOfWeek,
            RepeatWeekdaysCsv = repeatWeekdaysCsv, ComplianceEnabled = complianceEnabled,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Seeded(arp.Id, planning.Id, property.Id, sdkSite.Id);
    }

    /// <summary>
    /// Seeds one deployed occurrence (SDK Case + PlanningCase + PlanningCaseSite
    /// + Compliance) and returns the Compliance id.
    /// </summary>
    private async Task<int> SeedDeployedOccurrence(
        Seeded seeded, DateTime deadline, int status)
    {
        var sdkCase = new SdkCase
        {
            SiteId = seeded.SdkSiteId, Status = status, MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var planningCase = new PlanningCase
        {
            PlanningId = seeded.PlanningId, Status = status, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(new PlanningCaseSite
        {
            PlanningId = seeded.PlanningId, PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = seeded.SdkSiteId, MicrotingSdkeFormId = 0,
            MicrotingSdkCaseId = sdkCase.Id, Status = status,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var compliance = new BcCompliance
        {
            PlanningId = seeded.PlanningId, PropertyId = seeded.PropertyId,
            Deadline = DateTime.SpecifyKind(deadline.Date, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.Date.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance.Id;
    }

    private async Task<BcCompliance> ReloadCompliance(int id) =>
        await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == id);

    /// <summary>The deadlines the substituted deploy service was asked for, ascending.</summary>
    private List<DateTime> BackfilledDeadlines() =>
        _deployService.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name
                        == nameof(IEventDeployService.EnsureComplianceForOccurrenceAsync))
            .Select(c => ((DateTime)c.GetArguments()[1]!).Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

    private CalendarTaskUpdateRequestModel BuildUpdate(
        Seeded seeded, DateTime startDate, int repeatType,
        string? originalDate = null, string? repeatWeekdaysCsv = null) =>
        new()
        {
            Id = seeded.ArpId,
            Scope = "all",
            OriginalDate = originalDate,
            StartDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc),
            StartHour = 9.0,
            Duration = 1.0,
            Status = 1,
            RepeatType = repeatType,
            RepeatEvery = 1,
            RepeatWeekdaysCsv = repeatWeekdaysCsv,
            ComplianceEnabled = true,
            PropertyId = seeded.PropertyId,
            EformId = 0,
            Sites = [seeded.SdkSiteId],
            TagIds = [],
            Translates = []
        };

    /// <summary>Monday of the first full week that starts after today.</summary>
    private DateTime NextMonday()
    {
        var candidate = Today.AddDays(1);
        while (candidate.DayOfWeek != DayOfWeek.Monday)
        {
            candidate = candidate.AddDays(1);
        }
        return candidate;
    }

    private const int OpenStatus = 66;
    private const int CompletedStatus = 100;

    // ═════════════════════════════════════════════════════════════════════════
    // Branch 1 — same period, future anchor -> RELOCATE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Moving a weekly task from Monday to Wednesday of the SAME week is the
    /// #960 relocation, and must stay it: the period grid does not move, so the
    /// deployed-but-open occurrence keeps its case and only its Deadline shifts
    /// onto the new weekday. Nothing may be pulled off the device.
    /// </summary>
    [Test]
    public async Task UpdateTask_SamePeriodFutureAnchor_RelocatesAndRetractsNothing()
    {
        var monday = NextMonday();
        var wednesday = monday.AddDays(2);

        var seeded = await SeedEvent(monday, repeatType: (int)RepeatType.Week);
        var complianceId = await SeedDeployedOccurrence(seeded, monday, OpenStatus);

        var result = await _service.UpdateTask(BuildUpdate(
            seeded, wednesday, (int)RepeatType.Week,
            originalDate: monday.ToString("yyyy-MM-ddTHH:mm:ssZ")));

        Assert.That(result.Success, Is.True, result.Message);

        var compliance = await ReloadCompliance(complianceId);

        Assert.Multiple(() =>
        {
            Assert.That(compliance.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "a same-period move relocates; it must never retract the occurrence");
            Assert.That(compliance.Deadline.Date, Is.EqualTo(wednesday),
                "the open occurrence adopts the new weekday within its own week (#960)");
            Assert.That(BackfilledDeadlines(), Is.Empty,
                "a future anchor has no past range, so nothing is materialised");
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Branch 2 — different period, future anchor -> RETRACT, history intact
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pushing a weekly series four weeks out leaves its own week, so the open
    /// occurrences the old pattern deployed are retracted — but ONLY from today
    /// forward.
    ///
    /// The overdue row is the whole point. On a future anchor the backfill
    /// self-gates OFF, so anything retracted below the bound is gone for good:
    /// an unbounded retraction here silently destroys every unanswered overdue
    /// occurrence the series had accumulated, which is the record that the work
    /// was never done.
    /// </summary>
    [Test]
    public async Task UpdateTask_DifferentPeriodFutureAnchor_RetractsForwardButKeepsOverdueHistory()
    {
        var seeded = await SeedEvent(Today.AddDays(-28), repeatType: (int)RepeatType.Week);

        var overdueOpenId = await SeedDeployedOccurrence(seeded, Today.AddDays(-7), OpenStatus);
        var futureOpenId = await SeedDeployedOccurrence(seeded, Today.AddDays(7), OpenStatus);
        var futureDoneId = await SeedDeployedOccurrence(seeded, Today.AddDays(14), CompletedStatus);

        // No OriginalDate — the batch re-anchor shape, so the gate compares the
        // series' own previous anchor against the new one.
        var result = await _service.UpdateTask(BuildUpdate(
            seeded, Today.AddDays(28), (int)RepeatType.Week));

        Assert.That(result.Success, Is.True, result.Message);

        var overdueOpen = await ReloadCompliance(overdueOpenId);
        var futureOpen = await ReloadCompliance(futureOpenId);
        var futureDone = await ReloadCompliance(futureDoneId);
        var planning = await ItemsPlanningPnDbContext!.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == seeded.PlanningId);

        Assert.Multiple(() =>
        {
            Assert.That(overdueOpen.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "an unanswered OVERDUE occurrence is history the new pattern never re-creates — retracting it destroys the record");
            Assert.That(futureOpen.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "a still-open occurrence on a date the new pattern no longer generates must be pulled");
            Assert.That(futureDone.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "a completed occurrence is immutable (R2)");
            Assert.That(BackfilledDeadlines(), Is.Empty,
                "the anchor is in the future, so the backfill is a complete no-op");
            Assert.That(planning.NextExecutionTime, Is.Null,
                "and it writes no scheduler state either");
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Branch 3 — past anchor -> RETRACT from the anchor, then BACKFILL
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The #1122 headline path. Re-anchoring into the past retracts everything
    /// from the NEW anchor forward (the backfill is about to rebuild exactly that
    /// range, and leftovers would collide with it on the very days it fills) and
    /// leaves everything before it alone — the "2026-01-01 -> 2026-06-01 must not
    /// destroy January to May" case.
    /// </summary>
    [Test]
    public async Task UpdateTask_PastAnchor_RetractsFromTheAnchorAndBackfillsTheRange()
    {
        var seeded = await SeedEvent(Today.AddDays(-28), repeatType: (int)RepeatType.Week);

        var beforeAnchorId = await SeedDeployedOccurrence(seeded, Today.AddDays(-21), OpenStatus);
        var insideRangeId = await SeedDeployedOccurrence(seeded, Today.AddDays(-7), OpenStatus);
        var completedId = await SeedDeployedOccurrence(seeded, Today.AddDays(7), CompletedStatus);

        var newAnchor = Today.AddDays(-14);
        var result = await _service.UpdateTask(BuildUpdate(
            seeded, newAnchor, (int)RepeatType.Week));

        Assert.That(result.Success, Is.True, result.Message);

        var beforeAnchor = await ReloadCompliance(beforeAnchorId);
        var insideRange = await ReloadCompliance(insideRangeId);
        var completed = await ReloadCompliance(completedId);
        var planning = await ItemsPlanningPnDbContext!.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == seeded.PlanningId);

        var expectedBackfill = new List<DateTime> { newAnchor, newAnchor.AddDays(7) };

        Assert.Multiple(() =>
        {
            Assert.That(beforeAnchor.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "an occurrence BEFORE the new anchor is outside the range the new pattern owns and is never re-created — it must survive");
            Assert.That(insideRange.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "an open occurrence inside the rebuilt range is retracted so the backfill can re-create it from the new pattern");
            Assert.That(completed.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "a completed occurrence is immutable (R2), inside the range or not");
            Assert.That(BackfilledDeadlines(), Is.EqualTo(expectedBackfill),
                "every weekly occurrence in [anchor, today) is materialised as overdue");
            Assert.That(planning.NextExecutionTime!.Value.Date, Is.EqualTo(Today),
                "the scheduler is re-armed to the first occurrence that is not overdue");
            Assert.That(planning.LastExecutedTime, Is.Not.Null,
                "ExecuteCleanUp re-arms NextExecutionTime = null whenever LastExecutedTime is null");
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The gate must NOT fire when the period is unrepresentable
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// THE regression test for the inverted default. A DAILY rule has no single
    /// per-period anchor, so <c>NewPatternDateForPeriodOf</c> answers null. On
    /// `stable` the relocate path swallowed that null with
    /// <c>if (newDate == null) continue;</c> — a complete no-op. Collapsing the
    /// null to "different period" instead routed every daily rule down the
    /// RETRACT branch on any ordinary date edit, CaseDeleting live cases from the
    /// plain single-task edit modal. The gate must stay conservative: unknown
    /// means relocate, and relocate no-ops.
    /// </summary>
    [Test]
    public async Task UpdateTask_DailyRule_FutureDateChange_DoesNotRetract()
    {
        var anchor = Today.AddDays(3);
        var seeded = await SeedEvent(anchor, repeatType: (int)RepeatType.Day);
        var complianceId = await SeedDeployedOccurrence(seeded, anchor.AddDays(1), OpenStatus);

        var result = await _service.UpdateTask(BuildUpdate(
            seeded, anchor.AddDays(4), (int)RepeatType.Day,
            originalDate: anchor.ToString("yyyy-MM-ddTHH:mm:ssZ")));

        Assert.That(result.Success, Is.True, result.Message);

        var compliance = await ReloadCompliance(complianceId);

        Assert.Multiple(() =>
        {
            Assert.That(compliance.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "a daily rule's period is unrepresentable — 'unknown' must fall back to relocate, never to retract");
            Assert.That(compliance.Deadline.Date, Is.EqualTo(anchor.AddDays(1)),
                "and relocate is a documented no-op for this kind, so the deadline is untouched too");
        });
    }

    /// <summary>
    /// The second source of the same null: a weekly rule listing more than one
    /// weekday emits several occurrences per week, so no single date represents
    /// the week. Identical requirement, different branch of
    /// <c>NewPatternDateForPeriodOf</c>.
    /// </summary>
    [Test]
    public async Task UpdateTask_MultiWeekdayWeeklyRule_FutureDateChange_DoesNotRetract()
    {
        var monday = NextMonday();
        var seeded = await SeedEvent(
            monday, repeatType: (int)RepeatType.Week, repeatWeekdaysCsv: "1,3");
        var complianceId = await SeedDeployedOccurrence(seeded, monday.AddDays(2), OpenStatus);

        // Into ANOTHER week, which for a single-weekday rule would be a definite
        // period change — here it is unknown, so it must still relocate.
        var result = await _service.UpdateTask(BuildUpdate(
            seeded, monday.AddDays(14), (int)RepeatType.Week,
            originalDate: monday.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            repeatWeekdaysCsv: "1,3"));

        Assert.That(result.Success, Is.True, result.Message);

        var compliance = await ReloadCompliance(complianceId);

        Assert.Multiple(() =>
        {
            Assert.That(compliance.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "a multi-weekday weekly rule has no single per-period anchor — unknown, so relocate");
            Assert.That(compliance.Deadline.Date, Is.EqualTo(monday.AddDays(2)));
        });
    }
}
