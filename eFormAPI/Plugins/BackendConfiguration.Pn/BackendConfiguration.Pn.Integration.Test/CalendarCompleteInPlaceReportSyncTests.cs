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

using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

/// <summary>
/// Regression coverage for the bug where completing a calendar event with a
/// NO-mandatory-fields eForm via the in-place ToggleComplete path updated only
/// the SDK Case and left PlanningCase/PlanningCaseSite untouched — so the
/// completion never appeared in reportsv2 (which filters on
/// PlanningCase.MicrotingSdkCaseDoneAt). The fix mirrors the gRPC completion
/// sync (EventsGrpcService:1668-1705), writing eventStart (the scheduled,
/// possibly past, moment) so the row lands in the correct report period.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarCompleteInPlaceReportSyncTests : TestBaseSetup
{
    // A minimal real eForm with a single NON-mandatory Comment field. Drives
    // HasMandatoryFields(template) == false so ToggleComplete completes the
    // case IN PLACE (RequiresForm == false) instead of returning the form path.
    // Copied from EventDeployServiceTest.CommentTemplateXml.
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

    [Test]
    public async Task ToggleComplete_InPlace_PastEvent_SyncsPlanningCaseDoneAt()
    {
        // Boot a real SDK Core and create a NO-mandatory-fields template so the
        // in-place completion branch is taken.
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var template = await core.TemplateFromXml(CommentTemplateXml);
        var templateId = await core.TemplateCreate(template);

        var sdkSite = new Site
        {
            Name = "inplace-report-sync-site",
            MicrotingUid = 4344,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        // SDK case backing the compliance occurrence — references the
        // no-mandatory template via CheckListId.
        var sdkCase = new Microting.eForm.Infrastructure.Data.Entities.Case
        {
            SiteId = sdkSite.Id,
            CheckListId = templateId,
            Status = 66,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        // Scheduled in the PAST (30 days ago). eventStart = pastDate + 9h.
        var pastDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc);
        var expectedDoneAt = pastDate.AddHours(9);

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"InPlaceReportSync-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = templateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week, StartDate = pastDate,
            RelatedEFormId = templateId, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = pastDate, Status = true,
            RepeatType = 1, RepeatEvery = 1, DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planningSite = new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.PlanningSites.AddAsync(planningSite);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(calConfig);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Live (non-removed) compliance occurrence the user "clicks complete" on.
        var compliance = new Compliance
        {
            PlanningId = planning.Id, PropertyId = property.Id, AreaId = area.Id,
            Deadline = pastDate, StartDate = pastDate.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = templateId,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // The planning rows reportsv2 reads — initially NOT done.
        var planningCase = new PlanningCase
        {
            PlanningId = planning.Id, Status = 66,
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = templateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var planningCaseSite = new PlanningCaseSite
        {
            PlanningCaseId = planningCase.Id, PlanningId = planning.Id, Status = 66,
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkSiteId = (int)sdkSite.MicrotingUid!,
            MicrotingCheckListSitId = templateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        taskWizardService.DeleteTask(Arg.Any<int>()).Returns(Task.FromResult(new OperationResult(true)));

        var service = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext, taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>());

        // Act: complete the past occurrence in place.
        var result = await service.ToggleComplete(arp.Id, true, compliance.Id, null, null);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.Not.Null);
        Assert.That(result.Model!.RequiresForm, Is.False, "no-mandatory template must complete in place");

        // Reload from the DB (fresh, untracked) and assert the planning rows the
        // report reads now carry the scheduled-past done date.
        var reloadedCase = await ItemsPlanningPnDbContext.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == planningCase.Id);
        var reloadedSite = await ItemsPlanningPnDbContext.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.Id == planningCaseSite.Id);

        Assert.Multiple(() =>
        {
            Assert.That(reloadedCase.Status, Is.EqualTo(100), "PlanningCase must be marked done");
            Assert.That(reloadedCase.MicrotingSdkCaseDoneAt, Is.Not.Null,
                "PlanningCase.MicrotingSdkCaseDoneAt must be populated (report filter field)");
            Assert.That(reloadedCase.MicrotingSdkCaseDoneAt, Is.EqualTo(expectedDoneAt),
                "done date must be the scheduled PAST event-start, not now");
            Assert.That(reloadedCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Processed),
                "PlanningCase WorkflowState must be set to Processed");
            Assert.That(reloadedCase.DoneByUserId, Is.EqualTo(sdkCase.SiteId!.Value),
                "PlanningCase DoneByUserId must be the completing site");

            Assert.That(reloadedSite.Status, Is.EqualTo(100), "PlanningCaseSite must be marked done");
            Assert.That(reloadedSite.MicrotingSdkCaseDoneAt, Is.EqualTo(expectedDoneAt),
                "PlanningCaseSite done date must be the scheduled PAST event-start");
            Assert.That(reloadedSite.DoneByUserId, Is.EqualTo(sdkCase.SiteId!.Value),
                "PlanningCaseSite DoneByUserId must be the completing site");
        });
    }

    /// <summary>
    /// The calendar's worker picker lists every active PropertyWorker of the
    /// event's property, and ToggleComplete attributes the completion to the
    /// picked site on the in-place path: the SDK case is re-homed
    /// (Case.SiteId) and the report rows (PlanningCase/PlanningCaseSite
    /// DoneByUserId/Name) carry the picked site — matching what the
    /// compliance-form route does via UpdateCase.
    /// </summary>
    [Test]
    public async Task ToggleComplete_InPlace_PickedPropertyWorker_AttributesToPickedSite()
    {
        var s = await SeedInPlaceScenario("picked-worker", 4444);
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        // A second property worker — NOT the site the case was deployed to.
        var pickedSite = new Site
        {
            Name = "picked-property-worker-site",
            MicrotingUid = 4445,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(pickedSite);
        await MicrotingDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = s.PropertyId, WorkerId = pickedSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var result = await s.Service.ToggleComplete(s.ArpId, true, s.ComplianceId, null, pickedSite.Id);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.RequiresForm, Is.False, "no-mandatory template must complete in place");

        var reloadedSdkCase = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);
        var reloadedCase = await ItemsPlanningPnDbContext!.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseId);
        var reloadedSite = await ItemsPlanningPnDbContext.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseSiteId);

        Assert.Multiple(() =>
        {
            Assert.That(reloadedSdkCase.Status, Is.EqualTo(100));
            Assert.That(reloadedSdkCase.SiteId, Is.EqualTo(pickedSite.Id),
                "SDK case must be re-homed to the picked worker (parity with the form route)");
            Assert.That(reloadedCase.DoneByUserId, Is.EqualTo(pickedSite.Id),
                "PlanningCase DoneByUserId must be the picked site");
            Assert.That(reloadedCase.DoneByUserName, Is.EqualTo(pickedSite.Name),
                "PlanningCase DoneByUserName must be the picked site's name");
            Assert.That(reloadedSite.DoneByUserId, Is.EqualTo(pickedSite.Id),
                "PlanningCaseSite DoneByUserId must be the picked site");
            Assert.That(reloadedSite.DoneByUserName, Is.EqualTo(pickedSite.Name),
                "PlanningCaseSite DoneByUserName must be the picked site's name");
        });
    }

    /// <summary>
    /// A workerId that is NOT an active PropertyWorker of the event's property
    /// is rejected up front — before any compliance/case mutation — so a
    /// crafted request can never attribute a completion to an unrelated site.
    /// </summary>
    [Test]
    public async Task ToggleComplete_PickedWorkerNotPropertyWorker_IsRejectedWithoutMutation()
    {
        var s = await SeedInPlaceScenario("rogue-worker", 4446);
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        // A live SDK site that is NOT a PropertyWorker of the property.
        var rogueSite = new Site
        {
            Name = "rogue-unrelated-site",
            MicrotingUid = 4447,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(rogueSite);
        await MicrotingDbContext.SaveChangesAsync();

        var result = await s.Service.ToggleComplete(s.ArpId, true, s.ComplianceId, null, rogueSite.Id);

        Assert.That(result.Success, Is.False, "non-property-worker pick must be rejected");

        var reloadedSdkCase = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);
        var reloadedCase = await ItemsPlanningPnDbContext!.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseId);

        Assert.Multiple(() =>
        {
            Assert.That(reloadedSdkCase.Status, Is.EqualTo(66), "SDK case must be untouched");
            Assert.That(reloadedSdkCase.SiteId, Is.EqualTo(s.DeployedSiteId), "SDK case site must be untouched");
            Assert.That(reloadedCase.Status, Is.EqualTo(66), "PlanningCase must be untouched");
        });
    }

    private sealed class InPlaceScenario
    {
        public BackendConfigurationCalendarService Service = null!;
        public int ArpId;
        public int ComplianceId;
        public int SdkCaseId;
        public int DeployedSiteId;
        public int PropertyId;
        public int PlanningCaseId;
        public int PlanningCaseSiteId;
    }

    /// <summary>
    /// Seeds the same fixture as
    /// <see cref="ToggleComplete_InPlace_PastEvent_SyncsPlanningCaseDoneAt"/>:
    /// a no-mandatory-fields template, a deployed SDK case, and a live past
    /// compliance occurrence wired to an ARP with a CalendarConfiguration —
    /// so ToggleComplete takes the in-place branch. Distinct names/uids per
    /// call keep scenarios independent within the fixture.
    /// </summary>
    private async Task<InPlaceScenario> SeedInPlaceScenario(string tag, int microtingUid)
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var template = await core.TemplateFromXml(CommentTemplateXml);
        var templateId = await core.TemplateCreate(template);

        var sdkSite = new Site
        {
            Name = $"deployed-site-{tag}",
            MicrotingUid = microtingUid,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        var sdkCase = new Microting.eForm.Infrastructure.Data.Entities.Case
        {
            SiteId = sdkSite.Id,
            CheckListId = templateId,
            Status = 66,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var pastDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc);

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"InPlace-{tag}-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = templateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week, StartDate = pastDate,
            RelatedEFormId = templateId, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = pastDate, Status = true,
            RepeatType = 1, RepeatEvery = 1, DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planningSite = new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.PlanningSites.AddAsync(planningSite);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // The deployed site is itself a PropertyWorker (the normal shape —
        // assignees are always property workers).
        await BackendConfigurationPnDbContext.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = property.Id, WorkerId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(calConfig);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var compliance = new Compliance
        {
            PlanningId = planning.Id, PropertyId = property.Id, AreaId = area.Id,
            Deadline = pastDate, StartDate = pastDate.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = templateId,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planningCase = new PlanningCase
        {
            PlanningId = planning.Id, Status = 66,
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = templateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var planningCaseSite = new PlanningCaseSite
        {
            PlanningCaseId = planningCase.Id, PlanningId = planning.Id, Status = 66,
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkSiteId = (int)sdkSite.MicrotingUid!,
            MicrotingCheckListSitId = templateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        taskWizardService.DeleteTask(Arg.Any<int>()).Returns(Task.FromResult(new OperationResult(true)));

        var service = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext, taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>());

        return new InPlaceScenario
        {
            Service = service,
            ArpId = arp.Id,
            ComplianceId = compliance.Id,
            SdkCaseId = sdkCase.Id,
            DeployedSiteId = sdkSite.Id,
            PropertyId = property.Id,
            PlanningCaseId = planningCase.Id,
            PlanningCaseSiteId = planningCaseSite.Id
        };
    }
}
