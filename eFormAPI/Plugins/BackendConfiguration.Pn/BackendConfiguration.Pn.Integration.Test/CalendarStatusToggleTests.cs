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
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

/// <summary>
/// Coverage for the rule: <strong>no toggle on the calendar event modal
/// must cause the event to disappear from the calendar tavle.</strong>
/// Status=false ⇒ task surfaces with status=false so the frontend renders
/// it dimmed; ComplianceEnabled flips don't affect calendar visibility at
/// all (they only gate mobile / tracker display when the deadline passes).
///
/// The disappearance regression originally shipped via PR #869 because the
/// cascade in TaskWizardService.UpdateTask soft-deletes the underlying
/// Planning row when Status flips OFF, and the calendar query previously
/// filtered Plannings by WorkflowState != Removed. These tests exercise
/// both the "live Planning" and "soft-deleted Planning" shapes against
/// GetTasksForWeek to lock in the contract going forward.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarStatusToggleTests : TestBaseSetup
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

    private async Task<(Property property, AreaRulePlanning arp, Planning planning)>
        SeedCalendarTask(bool status, bool complianceEnabled, bool planningSoftDeleted)
    {
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
            Name = $"StatusToggleTest-{Guid.NewGuid()}",
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
            Enabled = status,
            RepeatEvery = 1,
            RepeatType = RepeatType.Week,
            StartDate = monday,
            RelatedEFormId = 0,
            WorkflowState = planningSoftDeleted
                ? Constants.WorkflowStates.Removed
                : Constants.WorkflowStates.Created,
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
            Status = status,
            ComplianceEnabled = complianceEnabled,
            RepeatType = 2,
            RepeatEvery = 1,
            RepeatWeekdaysCsv = "1",
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

        return (property, arp, planning);
    }

    private async Task<BackendConfigurationCalendarService> BuildService()
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        taskWizardService.DeleteTask(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(true)));

        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext!,
            coreHelper,
            Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!,
            taskWizardService,
            NullLogger<BackendConfigurationCalendarService>.Instance);
    }

    private static CalendarTaskRequestModel WeekRequest(int propertyId, DateTime monday) =>
        new()
        {
            PropertyId = propertyId,
            WeekStart = IsoUtc(monday),
            WeekEnd = IsoUtc(monday.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [],
            TagNames = [],
            SiteIds = []
        };

    /// <summary>
    /// Status=true, ComplianceEnabled=true (config A in the toggle matrix).
    /// Active task — must appear with status=true so the frontend renders
    /// it normally (not dimmed).
    /// </summary>
    [Test]
    public async Task GetTasksForWeek_StatusTrueComplianceTrue_TaskAppearsActive()
    {
        var (property, _, _) = await SeedCalendarTask(
            status: true, complianceEnabled: true, planningSoftDeleted: false);
        var monday = GetNextMonday();

        var service = await BuildService();
        var result = await service.GetTasksForWeek(WeekRequest(property.Id, monday));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(result.Model[0].Status, Is.True);
        Assert.That(result.Model[0].ComplianceEnabled, Is.True);
    }

    /// <summary>
    /// Status=false (config B in the toggle matrix). Inactive task — must
    /// still appear in the response with status=false so the frontend
    /// renders it dimmed instead of dropping it entirely.
    /// </summary>
    [Test]
    public async Task GetTasksForWeek_StatusFalse_TaskRemainsVisibleAsInactive()
    {
        var (property, _, _) = await SeedCalendarTask(
            status: false, complianceEnabled: false, planningSoftDeleted: false);
        var monday = GetNextMonday();

        var service = await BuildService();
        var result = await service.GetTasksForWeek(WeekRequest(property.Id, monday));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Has.Count.GreaterThanOrEqualTo(1),
            "Status=false must NOT cause the task to disappear from the calendar; "
            + "it should surface with status=false so the frontend can dim it.");
        Assert.That(result.Model[0].Status, Is.False);
    }

    /// <summary>
    /// Status=false AND the underlying Planning is soft-deleted (the shape
    /// the TaskWizardService.UpdateTask cascade leaves behind on ON→OFF).
    /// Calendar query must still surface the row.
    /// </summary>
    [Test]
    public async Task GetTasksForWeek_StatusFalsePlanningSoftDeleted_TaskRemainsVisible()
    {
        var (property, _, _) = await SeedCalendarTask(
            status: false, complianceEnabled: false, planningSoftDeleted: true);
        var monday = GetNextMonday();

        var service = await BuildService();
        var result = await service.GetTasksForWeek(WeekRequest(property.Id, monday));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Has.Count.GreaterThanOrEqualTo(1),
            "Soft-deleted Planning must NOT cause the AreaRulePlanning to "
            + "vanish from GetTasksForWeek — the calendar query intentionally "
            + "includes WorkflowState=Removed plannings so dimmed tasks "
            + "survive the Status=OFF cascade.");
        Assert.That(result.Model[0].Status, Is.False);
    }

    /// <summary>
    /// Status=true, ComplianceEnabled=false (config C in the toggle matrix).
    /// Active task — must still appear normally. ComplianceEnabled only
    /// gates mobile / tracker display when the deadline passes; it must
    /// not affect calendar visibility for a future occurrence.
    /// </summary>
    [Test]
    public async Task GetTasksForWeek_StatusTrueComplianceFalse_TaskVisibilityUnaffected()
    {
        var (property, _, _) = await SeedCalendarTask(
            status: true, complianceEnabled: false, planningSoftDeleted: false);
        var monday = GetNextMonday();

        var service = await BuildService();
        var result = await service.GetTasksForWeek(WeekRequest(property.Id, monday));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Has.Count.GreaterThanOrEqualTo(1),
            "ComplianceEnabled=false on an active task must NOT affect "
            + "calendar visibility — only mobile/tracker behaviour when "
            + "the deadline passes.");
        Assert.That(result.Model[0].Status, Is.True);
        Assert.That(result.Model[0].ComplianceEnabled, Is.False);
    }

    /// <summary>
    /// Status round-trip: ON→OFF→ON via the projection contract. Asserts
    /// that the planning Status field flips in DB and that
    /// GetTasksForWeek reflects the change on each read. (This test
    /// exercises the database write path through direct ARP updates
    /// because the full cascade is owned by TaskWizardService.UpdateTask
    /// which is fixture-mocked here.)
    /// </summary>
    [Test]
    public async Task GetTasksForWeek_StatusRoundTrip_OnOffOn_TaskStaysVisibleThroughout()
    {
        var (property, arp, _) = await SeedCalendarTask(
            status: true, complianceEnabled: true, planningSoftDeleted: false);
        var monday = GetNextMonday();
        var service = await BuildService();

        var afterOn = await service.GetTasksForWeek(WeekRequest(property.Id, monday));
        Assert.That(afterOn.Model[0].Status, Is.True);

        arp.Status = false;
        await BackendConfigurationPnDbContext!.SaveChangesAsync();

        var afterOff = await service.GetTasksForWeek(WeekRequest(property.Id, monday));
        Assert.That(afterOff.Model, Has.Count.GreaterThanOrEqualTo(1),
            "After Status flips OFF the task must still surface (dimmed).");
        Assert.That(afterOff.Model[0].Status, Is.False);

        arp.Status = true;
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var afterOnAgain = await service.GetTasksForWeek(WeekRequest(property.Id, monday));
        Assert.That(afterOnAgain.Model[0].Status, Is.True);
    }

    /// <summary>
    /// Compliance path with soft-deleted Planning: locks in the symmetric
    /// drop of the WorkflowState=Removed filter on
    /// <c>compliancePlanningsDict</c> (around line 702 of
    /// BackendConfigurationCalendarService). A regression that re-adds the
    /// filter on the compliance loop's planning lookup would let a past
    /// Compliance row vanish when its backing Planning was soft-deleted by
    /// the Status=OFF cascade.
    /// </summary>
    [Test]
    public async Task GetTasksForWeek_CompliancePath_PlanningSoftDeleted_TaskRemainsVisible()
    {
        var (property, _, planning) = await SeedCalendarTask(
            status: false, complianceEnabled: false, planningSoftDeleted: true);
        var monday = GetNextMonday();

        var compliance = new Compliance
        {
            PlanningId = planning.Id,
            PropertyId = property.Id,
            Deadline = monday,
            StartDate = monday.AddDays(-7),
            MicrotingSdkCaseId = 0,
            MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var service = await BuildService();
        var result = await service.GetTasksForWeek(WeekRequest(property.Id, monday));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Has.Some.Matches<CalendarTaskResponseModel>(t => t.IsFromCompliance),
            "Compliance row with a soft-deleted backing Planning must still "
            + "surface in the response — symmetric to the planningsDict fix.");
    }

    /// <summary>
    /// Mixed-status response: the most common production shape — a board
    /// with both active and inactive tasks. Both ARPs must surface in the
    /// same week response with their respective Status values so the
    /// frontend can render the inactive one dimmed alongside the active
    /// ones.
    /// </summary>
    [Test]
    public async Task GetTasksForWeek_MixedStatus_BothActiveAndInactiveSurface()
    {
        var (property, _, _) = await SeedCalendarTask(
            status: true, complianceEnabled: true, planningSoftDeleted: false);
        var monday = GetNextMonday();

        // Second ARP on the same property, this one inactive with a
        // soft-deleted Planning (the cascade shape).
        var planningInactive = new Planning
        {
            Enabled = false,
            RepeatEvery = 1,
            RepeatType = RepeatType.Week,
            StartDate = monday,
            RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Removed,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planningInactive);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var areaRuleInactive = new AreaRule
        {
            AreaId = (await BackendConfigurationPnDbContext!.Areas.FirstAsync()).Id,
            PropertyId = property.Id,
            EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRuleInactive);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var arpInactive = new AreaRulePlanning
        {
            AreaRuleId = areaRuleInactive.Id,
            PropertyId = property.Id,
            AreaId = areaRuleInactive.AreaId,
            ItemPlanningId = planningInactive.Id,
            StartDate = monday,
            Status = false,
            ComplianceEnabled = false,
            RepeatType = 2,
            RepeatEvery = 1,
            RepeatWeekdaysCsv = "1",
            DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arpInactive);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(new CalendarConfiguration
        {
            AreaRulePlanningId = arpInactive.Id,
            StartHour = 10.0,
            Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var service = await BuildService();
        var result = await service.GetTasksForWeek(WeekRequest(property.Id, monday));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model.Any(t => t.Status), Is.True,
            "Mixed-status response must include the active task.");
        Assert.That(result.Model.Any(t => !t.Status), Is.True,
            "Mixed-status response must include the inactive task (dimmed).");
    }
}
