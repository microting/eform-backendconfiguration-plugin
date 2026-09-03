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

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
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

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Focused coverage for <c>BackendConfigurationCalendarService.GetTaskTrackerList</c>'s
/// <c>TaskIsExpired</c> derivation. The method is the gRPC <c>ListTaskTracker</c>
/// back-end and computes expiration from the compliance deadline. The bug-repro
/// test asserts that a deadline whose calendar date is today must NOT be flagged
/// as expired, even if its time-of-day component has passed at the moment the
/// test runs — flutter-eform's overdue list relies on date-only semantics.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationCalendarServiceTaskTrackerListTest : TestBaseSetup
{
    private IUserService _userService = null!;
    private IBackendConfigurationTaskWizardService _taskWizardService = null!;
    private IEventDeployService _eventDeployService = null!;
    private BackendConfigurationCalendarService _calendarService = null!;

    [SetUp]
    public async Task SetupCalendarService()
    {
        // FK-safe clean of the rows the seed helper writes, mirroring the
        // pattern in CalendarResizeTests / CalendarAttachmentTests.
        BackendConfigurationPnDbContext!.Compliances.RemoveRange(
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

        var sdkConnectionString = MicrotingDbContext!.Database.GetConnectionString()!;

        _userService = Substitute.For<IUserService>();
        _userService.UserId.Returns(1);
        var mockLanguage = new Language { Id = 1, Name = "English", LanguageCode = "en-US" };
        _userService.GetCurrentUserLanguage().Returns(Task.FromResult(mockLanguage));

        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        _taskWizardService.DeleteTask(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(true)));

        _eventDeployService = Substitute.For<IEventDeployService>();

        _calendarService = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            _userService,
            BackendConfigurationPnDbContext!,
            new EFormCoreService(sdkConnectionString),
            _eventDeployService,
            ItemsPlanningPnDbContext!,
            _taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            Substitute.For<IBackendConfigurationComplianceReportService>()
        );
    }

    /// <summary>
    /// Seeds Property → Area → AreaRule → Planning → AreaRulePlanning → Compliance
    /// with the given deadline. MicrotingSdkCaseId is left at 0 so
    /// GetTaskTrackerList falls through to the "no SDK case" else branch
    /// (taskIsExpired = compliance.Deadline &lt; dateTimeNow). That's the
    /// predicate this test set targets; Task 3's fix updates this exact line.
    /// </summary>
    private async Task<int> SeedEventWithDeadline(DateTime deadlineUtc)
    {
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
            Name = $"TaskTrackerListProp-{Guid.NewGuid()}",
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
            StartDate = deadlineUtc.Date.AddDays(-7),
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
            StartDate = deadlineUtc.Date.AddDays(-7),
            Status = true,
            RepeatType = 2,
            RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var compliance = new Compliance
        {
            Deadline = deadlineUtc,
            PlanningId = planning.Id,
            PropertyId = property.Id,
            StartDate = deadlineUtc.Date.AddDays(-7),
            MicrotingSdkCaseId = 0, // forces the else branch in GetTaskTrackerList
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return property.Id;
    }

    /// <summary>
    /// BUG REPRO: deadline calendar date is today (UTC). The test runs at some
    /// arbitrary moment later in the day, so a raw DateTime comparison flags
    /// the row as expired even though the user has until end-of-day to act.
    /// The angular and flutter overdue UIs both use date-only semantics, so
    /// GetTaskTrackerList must follow suit.
    /// </summary>
    [Test]
    public async Task GetTaskTrackerList_DeadlineToday_TimeOfDayPassed_NotExpired()
    {
        var todayMidnightUtc = DateTime.UtcNow.Date.AddSeconds(1);
        var propertyId = await SeedEventWithDeadline(todayMidnightUtc);

        var result = await _calendarService.GetTaskTrackerList(propertyId, sdkSiteIdForFilter: null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Has.Count.EqualTo(1));
        Assert.That(result.Model[0].TaskIsExpired, Is.False,
            "Deadline whose calendar date equals today must not be expired, " +
            "regardless of time-of-day. This is the bug fixed in Task 3.");
    }

    /// <summary>
    /// Deadline is fully in the past (yesterday). Must be flagged as expired.
    /// Sanity check that the date-only predicate still catches genuinely
    /// overdue rows.
    /// </summary>
    [Test]
    public async Task GetTaskTrackerList_DeadlineYesterday_Expired()
    {
        var yesterdayLateUtc = DateTime.UtcNow.Date.AddDays(-1).AddHours(23).AddMinutes(59);
        var propertyId = await SeedEventWithDeadline(yesterdayLateUtc);

        var result = await _calendarService.GetTaskTrackerList(propertyId, sdkSiteIdForFilter: null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Has.Count.EqualTo(1));
        Assert.That(result.Model[0].TaskIsExpired, Is.True,
            "Deadline strictly before today must be expired.");
    }

    /// <summary>
    /// Deadline is tomorrow. Must NOT be flagged as expired.
    /// Sanity check that the predicate doesn't false-positive on future rows.
    /// </summary>
    [Test]
    public async Task GetTaskTrackerList_DeadlineTomorrow_NotExpired()
    {
        var tomorrowMorningUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(8);
        var propertyId = await SeedEventWithDeadline(tomorrowMorningUtc);

        var result = await _calendarService.GetTaskTrackerList(propertyId, sdkSiteIdForFilter: null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Has.Count.EqualTo(1));
        Assert.That(result.Model[0].TaskIsExpired, Is.False,
            "Deadline strictly after today must not be expired.");
    }
}
