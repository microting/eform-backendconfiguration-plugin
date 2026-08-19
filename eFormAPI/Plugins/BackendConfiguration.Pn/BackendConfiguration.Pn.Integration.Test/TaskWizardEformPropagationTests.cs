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

using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.EventDeployService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using NSubstitute;

/// <summary>
/// Covers the adjacent fix from the 2026-08-19 eForm-propagation spec:
/// <c>Planning.RelatedEFormId</c> / <c>RelatedEFormName</c> must be written in
/// EVERY <c>oldStatus</c> branch of
/// <see cref="BackendConfigurationTaskWizardService.UpdateTask"/>, not only in
/// <c>case true when areaRulePlanning.Status</c>.
///
/// Why it matters: <c>EventDeployService</c> writes
/// <c>Compliance.MicrotingSdkeFormId</c> from the eForm the SDK case was
/// actually created from, while the deploy that follows a REACTIVATION goes
/// through <c>PairItemWithSiteHelper.Pair(..., updateModel.EformId, ...)</c>.
/// A <c>Planning</c> left on the stale <c>RelatedEFormId</c> therefore disagreed
/// with both the AreaRule and the freshly deployed cases — defect 2 in the
/// spec's "secondary defects" section.
///
/// The fixture runs the REAL wizard service against the inherited contexts with
/// a real <c>eFormCore.Core</c> (needed for <c>ReadeForm</c> inside
/// <c>Pair</c>); only <see cref="IEventDeployService"/> is substituted, so the
/// repair pass's invocation can be asserted without redeploying anything.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskWizardEformPropagationTests : TestBaseSetup
{
    // Minimal real eForm — the wizard only needs a readable CheckList id.
    // Same XML the other calendar integration fixtures use.
    private const string CommentTemplateXml = @"
<?xml version='1.0' encoding='UTF-8'?>
<Main>
    <Id>9060</Id>
    <Repeated>0</Repeated>
    <Label>CommentMain</Label>
    <StartDate>2017-07-07</StartDate>
    <EndDate>2027-07-07</EndDate>
    <Language>da</Language>
    <MultiApproval>false</MultiApproval>
    <FastNavigation>false</FastNavigation>
    <Review>false</Review>
    <Summary>false</Summary>
    <DisplayOrder>0</DisplayOrder>
    <ElementList>
        <Element type='DataElement'>
            <Id>9060</Id>
            <Label>CommentDataElement</Label>
            <Description><![CDATA[CommentDataElementDescription]]></Description>
            <DisplayOrder>0</DisplayOrder>
            <ReviewEnabled>false</ReviewEnabled>
            <ManualSync>false</ManualSync>
            <ExtraFieldsEnabled>false</ExtraFieldsEnabled>
            <DoneButtonDisabled>false</DoneButtonDisabled>
            <ApprovalEnabled>false</ApprovalEnabled>
            <DataItemList>
                <DataItem type='Comment'>
                    <Id>73660</Id>
                    <Label>CommentField</Label>
                    <Description><![CDATA[CommentFieldDescription]]></Description>
                    <DisplayOrder>0</DisplayOrder>
                    <Multi>1</Multi>
                    <GeolocationEnabled>false</GeolocationEnabled>
                    <Split>false</Split>
                    <Value />
                    <ReadOnly>false</ReadOnly>
                    <Mandatory>false</Mandatory>
                    <Color>e8eaf6</Color>
                </DataItem>
            </DataItemList>
        </Element>
    </ElementList>
</Main>";

    // ------------------------------------------------------------------
    // 8. Reactivation branch (inactive → active).
    // ------------------------------------------------------------------
    [Test]
    public async Task UpdateTask_ReactivationBranch_WritesPlanningRelatedEFormIdMatchingTheAreaRule()
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var oldTemplateId = await core.TemplateCreate(await core.TemplateFromXml(CommentTemplateXml));
        var newTemplateId = await core.TemplateCreate(await core.TemplateFromXml(CommentTemplateXml));
        Assert.That(newTemplateId, Is.Not.EqualTo(oldTemplateId));

        // PairItemWithSiteHelper.Pair resolves the AreaRule's folder in the SDK.
        var folder = new Folder
        {
            Name = "eform-propagation-folder", MicrotingUid = 987_654,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Folders.AddAsync(folder);
        await MicrotingDbContext.SaveChangesAsync();

        var sdkSite = new Site
        {
            Name = "wizard-reactivation-site", MicrotingUid = 7101, LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"WizardEform-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // The task was created with the OLD eForm and is currently INACTIVE.
        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = oldTemplateId, FolderId = folder.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Deliberately relative to now — the wizard rejects nothing here, but a
        // hardcoded absolute date would rot and, once past, would make
        // PairItemWithSiteHelper take its deploy branch instead of the
        // "series has not started yet" short-circuit.
        var startDate = DateTime.UtcNow.Date.AddDays(30);

        var planning = new Planning
        {
            Enabled = false, RepeatEvery = 1,
            RepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Week,
            StartDate = startDate,
            // The stale value the reactivation branch used to leave behind.
            RelatedEFormId = oldTemplateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate,
            Status = false, // ← INACTIVE: this is what makes the update take the reactivation branch
            RepeatType = 2, RepeatEvery = 1, FolderId = folder.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var eventDeployService = Substitute.For<IEventDeployService>();

        var wizardService = new BackendConfigurationTaskWizardService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext,
            coreHelper,
            ItemsPlanningPnDbContext,
            eventDeployService,
            NullLogger<BackendConfigurationTaskWizardService>.Instance);

        // Reactivate AND change the eForm in the same save — the combination
        // that produced a Planning disagreeing with its own AreaRule.
        var result = await wizardService.UpdateTask(new TaskWizardCreateModel
        {
            Id = arp.Id,
            PropertyId = property.Id,
            FolderId = folder.Id,
            EformId = newTemplateId,
            StartDate = startDate,
            RepeatType = BackendConfiguration.Pn.Infrastructure.Enums.RepeatType.Week,
            RepeatEvery = 1,
            Status = BackendConfiguration.Pn.Infrastructure.Enums.TaskWizardStatuses.Active,
            Sites = [sdkSite.Id],
            TagIds = [],
            Translates = [],
            ComplianceEnabled = false
        });

        Assert.That(result.Success, Is.True, result.Message);

        var reloadedAreaRule = await BackendConfigurationPnDbContext.AreaRules
            .AsNoTracking().FirstAsync(x => x.Id == areaRule.Id);
        var reloadedPlanning = await ItemsPlanningPnDbContext.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == planning.Id);
        var reloadedArp = await BackendConfigurationPnDbContext.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == arp.Id);

        Assert.Multiple(() =>
        {
            Assert.That(reloadedArp.Status, Is.True, "the task must actually have been reactivated");
            Assert.That(reloadedAreaRule.EformId, Is.EqualTo(newTemplateId));
            // The fix: the Planning follows the AreaRule in the reactivation
            // branch too, so the deploy that Pair performs and the Compliance
            // row that follows it agree on the eForm.
            Assert.That(reloadedPlanning.RelatedEFormId, Is.EqualTo(newTemplateId));
            Assert.That(reloadedPlanning.RelatedEFormId, Is.EqualTo(reloadedAreaRule.EformId));
        });

        // The repair pass is invoked with the captured old id, so occurrences
        // deployed before the deactivation are swapped too.
        await eventDeployService.Received(1).RepairEformForOpenOccurrencesAsync(
            Arg.Any<AreaRulePlanning>(),
            oldTemplateId,
            newTemplateId,
            Arg.Any<CancellationToken>());
    }
    /// <summary>
    /// Seeds Folder → Area → Property → AreaRule → Planning → AreaRulePlanning
    /// for an ACTIVE task carrying <paramref name="oldTemplateId"/>, and returns
    /// everything the two tests below need.
    /// </summary>
    private async Task<(Folder Folder, Area Area, Property Property, AreaRule AreaRule, Planning Planning,
            AreaRulePlanning Arp)>
        SeedActiveTaskAsync(string tag, int oldTemplateId, DateTime startDate)
    {
        var folder = new Folder
        {
            Name = $"eform-{tag}-folder", MicrotingUid = 900_000 + Math.Abs(tag.GetHashCode() % 90_000),
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Folders.AddAsync(folder);
        await MicrotingDbContext.SaveChangesAsync();

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"WizardEform-{tag}-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = oldTemplateId, FolderId = folder.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1,
            RepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType.Week,
            StartDate = startDate, SdkFolderId = folder.Id, RelatedEFormId = oldTemplateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate,
            Status = true, // ← ACTIVE
            RepeatType = 2, RepeatEvery = 1, FolderId = folder.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (folder, area, property, areaRule, planning, arp);
    }

    /// <summary>
    /// Adds one SDK site and every linkage an assigned worker of the task has:
    /// a BC PlanningSite, an items-planning PlanningSite and an active
    /// PropertyWorker.
    /// </summary>
    private async Task<Site> SeedAssignedSiteAsync(
        Property property, AreaRulePlanning arp, Planning planning, Language language, string name, int microtingUid)
    {
        var site = new Site
        {
            Name = name, MicrotingUid = microtingUid, LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(
            new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
            {
                AreaRulePlanningsId = arp.Id, SiteId = site.Id,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
        await BackendConfigurationPnDbContext.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = property.Id, WorkerId = site.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await ItemsPlanningPnDbContext!.PlanningSites.AddAsync(
            new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
            {
                PlanningId = planning.Id, SiteId = site.Id,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        return site;
    }

    private async Task<Case> SeedOpenSdkCaseAsync(Site site, int checkListId)
    {
        // MicrotingUid deliberately null — calendar cases are created with
        // CaseCreateLocalOnly, and it keeps every retraction offline.
        var sdkCase = new Case
        {
            SiteId = site.Id, CheckListId = checkListId, Status = 33,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkCase;
    }

    private BackendConfigurationTaskWizardService BuildWizardService(
        eFormCore.Core core, Language language, IEventDeployService eventDeployService)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        return new BackendConfigurationTaskWizardService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext,
            coreHelper,
            ItemsPlanningPnDbContext,
            eventDeployService,
            NullLogger<BackendConfigurationTaskWizardService>.Instance);
    }

    // ------------------------------------------------------------------
    // Deactivate the task AND change the eForm in the same save.
    //
    // The deactivation branch retracts the PlanningCases and CaseDeletes the
    // cloud cases, but never marks the PlanningCaseSite rows removed — and for
    // calendar-created cases the CaseDelete is a no-op (synthetic MicrotingUid
    // the cloud has never seen), so the SDK Case rows stay live too. If the
    // eForm repair pass ran after that switch it would sweep exactly those rows
    // up and create a BRAND-NEW live case for a task the user just deactivated.
    // The fix is to skip the repair entirely when the task ends up inactive.
    // ------------------------------------------------------------------
    [Test]
    public async Task UpdateTask_DeactivateAndChangeEformInOneSave_NeverRunsTheEformRepair()
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var oldTemplateId = await core.TemplateCreate(await core.TemplateFromXml(CommentTemplateXml));
        var newTemplateId = await core.TemplateCreate(await core.TemplateFromXml(CommentTemplateXml));

        var startDate = DateTime.UtcNow.Date.AddDays(-30);
        var seed = await SeedActiveTaskAsync("deactivate", oldTemplateId, startDate);
        var site = await SeedAssignedSiteAsync(
            seed.Property, seed.Arp, seed.Planning, language, "wizard-deactivate-site", 7201);

        var openCase = await SeedOpenSdkCaseAsync(site, oldTemplateId);

        // The deployment rows the repair sweep would have picked up. The parent
        // PlanningCase is seeded Removed ONLY so the deactivation branch's own
        // cloud-delete loop (which needs a reachable CheckListSite and would
        // otherwise attempt a network round-trip) skips it — the LIVE
        // PlanningCaseSite below is what the sweep keys on, and it is untouched.
        var planningCase = new PlanningCase
        {
            PlanningId = seed.Planning.Id, Status = 66, MicrotingSdkeFormId = oldTemplateId,
            WorkflowState = Constants.WorkflowStates.Removed, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var planningCaseSite = new PlanningCaseSite
        {
            PlanningId = seed.Planning.Id, PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = site.Id, MicrotingSdkeFormId = oldTemplateId,
            MicrotingSdkCaseId = openCase.Id, Status = 66,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var eventDeployService = Substitute.For<IEventDeployService>();
        var wizardService = BuildWizardService(core, language, eventDeployService);

        var result = await wizardService.UpdateTask(new TaskWizardCreateModel
        {
            Id = seed.Arp.Id,
            PropertyId = seed.Property.Id,
            FolderId = seed.Folder.Id,
            EformId = newTemplateId,
            StartDate = startDate,
            RepeatType = BackendConfiguration.Pn.Infrastructure.Enums.RepeatType.Week,
            RepeatEvery = 1,
            Status = BackendConfiguration.Pn.Infrastructure.Enums.TaskWizardStatuses.NotActive,
            Sites = [],
            TagIds = [],
            Translates = [],
            ComplianceEnabled = false
        });

        Assert.That(result.Success, Is.True, result.Message);

        var reloadedArp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == seed.Arp.Id);
        var reloadedAreaRule = await BackendConfigurationPnDbContext.AreaRules
            .AsNoTracking().FirstAsync(x => x.Id == seed.AreaRule.Id);
        var reloadedPlanning = await ItemsPlanningPnDbContext.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == seed.Planning.Id);
        var casesForSite = await MicrotingDbContext.Cases.AsNoTracking().CountAsync(x => x.SiteId == site.Id);

        Assert.Multiple(() =>
        {
            Assert.That(reloadedArp.Status, Is.False, "the task must actually have been deactivated");
            // The definition rows still follow the edit — only the redeploy is skipped.
            Assert.That(reloadedAreaRule.EformId, Is.EqualTo(newTemplateId));
            Assert.That(reloadedPlanning.RelatedEFormId, Is.EqualTo(newTemplateId));
            Assert.That(reloadedPlanning.Enabled, Is.False);
            Assert.That(casesForSite, Is.EqualTo(1), "no new case may be created for a deactivated task");
        });

        // The actual fix: the repair pass is not even asked to run.
        await eventDeployService.DidNotReceive().RepairEformForOpenOccurrencesAsync(
            Arg.Any<AreaRulePlanning>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // Unassign a worker AND change the eForm in the same save.
    //
    // The active branch removes the worker's PlanningSites and CaseDeletes its
    // cloud case, but leaves the PlanningCaseSite live — so without the
    // assignee guard inside the repair pass the removed worker would be handed
    // a brand-new case carrying the NEW eForm. Runs the REAL EventDeployService
    // so the whole save is exercised end to end.
    // ------------------------------------------------------------------
    [Test]
    public async Task UpdateTask_UnassignWorkerAndChangeEformInOneSave_RemovedWorkerGetsNoNewCase()
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var oldTemplateId = await core.TemplateCreate(await core.TemplateFromXml(CommentTemplateXml));
        var newTemplateId = await core.TemplateCreate(await core.TemplateFromXml(CommentTemplateXml));

        var startDate = DateTime.UtcNow.Date.AddDays(-30);
        var seed = await SeedActiveTaskAsync("unassign", oldTemplateId, startDate);
        var removedSite = await SeedAssignedSiteAsync(
            seed.Property, seed.Arp, seed.Planning, language, "wizard-unassign-removed", 7202);
        var keptSite = await SeedAssignedSiteAsync(
            seed.Property, seed.Arp, seed.Planning, language, "wizard-unassign-kept", 7203);

        var removedCase = await SeedOpenSdkCaseAsync(removedSite, oldTemplateId);
        var keptCase = await SeedOpenSdkCaseAsync(keptSite, oldTemplateId);

        var planningCase = new PlanningCase
        {
            PlanningId = seed.Planning.Id, Status = 66, MicrotingSdkeFormId = oldTemplateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var removedPcs = new PlanningCaseSite
        {
            PlanningId = seed.Planning.Id, PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = removedSite.Id, MicrotingSdkeFormId = oldTemplateId,
            MicrotingSdkCaseId = removedCase.Id, Status = 66,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        var keptPcs = new PlanningCaseSite
        {
            PlanningId = seed.Planning.Id, PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = keptSite.Id, MicrotingSdkeFormId = oldTemplateId,
            MicrotingSdkCaseId = keptCase.Id, Status = 66,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(removedPcs);
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(keptPcs);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IBackendConfigurationCalendarService>());
        var sp = services.BuildServiceProvider();
        var coreHelperForDeploy = Substitute.For<IEFormCoreService>();
        coreHelperForDeploy.GetCore().Returns(Task.FromResult(core));
        var eventDeployService = new EventDeployService(
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, coreHelperForDeploy, sp,
            NullLogger<EventDeployService>.Instance);

        var wizardService = BuildWizardService(core, language, eventDeployService);

        var result = await wizardService.UpdateTask(new TaskWizardCreateModel
        {
            Id = seed.Arp.Id,
            PropertyId = seed.Property.Id,
            FolderId = seed.Folder.Id,
            EformId = newTemplateId,
            StartDate = startDate,
            RepeatType = BackendConfiguration.Pn.Infrastructure.Enums.RepeatType.Week,
            RepeatEvery = 1,
            Status = BackendConfiguration.Pn.Infrastructure.Enums.TaskWizardStatuses.Active,
            Sites = [keptSite.Id], // ← removedSite is dropped
            TagIds = [],
            Translates = [],
            ComplianceEnabled = false
        });

        Assert.That(result.Success, Is.True, result.Message);

        var reloadedRemovedCase = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == removedCase.Id);
        var reloadedKeptCase = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == keptCase.Id);
        var reloadedRemovedPcs = await ItemsPlanningPnDbContext.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.Id == removedPcs.Id);
        var reloadedKeptPcs = await ItemsPlanningPnDbContext.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.Id == keptPcs.Id);
        var keptNewCase = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == reloadedKeptPcs.MicrotingSdkCaseId);
        var casesForRemovedSite = await MicrotingDbContext.Cases
            .AsNoTracking().CountAsync(x => x.SiteId == removedSite.Id);
        var casesForKeptSite = await MicrotingDbContext.Cases
            .AsNoTracking().CountAsync(x => x.SiteId == keptSite.Id);

        Assert.Multiple(() =>
        {
            // The removed worker: form withdrawn, nothing handed back.
            Assert.That(reloadedRemovedCase.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Created),
                "the unassigned worker's case must be retracted");
            Assert.That(casesForRemovedSite, Is.EqualTo(1),
                "the unassigned worker must NOT receive a new case carrying the new eForm");
            Assert.That(reloadedRemovedPcs.MicrotingSdkCaseId, Is.EqualTo(removedCase.Id));

            // The worker who stays assigned: swapped onto the new eForm.
            Assert.That(reloadedKeptCase.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(casesForKeptSite, Is.EqualTo(2));
            Assert.That(reloadedKeptPcs.MicrotingSdkCaseId, Is.Not.EqualTo(keptCase.Id));
            Assert.That(reloadedKeptPcs.MicrotingSdkeFormId, Is.EqualTo(newTemplateId));
            Assert.That(keptNewCase.CheckListId, Is.EqualTo(newTemplateId));
            Assert.That(keptNewCase.SiteId, Is.EqualTo(keptSite.Id));
        });
    }
}
