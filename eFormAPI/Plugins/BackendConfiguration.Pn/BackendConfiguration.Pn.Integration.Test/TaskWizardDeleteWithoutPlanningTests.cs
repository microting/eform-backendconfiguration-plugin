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
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

/// <summary>
/// #1376 — <see cref="BackendConfigurationTaskWizardService.DeleteTaskDeferredRetraction"/>
/// (the task-list delete) on a legacy row without a Planning.
///
/// <para>Legacy area rules leave AreaRulePlannings with <c>ItemPlanningId = 0</c>, or
/// pointing at a Planning that no longer exists. The delete must remove such a row and
/// what hangs off it (PlanningSites, tag links, calendar configuration) without:</para>
/// <list type="bullet">
/// <item>failing on the missing Planning;</item>
/// <item>sweeping compliances whose <c>PlanningId</c> is 0 — they are not this row's;</item>
/// <item>removing an AreaRule that another live AreaRulePlanning still uses (legacy
/// area types hang several plannings off one rule).</item>
/// </list>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskWizardDeleteWithoutPlanningTests : TestBaseSetup
{
    private BackendConfigurationTaskWizardService _wizard = null!;

    [SetUp]
    public async Task SetupWizard()
    {
        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));

        _wizard = new BackendConfigurationTaskWizardService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext!,
            coreHelper,
            ItemsPlanningPnDbContext!,
            Substitute.For<IEventDeployService>(),
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            TestContextLogger<BackendConfigurationTaskWizardService>.Instance);
    }

    [Test]
    public async Task Delete_ActiveRowWithItemPlanningIdZero_RemovesTheRowAndWhatHangsOffIt()
    {
        var (property, area) = await SeedPropertyAndArea();
        var areaRule = await SeedAreaRule(property.Id, area.Id);
        var arp = await SeedArp(areaRule, itemPlanningId: 0);

        var planningSite = new BcPlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = 1, Status = 33,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(planningSite);
        var tag = new AreaRulePlanningTag
        {
            AreaRulePlanningId = arp.Id, ItemPlanningTagId = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlanningTags.AddAsync(tag);
        var calendarConfiguration = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(calendarConfiguration);

        // Another property's compliance that happens to carry PlanningId 0. The old
        // sweep matched it on PlanningId == ItemPlanningId == 0.
        var unrelatedCompliance = new Compliance
        {
            PlanningId = 0, PropertyId = property.Id, AreaId = area.Id,
            // Compliances is UNIQUE on (PlanningId, Deadline): keep this one's deadline distinct.
            Deadline = DateTime.UtcNow.Date.AddDays(3).AddSeconds(Random.Shared.Next(1, 86_000)),
            StartDate = DateTime.UtcNow.Date,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(unrelatedCompliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var result = await _wizard.DeleteTaskDeferredRetraction(arp.Id);

        Assert.That(result.Success, Is.True, result.Message);
        var db = BackendConfigurationPnDbContext;
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await StateOf(db.AreaRulePlannings, arp.Id), Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(await StateOf(db.AreaRules, areaRule.Id), Is.EqualTo(Constants.WorkflowStates.Removed),
                "A rule no other live row uses goes with its only row.");
            Assert.That(await StateOf(db.PlanningSites, planningSite.Id), Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(await StateOf(db.AreaRulePlanningTags, tag.Id), Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(await StateOf(db.CalendarConfigurations, calendarConfiguration.Id),
                Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(await StateOf(db.Compliances, unrelatedCompliance.Id),
                Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "A compliance with PlanningId 0 is not this row's and must survive.");
        });
    }

    [Test]
    public async Task Delete_ActiveRowOnMissingPlanning_Succeeds()
    {
        var (property, area) = await SeedPropertyAndArea();
        var areaRule = await SeedAreaRule(property.Id, area.Id);
        var arp = await SeedArp(areaRule, itemPlanningId: int.MaxValue);

        var result = await _wizard.DeleteTaskDeferredRetraction(arp.Id);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await StateOf(BackendConfigurationPnDbContext!.AreaRulePlannings, arp.Id),
            Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task Delete_RowWhoseAreaRuleHasALiveSibling_KeepsTheRuleAndItsTranslations()
    {
        var (property, area) = await SeedPropertyAndArea();
        var areaRule = await SeedAreaRule(property.Id, area.Id);
        var translation = new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = "Example rule",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.AreaRuleTranslations.AddAsync(translation);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var orphan = await SeedArp(areaRule, itemPlanningId: 0);
        var sibling = await SeedArp(areaRule, itemPlanningId: int.MaxValue - 1);

        var result = await _wizard.DeleteTaskDeferredRetraction(orphan.Id);

        Assert.That(result.Success, Is.True, result.Message);
        var db = BackendConfigurationPnDbContext;
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await StateOf(db.AreaRulePlannings, orphan.Id), Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(await StateOf(db.AreaRulePlannings, sibling.Id), Is.Not.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(await StateOf(db.AreaRules, areaRule.Id), Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "The live sibling still uses the rule.");
            Assert.That(await StateOf(db.AreaRuleTranslations, translation.Id),
                Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        });
    }

    /// <summary>
    /// The calendar-side rows go last: when resolving a device deployment fails (here a
    /// PlanningCaseSite naming a CheckListSite that does not exist, so the lookup throws),
    /// the delete reports failure and the CalendarConfiguration, tag link and row itself
    /// are still live, so the calendar still reaches the series and the delete can be
    /// retried (see BackendConfigurationCalendarService.DeleteEntireSeries).
    /// </summary>
    [Test]
    public async Task Delete_FailingRetractionLookup_LeavesCalendarSideRowsLive()
    {
        var (property, area) = await SeedPropertyAndArea();
        var areaRule = await SeedAreaRule(property.Id, area.Id);

        var planning = new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning
        {
            Enabled = true, RepeatEvery = 1,
            RepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Day,
            StartDate = DateTime.UtcNow.Date, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        var planningCase = new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningCase
        {
            PlanningId = planning.Id, Status = 66, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(
            new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningCaseSite
            {
                PlanningId = planning.Id, PlanningCaseId = planningCase.Id, MicrotingSdkeFormId = 0,
                MicrotingSdkCaseId = 0, MicrotingCheckListSitId = int.MaxValue, Status = 66,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = await SeedArp(areaRule, itemPlanningId: planning.Id);
        var tag = new AreaRulePlanningTag
        {
            AreaRulePlanningId = arp.Id, ItemPlanningTagId = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.AreaRulePlanningTags.AddAsync(tag);
        var calendarConfiguration = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(calendarConfiguration);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var result = await _wizard.DeleteTaskDeferredRetraction(arp.Id);

        Assert.That(result.Success, Is.False, "The unresolvable deployment must fail the delete.");
        var db = BackendConfigurationPnDbContext;
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await StateOf(db.CalendarConfigurations, calendarConfiguration.Id),
                Is.Not.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(await StateOf(db.AreaRulePlanningTags, tag.Id), Is.Not.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(await StateOf(db.AreaRulePlannings, arp.Id), Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        });
    }

    private static Task<string> StateOf<T>(IQueryable<T> set, int id) where T : PnBase
        => set.AsNoTracking().Where(x => x.Id == id).Select(x => x.WorkflowState).SingleAsync();

    private async Task<AreaRule> SeedAreaRule(int propertyId, int areaId)
    {
        var areaRule = new AreaRule
        {
            AreaId = areaId, PropertyId = propertyId, CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await areaRule.Create(BackendConfigurationPnDbContext!);
        return areaRule;
    }

    private async Task<AreaRulePlanning> SeedArp(AreaRule areaRule, int itemPlanningId)
    {
        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = areaRule.PropertyId, AreaId = areaRule.AreaId,
            ItemPlanningId = itemPlanningId, Status = true, StartDate = DateTime.UtcNow.Date.AddYears(-3),
            RepeatType = 1, RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await arp.Create(BackendConfigurationPnDbContext!);
        return arp;
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
            Name = $"DeleteWithoutPlanningProp-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        return (property, area);
    }
}
