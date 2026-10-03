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
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.TaskListActiveWithoutPlanningRepair;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;
using PlanningCase = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningCase;
using PlanningCaseSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningCaseSite;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;
using SdkCheckListSite = Microting.eForm.Infrastructure.Data.Entities.CheckListSite;
using SdkSite = Microting.eForm.Infrastructure.Data.Entities.Site;
using Planning = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning;
using ItemsPlanningRepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType;

/// <summary>
/// #1376 — the one-time cleanup of ACTIVE AreaRulePlannings without a live Planning,
/// driven through the real repair service and the real task wizard delete.
///
/// <para>SDK cases are seeded AFTER <c>GetCore()</c> in SetUp (the core migrates the SDK
/// schema), and retraction goes through the <c>RetractDeployment</c> seam: CI has no
/// Microting cloud for <c>core.CaseDelete</c>.</para>
///
/// <para>Seed, per test: a lone row with ItemPlanningId 0, a lone row on a removed
/// Planning, a legacy-sibling pair (one rule, a planning-less row next to a live one),
/// a normal live task and an inactive row without a Planning. Only the two lone rows
/// are deleted; the sibling is listed as skipped and nothing else is touched.</para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskListActiveWithoutPlanningRepairTests : TestBaseSetup
{
    private TaskListActiveWithoutPlanningRepairService _repair = null!;

    private sealed record Seed(
        int LoneNoPlanning, int LoneRemovedPlanning, int SiblingOrphan, int SiblingLive, int LiveTask, int Inactive);

    [SetUp]
    public async Task SetupRepair()
    {
        // FK-safe clean: the repair scans every active row, so the fixture owns the table.
        var db = BackendConfigurationPnDbContext!;
        db.CalendarOccurrenceExceptionSites.RemoveRange(db.CalendarOccurrenceExceptionSites);
        await db.SaveChangesAsync();
        db.CalendarOccurrenceExceptions.RemoveRange(db.CalendarOccurrenceExceptions);
        db.CalendarConfigurations.RemoveRange(db.CalendarConfigurations);
        db.AreaRulePlanningTags.RemoveRange(db.AreaRulePlanningTags);
        db.AreaRulePlanningWorkerTags.RemoveRange(db.AreaRulePlanningWorkerTags);
        db.AreaRulePlanningFiles.RemoveRange(db.AreaRulePlanningFiles);
        db.PlanningSites.RemoveRange(db.PlanningSites);
        await db.SaveChangesAsync();
        db.AreaRulePlannings.RemoveRange(db.AreaRulePlannings);
        await db.SaveChangesAsync();
        db.AreaRuleTranslations.RemoveRange(db.AreaRuleTranslations);
        await db.SaveChangesAsync();
        db.AreaRules.RemoveRange(db.AreaRules);
        await db.SaveChangesAsync();

        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));

        var wizard = new BackendConfigurationTaskWizardService(
            new BackendConfigurationLocalizationService(),
            userService,
            db,
            coreHelper,
            ItemsPlanningPnDbContext!,
            Substitute.For<IEventDeployService>(),
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            TestContextLogger<BackendConfigurationTaskWizardService>.Instance);

        _repair = new TaskListActiveWithoutPlanningRepairService(
            db,
            ItemsPlanningPnDbContext!,
            coreHelper,
            wizard,
            TestContextLogger<TaskListActiveWithoutPlanningRepairService>.Instance);
    }

    [Test]
    public async Task DryRun_ListsLoneRowsForDeletionAndSiblingAsSkipped_AndWritesNothing()
    {
        var seed = await SeedAll();

        var result = await _repair.DryRunAsync();

        Assert.That(result.Success, Is.True, result.Message);
        var plan = result.Model;
        Assert.Multiple(() =>
        {
            Assert.That(plan.Deletions.Select(x => x.AreaRulePlanningId),
                Is.EqualTo(new[] { seed.LoneNoPlanning, seed.LoneRemovedPlanning }));
            Assert.That(plan.Deletions.Select(x => x.PlanningState), Is.EqualTo(new[]
            {
                TaskListActiveWithoutPlanningRepairService.StateNoPlanning,
                TaskListActiveWithoutPlanningRepairService.StatePlanningRemoved
            }));
            Assert.That(plan.Skipped.Select(x => x.AreaRulePlanningId), Is.EqualTo(new[] { seed.SiblingOrphan }));
            Assert.That(plan.Skipped.Single().SkipReason,
                Is.EqualTo(TaskListActiveWithoutPlanningRepairService.ReasonLegacySibling));
            Assert.That(plan.PlanHash, Is.Not.Empty);
        });

        Assert.That(await LiveArpIds(), Is.EquivalentTo(new[]
        {
            seed.LoneNoPlanning, seed.LoneRemovedPlanning, seed.SiblingOrphan, seed.SiblingLive, seed.LiveTask,
            seed.Inactive
        }), "The dry run must write nothing.");

        var again = await _repair.DryRunAsync();
        Assert.That(again.Model.PlanHash, Is.EqualTo(plan.PlanHash), "Two dry runs over the same data agree.");
    }

    [Test]
    public async Task Run_WithoutHash_IsRefused()
    {
        var seed = await SeedAll();

        var result = await _repair.RunAsync("");

        Assert.That(result.Success, Is.False);
        Assert.That(await LiveArpIds(), Does.Contain(seed.LoneNoPlanning));
    }

    [Test]
    public async Task Run_WithStaleHash_IsRefusedAndWritesNothing()
    {
        var seed = await SeedAll();
        var planHash = (await _repair.DryRunAsync()).Model.PlanHash;

        // The data changes after the review: another lone row appears.
        var (property, area) = await SeedPropertyAndArea();
        var arrived = await SeedArp(await SeedAreaRule(property.Id, area.Id), itemPlanningId: 0, status: true);

        var result = await _repair.RunAsync(planHash);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("plan hash mismatch"));
        Assert.That(await LiveArpIds(), Is.SupersetOf(new[] { seed.LoneNoPlanning, seed.LoneRemovedPlanning, arrived }));
    }

    [Test]
    public async Task Run_DeletesOnlyLoneRows_AndASecondRunIsRefused()
    {
        var seed = await SeedAll();
        var planHash = (await _repair.DryRunAsync()).Model.PlanHash;

        var result = await _repair.RunAsync(planHash);

        Assert.That(result.Success, Is.True, result.Message + string.Join("; ", result.Model?.Failures ?? []));
        Assert.That(result.Model.DeletedAreaRulePlanningIds,
            Is.EqualTo(new[] { seed.LoneNoPlanning, seed.LoneRemovedPlanning }));
        Assert.That(await LiveArpIds(), Is.EquivalentTo(new[]
        {
            seed.SiblingOrphan, seed.SiblingLive, seed.LiveTask, seed.Inactive
        }), "Only the lone rows go; the legacy sibling, the live task and the inactive row stay.");

        var second = await _repair.DryRunAsync();
        Assert.Multiple(() =>
        {
            Assert.That(second.Model.Deletions, Is.Empty);
            Assert.That(second.Model.Skipped.Select(x => x.AreaRulePlanningId), Is.EqualTo(new[] { seed.SiblingOrphan }));
        });

        var secondRun = await _repair.RunAsync(second.Model.PlanHash);
        Assert.That(secondRun.Success, Is.False);
        Assert.That(secondRun.Message, Does.Contain("nothing to delete"));
    }

    /// <summary>
    /// A lone row on a removed Planning with four device deployments: an open case, a
    /// completed case (Status 100), a case answered but not closed (DoneAt set) and a
    /// CheckListSite-only deployment. The plan lists, and the run retracts, only the open
    /// case and the CheckListSite; the completed records stay untouched.
    /// </summary>
    [Test]
    public async Task Run_RetractsOpenCaseAndCheckListSite_AndKeepsCompletedCases()
    {
        var (property, area) = await SeedPropertyAndArea();
        var planningId = await SeedPlanning(Constants.WorkflowStates.Removed);
        var arpId = await SeedArp(await SeedAreaRule(property.Id, area.Id), planningId, status: true);

        var siteId = await SeedSdkSite();
        var uidBase = Random.Shared.Next(100_000, 900_000);
        var openCase = await SeedCaseDeployment(planningId, siteId, status: 66, doneAt: null, microtingUid: uidBase + 1);
        var completedCase = await SeedCaseDeployment(planningId, siteId, status: 100, doneAt: DateTime.UtcNow.AddDays(-2),
            microtingUid: uidBase + 2);
        var answeredCase = await SeedCaseDeployment(planningId, siteId, status: 66, doneAt: DateTime.UtcNow.AddDays(-1),
            microtingUid: uidBase + 3);
        var checkListSite = await SeedCheckListSiteDeployment(planningId, siteId, microtingUid: uidBase + 4);

        var plan = (await _repair.DryRunAsync()).Model;
        var deletion = plan.Deletions.Single(x => x.AreaRulePlanningId == arpId);
        Assert.Multiple(() =>
        {
            Assert.That(deletion.CasesToRetract.Select(x => x.MicrotingUid),
                Is.EquivalentTo(new[] { uidBase + 1, uidBase + 4 }));
            Assert.That(deletion.CasesToRetract.Single(x => x.MicrotingUid == uidBase + 1).SdkCaseId, Is.EqualTo(openCase));
            Assert.That(deletion.CasesToRetract.Single(x => x.MicrotingUid == uidBase + 4).CheckListSiteId,
                Is.EqualTo(checkListSite));
        });

        var retracted = new System.Collections.Generic.List<int>();
        _repair.RetractDeployment = uid =>
        {
            retracted.Add(uid);
            return Task.FromResult(true);
        };
        var result = await _repair.RunAsync(plan.PlanHash);

        Assert.That(result.Success, Is.True, result.Message + string.Join("; ", result.Model?.Failures ?? []));
        Assert.Multiple(async () =>
        {
            Assert.That(retracted, Is.EquivalentTo(new[] { uidBase + 1, uidBase + 4 }));
            Assert.That(result.Model.RetractedCases, Is.EqualTo(2));
            Assert.That(result.Model.DeletedAreaRulePlanningIds, Does.Contain(arpId));
            foreach (var kept in new[] { completedCase, answeredCase })
            {
                Assert.That(await MicrotingDbContext!.Cases.AsNoTracking().Where(x => x.Id == kept)
                        .Select(x => x.WorkflowState).SingleAsync(),
                    Is.Not.EqualTo(Constants.WorkflowStates.Removed), $"Completed case {kept} must be kept.");
            }
        });
    }

    /// <summary>
    /// A row deactivated between the plan and its write is not deleted: the run lists it
    /// as changed and still deletes the other planned row.
    /// </summary>
    [Test]
    public async Task Run_RowChangedAfterPlan_IsSkippedAsChanged()
    {
        var seed = await SeedAll();
        var planHash = (await _repair.DryRunAsync()).Model.PlanHash;

        _repair.OnBeforeWrite = async row =>
        {
            if (row.AreaRulePlanningId == seed.LoneNoPlanning)
            {
                await BackendConfigurationPnDbContext!.AreaRulePlannings
                    .Where(x => x.Id == seed.LoneNoPlanning)
                    .ExecuteUpdateAsync(x => x.SetProperty(y => y.Status, false));
            }
        };
        var result = await _repair.RunAsync(planHash);

        Assert.That(result.Success, Is.True, result.Message + string.Join("; ", result.Model?.Failures ?? []));
        Assert.Multiple(async () =>
        {
            Assert.That(result.Model.DeletedAreaRulePlanningIds, Is.EqualTo(new[] { seed.LoneRemovedPlanning }));
            Assert.That(result.Model.SkippedAsChanged.Single(), Does.Contain($"AreaRulePlanning {seed.LoneNoPlanning}"));
            Assert.That(await LiveArpIds(), Does.Contain(seed.LoneNoPlanning));
        });
    }

    // ── Seed ────────────────────────────────────────────────────────────────

    private async Task<Seed> SeedAll()
    {
        var (property, area) = await SeedPropertyAndArea();

        var loneNoPlanning = await SeedArp(await SeedAreaRule(property.Id, area.Id), 0, status: true);
        var loneRemoved = await SeedArp(await SeedAreaRule(property.Id, area.Id),
            await SeedPlanning(Constants.WorkflowStates.Removed), status: true);

        var sharedRule = await SeedAreaRule(property.Id, area.Id);
        var siblingOrphan = await SeedArp(sharedRule, 0, status: true);
        var siblingLive = await SeedArp(sharedRule, await SeedPlanning(Constants.WorkflowStates.Created), status: true);

        var liveTask = await SeedArp(await SeedAreaRule(property.Id, area.Id),
            await SeedPlanning(Constants.WorkflowStates.Created), status: true);
        var inactive = await SeedArp(await SeedAreaRule(property.Id, area.Id), 0, status: false);

        return new Seed(loneNoPlanning, loneRemoved, siblingOrphan, siblingLive, liveTask, inactive);
    }

    private async Task<int> SeedSdkSite()
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new SdkSite
        {
            Name = $"repair-site-{Guid.NewGuid()}", MicrotingUid = null,
            LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    /// <summary>An SDK case deployed through the planning (PlanningCase + PlanningCaseSite); returns the case id.</summary>
    private async Task<int> SeedCaseDeployment(int planningId, int siteId, int status, DateTime? doneAt, int microtingUid)
    {
        var sdkCase = new SdkCase
        {
            SiteId = siteId, Status = status, DoneAt = doneAt, MicrotingUid = microtingUid,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        await SeedPlanningCaseSite(planningId, siteId, status, sdkCaseId: sdkCase.Id, checkListSiteId: 0);
        return sdkCase.Id;
    }

    /// <summary>A repeated deployment that exists only as a CheckListSite; returns its id.</summary>
    private async Task<int> SeedCheckListSiteDeployment(int planningId, int siteId, int microtingUid)
    {
        var checkListSite = new SdkCheckListSite
        {
            SiteId = siteId, MicrotingUid = microtingUid, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.CheckListSites.AddAsync(checkListSite);
        await MicrotingDbContext.SaveChangesAsync();

        await SeedPlanningCaseSite(planningId, siteId, status: 66, sdkCaseId: 0, checkListSiteId: checkListSite.Id);
        return checkListSite.Id;
    }

    private async Task SeedPlanningCaseSite(int planningId, int siteId, int status, int sdkCaseId, int checkListSiteId)
    {
        var planningCase = new PlanningCase
        {
            PlanningId = planningId, Status = status, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(new PlanningCaseSite
        {
            PlanningId = planningId, PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = siteId, MicrotingSdkeFormId = 0,
            MicrotingSdkCaseId = sdkCaseId, MicrotingCheckListSitId = checkListSiteId, Status = status,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();
    }

    private Task<int[]> LiveArpIds()
        => BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking()
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.Id)
            .ToArrayAsync();

    private async Task<int> SeedPlanning(string workflowState)
    {
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = ItemsPlanningRepeatType.Day,
            StartDate = DateTime.UtcNow.Date, RelatedEFormId = 0,
            WorkflowState = workflowState, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        return planning.Id;
    }

    private async Task<AreaRule> SeedAreaRule(int propertyId, int areaId)
    {
        var areaRule = new AreaRule
        {
            AreaId = areaId, PropertyId = propertyId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);
        return areaRule;
    }

    private async Task<int> SeedArp(AreaRule areaRule, int itemPlanningId, bool status)
    {
        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = areaRule.PropertyId, AreaId = areaRule.AreaId,
            ItemPlanningId = itemPlanningId, Status = status, StartDate = DateTime.UtcNow.Date.AddYears(-3),
            RepeatType = 1, RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);
        return arp.Id;
    }

    private async Task<(Property Property, Area Area)> SeedPropertyAndArea()
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);

        var property = new Property
        {
            Name = $"ActiveWithoutPlanningProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        return (property, area);
    }
}
