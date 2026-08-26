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
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
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
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;
using BcCompliance = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Compliance;
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;
using IpPlanningSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;

/// <summary>
/// Reproduces the production reassignment data-loss reported for tenant 1111 on
/// 11 Aug 2026: reassigning a calendar event from worker A to worker B via the
/// edit modal leaves the occurrence with ZERO live <c>Compliance</c> rows.
///
/// <para>
/// Domain rule (authoritative): there is exactly ONE <c>Compliance</c> row per
/// event-occurrence, SHARED by every assigned worker — whoever completes it
/// completes the event. The <c>UNIQUE (PlanningId, Deadline)</c> index encodes
/// that rule and is correct.
/// </para>
///
/// <para>
/// Root cause — <see cref="CalendarAssignmentReconciliationService.ReconcileEventAsync"/>
/// deploys ADDITIONS (step e) before retracting REMOVALS (step f), and then
/// throws away what the deploy handed back:
/// <list type="number">
///   <item>B's deploy runs while A's <c>Compliance</c> row is still live.</item>
///   <item><c>EventDeployService.EnsureComplianceRowAsync</c> INSERTs and hits
///         the <c>(PlanningId, Deadline)</c> unique index.</item>
///   <item>The duplicate-key catch re-reads with
///         <c>WorkflowState != 'removed'</c>, finds A's row, sees
///         <c>MicrotingSdkCaseId &gt; 0</c> so skips the adopt branch, and hands
///         back A's row as if it were B's.</item>
///   <item><c>CalendarAssignmentReconciliationService</c> DISCARDS that return
///         value, so B's freshly created SDK case is never adopted onto the
///         shared row.</item>
///   <item>Step (f) then soft-deletes A's row.</item>
/// </list>
/// Net effect: zero live <c>Compliance</c> rows for the occurrence, a tombstone
/// squatting on the unique slot, and B holding an orphaned SDK case. Production
/// evidence: compliance 334 has exactly two versions — created 22 Jul pointing
/// at case 326 (A's) and removed 11 Aug still pointing at case 326, never
/// adopted onto B's case.
/// </para>
///
/// <para>
/// The correct behaviour, asserted below, is that the single shared row SURVIVES
/// the reassignment and ends up pointing at an SDK case owned by B.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ComplianceReassignmentTests : TestBaseSetup
{
    // Minimal real eForm. Content is irrelevant — the deploy path only needs a
    // real CheckList id it can hand to ReadeForm + CaseCreateLocalOnly. Copied
    // verbatim from EventDeployServiceEformRepairTests.CommentTemplateXml.
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

    /// <summary>SDK <c>Case.Status</c> the deploy path leaves on a live, unanswered case.</summary>
    private const int OpenCaseStatus = 33;

    /// <summary><c>PlanningCase</c> / <c>PlanningCaseSite</c> status written by the deploy path.</summary>
    private const int OpenPlanningStatus = 66;

    private sealed class Scenario
    {
        public AreaRulePlanning Arp = null!;
        public Property Property = null!;
        public Area Area = null!;
        public Planning Planning = null!;
        public Language Language = null!;
        public int TemplateId;
        public CalendarAssignmentReconciliationService Engine = null!;
    }

    /// <summary>
    /// Seeds Area → Property → AreaRule → Planning → AreaRulePlanning plus one
    /// real eForm template, and wires a REAL <see cref="EventDeployService"/>
    /// behind a REAL <see cref="CalendarAssignmentReconciliationService"/> — the
    /// bug only reproduces when the genuine deploy path runs and actually hits
    /// the (PlanningId, Deadline) unique index.
    /// Adapted from EventDeployServiceEformRepairTests.SeedScenarioAsync.
    /// </summary>
    private async Task<Scenario> SeedScenarioAsync(DateTime arpStartDate)
    {
        var core = await GetCore();

        // GetCore() seeds the SDK default languages; reuse one rather than
        // inserting a duplicate.
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var templateId = await core.TemplateCreate(await core.TemplateFromXml(CommentTemplateXml));

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"ComplianceReassignment-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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

        // RelatedEFormId matters: ReconcileEventAsync loads the ARP WITHOUT
        // Include(AreaRule), so EnsureComplianceForOccurrenceAsync falls back to
        // planning.RelatedEFormId for the eForm id.
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week, StartDate = arpStartDate,
            RelatedEFormId = templateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = arpStartDate, Status = true,
            RepeatType = 1, RepeatEvery = 1, DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IBackendConfigurationCalendarService>());
        var sp = services.BuildServiceProvider();

        var deployService = new EventDeployService(
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, coreHelper, sp,
            NullLogger<EventDeployService>.Instance);

        var resolver = new CalendarAssignmentResolver(BackendConfigurationPnDbContext, coreHelper);

        var engine = new CalendarAssignmentReconciliationService(
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, coreHelper,
            deployService, resolver,
            NullLogger<CalendarAssignmentReconciliationService>.Instance);

        return new Scenario
        {
            Arp = arp, Property = property, Area = area, Planning = planning,
            Language = language, TemplateId = templateId, Engine = engine
        };
    }

    /// <summary>
    /// Adds one SDK site and wires it as a (BC) PlanningSite of the ARP, an
    /// items-planning PlanningSite of the Planning and an active PropertyWorker
    /// — the three linkages the deploy path's cross-worker leak guard accepts
    /// (EventDeployService.ResolveSiteLinkageAsync).
    /// Adapted from EventDeployServiceEformRepairTests.SeedSiteAsync.
    /// </summary>
    private async Task<Site> SeedSiteAsync(Scenario s, string name, int microtingUid)
    {
        var site = new Site
        {
            Name = name, MicrotingUid = microtingUid, LanguageId = s.Language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = s.Arp.Id, SiteId = site.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = s.Property.Id, WorkerId = site.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await ItemsPlanningPnDbContext!.PlanningSites.AddAsync(new IpPlanningSite
        {
            PlanningId = s.Planning.Id, SiteId = site.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        return site;
    }

    /// <summary>
    /// Unassigns a site from the event the way the calendar edit modal does:
    /// the (BC) PlanningSite row is soft-deleted, which is what
    /// <see cref="CalendarAssignmentResolver"/> reads.
    /// </summary>
    private async Task RemoveBcPlanningSiteAsync(Scenario s, Site site)
    {
        var planningSite = await BackendConfigurationPnDbContext!.PlanningSites
            .FirstAsync(x => x.AreaRulePlanningsId == s.Arp.Id
                             && x.SiteId == site.Id
                             && x.WorkflowState != Constants.WorkflowStates.Removed);
        await planningSite.Delete(BackendConfigurationPnDbContext);
    }

    /// <summary>
    /// MicrotingUid stays null so the retraction path never reaches the cloud —
    /// RetractSiteForOccurrenceAsync only calls core.CaseDelete for a case that
    /// has one, and the local soft-deletes are what this test is about.
    /// </summary>
    private async Task<SdkCase> SeedSdkCaseAsync(Site site, int checkListId)
    {
        var sdkCase = new SdkCase
        {
            SiteId = site.Id, CheckListId = checkListId, Status = OpenCaseStatus,
            MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkCase;
    }

    private async Task<PlanningCase> SeedPlanningCaseAsync(Scenario s, int eformId)
    {
        var planningCase = new PlanningCase
        {
            PlanningId = s.Planning.Id, Status = OpenPlanningStatus, MicrotingSdkeFormId = eformId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        return planningCase;
    }

    private async Task<PlanningCaseSite> SeedPlanningCaseSiteAsync(
        Scenario s, PlanningCase planningCase, Site site, SdkCase sdkCase, int eformId)
    {
        var planningCaseSite = new PlanningCaseSite
        {
            PlanningId = s.Planning.Id, PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = site.Id, MicrotingSdkeFormId = eformId,
            MicrotingSdkCaseId = sdkCase.Id, Status = OpenPlanningStatus,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        return planningCaseSite;
    }

    /// <summary>
    /// The ONE shared Compliance row for the occurrence. <c>PlanningCaseSiteId</c>
    /// deliberately stores the PlanningCase id — the (misnamed) convention
    /// EformParsedByServerHandler and EnsureComplianceRowAsync both use.
    /// </summary>
    private async Task<BcCompliance> SeedComplianceAsync(
        Scenario s, DateTime deadline, PlanningCase planningCase, SdkCase sdkCase, int eformId)
    {
        var compliance = new BcCompliance
        {
            PlanningId = s.Planning.Id, PropertyId = s.Property.Id, AreaId = s.Area.Id,
            Deadline = deadline.Date, StartDate = deadline.Date.AddDays(-7),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = eformId,
            PlanningCaseSiteId = planningCase.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance;
    }

    [Test]
    public async Task ReconcileEvent_ReassignsWorker_TransfersComplianceToNewWorker()
    {
        // ---------------------------------------------------------------
        // Arrange: one FUTURE occurrence, deployed and assigned to worker A.
        // ---------------------------------------------------------------
        var deadline = DateTime.UtcNow.Date.AddDays(5);
        var s = await SeedScenarioAsync(deadline.AddDays(-14));

        var workerA = await SeedSiteAsync(s, $"reassign-A-{Guid.NewGuid()}", 7101);

        var caseA = await SeedSdkCaseAsync(workerA, s.TemplateId);
        var planningCaseA = await SeedPlanningCaseAsync(s, s.TemplateId);
        await SeedPlanningCaseSiteAsync(s, planningCaseA, workerA, caseA, s.TemplateId);
        var compliance = await SeedComplianceAsync(s, deadline, planningCaseA, caseA, s.TemplateId);

        Assert.That(compliance.Id, Is.GreaterThan(0),
            "precondition: the seeded occurrence must own a live shared Compliance row");

        // ---------------------------------------------------------------
        // Act: the admin reassigns the task from A to B in the edit modal —
        // B is added as an assignee, A is unassigned, then the calendar
        // reconciliation engine runs for the event.
        // ---------------------------------------------------------------
        var workerB = await SeedSiteAsync(s, $"reassign-B-{Guid.NewGuid()}", 7102);
        await RemoveBcPlanningSiteAsync(s, workerA);

        await s.Engine.ReconcileEventAsync(s.Arp.Id);

        // ---------------------------------------------------------------
        // Assert: ONE Compliance row per occurrence, shared by whoever is
        // assigned. Reassignment must MOVE that row to B, never destroy it.
        // ---------------------------------------------------------------
        var liveRows = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking()
            .Where(x => x.PlanningId == s.Planning.Id
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        liveRows = liveRows.Where(x => x.Deadline.Date == deadline.Date).ToList();

        Assert.That(liveRows.Count, Is.EqualTo(1),
            "the occurrence must keep exactly ONE live shared Compliance row after reassigning "
            + "worker A -> worker B; zero means the reconciliation engine deployed B's addition "
            + "before retracting A's removal, discarded the row EnsureComplianceRowAsync handed "
            + "back on the duplicate-key path, and then soft-deleted the only row for "
            + $"(planning {s.Planning.Id}, deadline {deadline.Date:yyyy-MM-dd})");

        var survivingRow = liveRows[0];
        var backingCase = await MicrotingDbContext!.Cases
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == survivingRow.MicrotingSdkCaseId);

        Assert.That(backingCase, Is.Not.Null,
            $"the surviving Compliance row's MicrotingSdkCaseId ({survivingRow.MicrotingSdkCaseId}) "
            + "must resolve to a real SDK Case");

        Assert.That(backingCase!.SiteId, Is.EqualTo(workerB.Id),
            "the surviving shared Compliance row must point at the NEW assignee's SDK case — "
            + $"expected site B ({workerB.Id}), and it must not still point at the retracted "
            + $"worker A ({workerA.Id}); a stale pointer means B's freshly created case was "
            + "orphaned instead of adopted onto the shared row");
    }
}
