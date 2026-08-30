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
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
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
/// #960 — period-level completed-occurrence suppression.
///
/// A completed occurrence freezes its WHOLE recurrence period. When a monthly
/// Nth-weekday rule is re-anchored to a different weekday (e.g. an "all"/
/// "thisAndFollowing" edit moves "1st Friday" → "1st Thursday"), the recurrence
/// emit loop would otherwise sprout a phantom not-completed sibling next to the
/// pinned completed occurrence in a month that is already done.
///
/// These tests pin the rule on the NEW weekday (Thursday) and seed a completed
/// compliance on the OLD weekday (Friday) of the same month, then assert the
/// completed month renders exactly one (completed) tile — while a future month
/// with no completed occurrence still renders the rule normally.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarCompletedPeriodSuppressionTests : TestBaseSetup
{
    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Test]
    public async Task GetTasksForWeek_MonthlyNthWeekday_CompletedMonth_NoPhantomSibling()
    {
        // Boot a real SDK Core so completion (Case.Status==100) can be read.
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var sdkSite = new Site
        {
            Name = "period-suppression-test-site",
            MicrotingUid = 5454,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        // Completed (Status=100) backing case for the 1st-Friday-of-July occurrence.
        var sdkCase = new Microting.eForm.Infrastructure.Data.Entities.Case
        {
            SiteId = sdkSite.Id,
            Status = 100,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
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
            Name = $"PeriodSuppressionTest-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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

        // Monthly Nth-weekday rule starting 2026-01-01, RE-ANCHORED to the
        // 1st THURSDAY of each month (the new pattern after the edit).
        var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Month, StartDate = startDate,
            RelatedEFormId = 0, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = 3, RepeatEvery = 1, RepeatOrdinalWeek = 1, DayOfWeek = 4, // 1st Thursday
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

        // Completed occurrence on the OLD pattern: 1st FRIDAY of July 2026 = Jul 3.
        var completedFriday = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc);
        var compliance = new Compliance
        {
            PlanningId = planning.Id, PropertyId = property.Id, AreaId = area.Id,
            Deadline = completedFriday, StartDate = completedFriday.AddDays(-30),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Removed // completed path soft-deletes
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var service = BuildService(core);

        // --- Completed month (July 2026): week Mon Jun 29 – Sun Jul 5 holds
        //     both the rule's 1st Thursday (Jul 2) and the completed Fri (Jul 3).
        var julyWeekStart = new DateTime(2026, 6, 29, 0, 0, 0, DateTimeKind.Utc);
        var julyResult = await service.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = property.Id,
            WeekStart = IsoUtc(julyWeekStart),
            WeekEnd = IsoUtc(julyWeekStart.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [], TagNames = [], SiteIds = []
        });

        Assert.That(julyResult.Success, Is.True, julyResult.Message);
        Assert.That(julyResult.Model, Is.Not.Null);

        var thursdayTiles = julyResult.Model!.Where(t => t.TaskDate == Key(new DateTime(2026, 7, 2))).ToList();
        var fridayTiles = julyResult.Model!.Where(t => t.TaskDate == Key(completedFriday)).ToList();

        Assert.Multiple(() =>
        {
            // The phantom: re-anchored Thursday in the completed month must NOT render.
            Assert.That(thursdayTiles, Is.Empty,
                "No phantom not-completed Thursday sibling in a completed month");
            // The real completed occurrence renders exactly once via the compliance loop.
            Assert.That(fridayTiles, Has.Count.EqualTo(1),
                "Completed Friday renders exactly once");
            Assert.That(fridayTiles[0].Completed, Is.True, "Friday must show completed");
            Assert.That(fridayTiles[0].IsFromCompliance, Is.True, "Friday must be the compliance row");
        });

        // --- Future month (August 2026): no completed occurrence → the rule's
        //     1st Thursday (Aug 6) renders normally as a not-completed tile.
        var augWeekStart = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);
        var augResult = await service.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = property.Id,
            WeekStart = IsoUtc(augWeekStart),
            WeekEnd = IsoUtc(augWeekStart.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [], TagNames = [], SiteIds = []
        });

        Assert.That(augResult.Success, Is.True, augResult.Message);
        var augThursday = augResult.Model!.Where(t => t.TaskDate == Key(new DateTime(2026, 8, 6))).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(augThursday, Has.Count.EqualTo(1),
                "Future month with no completed occurrence renders the rule's 1st Thursday");
            Assert.That(augThursday[0].Completed, Is.False, "Future Thursday is not completed");
        });
    }

    [Test]
    public async Task UpdateTask_ScopeAll_DateChange_RelocatesNonCompleted_FreezesCompleted()
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var sdkSite = new Site
        {
            Name = "all-repattern-test-site", MicrotingUid = 6565,
            LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        // Two backing cases: one NOT completed (relocatable), one completed (frozen).
        var caseNotDone = new Microting.eForm.Infrastructure.Data.Entities.Case
        { SiteId = sdkSite.Id, Status = 66, WorkflowState = Constants.WorkflowStates.Created };
        var caseDone = new Microting.eForm.Infrastructure.Data.Entities.Case
        { SiteId = sdkSite.Id, Status = 100, WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext.Cases.AddRangeAsync(caseNotDone, caseDone);
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
            Name = $"AllRepatternTest-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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

        // Monthly Nth-weekday series currently on the 1st FRIDAY (DayOfWeek=5).
        var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Month, StartDate = startDate,
            RelatedEFormId = 0, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = 3, RepeatEvery = 1, RepeatOrdinalWeek = 1, DayOfWeek = 5, // 1st Friday (old)
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

        // Deployed-but-NOT-completed past occurrence: 1st Friday of May 2026 = May 1.
        var mayFriday = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var notDone = new Compliance
        {
            PlanningId = planning.Id, PropertyId = property.Id, AreaId = area.Id,
            Deadline = mayFriday, StartDate = mayFriday.AddDays(-30),
            MicrotingSdkCaseId = caseNotDone.Id, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created
        };
        // Completed (frozen) occurrence: 1st Friday of July 2026 = Jul 3.
        var julyFriday = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc);
        var done = new Compliance
        {
            PlanningId = planning.Id, PropertyId = property.Id, AreaId = area.Id,
            Deadline = julyFriday, StartDate = julyFriday.AddDays(-30),
            MicrotingSdkCaseId = caseDone.Id, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext.Compliances.AddRangeAsync(notDone, done);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        var notDoneId = notDone.Id;
        var doneId = done.Id;

        var service = BuildService(core);

        // scope=all, change the 1st Friday → 1st Thursday going forward. Click a
        // future occurrence (Sep 4, old Friday) and move it to Sep 3 (new Thursday).
        var result = await service.UpdateTask(new CalendarTaskUpdateRequestModel
        {
            Id = arp.Id,
            Scope = "all",
            OriginalDate = "2026-09-04T00:00:00Z",
            StartDate = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc), // 1st Thursday of Sep
            StartHour = 9.0,
            Duration = 1.0,
            Status = 1,
            RepeatType = 3,
            RepeatEvery = 1,
            RepeatOrdinalWeek = 1,
            ComplianceEnabled = false,
            PropertyId = property.Id,
            EformId = 0,
            Sites = [(int)sdkSite.Id],
            TagIds = [],
            Translates = []
        });

        Assert.That(result.Success, Is.True, result.Message);

        var reloadedNotDone = await BackendConfigurationPnDbContext.Compliances.AsNoTracking()
            .FirstAsync(c => c.Id == notDoneId);
        var reloadedDone = await BackendConfigurationPnDbContext.Compliances.AsNoTracking()
            .FirstAsync(c => c.Id == doneId);

        Assert.Multiple(() =>
        {
            // Deployed-not-completed past Friday moves to the new pattern's 1st
            // Thursday of the SAME month (May 7), preserving its period.
            Assert.That(reloadedNotDone.Deadline.Date, Is.EqualTo(new DateTime(2026, 5, 7)),
                "Non-completed past occurrence relocates to 1st Thursday of May");
            // Completed occurrence is frozen — Deadline must never change (R2).
            Assert.That(reloadedDone.Deadline.Date, Is.EqualTo(julyFriday.Date),
                "Completed occurrence stays on its original date");
        });
    }

    /// <summary>
    /// #952 hardening — the "all"-scope relocation must compute the new-pattern
    /// date from the SAME source the renderer (GetOccurrencesInWeek) uses,
    /// i.e. <c>planning.DayOfMonth</c>, NOT <c>arp.DayOfMonth</c>. Otherwise a
    /// relocated compliance lands on a different day than the rule actually
    /// renders, leaving a duplicate/misplaced tile.
    ///
    /// The task wizard is mocked, so it does NOT re-derive planning.DayOfMonth
    /// on the edit — that deliberately decouples planning.DayOfMonth (4, the
    /// renderer's day) from arp.DayOfMonth (which UpdateTask sets from the
    /// request's DayOfMonth = 15). A correct relocation moves the past row to
    /// June 4 (planning), not June 15 (arp).
    ///
    /// In production the (real) wizard runs first and writes
    /// planning.DayOfMonth = DeriveDayOfMonth(Year, newStartDate) = newStartDate.Day,
    /// then UpdateTask re-reads the planning row before relocating, so the two
    /// agree and relocation reads the fresh value. This test isolates ONLY the
    /// "which field does relocation trust" invariant by holding planning fixed.
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeAll_Yearly_RelocatesUsingPlanningDayOfMonth_NotArp()
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var sdkSite = new Site
        {
            Name = "yearly-dom-source-site", MicrotingUid = 8787,
            LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        // Deployed-but-NOT-completed backing case (relocatable).
        var caseNotDone = new Microting.eForm.Infrastructure.Data.Entities.Case
        { SiteId = sdkSite.Id, Status = 66, WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext.Cases.AddAsync(caseNotDone);
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
            Name = $"YearlyDomSource-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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

        // Yearly series anchored on June 4 (planning.DayOfMonth = 4 = renderer's day).
        var startDate = new DateTime(2020, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = (RepeatType)4,
            DayOfMonth = 4, StartDate = startDate, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = 4, RepeatEvery = 1, DayOfMonth = 4,
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

        // Deployed-not-completed past occurrence sitting on the WRONG day (June 20).
        var pastWrongDay = new DateTime(2022, 6, 20, 0, 0, 0, DateTimeKind.Utc);
        var notDone = new Compliance
        {
            PlanningId = planning.Id, PropertyId = property.Id, AreaId = area.Id,
            Deadline = pastWrongDay, StartDate = pastWrongDay.AddDays(-30),
            MicrotingSdkCaseId = caseNotDone.Id, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(notDone);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        var notDoneId = notDone.Id;

        var service = BuildService(core);

        // scope=all date change. The request's DayOfMonth = 15 DIVERGES from
        // planning.DayOfMonth (4) — the wizard mock leaves planning.DayOfMonth
        // untouched, so this isolates which field the relocation trusts.
        var result = await service.UpdateTask(new CalendarTaskUpdateRequestModel
        {
            Id = arp.Id,
            Scope = "all",
            OriginalDate = "2027-06-20T00:00:00Z",
            StartDate = new DateTime(2027, 6, 4, 0, 0, 0, DateTimeKind.Utc), // future, date changed
            StartHour = 9.0,
            Duration = 1.0,
            Status = 1,
            RepeatType = 4,
            RepeatEvery = 1,
            RepeatOrdinalWeek = null,
            DayOfMonth = 15, // divergent on purpose
            ComplianceEnabled = false,
            PropertyId = property.Id,
            EformId = 0,
            Sites = [(int)sdkSite.Id],
            TagIds = [],
            Translates = []
        });

        Assert.That(result.Success, Is.True, result.Message);

        var reloaded = await BackendConfigurationPnDbContext.Compliances.AsNoTracking()
            .FirstAsync(c => c.Id == notDoneId);

        // Must land on June 4 (planning.DayOfMonth — what the renderer generates),
        // NOT June 15 (arp.DayOfMonth from the request). Pre-fix it moved to June 15.
        Assert.That(reloaded.Deadline.Date, Is.EqualTo(new DateTime(2022, 6, 4)),
            "relocation uses planning.DayOfMonth (renderer's source), not arp.DayOfMonth");
    }

    private BackendConfigurationCalendarService BuildService(eFormCore.Core core)
    {
        var language = MicrotingDbContext!.Languages.First();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        taskWizardService.DeleteTask(Arg.Any<int>()).Returns(Task.FromResult(new OperationResult(true)));
        taskWizardService.UpdateTask(Arg.Any<TaskWizardCreateModel>())
            .Returns(Task.FromResult(new OperationResult(true)));

        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>());
    }
}
