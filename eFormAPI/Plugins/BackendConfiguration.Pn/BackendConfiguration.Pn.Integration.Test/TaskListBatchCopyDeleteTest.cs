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
/// Covers Task 6: the two remaining <c>BackendConfigurationTaskListService</c>
/// batch actions, Copy and Delete.
///
/// Copy delegates to <c>IBackendConfigurationCalendarService.CreateTask</c>
/// (verified real signature: <c>Task&lt;OperationDataResult&lt;int&gt;&gt;</c>) with a
/// <c>CalendarTaskCreateRequestModel</c> built from the source task's full
/// current state (<c>BuildUpdateModel</c>) plus the caller's target
/// property/board/date. As verified against
/// <c>BackendConfigurationCalendarService.CreateTask</c>'s real body:
/// FolderId is left null (the target property auto-resolves/creates its own
/// "00. Logbøger" folder), Status is the numeric NotActive convention (2),
/// Sites is set to the caller's explicit <c>SiteId</c> — chosen from the
/// TARGET property in the copy dialog — and WorkerTagIds is always cleared,
/// since that explicit assignee is what satisfies CreateTask's own
/// "AtLeastOneWorkerMustBeAssigned" guard now, not a carried-over source
/// worker tag.
///
/// Delete delegates to
/// <c>IBackendConfigurationTaskWizardService.DeleteTaskDeferredRetraction</c>
/// — the batch/calendar variant that performs the same DB soft-deletes as
/// <c>DeleteTask</c> (Planning/AreaRuleTranslations/PlanningSites/AreaRule/
/// AreaRulePlanning/Compliances) SYNCHRONOUSLY, so the row is gone from the
/// next tasks/index read, but retracts every deployed SDK case fire-and-forget
/// instead of inline. The plain <c>DeleteTask</c> awaits <c>core.CaseDelete</c>
/// per case, which blocks for minutes in dev (no eform-core consumer) and would
/// hang the whole batch request; the deferred variant mirrors the fire-and
/// -forget shape <c>BackendConfigurationCompliancesService.UpdateFromCalendar</c>
/// already ships (PR #1049). Delete is guarded by a lightweight
/// <c>IsEligibleTaskAsync</c> check (mirrors <c>BuildUpdateModel</c>'s own
/// CreatedInGuide rule lookup) so it can only ever act on task-list-eligible
/// plannings, not arbitrary AreaRulePlanning ids.
///
/// Both <c>IBackendConfigurationCalendarService</c> and
/// <c>IBackendConfigurationTaskWizardService</c> are substituted via
/// NSubstitute (same isolation strategy as <see cref="TaskListBatchWorkersTest"/>)
/// so these tests verify exactly what Task 6 adds: the request-model
/// construction and per-task orchestration, against the REAL Testcontainers
/// -backed DB for the source-state reads.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskListBatchCopyDeleteTest : TestBaseSetup
{
    private IUserService _userService = null!;
    private IBackendConfigurationTaskWizardService _taskWizardService = null!;
    private IBackendConfigurationCalendarService _calendarService = null!;
    private IBackendConfigurationLocalizationService _localizationService = null!;
    private BackendConfigurationTaskListService _taskListService = null!;
    private List<CalendarTaskCreateRequestModel> _createCalls = null!;
    private List<int> _deleteCalls = null!;

    [SetUp]
    public async Task SetupTaskListService()
    {
        // FK-safe cleanup so each test starts fresh (mirrors
        // TaskListBatchWorkersTest / TaskListBatchEformTagsTest).
        BackendConfigurationPnDbContext!.PlanningSites.RemoveRange(
            BackendConfigurationPnDbContext.PlanningSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRulePlanningWorkerTags.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlanningWorkerTags);
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

        // PropertyWorkers FK-restricts on Property, so it must be cleared
        // before Properties below (added for the Copy SiteId/TargetProperty
        // validation guard — see SeedTargetProperty/SeedPropertyWorker).
        BackendConfigurationPnDbContext.PropertyWorkers.RemoveRange(
            BackendConfigurationPnDbContext.PropertyWorkers);
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

        // Echoes the key back, matching the plugin's resx-free convention
        // where GetString("Some English sentence") is itself the message.
        _localizationService = Substitute.For<IBackendConfigurationLocalizationService>();
        _localizationService.GetString(Arg.Any<string>())
            .Returns(callInfo => (string)callInfo[0]);

        _createCalls = [];
        _calendarService = Substitute.For<IBackendConfigurationCalendarService>();
        _calendarService.CreateTask(Arg.Do<CalendarTaskCreateRequestModel>(m => _createCalls.Add(m)))
            .Returns(Task.FromResult(new OperationDataResult<int>(true, "CalendarTaskCreatedSuccessfully", 1)));

        _deleteCalls = [];
        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        // Delete now delegates to the fire-and-forget-retraction variant so a
        // batch delete returns immediately instead of blocking on core.CaseDelete
        // (which hangs for minutes in dev). Record calls to THAT method.
        _taskWizardService.DeleteTaskDeferredRetraction(Arg.Do<int>(id => _deleteCalls.Add(id)))
            .Returns(Task.FromResult(new OperationResult(true, "TaskDeletedSuccessful")));

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
    /// Seeds Area → Property → AreaRule(+translation, CreatedInGuide) →
    /// Planning → AreaRulePlanning(+PlanningSites, +WorkerTags) →
    /// CalendarConfiguration — the exact shape <c>BuildUpdateModel</c> reads.
    /// Returns the ARP Id.
    /// </summary>
    private async Task<int> SeedTask(IEnumerable<int> siteIds, IEnumerable<int> workerTagIds = null,
        bool status = true, int repeatType = 2, int eformId = 7, bool createdInGuide = true,
        int? boardId = null)
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
            AreaId = area.Id, PropertyId = property.Id, EformId = eformId, CreatedInGuide = createdInGuide,
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

        foreach (var tagId in workerTagIds ?? [])
        {
            var workerTag = new AreaRulePlanningWorkerTag
            {
                AreaRulePlanningId = arp.Id, TagId = tagId,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await workerTag.Create(BackendConfigurationPnDbContext!);
        }

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0, BoardId = boardId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await calConfig.Create(BackendConfigurationPnDbContext!);

        return arp.Id;
    }

    /// <summary>
    /// Seeds a real Property row to stand in as a Copy TargetPropertyId.
    /// Copy's new SiteId/TargetProperty guard does a real (FK-backed)
    /// PropertyWorkers lookup, so — unlike the source-side SeedTask
    /// property, which was previously just an unrelated auto-id — the
    /// target property must actually exist in the DB.
    /// </summary>
    private async Task<int> SeedTargetProperty()
    {
        var property = new Property
        {
            Name = $"CopyTargetProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        return property.Id;
    }

    /// <summary>
    /// Links a worker (site) to a property via PropertyWorker — the same
    /// entity/shape Copy's new guard and EventDeployService's existing
    /// PropertyWorkers guard both query.
    /// </summary>
    private async Task SeedPropertyWorker(int propertyId, int workerId)
    {
        await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = propertyId, WorkerId = workerId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    // ---- Copy ----

    [Test]
    public async Task Copy_CallsCreateTask_WithTargetPropertyBoardAndInactiveStatus()
    {
        var arpId = await SeedTask([100], workerTagIds: [42]);
        var targetPropertyId = await SeedTargetProperty();
        await SeedPropertyWorker(targetPropertyId, 900);
        var startDate = DateTime.UtcNow.Date.AddDays(7);

        var result = await _taskListService.Copy(new TaskListBatchCopyModel
        {
            TaskIds = [arpId], TargetPropertyId = targetPropertyId, TargetBoardId = 777, StartDate = startDate,
            SiteId = 900
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_createCalls, Has.Count.EqualTo(1));
        var call = _createCalls[0];
        Assert.Multiple(() =>
        {
            Assert.That(call.PropertyId, Is.EqualTo(targetPropertyId));
            Assert.That(call.BoardId, Is.EqualTo(777));
            Assert.That(call.StartDate, Is.EqualTo(startDate));
            // TaskWizardStatuses.NotActive == 2.
            Assert.That(call.Status, Is.EqualTo(2));
            // Target property resolves its own default folder.
            Assert.That(call.FolderId, Is.Null);
        });
    }

    [Test]
    public async Task Copy_AssignsChosenSite_AndClearsWorkerTags()
    {
        var arpId = await SeedTask([100, 101], workerTagIds: [42, 43]);
        var targetPropertyId = await SeedTargetProperty();
        await SeedPropertyWorker(targetPropertyId, 900);

        var result = await _taskListService.Copy(new TaskListBatchCopyModel
        {
            TaskIds = [arpId], TargetPropertyId = targetPropertyId, TargetBoardId = 777,
            StartDate = DateTime.UtcNow.Date.AddDays(7), SiteId = 900
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_createCalls, Has.Count.EqualTo(1));
        var call = _createCalls[0];
        Assert.Multiple(() =>
        {
            // Sites is set to the caller's explicit target-property
            // assignee, not carried over from the source.
            Assert.That(call.Sites, Is.EquivalentTo(new[] { 900 }));
            // WorkerTagIds is always cleared — the explicit assignee above
            // satisfies CreateTask's "AtLeastOneWorkerMustBeAssigned" guard.
            Assert.That(call.WorkerTagIds, Is.Empty);
        });
    }

    [Test]
    public async Task Copy_RoundTripsEformTagsRepeatFieldsAndDescription()
    {
        var arpId = await SeedTask([100], workerTagIds: [42], eformId: 9);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.FirstAsync(x => x.Id == arpId);
        arp.RepeatType = 2;
        arp.RepeatEvery = 3;
        await arp.Update(BackendConfigurationPnDbContext);

        var planningTag = new AreaRulePlanningTag
        {
            AreaRulePlanningId = arpId, ItemPlanningTagId = 61,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planningTag.Create(BackendConfigurationPnDbContext!);

        var planning = await ItemsPlanningPnDbContext!.Plannings.FirstAsync(x => x.Id == arp.ItemPlanningId);
        var targetPropertyId = await SeedTargetProperty();
        await SeedPropertyWorker(targetPropertyId, 900);

        var result = await _taskListService.Copy(new TaskListBatchCopyModel
        {
            TaskIds = [arpId], TargetPropertyId = targetPropertyId, TargetBoardId = 777,
            StartDate = DateTime.UtcNow.Date.AddDays(7), SiteId = 900
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_createCalls, Has.Count.EqualTo(1));
        var call = _createCalls[0];
        Assert.Multiple(() =>
        {
            Assert.That(call.EformId, Is.EqualTo(9));
            Assert.That(call.TagIds, Is.EquivalentTo(new[] { 61 }));
            Assert.That(call.RepeatType, Is.EqualTo(2));
            Assert.That(call.RepeatEvery, Is.EqualTo(3));
            Assert.That(call.DescriptionHtml, Is.EqualTo(planning.Description));
            Assert.That(call.Translates, Has.Count.EqualTo(1));
            Assert.That(call.Translates[0].Description, Is.EqualTo("Task description"));
        });
    }

    [Test]
    public async Task Copy_DoesNotModifySourcePlanning()
    {
        var arpId = await SeedTask([100], workerTagIds: [42]);
        var sourceBefore = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == arpId);
        var targetPropertyId = await SeedTargetProperty();
        await SeedPropertyWorker(targetPropertyId, 900);

        var result = await _taskListService.Copy(new TaskListBatchCopyModel
        {
            TaskIds = [arpId], TargetPropertyId = targetPropertyId, TargetBoardId = 777,
            StartDate = DateTime.UtcNow.Date.AddDays(7), SiteId = 900
        });

        Assert.That(result.Success, Is.True, result.Message);
        var sourceAfter = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == arpId);
        Assert.Multiple(() =>
        {
            Assert.That(sourceAfter.PropertyId, Is.EqualTo(sourceBefore.PropertyId));
            Assert.That(sourceAfter.Status, Is.EqualTo(sourceBefore.Status));
            Assert.That(sourceAfter.StartDate, Is.EqualTo(sourceBefore.StartDate));
        });
        // CreateTask (mocked) is the only thing that would create the new
        // planning — no second AreaRulePlanning row was written by Copy
        // itself, since it only ever reads the source via BuildUpdateModel.
        var totalRows = await BackendConfigurationPnDbContext!.AreaRulePlannings.CountAsync();
        Assert.That(totalRows, Is.EqualTo(1));
    }

    [Test]
    public async Task Copy_UnknownPlanningId_PartialFailure_MessageContainsCount()
    {
        var validArpId = await SeedTask([100], workerTagIds: [42]);
        const int unknownArpId = 999_999;
        var targetPropertyId = await SeedTargetProperty();
        await SeedPropertyWorker(targetPropertyId, 900);

        var result = await _taskListService.Copy(new TaskListBatchCopyModel
        {
            TaskIds = [unknownArpId, validArpId], TargetPropertyId = targetPropertyId, TargetBoardId = 777,
            StartDate = DateTime.UtcNow.Date.AddDays(7), SiteId = 900
        });

        Assert.That(result.Success, Is.True, "one of two succeeded, so overall Success is true");
        Assert.That(result.Message, Does.Contain("1/2"));
        Assert.That(result.Message, Does.Contain("Task not found"));
        Assert.That(_createCalls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Copy_PlanningNotCreatedInGuide_IsTreatedAsNotFound()
    {
        var arpId = await SeedTask([100], workerTagIds: [42], createdInGuide: false);
        var targetPropertyId = await SeedTargetProperty();
        await SeedPropertyWorker(targetPropertyId, 900);

        var result = await _taskListService.Copy(new TaskListBatchCopyModel
        {
            TaskIds = [arpId], TargetPropertyId = targetPropertyId, TargetBoardId = 777,
            StartDate = DateTime.UtcNow.Date.AddDays(7), SiteId = 900
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Task not found"));
        Assert.That(_createCalls, Is.Empty);
    }

    [Test]
    public async Task Copy_EmptyTaskIds_SucceedsAsNoOp()
    {
        var targetPropertyId = await SeedTargetProperty();
        await SeedPropertyWorker(targetPropertyId, 900);

        var result = await _taskListService.Copy(new TaskListBatchCopyModel
        {
            TaskIds = [], TargetPropertyId = targetPropertyId, TargetBoardId = 777,
            StartDate = DateTime.UtcNow.Date.AddDays(7), SiteId = 900
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Is.EqualTo("Tasks copied"));
        Assert.That(_createCalls, Is.Empty);
    }

    [Test]
    public async Task Copy_SiteNotOnTargetProperty_FailsWholeBatch_WithoutCreateCalls()
    {
        // Seed the target property WITHOUT linking site 900 as a
        // PropertyWorker — the defense-in-depth guard must refuse the
        // whole batch before touching any task, even though the source
        // task itself is perfectly valid.
        var arpId = await SeedTask([100], workerTagIds: [42]);
        var targetPropertyId = await SeedTargetProperty();

        var result = await _taskListService.Copy(new TaskListBatchCopyModel
        {
            TaskIds = [arpId], TargetPropertyId = targetPropertyId, TargetBoardId = 777,
            StartDate = DateTime.UtcNow.Date.AddDays(7), SiteId = 900
        });

        Assert.That(result.Success, Is.False);
        Assert.That(_createCalls, Is.Empty);
    }

    // ---- Delete ----

    [Test]
    public async Task Delete_CallsTaskWizardDeferredRetraction_PerEligibleId()
    {
        var arpId1 = await SeedTask([100]);
        var arpId2 = await SeedTask([200]);

        var result = await _taskListService.Delete(new TaskListBatchRequestModel
        {
            TaskIds = [arpId1, arpId2]
        });

        Assert.That(result.Success, Is.True, result.Message);
        // Each eligible task is deleted via the deferred-retraction variant so
        // the batch never blocks on the external core.CaseDelete platform call.
        Assert.That(_deleteCalls, Is.EquivalentTo(new[] { arpId1, arpId2 }));
    }

    [Test]
    public async Task Delete_UnknownPlanningId_PartialFailure_MessageContainsCount()
    {
        var validArpId = await SeedTask([100]);
        const int unknownArpId = 999_999;

        var result = await _taskListService.Delete(new TaskListBatchRequestModel
        {
            TaskIds = [unknownArpId, validArpId]
        });

        Assert.That(result.Success, Is.True, "one of two succeeded, so overall Success is true");
        Assert.That(result.Message, Does.Contain("1/2"));
        Assert.That(result.Message, Does.Contain("Task not found"));
        Assert.That(_deleteCalls, Is.EquivalentTo(new[] { validArpId }));
    }

    [Test]
    public async Task Delete_AlreadyDeletedId_TreatedAsNotFound_DoesNotCallDeleteTask()
    {
        var arpId = await SeedTask([100]);
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.FirstAsync(x => x.Id == arpId);
        arp.WorkflowState = Constants.WorkflowStates.Removed;
        await arp.Update(BackendConfigurationPnDbContext);

        var result = await _taskListService.Delete(new TaskListBatchRequestModel
        {
            TaskIds = [arpId]
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Task not found"));
        Assert.That(_deleteCalls, Is.Empty);
    }

    [Test]
    public async Task Delete_PlanningNotCreatedInGuide_IsRefused_DoesNotCallDeleteTask()
    {
        // Server-side safety requirement: Delete must only ever act on
        // task-list-eligible plannings (CreatedInGuide) — not arbitrary
        // AreaRulePlanning ids that happen to exist but aren't actually
        // surfaced on the task-list page.
        var arpId = await SeedTask([100], createdInGuide: false);

        var result = await _taskListService.Delete(new TaskListBatchRequestModel
        {
            TaskIds = [arpId]
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Task not found"));
        Assert.That(_deleteCalls, Is.Empty);
    }

    [Test]
    public async Task Delete_EmptyTaskIds_SucceedsAsNoOp()
    {
        var result = await _taskListService.Delete(new TaskListBatchRequestModel
        {
            TaskIds = []
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Is.EqualTo("Tasks deleted"));
        Assert.That(_deleteCalls, Is.Empty);
    }

    [Test]
    public async Task Delete_AllUnknownPlanningIds_FailsOutright()
    {
        var result = await _taskListService.Delete(new TaskListBatchRequestModel
        {
            TaskIds = [999_001, 999_002]
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("0/2"));
        Assert.That(_deleteCalls, Is.Empty);
    }
}
