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

using System.Globalization;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationCompliancesService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

/// <summary>
/// #1352 — a calendar task assigned ONLY to a team (an <c>AreaRulePlanningWorkerTag</c>
/// row and no <c>PlanningSites</c>) could not be completed from the web.
///
/// <para>
/// <see cref="BackendConfigurationCalendarService.PrepareComplete"/> (the combined
/// complete modal) and <see cref="BackendConfigurationCalendarService.ToggleComplete"/>
/// without a worker picked the site to materialise the on-demand case on from
/// <c>arp.PlanningSites</c> only, so a team-only task returned <c>NoAssignedWorker</c>.
/// Both now fall back to the event's effective assignees
/// (<see cref="CalendarAssignmentResolver.ResolveEffectiveSiteIdsAsync"/>: explicit sites
/// plus live team members linked to the event's property) and pick the lowest site id.
/// That site is only where the case lives; whoever the modal picks is written to the
/// case on save (<see cref="BackendConfigurationCompliancesService.UpdateFromCalendar"/>).
/// </para>
///
/// <para>
/// Everything runs for real against the fixture's SDK core: the calendar service, the
/// <see cref="EventDeployService"/> that materialises the case, the membership service
/// and the compliance save. Only collaborators these paths never reach are substituted.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarTeamOnlyCompleteTests : TestBaseSetup
{
    // Minimal real eForm with no mandatory field, so ToggleComplete completes in place.
    // Copied from CalendarPrepareCompleteTests.CommentTemplateXml.
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

    private const int CompletedStatus = 100;

    private eFormCore.Core _core = null!;
    private Language _language = null!;
    private IEFormCoreService _coreHelper = null!;
    private IUserService _userService = null!;
    private BackendConfigurationCalendarService _calendarService = null!;

    /// <summary>
    /// None of these tables is in a seed file, so rows from an earlier test would attach
    /// to a reused AreaRulePlanning id (same reason as CalendarTeamOnlyTaskStatusTests
    /// and CalendarPrepareCompleteTests.SeedGraphAsync).
    /// </summary>
    [SetUp]
    public async Task SetupServices()
    {
        await BackendConfigurationPnDbContext!.Database
            .ExecuteSqlRawAsync("DELETE FROM `AreaRulePlanningWorkerTags`;");
        await BackendConfigurationPnDbContext.Database
            .ExecuteSqlRawAsync("DELETE FROM CalendarConfigurations");
        await BackendConfigurationPnDbContext.Database
            .ExecuteSqlRawAsync("DELETE FROM CalendarOccurrenceExceptions");

        // Core FIRST: SDK rows must not be inserted before Core has migrated the SDK schema.
        _core = await GetCore();
        _language = await MicrotingDbContext!.Languages.FirstAsync();

        _userService = Substitute.For<IUserService>();
        _userService.UserId.Returns(1);
        _userService.GetCurrentUserLanguage().Returns(Task.FromResult(_language));

        _coreHelper = Substitute.For<IEFormCoreService>();
        _coreHelper.GetCore().Returns(Task.FromResult(_core));

        // EventDeployService only resolves the calendar service lazily for paths these
        // tests never take; a substitute satisfies the provider.
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IBackendConfigurationCalendarService>());
        var eventDeployService = new EventDeployService(
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext!, _coreHelper,
            services.BuildServiceProvider(), TestContextLogger<EventDeployService>.Instance);

        _calendarService = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), _userService,
            BackendConfigurationPnDbContext, _coreHelper, eventDeployService,
            ItemsPlanningPnDbContext!, Substitute.For<IBackendConfigurationTaskWizardService>(),
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(_coreHelper, BackendConfigurationPnDbContext));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property A with a weekly task (no PlanningSites) and Team A with Worker A and
    /// Worker B. Worker A is created first, so it has the lower site id.
    /// </summary>
    private sealed record Scenario(
        int ArpId, int PlanningId, int PropertyId, int TeamId, int WorkerA, int WorkerB);

    /// <param name="membersOnProperty">
    /// false leaves both team members without a PropertyWorker link to Property A.
    /// </param>
    private async Task<Scenario> SeedTeamOnlyTaskAsync(bool membersOnProperty = true)
    {
        var templateId = await _core.TemplateCreate(await _core.TemplateFromXml(CommentTemplateXml));
        var startDate = DateTime.UtcNow.Date.AddDays(-14);

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"Property A {Guid.NewGuid()}", ItemPlanningTagId = 0,
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
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week, StartDate = startDate,
            RelatedEFormId = templateId, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = 1, RepeatEvery = 1, DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var team = new Tag { Name = $"Team A {Guid.NewGuid()}", WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext!.Tags.AddAsync(team);
        await MicrotingDbContext.SaveChangesAsync();

        var propertyId = membersOnProperty ? property.Id : (int?)null;
        var workerA = await SeedTeamMemberAsync(team.Id, "Worker A", propertyId);
        var workerB = await SeedTeamMemberAsync(team.Id, "Worker B", propertyId);

        // The team link — and deliberately NO PlanningSites row.
        await BackendConfigurationPnDbContext.AreaRulePlanningWorkerTags.AddAsync(new AreaRulePlanningWorkerTag
        {
            AreaRulePlanningId = arp.Id, TagId = team.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Scenario(arp.Id, planning.Id, property.Id, team.Id, workerA, workerB);
    }

    /// <summary>
    /// A live SDK site carrying the team. It needs a MicrotingUid: the on-demand deploy
    /// creates its case with <c>CaseCreateLocalOnly(..., site.MicrotingUid, ...)</c>.
    /// </summary>
    private async Task<int> SeedTeamMemberAsync(int teamId, string name, int? propertyId)
    {
        var site = new Site
        {
            Name = $"{name} {Guid.NewGuid()}", MicrotingUid = Random.Shared.Next(600_000, 699_999),
            LanguageId = _language.Id, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        await MicrotingDbContext.SiteTags.AddAsync(new SiteTag
        {
            TagId = teamId, SiteId = site.Id, WorkflowState = Constants.WorkflowStates.Created
        });
        await MicrotingDbContext.SaveChangesAsync();

        if (propertyId.HasValue)
        {
            await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(new PropertyWorker
            {
                PropertyId = propertyId.Value, WorkerId = site.Id,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
            await BackendConfigurationPnDbContext.SaveChangesAsync();
        }

        return site.Id;
    }

    private static DateTime Occurrence() => DateTime.UtcNow.Date.AddDays(3);

    private static string Iso(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string NoAssignedWorker() =>
        new BackendConfigurationLocalizationService().GetString("NoAssignedWorker");

    private async Task<Case> ReadCaseAsync(int caseId) =>
        await MicrotingDbContext!.Cases.AsNoTracking().FirstAsync(x => x.Id == caseId);

    private async Task<int> ComplianceCountAsync(int planningId) =>
        await BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
            .CountAsync(c => c.PlanningId == planningId && c.WorkflowState != Constants.WorkflowStates.Removed);

    // ─────────────────────────────────────────────────────────────────────────
    // PrepareComplete
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE #1352 regression: the complete modal can open a team-only task. The case is
    /// materialised on the lowest-id team member linked to the property.
    /// <b>Fails on the old code</b> with <c>NoAssignedWorker</c>.
    /// </summary>
    [Test]
    public async Task PrepareComplete_TeamOnlyTask_MaterialisesTheCaseOnTheLowestIdTeamMember()
    {
        var s = await SeedTeamOnlyTaskAsync();

        var result = await _calendarService.PrepareComplete(s.ArpId, null, Iso(Occurrence()));

        Assert.That(result.Success, Is.True, result.Message);
        var sdkCase = await ReadCaseAsync(result.Model!.SdkCaseId);
        var compliance = await BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
            .FirstAsync(x => x.Id == result.Model.ComplianceId);
        Assert.Multiple(() =>
        {
            Assert.That(compliance.PlanningId, Is.EqualTo(s.PlanningId));
            Assert.That(compliance.PropertyId, Is.EqualTo(s.PropertyId));
            Assert.That(compliance.MicrotingSdkCaseId, Is.EqualTo(sdkCase.Id));
            Assert.That(compliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(sdkCase.SiteId, Is.EqualTo(Math.Min(s.WorkerA, s.WorkerB)),
                "the case lives on the lowest-id team member linked to the property");
            Assert.That(result.Model.AssignedSiteId, Is.EqualTo(sdkCase.SiteId));
            Assert.That(sdkCase.Status, Is.Not.EqualTo(CompletedStatus), "PrepareComplete completes nothing");
        });
    }

    /// <summary>
    /// The modal's picked worker is written to the case on save: completing the case
    /// PrepareComplete put on Worker A as Worker B, through the same
    /// <c>UpdateFromCalendar</c> the calendar's save calls, stores Worker B.
    /// </summary>
    [Test]
    public async Task PrepareComplete_TeamOnlyTask_SavedAsTheOtherMember_StoresThatMemberOnTheCase()
    {
        var s = await SeedTeamOnlyTaskAsync();
        var prepared = await _calendarService.PrepareComplete(s.ArpId, null, Iso(Occurrence()));
        Assert.That(prepared.Success, Is.True, prepared.Message);
        var caseId = prepared.Model!.SdkCaseId;
        var complianceId = prepared.Model.ComplianceId;
        var otherMember = Math.Max(s.WorkerA, s.WorkerB);
        Assert.That(prepared.Model.AssignedSiteId, Is.Not.EqualTo(otherMember), "precondition");

        await AddRetractionDecoyAsync(caseId);

        var compliancesService = new BackendConfigurationCompliancesService(
            ItemsPlanningPnDbContext!, BackendConfigurationPnDbContext!, _userService,
            new BackendConfigurationLocalizationService(), _coreHelper, TimePlanningPnDbContext!);
        var doneAt = Occurrence().AddHours(9);
        var saved = await compliancesService.UpdateFromCalendar(new ReplyRequest
        {
            Id = caseId,
            Label = "team-only-complete",
            DoneAt = doneAt,
            IsDoneAtEditable = true,
            ExtraId = complianceId,
            SiteId = otherMember,
            ElementList = new List<CaseEditRequest>()
        });

        Assert.That(saved.Success, Is.True, saved.Message);
        var sdkCase = await ReadCaseAsync(caseId);
        var compliance = await BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
            .FirstAsync(x => x.Id == complianceId);
        var planningCaseSite = await ItemsPlanningPnDbContext!.PlanningCaseSites.AsNoTracking()
            .FirstAsync(x => x.MicrotingSdkCaseId == caseId);
        Assert.Multiple(() =>
        {
            Assert.That(sdkCase.SiteId, Is.EqualTo(otherMember), "the picked worker, not the materialisation site");
            Assert.That(sdkCase.Status, Is.EqualTo(CompletedStatus));
            Assert.That(compliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(planningCaseSite.Status, Is.EqualTo(CompletedStatus));
            Assert.That(planningCaseSite.DoneByUserId, Is.EqualTo(otherMember));
        });
    }

    /// <summary>
    /// A team none of whose members work on the event's property still has nobody to
    /// materialise for: <c>NoAssignedWorker</c>, and nothing is written.
    /// </summary>
    [Test]
    public async Task PrepareComplete_TeamWithNoMemberOnTheProperty_ReturnsNoAssignedWorker()
    {
        var s = await SeedTeamOnlyTaskAsync(membersOnProperty: false);

        var result = await _calendarService.PrepareComplete(s.ArpId, null, Iso(Occurrence()));

        var complianceCount = await ComplianceCountAsync(s.PlanningId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo(NoAssignedWorker()));
            Assert.That(complianceCount, Is.Zero, "nothing may be materialised");
        });
    }

    /// <summary>
    /// Regression guard: a task with an explicit worker still materialises on that
    /// PlanningSite, even when a team member linked to the property has a lower id.
    /// </summary>
    [Test]
    public async Task PrepareComplete_TaskWithAPlanningSite_StillMaterialisesOnThePlanningSite()
    {
        var s = await SeedTeamOnlyTaskAsync();
        var explicitWorker = Math.Max(s.WorkerA, s.WorkerB);
        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = s.ArpId, SiteId = explicitWorker,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var result = await _calendarService.PrepareComplete(s.ArpId, null, Iso(Occurrence()));

        Assert.That(result.Success, Is.True, result.Message);
        var sdkCase = await ReadCaseAsync(result.Model!.SdkCaseId);
        Assert.That(sdkCase.SiteId, Is.EqualTo(explicitWorker),
            "the first explicit PlanningSite wins over the lower-id team member");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ToggleComplete (no worker picked — the mobile/gRPC shape)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ToggleComplete without a worker materialises a team-only occurrence on the
    /// lowest-id team member and, with no mandatory field, completes it in place.
    /// <b>Fails on the old code</b> with <c>NoAssignedWorker</c>.
    /// </summary>
    [Test]
    public async Task ToggleComplete_TeamOnlyTask_NoWorker_MaterialisesAndCompletes()
    {
        var s = await SeedTeamOnlyTaskAsync();

        var result = await _calendarService.ToggleComplete(s.ArpId, true, null, Iso(Occurrence()));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.RequiresForm, Is.False, "the comment eForm has no mandatory field");

        var compliance = await BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
            .SingleAsync(x => x.PlanningId == s.PlanningId);
        var sdkCase = await ReadCaseAsync(compliance.MicrotingSdkCaseId);
        Assert.Multiple(() =>
        {
            Assert.That(sdkCase.SiteId, Is.EqualTo(Math.Min(s.WorkerA, s.WorkerB)));
            Assert.That(sdkCase.Status, Is.EqualTo(CompletedStatus));
        });
    }

    /// <summary>ToggleComplete keeps refusing a team with nobody on the property.</summary>
    [Test]
    public async Task ToggleComplete_TeamWithNoMemberOnTheProperty_ReturnsNoAssignedWorker()
    {
        var s = await SeedTeamOnlyTaskAsync(membersOnProperty: false);

        var result = await _calendarService.ToggleComplete(s.ArpId, true, null, Iso(Occurrence()));

        var complianceCount = await ComplianceCountAsync(s.PlanningId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo(NoAssignedWorker()));
            Assert.That(complianceCount, Is.Zero, "nothing may be materialised");
        });
    }

    /// <summary>
    /// <c>UpdateFromCalendar</c> hands <c>core.CaseDelete(microtingUid)</c> to a
    /// fire-and-forget task, and that is a real call to the Microting platform. A second
    /// Case row sharing the MicrotingUid makes the SDK's by-uid lookup throw locally
    /// before the communicator is reached — the offline trick documented on
    /// ComplianceCompletionLegacyPathsTests. The decoy is referenced by no Compliance or
    /// PlanningCaseSite, so nothing under test reads it.
    /// </summary>
    private async Task AddRetractionDecoyAsync(int caseId)
    {
        var sdkCase = await ReadCaseAsync(caseId);
        if (sdkCase.MicrotingUid == null)
        {
            return;
        }

        await MicrotingDbContext!.Cases.AddAsync(new Case
        {
            SiteId = sdkCase.SiteId, CheckListId = sdkCase.CheckListId, Status = sdkCase.Status,
            MicrotingUid = sdkCase.MicrotingUid, WorkflowState = Constants.WorkflowStates.Created
        });
        await MicrotingDbContext.SaveChangesAsync();
    }
}
