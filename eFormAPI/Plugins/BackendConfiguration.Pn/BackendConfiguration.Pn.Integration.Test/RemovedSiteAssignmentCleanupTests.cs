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

using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Controllers;
using BackendConfiguration.Pn.Services.RemovedSiteAssignmentCleanup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1376 — the dry-run → run?planHash cleanup of assignments that removed or missing SDK
/// sites left on inactive legacy rules (<see cref="RemovedSiteAssignmentCleanupService"/>).
/// The fixture empties PlanningSites before each test, so the plan
/// holds exactly what the test seeded.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class RemovedSiteAssignmentCleanupTests : TestBaseSetup
{
    /// <summary>An SDK site id no Sites row has: the "missing" case.</summary>
    private const int MissingSiteId = 987_654_321;

    private RemovedSiteAssignmentCleanupService _sut = null!;
    private int _propertyId;

    [SetUp]
    public async Task SetUpCleanup()
    {
        var ctx = BackendConfigurationPnDbContext!;
        ctx.PlanningSites.RemoveRange(ctx.PlanningSites);
        await ctx.SaveChangesAsync();

        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        _sut = new RemovedSiteAssignmentCleanupService(ctx, coreHelper, userService,
            TestContextLogger<RemovedSiteAssignmentCleanupService>.Instance);

        var property = new Property
        {
            Name = $"Example Property {Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(ctx);
        _propertyId = property.Id;
    }

    [Test]
    public void TheEndpointsAreAdminOnly()
    {
        var authorize = typeof(RemovedSiteAssignmentCleanupController)
            .GetCustomAttributes(true).OfType<AuthorizeAttribute>().ToList();
        Assert.That(authorize.Select(a => a.Roles), Is.EquivalentTo(new[] { EformRole.Admin }));
    }

    [Test]
    public async Task DryRun_ListsOnlyInactiveLegacyAssignmentsOfRemovedOrMissingSites_AndWritesNothing()
    {
        var s = await SeedScenario();

        var dry = await _sut.DryRunAsync();

        Assert.That(dry.Success, Is.True, dry.Message);
        Assert.That(dry.Model.Assignments.Select(a => a.PlanningSiteId),
            Is.EquivalentTo(new[] { s.RemovedSiteRow, s.MissingSiteRow }));
        var removed = dry.Model.Assignments.Single(a => a.PlanningSiteId == s.RemovedSiteRow);
        Assert.That(removed.SiteState, Is.EqualTo(RemovedSiteAssignmentCleanupService.SiteRemoved));
        Assert.That(removed.SiteRemovedAt, Is.Not.Null);
        Assert.That(removed.PropertyId, Is.EqualTo(_propertyId));
        Assert.That(dry.Model.Assignments.Single(a => a.PlanningSiteId == s.MissingSiteRow).SiteState,
            Is.EqualTo(RemovedSiteAssignmentCleanupService.SiteMissing));
        Assert.That(dry.Model.PlanHash, Is.Not.Empty);

        foreach (var id in s.All)
        {
            Assert.That(await IsLive(id), Is.True, $"the dry run must not write: PlanningSite {id}");
        }
    }

    [Test]
    public async Task Run_WithTheReviewedHash_RemovesOnlyThePlannedRows_AndASecondRunIsRefused()
    {
        var s = await SeedScenario();
        var dry = await _sut.DryRunAsync();

        var run = await _sut.RunAsync(dry.Model.PlanHash);

        Assert.That(run.Success, Is.True, run.Message);
        Assert.That(run.Model.RemovedAssignments, Is.EqualTo(2));
        Assert.That(await IsLive(s.RemovedSiteRow), Is.False);
        Assert.That(await IsLive(s.MissingSiteRow), Is.False);
        Assert.That(await IsLive(s.ActiveRuleRow), Is.True, "an active rule is never touched");
        Assert.That(await IsLive(s.LiveSiteRow), Is.True, "a live worker's assignment is never touched");
        Assert.That(await IsLive(s.PlannedLegacyRow), Is.True, "a rule with an items-planning side is out of scope");

        var again = await _sut.RunAsync(dry.Model.PlanHash);
        Assert.That(again.Success, Is.False, "the old hash no longer matches the (now empty) plan");

        var emptyDry = await _sut.DryRunAsync();
        Assert.That(emptyDry.Model.Assignments, Is.Empty);
        var emptyRun = await _sut.RunAsync(emptyDry.Model.PlanHash);
        Assert.That(emptyRun.Success, Is.False, "a plan with nothing to remove is refused");
    }

    [Test]
    public async Task Run_WithAStaleHash_IsRefusedAndWritesNothing()
    {
        var s = await SeedScenario();
        var dry = await _sut.DryRunAsync();

        // The data changes after the review: one more removed worker's assignment appears.
        var lateSite = await SeedSdkSite(Constants.WorkflowStates.Removed);
        var lateRow = await AssignSite(await SeedArp(active: false, itemPlanningId: 0), lateSite);

        var run = await _sut.RunAsync(dry.Model.PlanHash);

        Assert.That(run.Success, Is.False);
        foreach (var id in s.All.Append(lateRow))
        {
            Assert.That(await IsLive(id), Is.True, $"a refused run must not write: PlanningSite {id}");
        }

        var missingHash = await _sut.RunAsync(string.Empty);
        Assert.That(missingHash.Success, Is.False);
    }

    [Test]
    public async Task Run_RowThatNoLongerQualifiesAtWriteTime_IsSkipped()
    {
        var s = await SeedScenario();
        var dry = await _sut.DryRunAsync();

        // Between plan and write somebody reactivates the rule of the removed site's row.
        _sut.OnBeforeWrite = async planningSiteId =>
        {
            if (planningSiteId != s.RemovedSiteRow)
            {
                return;
            }
            var arpId = await BackendConfigurationPnDbContext!.PlanningSites.AsNoTracking()
                .Where(x => x.Id == planningSiteId).Select(x => x.AreaRulePlanningsId).SingleAsync();
            await BackendConfigurationPnDbContext.AreaRulePlannings.Where(x => x.Id == arpId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, true));
        };

        var run = await _sut.RunAsync(dry.Model.PlanHash);

        Assert.That(run.Success, Is.True, run.Message);
        Assert.That(run.Model.Skipped, Has.Count.EqualTo(1));
        Assert.That(await IsLive(s.RemovedSiteRow), Is.True, "a row that stopped qualifying is left as it is");
        Assert.That(await IsLive(s.MissingSiteRow), Is.False);
    }

    // ── Seeding ─────────────────────────────────────────────────────────────

    private sealed record Scenario(
        int RemovedSiteRow, int MissingSiteRow, int ActiveRuleRow, int LiveSiteRow, int PlannedLegacyRow)
    {
        public int[] All => [RemovedSiteRow, MissingSiteRow, ActiveRuleRow, LiveSiteRow, PlannedLegacyRow];
    }

    /// <summary>
    /// Two rows in scope (inactive legacy rule; site removed / site missing) and three out of
    /// scope (active rule with a removed site; live site; inactive rule with a planning).
    /// </summary>
    private async Task<Scenario> SeedScenario()
    {
        var removedSite = await SeedSdkSite(Constants.WorkflowStates.Removed);
        var liveSite = await SeedSdkSite(Constants.WorkflowStates.Created);

        return new Scenario(
            RemovedSiteRow: await AssignSite(await SeedArp(active: false, itemPlanningId: 0), removedSite),
            MissingSiteRow: await AssignSite(await SeedArp(active: false, itemPlanningId: 0), MissingSiteId),
            ActiveRuleRow: await AssignSite(await SeedArp(active: true, itemPlanningId: 0), removedSite),
            LiveSiteRow: await AssignSite(await SeedArp(active: false, itemPlanningId: 0), liveSite),
            PlannedLegacyRow: await AssignSite(await SeedArp(active: false, itemPlanningId: 4242), removedSite));
    }

    private async Task<bool> IsLive(int planningSiteId)
        => await BackendConfigurationPnDbContext!.PlanningSites.AsNoTracking()
            .AnyAsync(x => x.Id == planningSiteId && x.WorkflowState != Constants.WorkflowStates.Removed);

    private async Task<int> SeedSdkSite(string workflowState)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"Jane Doe {Guid.NewGuid()}", MicrotingUid = null, LanguageId = language.Id,
            WorkflowState = workflowState,
            // Sites.Add bypasses the SDK's Create/Delete, which stamp these; a real removal sets UpdatedAt.
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    private async Task<int> SeedArp(bool active, int itemPlanningId)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type2, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);
        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = _propertyId, EformId = 7, CreatedInGuide = false,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);
        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = _propertyId, AreaId = area.Id,
            ItemPlanningId = itemPlanningId, StartDate = DateTime.UtcNow.Date, Status = active,
            RepeatType = 2, RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);
        return arp.Id;
    }

    private async Task<int> AssignSite(int arpId, int siteId)
    {
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == arpId);
        var planningSite = new PlanningSite
        {
            AreaRulePlanningsId = arpId, SiteId = siteId, AreaId = arp.AreaId, AreaRuleId = arp.AreaRuleId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planningSite.Create(BackendConfigurationPnDbContext!);
        return planningSite.Id;
    }
}
