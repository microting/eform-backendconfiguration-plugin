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

using System.Globalization;
using eFormCore;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

/// <summary>
/// Regression coverage for the calendar orphan-render / occurrence-exception
/// fixes:
///
///   #930 — MoveTask thisAndFollowing (and UpdateTask thisAndFollowing) must NOT
///          create a CalendarOccurrenceException backfill anchor for a past date
///          that already has a Compliance row. A completed past occurrence is
///          rendered by the compliance loop, not resurrected as a false
///          uncompleted orphan tile. Genuinely-orphaned past dates (no
///          compliance) must still be anchored.
///
///   #928 — The orphan-render loop in GetTasksForWeek skips an exception whose
///          {planning.Id}:{OriginalDate:yyyy-MM-dd} is already in the in-week
///          compliance set, so it cannot stack a duplicate/unopenable tile over
///          a compliance-rendered one; and the exceptionsByArp inner dict
///          tolerates two non-removed exceptions for the same ARP+OriginalDate
///          (keeps highest Id) instead of throwing a duplicate-key exception.
///
/// Built on the SDK-core + SDK-Case seeding pattern from
/// <see cref="CalendarCompleteOccurrenceTests"/> because the compliance /
/// completion path reads the backing SDK Case (Status=100 completed,
/// &lt;100 open). The real mocked coreHelper is passed (NOT null) for that reason.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarOrphanRenderComplianceTests : TestBaseSetup
{
    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    // Fixed concrete Mondays (DateTime.Now is blocked in this codebase). These
    // are genuinely in the PAST relative to the test "today" so the
    // backfill/orphan logic engages.
    private static readonly DateTime SeriesStart = new(2026, 6, 1); // Monday
    private static readonly DateTime SecondMonday = new(2026, 6, 8); // Monday
    private static readonly DateTime ThirdMonday = new(2026, 6, 15); // Monday

    [SetUp]
    public async Task CleanCalendarTables()
    {
        // FK-safe cleanup so each test starts fresh (base [SetUp] already ran,
        // so the contexts are available here).
        BackendConfigurationPnDbContext!.CalendarOccurrenceExceptionSites.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptionSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptions);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.Compliances.RemoveRange(
            BackendConfigurationPnDbContext.Compliances);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRulePlannings.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlannings);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // AreaRuleTranslation -> AreaRule is DeleteBehavior.Restrict, so the
        // children must go first. The SQL dump used to truncate this table
        // before every test; it now replays once per fixture, so leftover
        // translations would block the delete below with MySQL 1451.
        BackendConfigurationPnDbContext.AreaRuleTranslations.RemoveRange(
            BackendConfigurationPnDbContext.AreaRuleTranslations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRules.RemoveRange(
            BackendConfigurationPnDbContext.AreaRules);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.Areas.RemoveRange(
            BackendConfigurationPnDbContext.Areas);
        BackendConfigurationPnDbContext.Properties.RemoveRange(
            BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext!.Plannings.RemoveRange(
            ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        MicrotingDbContext!.Cases.RemoveRange(MicrotingDbContext.Cases);
        await MicrotingDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds an SDK Site + Case with the given completion status (100 =
    /// completed, &lt;100 = open) and returns the Case Id.
    /// </summary>
    private async Task<int> SeedSdkCase(int status, int microtingUid)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var sdkSite = new Site
        {
            Name = $"orphan-render-test-site-{microtingUid}",
            MicrotingUid = microtingUid,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        var sdkCase = new Case
        {
            SiteId = sdkSite.Id,
            Status = status,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        return sdkCase.Id;
    }

    /// <summary>
    /// Seeds Area→Property→AreaRule→Planning→AreaRulePlanning(weekly Monday)→
    /// CalendarConfiguration. Returns (arpId, propertyId, planningId, areaId).
    /// </summary>
    private async Task<(int ArpId, int PropertyId, int PlanningId, int AreaId)> SeedWeeklyMondaySeries(
        DateTime startDate)
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
            Name = $"OrphanRenderTest-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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

        var areaRuleTranslation = new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = "Orphan Render Title",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(areaRuleTranslation);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
            DayOfWeek = DayOfWeek.Monday, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Weekly Monday series: RepeatType=2 (weeks), RepeatWeekdaysCsv="1",
        // DayOfWeek=1 (Monday).
        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id,
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc), Status = true,
            RepeatType = 2, RepeatEvery = 1, RepeatWeekdaysCsv = "1", DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(calConfig);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (arp.Id, property.Id, planning.Id, area.Id);
    }

    private BackendConfigurationCalendarService BuildService(Core core)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(
            new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));

        // The completion / compliance path reads the backing SDK Case, so the
        // real mocked coreHelper must be wired (NOT null).
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        taskWizardService.DeleteTask(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(true)));

        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            // #1122 — the calendar service now delegates the retract/backfill
            // halves of a cross-period re-anchor. Neither fires in these fixtures.
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>());
    }

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------
    // T1 — #930: thisAndFollowing move does NOT resurrect a completed past
    //      occurrence that already has a Compliance row.
    // ------------------------------------------------------------------

    [Test]
    public async Task MoveTask_ThisAndFollowing_DoesNotResurrectCompletedPastOccurrence()
    {
        var core = await GetCore();
        var (arpId, propertyId, planningId, areaId) = await SeedWeeklyMondaySeries(SeriesStart);

        // Completed backing case for the FIRST occurrence (2026-06-01).
        var completedCaseId = await SeedSdkCase(status: 100, microtingUid: 5601);
        var compliance = new Compliance
        {
            PlanningId = planningId, PropertyId = propertyId, AreaId = areaId,
            Deadline = DateTime.SpecifyKind(SeriesStart, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(SeriesStart.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = completedCaseId, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Removed
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var service = BuildService(core);

        // thisAndFollowing move on a later occurrence (2026-06-15 → 2026-06-16).
        var moveResult = await service.MoveTask(new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            OriginalDate = ThirdMonday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewDate = ThirdMonday.AddDays(1).ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 10.0,
            Scope = "thisAndFollowing"
        });
        Assert.That(moveResult.Success, Is.True, moveResult.Message);

        // (a) No backfill anchor for the completed past date.
        var anchorForCompletedDate = await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.OriginalDate.Date == SeriesStart.Date)
            .ToListAsync();
        Assert.That(anchorForCompletedDate, Is.Empty,
            "the completed past occurrence must not be anchored as a false orphan (#930)");

        // (b) The week of 2026-06-01 renders that date exactly once, from the
        //     compliance loop, as completed.
        var result = await service.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = IsoUtc(SeriesStart),
            WeekEnd = IsoUtc(SeriesStart.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [], TagNames = [], SiteIds = []
        });
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.Not.Null);

        var tilesOnStart = result.Model.Where(t => t.TaskDate == Key(SeriesStart)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(tilesOnStart, Has.Count.EqualTo(1),
                "completed past date must render exactly once");
            Assert.That(tilesOnStart[0].IsFromCompliance, Is.True,
                "the single tile must be the compliance row");
            Assert.That(tilesOnStart[0].Completed, Is.True,
                "the compliance tile must show as completed");
        });
    }

    // ------------------------------------------------------------------
    // T2 — #930: a past occurrence WITHOUT a compliance still gets a backfill
    //      anchor (guards that the compliance-skip didn't over-suppress).
    // ------------------------------------------------------------------

    [Test]
    public async Task MoveTask_ThisAndFollowing_StillAnchorsPastOccurrenceWithoutCompliance()
    {
        var core = await GetCore();
        var (arpId, _, _, _) = await SeedWeeklyMondaySeries(SeriesStart);

        // NO compliance seeded for 2026-06-08 (uncompleted past occurrence).
        var service = BuildService(core);

        var moveResult = await service.MoveTask(new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            OriginalDate = ThirdMonday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewDate = ThirdMonday.AddDays(1).ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 10.0,
            Scope = "thisAndFollowing"
        });
        Assert.That(moveResult.Success, Is.True, moveResult.Message);

        var anchorForSecondMonday = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .Where(x => x.AreaRulePlanningId == arpId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.OriginalDate.Date == SecondMonday.Date)
            .ToListAsync();

        Assert.That(anchorForSecondMonday, Has.Count.EqualTo(1),
            "a genuinely-orphaned past date (no compliance) must still be anchored (#930)");
    }

    // ------------------------------------------------------------------
    // T3 — #928: the orphan loop does not stack a tile over a
    //      compliance-covered in-week date.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetTasksForWeek_DoesNotStackOrphanTileOverComplianceDate()
    {
        var core = await GetCore();
        var (arpId, propertyId, planningId, areaId) = await SeedWeeklyMondaySeries(SeriesStart);

        // Wednesday of the series' first week: an in-week date that the weekly-
        // Monday rule does NOT generate, so the orphan reaches the new
        // compliance-skip guard instead of being pre-empted by the recurrence
        // (renderedDates) guard.
        var dateX = SeriesStart.AddDays(2);

        // Open (Status<100) backing case for the in-week compliance.
        var openCaseId = await SeedSdkCase(status: 50, microtingUid: 5602);
        var compliance = new Compliance
        {
            PlanningId = planningId, PropertyId = propertyId, AreaId = areaId,
            Deadline = DateTime.SpecifyKind(dateX, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(dateX.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = openCaseId, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Pre-existing backfill orphan anchor for the SAME date X.
        var orphanAnchor = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = DateTime.SpecifyKind(dateX, DateTimeKind.Utc),
            NewDate = null,
            StartHour = 9.0,
            Duration = 1.0,
            IsDeleted = false,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.AddAsync(orphanAnchor);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var service = BuildService(core);

        var result = await service.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = IsoUtc(SeriesStart),
            WeekEnd = IsoUtc(SeriesStart.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [], TagNames = [], SiteIds = []
        });
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.Not.Null);

        var tilesOnX = result.Model.Where(t => t.TaskDate == Key(dateX)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(tilesOnX, Has.Count.EqualTo(1),
                "date X must render exactly once — the orphan anchor must not stack (#928)");
            Assert.That(tilesOnX[0].IsFromCompliance, Is.True,
                "the surviving tile must be the compliance row, not the orphan anchor");
        });
    }

    // Note: a unique constraint on CalendarOccurrenceException(AreaRulePlanningId,
    // OriginalDate) makes two non-removed same-date exceptions for one ARP
    // impossible at the DB level, so the exceptionsByArp keying is collision-free
    // by construction — no dedup test is needed.
}
