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
using BackendConfiguration.Pn.Services.BackendConfigurationCaseService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationReportService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.ExcelService;
using BackendConfiguration.Pn.Services.WordService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using SdkCaseEntity = Microting.eForm.Infrastructure.Data.Entities.Case;
using BackendPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

/// <summary>
/// CHARACTERIZATION tests — these pin what the four "complete a task" paths do
/// TODAY, warts included, so the upcoming shared-row completion work cannot
/// change them silently. Nothing here asserts desirable behaviour; several of
/// these assertions pin things that are arguably wrong (each is called out in
/// the per-test doc comment). Do NOT "fix" a failing test here by relaxing it —
/// a failure means production behaviour moved, and the move must be deliberate.
///
/// <para>The four paths:</para>
/// <list type="number">
/// <item><description><see cref="BackendConfigurationCalendarService.ToggleComplete"/>
/// in-place branch (no mandatory fields) — mutates the SDK Case + the ItemsPlanning
/// report rows.</description></item>
/// <item><description><see cref="BackendConfigurationCalendarService.ToggleComplete"/>
/// mandatory-fields branch — pure lookup, hands the frontend route params.</description></item>
/// <item><description><see cref="BackendConfigurationCaseService.Update"/> —
/// PUT api/backend-configuration-pn/cases.</description></item>
/// <item><description><see cref="BackendConfigurationReportService.Update"/> —
/// PUT api/backend-configuration-pn/report/cases.</description></item>
/// </list>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ComplianceCompletionCalendarAndReportTests : TestBaseSetup
{
    /// <summary>
    /// A minimal real eForm whose single Comment field is NOT mandatory, so
    /// BackendConfigurationCalendarService.HasMandatoryFields returns false and
    /// ToggleComplete takes the in-place completion branch. Copied from
    /// CalendarCompleteInPlaceReportSyncTests.CommentTemplateXml.
    /// </summary>
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

    /// <summary>
    /// Same shape as <see cref="CommentTemplateXml"/> but with
    /// <c>&lt;Mandatory&gt;true&lt;/Mandatory&gt;</c>, so HasMandatoryFields
    /// returns true and ToggleComplete must take the RequiresForm branch.
    /// </summary>
    private const string MandatoryCommentTemplateXml = @"
<?xml version='1.0' encoding='UTF-8'?>
<Main>
    <Id>9061</Id>
    <Repeated>0</Repeated>
    <Label>MandatoryCommentMain</Label>
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
            <Id>9061</Id>
            <Label>MandatoryCommentDataElement</Label>
            <Description><![CDATA[MandatoryCommentDataElementDescription]]></Description>
            <DisplayOrder>0</DisplayOrder>
            <ReviewEnabled>false</ReviewEnabled>
            <ManualSync>false</ManualSync>
            <ExtraFieldsEnabled>false</ExtraFieldsEnabled>
            <DoneButtonDisabled>false</DoneButtonDisabled>
            <ApprovalEnabled>false</ApprovalEnabled>
            <DataItemList>
                <DataItem type='Comment'>
                    <Id>73661</Id>
                    <Label>MandatoryCommentField</Label>
                    <Description><![CDATA[MandatoryCommentFieldDescription]]></Description>
                    <DisplayOrder>0</DisplayOrder>
                    <Multi>1</Multi>
                    <GeolocationEnabled>false</GeolocationEnabled>
                    <Split>false</Split>
                    <Value />
                    <ReadOnly>false</ReadOnly>
                    <Mandatory>true</Mandatory>
                    <Color>e8eaf6</Color>
                </DataItem>
            </DataItemList>
        </Element>
    </ElementList>
</Main>";

    // ---------------------------------------------------------------------
    // 1. ToggleComplete — in-place branch
    // ---------------------------------------------------------------------

    /// <summary>
    /// CHARACTERIZATION (arguably wrong): completing an occurrence in place
    /// NEVER soft-deletes the Compliance row. ToggleComplete
    /// (BackendConfigurationCalendarService:3101-3428) contains zero calls to
    /// <c>Compliance.Delete</c> — the compliance row survives verbatim, same
    /// WorkflowState, same Version, same MicrotingSdkCaseId. The occurrence
    /// disappears from the calendar only as a side effect of the READ path
    /// filtering completed occurrences out on <c>sdkCase.Status == 100</c>;
    /// the compliance table itself still reports the task as outstanding.
    /// </summary>
    [Test]
    public async Task ToggleComplete_InPlace_DoesNotSoftDeleteComplianceRow()
    {
        var s = await SeedInPlaceScenario("no-soft-delete", 5101);

        var seededCompliance = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);

        var result = await s.Service.ToggleComplete(s.ArpId, true, s.ComplianceId, null, null);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.RequiresForm, Is.False, "no-mandatory template must complete in place");

        // The SDK case IS completed — proving the completion really ran and the
        // compliance row simply was not part of it.
        var reloadedSdkCase = await MicrotingDbContext!.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);
        Assert.That(reloadedSdkCase.Status, Is.EqualTo(100), "precondition: the in-place completion ran");

        var reloadedCompliance = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);

        Assert.Multiple(() =>
        {
            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "TODAY the compliance row is NOT soft-deleted by an in-place completion");
            Assert.That(reloadedCompliance.Version, Is.EqualTo(seededCompliance.Version),
                "TODAY the compliance row is not written to at all — Version must be untouched");
            Assert.That(reloadedCompliance.UpdatedAt, Is.EqualTo(seededCompliance.UpdatedAt),
                "TODAY the compliance row is not written to at all — UpdatedAt must be untouched");
            Assert.That(reloadedCompliance.MicrotingSdkCaseId, Is.EqualTo(s.SdkCaseId),
                "the compliance still points at the (now completed) SDK case");
        });

        // And it is still returned by the plain "live compliances for this planning"
        // query the batch/report code uses.
        var liveForPlanning = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking()
            .Where(x => x.PlanningId == s.PlanningId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.Id)
            .ToListAsync();
        Assert.That(liveForPlanning, Does.Contain(s.ComplianceId),
            "the completed occurrence is still a LIVE compliance row after completion");
    }

    /// <summary>
    /// CHARACTERIZATION (arguably wrong): the in-place branch retracts NOTHING.
    /// ToggleComplete never calls <c>core.CaseDelete</c> — not for sibling
    /// workers sharing the planning, and not even for the worker who completed
    /// it. Every deployed SDK case keeps its MicrotingUid and stays
    /// WorkflowState=created, i.e. the eForm remains on every device including
    /// the completing worker's. Siblings additionally keep Status=66, so a
    /// second worker can complete the same shared row again afterwards.
    /// </summary>
    [Test]
    public async Task ToggleComplete_InPlace_DoesNotRetractAnyCase()
    {
        var s = await SeedInPlaceScenario("no-retract", 5102);
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        // A SECOND worker on the same property with their own deployed SDK case
        // for the same planning — the "shared row" shape.
        var siblingSite = new Site
        {
            Name = $"sibling-site-{Guid.NewGuid()}",
            MicrotingUid = 5103,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(siblingSite);
        await MicrotingDbContext.SaveChangesAsync();

        var siblingSdkCase = new SdkCaseEntity
        {
            SiteId = siblingSite.Id,
            CheckListId = s.TemplateId,
            MicrotingUid = 915103,
            Status = 66,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Cases.AddAsync(siblingSdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = s.PropertyId, WorkerId = siblingSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var result = await s.Service.ToggleComplete(s.ArpId, true, s.ComplianceId, null, null);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.RequiresForm, Is.False);

        var completedCase = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);
        var reloadedSibling = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == siblingSdkCase.Id);

        Assert.Multiple(() =>
        {
            // The completing worker's own case is completed but NOT retracted:
            // CaseDelete would flip WorkflowState to "removed".
            Assert.That(completedCase.Status, Is.EqualTo(100));
            Assert.That(completedCase.MicrotingUid, Is.EqualTo(s.SdkCaseMicrotingUid),
                "TODAY the completing worker's own case is never retracted — MicrotingUid survives");
            Assert.That(completedCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "TODAY core.CaseDelete is never called, so the case stays 'created', not 'removed'");

            // The sibling worker's case is not touched in any way.
            Assert.That(reloadedSibling.MicrotingUid, Is.Not.Null,
                "TODAY a sibling worker's case is never retracted either");
            Assert.That(reloadedSibling.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(reloadedSibling.Status, Is.EqualTo(66),
                "TODAY the sibling's shared-row case is still outstanding after completion");
        });
    }

    /// <summary>
    /// CHARACTERIZATION: the in-place branch dates the completion from
    /// <c>eventStart</c> = Compliance.Deadline day + the ARP's
    /// CalendarConfiguration.StartHour (falling back to 9.0), NOT from
    /// <c>DateTime.Now</c>. Both Case.DoneAt and Case.DoneAtUserModifiable get
    /// that value, so an occurrence scheduled 30 days ago is dated 30 days ago
    /// even when the user taps Complete today. Pinned for the default 9.0 hour
    /// and for a fractional non-default hour (14.5 -> 14:30).
    /// </summary>
    [Test]
    public async Task ToggleComplete_InPlace_UsesEventStartNotNow()
    {
        var defaultHour = await SeedInPlaceScenario("event-start-9", 5104, startHour: 9.0);
        var customHour = await SeedInPlaceScenario("event-start-1430", 5105, startHour: 14.5);

        var defaultResult = await defaultHour.Service
            .ToggleComplete(defaultHour.ArpId, true, defaultHour.ComplianceId, null, null);
        var customResult = await customHour.Service
            .ToggleComplete(customHour.ArpId, true, customHour.ComplianceId, null, null);

        Assert.That(defaultResult.Success, Is.True, defaultResult.Message);
        Assert.That(customResult.Success, Is.True, customResult.Message);

        var defaultCase = await MicrotingDbContext!.Cases
            .AsNoTracking().FirstAsync(x => x.Id == defaultHour.SdkCaseId);
        var customCase = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == customHour.SdkCaseId);

        var expectedDefault = defaultHour.DeadlineDay.AddHours(9);
        var expectedCustom = customHour.DeadlineDay.AddHours(14).AddMinutes(30);

        Assert.Multiple(() =>
        {
            Assert.That(defaultCase.DoneAt, Is.EqualTo(expectedDefault),
                "DoneAt is deadline day + StartHour (9.0), not now");
            Assert.That(defaultCase.DoneAtUserModifiable, Is.EqualTo(expectedDefault),
                "DoneAtUserModifiable mirrors DoneAt on the in-place path");

            Assert.That(customCase.DoneAt, Is.EqualTo(expectedCustom),
                "a non-default fractional StartHour (14.5) becomes 14:30 on the deadline day");
            Assert.That(customCase.DoneAtUserModifiable, Is.EqualTo(expectedCustom));

            // Explicitly NOT "now": the occurrences are 30 days in the past.
            Assert.That(defaultCase.DoneAt!.Value, Is.LessThan(DateTime.UtcNow.AddDays(-7)),
                "DoneAt must be the scheduled PAST moment, never DateTime.Now");
            Assert.That(customCase.DoneAt!.Value, Is.LessThan(DateTime.UtcNow.AddDays(-7)));
        });

        // The report rows are dated from the same eventStart.
        var defaultPlanningCase = await ItemsPlanningPnDbContext!.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == defaultHour.PlanningCaseId);
        var customPlanningCase = await ItemsPlanningPnDbContext.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == customHour.PlanningCaseId);

        Assert.Multiple(() =>
        {
            Assert.That(defaultPlanningCase.MicrotingSdkCaseDoneAt, Is.EqualTo(expectedDefault));
            Assert.That(customPlanningCase.MicrotingSdkCaseDoneAt, Is.EqualTo(expectedCustom));
        });
    }

    /// <summary>
    /// CHARACTERIZATION: ToggleComplete refuses to un-complete. <c>completed:
    /// false</c> short-circuits on the very first line
    /// (BackendConfigurationCalendarService:3118-3123) with
    /// Success=false / message key "UncompleteNotSupported" — before the ARP is
    /// even loaded, so nothing at all is written.
    /// </summary>
    [Test]
    public async Task ToggleComplete_Uncomplete_IsRejected()
    {
        var s = await SeedInPlaceScenario("uncomplete", 5106);

        var seededCompliance = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);

        var result = await s.Service.ToggleComplete(s.ArpId, false, s.ComplianceId, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "un-completing is not supported");
            // The test-project's IBackendConfigurationLocalizationService stub
            // echoes the key back, so this pins the exact message KEY.
            Assert.That(result.Message, Is.EqualTo("UncompleteNotSupported"));
            Assert.That(result.Model, Is.Null, "no result model on the rejection path");
        });

        await AssertNothingWrittenAsync(s, seededCompliance);
    }

    // ---------------------------------------------------------------------
    // 2. ToggleComplete — mandatory-fields branch
    // ---------------------------------------------------------------------

    /// <summary>
    /// CHARACTERIZATION: when the template has at least one mandatory field the
    /// calendar cannot complete in place. ToggleComplete returns Success=true
    /// with RequiresForm=true plus the route params the frontend needs, and
    /// writes NOTHING — the SDK case is still Status=66 with a null DoneAt, the
    /// PlanningCase/PlanningCaseSite are still 66, and the Compliance row is
    /// untouched (same Version).
    /// </summary>
    [Test]
    public async Task ToggleComplete_MandatoryFields_ReturnsRequiresFormAndWritesNothing()
    {
        var s = await SeedInPlaceScenario("mandatory", 5107, templateXml: MandatoryCommentTemplateXml);

        var seededCompliance = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);

        var result = await s.Service.ToggleComplete(s.ArpId, true, s.ComplianceId, null, null);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Model!.RequiresForm, Is.True,
                "a mandatory field forces the form route");
            Assert.That(result.Model.SdkCaseId, Is.EqualTo(s.SdkCaseId));
            Assert.That(result.Model.TemplateId, Is.EqualTo(s.TemplateId));
            Assert.That(result.Model.ComplianceId, Is.EqualTo(s.ComplianceId));
            Assert.That(result.Model.PropertyId, Is.EqualTo(s.PropertyId));
            // No explicit worker pick -> falls back to the case's deployed site.
            Assert.That(result.Model.WorkerId, Is.EqualTo(s.DeployedSiteId));
            Assert.That(result.Model.EventStart,
                Is.EqualTo(s.DeadlineDay.AddHours(9).ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
                    System.Globalization.CultureInfo.InvariantCulture)),
                "EventStart is the scheduled moment so the form can default DoneAt to it");
        });

        await AssertNothingWrittenAsync(s, seededCompliance);
    }

    /// <summary>
    /// CHARACTERIZATION: <paramref name="workerId"/> must be a LIVE
    /// PropertyWorker of the event's property. A PropertyWorker whose
    /// WorkflowState is "removed" is rejected with "SelectedWorkerNotAssignedToTask"
    /// before any mutation — the guard at
    /// BackendConfigurationCalendarService:3146-3158 filters on
    /// <c>WorkflowState != Removed</c>, and it runs before the compliance is
    /// even resolved.
    /// </summary>
    [Test]
    public async Task ToggleComplete_RemovedPropertyWorker_IsRejectedBeforeAnyMutation()
    {
        var s = await SeedInPlaceScenario("removed-worker", 5108);
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var retiredSite = new Site
        {
            Name = $"retired-site-{Guid.NewGuid()}",
            MicrotingUid = 5109,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(retiredSite);
        await MicrotingDbContext.SaveChangesAsync();

        // Assigned to the property once, but the assignment has been removed.
        await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = s.PropertyId, WorkerId = retiredSite.Id,
            WorkflowState = Constants.WorkflowStates.Removed, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var seededCompliance = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);

        var result = await s.Service.ToggleComplete(s.ArpId, true, s.ComplianceId, null, retiredSite.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "a removed PropertyWorker is not a live worker");
            Assert.That(result.Message, Is.EqualTo("SelectedWorkerNotAssignedToTask"));
        });

        await AssertNothingWrittenAsync(s, seededCompliance);
    }

    // ---------------------------------------------------------------------
    // 3. BackendConfigurationCaseService.Update (PUT .../cases)
    // ---------------------------------------------------------------------

    /// <summary>
    /// CHARACTERIZATION (arguably wrong, two ways):
    /// <list type="bullet">
    /// <item><description>the service sets <c>Case.Status = 100</c> and writes
    /// <c>Case.DoneAtUserModifiable</c>, but NEVER writes <c>Case.DoneAt</c> —
    /// the two diverge permanently after an admin edit;</description></item>
    /// <item><description>it never sets <c>PlanningCase.Status = 100</c>. It
    /// only stamps PlanningCase.MicrotingSdkCaseDoneAt, so reportsv2 (which
    /// filters PlanningCases on Status=100) still sees this case as
    /// outstanding.</description></item>
    /// </list>
    /// It also touches no Compliance row at all. Unlike the report service it
    /// DOES re-home the case when <c>model.SiteId != 0</c> — pinned here so the
    /// contrast with <see cref="ReportService_Update_CompletesCaseWithoutSiteReassignment"/>
    /// stays visible.
    /// </summary>
    [Test]
    public async Task CaseService_Update_CompletesCaseButLeavesPlanningCaseStatus()
    {
        var s = await SeedPlainCaseScenario("case-service", 5110);
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        userService.GetCurrentUserAsync().Returns(Task.FromResult(new EformUser { Id = 1 }));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var service = new BackendConfigurationCaseService(
            ItemsPlanningPnDbContext!,
            NullLogger<BackendConfigurationCaseService>.Instance,
            coreHelper,
            new BackendConfigurationLocalizationService(),
            userService);

        var seededCompliance = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);

        // The admin picks a new calendar day and re-assigns the case.
        var newDoneAtDay = new DateTime(2025, 6, 17, 0, 0, 0, DateTimeKind.Unspecified);
        var result = await service.Update(new ReplyRequest
        {
            Id = s.SdkCaseId,
            ElementList = new List<CaseEditRequest>(),
            DoneAt = newDoneAtDay,
            SiteId = s.OtherSiteId
        });

        Assert.That(result.Success, Is.True, result.Message);

        var reloadedCase = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);
        var reloadedPlanningCase = await ItemsPlanningPnDbContext!.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseId);
        var reloadedCompliance = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);

        // DoneAtUserModifiable = the model's DATE with the original DoneAt's
        // TIME-of-day grafted on (BackendConfigurationCaseService.cs:62-66).
        var expectedUserModifiable = new DateTime(
            newDoneAtDay.Year, newDoneAtDay.Month, newDoneAtDay.Day,
            s.SeededDoneAt.Hour, s.SeededDoneAt.Minute, s.SeededDoneAt.Second);

        Assert.Multiple(() =>
        {
            Assert.That(reloadedCase.Status, Is.EqualTo(100), "the SDK case is marked completed");
            Assert.That(reloadedCase.DoneAt, Is.EqualTo(s.SeededDoneAt),
                "TODAY Case.DoneAt is NEVER written by this path — only DoneAtUserModifiable is");
            Assert.That(reloadedCase.DoneAtUserModifiable, Is.EqualTo(expectedUserModifiable),
                "DoneAtUserModifiable takes the model's date + the original DoneAt's time");
            Assert.That(reloadedCase.SiteId, Is.EqualTo(s.OtherSiteId),
                "this path DOES re-home the case when model.SiteId != 0");

            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(66),
                "TODAY PlanningCase.Status is NEVER set to 100 by this path — it is left "
                + "exactly as seeded");
            Assert.That(reloadedPlanningCase.MicrotingSdkCaseDoneAt, Is.EqualTo(expectedUserModifiable),
                "only the done-at stamp is mirrored onto the PlanningCase");

            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "TODAY this path touches no Compliance row");
            Assert.That(reloadedCompliance.Version, Is.EqualTo(seededCompliance.Version),
                "TODAY this path touches no Compliance row");
        });
    }

    // ---------------------------------------------------------------------
    // 4. BackendConfigurationReportService.Update (PUT .../report/cases)
    // ---------------------------------------------------------------------

    /// <summary>
    /// CHARACTERIZATION (arguably wrong, two ways):
    /// <list type="bullet">
    /// <item><description>this path NEVER touches <c>Case.SiteId</c> — the
    /// incoming <c>model.SiteId</c> is silently ignored, unlike
    /// <see cref="BackendConfigurationCaseService.Update"/> which re-homes the
    /// case. The same gesture attributes differently depending on which screen
    /// it came from;</description></item>
    /// <item><description>it never sets <c>PlanningCase.Status = 100</c>
    /// either — only Case.Status and the done-at stamps.</description></item>
    /// </list>
    /// It also touches no Compliance row.
    /// </summary>
    [Test]
    public async Task ReportService_Update_CompletesCaseWithoutSiteReassignment()
    {
        var s = await SeedPlainCaseScenario("report-service", 5111);
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        userService.GetCurrentUserAsync().Returns(Task.FromResult(new EformUser { Id = 1 }));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var service = new BackendConfigurationReportService(
            new BackendConfigurationLocalizationService(),
            NullLogger<BackendConfigurationReportService>.Instance,
            coreHelper,
            Substitute.For<IWordService>(),
            Substitute.For<IExcelService>(),
            Substitute.For<ICasePostBaseService>(),
            ItemsPlanningPnDbContext!,
            userService,
            BackendConfigurationPnDbContext!);

        var seededCompliance = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);

        var newDoneAtDay = new DateTime(2025, 6, 17, 0, 0, 0, DateTimeKind.Unspecified);
        var result = await service.Update(new ReplyRequest
        {
            Id = s.SdkCaseId,
            ElementList = new List<CaseEditRequest>(),
            DoneAt = newDoneAtDay,
            // Deliberately a DIFFERENT site than the case is deployed to — this
            // path ignores it entirely.
            SiteId = s.OtherSiteId
        });

        Assert.That(result.Success, Is.True, result.Message);

        var reloadedCase = await MicrotingDbContext.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);
        var reloadedPlanningCase = await ItemsPlanningPnDbContext!.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseId);
        var reloadedPlanningCaseSite = await ItemsPlanningPnDbContext.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseSiteId);
        var reloadedCompliance = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);

        var expectedUserModifiable = new DateTime(
            newDoneAtDay.Year, newDoneAtDay.Month, newDoneAtDay.Day,
            s.SeededDoneAt.Hour, s.SeededDoneAt.Minute, s.SeededDoneAt.Second);

        Assert.Multiple(() =>
        {
            Assert.That(reloadedCase.Status, Is.EqualTo(100), "the SDK case is marked completed");
            Assert.That(reloadedCase.SiteId, Is.EqualTo(s.DeployedSiteId),
                "TODAY this path NEVER writes Case.SiteId — model.SiteId is ignored");
            Assert.That(reloadedCase.DoneAt, Is.EqualTo(s.SeededDoneAt),
                "TODAY Case.DoneAt is never written by this path either");
            Assert.That(reloadedCase.DoneAtUserModifiable, Is.EqualTo(expectedUserModifiable));

            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(66),
                "TODAY PlanningCase.Status is NEVER set to 100 by this path — it is left "
                + "exactly as seeded");
            Assert.That(reloadedPlanningCase.MicrotingSdkCaseDoneAt, Is.EqualTo(expectedUserModifiable));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseDoneAt, Is.EqualTo(expectedUserModifiable));
            Assert.That(reloadedPlanningCaseSite.Status, Is.EqualTo(66),
                "TODAY PlanningCaseSite.Status is not promoted either when the row already exists");

            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created),
                "TODAY this path touches no Compliance row");
            Assert.That(reloadedCompliance.Version, Is.EqualTo(seededCompliance.Version),
                "TODAY this path touches no Compliance row");
        });
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Asserts that a ToggleComplete call left every row of the scenario
    /// exactly as seeded. Shared by the reject/RequiresForm tests.
    /// </summary>
    private async Task AssertNothingWrittenAsync(InPlaceScenario s, Compliance seededCompliance)
    {
        var reloadedSdkCase = await MicrotingDbContext!.Cases
            .AsNoTracking().FirstAsync(x => x.Id == s.SdkCaseId);
        var reloadedCompliance = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == s.ComplianceId);
        var reloadedPlanningCase = await ItemsPlanningPnDbContext!.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseId);
        var reloadedPlanningCaseSite = await ItemsPlanningPnDbContext.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.Id == s.PlanningCaseSiteId);

        Assert.Multiple(() =>
        {
            Assert.That(reloadedSdkCase.Status, Is.EqualTo(66), "SDK case must still be outstanding");
            Assert.That(reloadedSdkCase.DoneAt, Is.Null, "SDK case must have no done-at");
            Assert.That(reloadedSdkCase.DoneAtUserModifiable, Is.Null);
            Assert.That(reloadedSdkCase.SiteId, Is.EqualTo(s.DeployedSiteId));

            Assert.That(reloadedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(reloadedCompliance.Version, Is.EqualTo(seededCompliance.Version));
            Assert.That(reloadedCompliance.UpdatedAt, Is.EqualTo(seededCompliance.UpdatedAt));

            Assert.That(reloadedPlanningCase.Status, Is.EqualTo(66));
            Assert.That(reloadedPlanningCase.MicrotingSdkCaseDoneAt, Is.Null);
            Assert.That(reloadedPlanningCaseSite.Status, Is.EqualTo(66));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseDoneAt, Is.Null);
        });
    }

    private sealed class InPlaceScenario
    {
        public BackendConfigurationCalendarService Service = null!;
        public int ArpId;
        public int ComplianceId;
        public int SdkCaseId;
        public int SdkCaseMicrotingUid;
        public int DeployedSiteId;
        public int PropertyId;
        public int PlanningId;
        public int TemplateId;
        public int PlanningCaseId;
        public int PlanningCaseSiteId;
        public DateTime DeadlineDay;
    }

    /// <summary>
    /// Seeds a live past compliance occurrence wired to an ARP with a
    /// CalendarConfiguration, plus the ItemsPlanning report rows. Adapted from
    /// CalendarCompleteInPlaceReportSyncTests.SeedInPlaceScenario, with the
    /// template XML and the calendar StartHour made parameters, and a
    /// MicrotingUid on the SDK case so retraction is observable.
    /// Every name is Guid-suffixed and every id is returned so callers scope
    /// their assertions — the SQL dumps replay once per fixture.
    /// </summary>
    private async Task<InPlaceScenario> SeedInPlaceScenario(
        string tag, int microtingUid, double startHour = 9.0, string templateXml = CommentTemplateXml)
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var template = await core.TemplateFromXml(templateXml);
        var templateId = await core.TemplateCreate(template);

        var sdkSite = new Site
        {
            Name = $"deployed-site-{tag}-{Guid.NewGuid()}",
            MicrotingUid = microtingUid,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        var caseMicrotingUid = 900000 + microtingUid;
        var sdkCase = new SdkCaseEntity
        {
            SiteId = sdkSite.Id,
            CheckListId = templateId,
            MicrotingUid = caseMicrotingUid,
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
            Name = $"Characterization-{tag}-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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

        await BackendConfigurationPnDbContext.PlanningSites.AddAsync(new BackendPlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = property.Id, WorkerId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = startHour, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(calConfig);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Created via Create() so Version/UpdatedAt carry realistic values —
        // that is what makes "Version unchanged" a meaningful assertion.
        var compliance = new Compliance
        {
            PlanningId = planning.Id, PropertyId = property.Id, AreaId = area.Id,
            Deadline = pastDate, StartDate = pastDate.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = templateId
        };
        await compliance.Create(BackendConfigurationPnDbContext);

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
            SdkCaseMicrotingUid = caseMicrotingUid,
            DeployedSiteId = sdkSite.Id,
            PropertyId = property.Id,
            PlanningId = planning.Id,
            TemplateId = templateId,
            PlanningCaseId = planningCase.Id,
            PlanningCaseSiteId = planningCaseSite.Id,
            DeadlineDay = pastDate
        };
    }

    private sealed class PlainCaseScenario
    {
        public int SdkCaseId;
        public int DeployedSiteId;
        public int OtherSiteId;
        public int PlanningCaseId;
        public int PlanningCaseSiteId;
        public int ComplianceId;
        public DateTime SeededDoneAt;
    }

    /// <summary>
    /// Seeds an already-answered SDK case (non-null DoneAt so the DoneAt-vs-
    /// DoneAtUserModifiable divergence is observable) with exactly one
    /// PlanningCase / PlanningCaseSite pointing at it — both services look those
    /// up with Single/SingleOrDefault on MicrotingSdkCaseId — plus a live
    /// Compliance row so "touches no Compliance" can be asserted.
    /// </summary>
    private async Task<PlainCaseScenario> SeedPlainCaseScenario(string tag, int microtingUid)
    {
        var core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var template = await core.TemplateFromXml(CommentTemplateXml);
        var templateId = await core.TemplateCreate(template);

        var sdkSite = new Site
        {
            Name = $"plain-site-{tag}-{Guid.NewGuid()}",
            MicrotingUid = microtingUid,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        var otherSite = new Site
        {
            Name = $"plain-other-site-{tag}-{Guid.NewGuid()}",
            MicrotingUid = microtingUid + 500,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddRangeAsync(sdkSite, otherSite);
        await MicrotingDbContext.SaveChangesAsync();

        // A fixed, obviously-not-"now" answered-at moment with a distinctive
        // time-of-day, so the grafted DoneAtUserModifiable is unambiguous.
        var seededDoneAt = new DateTime(2024, 3, 4, 7, 8, 9, DateTimeKind.Unspecified);

        var sdkCase = new SdkCaseEntity
        {
            SiteId = sdkSite.Id,
            CheckListId = templateId,
            MicrotingUid = 900000 + microtingUid,
            Status = 66,
            DoneAt = seededDoneAt,
            DoneAtUserModifiable = seededDoneAt,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
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
            Name = $"Characterization-{tag}-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = seededDoneAt.Date, RelatedEFormId = templateId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

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

        var compliance = new Compliance
        {
            PlanningId = planning.Id, PropertyId = property.Id, AreaId = area.Id,
            Deadline = seededDoneAt.Date, StartDate = seededDoneAt.Date.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = templateId
        };
        await compliance.Create(BackendConfigurationPnDbContext);

        return new PlainCaseScenario
        {
            SdkCaseId = sdkCase.Id,
            DeployedSiteId = sdkSite.Id,
            OtherSiteId = otherSite.Id,
            PlanningCaseId = planningCase.Id,
            PlanningCaseSiteId = planningCaseSite.Id,
            ComplianceId = compliance.Id,
            SeededDoneAt = seededDoneAt
        };
    }
}
