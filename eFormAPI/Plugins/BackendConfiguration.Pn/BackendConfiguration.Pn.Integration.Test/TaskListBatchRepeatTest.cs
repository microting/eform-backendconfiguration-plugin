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

using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarOccurrenceRetraction;
using BackendConfiguration.Pn.Services.CalendarPastSeriesBackfill;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// "Sæt frekvens" — the <c>ChangeRepeat</c> batch action on
/// <see cref="BackendConfigurationTaskListService"/>.
///
/// The action puts ONE custom repeat rule (the "Brugerdefineret gentagelse"
/// dialog: unit, interval, weekdays, day-of-month / Nth weekday and the
/// "Slutter" end condition) and one new start date on every selected task. On
/// top of the shared <c>BuildUpdateModel</c> + <c>RunPerTask</c> rail it
/// overwrites exactly those fields, plus the two overrides ChangeStartDate
/// needs to make the new anchor stick (OriginalDate = null, Scope = "all").
/// Everything else must round-trip unchanged, so — like
/// <see cref="TaskListBatchStatusTest"/> — the assertions are on the CAPTURED
/// <c>CalendarTaskUpdateRequestModel</c>, built from the real DB.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskListBatchRepeatTest : TestBaseSetup
{
    private IBackendConfigurationCalendarService _calendarService = null!;
    private BackendConfigurationTaskListService _taskListService = null!;
    private List<CalendarTaskUpdateRequestModel> _updateCalls = null!;

    [SetUp]
    public async Task SetupTaskListService()
    {
        // FK-safe cleanup so each test starts fresh (same ordering as
        // TaskListBatchStatusTest).
        BackendConfigurationPnDbContext!.Compliances.RemoveRange(
            BackendConfigurationPnDbContext.Compliances);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRulePlanningTags.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlanningTags);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.PlanningSites.RemoveRange(
            BackendConfigurationPnDbContext.PlanningSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRulePlannings.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlannings);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

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

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage()
            .Returns(Task.FromResult(new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));

        // Echoes the key back, so GetString("Key") is itself the message.
        var localizationService = Substitute.For<IBackendConfigurationLocalizationService>();
        localizationService.GetString(Arg.Any<string>())
            .Returns(callInfo => (string)callInfo[0]);

        _updateCalls = [];
        _calendarService = Substitute.For<IBackendConfigurationCalendarService>();
        _calendarService.UpdateTask(Arg.Do<CalendarTaskUpdateRequestModel>(m => _updateCalls.Add(m)))
            .Returns(Task.FromResult(new OperationResult(true, "CalendarTaskUpdatedSuccessfully")));

        _taskListService = new BackendConfigurationTaskListService(
            localizationService,
            userService,
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            _calendarService,
            Substitute.For<IBackendConfigurationTaskWizardService>(),
            // ChangeRepeat never touches the start-date preview projections.
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            TestContextLogger<BackendConfigurationTaskListService>.Instance
        );
    }

    /// <summary>
    /// Seeds Area → Property → AreaRule(+translation, CreatedInGuide=true) →
    /// Planning → AreaRulePlanning(+PlanningSites) → CalendarConfiguration — the
    /// exact shape <c>BuildUpdateModel</c> reads — with the given STORED repeat
    /// rule, so the test can prove the batch replaces each of its fields.
    /// Returns the ARP Id.
    /// </summary>
    private async Task<int> SeedTask(
        IEnumerable<int> siteIds,
        int repeatType,
        int repeatEvery,
        string? repeatWeekdaysCsv = null,
        int dayOfMonth = 0,
        int? repeatOrdinalWeek = null,
        int? repeatEndMode = null,
        int? repeatOccurrences = null,
        DateTime? repeatUntilDate = null,
        int eformId = 7,
        bool complianceEnabled = true,
        bool status = true)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);

        var property = new Property
        {
            Name = $"BatchRepeatProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = eformId, CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);

        var areaRuleTranslation = new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = "Task", Description = "Task description",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRuleTranslation.Create(BackendConfigurationPnDbContext!);

        var anchor = DateTime.UtcNow.Date;

        var planning = new Planning
        {
            Enabled = status, RepeatEvery = repeatEvery,
            RepeatType = (Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType)repeatType,
            StartDate = anchor, RelatedEFormId = eformId, Description = "Original description",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planning.Create(ItemsPlanningPnDbContext!);

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = anchor, Status = status,
            RepeatType = repeatType, RepeatEvery = repeatEvery,
            RepeatWeekdaysCsv = repeatWeekdaysCsv,
            DayOfWeek = (int)anchor.DayOfWeek, DayOfMonth = dayOfMonth,
            RepeatOrdinalWeek = repeatOrdinalWeek,
            RepeatEndMode = repeatEndMode, RepeatOccurrences = repeatOccurrences,
            RepeatUntilDate = repeatUntilDate,
            ComplianceEnabled = complianceEnabled,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);

        foreach (var siteId in siteIds)
        {
            var planningSite = new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
            {
                AreaRulePlanningsId = arp.Id, SiteId = siteId,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await planningSite.Create(BackendConfigurationPnDbContext!);
        }

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await calConfig.Create(BackendConfigurationPnDbContext!);

        return arp.Id;
    }

    // ------------------------------------------------------------------
    // The rule and the start date land on EVERY selected task
    // ------------------------------------------------------------------

    /// <summary>
    /// Three tasks with three different stored rules (daily, weekly on one day
    /// ending after N, monthly on the Nth weekday ending on a date) all get the
    /// SAME new rule: every 2nd week on Mon+Wed+Fri, ending after 12
    /// occurrences — and the same new start date.
    /// </summary>
    [Test]
    public async Task ChangeRepeat_WeeklyRule_IsSetOnEverySelectedTask()
    {
        var daily = await SeedTask([100], repeatType: 1, repeatEvery: 1);
        var weekly = await SeedTask([101], repeatType: 2, repeatEvery: 1,
            repeatWeekdaysCsv: "2", repeatEndMode: 1, repeatOccurrences: 4);
        var monthly = await SeedTask([102], repeatType: 3, repeatEvery: 3,
            repeatWeekdaysCsv: "4", repeatOrdinalWeek: 2, repeatEndMode: 2,
            repeatUntilDate: DateTime.UtcNow.Date.AddMonths(6));
        var newStart = DateTime.UtcNow.Date.AddDays(10);

        var result = await _taskListService.ChangeRepeat(new TaskListBatchRepeatModel
        {
            TaskIds = [daily, weekly, monthly],
            StartDate = newStart,
            RepeatType = 2,
            RepeatEvery = 2,
            RepeatWeekdaysCsv = "1,3,5",
            DayOfMonth = null,
            RepeatOrdinalWeek = null,
            RepeatEndMode = 1,
            RepeatOccurrences = 12,
            RepeatUntilDate = null
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(3));
        Assert.That(_updateCalls.Select(c => c.Id), Is.EquivalentTo(new[] { daily, weekly, monthly }));
        foreach (var call in _updateCalls)
        {
            Assert.Multiple(() =>
            {
                Assert.That(call.StartDate, Is.EqualTo(newStart), $"#{call.Id} start date");
                Assert.That(call.OriginalDate, Is.Null,
                    $"#{call.Id}: a non-null OriginalDate makes UpdateTask discard the new start date");
                Assert.That(call.Scope, Is.EqualTo("all"), $"#{call.Id} scope");
                Assert.That(call.RepeatType, Is.EqualTo(2), $"#{call.Id} repeat type");
                Assert.That(call.RepeatEvery, Is.EqualTo(2), $"#{call.Id} repeat every");
                Assert.That(call.RepeatWeekdaysCsv, Is.EqualTo("1,3,5"), $"#{call.Id} weekdays");
                Assert.That(call.DayOfMonth, Is.Null, $"#{call.Id}: a stale day-of-month must be cleared");
                Assert.That(call.RepeatOrdinalWeek, Is.Null, $"#{call.Id}: a stale ordinal must be cleared");
                Assert.That(call.RepeatEndMode, Is.EqualTo(1), $"#{call.Id} end mode");
                Assert.That(call.RepeatOccurrences, Is.EqualTo(12), $"#{call.Id} occurrences");
                Assert.That(call.RepeatUntilDate, Is.Null, $"#{call.Id}: a stale until-date must be cleared");
            });
        }
    }

    /// <summary>
    /// The "Slutter: til og med dato" branch and a monthly day-of-month rule,
    /// so every field of the dialog is carried at least once across the tests.
    /// </summary>
    [Test]
    public async Task ChangeRepeat_MonthlyDayOfMonthUntilDate_IsSetVerbatim()
    {
        var first = await SeedTask([100], repeatType: 2, repeatEvery: 1, repeatWeekdaysCsv: "1,2");
        var second = await SeedTask([101], repeatType: 4, repeatEvery: 1, dayOfMonth: 5);
        var newStart = DateTime.UtcNow.Date.AddDays(3);
        var until = DateTime.UtcNow.Date.AddMonths(9);

        var result = await _taskListService.ChangeRepeat(new TaskListBatchRepeatModel
        {
            TaskIds = [first, second],
            StartDate = newStart,
            RepeatType = 3,
            RepeatEvery = 1,
            RepeatWeekdaysCsv = null,
            DayOfMonth = 15,
            RepeatOrdinalWeek = null,
            RepeatEndMode = 2,
            RepeatOccurrences = null,
            RepeatUntilDate = until
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(2));
        foreach (var call in _updateCalls)
        {
            Assert.Multiple(() =>
            {
                Assert.That(call.StartDate, Is.EqualTo(newStart));
                Assert.That(call.RepeatType, Is.EqualTo(3));
                Assert.That(call.RepeatEvery, Is.EqualTo(1));
                Assert.That(call.RepeatWeekdaysCsv, Is.Null, "stale weekdays must be cleared");
                Assert.That(call.DayOfMonth, Is.EqualTo(15));
                Assert.That(call.RepeatOrdinalWeek, Is.Null);
                Assert.That(call.RepeatEndMode, Is.EqualTo(2));
                Assert.That(call.RepeatOccurrences, Is.Null);
                Assert.That(call.RepeatUntilDate?.Date, Is.EqualTo(until));
            });
        }
    }

    /// <summary>
    /// The "Nth weekday of the month" rule carries its weekday CSV, and its
    /// ordinal follows the new start date (#1289, as in ChangeStartDate) rather
    /// than the one sent — the 2 sent here is wrong on purpose for any date in
    /// the first or last weeks of a month. A stored "after N" end condition is
    /// replaced by "never" when the request leaves the end mode out.
    /// </summary>
    [Test]
    public async Task ChangeRepeat_MonthlyNthWeekday_OrdinalFollowsTheStartDate()
    {
        var arpId = await SeedTask([100], repeatType: 1, repeatEvery: 1,
            repeatEndMode: 1, repeatOccurrences: 4);
        var newStart = DateTime.UtcNow.Date.AddDays(1);
        var expectedOrdinal = (newStart.Day - 1) / 7 + 1;

        var result = await _taskListService.ChangeRepeat(new TaskListBatchRepeatModel
        {
            TaskIds = [arpId],
            StartDate = newStart,
            RepeatType = 3,
            RepeatEvery = 1,
            RepeatWeekdaysCsv = "1",
            DayOfMonth = 0,
            RepeatOrdinalWeek = expectedOrdinal == 2 ? 3 : 2,
            RepeatEndMode = null
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].RepeatType, Is.EqualTo(3));
            Assert.That(_updateCalls[0].RepeatWeekdaysCsv, Is.EqualTo("1"));
            Assert.That(_updateCalls[0].DayOfMonth, Is.EqualTo(0));
            Assert.That(_updateCalls[0].RepeatOrdinalWeek, Is.EqualTo(expectedOrdinal),
                "the ordinal of an Nth-weekday rule is taken from the new start date");
            Assert.That(_updateCalls[0].RepeatEndMode, Is.EqualTo(0),
                "'Slutter: aldrig' must be sent as 0, not left as the stored end mode");
            Assert.That(_updateCalls[0].RepeatOccurrences, Is.Null);
        });
    }

    // ------------------------------------------------------------------
    // Everything else must round-trip untouched
    // ------------------------------------------------------------------

    [Test]
    public async Task ChangeRepeat_LeavesEformWorkersComplianceAndStatusUntouched()
    {
        var arpId = await SeedTask([100, 101], repeatType: 1, repeatEvery: 1,
            eformId: 42, complianceEnabled: false, status: false);

        var result = await _taskListService.ChangeRepeat(new TaskListBatchRepeatModel
        {
            TaskIds = [arpId],
            StartDate = DateTime.UtcNow.Date.AddDays(7),
            RepeatType = 2,
            RepeatEvery = 1,
            RepeatWeekdaysCsv = "2",
            RepeatEndMode = 0
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].EformId, Is.EqualTo(42));
            Assert.That(_updateCalls[0].Sites, Is.EquivalentTo(new[] { 100, 101 }));
            Assert.That(_updateCalls[0].ComplianceEnabled, Is.False);
            Assert.That(_updateCalls[0].Status, Is.EqualTo(2), "an inactive task stays inactive");
            Assert.That(_updateCalls[0].StartHour, Is.EqualTo(9.0));
            Assert.That(_updateCalls[0].Duration, Is.EqualTo(1.0));
            Assert.That(_updateCalls[0].Translates.Select(t => t.Name), Is.EquivalentTo(new[] { "Task" }));
        });
    }

    // ------------------------------------------------------------------
    // Partial failure and the pre-loop guard
    // ------------------------------------------------------------------

    [Test]
    public async Task ChangeRepeat_UnknownPlanningId_PartialFailure_TheGoodOneStillRuns()
    {
        var validArpId = await SeedTask([100], repeatType: 1, repeatEvery: 1);
        const int unknownArpId = 999_999;

        var result = await _taskListService.ChangeRepeat(new TaskListBatchRepeatModel
        {
            TaskIds = [unknownArpId, validArpId],
            StartDate = DateTime.UtcNow.Date.AddDays(2),
            RepeatType = 2,
            RepeatEvery = 1,
            RepeatWeekdaysCsv = "3",
            RepeatEndMode = 0
        });

        Assert.That(result.Success, Is.True, "one of two succeeded, so overall Success is true");
        Assert.Multiple(() =>
        {
            Assert.That(result.Message, Does.Contain("1/2"));
            Assert.That(result.Message, Does.Contain("Task not found"));
            Assert.That(_updateCalls, Has.Count.EqualTo(1));
            Assert.That(_updateCalls[0].Id, Is.EqualTo(validArpId));
            Assert.That(_updateCalls[0].RepeatWeekdaysCsv, Is.EqualTo("3"));
        });
    }

    /// <summary>
    /// A rule the custom-repeat dialog cannot produce is rejected once, before
    /// the loop: no repeat at all (which would stop every selected task from
    /// recurring), an interval of 0, an unknown end mode, or an end condition
    /// without its count or date.
    /// </summary>
    [TestCase(0, 1, 0, null, false, TestName = "ChangeRepeat_InvalidRule_NoRepeatType_IsRejected")]
    [TestCase(5, 1, 0, null, false, TestName = "ChangeRepeat_InvalidRule_UnknownRepeatType_IsRejected")]
    [TestCase(2, 0, 0, null, false, TestName = "ChangeRepeat_InvalidRule_ZeroInterval_IsRejected")]
    [TestCase(2, 1, 3, null, false, TestName = "ChangeRepeat_InvalidRule_UnknownEndMode_IsRejected")]
    [TestCase(2, 1, 1, null, false, TestName = "ChangeRepeat_InvalidRule_AfterWithoutCount_IsRejected")]
    [TestCase(2, 1, 1, 0, false, TestName = "ChangeRepeat_InvalidRule_AfterZeroTimes_IsRejected")]
    [TestCase(2, 1, 2, null, false, TestName = "ChangeRepeat_InvalidRule_UntilWithoutDate_IsRejected")]
    public async Task ChangeRepeat_InvalidRule_IsRejectedBeforeAnyTaskIsTouched(
        int repeatType, int repeatEvery, int repeatEndMode, int? repeatOccurrences, bool withUntilDate)
    {
        var arpId = await SeedTask([100], repeatType: 1, repeatEvery: 1);

        var result = await _taskListService.ChangeRepeat(new TaskListBatchRepeatModel
        {
            TaskIds = [arpId],
            StartDate = DateTime.UtcNow.Date.AddDays(1),
            RepeatType = repeatType,
            RepeatEvery = repeatEvery,
            RepeatWeekdaysCsv = "1",
            RepeatEndMode = repeatEndMode,
            RepeatOccurrences = repeatOccurrences,
            RepeatUntilDate = withUntilDate ? DateTime.UtcNow.Date.AddMonths(1) : null
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("InvalidRepeatRule"));
            Assert.That(_updateCalls, Is.Empty);
        });
    }

    /// <summary>
    /// An absent start date deserialises to default(DateTime). It is rejected
    /// once, before the loop, so no task is touched — the same guard as
    /// ChangeStartDate.
    /// </summary>
    [Test]
    public async Task ChangeRepeat_MissingStartDate_IsRejectedBeforeAnyTaskIsTouched()
    {
        var first = await SeedTask([100], repeatType: 1, repeatEvery: 1);
        var second = await SeedTask([101], repeatType: 1, repeatEvery: 1);

        var result = await _taskListService.ChangeRepeat(new TaskListBatchRepeatModel
        {
            TaskIds = [first, second],
            RepeatType = 2,
            RepeatEvery = 1,
            RepeatWeekdaysCsv = "1",
            RepeatEndMode = 0
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("StartDateIsRequired"));
            Assert.That(_updateCalls, Is.Empty);
        });
    }
}
