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
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
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
/// Regression coverage for the bug where completing ONE occurrence of a
/// multi-day recurring series (e.g. Mon–Fri) removed ALL the other
/// (uncompleted) occurrences from the calendar week view.
///
/// Root cause was a per-PLANNING dedup gate in GetTasksForWeek's recurrence
/// loop: the moment any compliance row existed for the planning in the week it
/// skipped EVERY occurrence of that planning, not just the completed date. The
/// fix keys the gate per (planning, date).
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarCompleteOccurrenceTests : TestBaseSetup
{
    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static DateTime GetNextMonday()
    {
        var today = DateTime.UtcNow.Date;
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7;
        return DateTime.SpecifyKind(today.AddDays(daysUntilMonday), DateTimeKind.Utc);
    }

    [Test]
    public async Task GetTasksForWeek_MultiDaySeries_CompleteOneDay_KeepsOtherDays()
    {
        // Boot a real SDK Core so the dedup path can read the backing Case.
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var sdkSite = new Site
        {
            Name = "complete-occurrence-test-site",
            MicrotingUid = 4343,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        // Status=100 backing case for the completed Tuesday occurrence.
        var sdkCase = new Microting.eForm.Infrastructure.Data.Entities.Case
        {
            SiteId = sdkSite.Id,
            Status = 100,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var monday = GetNextMonday();

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"CompleteOccurrenceTest-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week, StartDate = monday,
            RelatedEFormId = 0, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Mon–Fri multi-day weekly series.
        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = monday, Status = true,
            RepeatType = 2, RepeatEvery = 1, RepeatWeekdaysCsv = "1,2,3,4,5", DayOfWeek = 1,
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

        // Complete only TUESDAY: one removed-completed Compliance for that date.
        var tuesday = monday.AddDays(1);
        var compliance = new Compliance
        {
            PlanningId = planning.Id, PropertyId = property.Id, AreaId = area.Id,
            Deadline = tuesday, StartDate = monday.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Removed
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        taskWizardService.DeleteTask(Arg.Any<int>()).Returns(Task.FromResult(new OperationResult(true)));

        var service = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext, taskWizardService,
            NullLogger<BackendConfigurationCalendarService>.Instance);

        // Calendar UI fetch (ActionableOnly=false) for the week of the series.
        var result = await service.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = property.Id,
            WeekStart = IsoUtc(monday),
            WeekEnd = IsoUtc(monday.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [], TagNames = [], SiteIds = []
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.Not.Null);

        string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        bool HasTaskOn(DateTime d) => result.Model.Any(t => t.TaskDate == Key(d));

        // The exact regression: the four UNCOMPLETED weekdays must still render.
        // Pre-fix the per-planning gate dropped all of them, leaving only the
        // completed Tuesday.
        var tuesdayTasks = result.Model.Where(t => t.TaskDate == Key(tuesday)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(HasTaskOn(monday), Is.True, "Monday (uncompleted) must remain visible");
            Assert.That(HasTaskOn(monday.AddDays(2)), Is.True, "Wednesday (uncompleted) must remain visible");
            Assert.That(HasTaskOn(monday.AddDays(3)), Is.True, "Thursday (uncompleted) must remain visible");
            Assert.That(HasTaskOn(monday.AddDays(4)), Is.True, "Friday (uncompleted) must remain visible");

            // The completed day itself must stay visible (rendered by the
            // compliance loop) and must NOT be double-rendered (recurrence-emit
            // suppressed for exactly that date).
            Assert.That(tuesdayTasks, Has.Count.EqualTo(1), "Tuesday (completed) must render exactly once");
            Assert.That(tuesdayTasks[0].IsFromCompliance, Is.True, "Tuesday must be the compliance row");
            Assert.That(tuesdayTasks[0].Completed, Is.True, "Tuesday must show as completed");
        });
    }
}
