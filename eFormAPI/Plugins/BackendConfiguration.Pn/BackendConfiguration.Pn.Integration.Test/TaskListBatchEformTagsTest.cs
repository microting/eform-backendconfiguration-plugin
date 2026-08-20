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

using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using Microsoft.EntityFrameworkCore;
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
/// Covers Task 5: the ChangeEform / AddTags / RemoveTags batch actions on
/// <c>BackendConfigurationTaskListService</c>, added on top of Task 4's
/// shared <c>BuildUpdateModel</c> + <c>RunPerTask</c> pattern.
///
/// Same isolation approach as <see cref="TaskListBatchWorkersTest"/>:
/// <c>IBackendConfigurationCalendarService</c> is substituted via NSubstitute
/// so these tests verify exactly what Task 5 adds — (1) <c>EformId</c> is
/// overwritten wholesale by ChangeEform, and (2) AddTags/RemoveTags mutate
/// only the <c>TagIds</c> list (Union-dedup / Except respectively) on the
/// model built from the REAL Testcontainers-backed DB — by asserting on the
/// captured <c>CalendarTaskUpdateRequestModel</c> rather than DB rows.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskListBatchEformTagsTest : TestBaseSetup
{
    private IUserService _userService = null!;
    private IBackendConfigurationTaskWizardService _taskWizardService = null!;
    private IBackendConfigurationCalendarService _calendarService = null!;
    private IBackendConfigurationLocalizationService _localizationService = null!;
    private BackendConfigurationTaskListService _taskListService = null!;
    private List<CalendarTaskUpdateRequestModel> _updateCalls = null!;

    [SetUp]
    public async Task SetupTaskListService()
    {
        // FK-safe cleanup so each test starts fresh (mirrors
        // TaskListBatchWorkersTest / CalendarUpdateTaskScopeTests).
        BackendConfigurationPnDbContext!.AreaRulePlanningTags.RemoveRange(
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

        _userService = Substitute.For<IUserService>();
        _userService.UserId.Returns(1);
        _userService.GetCurrentUserLanguage()
            .Returns(Task.FromResult(new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));

        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();

        // Echoes the key back, matching the plugin's resx-free convention
        // where GetString("Some English sentence") is itself the message.
        _localizationService = Substitute.For<IBackendConfigurationLocalizationService>();
        _localizationService.GetString(Arg.Any<string>())
            .Returns(callInfo => (string)callInfo[0]);

        _updateCalls = [];
        _calendarService = Substitute.For<IBackendConfigurationCalendarService>();
        _calendarService.UpdateTask(Arg.Do<CalendarTaskUpdateRequestModel>(m => _updateCalls.Add(m)))
            .Returns(Task.FromResult(new OperationResult(true, "CalendarTaskUpdatedSuccessfully")));

        _taskListService = new BackendConfigurationTaskListService(
            _localizationService,
            _userService,
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            _calendarService,
            _taskWizardService,
            NullLogger<BackendConfigurationTaskListService>.Instance
        );
    }

    /// <summary>
    /// Seeds Area → Property → AreaRule(+translation, CreatedInGuide=true) →
    /// Planning → AreaRulePlanning(+PlanningSites, +AreaRulePlanningTags) →
    /// CalendarConfiguration — the exact shape <c>BuildUpdateModel</c> reads.
    /// Returns the ARP Id.
    /// </summary>
    private async Task<int> SeedTask(IEnumerable<int> siteIds, IEnumerable<int> tagIds = null,
        bool status = true, int repeatType = 2, int eformId = 7,
        bool withCalendarConfiguration = true)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);

        var property = new Property
        {
            Name = $"TaskListProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week, StartDate = DateTime.UtcNow.Date,
            RelatedEFormId = eformId, Description = "Original description",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planning.Create(ItemsPlanningPnDbContext!);

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = DateTime.UtcNow.Date, Status = status,
            RepeatType = repeatType, RepeatEvery = 1,
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

        foreach (var tagId in tagIds ?? [])
        {
            var planningTag = new AreaRulePlanningTag
            {
                AreaRulePlanningId = arp.Id, ItemPlanningTagId = tagId,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await planningTag.Create(BackendConfigurationPnDbContext!);
        }

        // A task-wizard task has no CalendarConfiguration until CreateTask or the
        // startup backfill makes one, so the un-configured shape is reachable in
        // production and BuildUpdateModel has to fall back for it.
        if (withCalendarConfiguration)
        {
            var calConfig = new CalendarConfiguration
            {
                AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await calConfig.Create(BackendConfigurationPnDbContext!);
        }

        return arp.Id;
    }

    // ------------------------------------------------------------------
    // ChangeEform
    // ------------------------------------------------------------------

    [Test]
    public async Task ChangeEform_UpdatesEformId()
    {
        var arpId = await SeedTask([100], eformId: 7);

        var result = await _taskListService.ChangeEform(new TaskListBatchChangeEformModel
        {
            TaskIds = [arpId], EformId = 99
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Id, Is.EqualTo(arpId));
            Assert.That(_updateCalls[0].Scope, Is.EqualTo("all"));
            Assert.That(_updateCalls[0].EformId, Is.EqualTo(99));
        });
    }

    [Test]
    public async Task ChangeEform_RoundTripsSitesAndTags_OtherFieldsUnchanged()
    {
        var arpId = await SeedTask([100, 101], tagIds: [5]);

        var result = await _taskListService.ChangeEform(new TaskListBatchChangeEformModel
        {
            TaskIds = [arpId], EformId = 99
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            // Sites and TagIds must round-trip unchanged — ChangeEform only
            // touches EformId.
            Assert.That(_updateCalls[0].Sites, Is.EquivalentTo(new[] { 100, 101 }));
            Assert.That(_updateCalls[0].TagIds, Is.EquivalentTo(new[] { 5 }));
        });
    }

    [Test]
    public async Task ChangeEform_UnknownPlanningId_PartialFailure_MessageContainsCount()
    {
        var validArpId = await SeedTask([100]);
        const int unknownArpId = 999_999;

        var result = await _taskListService.ChangeEform(new TaskListBatchChangeEformModel
        {
            TaskIds = [unknownArpId, validArpId], EformId = 99
        });

        Assert.That(result.Success, Is.True, "one of two succeeded, so overall Success is true");
        Assert.That(result.Message, Does.Contain("1/2"));
        Assert.That(result.Message, Does.Contain("Task not found"));
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].Id, Is.EqualTo(validArpId));
    }

    // ------------------------------------------------------------------
    // AddTags
    // ------------------------------------------------------------------

    [Test]
    public async Task AddTags_UnionsWithoutDuplicatingExistingTags()
    {
        var arpId = await SeedTask([100], tagIds: [1, 2]);

        var result = await _taskListService.AddTags(new TaskListBatchTagsModel
        {
            TaskIds = [arpId], TagIds = [2, 3]
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        // 2 already existed — union must not duplicate it.
        Assert.That(_updateCalls[0].TagIds, Is.EquivalentTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task AddTags_NoExistingTags_ResultIsExactlyTheAddedSet()
    {
        var arpId = await SeedTask([100]);

        var result = await _taskListService.AddTags(new TaskListBatchTagsModel
        {
            TaskIds = [arpId], TagIds = [7, 8]
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].TagIds, Is.EquivalentTo(new[] { 7, 8 }));
    }

    [Test]
    public async Task AddTags_RoundTripsSitesAndEformId_OtherFieldsUnchanged()
    {
        var arpId = await SeedTask([100, 101], tagIds: [1], eformId: 7);

        var result = await _taskListService.AddTags(new TaskListBatchTagsModel
        {
            TaskIds = [arpId], TagIds = [2]
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Sites, Is.EquivalentTo(new[] { 100, 101 }));
            Assert.That(_updateCalls[0].EformId, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task AddTags_UnknownPlanningId_PartialFailure_MessageContainsCount()
    {
        var validArpId = await SeedTask([100], tagIds: [1]);
        const int unknownArpId = 999_999;

        var result = await _taskListService.AddTags(new TaskListBatchTagsModel
        {
            TaskIds = [unknownArpId, validArpId], TagIds = [2]
        });

        Assert.That(result.Success, Is.True, "one of two succeeded, so overall Success is true");
        Assert.That(result.Message, Does.Contain("1/2"));
        Assert.That(result.Message, Does.Contain("Task not found"));
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].Id, Is.EqualTo(validArpId));
    }

    // ------------------------------------------------------------------
    // RemoveTags
    // ------------------------------------------------------------------

    [Test]
    public async Task RemoveTags_RemovesOnlyGivenIds_LeavesOthers()
    {
        var arpId = await SeedTask([100], tagIds: [1, 2, 3]);

        var result = await _taskListService.RemoveTags(new TaskListBatchTagsModel
        {
            TaskIds = [arpId], TagIds = [2]
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].TagIds, Is.EquivalentTo(new[] { 1, 3 }));
    }

    [Test]
    public async Task RemoveTags_RemovingNonExistentId_IsNoOpForThatId()
    {
        var arpId = await SeedTask([100], tagIds: [1, 2]);

        var result = await _taskListService.RemoveTags(new TaskListBatchTagsModel
        {
            TaskIds = [arpId], TagIds = [999]
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].TagIds, Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    public async Task RemoveTags_RoundTripsSitesAndEformId_OtherFieldsUnchanged()
    {
        var arpId = await SeedTask([100, 101], tagIds: [1, 2], eformId: 7);

        var result = await _taskListService.RemoveTags(new TaskListBatchTagsModel
        {
            TaskIds = [arpId], TagIds = [1]
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Sites, Is.EquivalentTo(new[] { 100, 101 }));
            Assert.That(_updateCalls[0].EformId, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task RemoveTags_UnknownPlanningId_PartialFailure_MessageContainsCount()
    {
        var validArpId = await SeedTask([100], tagIds: [1, 2]);
        const int unknownArpId = 999_999;

        var result = await _taskListService.RemoveTags(new TaskListBatchTagsModel
        {
            TaskIds = [unknownArpId, validArpId], TagIds = [1]
        });

        Assert.That(result.Success, Is.True, "one of two succeeded, so overall Success is true");
        Assert.That(result.Message, Does.Contain("1/2"));
        Assert.That(result.Message, Does.Contain("Task not found"));
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].Id, Is.EqualTo(validArpId));
    }

    /// <summary>
    /// BuildUpdateModel feeds every batch operation into CalendarService.UpdateTask,
    /// which writes StartHour unconditionally. For a wizard task that has no
    /// CalendarConfiguration yet, falling back to 0 made any batch action silently
    /// create one at midnight -- moving an event the grid was rendering at 09:00, and
    /// stamping it with a real CreatedByUserId so the legacy-midnight repair can
    /// never reclaim it. The fallback must match the read default.
    /// </summary>
    [Test]
    public async Task AddTags_TaskWithoutConfiguration_RoundTripsNineToTenNotMidnight()
    {
        var arpId = await SeedTask([100], withCalendarConfiguration: false);

        var result = await _taskListService.AddTags(new TaskListBatchTagsModel
        {
            TaskIds = [arpId], TagIds = [7]
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].StartHour, Is.EqualTo(9.0));
        Assert.That(_updateCalls[0].Duration, Is.EqualTo(1.0));
    }
}
