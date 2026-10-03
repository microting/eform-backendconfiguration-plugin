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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.AssignmentWorker;
using BackendConfiguration.Pn.Services.BackendConfigurationAssignmentWorkerService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using BcPlanning = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1376 — removing a worker (BC worker delete) or unlinking them from a property
/// (property unassign) must not leave their assignments behind, and must be refused while
/// they are still assigned to an active event.
///
/// <para>The cleanup used to look up items-planning Planning 0 for a legacy rule with
/// <c>ItemPlanningId = 0</c>, throw, and stop halfway: the property link was already gone,
/// the remaining PlanningSites stayed live. Every assertion is scoped to the rows the test
/// seeded.</para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class WorkerRemovalLeftoversTests : TestBaseSetup
{
    // ── Fix 1: legacy ItemPlanningId 0 rows no longer stop the cleanup ───────

    [Test]
    public async Task PropertyUnassign_WithLegacyItemPlanningIdZeroRows_RemovesEveryAssignmentAndTheLink()
    {
        var siteId = await SeedSdkSite();
        var property = await SeedProperty();
        var link = await LinkSiteToProperty(property.Id, siteId);
        var legacyA = await SeedArp(property.Id, active: false, itemPlanningId: 0);
        var legacyB = await SeedArp(property.Id, active: false, itemPlanningId: 0);
        var psA = await AssignSite(legacyA, siteId);
        var psB = await AssignSite(legacyB, siteId);

        var result = await UnassignAsync(siteId, property.Id);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await LiveState(psA), Is.False, "the first legacy assignment must be removed");
        Assert.That(await LiveState(psB), Is.False,
            "the cleanup must not stop at a legacy row: the second assignment must be removed too");
        Assert.That(await PropertyWorkerIsLive(link.Id), Is.False);
    }

    [Test]
    public async Task WorkerDelete_WithLegacyItemPlanningIdZeroRows_RemovesEveryAssignmentAndTheLink()
    {
        var siteId = await SeedSdkSite();
        var property = await SeedProperty();
        var link = await LinkSiteToProperty(property.Id, siteId);
        var legacyA = await SeedArp(property.Id, active: false, itemPlanningId: 0);
        var legacyB = await SeedArp(property.Id, active: false, itemPlanningId: 0);
        var psA = await AssignSite(legacyA, siteId);
        var psB = await AssignSite(legacyB, siteId);

        var result = await (await CreateService()).Delete(siteId);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await LiveState(psA), Is.False);
        Assert.That(await LiveState(psB), Is.False);
        Assert.That(await PropertyWorkerIsLive(link.Id), Is.False);
    }

    [Test]
    public async Task PropertyUnassign_RuleWhosePlanningNoLongerExists_RemovesTheAssignmentWithoutThrowing()
    {
        var siteId = await SeedSdkSite();
        var property = await SeedProperty();
        var link = await LinkSiteToProperty(property.Id, siteId);
        // Inactive, so the refusal does not apply; the planning id points at no Planning row.
        var orphan = await SeedArp(property.Id, active: false, itemPlanningId: 987_654_321);
        var ps = await AssignSite(orphan, siteId);

        var result = await UnassignAsync(siteId, property.Id);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await LiveState(ps), Is.False);
        Assert.That(await PropertyWorkerIsLive(link.Id), Is.False);
    }

    // ── Fix 2: server-side "still assigned to an active event" refusal ───────

    [Test]
    public async Task WorkerDelete_StillNamedOnAnActiveEvent_IsRefusedBeforeAnyWrite()
    {
        var siteId = await SeedSdkSite();
        var property = await SeedProperty();
        var link = await LinkSiteToProperty(property.Id, siteId);
        var active = await SeedArp(property.Id, active: true, itemPlanningId: await SeedPlanning());
        var legacy = await SeedArp(property.Id, active: false, itemPlanningId: 0);
        var psActive = await AssignSite(active, siteId);
        var psLegacy = await AssignSite(legacy, siteId);

        var result = await (await CreateService()).Delete(siteId);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(new BackendConfigurationLocalizationService().GetString(
            BackendConfigurationAssignmentWorkerServiceHelper.WorkerStillAssignedToEventsCannotDeleteKey)));
        Assert.That(await PropertyWorkerIsLive(link.Id), Is.True, "a refused delete must keep the property link");
        Assert.That(await LiveState(psActive), Is.True);
        Assert.That(await LiveState(psLegacy), Is.True, "a refused delete must not clean anything up");
    }

    [Test]
    public async Task WorkerDelete_AssignedThroughAWorkerTag_IsRefused()
    {
        var siteId = await SeedSdkSite();
        var property = await SeedProperty();
        var link = await LinkSiteToProperty(property.Id, siteId);
        var active = await SeedArp(property.Id, active: true, itemPlanningId: await SeedPlanning());
        var tagId = await SeedSdkTag();
        await LinkSiteToTag(tagId, siteId);
        await AddWorkerTagLink(active, tagId);

        var result = await (await CreateService()).Delete(siteId);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(new BackendConfigurationLocalizationService().GetString(
            BackendConfigurationAssignmentWorkerServiceHelper.WorkerStillAssignedToEventsCannotDeleteKey)));
        Assert.That(await PropertyWorkerIsLive(link.Id), Is.True);
    }

    [Test]
    public async Task WorkerDelete_NamedOnAnActiveEventOfAnUnlinkedProperty_IsRefusedAndNothingIsDeactivated()
    {
        var siteId = await SeedSdkSite();
        var property = await SeedProperty();
        var link = await LinkSiteToProperty(property.Id, siteId);
        await link.Delete(BackendConfigurationPnDbContext!); // the old link is gone, the assignment is not
        var active = await SeedArp(property.Id, active: true, itemPlanningId: await SeedPlanning());
        var ps = await AssignSite(active, siteId);

        var result = await (await CreateService()).Delete(siteId);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(new BackendConfigurationLocalizationService().GetString(
            BackendConfigurationAssignmentWorkerServiceHelper.WorkerStillAssignedToEventsCannotDeleteKey)));
        Assert.That(await LiveState(ps), Is.True);
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.AsNoTracking().SingleAsync(x => x.Id == active);
        Assert.That(arp.Status, Is.True, "a refused delete must not deactivate the event");
    }

    [Test]
    public async Task WorkerDelete_WorkerTagOnAnActiveEventOfAPropertyTheWorkerIsNotOn_DoesNotBlock()
    {
        var siteId = await SeedSdkSite();
        var linked = await SeedProperty();
        var other = await SeedProperty();
        var link = await LinkSiteToProperty(linked.Id, siteId);
        var activeElsewhere = await SeedArp(other.Id, active: true, itemPlanningId: await SeedPlanning());
        var tagId = await SeedSdkTag();
        await LinkSiteToTag(tagId, siteId);
        await AddWorkerTagLink(activeElsewhere, tagId);

        var result = await (await CreateService()).Delete(siteId);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await PropertyWorkerIsLive(link.Id), Is.False);
    }

    [Test]
    public async Task WorkerDelete_OnlyInactiveAssignments_IsAllowed()
    {
        var siteId = await SeedSdkSite();
        var property = await SeedProperty();
        var link = await LinkSiteToProperty(property.Id, siteId);
        var inactive = await SeedArp(property.Id, active: false, itemPlanningId: await SeedPlanning());
        var ps = await AssignSite(inactive, siteId);

        var result = await (await CreateService()).Delete(siteId);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await LiveState(ps), Is.False);
        Assert.That(await PropertyWorkerIsLive(link.Id), Is.False);
    }

    [Test]
    public async Task PropertyUnassign_StillNamedOnAnActiveEventOfThatProperty_IsRefusedBeforeAnyWrite()
    {
        var siteId = await SeedSdkSite();
        var property = await SeedProperty();
        var link = await LinkSiteToProperty(property.Id, siteId);
        var active = await SeedArp(property.Id, active: true, itemPlanningId: await SeedPlanning());
        var ps = await AssignSite(active, siteId);

        var result = await UnassignAsync(siteId, property.Id, taskManagementEnabled: true);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message,
            Is.EqualTo(BackendConfigurationAssignmentWorkerServiceHelper.WorkerStillAssignedToEventsCannotUnassignPropertyKey));
        Assert.That(await PropertyWorkerIsLive(link.Id), Is.True, "a refused unassign must keep the property link");
        Assert.That(await LiveState(ps), Is.True);
        var reloaded = await BackendConfigurationPnDbContext!.PropertyWorkers.AsNoTracking().SingleAsync(x => x.Id == link.Id);
        Assert.That(reloaded.TaskManagementEnabled, Is.False,
            "the refusal must come before the TaskManagementEnabled write as well");
    }

    [Test]
    public async Task PropertyUnassign_ActiveEventOnAnotherProperty_DoesNotBlock()
    {
        var siteId = await SeedSdkSite();
        var kept = await SeedProperty();
        var removed = await SeedProperty();
        var keptLink = await LinkSiteToProperty(kept.Id, siteId);
        var removedLink = await LinkSiteToProperty(removed.Id, siteId);
        var activeOnKept = await SeedArp(kept.Id, active: true, itemPlanningId: await SeedPlanning());
        var psKept = await AssignSite(activeOnKept, siteId);

        var result = await BackendConfigurationAssignmentWorkerServiceHelper.Update(
            new PropertyAssignWorkersModel
            {
                SiteId = siteId,
                Assignments =
                [
                    new PropertyAssignmentWorkerModel { PropertyId = kept.Id, IsChecked = true },
                    new PropertyAssignmentWorkerModel { PropertyId = removed.Id, IsChecked = false }
                ]
            },
            await GetCore(), UserService(), BackendConfigurationPnDbContext!, CaseTemplatePnDbContext!, null,
            ItemsPlanningPnDbContext!);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await PropertyWorkerIsLive(removedLink.Id), Is.False);
        Assert.That(await PropertyWorkerIsLive(keptLink.Id), Is.True);
        Assert.That(await LiveState(psKept), Is.True);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private static IUserService UserService()
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetFirstUserIdInDb().Returns(Task.FromResult(1));
        return userService;
    }

    private async Task<BackendConfigurationAssignmentWorkerService> CreateService()
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return new BackendConfigurationAssignmentWorkerService(
            coreHelper, IdentityTestUtils.CreateRealUserManager(BaseDbContext!), UserService(),
            BackendConfigurationPnDbContext!, new BackendConfigurationLocalizationService(),
            ItemsPlanningPnDbContext!, TimePlanningPnDbContext!, CaseTemplatePnDbContext!, BaseDbContext!,
            TestContextLogger<BackendConfigurationAssignmentWorkerService>.Instance,
            Substitute.For<ICalendarAssignmentReconciliationService>());
    }

    private async Task<Microting.eFormApi.BasePn.Infrastructure.Models.API.OperationResult> UnassignAsync(
        int siteId, int propertyId, bool taskManagementEnabled = false)
        => await BackendConfigurationAssignmentWorkerServiceHelper.Update(
            new PropertyAssignWorkersModel
            {
                SiteId = siteId,
                TaskManagementEnabled = taskManagementEnabled,
                Assignments = [new PropertyAssignmentWorkerModel { PropertyId = propertyId, IsChecked = false }]
            },
            await GetCore(), UserService(), BackendConfigurationPnDbContext!, CaseTemplatePnDbContext!, null,
            ItemsPlanningPnDbContext!);

    private async Task<bool> LiveState(int planningSiteId)
        => await BackendConfigurationPnDbContext!.PlanningSites.AsNoTracking()
            .AnyAsync(x => x.Id == planningSiteId && x.WorkflowState != Constants.WorkflowStates.Removed);

    private async Task<bool> PropertyWorkerIsLive(int propertyWorkerId)
        => await BackendConfigurationPnDbContext!.PropertyWorkers.AsNoTracking()
            .AnyAsync(x => x.Id == propertyWorkerId && x.WorkflowState != Constants.WorkflowStates.Removed);

    private async Task<int> SeedSdkSite()
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"Jane Doe {Guid.NewGuid()}", MicrotingUid = null, LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    private async Task<int> SeedSdkTag()
    {
        var tag = new Tag { Name = $"team-{Guid.NewGuid()}", WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext!.Tags.AddAsync(tag);
        await MicrotingDbContext.SaveChangesAsync();
        return tag.Id;
    }

    private async Task LinkSiteToTag(int tagId, int siteId)
    {
        await MicrotingDbContext!.SiteTags.AddAsync(new SiteTag
        {
            TagId = tagId, SiteId = siteId, WorkflowState = Constants.WorkflowStates.Created
        });
        await MicrotingDbContext.SaveChangesAsync();
    }

    private async Task<Property> SeedProperty()
    {
        var property = new Property
        {
            Name = $"Example Property {Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        return property;
    }

    private async Task<PropertyWorker> LinkSiteToProperty(int propertyId, int siteId)
    {
        var link = new PropertyWorker
        {
            PropertyId = propertyId, WorkerId = siteId, TaskManagementEnabled = false,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await link.Create(BackendConfigurationPnDbContext!);
        return link;
    }

    private async Task<int> SeedPlanning()
    {
        var planning = new BcPlanning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = DateTime.UtcNow.Date, RelatedEFormId = 7, Description = "Task",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await planning.Create(ItemsPlanningPnDbContext!);
        return planning.Id;
    }

    /// <summary>
    /// A rule on <paramref name="propertyId"/>. <c>itemPlanningId: 0, active: false</c> is the
    /// legacy shape the cleanup used to crash on.
    /// </summary>
    private async Task<int> SeedArp(int propertyId, bool active, int itemPlanningId)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await area.Create(BackendConfigurationPnDbContext!);
        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = propertyId, EformId = 7, CreatedInGuide = itemPlanningId != 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);
        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = propertyId, AreaId = area.Id,
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

    private async Task AddWorkerTagLink(int arpId, int tagId)
    {
        await new AreaRulePlanningWorkerTag
        {
            AreaRulePlanningId = arpId, TagId = tagId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        }.Create(BackendConfigurationPnDbContext!);
    }
}
