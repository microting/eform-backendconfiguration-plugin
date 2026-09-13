using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// DELETE boards/{id} — the board cascade (#1238).
///
/// The wizard is substituted here on purpose: these tests are about WHICH
/// wizard entry point the cascade uses and about what survives a failing
/// series, not about what the wizard itself deletes (that is the wizard's own
/// fixtures). The two entry points differ only in when the external
/// core.CaseDelete calls run — inline (DeleteTask) or fire-and-forget after
/// every DB row is soft-deleted (DeleteTaskDeferredRetraction) — so the
/// substitute models the inline one as a call that does not come back for
/// minutes, which is exactly the shape that made it unusable inside a request.
/// </summary>
[TestFixture]
public class CalendarBoardDeleteCascadeTests : TestBaseSetup
{
    /// <summary>How long a DeleteBoard call may take before it counts as blocked.
    /// Generous: the cascade's own DB work in these fixtures is milliseconds, and
    /// the only thing that could push it past this is awaiting the simulated
    /// inline retraction below.</summary>
    private static readonly TimeSpan DeleteBoardBudget = TimeSpan.FromSeconds(30);

    /// <summary>Stands in for the inline device retraction: a call that does not
    /// return for far longer than any request may last.</summary>
    private static readonly TimeSpan InlineRetractionStall = TimeSpan.FromMinutes(10);

    private BackendConfigurationCalendarService _service = null!;
    private IBackendConfigurationTaskWizardService _taskWizardService = null!;

    private BackendConfigurationCalendarService BuildService()
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var coreHelper = Substitute.For<IEFormCoreService>();
        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();

