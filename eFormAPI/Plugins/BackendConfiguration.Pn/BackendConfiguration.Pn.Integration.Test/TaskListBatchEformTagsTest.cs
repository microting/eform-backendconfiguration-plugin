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
        // Compliances go first: they carry PropertyId/AreaId/PlanningId, so the
        // rows seeded by SetCompliance_DoesNotDeleteExistingComplianceRows must
        // not survive into a sibling test whose Property/Area are about to be
        // deleted (same ordering as CalendarComplianceMoveTests' SetUp).
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
            // #1122 added the change-start-date action, which needs the
            // retraction/backfill projections for its PREVIEW only. None of the
            // actions these fixtures exercise touches either, so substitutes
            // that are never called keep the constructor satisfied without
            // pulling an SDK core into fixtures that do not need one.
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
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
        bool withCalendarConfiguration = true, bool complianceEnabled = false)
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
            // false is the CLR default for the non-nullable bool column, so the
            // default argument leaves every pre-existing test's seed identical.
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

    // ------------------------------------------------------------------
    // SetCompliance
    // ------------------------------------------------------------------

    /// <summary>
    /// The flag is a plain overwrite, not a toggle: whatever the task had before,
    /// the batch value wins. Seeding the opposite of the requested value proves the
    /// captured model carries the caller's intent rather than the round-tripped
    /// database value.
    /// </summary>
    [Test]
    public async Task SetCompliance_EnablesCompliance()
    {
        var arpId = await SeedTask([100], complianceEnabled: false);

        var result = await _taskListService.SetCompliance(new TaskListBatchComplianceModel
        {
            TaskIds = [arpId], ComplianceEnabled = true
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Id, Is.EqualTo(arpId));
            Assert.That(_updateCalls[0].Scope, Is.EqualTo("all"));
            Assert.That(_updateCalls[0].ComplianceEnabled, Is.True);
        });
    }

    /// <summary>
    /// The "off" direction needs its own coverage because a batch action written as
    /// a conditional set (only write when true) would still pass the enable test
    /// while silently refusing to ever hide an overdue task from the app again.
    /// </summary>
    [Test]
    public async Task SetCompliance_DisablesCompliance()
    {
        var arpId = await SeedTask([100], complianceEnabled: true);

        var result = await _taskListService.SetCompliance(new TaskListBatchComplianceModel
        {
            TaskIds = [arpId], ComplianceEnabled = false
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Id, Is.EqualTo(arpId));
            Assert.That(_updateCalls[0].Scope, Is.EqualTo("all"));
            Assert.That(_updateCalls[0].ComplianceEnabled, Is.False);
        });
    }

    [Test]
    public async Task SetCompliance_UnknownPlanningId_PartialFailure_MessageContainsCount()
    {
        var validArpId = await SeedTask([100]);
        const int unknownArpId = 999_999;

        var result = await _taskListService.SetCompliance(new TaskListBatchComplianceModel
        {
            TaskIds = [unknownArpId, validArpId], ComplianceEnabled = true
        });

        Assert.That(result.Success, Is.True, "one of two succeeded, so overall Success is true");
        Assert.That(result.Message, Does.Contain("1/2"));
        Assert.That(result.Message, Does.Contain("Task not found"));
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].Id, Is.EqualTo(validArpId));
    }

    /// <summary>
    /// The core contract of the batch action. The single-task calendar edit modal
    /// forces the status control to active whenever the admin picks either overdue
    /// option, so a naive port of that behaviour would reactivate — and redeploy the
    /// cases of — every dormant task caught in a 40-row selection. BuildUpdateModel
    /// round-trips <c>arp.Status</c> (mapped as 1 = active, 2 = inactive) and
    /// SetCompliance must leave it alone, so an inactive task stays inactive.
    /// </summary>
    [Test]
    public async Task SetCompliance_InactiveTask_LeavesStatusInactive()
    {
        var arpId = await SeedTask([100], status: false);

        var result = await _taskListService.SetCompliance(new TaskListBatchComplianceModel
        {
            TaskIds = [arpId], ComplianceEnabled = true
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Status, Is.EqualTo(2), "inactive must survive the batch action");
            Assert.That(_updateCalls[0].ComplianceEnabled, Is.True);
        });
    }

    /// <summary>
    /// Counterpart to the inactive case: guards against a "fix" that hard-codes the
    /// status to inactive, which would pass the test above while deactivating every
    /// live task the admin touched.
    /// </summary>
    [Test]
    public async Task SetCompliance_ActiveTask_LeavesStatusActive()
    {
        var arpId = await SeedTask([100], status: true);

        var result = await _taskListService.SetCompliance(new TaskListBatchComplianceModel
        {
            TaskIds = [arpId], ComplianceEnabled = true
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Status, Is.EqualTo(1), "active must survive the batch action");
            Assert.That(_updateCalls[0].ComplianceEnabled, Is.True);
        });
    }

    [Test]
    public async Task SetCompliance_RoundTripsSitesAndTags_OtherFieldsUnchanged()
    {
        var arpId = await SeedTask([100, 101], tagIds: [5], eformId: 7);

        var result = await _taskListService.SetCompliance(new TaskListBatchComplianceModel
        {
            TaskIds = [arpId], ComplianceEnabled = true
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            // UpdateTask writes the whole model back, so anything SetCompliance
            // does not deliberately mutate has to arrive untouched or the batch
            // action becomes a silent wipe of assignees, tags or the eForm.
            Assert.That(_updateCalls[0].Sites, Is.EquivalentTo(new[] { 100, 101 }));
            Assert.That(_updateCalls[0].TagIds, Is.EquivalentTo(new[] { 5 }));
            Assert.That(_updateCalls[0].EformId, Is.EqualTo(7));
        });
    }

    /// <summary>
    /// RunPerTask walks the ids sequentially, one UpdateTask per task — the batch is
    /// not collapsed into a single call, and no id is skipped once an earlier one has
    /// been handled.
    /// </summary>
    [Test]
    public async Task SetCompliance_MultipleTasks_EachGetsItsOwnUpdateInOrder()
    {
        var firstArpId = await SeedTask([100]);
        var secondArpId = await SeedTask([101]);

        var result = await _taskListService.SetCompliance(new TaskListBatchComplianceModel
        {
            TaskIds = [firstArpId, secondArpId], ComplianceEnabled = true
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Id, Is.EqualTo(firstArpId));
            Assert.That(_updateCalls[1].Id, Is.EqualTo(secondArpId));
            Assert.That(_updateCalls[0].ComplianceEnabled, Is.True);
            Assert.That(_updateCalls[1].ComplianceEnabled, Is.True);
        });
    }

    /// <summary>
    /// Issue #1124 binds the batch action to "no eager cleanup of already-overdue
    /// rows — the batch persists the flag and nothing else". The older
    /// <c>BackendConfigurationAreaRulePlanningsServiceHelper</c> path answers a
    /// compliance change by deleting the planning's <c>Compliance</c> rows and
    /// recomputing <c>Property.ComplianceStatus</c>; #1124 explicitly rejects
    /// that, because an admin flipping a flag on a 40-row selection has not asked
    /// for the recorded history of overdue occurrences to be destroyed.
    ///
    /// <c>IBackendConfigurationCalendarService</c> is substituted in this fixture,
    /// so nothing downstream of <c>UpdateTask</c> executes — which is exactly what
    /// gives this test its value: the only way these rows can disappear is if a
    /// <c>Compliances.RemoveRange(...)</c> is written directly into
    /// <c>SetCompliance</c>, and that is the regression being guarded.
    ///
    /// Both a still-open (Created) and an already-retracted (Removed) row are
    /// seeded, so a blanket delete and a "only the active ones" variant are both
    /// caught, and WorkflowState is asserted as well as the count so a soft-delete
    /// (flipping Created to Removed in place) cannot pass either.
    /// </summary>
    [Test]
    public async Task SetCompliance_DoesNotTouchExistingComplianceRows()
    {
        var arpId = await SeedTask([100], complianceEnabled: true);
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .SingleAsync(x => x.Id == arpId);

        var openDeadline = DateTime.UtcNow.Date.AddDays(-14);
        var retractedDeadline = DateTime.UtcNow.Date.AddDays(-7);
        foreach (var (deadline, workflowState) in new[]
                 {
                     (openDeadline, Constants.WorkflowStates.Created),
                     (retractedDeadline, Constants.WorkflowStates.Removed)
                 })
        {
            await BackendConfigurationPnDbContext.Compliances.AddAsync(new Compliance
            {
                PlanningId = arp.ItemPlanningId,
                PropertyId = arp.PropertyId,
                AreaId = arp.AreaId,
                // Deadline is the occurrence date; both are in the past, i.e. the
                // "already overdue" rows the eager-cleanup path would have culled.
                Deadline = DateTime.SpecifyKind(deadline, DateTimeKind.Utc),
                StartDate = DateTime.SpecifyKind(deadline.AddDays(-7), DateTimeKind.Utc),
                MicrotingSdkCaseId = 0,
                MicrotingSdkeFormId = 0,
                WorkflowState = workflowState,
                CreatedByUserId = 1,
                UpdatedByUserId = 1
            });
        }

        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Turning compliance OFF is the direction an eager cleanup would fire on.
        var result = await _taskListService.SetCompliance(new TaskListBatchComplianceModel
        {
            TaskIds = [arpId], ComplianceEnabled = false
        });

        Assert.That(result.Success, Is.True, result.Message);

        // AsNoTracking so the assertion reads the DB rows rather than the
        // instances this test added to the change tracker.
        var rows = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking()
            .Where(x => x.PlanningId == arp.ItemPlanningId)
            .OrderBy(x => x.Deadline)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2),
                "SetCompliance must persist the flag and nothing else — no Compliance row may be deleted");
            Assert.That(rows[0].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "the open overdue row must not be soft-deleted either");
            Assert.That(rows[1].WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the already-retracted row must be left exactly as it was");
            Assert.That(_updateCalls, Has.Count.EqualTo(1));
            Assert.That(_updateCalls[0].ComplianceEnabled, Is.False,
                "the flag itself still has to be written");
        });
    }
}
