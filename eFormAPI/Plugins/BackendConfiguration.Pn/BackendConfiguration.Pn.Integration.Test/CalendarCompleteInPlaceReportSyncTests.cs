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
            NullLogger<BackendConfigurationCalendarService>.Instance);

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
}