        // The deferred variant returns as soon as its DB soft-deletes are done.
        _taskWizardService.DeleteTaskDeferredRetraction(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(true)));

        // The inline variant awaits one core.CaseDelete per deployed case per
        // assignee. Modelled as a call that never returns in time, so a cascade
        // that reverts to it fails DeleteBoardWithinBudget instead of quietly
        // passing.
        _taskWizardService.DeleteTask(Arg.Any<int>())
            .Returns(_ => StalledRetraction());

        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, _taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper));
    }

    private static async Task<OperationResult> StalledRetraction()
    {
        await Task.Delay(InlineRetractionStall);
        return new OperationResult(true);
    }

    /// <summary>Calls DeleteBoard and turns "never came back" into a failed
    /// assertion rather than a hung test run.</summary>
    private async Task<OperationResult> DeleteBoardWithinBudget(int boardId)
    {
        var deleteBoard = _service.DeleteBoard(boardId);
        var first = await Task.WhenAny(deleteBoard, Task.Delay(DeleteBoardBudget));
        Assert.That(first, Is.SameAs(deleteBoard),
            $"DeleteBoard did not return within {DeleteBoardBudget.TotalSeconds}s — the request is awaiting device retraction.");
        return await deleteBoard;
    }

    /// <summary>Seeds a board plus <paramref name="eventCount"/> events placed on it.
    /// Returns (boardId, list of AreaRulePlanning ids).</summary>
    private async Task<(int boardId, List<int> arpIds)> SeedBoardWithEvents(int eventCount)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"BoardCascadeTest-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var board = new CalendarBoard
        {
            Name = "Board A", Color = "#112233", PropertyId = property.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarBoards.AddAsync(board);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var arpIds = new List<int>();
        for (var i = 0; i < eventCount; i++)
        {
            var areaRule = new AreaRule
            {
                AreaId = area.Id, PropertyId = property.Id, EformId = 0,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
            await BackendConfigurationPnDbContext.SaveChangesAsync();

            var planning = new Planning
            {
                Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
                StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                RelatedEFormId = 0, WorkflowState = Constants.WorkflowStates.Created,
                CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
            await ItemsPlanningPnDbContext.SaveChangesAsync();

            var arp = new AreaRulePlanning
            {
                AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
                ItemPlanningId = planning.Id,
                StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = true, RepeatType = 2, RepeatEvery = 1,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
            await BackendConfigurationPnDbContext.SaveChangesAsync();

            var calConfig = new CalendarConfiguration
            {
                AreaRulePlanningId = arp.Id, BoardId = board.Id, StartHour = 9.0, Duration = 1.0,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(calConfig);
            await BackendConfigurationPnDbContext.SaveChangesAsync();

            arpIds.Add(arp.Id);
        }

        return (board.Id, arpIds);
    }

    /// <summary>Adds a moved-occurrence exception to an event, so the tests can
    /// tell whether the calendar-side rows of a series were touched.</summary>
    private async Task<int> SeedOccurrenceException(int arpId)
    {
        var exception = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc),
            IsDeleted = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(exception);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return exception.Id;
    }

    private async Task<int> LiveConfigCount(int boardId) =>
        await BackendConfigurationPnDbContext!.CalendarConfigurations
            .Where(x => x.BoardId == boardId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .CountAsync();

    private async Task<string> BoardWorkflowState(int boardId) =>
        (await BackendConfigurationPnDbContext!.CalendarBoards.FirstAsync(x => x.Id == boardId))
        .WorkflowState;

    [Test]
    public async Task GetBoardEventCount_ReturnsDistinctEventCount()
    {
        _service = BuildService();
        var (boardId, _) = await SeedBoardWithEvents(3);

        var result = await _service.GetBoardEventCount(boardId);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Model, Is.EqualTo(3));
    }

    [Test]
    public async Task DeleteBoard_CascadeDeletesEvents_AndDelegatesEachAreaRulePlanning()
    {
        _service = BuildService();
        var (boardId, arpIds) = await SeedBoardWithEvents(2);
        var exceptionId = await SeedOccurrenceException(arpIds[0]);

        var result = await DeleteBoardWithinBudget(boardId);

        Assert.That(result.Success, Is.True);

        // Board itself soft-deleted.
        Assert.That(await BoardWorkflowState(boardId), Is.EqualTo(Constants.WorkflowStates.Removed));

        // Every CalendarConfiguration on the board soft-deleted.
        Assert.That(await LiveConfigCount(boardId), Is.EqualTo(0));

        // The calendar-side occurrence exceptions go too. Paired with the
        // survives-a-failure assertion below, this pins the cleanup loop from both
        // sides: without it, removing the loop altogether would keep the fixture green.
        var occurrenceException = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .FirstAsync(x => x.Id == exceptionId);
        Assert.That(occurrenceException.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        // AreaRulePlanning removal delegated once per distinct event.
        foreach (var arpId in arpIds)
        {
            await _taskWizardService.Received(1).DeleteTaskDeferredRetraction(arpId);
        }
    }

    /// <summary>
    /// #1238 acceptance: the request must not await device retraction, and a board
    /// with many deployed occurrences must not be able to hang it. The substitute's
    /// inline variant stalls for ten minutes; the call still has to come back inside
    /// the budget, which it can only do by never entering that path.
    /// </summary>
    [Test]
    public async Task DeleteBoard_ReturnsWithoutAwaitingInlineDeviceRetraction()
    {
        _service = BuildService();
        var (boardId, arpIds) = await SeedBoardWithEvents(3);

        var result = await DeleteBoardWithinBudget(boardId);

        Assert.That(result.Success, Is.True);
        await _taskWizardService.DidNotReceive().DeleteTask(Arg.Any<int>());
        foreach (var arpId in arpIds)
        {
            await _taskWizardService.Received(1).DeleteTaskDeferredRetraction(arpId);
        }
    }

    /// <summary>Empty-path cover: a board with no events is simply removed, and
    /// nothing is delegated to the wizard at all. Note this one does NOT pin #1238 —
    /// with zero events the cascade loop never runs, so the pre-#1238 code passes it
    /// too. It is here so the empty case keeps working, not as a regression guard.</summary>
    [Test]
    public async Task DeleteBoard_WithNoEvents_RemovesBoardAndDelegatesNothing()
    {
        _service = BuildService();
        var (boardId, _) = await SeedBoardWithEvents(0);

        var result = await DeleteBoardWithinBudget(boardId);

        Assert.That(result.Success, Is.True);
        Assert.That(await BoardWorkflowState(boardId), Is.EqualTo(Constants.WorkflowStates.Removed));
        await _taskWizardService.DidNotReceive().DeleteTaskDeferredRetraction(Arg.Any<int>());
        await _taskWizardService.DidNotReceive().DeleteTask(Arg.Any<int>());
    }

    [Test]
    public async Task DeleteBoard_DoesNotTouchEventsOnOtherBoards()
    {
        _service = BuildService();
        var (boardId, _) = await SeedBoardWithEvents(1);
        var (otherBoardId, otherArpIds) = await SeedBoardWithEvents(1);

        await DeleteBoardWithinBudget(boardId);

        // The other board and its event are untouched.
        Assert.That(await BoardWorkflowState(otherBoardId),
            Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(await LiveConfigCount(otherBoardId), Is.EqualTo(1));

        await _taskWizardService.DidNotReceive().DeleteTaskDeferredRetraction(otherArpIds[0]);
    }

    /// <summary>
    /// The recoverable-on-failure property. A failing series aborts the cascade
    /// before the board is touched, and leaves that series whole — including its
    /// calendar-side rows, which the cascade now removes only after the wizard has
    /// succeeded. So the board still lists the event, and the caller is told the
    /// delete failed while it is still waiting for the answer.
    /// </summary>
    [Test]
    public async Task DeleteBoard_WhenEventDeletionFails_LeavesBoardAndSeriesIntact()
    {
        _service = BuildService();
        _taskWizardService.DeleteTaskDeferredRetraction(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(false, "boom")));

        var (boardId, arpIds) = await SeedBoardWithEvents(1);
        var exceptionId = await SeedOccurrenceException(arpIds[0]);

        var result = await DeleteBoardWithinBudget(boardId);

        Assert.That(result.Success, Is.False);

        // Board must NOT be soft-deleted — it should be left intact / recoverable.
        Assert.That(await BoardWorkflowState(boardId),
            Is.Not.EqualTo(Constants.WorkflowStates.Removed));

        // The failing series keeps its calendar-side rows, so the event is still on
        // the board and a re-issued delete picks it up again instead of leaving an
        // AreaRulePlanning that nothing points at.
        Assert.That(await LiveConfigCount(boardId), Is.EqualTo(1));

        var occurrenceException = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions
            .FirstAsync(x => x.Id == exceptionId);
        Assert.That(occurrenceException.WorkflowState,
            Is.Not.EqualTo(Constants.WorkflowStates.Removed));
    }

    /// <summary>
    /// A mid-cascade failure stops at the failing series: the ones already deleted
    /// stay deleted, everything after it is untouched, and the board survives.
    /// Which event fails is decided by call order rather than by id, so the test
    /// does not depend on the order the cascade happens to enumerate them in.
    /// </summary>
    [Test]
    public async Task DeleteBoard_AbortsAtFailingSeries_KeepingTheRestAndTheBoard()
    {
        _service = BuildService();
        var calls = 0;
        _taskWizardService.DeleteTaskDeferredRetraction(Arg.Any<int>())
            .Returns(_ =>
            {
                calls++;
                return Task.FromResult(calls == 1
                    ? new OperationResult(true)
                    : new OperationResult(false, "boom"));
            });

        var (boardId, _) = await SeedBoardWithEvents(3);

        var result = await DeleteBoardWithinBudget(boardId);

        Assert.That(result.Success, Is.False);
        Assert.That(calls, Is.EqualTo(2), "the cascade must stop at the first failing series");

        // One series deleted, the other two still on a board that still exists.
        Assert.That(await LiveConfigCount(boardId), Is.EqualTo(2));
        Assert.That(await BoardWorkflowState(boardId),
            Is.Not.EqualTo(Constants.WorkflowStates.Removed));
    }

    /// <summary>
    /// The retry property, exercised over genuinely PARTIAL state. The first attempt
    /// must get one series out of two through before it fails, so the retry runs
    /// against a board that has one live CalendarConfiguration left; the retry then
    /// has to delegate exactly ONCE. That count is the whole point: the only thing
    /// stopping the retry from deleting the first series a second time is that its
    /// CalendarConfiguration is gone, and a stub that fails on call #1 (as this test
    /// used to do) never creates that state and never checks it.
    /// </summary>
    [Test]
    public async Task DeleteBoard_AfterPartialFailure_RetriesOnlyTheRemainingSeries()
    {
        _service = BuildService();
        var calls = 0;
        _taskWizardService.DeleteTaskDeferredRetraction(Arg.Any<int>())
            .Returns(_ =>
            {
                calls++;
                return Task.FromResult(calls == 1
                    ? new OperationResult(true)
                    : new OperationResult(false, "boom"));
            });

        var (boardId, arpIds) = await SeedBoardWithEvents(2);

        var firstAttempt = await DeleteBoardWithinBudget(boardId);
        Assert.That(firstAttempt.Success, Is.False);
        Assert.That(calls, Is.EqualTo(2), "the first attempt must reach the second series");

        // Series #1 is gone from the board, series #2 is still on it.
        Assert.That(await LiveConfigCount(boardId), Is.EqualTo(1));
        Assert.That(await BoardWorkflowState(boardId),
            Is.Not.EqualTo(Constants.WorkflowStates.Removed));

        // Which id survived depends on enumeration order, so read it rather than assume.
        var remainingArpId = await BackendConfigurationPnDbContext!.CalendarConfigurations
            .Where(x => x.BoardId == boardId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.AreaRulePlanningId)
            .SingleAsync();
        var deletedArpId = arpIds.Single(id => id != remainingArpId);

        var callsAfterFirstAttempt = calls;
        _taskWizardService.DeleteTaskDeferredRetraction(Arg.Any<int>())
            .Returns(_ =>
            {
                calls++;
                return Task.FromResult(new OperationResult(true));
            });

        var retry = await DeleteBoardWithinBudget(boardId);

        Assert.That(retry.Success, Is.True);
        Assert.That(calls - callsAfterFirstAttempt, Is.EqualTo(1),
            "the retry must delegate only the series that is still on the board");
        Assert.That(await BoardWorkflowState(boardId), Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(await LiveConfigCount(boardId), Is.EqualTo(0));

        // Across both attempts: the failing series was delegated twice (once per
        // attempt), the one that succeeded on attempt 1 was never delegated again.
        await _taskWizardService.Received(2).DeleteTaskDeferredRetraction(remainingArpId);
        await _taskWizardService.Received(1).DeleteTaskDeferredRetraction(deletedArpId);
    }

    /// <summary>
    /// An AreaRulePlanning can be removed by a path that does not clear its
    /// CalendarConfiguration — BackendConfigurationTaskListService.Delete (task-list
    /// batch delete) and DELETE /task-wizard/{id} both do exactly that — leaving a
    /// live CalendarConfiguration pointing at a Removed planning. DeleteBoard still
    /// collects that row, and the wizard can only answer "task not found" for it, so
    /// if that answer aborted the cascade the board would be undeletable forever:
    /// nothing else reaps the stale row, so every retry would fail identically.
    ///
    /// The cascade must instead recognise the already-removed planning, skip the
    /// wizard entirely and clear the calendar-side rows.
    /// </summary>
    [Test]
    public async Task DeleteBoard_WithConfigurationForAlreadyRemovedPlanning_ClearsItAndRemovesBoard()
    {
        _service = BuildService();
        var (boardId, arpIds) = await SeedBoardWithEvents(1);
        var exceptionId = await SeedOccurrenceException(arpIds[0]);

        // The planning goes; its CalendarConfiguration deliberately stays, which is
        // exactly what those two delete paths leave behind.
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .FirstAsync(x => x.Id == arpIds[0]);
        arp.UpdatedByUserId = 1;
        await arp.Delete(BackendConfigurationPnDbContext);
        Assert.That(await LiveConfigCount(boardId), Is.EqualTo(1),
            "the stale CalendarConfiguration is the precondition under test");

        // Were the wizard consulted at all, this is the answer it would give.
        _taskWizardService.DeleteTaskDeferredRetraction(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(false, "Task not found")));

        var result = await DeleteBoardWithinBudget(boardId);

        Assert.That(result.Success, Is.True);
        await _taskWizardService.DidNotReceive().DeleteTaskDeferredRetraction(Arg.Any<int>());
        await _taskWizardService.DidNotReceive().DeleteTask(Arg.Any<int>());

        Assert.That(await LiveConfigCount(boardId), Is.EqualTo(0));
        Assert.That(await BoardWorkflowState(boardId), Is.EqualTo(Constants.WorkflowStates.Removed));

        var occurrenceException = await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .FirstAsync(x => x.Id == exceptionId);
        Assert.That(occurrenceException.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    /// <summary>
    /// The single-event counterpart of the test above: a scope "all" delete against a
    /// planning that is already removed must be idempotent — clear the stale
    /// CalendarConfiguration and report success — rather than error out and leave the
    /// row behind. The stale row does not render on the calendar (both GetTasksForWeek
    /// producers join through live AreaRulePlannings), so this is about the row being
    /// clearable at all, not about a visible phantom event.
    /// </summary>
    [Test]
    public async Task DeleteTask_WhenPlanningAlreadyRemoved_ClearsStaleCalendarConfiguration()
    {
        _service = BuildService();
        var (boardId, arpIds) = await SeedBoardWithEvents(1);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .FirstAsync(x => x.Id == arpIds[0]);
        arp.UpdatedByUserId = 1;
        await arp.Delete(BackendConfigurationPnDbContext);

        _taskWizardService.DeleteTask(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(false, "Task not found")));

        var result = await _service.DeleteTask(new CalendarTaskDeleteRequestModel { Id = arpIds[0] });

        Assert.That(result.Success, Is.True);
        await _taskWizardService.DidNotReceive().DeleteTask(Arg.Any<int>());
        Assert.That(await LiveConfigCount(boardId), Is.EqualTo(0));
    }
}
