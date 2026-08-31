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
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
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

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarActionableOnlyTests : TestBaseSetup
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

    /// <summary>
    /// PR #847 regression coverage: a removed-completed Compliance (the shape
    /// the canonical complete paths leave behind — WorkflowState=Removed +
    /// backing SDK Case Status=100) must suppress recurrence-expansion re-emit
    /// for that same date on the ActionableOnly (mobile-worker gRPC) read path,
    /// without rendering the completed event as an actionable task.
    /// </summary>
    [Test]
    public async Task GetTasksForWeek_ActionableOnly_RemovedCompletedCompliance_SuppressesRecurrenceReEmit()
    {
        // Arrange — boot a real SDK Core so IsComplianceActionable can read
        // the backing Case row from the SDK schema testcontainer.
        var core = await GetCore();

        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var sdkSite = new Site
        {
            Name = "actionable-only-test-site",
            MicrotingUid = 4242,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        // The SDK Case the just-completed Compliance points at — Status=100
        // is the canonical "done" marker that IsComplianceActionable strips
        // and that the new dedup filter keys off.
        var sdkCase = new Microting.eForm.Infrastructure.Data.Entities.Case
        {
            SiteId = sdkSite.Id,
            Status = 100,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        // The recurring rotation that would otherwise re-emit a phantom task
        // for the just-completed date. Weekly Monday with no end so the
        // emitted week is part of the active recurrence series.
        var monday = GetNextMonday();
        var area = new Area
        {
            Type = AreaTypesEnum.Type1,
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"ActionableOnlyTest-{Guid.NewGuid()}",
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id,
            PropertyId = property.Id,
            EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true,
            RepeatEvery = 1,
            RepeatType = RepeatType.Week,
            StartDate = monday,
            RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id,
            PropertyId = property.Id,
            AreaId = area.Id,
            ItemPlanningId = planning.Id,
            StartDate = monday,
            Status = true,
            RepeatType = 2,       // 2 = weekly
            RepeatEvery = 1,      // every week
            RepeatWeekdaysCsv = "1", // Monday only
            DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id,
            StartHour = 9.0,
            Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(calConfig);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // The just-completed Compliance row: canonical complete-path shape
        // (WorkflowState=Removed + MicrotingSdkCaseId pointing at a Status=100
        // case). Without the PR #847 fix the ActionableOnly read would skip
        // this row entirely, miss the date from the dedup set, and the
        // recurrence loop would re-emit a fresh uncompleted task for monday.
        var compliance = new Compliance
        {
            PlanningId = planning.Id,
            PropertyId = property.Id,
            AreaId = area.Id,
            Deadline = monday,
            StartDate = monday.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id,
            MicrotingSdkeFormId = 0,
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
        taskWizardService.DeleteTask(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(true)));

        var service = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext,
            coreHelper,
            Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext,
            taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>());

        // Act — ListEvents-style mobile-worker fetch (ActionableOnly=true) for
        // the week containing the just-completed Monday.
        var weekStart = monday;
        var weekEnd = monday.AddDays(6).AddHours(23).AddMinutes(59);
        var result = await service.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = property.Id,
            WeekStart = IsoUtc(weekStart),
            WeekEnd = IsoUtc(weekEnd),
            ActionableOnly = true,
            BoardIds = [],
            TagNames = [],
            SiteIds = []
        });

        // Assert — recurrence-expansion did NOT re-emit a phantom uncompleted
        // task for the just-completed date. The mobile worker must see the
        // event ABSENT (Status=100 ⇒ stripped by IsComplianceActionable AND
        // the dedup set suppresses the recurrence emit). Without the PR #847
        // dedup union, this test would see a single task on Monday with
        // IsFromCompliance=false (the recurrence shape).
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.Not.Null);

        var mondayKey = monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var tasksOnMonday = result.Model
            .Where(t => t.TaskDate == mondayKey)
            .ToList();
        Assert.That(tasksOnMonday, Is.Empty,
            "Removed-completed Compliance for that date must suppress recurrence re-emit; "
            + "otherwise the mobile worker sees a phantom uncompleted task right after "
            + "completing it. See PR #847 / BackendConfigurationCalendarService.cs "
            + "compliancesForDedup construction in the ActionableOnly branch.");
    }
}
