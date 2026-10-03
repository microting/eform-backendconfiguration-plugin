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

namespace BackendConfiguration.Pn.Integration.Test;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.DuplicatePlanningSiteRepair;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NUnit.Framework;
using PlanningSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite;

/// <summary>
/// #1385 — the dry-run → run?planHash= cleanup of the duplicate items-planning
/// PlanningSites rows the wizard's create path wrote before #1385 (sites A, B, A, B).
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class DuplicatePlanningSiteRepairTests : TestBaseSetup
{
    // The plan spans every owned planning in the database, so each test starts empty.
    protected override bool ResetDatabasePerTest => true;

    private const int SiteA = 101;
    private const int SiteB = 102;

    private DuplicatePlanningSiteRepairService _sut = null!;

    [SetUp]
    public void CreateService()
    {
        _sut = new DuplicatePlanningSiteRepairService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!,
            TestContextLogger<DuplicatePlanningSiteRepairService>.Instance);
    }

    private sealed record OwnedPlanning(int PlanningId, List<int> RowIds, int ArpId, int PropertyId, int AreaId);

    /// <summary>A planning owned by a live AreaRulePlanning, with one PlanningSites row per entry of <paramref name="siteIds"/>.</summary>
    private async Task<OwnedPlanning> SeedOwnedPlanningAsync(params int[] siteIds)
    {
        var (planningId, rowIds) = await SeedPlanningWithSitesAsync(siteIds);
        var property = new Property
        {
            Name = $"Example Property {Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);
        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);
        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planningId, Status = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);
        return new OwnedPlanning(planningId, rowIds, arp.Id, property.Id, area.Id);
    }

    private async Task<(int PlanningId, List<int> RowIds)> SeedPlanningWithSitesAsync(params int[] siteIds)
    {
        var planning = new Planning
        {
            Enabled = true, RepeatType = RepeatType.Week, RepeatEvery = 1, StartDate = DateTime.UtcNow.Date,
            RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var rowIds = new List<int>();
        foreach (var siteId in siteIds)
        {
            var row = new PlanningSite
            {
                PlanningId = planning.Id, SiteId = siteId,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await ItemsPlanningPnDbContext.PlanningSites.AddAsync(row);
            await ItemsPlanningPnDbContext.SaveChangesAsync();
            rowIds.Add(row.Id);
        }

        return (planning.Id, rowIds);
    }

    private async Task<List<int>> LiveRowIdsAsync(int planningId)
        => await ItemsPlanningPnDbContext!.PlanningSites
            .AsNoTracking()
            .Where(x => x.PlanningId == planningId && x.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

    [Test]
    public async Task DryRun_ListsTheDuplicatesKeepingTheLowestId_AndWritesNothing()
    {
        var (planningId, rows, _, _, _) = await SeedOwnedPlanningAsync(SiteA, SiteB, SiteA, SiteB);
        await SeedOwnedPlanningAsync(SiteA, SiteB); // clean planning: not in the plan

        var result = await _sut.DryRunAsync();

        Assert.That(result.Success, Is.True, result.Message);
        var plan = result.Model;
        Assert.Multiple(() =>
        {
            Assert.That(plan.Groups.Select(g => (g.PlanningId, g.SiteId, g.KeptPlanningSiteId)),
                Is.EqualTo(new[] { (planningId, SiteA, rows[0]), (planningId, SiteB, rows[1]) }));
            Assert.That(plan.Groups.Select(g => g.RemovedPlanningSiteIds),
                Is.EqualTo(new[] { new List<int> { rows[2] }, new List<int> { rows[3] } }));
            Assert.That(plan.RowsToRemove, Is.EqualTo(2));
            Assert.That(plan.PlanHash, Is.Not.Empty);
        });
        Assert.That(await LiveRowIdsAsync(planningId), Is.EqualTo(rows), "a dry run writes nothing");
    }

    [Test]
    public async Task DryRun_IgnoresPlanningsNoAreaRulePlanningOwns()
    {
        await SeedPlanningWithSitesAsync(SiteA, SiteA);

        var result = await _sut.DryRunAsync();

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model.Groups, Is.Empty);
    }

    [Test]
    public async Task Run_WithTheDryRunsHash_LeavesOneLiveRowPerSite_AndASecondRunIsRefusedAsEmpty()
    {
        var (planningId, rows, _, _, _) = await SeedOwnedPlanningAsync(SiteA, SiteB, SiteA, SiteB);
        var plan = (await _sut.DryRunAsync()).Model;

        var run = await _sut.RunAsync(plan.PlanHash);

        Assert.That(run.Success, Is.True, run.Message);
        Assert.Multiple(() =>
        {
            Assert.That(run.Model.Removed, Is.EqualTo(2));
            Assert.That(run.Model.Skipped, Is.Empty);
            Assert.That(run.Model.Failures, Is.Empty);
        });
        Assert.That(await LiveRowIdsAsync(planningId), Is.EqualTo(new[] { rows[0], rows[1] }));

        var second = await _sut.RunAsync((await _sut.DryRunAsync()).Model.PlanHash);
        Assert.That(second.Success, Is.False, "nothing is left to remove");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not-the-hash")]
    public async Task Run_WithoutTheCurrentPlansHash_IsRefusedAndWritesNothing(string? planHash)
    {
        var (planningId, rows, _, _, _) = await SeedOwnedPlanningAsync(SiteA, SiteA);

        var run = await _sut.RunAsync(planHash!);

        Assert.That(run.Success, Is.False);
        Assert.That(await LiveRowIdsAsync(planningId), Is.EqualTo(rows));
    }

    [Test]
    public async Task Run_AfterTheDataChanged_IsRefused()
    {
        var (planningId, rows, _, _, _) = await SeedOwnedPlanningAsync(SiteA, SiteA);
        var plan = (await _sut.DryRunAsync()).Model;
        await SeedOwnedPlanningAsync(SiteB, SiteB);

        var run = await _sut.RunAsync(plan.PlanHash);

        Assert.That(run.Success, Is.False);
        Assert.That(await LiveRowIdsAsync(planningId), Is.EqualTo(rows));
    }

    /// <summary>
    /// The kept row is removed after the plan was computed, right before the extra row's
    /// write: the extra row is skipped, so the pair never loses its last live row.
    /// </summary>
    [Test]
    public async Task Run_KeptRowRemovedMidRun_SkipsTheGroupInsteadOfRemovingTheLastRow()
    {
        var (planningId, rows, _, _, _) = await SeedOwnedPlanningAsync(SiteA, SiteA, SiteB, SiteB);
        var plan = (await _sut.DryRunAsync()).Model;
        var keptA = rows[0];
        var removedOnce = false;
        _sut.OnBeforeRemove = async row =>
        {
            if (removedOnce) return;
            removedOnce = true;
            // Lands before the SiteB group is re-read: remove SiteB's kept row behind the run's back.
            await ItemsPlanningPnDbContext!.PlanningSites
                .Where(x => x.Id == rows[2])
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.WorkflowState, Constants.WorkflowStates.Removed));
        };

        var run = await _sut.RunAsync(plan.PlanHash);

        Assert.That(run.Success, Is.True, run.Message);
        Assert.Multiple(() =>
        {
            Assert.That(run.Model.Removed, Is.EqualTo(1), "SiteA's extra row");
            Assert.That(run.Model.Skipped, Has.Count.EqualTo(1), "SiteB's extra row: its kept row is gone");
        });
        Assert.That(await LiveRowIdsAsync(planningId), Is.EqualTo(new[] { keptA, rows[3] }));
    }

    /// <summary>
    /// #1385 — removing a worker from a property read the worker's items-planning
    /// PlanningSites row with SingleOrDefault, which threw on a duplicated pair.
    /// <b>Fails on the old code.</b> Every live row of the pair is removed now.
    /// </summary>
    [Test]
    public async Task RemovingAWorkerFromAProperty_RemovesEveryDuplicatedPlanningSitesRow()
    {
        var core = await GetCore();
        var owned = await SeedOwnedPlanningAsync(SiteA, SiteA);
        await new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
        {
            AreaRulePlanningsId = owned.ArpId, AreaId = owned.AreaId, SiteId = SiteA,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);
        var propertyWorker = new PropertyWorker
        {
            PropertyId = owned.PropertyId, WorkerId = SiteA,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await propertyWorker.Create(BackendConfigurationPnDbContext!);
        var property = await BackendConfigurationPnDbContext!.Properties.SingleAsync(x => x.Id == owned.PropertyId);

        await BackendConfiguration.Pn.Infrastructure.Helpers.BackendConfigurationAssignmentWorkerServiceHelper
            .DeleteAllEntriesForPropertyAssignment(propertyWorker, core, property,
                core.DbContextHelper.GetDbContext(), CaseTemplatePnDbContext!,
                BackendConfigurationPnDbContext, ItemsPlanningPnDbContext!);

        Assert.That(await LiveRowIdsAsync(owned.PlanningId), Is.Empty);
    }
}
