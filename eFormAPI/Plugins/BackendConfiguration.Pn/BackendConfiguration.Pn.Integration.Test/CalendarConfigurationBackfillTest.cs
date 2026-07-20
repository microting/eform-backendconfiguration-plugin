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

using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.CalendarConfigurationBackfillService;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Focused coverage for <c>CalendarConfigurationBackfillService.RunIfNeededAsync</c>,
/// the idempotent startup backfill that attaches a <c>CalendarConfiguration</c>
/// (board link) to every non-removed <c>AreaRulePlanning</c> whose <c>AreaRule</c>
/// has <c>CreatedInGuide == true</c> and does not already have one. Exercises: the
/// happy path onto an existing board, auto-creation of a "Default" board when the
/// property has none, idempotency against already-configured plannings, a second
/// no-op run, and the <c>CreatedInGuide</c> filter that excludes non-wizard
/// (area-rule-authored) plannings from the backfill.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarConfigurationBackfillTest : TestBaseSetup
{
    private CalendarConfigurationBackfillService _sut = null!;

    [SetUp]
    public async Task SetUpBackfill()
    {
        // FK-safe clean of the rows this fixture writes, mirroring
        // CalendarTaskListIndexTest's ordering (children before parents).
        BackendConfigurationPnDbContext!.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarBoards.RemoveRange(
            BackendConfigurationPnDbContext.CalendarBoards);
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

        _sut = new CalendarConfigurationBackfillService(
            BackendConfigurationPnDbContext!,
            NullLogger<CalendarConfigurationBackfillService>.Instance);
    }

    /// <summary>
    /// Seeds a fresh Property + Area pair using the real entities'
    /// <c>.Create(BackendConfigurationPnDbContext!)</c>, mirroring
    /// <c>CalendarTaskListIndexTest.SeedSeries</c>/<c>CalendarBoardDeleteCascadeTests
    /// .SeedBoardWithEvents</c>. AreaRule/AreaRulePlanning carry FK columns
    /// (PropertyId/AreaId) enforced against these rows.
    /// </summary>
    private async Task<Property> SeedProperty()
    {
        var property = new Property
        {
            Name = $"CalendarBackfillProp-{Guid.NewGuid()}",
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        return property;
    }

    private async Task<Area> SeedArea()
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1,
            ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);
        return area;
    }

    /// <summary>
    /// Seeds one AreaRule + AreaRulePlanning series for <paramref name="propertyId"/>.
    /// <paramref name="createdInGuide"/> becomes AreaRule.CreatedInGuide, the flag the
    /// backfill filters on: only rules created via the task wizard/calendar (true) are
    /// eligible; rules authored directly on an area rule (false) are left alone.
    /// </summary>
    private async Task<AreaRulePlanning> SeedPlanning(int propertyId, int areaId, bool createdInGuide)
    {
        var areaRule = new AreaRule
        {
            AreaId = areaId,
            PropertyId = propertyId,
            CreatedInGuide = createdInGuide,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);

        var planning = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id,
            PropertyId = propertyId,
            AreaId = areaId,
            StartDate = DateTime.UtcNow.Date,
            Status = true,
            RepeatType = 2,
            RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await planning.Create(BackendConfigurationPnDbContext!);
        return planning;
    }

    private async Task<AreaRulePlanning> SeedWizardPlanning(int propertyId, int areaId)
        => await SeedPlanning(propertyId, areaId, createdInGuide: true);

    [Test]
    public async Task RunIfNeededAsync_WizardPlanningWithoutConfiguration_UsesPropertysLowestIdBoard()
    {
        var property = await SeedProperty();
        var area = await SeedArea();

        // Two boards on the same property: the backfill must attach to the
        // lowest-Id one, mirroring GetBoards' "first board" default semantics.
        var lowerBoard = new CalendarBoard { Name = "Board A", Color = "#111111", PropertyId = property.Id };
        await lowerBoard.Create(BackendConfigurationPnDbContext!);
        var higherBoard = new CalendarBoard { Name = "Board B", Color = "#222222", PropertyId = property.Id };
        await higherBoard.Create(BackendConfigurationPnDbContext!);

        var planning = await SeedWizardPlanning(property.Id, area.Id);

        await _sut.RunIfNeededAsync();

        var configuration = BackendConfigurationPnDbContext!.CalendarConfigurations
            .Single(c => c.AreaRulePlanningId == planning.Id);
        Assert.That(configuration.BoardId, Is.EqualTo(lowerBoard.Id));
        Assert.That(configuration.StartHour, Is.EqualTo(0));
        Assert.That(configuration.Duration, Is.EqualTo(1));
        Assert.That(configuration.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    [Test]
    public async Task RunIfNeededAsync_PropertyWithoutBoards_CreatesDefaultBoardAndUsesIt()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        var planning = await SeedWizardPlanning(property.Id, area.Id);

        await _sut.RunIfNeededAsync();

        var createdBoard = BackendConfigurationPnDbContext!.CalendarBoards
            .Single(b => b.PropertyId == property.Id);
        Assert.That(createdBoard.Name, Is.EqualTo("Default"));
        Assert.That(createdBoard.Color, Is.EqualTo("#c30000"));

        var configuration = BackendConfigurationPnDbContext.CalendarConfigurations
            .Single(c => c.AreaRulePlanningId == planning.Id);
        Assert.That(configuration.BoardId, Is.EqualTo(createdBoard.Id));
    }

    [Test]
    public async Task RunIfNeededAsync_PlanningWithExistingConfiguration_IsLeftUntouched()
    {
        var property = await SeedProperty();
        var area = await SeedArea();

        var board = new CalendarBoard { Name = "Board A", Color = "#111111", PropertyId = property.Id };
        await board.Create(BackendConfigurationPnDbContext!);

        var otherBoard = new CalendarBoard { Name = "Board B", Color = "#222222", PropertyId = property.Id };
        await otherBoard.Create(BackendConfigurationPnDbContext!);

        var planning = await SeedWizardPlanning(property.Id, area.Id);
        var existing = new CalendarConfiguration
        {
            AreaRulePlanningId = planning.Id,
            StartHour = 9,
            Duration = 2,
            BoardId = otherBoard.Id
        };
        await existing.Create(BackendConfigurationPnDbContext!);

        await _sut.RunIfNeededAsync();

        var configurations = BackendConfigurationPnDbContext!.CalendarConfigurations
            .Where(c => c.AreaRulePlanningId == planning.Id)
            .ToList();
        Assert.That(configurations, Has.Count.EqualTo(1),
            "Backfill must not create a second configuration for an already-configured planning.");
        Assert.That(configurations[0].BoardId, Is.EqualTo(otherBoard.Id));
        Assert.That(configurations[0].StartHour, Is.EqualTo(9));
        Assert.That(configurations[0].Duration, Is.EqualTo(2));
    }

    [Test]
    public async Task RunIfNeededAsync_CalledTwice_SecondRunIsNoOp()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        await SeedWizardPlanning(property.Id, area.Id);

        await _sut.RunIfNeededAsync();
        var countAfterFirst = BackendConfigurationPnDbContext!.CalendarConfigurations.Count();
        Assert.That(countAfterFirst, Is.EqualTo(1));

        await _sut.RunIfNeededAsync();

        Assert.That(BackendConfigurationPnDbContext.CalendarConfigurations.Count(),
            Is.EqualTo(countAfterFirst));
    }

    [Test]
    public async Task RunIfNeededAsync_PlanningWhoseAreaRuleWasNotCreatedInGuide_GetsNoConfiguration()
    {
        var property = await SeedProperty();
        var area = await SeedArea();

        // CreatedInGuide = false: an area-rule-authored planning, not a
        // wizard/calendar planning. The backfill must not touch it, even
        // though it otherwise matches (non-removed, no CalendarConfiguration).
        var planning = await SeedPlanning(property.Id, area.Id, createdInGuide: false);

        await _sut.RunIfNeededAsync();

        var configurationExists = BackendConfigurationPnDbContext!.CalendarConfigurations
            .Any(c => c.AreaRulePlanningId == planning.Id);
        Assert.That(configurationExists, Is.False);

        // And it must not have triggered a spurious "Default" board creation
        // for the property either.
        var boardExists = BackendConfigurationPnDbContext.CalendarBoards
            .Any(b => b.PropertyId == property.Id);
        Assert.That(boardExists, Is.False);
    }
}
