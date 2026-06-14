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
/// #954 — render-level regression coverage.
///
/// A recurring calendar task with a DEPLOYED-but-not-yet-done Compliance row
/// (the row a device worker creates when opening the task:
/// <c>MicrotingSdkCaseId &gt; 0</c> with a backing SDK Case whose
/// <c>Status != 100</c>) used to render TWO tiles after a series-level move
/// ("all" / "thisAndFollowing"): one at the old <c>Compliance.Deadline</c> and
/// one at the new recurrence anchor.
///
/// The fix in <c>BackendConfigurationCalendarService.MoveTask</c> shifts the
/// dragged occurrence's open Compliance row's <c>Deadline</c> to the new date so
/// the recurrence-dedup gate (<c>complianceDateSet</c>, keyed
/// <c>PlanningId:Deadline</c>) suppresses the duplicate recurrence tile — leaving
/// a single tile at the new date.
///
/// These tests exercise the fix at the RENDER level via
/// <see cref="BackendConfigurationCalendarService.GetTasksForWeek"/>, which is
/// why a REAL SDK core (and seeded SDK Cases) is required: the move guard, the
/// completion read, and the compliance-emit loop all consult the backing SDK
/// Case. A <c>MicrotingSdkCaseId == 0</c> row is NOT emitted as a tile and NOT
/// in the dedup set (Defect E / #935), so it would prove nothing here — every
/// Compliance row below is deployed (<c>MicrotingSdkCaseId &gt; 0</c>).
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarComplianceMoveTests : TestBaseSetup
{
    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime GetNextMonday()
    {
        var today = DateTime.UtcNow.Date;
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7; // always strictly future
        return today.AddDays(daysUntilMonday);
    }

    [SetUp]
    public async Task CleanCalendarTables()
    {
        // FK-safe cleanup so each test starts fresh (base [SetUp] already ran).
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
    /// completed, &lt;100 = open / deployed-not-done) and returns the Case Id.
    /// Required non-null Case fields: SiteId (FK to a real Site), Status,
    /// WorkflowState. Id is identity — captured from the generated row.
    /// </summary>
    private async Task<int> SeedSdkCase(int status, int microtingUid)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var sdkSite = new Site
        {
            Name = $"compliance-move-test-site-{microtingUid}",
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
    /// Seeds Area→Property→AreaRule(+translation)→Planning→AreaRulePlanning
    /// (weekly Monday)→CalendarConfiguration. Returns
    /// (arpId, propertyId, planningId, areaId).
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
            Name = $"ComplianceMoveTest-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = "Compliance Move Title",
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

        // Weekly Monday series: RepeatType=2 (weeks), RepeatWeekdaysCsv="1", DayOfWeek=1.
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

    /// <summary>
    /// Seeds a DEPLOYED Compliance row (MicrotingSdkCaseId &gt; 0) at the given
    /// deadline. Returns the Compliance Id.
    /// </summary>
    private async Task<int> SeedDeployedCompliance(int planningId, int propertyId, int areaId,
        DateTime deadline, int sdkCaseId)
    {
        var compliance = new Compliance
        {
            PlanningId = planningId,
            PropertyId = propertyId,
            AreaId = areaId,
            Deadline = DateTime.SpecifyKind(deadline, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCaseId,
            MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance.Id;
    }

    private BackendConfigurationCalendarService BuildService(Core core)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(
            new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));

        // The move guard, completion read and compliance-emit loop all read the
        // backing SDK Case, so the real core must be wired through coreHelper.
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
            NullLogger<BackendConfigurationCalendarService>.Instance);
    }

    private async Task<List<CalendarTaskResponseModel>> TilesForWeek(
        BackendConfigurationCalendarService service, int propertyId, DateTime weekMonday, int planningId)
    {
        var result = await service.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = IsoUtc(weekMonday),
            WeekEnd = IsoUtc(weekMonday.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [], TagNames = [], SiteIds = []
        });
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.Not.Null);
        return result.Model.Where(t => t.PlanningId == planningId).ToList();
    }

    // ------------------------------------------------------------------
    // (a) #954 — render-level: deployed-open compliance renders a SINGLE
    //     tile at the NEW date after a scope=all move, none at the old date.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetTasksForWeek_AfterScopeAllMove_DeployedOpenComplianceRendersSingleTileAtNewDate()
    {
        var core = await GetCore();

        var monday = GetNextMonday();
        var (arpId, propertyId, planningId, areaId) = await SeedWeeklyMondaySeries(monday);

        // Deployed-open compliance at the original Monday (Status 66 != 100).
        var openCaseId = await SeedSdkCase(status: 66, microtingUid: 9541);
        await SeedDeployedCompliance(planningId, propertyId, areaId, monday, openCaseId);

        var service = BuildService(core);

        // Baseline: original Monday's week renders exactly ONE tile for this planning.
        var beforeTiles = await TilesForWeek(service, propertyId, monday, planningId);
        var beforeOnMonday = beforeTiles.Where(t => t.TaskDate == Key(monday)).ToList();
        Assert.That(beforeOnMonday, Has.Count.EqualTo(1),
            "before the move the original Monday must render exactly one tile (compliance dedups the recurrence)");

        // Move the whole series Monday -> Monday+7.
        var newDate = monday.AddDays(7);
        var moveResult = await service.MoveTask(new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            OriginalDate = monday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewDate = newDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 9.0,
            Scope = "all"
        });
        Assert.That(moveResult.Success, Is.True, moveResult.Message);

        // New-date week: exactly ONE tile for this planning, at Monday+7.
        var afterNewTiles = await TilesForWeek(service, propertyId, newDate, planningId);
        Assert.Multiple(() =>
        {
            Assert.That(afterNewTiles, Has.Count.EqualTo(1),
                "after the move the new-date week must render exactly one tile for this planning");
            Assert.That(afterNewTiles[0].TaskDate, Is.EqualTo(Key(newDate)),
                "the single tile must sit at the new date");
        });

        // Original Monday's week: ZERO tiles for this planning on the original Monday.
        var afterOldTiles = await TilesForWeek(service, propertyId, monday, planningId);
        var afterOnOldMonday = afterOldTiles.Where(t => t.TaskDate == Key(monday)).ToList();
        Assert.That(afterOnOldMonday, Is.Empty,
            "no leftover duplicate tile remains on the original Monday after the series move");

        // DB-level: the compliance row's Deadline followed the move to Monday+7.
        var compliance = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().SingleAsync(x => x.PlanningId == planningId);
        Assert.That(compliance.Deadline.Date, Is.EqualTo(newDate.Date),
            "the deployed-open compliance row's Deadline shifts to the new date");
    }

    // ------------------------------------------------------------------
    // (b) #954 — only the dragged occurrence's open row moves; a completed
    //     history row on an earlier date stays pinned (DB-level).
    // ------------------------------------------------------------------

    [Test]
    public async Task MoveTask_ScopeAll_LeavesCompletedHistoryRowOnOtherDatePinned()
    {
        var core = await GetCore();

        var monday = GetNextMonday();
        var (arpId, propertyId, planningId, areaId) = await SeedWeeklyMondaySeries(monday);

        // Deployed-OPEN compliance at the future Monday (case A, Status 66).
        var openCaseId = await SeedSdkCase(status: 66, microtingUid: 9542);
        var openComplianceId = await SeedDeployedCompliance(planningId, propertyId, areaId, monday, openCaseId);

        // COMPLETED history compliance at an earlier past date (case B, Status 100).
        var historyDate = monday.AddDays(-7);
        var completedCaseId = await SeedSdkCase(status: 100, microtingUid: 9543);
        var historyComplianceId = await SeedDeployedCompliance(planningId, propertyId, areaId, historyDate, completedCaseId);

        var service = BuildService(core);

        var newDate = monday.AddDays(7);
        var moveResult = await service.MoveTask(new CalendarTaskMoveRequestModel
        {
            Id = arpId,
            OriginalDate = monday.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewDate = newDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 9.0,
            Scope = "all"
        });
        Assert.That(moveResult.Success, Is.True, moveResult.Message);

        var openRow = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().SingleAsync(x => x.Id == openComplianceId);
        var historyRow = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().SingleAsync(x => x.Id == historyComplianceId);

        Assert.Multiple(() =>
        {
            Assert.That(openRow.Deadline.Date, Is.EqualTo(newDate.Date),
                "the dragged occurrence's open row moves to the new date");
            Assert.That(historyRow.Deadline.Date, Is.EqualTo(historyDate.Date),
                "the completed history row stays pinned to its past date");
        });
    }
}
