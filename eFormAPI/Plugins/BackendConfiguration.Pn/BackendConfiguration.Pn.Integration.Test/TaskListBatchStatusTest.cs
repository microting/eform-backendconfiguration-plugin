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

using BackendConfiguration.Pn.Infrastructure.Enums;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1123 Part B — the <c>SetStatus</c> batch action on
/// <see cref="BackendConfigurationTaskListService"/>.
///
/// SCOPE, and why substituting the calendar service is correct HERE. What this
/// action adds on top of the shared <c>BuildUpdateModel</c> + <c>RunPerTask</c>
/// rail is exactly one thing: it overwrites <c>Status</c> on the built model and
/// forwards it, leaving every other field a faithful round-trip. So these tests
/// assert on the CAPTURED <c>CalendarTaskUpdateRequestModel</c> — built from the
/// REAL Testcontainers-backed DB — rather than on DB rows, the same isolation
/// <see cref="TaskListBatchEformTagsTest"/> uses.
///
/// What deactivation then DOES to deployed occurrences is a different contract,
/// it is where the data-loss bug lived, and it is covered end-to-end with NO
/// substitution at all in <see cref="TaskWizardDeactivateRetractionTests"/>.
/// Splitting it that way is deliberate: a fixture that substituted the calendar
/// service AND claimed to prove R2 would prove nothing, which is precisely the
/// trap #1122 fell into with 25 fixtures.
///
/// Wire values under test: <c>TaskWizardStatuses.Active == 1</c>,
/// <c>NotActive == 2</c> — the convention <c>BuildUpdateModel</c> itself
/// round-trips (<c>arp.Status ? 1 : 2</c>).
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskListBatchStatusTest : TestBaseSetup
{
    private IBackendConfigurationCalendarService _calendarService = null!;
    private BackendConfigurationTaskListService _taskListService = null!;
    private List<CalendarTaskUpdateRequestModel> _updateCalls = null!;

    [SetUp]
    public async Task SetupTaskListService()
    {
        // FK-safe cleanup so each test starts fresh (same ordering as
        // TaskListBatchEformTagsTest).
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

        // Echoes the key back, matching the plugin's resx-free convention where
        // GetString("Some English sentence") is itself the message.
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
            // SetStatus never touches either projection (they exist for
            // change-start-date's preview), so substitutes that are never called
            // keep the constructor satisfied without pulling in an SDK core.
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            NullLogger<BackendConfigurationTaskListService>.Instance
        );
    }

    /// <summary>
    /// Seeds Area → Property → AreaRule(+translation, CreatedInGuide=true) →
    /// Planning → AreaRulePlanning(+PlanningSites) → CalendarConfiguration — the
    /// exact shape <c>BuildUpdateModel</c> reads. Returns the ARP Id.
    /// </summary>
    private async Task<int> SeedTask(IEnumerable<int> siteIds, bool status = true, int eformId = 7)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);

        var property = new Property
        {
            Name = $"BatchStatusProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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

        var planning = new Planning
        {
            // Mirrors the paired field: an inactive task's Planning is disabled.
            Enabled = status, RepeatEvery = 1,
            RepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Week,
            StartDate = DateTime.UtcNow.Date, RelatedEFormId = eformId, Description = "Original description",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planning.Create(ItemsPlanningPnDbContext!);

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = DateTime.UtcNow.Date, Status = status,
            RepeatType = 2, RepeatEvery = 1, ComplianceEnabled = true,
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
    // Both directions
    // ------------------------------------------------------------------

    [Test]
    public async Task SetStatus_Active_SendsStatusOne()
    {
        var arpId = await SeedTask([100], status: false);

        var result = await _taskListService.SetStatus(new TaskListBatchStatusModel
        {
            TaskIds = [arpId], Active = true
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Id, Is.EqualTo(arpId));
            Assert.That(_updateCalls[0].Scope, Is.EqualTo("all"));
            Assert.That(_updateCalls[0].Status, Is.EqualTo((int)TaskWizardStatuses.Active));
            Assert.That(_updateCalls[0].Status, Is.EqualTo(1),
                "the wire value the Angular edit modal and BuildUpdateModel both use");
        });
    }

    [Test]
    public async Task SetStatus_Inactive_SendsStatusTwo()
    {
        var arpId = await SeedTask([100]);

        var result = await _taskListService.SetStatus(new TaskListBatchStatusModel
        {
            TaskIds = [arpId], Active = false
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Status, Is.EqualTo((int)TaskWizardStatuses.NotActive));
            Assert.That(_updateCalls[0].Status, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// The whole selection is flipped, not just the first row — a batch that
    /// silently stopped after one task would still pass the single-row tests.
    /// </summary>
    [Test]
    public async Task SetStatus_MultipleTasks_FlipsEveryOne()
    {
        var first = await SeedTask([100]);
        var second = await SeedTask([101]);

        var result = await _taskListService.SetStatus(new TaskListBatchStatusModel
        {
            TaskIds = [first, second], Active = false
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls.Select(c => c.Id), Is.EquivalentTo(new[] { first, second }));
            Assert.That(_updateCalls.All(c => c.Status == (int)TaskWizardStatuses.NotActive), Is.True);
        });
    }

    // ------------------------------------------------------------------
    // Everything else must round-trip untouched
    // ------------------------------------------------------------------

    /// <summary>
    /// SetStatus overrides exactly ONE field. Sites in particular is the one that
    /// matters: an empty Sites list on an ACTIVATE would trip the wizard's
    /// "Active && Sites.Count == 0" coercion and silently leave the task
    /// inactive, so a batch reactivate that dropped assignees would look like it
    /// simply did not work.
    /// </summary>
    [Test]
    public async Task SetStatus_Activate_RoundTripsSitesAndComplianceFlag()
    {
        var arpId = await SeedTask([100, 101], status: false);

        var result = await _taskListService.SetStatus(new TaskListBatchStatusModel
        {
            TaskIds = [arpId], Active = true
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Sites, Is.EquivalentTo(new[] { 100, 101 }),
                "an activate must carry the assignees, or the wizard coerces it straight back to inactive");
            Assert.That(_updateCalls[0].ComplianceEnabled, Is.True,
                "compliance is not this action's business");
            Assert.That(_updateCalls[0].EformId, Is.EqualTo(7));
        });
    }

    /// <summary>
    /// The reverse direction of the same guarantee, and the round-trip's second
    /// half: deactivating does not strip the assignee list either, so
    /// deactivate → reactivate is symmetric.
    /// </summary>
    [Test]
    public async Task SetStatus_Deactivate_RoundTripsSites()
    {
        var arpId = await SeedTask([100, 101]);

        var result = await _taskListService.SetStatus(new TaskListBatchStatusModel
        {
            TaskIds = [arpId], Active = false
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].Sites, Is.EquivalentTo(new[] { 100, 101 }));
    }

    // ------------------------------------------------------------------
    // Partial failure
    // ------------------------------------------------------------------

    [Test]
    public async Task SetStatus_UnknownPlanningId_PartialFailure_TheGoodOneStillRuns()
    {
        var validArpId = await SeedTask([100]);
        const int unknownArpId = 999_999;

        var result = await _taskListService.SetStatus(new TaskListBatchStatusModel
        {
            TaskIds = [unknownArpId, validArpId], Active = false
        });

        Assert.That(result.Success, Is.True, "one of two succeeded, so overall Success is true");
        Assert.Multiple(() =>
        {
            Assert.That(result.Message, Does.Contain("1/2"));
            Assert.That(result.Message, Does.Contain("Task not found"));
            Assert.That(_updateCalls, Has.Count.EqualTo(1));
            Assert.That(_updateCalls[0].Id, Is.EqualTo(validArpId));
        });
    }

    /// <summary>
    /// A per-task failure returned by the calendar service (rather than an
    /// ineligible id) is reported the same way and does not stop the batch.
    /// </summary>
    [Test]
    public async Task SetStatus_CalendarServiceFailsForOneTask_ReportsPartialAndContinues()
    {
        var failing = await SeedTask([100]);
        var succeeding = await SeedTask([101]);

        // Only the RETURN is re-specified. The SetUp's `Arg.Do` capture is an
        // argument-matcher side effect and keeps firing for matching calls, so
        // re-adding to _updateCalls here would double-count every call.
        _calendarService.UpdateTask(Arg.Any<CalendarTaskUpdateRequestModel>())
            .Returns(ci => Task.FromResult(
                ci.Arg<CalendarTaskUpdateRequestModel>().Id == failing
                    ? new OperationResult(false, "boom")
                    : new OperationResult(true, "ok")));

        var result = await _taskListService.SetStatus(new TaskListBatchStatusModel
        {
            TaskIds = [failing, succeeding], Active = false
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "the second task still succeeded");
            Assert.That(result.Message, Does.Contain("1/2"));
            Assert.That(result.Message, Does.Contain("boom"));
            Assert.That(_updateCalls, Has.Count.EqualTo(2),
                "the failure of the first must not abort the loop");
        });
    }
}
