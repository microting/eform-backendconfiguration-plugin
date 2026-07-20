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
/// Covers Task 4: the shared <c>BuildUpdateModel</c> delegation helper and the
/// three worker batch actions (Assign / Reassign / AddWorker) on
/// <c>BackendConfigurationTaskListService</c>.
///
/// The invariant under test (per spec) is that batch actions delegate to
/// <c>IBackendConfigurationCalendarService.UpdateTask</c> per task — never bulk
/// -update <c>AreaRulePlanning</c> rows directly — so redeploy/retraction/
/// compliance side effects happen exactly as for a single-task edit.
/// <c>UpdateTask</c>'s own field-by-field business logic (past-date guard,
/// "at least one worker" guard, TaskWizard delegation, compliance
/// relocation, ...) is already covered by <see cref="CalendarUpdateTaskScopeTests"/>
/// and friends. Here, <c>IBackendConfigurationCalendarService</c> is
/// substituted via NSubstitute so these tests isolate and verify exactly
/// what Task 4 adds: (1) <c>BuildUpdateModel</c> correctly loads an
/// AreaRulePlanning's current state (Sites in particular) from the REAL
/// Testcontainers-backed DB, and (2) each batch action's Sites-mutation +
/// partial-failure-aggregation logic. Standing up the real
/// <c>BackendConfigurationCalendarService</c> would additionally require a
/// real (non-mocked) <c>IBackendConfigurationTaskWizardService</c> — the
/// component that actually persists <c>PlanningSites</c> — which existing
/// calendar integration tests (e.g. CalendarUpdateTaskScopeTests) always mock
/// out too, so it would not add coverage of the Sites-persistence path
/// anyway; mocking one level higher (the calendar service interface) keeps
/// the fixture focused and fast while still exercising the real DB read path.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskListBatchWorkersTest : TestBaseSetup
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
        // CalendarUpdateTaskScopeTests / CalendarTaskListIndexTest).
        BackendConfigurationPnDbContext!.PlanningSites.RemoveRange(
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
    /// Planning → AreaRulePlanning(+PlanningSites) → CalendarConfiguration —
    /// the exact shape <c>BuildUpdateModel</c> reads. Returns the ARP Id.
    /// </summary>
    private async Task<int> SeedTask(IEnumerable<int> siteIds, bool status = true, int repeatType = 2)
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
            AreaId = area.Id, PropertyId = property.Id, EformId = 7, CreatedInGuide = true,
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
            RelatedEFormId = 7, Description = "Original description",
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

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await calConfig.Create(BackendConfigurationPnDbContext!);

        return arp.Id;
    }

    [Test]
    public async Task Assign_ReplacesSites()
    {
        var arpId = await SeedTask([100, 101]);

        var result = await _taskListService.Assign(new TaskListBatchAssignModel
        {
            TaskIds = [arpId], SiteId = 200
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Id, Is.EqualTo(arpId));
            Assert.That(_updateCalls[0].Scope, Is.EqualTo("all"));
            Assert.That(_updateCalls[0].Sites, Is.EquivalentTo(new[] { 200 }));
        });
    }

    [Test]
    public async Task Assign_RoundTripsDescriptionWorkerTagsDatesAndTranslations()
    {
        var arpId = await SeedTask([100]);

        var workerTag = new AreaRulePlanningWorkerTag
        {
            AreaRulePlanningId = arpId, TagId = 42,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await workerTag.Create(BackendConfigurationPnDbContext!);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.FirstAsync(x => x.Id == arpId);
        var seededAnchorWeekday = arp.StartDate!.Value.DayOfWeek;
        var planning = await ItemsPlanningPnDbContext!.Plannings.FirstAsync(x => x.Id == arp.ItemPlanningId);

        var result = await _taskListService.Assign(new TaskListBatchAssignModel
        {
            TaskIds = [arpId], SiteId = 200
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        var call = _updateCalls[0];
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);
        Assert.Multiple(() =>
        {
            // DescriptionHtml round-trips the seeded Planning.Description
            // verbatim — UpdateTask writes this unconditionally on every
            // "all"-scope edit, so a batch worker action must not blank it.
            Assert.That(call.DescriptionHtml, Is.EqualTo(planning.Description));
            // Seeded worker-tag TagIds round-trip unchanged.
            Assert.That(call.WorkerTagIds, Is.EquivalentTo(new[] { 42 }));
            // StartDate must land on the same weekday as the real series
            // anchor (so RepeatType==Week's arp.DayOfWeek write stays
            // correct) and must be at least tomorrow (so UpdateTask's
            // past-date guard never rejects the edit).
            Assert.That(call.StartDate.DayOfWeek, Is.EqualTo(seededAnchorWeekday));
            Assert.That(call.StartDate, Is.GreaterThanOrEqualTo(tomorrow));
            // OriginalDate must mirror StartDate's date component exactly —
            // this is what keeps UpdateTask's dateChanged flag false so the
            // TRUE series anchor is preserved via its DB re-fetch instead of
            // being relocated to our synthetic placeholder date.
            Assert.That(call.OriginalDate, Is.EqualTo(call.StartDate.ToString("yyyy-MM-dd")));
            // Translations round-trip: count and Description value.
            Assert.That(call.Translates, Has.Count.EqualTo(1));
            Assert.That(call.Translates[0].Description, Is.EqualTo("Task description"));
        });
    }

    [Test]
    public async Task AddWorker_AppendsSite()
    {
        var arpId = await SeedTask([100]);

        var result = await _taskListService.AddWorker(new TaskListBatchAssignModel
        {
            TaskIds = [arpId], SiteId = 200
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].Sites, Is.EquivalentTo(new[] { 100, 200 }));
    }

    [Test]
    public async Task AddWorker_AlreadyAssigned_IsNoOp_DoesNotCallUpdateTask()
    {
        var arpId = await SeedTask([100]);

        var result = await _taskListService.AddWorker(new TaskListBatchAssignModel
        {
            TaskIds = [arpId], SiteId = 100
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Is.Empty, "dedup no-op must not call UpdateTask at all");
    }

    [Test]
    public async Task Reassign_MovesOnlyMatchingFromSite_SkipsOthers()
    {
        var arpAssigned = await SeedTask([100]);
        var arpNotAssigned = await SeedTask([200]);

        var result = await _taskListService.Reassign(new TaskListBatchReassignModel
        {
            TaskIds = [arpAssigned, arpNotAssigned], FromSiteId = 100, ToSiteId = 300
        });

        Assert.That(result.Success, Is.True, result.Message);
        // Only the matching planning triggers an UpdateTask call; the
        // non-matching one is skipped (skip = success, no change).
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(_updateCalls[0].Id, Is.EqualTo(arpAssigned));
            Assert.That(_updateCalls[0].Sites, Is.EquivalentTo(new[] { 300 }));
            // Spec: "the result reports which tasks were moved" — the
            // success message must distinguish the one actually-moved task
            // from the one silently-skipped (not-assigned-to-"from") task.
            Assert.That(result.Message, Does.Contain("moved 1"));
            Assert.That(result.Message, Does.Contain("skipped 1"));
        });
    }

    [Test]
    public async Task Reassign_ToSiteWithStaleRemovedPlanningSite_ReAddsSite_AndLeavesNoTrackedPlanningSites()
    {
        // Regression for the "reassign strips assignment" bug. A task that was
        // previously reassigned leaves a soft-REMOVED PlanningSite row for the
        // site it moved AWAY from. Reassigning BACK to that site must re-add it.
        //
        // Root cause: BuildUpdateModel loaded PlanningSites on a TRACKING query
        // with an unfiltered .Include(x => x.PlanningSites), attaching the
        // removed row to the request-scoped DbContext that the calendar+wizard
        // services share. The downstream wizard then loads the same ARP with a
        // FILTERED include (.Where(non-removed)); EF relationship fixup re-adds
        // already-tracked entities to the navigation IGNORING that filter, so
        // the wizard's currentSiteIds contained the removed site, sitesToAdd
        // came out empty, and the PlanningSite was never re-created — the task
        // silently ended up with NO assignment (repro: 138->220 then 220->138
        // left both rows removed). The fix reads BuildUpdateModel AsNoTracking.
        //
        // UpdateTask is mocked here, so this locks the two observable guarantees
        // at this layer: (1) the Sites handed to UpdateTask is exactly the
        // reassigned set (the removed row is ignored as a source), and (2)
        // building the model leaves NO PlanningSite tracked in the shared
        // context — the precise pollution that corrupted the wizard's filtered
        // include downstream.
        var arpId = await SeedTask([100]);

        // Simulate the stale removed row a prior reassign (300 -> 100) left behind.
        var staleRemoved = new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
        {
            AreaRulePlanningsId = arpId, SiteId = 300,
            WorkflowState = Constants.WorkflowStates.Removed, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await staleRemoved.Create(BackendConfigurationPnDbContext!);

        // SeedTask/Create leave their entities tracked; detach everything so the
        // change-tracker assertion below measures ONLY what the service's read
        // path (BuildUpdateModel) attaches — exactly as a fresh request would.
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();

        var result = await _taskListService.Reassign(new TaskListBatchReassignModel
        {
            TaskIds = [arpId], FromSiteId = 100, ToSiteId = 300
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            // The reassigned set the wizard must persist: 300 replaces 100.
            Assert.That(_updateCalls[0].Sites, Is.EquivalentTo(new[] { 300 }));
            // The removed row must never leak into BuildUpdateModel's Sites source.
            Assert.That(_updateCalls[0].Sites, Does.Not.Contain(100));
            // Root-cause guard: the read path must not leave any PlanningSite
            // tracked in the shared context, or the wizard's filtered include is
            // corrupted and reassign-to-a-previously-removed-site loses the worker.
            Assert.That(
                BackendConfigurationPnDbContext.ChangeTracker
                    .Entries<Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite>()
                    .ToList(),
                Is.Empty,
                "BuildUpdateModel must read PlanningSites AsNoTracking so soft-removed rows "
                + "don't pollute the shared DbContext and corrupt the wizard's filtered include");
        });
    }

    [Test]
    public async Task Reassign_FromSiteEqualsToSite_NoOpButStillMatches()
    {
        var arpId = await SeedTask([100]);

        var result = await _taskListService.Reassign(new TaskListBatchReassignModel
        {
            TaskIds = [arpId], FromSiteId = 100, ToSiteId = 100
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].Sites, Is.EquivalentTo(new[] { 100 }));
    }

    [Test]
    public async Task Assign_UnknownPlanningId_PartialFailure_MessageContainsCount()
    {
        var validArpId = await SeedTask([100]);
        const int unknownArpId = 999_999;

        var result = await _taskListService.Assign(new TaskListBatchAssignModel
        {
            TaskIds = [unknownArpId, validArpId], SiteId = 200
        });

        Assert.That(result.Success, Is.True, "one of two succeeded, so overall Success is true");
        Assert.That(result.Message, Does.Contain("1/2"));
        Assert.That(result.Message, Does.Contain("Task not found"));
        // Only the valid planning reaches UpdateTask.
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].Id, Is.EqualTo(validArpId));
    }

    [Test]
    public async Task Assign_AllUnknownPlanningIds_FailsOutright()
    {
        var result = await _taskListService.Assign(new TaskListBatchAssignModel
        {
            TaskIds = [999_001, 999_002], SiteId = 200
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("0/2"));
        Assert.That(_updateCalls, Is.Empty);
    }

    [Test]
    public async Task Assign_EmptyTaskIds_SucceedsAsNoOp()
    {
        var result = await _taskListService.Assign(new TaskListBatchAssignModel
        {
            TaskIds = [], SiteId = 200
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Is.EqualTo("Tasks updated"));
        Assert.That(_updateCalls, Is.Empty);
    }

    [Test]
    public async Task Assign_PlanningNotCreatedInGuide_IsTreatedAsNotFound()
    {
        var arpId = await SeedTask([100]);
        // Flip CreatedInGuide off after seeding — BuildUpdateModel only
        // handles wizard-created (CreatedInGuide) rules.
        var rule = await BackendConfigurationPnDbContext!.AreaRules
            .FirstAsync(x => x.AreaRulesPlannings.Any(a => a.Id == arpId));
        rule.CreatedInGuide = false;
        await rule.Update(BackendConfigurationPnDbContext);

        var result = await _taskListService.Assign(new TaskListBatchAssignModel
        {
            TaskIds = [arpId], SiteId = 200
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Task not found"));
        Assert.That(_updateCalls, Is.Empty);
    }
}
