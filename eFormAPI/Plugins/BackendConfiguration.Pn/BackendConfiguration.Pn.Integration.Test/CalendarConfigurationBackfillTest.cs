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
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Coverage for <c>CalendarConfigurationBackfillService.RunIfNeededAsync</c>, the
/// idempotent startup pass that converts task-wizard plannings (AreaRule
/// <c>CreatedInGuide == true</c>, no live <c>CalendarConfiguration</c>) into
/// calendar tasks: recurrence normalization on ARP + linked items-planning
/// Planning row, then a <c>CalendarConfiguration</c> at 09:00-10:00 on the
/// property's default board. Exercises the full frequency conversion table
/// (Altid, legacy (Day,0), Day N, Week N, Month N, Year pass-through), inactive
/// and orphaned rows, idempotency, crash-resume, board resolution, and the
/// <c>CreatedInGuide</c> filter.
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

        ItemsPlanningPnDbContext!.Plannings.RemoveRange(
            ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        _sut = new CalendarConfigurationBackfillService(
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
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
    /// Seeds one AreaRule + AreaRulePlanning series for <paramref name="propertyId"/>
    /// WITHOUT a linked items-planning Planning row (ItemPlanningId points nowhere) —
    /// the orphan case. <paramref name="createdInGuide"/> becomes
    /// AreaRule.CreatedInGuide, the flag the backfill filters on.
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

    /// <summary>
    /// Seeds a full wizard task: AreaRule (CreatedInGuide) + items-planning Planning
    /// carrying the old frequency encoding + the linked AreaRulePlanning, mirroring
    /// what BackendConfigurationTaskWizardService.CreateTask writes (frequency
    /// mirrored on both entities, ARP detail columns left at defaults).
    /// </summary>
    private async Task<(AreaRulePlanning Arp, Planning Planning)> SeedWizardTask(
        int propertyId,
        int areaId,
        int repeatType,
        int repeatEvery,
        DateTime startDate,
        bool status = true,
        string planningWorkflowState = Constants.WorkflowStates.Created)
    {
        var areaRule = new AreaRule
        {
            AreaId = areaId,
            PropertyId = propertyId,
            CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);

        // AddAsync, not .Create(): .Create() would overwrite the caller-chosen
        // WorkflowState (needed Removed for the inactive-task case).
        var planning = new Planning
        {
            Enabled = status,
            RepeatEvery = repeatEvery,
            RepeatType = (RepeatType)repeatType,
            StartDate = startDate,
            RelatedEFormId = 0,
            WorkflowState = planningWorkflowState,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id,
            PropertyId = propertyId,
            AreaId = areaId,
            ItemPlanningId = planning.Id,
            StartDate = startDate,
            Status = status,
            RepeatType = repeatType,
            RepeatEvery = repeatEvery,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);
        return (arp, planning);
    }

    private CalendarConfiguration GetSingleConfiguration(int arpId)
        => BackendConfigurationPnDbContext!.CalendarConfigurations
            .Single(c => c.AreaRulePlanningId == arpId);

    private void AssertNineToTenOnDefaultBoard(CalendarConfiguration configuration, int propertyId)
    {
        Assert.That(configuration.StartHour, Is.EqualTo(9.0));
        Assert.That(configuration.Duration, Is.EqualTo(1.0));
        Assert.That(configuration.Color, Is.Null);
        var defaultBoard = BackendConfigurationPnDbContext!.CalendarBoards
            .Where(b => b.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(b => b.PropertyId == propertyId)
            .OrderBy(b => b.Id)
            .First();
        Assert.That(configuration.BoardId, Is.EqualTo(defaultBoard.Id));
    }

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

        var configuration = GetSingleConfiguration(planning.Id);
        Assert.That(configuration.BoardId, Is.EqualTo(lowerBoard.Id));
        Assert.That(configuration.StartHour, Is.EqualTo(9.0));
        Assert.That(configuration.Duration, Is.EqualTo(1.0));
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

        var configuration = GetSingleConfiguration(planning.Id);
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
            StartHour = 8,
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
        Assert.That(configurations[0].StartHour, Is.EqualTo(8));
        Assert.That(configurations[0].Duration, Is.EqualTo(2));
    }

    [Test]
    public async Task RunIfNeededAsync_MonthlyWithStaleMarkerButUnnormalizedArp_NormalizesInPlaceWithoutDuplicatingMarker()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        var board = new CalendarBoard { Name = "Board A", Color = "#111111", PropertyId = property.Id };
        await board.Create(BackendConfigurationPnDbContext!);

        // 2026-01-31 is a Saturday (DayOfWeek 6).
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Month, repeatEvery: 1,
            startDate: new DateTime(2026, 1, 31));

        // Reproduce the old, since-replaced backfill's output: a live marker exists
        // but the ARP recurrence detail columns were never populated
        // (RepeatOrdinalWeek still null), so the task keeps rendering "på dag 0".
        var staleMarker = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id,
            StartHour = 9.0,
            Duration = 1.0,
            BoardId = board.Id
        };
        await staleMarker.Create(BackendConfigurationPnDbContext!);

        await _sut.RunIfNeededAsync();

        // The stale marker must NOT gate normalization: the ordinal-weekday encoding
        // has to be derived exactly as the no-prior-marker Month test asserts.
        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(3));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(1));
        Assert.That(updatedArp.DayOfWeek, Is.EqualTo(6));
        Assert.That(updatedArp.RepeatOrdinalWeek, Is.EqualTo(1));
        Assert.That(updatedArp.DayOfMonth, Is.EqualTo(0));

        var updatedPlanning = ItemsPlanningPnDbContext!.Plannings.Single(x => x.Id == planning.Id);
        Assert.That(updatedPlanning.RepeatOrdinalWeek, Is.EqualTo(1));

        // The existing marker is reused, not duplicated.
        Assert.That(BackendConfigurationPnDbContext.CalendarConfigurations
            .Count(c => c.AreaRulePlanningId == arp.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task RunIfNeededAsync_WeeklyWithStaleMarkerButUnnormalizedArp_NormalizesInPlaceWithoutDuplicatingMarker()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        var board = new CalendarBoard { Name = "Board A", Color = "#111111", PropertyId = property.Id };
        await board.Create(BackendConfigurationPnDbContext!);

        // 2026-01-07 is a Wednesday (DayOfWeek 3).
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Week, repeatEvery: 1,
            startDate: new DateTime(2026, 1, 7));

        // Old backfill's output: marker exists, RepeatWeekdaysCsv never written.
        var staleMarker = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id,
            StartHour = 9.0,
            Duration = 1.0,
            BoardId = board.Id
        };
        await staleMarker.Create(BackendConfigurationPnDbContext!);

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(2));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(1));
        Assert.That(updatedArp.DayOfWeek, Is.EqualTo(3));
        Assert.That(updatedArp.RepeatWeekdaysCsv, Is.EqualTo("3"));

        // The existing marker is reused, not duplicated.
        Assert.That(BackendConfigurationPnDbContext.CalendarConfigurations
            .Count(c => c.AreaRulePlanningId == arp.Id), Is.EqualTo(1));
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

    // --- Frequency conversion table ---

    [Test]
    public async Task RunIfNeededAsync_AltidPlanning_ConvertsToDailyOnBothEntities()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // Wizard "Altid" encoding: RepeatType 0, RepeatEvery 0.
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: 0, repeatEvery: 0,
            startDate: new DateTime(2026, 1, 7));

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(1));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(1));
        Assert.That(updatedArp.DayOfWeek, Is.EqualTo(0));
        Assert.That(updatedArp.RepeatWeekdaysCsv, Is.Null);

        var updatedPlanning = ItemsPlanningPnDbContext!.Plannings.Single(x => x.Id == planning.Id);
        Assert.That(updatedPlanning.RepeatType, Is.EqualTo(RepeatType.Day));
        Assert.That(updatedPlanning.RepeatEvery, Is.EqualTo(1));

        AssertNineToTenOnDefaultBoard(GetSingleConfiguration(arp.Id), property.Id);
    }

    [Test]
    public async Task RunIfNeededAsync_LegacyDayZeroPlanning_ConvertsToDailyOnBothEntities()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // Legacy items-planning "always" encoding: (Day, 0).
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Day, repeatEvery: 0,
            startDate: new DateTime(2026, 1, 7));

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(1));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(1));
        Assert.That(updatedArp.DayOfWeek, Is.EqualTo(0));
        Assert.That(updatedArp.RepeatWeekdaysCsv, Is.Null);

        var updatedPlanning = ItemsPlanningPnDbContext!.Plannings.Single(x => x.Id == planning.Id);
        Assert.That(updatedPlanning.RepeatType, Is.EqualTo(RepeatType.Day));
        Assert.That(updatedPlanning.RepeatEvery, Is.EqualTo(1));

        AssertNineToTenOnDefaultBoard(GetSingleConfiguration(arp.Id), property.Id);
    }

    [Test]
    public async Task RunIfNeededAsync_DailyEveryOne_ReDerivesArpAndLeavesPlanningUntouched()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Day, repeatEvery: 1,
            startDate: new DateTime(2026, 1, 7));

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(1));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(1));

        var updatedPlanning = ItemsPlanningPnDbContext!.Plannings.Single(x => x.Id == planning.Id);
        Assert.That(updatedPlanning.RepeatType, Is.EqualTo(RepeatType.Day));
        Assert.That(updatedPlanning.RepeatEvery, Is.EqualTo(1));
        Assert.That(updatedPlanning.RepeatOrdinalWeek, Is.Null);

        AssertNineToTenOnDefaultBoard(GetSingleConfiguration(arp.Id), property.Id);
    }

    [Test]
    public async Task RunIfNeededAsync_DailyEveryThree_ReDerivesArpAndLeavesPlanningUntouched()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Day, repeatEvery: 3,
            startDate: new DateTime(2026, 1, 7));

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(1));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(3));
        Assert.That(updatedArp.RepeatWeekdaysCsv, Is.Null);

        var updatedPlanning = ItemsPlanningPnDbContext!.Plannings.Single(x => x.Id == planning.Id);
        Assert.That(updatedPlanning.RepeatType, Is.EqualTo(RepeatType.Day));
        Assert.That(updatedPlanning.RepeatEvery, Is.EqualTo(3));

        AssertNineToTenOnDefaultBoard(GetSingleConfiguration(arp.Id), property.Id);
    }

    [Test]
    public async Task RunIfNeededAsync_WeeklyEveryOneStartingWednesday_WritesWeekdayDetailColumns()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // 2026-01-07 is a Wednesday (DayOfWeek 3).
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Week, repeatEvery: 1,
            startDate: new DateTime(2026, 1, 7));

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(2));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(1));
        Assert.That(updatedArp.DayOfWeek, Is.EqualTo(3));
        Assert.That(updatedArp.RepeatWeekdaysCsv, Is.EqualTo("3"));

        var updatedPlanning = ItemsPlanningPnDbContext!.Plannings.Single(x => x.Id == planning.Id);
        Assert.That(updatedPlanning.RepeatType, Is.EqualTo(RepeatType.Week));
        Assert.That(updatedPlanning.RepeatEvery, Is.EqualTo(1));
        Assert.That(updatedPlanning.RepeatOrdinalWeek, Is.Null);

        AssertNineToTenOnDefaultBoard(GetSingleConfiguration(arp.Id), property.Id);
    }

    [Test]
    public async Task RunIfNeededAsync_WeeklyEveryTwoStartingSunday_WritesWeekdayZero()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // 2026-01-04 is a Sunday (DayOfWeek 0) — the CSV must still be written
        // ("0"), not left null, so the edit modal classifies it as weekly-one.
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Week, repeatEvery: 2,
            startDate: new DateTime(2026, 1, 4));

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(2));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(2));
        Assert.That(updatedArp.DayOfWeek, Is.EqualTo(0));
        Assert.That(updatedArp.RepeatWeekdaysCsv, Is.EqualTo("0"));

        var updatedPlanning = ItemsPlanningPnDbContext!.Plannings.Single(x => x.Id == planning.Id);
        Assert.That(updatedPlanning.RepeatType, Is.EqualTo(RepeatType.Week));
        Assert.That(updatedPlanning.RepeatEvery, Is.EqualTo(2));

        AssertNineToTenOnDefaultBoard(GetSingleConfiguration(arp.Id), property.Id);
    }

    [Test]
    public async Task RunIfNeededAsync_MonthlyEveryOneStartingDay31_WritesFirstOrdinalWeekday()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // 2026-01-31 is a Saturday (DayOfWeek 6) on a month-boundary day the
        // ordinal-weekday encoding must not choke on.
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Month, repeatEvery: 1,
            startDate: new DateTime(2026, 1, 31));

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(3));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(1));
        Assert.That(updatedArp.DayOfWeek, Is.EqualTo(6));
        Assert.That(updatedArp.RepeatOrdinalWeek, Is.EqualTo(1));
        Assert.That(updatedArp.DayOfMonth, Is.EqualTo(0));

        var updatedPlanning = ItemsPlanningPnDbContext!.Plannings.Single(x => x.Id == planning.Id);
        Assert.That(updatedPlanning.RepeatType, Is.EqualTo(RepeatType.Month));
        Assert.That(updatedPlanning.RepeatEvery, Is.EqualTo(1));
        Assert.That(updatedPlanning.RepeatOrdinalWeek, Is.EqualTo(1));

        AssertNineToTenOnDefaultBoard(GetSingleConfiguration(arp.Id), property.Id);
    }

    [Test]
    public async Task RunIfNeededAsync_MonthlyEveryThreeStartingDay29_WritesFirstOrdinalWeekday()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // 2026-03-29 is a Sunday (DayOfWeek 0) on day 29.
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Month, repeatEvery: 3,
            startDate: new DateTime(2026, 3, 29));

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(3));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(3));
        Assert.That(updatedArp.DayOfWeek, Is.EqualTo(0));
        Assert.That(updatedArp.RepeatOrdinalWeek, Is.EqualTo(1));
        Assert.That(updatedArp.DayOfMonth, Is.EqualTo(0));

        var updatedPlanning = ItemsPlanningPnDbContext!.Plannings.Single(x => x.Id == planning.Id);
        Assert.That(updatedPlanning.RepeatType, Is.EqualTo(RepeatType.Month));
        Assert.That(updatedPlanning.RepeatEvery, Is.EqualTo(3));
        Assert.That(updatedPlanning.RepeatOrdinalWeek, Is.EqualTo(1));

        AssertNineToTenOnDefaultBoard(GetSingleConfiguration(arp.Id), property.Id);
    }

    [Test]
    public async Task RunIfNeededAsync_YearlyPlanning_PassesThroughUntouchedButGetsConfiguration()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // RepeatType 4 (Year) is not wizard-producible; the conversion must not
        // rewrite its recurrence, only attach the 09:00-10:00 configuration.
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: 4, repeatEvery: 1,
            startDate: new DateTime(2026, 1, 7));

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(4));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(1));
        Assert.That(updatedArp.DayOfWeek, Is.EqualTo(0));
        Assert.That(updatedArp.RepeatWeekdaysCsv, Is.Null);
        Assert.That(updatedArp.RepeatOrdinalWeek, Is.Null);

        var updatedPlanning = ItemsPlanningPnDbContext!.Plannings.Single(x => x.Id == planning.Id);
        Assert.That((int)updatedPlanning.RepeatType, Is.EqualTo(4));
        Assert.That(updatedPlanning.RepeatEvery, Is.EqualTo(1));
        Assert.That(updatedPlanning.RepeatOrdinalWeek, Is.Null);

        AssertNineToTenOnDefaultBoard(GetSingleConfiguration(arp.Id), property.Id);
    }

    [Test]
    public async Task RunIfNeededAsync_YearlyWithRepeatEveryZero_RepeatEveryZeroTakesPrecedence()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // Pins the (RepeatType 4, RepeatEvery 0) precedence: RepeatEvery 0 means
        // "Altid" regardless of type, so it converts to daily rather than passing
        // through as yearly.
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: 4, repeatEvery: 0,
            startDate: new DateTime(2026, 1, 7));

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(1));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(1));

        var updatedPlanning = ItemsPlanningPnDbContext!.Plannings.Single(x => x.Id == planning.Id);
        Assert.That(updatedPlanning.RepeatType, Is.EqualTo(RepeatType.Day));
        Assert.That(updatedPlanning.RepeatEvery, Is.EqualTo(1));

        AssertNineToTenOnDefaultBoard(GetSingleConfiguration(arp.Id), property.Id);
    }

    // --- Inactive / orphaned rows ---

    [Test]
    public async Task RunIfNeededAsync_RemovedArp_IsSkippedEntirely()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: 0, repeatEvery: 0,
            startDate: new DateTime(2026, 1, 7));
        arp.WorkflowState = Constants.WorkflowStates.Removed;
        await BackendConfigurationPnDbContext!.SaveChangesAsync();

        await _sut.RunIfNeededAsync();

        Assert.That(BackendConfigurationPnDbContext.CalendarConfigurations
            .Any(c => c.AreaRulePlanningId == arp.Id), Is.False);
        var untouchedPlanning = ItemsPlanningPnDbContext!.Plannings.Single(x => x.Id == planning.Id);
        Assert.That((int)untouchedPlanning.RepeatType, Is.EqualTo(0));
        Assert.That(untouchedPlanning.RepeatEvery, Is.EqualTo(0));
    }

    [Test]
    public async Task RunIfNeededAsync_InactiveTaskWithSoftDeletedPlanning_IsStillNormalized()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // Inactive wizard task: ARP.Status=false, Planning soft-deleted. The
        // Planning lookup deliberately includes removed rows so a reactivated
        // task carries the correct recurrence.
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: (int)RepeatType.Week, repeatEvery: 1,
            startDate: new DateTime(2026, 1, 7),
            status: false,
            planningWorkflowState: Constants.WorkflowStates.Removed);

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(2));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(1));
        Assert.That(updatedArp.DayOfWeek, Is.EqualTo(3));
        Assert.That(updatedArp.RepeatWeekdaysCsv, Is.EqualTo("3"));

        AssertNineToTenOnDefaultBoard(GetSingleConfiguration(arp.Id), property.Id);
    }

    [Test]
    public async Task RunIfNeededAsync_ArpWithMissingPlanningRow_GetsConfigurationWithoutCrash()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // SeedPlanning creates no items-planning Planning row (ItemPlanningId 0):
        // normalization must be skipped but the marker row still created so the
        // ARP is never re-scanned on later boots.
        var arp = await SeedWizardPlanning(property.Id, area.Id);

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext!.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(2));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(1));
        Assert.That(updatedArp.RepeatWeekdaysCsv, Is.Null);

        AssertNineToTenOnDefaultBoard(GetSingleConfiguration(arp.Id), property.Id);

        await _sut.RunIfNeededAsync();
        Assert.That(BackendConfigurationPnDbContext.CalendarConfigurations
            .Count(c => c.AreaRulePlanningId == arp.Id), Is.EqualTo(1));
    }

    // --- Idempotency / crash-resume ---

    [Test]
    public async Task RunIfNeededAsync_CalledTwice_SecondRunIsNoOp()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: 0, repeatEvery: 0,
            startDate: new DateTime(2026, 1, 7));

        await _sut.RunIfNeededAsync();
        var countAfterFirst = BackendConfigurationPnDbContext!.CalendarConfigurations.Count();
        Assert.That(countAfterFirst, Is.EqualTo(1));
        var arpVersionAfterFirst = BackendConfigurationPnDbContext.AreaRulePlannings
            .Single(x => x.Id == arp.Id).Version;

        await _sut.RunIfNeededAsync();

        Assert.That(BackendConfigurationPnDbContext.CalendarConfigurations.Count(),
            Is.EqualTo(countAfterFirst));
        Assert.That(BackendConfigurationPnDbContext.AreaRulePlannings
            .Single(x => x.Id == arp.Id).Version, Is.EqualTo(arpVersionAfterFirst));
    }

    [Test]
    public async Task RunIfNeededAsync_CrashResumeAfterPlanningMutation_ConvergesToSameFinalState()
    {
        var property = await SeedProperty();
        var area = await SeedArea();
        // Altid row: the first pass rewrites Planning to (Day, 1). Deleting only
        // the CalendarConfiguration simulates a crash between the recurrence
        // writes and the marker insert; the re-run re-dispatches the row as
        // (Day, 1) and must converge to the identical final state.
        var (arp, planning) = await SeedWizardTask(
            property.Id, area.Id, repeatType: 0, repeatEvery: 0,
            startDate: new DateTime(2026, 1, 7));

        await _sut.RunIfNeededAsync();

        var configuration = GetSingleConfiguration(arp.Id);
        BackendConfigurationPnDbContext!.CalendarConfigurations.Remove(configuration);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await _sut.RunIfNeededAsync();

        var updatedArp = BackendConfigurationPnDbContext.AreaRulePlannings.Single(x => x.Id == arp.Id);
        Assert.That(updatedArp.RepeatType, Is.EqualTo(1));
        Assert.That(updatedArp.RepeatEvery, Is.EqualTo(1));

        var updatedPlanning = ItemsPlanningPnDbContext!.Plannings.Single(x => x.Id == planning.Id);
        Assert.That(updatedPlanning.RepeatType, Is.EqualTo(RepeatType.Day));
        Assert.That(updatedPlanning.RepeatEvery, Is.EqualTo(1));

        AssertNineToTenOnDefaultBoard(GetSingleConfiguration(arp.Id), property.Id);
    }
}
