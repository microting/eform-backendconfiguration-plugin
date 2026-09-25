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

using BackendConfiguration.Pn.Infrastructure.Enums;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using NSubstitute;
using BcCompliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;

/// <summary>
/// #1322 — a calendar task assigned ONLY to a team (worker tag) must keep the
/// status the user chose.
///
/// <para>
/// The calendar hands the task wizard its explicit sites only; the team links
/// are written afterwards as <c>AreaRulePlanningWorkerTag</c> rows. The wizard
/// downgraded every site-less Active task to NotActive, so a team-only task was
/// saved dimmed with <c>Planning.Enabled = false</c>, and
/// <c>CalendarAssignmentReconciliationService.ReconcileEventAsync</c> returned
/// on <c>!arp.Status</c> before it ever expanded the team. The calendar now
/// tells the wizard about the team (<see cref="TaskWizardCreateModel.HasWorkerTags"/>),
/// and the wizard downgrades only a task with neither sites nor a team.
/// </para>
///
/// <para>
/// These tests run the REAL wizard and the REAL reconciliation engine and
/// resolver against the fixture's SDK core. Only <see cref="IEventDeployService"/>
/// is substituted: the reconcile assertion is that the engine asks it to deploy
/// the occurrence to each team member linked to the property, which is exactly
/// the step the inactive status used to skip.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarTeamOnlyTaskStatusTests : TestBaseSetup
{
    // Minimal real eForm; the wizard's CreateTask reads the CheckList row.
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

    private Core _core = null!;
    private IEventDeployService _eventDeployService = null!;
    private BackendConfigurationTaskWizardService _wizardService = null!;
    private CalendarAssignmentReconciliationService _reconciliationService = null!;
    private BackendConfigurationCalendarService _calendarService = null!;

    /// <summary>
    /// This table is in no seed file, so links from an earlier test could attach
    /// to a reused AreaRulePlanning id (same reason as WorkerTagPropertyScopeTests).
    /// </summary>
    [SetUp]
    public async Task SetupServices()
    {
        await BackendConfigurationPnDbContext!.Database
            .ExecuteSqlRawAsync("DELETE FROM `AreaRulePlanningWorkerTags`;");

        // Core FIRST: SDK rows must not be inserted before Core.StartSqlOnly has
        // migrated the SDK schema (see TaskFolderIdPreservationTests.SeedSdkFolderAsync).
        _core = await GetCore();
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(_core));

        _eventDeployService = Substitute.For<IEventDeployService>();

        _wizardService = new BackendConfigurationTaskWizardService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext,
            coreHelper,
            ItemsPlanningPnDbContext!,
            _eventDeployService,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            TestContextLogger<BackendConfigurationTaskWizardService>.Instance);

        var membership = new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext);
        _reconciliationService = new CalendarAssignmentReconciliationService(
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext!, coreHelper,
            _eventDeployService,
            new CalendarAssignmentResolver(BackendConfigurationPnDbContext, membership),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<CalendarAssignmentReconciliationService>.Instance);

        _calendarService = new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext,
            coreHelper,
            _eventDeployService,
            ItemsPlanningPnDbContext!,
            _wizardService,
            _reconciliationService,
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            membership);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property A with Team A: Worker A and Worker B are members linked to the
    /// property. Outsider is a Team A member NOT linked to Property A, so a
    /// team expansion that ignored the property would show up.
    /// </summary>
    private sealed record Scenario(
        int PropertyId, int EformId, int FolderId, int TeamId,
        int WorkerA, int WorkerB, int Outsider);

    private async Task<Scenario> SeedScenario()
    {
        var eformId = await _core.TemplateCreate(await _core.TemplateFromXml(CommentTemplateXml));

        var folder = new Folder
        {
            Name = $"team-only-folder-{Guid.NewGuid()}", MicrotingUid = Random.Shared.Next(900_000, 999_999),
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Folders.AddAsync(folder);
        await MicrotingDbContext.SaveChangesAsync();

        // The wizard tags the new Planning with the property's items-planning
        // tag, and PlanningsTags has a real foreign key to PlanningTags, so the
        // property needs an existing tag rather than 0.
        var propertyPlanningTag = new PlanningTag
        {
            Name = $"Property A tag {Guid.NewGuid()}", WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(propertyPlanningTag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"Property A {Guid.NewGuid()}", ItemPlanningTagId = propertyPlanningTag.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var team = new Tag { Name = $"Team A {Guid.NewGuid()}", WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext.Tags.AddAsync(team);
        await MicrotingDbContext.SaveChangesAsync();

        var workerA = await SeedTeamMember(team.Id, "Worker A", property.Id);
        var workerB = await SeedTeamMember(team.Id, "Worker B", property.Id);
        var outsider = await SeedTeamMember(team.Id, "Outsider", propertyId: null);

        return new Scenario(property.Id, eformId, folder.Id, team.Id, workerA, workerB, outsider);
    }

    private async Task<int> SeedTeamMember(int teamId, string name, int? propertyId)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"{name} {Guid.NewGuid()}", MicrotingUid = null, LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
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

    /// <summary>A weekly series starting on a future Monday, so nothing is in the past.</summary>
    private static DateTime SeriesStart()
    {
        var today = DateTime.UtcNow.Date;
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return DateTime.SpecifyKind(today.AddDays(daysUntilMonday == 0 ? 7 : daysUntilMonday), DateTimeKind.Utc);
    }

    private static CalendarTaskCreateRequestModel BuildCreate(Scenario s, List<int> sites, List<int> teams) =>
        new()
        {
            PropertyId = s.PropertyId,
            FolderId = s.FolderId,
            EformId = s.EformId,
            StartDate = SeriesStart(),
            RepeatType = (int)RepeatType.Week,
            RepeatEvery = 1,
            Status = (int)TaskWizardStatuses.Active,
            Sites = sites,
            WorkerTagIds = teams,
            StartHour = 9.0,
            Duration = 1.0,
            Translates = [new CommonTranslationsModel { LanguageId = 1, Name = "Team task" }]
        };

    private static CalendarTaskUpdateRequestModel BuildEdit(
        Scenario s, int arpId, string scope, DateTime originalDate, List<int> sites, List<int> teams) =>
        new()
        {
            Id = arpId,
            Scope = scope,
            OriginalDate = originalDate.ToString("yyyy-MM-dd") + "T00:00:00Z",
            // Same day as the edited occurrence: a field edit, not a date move.
            StartDate = originalDate,
            PropertyId = s.PropertyId,
            FolderId = s.FolderId,
            EformId = s.EformId,
            RepeatType = (int)RepeatType.Week,
            RepeatEvery = 1,
            Status = (int)TaskWizardStatuses.Active,
            Sites = sites,
            WorkerTagIds = teams,
            TagIds = [],
            StartHour = 9.0,
            Duration = 1.0,
            Translates = [new CommonTranslationsModel { LanguageId = 1, Name = "Team task" }]
        };

    private async Task<int> CreateViaCalendar(CalendarTaskCreateRequestModel model)
    {
        var result = await _calendarService.CreateTask(model);
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.GreaterThan(0), "the calendar must correlate the created task");
        return result.Model;
    }

    private async Task<(bool ArpStatus, bool PlanningEnabled)> StatusOf(int arpId)
    {
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == arpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == arp.ItemPlanningId);
        return (arp.Status, planning.Enabled);
    }

    /// <summary>Both the AreaRulePlanning and its items-planning Planning carry <paramref name="active"/>.</summary>
    private async Task AssertSavedStatus(int arpId, bool active)
    {
        var (arpStatus, planningEnabled) = await StatusOf(arpId);
        Assert.Multiple(() =>
        {
            Assert.That(arpStatus, Is.EqualTo(active), "AreaRulePlanning.Status");
            Assert.That(planningEnabled, Is.EqualTo(active), "Planning.Enabled follows the saved status");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Create
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE #1322 regression: a team-only task is saved active, with an enabled
    /// Planning, and reconcile deploys the occurrence to the team members linked
    /// to the property. <b>Fails on the old code</b>: the wizard saved it
    /// NotActive and reconcile returned before resolving the team.
    /// </summary>
    [Test]
    public async Task CreateTask_TeamOnly_StaysActive_AndReconcileDeploysToThePropertysTeamMembers()
    {
        var s = await SeedScenario();

        var arpId = await CreateViaCalendar(BuildCreate(s, sites: [], teams: [s.TeamId]));

        await AssertSavedStatus(arpId, active: true);

        // An occurrence of the new task already deployed this week (the first
        // member's app sync materialises one); reconcile must fan it out to the team.
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == arpId);
        var occurrence = SeriesStart();
        await BackendConfigurationPnDbContext.Compliances.AddAsync(new BcCompliance
        {
            PlanningId = arp.ItemPlanningId, Deadline = occurrence, MicrotingSdkCaseId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        _eventDeployService.ClearReceivedCalls();

        await _reconciliationService.ReconcileEventAsync(arpId);

        var deployedTo = _eventDeployService.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IEventDeployService.EnsureComplianceForOccurrenceAsync))
            .Select(c => (int)c.GetArguments()[2]!)
            .ToList();
        Assert.That(deployedTo, Is.EquivalentTo(new[] { s.WorkerA, s.WorkerB }),
            "the team expands to its members linked to Property A, and only them");
    }

    /// <summary>
    /// Product decision on #1322: a team with no live member on the property is
    /// still an assignee. The task stays active with no recipients; members who
    /// join later get it through the live team link.
    /// </summary>
    [Test]
    public async Task CreateTask_TeamWithNoMemberOnTheProperty_StaysActive()
    {
        var s = await SeedScenario();
        var emptyTeam = new Tag { Name = $"Team B {Guid.NewGuid()}", WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext!.Tags.AddAsync(emptyTeam);
        await MicrotingDbContext.SaveChangesAsync();

        var arpId = await CreateViaCalendar(BuildCreate(s, sites: [], teams: [emptyTeam.Id]));

        await AssertSavedStatus(arpId, active: true);
    }

    /// <summary>The calendar still refuses a task with neither a worker nor a team.</summary>
    [Test]
    public async Task CreateTask_NoWorkerAndNoTeam_IsStillRejected()
    {
        var s = await SeedScenario();

        var result = await _calendarService.CreateTask(BuildCreate(s, sites: [], teams: []));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message,
            Is.EqualTo(new BackendConfigurationLocalizationService().GetString("AtLeastOneWorkerMustBeAssigned")));
    }

    /// <summary>
    /// The plain Task Wizard page has no teams: a site-less Active task is still
    /// downgraded to NotActive there, with a disabled Planning.
    /// </summary>
    [Test]
    public async Task WizardCreateTask_WithoutSitesOrTeams_IsStillDowngradedToNotActive()
    {
        var s = await SeedScenario();

        var result = await _wizardService.CreateTask(new TaskWizardCreateModel
        {
            PropertyId = s.PropertyId,
            FolderId = s.FolderId,
            EformId = s.EformId,
            StartDate = SeriesStart(),
            RepeatType = RepeatType.Week,
            RepeatEvery = 1,
            Status = TaskWizardStatuses.Active,
            Sites = [],
            Translates = [new CommonTranslationsModel { LanguageId = 1, Name = "Wizard task" }]
        });
        Assert.That(result.Success, Is.True, result.Message);

        var arpId = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .Where(x => x.PropertyId == s.PropertyId)
            .Select(x => x.Id)
            .SingleAsync();
        await AssertSavedStatus(arpId, active: false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Update
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scope "all": removing the last explicit worker while keeping the team
    /// leaves the task active. <b>Fails on the old code</b>: the wizard saw
    /// <c>Sites = []</c> and deactivated the series.
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeAll_RemovingTheLastWorkerButKeepingTheTeam_StaysActive()
    {
        var s = await SeedScenario();
        var arpId = await CreateViaCalendar(BuildCreate(s, sites: [s.WorkerA], teams: [s.TeamId]));
        Assert.That((await StatusOf(arpId)).ArpStatus, Is.True, "precondition: created active");

        var result = await _calendarService.UpdateTask(
            BuildEdit(s, arpId, "all", SeriesStart(), sites: [], teams: [s.TeamId]));
        Assert.That(result.Success, Is.True, result.Message);

        var (arpStatus, planningEnabled) = await StatusOf(arpId);
        var livePlanningSites = await BackendConfigurationPnDbContext!.PlanningSites
            .AsNoTracking()
            .CountAsync(x => x.AreaRulePlanningsId == arpId
                             && x.WorkflowState != Constants.WorkflowStates.Removed);
        Assert.Multiple(() =>
        {
            Assert.That(arpStatus, Is.True);
            Assert.That(planningEnabled, Is.True);
            Assert.That(livePlanningSites, Is.Zero, "the explicit worker was removed");
        });
    }

    /// <summary>
    /// Scope "all" re-toggling a team-only task that was saved inactive (the
    /// data the bug left behind) makes it active, and it stays active.
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeAll_ReactivatingATeamOnlyTask_StaysActive()
    {
        var s = await SeedScenario();
        var create = BuildCreate(s, sites: [], teams: [s.TeamId]);
        create.Status = (int)TaskWizardStatuses.NotActive;
        var arpId = await CreateViaCalendar(create);
        Assert.That((await StatusOf(arpId)).ArpStatus, Is.False, "precondition: saved inactive");

        var result = await _calendarService.UpdateTask(
            BuildEdit(s, arpId, "all", SeriesStart(), sites: [], teams: [s.TeamId]));
        Assert.That(result.Success, Is.True, result.Message);

        await AssertSavedStatus(arpId, active: true);
    }

    /// <summary>
    /// Scope "thisAndFollowing" does not write team links, so the wizard is told
    /// about the task's PERSISTED teams. Removing the last explicit worker while
    /// the task keeps its team leaves it active. <b>Fails on the old code.</b>
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeThisAndFollowing_RemovingTheLastWorkerButKeepingTheTeam_StaysActive()
    {
        var s = await SeedScenario();
        var arpId = await CreateViaCalendar(BuildCreate(s, sites: [s.WorkerA], teams: [s.TeamId]));

        // The second occurrence of the weekly series.
        var result = await _calendarService.UpdateTask(
            BuildEdit(s, arpId, "thisAndFollowing", SeriesStart().AddDays(7), sites: [], teams: [s.TeamId]));
        Assert.That(result.Success, Is.True, result.Message);

        await AssertSavedStatus(arpId, active: true);
    }

    /// <summary>
    /// The calendar still refuses an edit that leaves neither a worker nor a
    /// team, so the task is not silently deactivated.
    /// </summary>
    [Test]
    public async Task UpdateTask_RemovingEveryWorkerAndTeam_IsStillRejected()
    {
        var s = await SeedScenario();
        var arpId = await CreateViaCalendar(BuildCreate(s, sites: [s.WorkerA], teams: [s.TeamId]));

        var result = await _calendarService.UpdateTask(
            BuildEdit(s, arpId, "all", SeriesStart(), sites: [], teams: []));

        Assert.That(result.Success, Is.False);
        Assert.That((await StatusOf(arpId)).ArpStatus, Is.True, "the rejected edit changed nothing");
    }
}
