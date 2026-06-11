using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
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

[TestFixture]
public class CalendarBoardDeleteCascadeTests : TestBaseSetup
{
    private BackendConfigurationCalendarService _service = null!;
    private IBackendConfigurationTaskWizardService _taskWizardService = null!;

    private BackendConfigurationCalendarService BuildService()
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var coreHelper = Substitute.For<IEFormCoreService>();
        _taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        _taskWizardService.DeleteTask(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(true)));

        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, _taskWizardService,
            NullLogger<BackendConfigurationCalendarService>.Instance);
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

        var result = await _service.DeleteBoard(boardId);

        Assert.That(result.Success, Is.True);

        // Board itself soft-deleted.
        var board = await BackendConfigurationPnDbContext!.CalendarBoards
            .FirstAsync(x => x.Id == boardId);
        Assert.That(board.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        // Every CalendarConfiguration on the board soft-deleted.
        var liveConfigs = await BackendConfigurationPnDbContext.CalendarConfigurations
            .Where(x => x.BoardId == boardId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .CountAsync();
        Assert.That(liveConfigs, Is.EqualTo(0));

        // AreaRulePlanning removal delegated once per distinct event.
        foreach (var arpId in arpIds)
        {
            await _taskWizardService.Received(1).DeleteTask(arpId);
        }
    }

    [Test]
    public async Task DeleteBoard_DoesNotTouchEventsOnOtherBoards()
    {
        _service = BuildService();
        var (boardId, _) = await SeedBoardWithEvents(1);
        var (otherBoardId, otherArpIds) = await SeedBoardWithEvents(1);

        await _service.DeleteBoard(boardId);

        // The other board and its event are untouched.
        var otherBoard = await BackendConfigurationPnDbContext!.CalendarBoards
            .FirstAsync(x => x.Id == otherBoardId);
        Assert.That(otherBoard.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));

        var otherConfigsLive = await BackendConfigurationPnDbContext.CalendarConfigurations
            .Where(x => x.BoardId == otherBoardId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .CountAsync();
        Assert.That(otherConfigsLive, Is.EqualTo(1));

        await _taskWizardService.DidNotReceive().DeleteTask(otherArpIds[0]);
    }

    [Test]
    public async Task DeleteBoard_WhenEventDeletionFails_AbortsWithBoardIntact()
    {
        _service = BuildService();

        // Re-stub DeleteTask to return failure so the per-event series-delete aborts.
        _taskWizardService.DeleteTask(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(false, "boom")));

        var (boardId, _) = await SeedBoardWithEvents(1);

        var result = await _service.DeleteBoard(boardId);

        Assert.That(result.Success, Is.False);

        // Board must NOT be soft-deleted — it should be left intact / recoverable.
        var board = await BackendConfigurationPnDbContext!.CalendarBoards
            .FirstAsync(x => x.Id == boardId);
        Assert.That(board.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
    }
}
