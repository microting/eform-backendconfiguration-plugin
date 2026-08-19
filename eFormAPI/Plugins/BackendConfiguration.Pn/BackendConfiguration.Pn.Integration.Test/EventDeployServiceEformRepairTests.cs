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

/// <summary>
/// DB-backed coverage for
/// <see cref="EventDeployService.RepairEformForOpenOccurrencesAsync"/> — the
/// pass that propagates an eForm change onto every already-deployed occurrence
/// that has NOT been completed yet.
///
/// Background: a calendar event's eForm id lives in six places
/// (<c>AreaRule.EformId</c>, <c>Planning.RelatedEFormId</c>,
/// <c>PlanningCase.MicrotingSdkeFormId</c>,
/// <c>PlanningCaseSite.MicrotingSdkeFormId</c>,
/// <c>Compliance.MicrotingSdkeFormId</c> and the SDK <c>Cases.CheckListId</c>).
/// Before the fix only the first two were rewritten on edit, so a deployed
/// occurrence still COMPLETED with the creation-time eForm even though the
/// calendar displayed the new one. Each test below pins one row of the design
/// spec's testing table (2026-08-19-calendar-eform-change-propagation-design.md).
///
/// The fixture mirrors <see cref="CalendarPrepareCompleteTests"/>: a real
/// <c>eFormCore.Core</c> against the MariaDb testcontainer (so
/// <c>ReadeForm</c> / <c>CaseCreateLocalOnly</c> / <c>CaseDeleteResult</c> run
/// for real — <c>Core</c> is a concrete class and cannot be substituted), with
/// <see cref="IEFormCoreService"/> as the only SDK-facing mock so the
/// "no eForm change ⇒ zero SDK work" case can be asserted on call counts.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class EventDeployServiceEformRepairTests : TestBaseSetup
{
    // Minimal real eForm. Content is irrelevant — the repair pass only needs a
    // real CheckList id it can hand to ReadeForm + CaseCreateLocalOnly. Copied
    // verbatim from CalendarPrepareCompleteTests.CommentTemplateXml.
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
    /// SDK <c>Case.Status</c> the deploy path leaves on a live, unanswered case:
    /// <c>SqlController.CaseCreate</c> writes 33. (66 is the plugin-side
    /// PlanningCase/PlanningCaseSite status — and, on a Case, "parsed by
    /// server" — so it is deliberately a different constant here.)
    /// </summary>
    private const int OpenCaseStatus = 33;

    /// <summary>
    /// <c>PlanningCase.Status</c> / <c>PlanningCaseSite.Status</c> the deploy
    /// path writes (EventDeployService steps 3-4).
    /// </summary>
    private const int OpenPlanningStatus = 66;

    /// <summary>SDK <c>Case.Status</c> for an answered case (EventDeployService.CompletedStatus).</summary>
    private const int CompletedStatus = 100;

    private sealed class Scenario
    {
        public eFormCore.Core Core = null!;
        public IEFormCoreService CoreHelper = null!;
        public EventDeployService Service = null!;
        public AreaRulePlanning Arp = null!;
        public AreaRule AreaRule = null!;
        public Property Property = null!;
        public Area Area = null!;
        public Planning Planning = null!;
        public Language Language = null!;
        public int OldTemplateId;
        public int NewTemplateId;
    }

    /// <summary>
    /// Seeds Area → Property → AreaRule → Planning → AreaRulePlanning plus TWO
    /// real eForm templates (the "old" one every seeded case is created from,
    /// and the "new" one the repair pass must swap to), and builds a real
    /// <see cref="EventDeployService"/> over the inherited contexts.
    /// </summary>
    private async Task<Scenario> SeedScenarioAsync(string tag, DateTime arpStartDate)
    {
        var core = await GetCore();

        // GetCore() seeds the SDK default languages; reuse one rather than
        // inserting a duplicate.
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        // Two distinct CheckLists from the same XML — EformCreateDb never
        // dedupes, so each TemplateCreate yields a fresh id.
        var oldTemplateId = await core.TemplateCreate(await core.TemplateFromXml(CommentTemplateXml));
        var newTemplateId = await core.TemplateCreate(await core.TemplateFromXml(CommentTemplateXml));
        Assert.That(newTemplateId, Is.Not.EqualTo(oldTemplateId),
            "the fixture needs two distinct eForm templates to be meaningful");

        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"EformRepair-{tag}-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = oldTemplateId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week, StartDate = arpStartDate,
            RelatedEFormId = oldTemplateId,
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

        var service = new EventDeployService(
            BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, coreHelper, sp,
            NullLogger<EventDeployService>.Instance);

        arp = await BackendConfigurationPnDbContext.AreaRulePlannings
            .Include(x => x.AreaRule)
            .FirstAsync(x => x.Id == arp.Id);

        return new Scenario
        {
            Core = core, CoreHelper = coreHelper, Service = service, Arp = arp, AreaRule = areaRule,
            Property = property, Area = area, Planning = planning, Language = language,
            OldTemplateId = oldTemplateId, NewTemplateId = newTemplateId
        };
    }

    /// <summary>
    /// Adds one SDK site and wires it as both a (BC) PlanningSite of the ARP,
    /// an items-planning PlanningSite of the Planning and an active
    /// PropertyWorker — the three linkages the deploy path's cross-worker leak
    /// guard accepts (EventDeployService.cs:503-551).
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

        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(
            new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
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

        await ItemsPlanningPnDbContext!.PlanningSites.AddAsync(
            new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
            {
                PlanningId = s.Planning.Id, SiteId = site.Id,
                WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
            });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        return site;
    }

    /// <summary>
    /// Creates a live SDK case for <paramref name="site"/> on
    /// <paramref name="checkListId"/>. <paramref name="microtingUid"/> is left
    /// null by default so the retraction stays purely local; pass one to reach
    /// the cloud-delete branch of <c>RetractSdkCaseAsync</c>.
    /// </summary>
    private async Task<Case> SeedSdkCaseAsync(
        Site site, int checkListId, int status, DateTime? doneAt = null, int? microtingUid = null)
    {
        var sdkCase = new Case
        {
            SiteId = site.Id, CheckListId = checkListId, Status = status, DoneAt = doneAt,
            MicrotingUid = microtingUid,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkCase;
    }

    /// <summary>
    /// A ROOT <c>CheckList</c> with no translations at all. It satisfies the
    /// repair pass's pre-flight probe ("a non-removed root CheckList exists for
    /// this id"), but <c>SqlController.ReadeForm</c> resolves its label through
    /// <c>CheckListTranslations.FirstAsync</c>, which throws on an empty
    /// sequence — so the failure lands exactly where the compensation path needs
    /// it: INSIDE <c>CreateSdkCaseForRotationAsync</c>, after the old case has
    /// already been retracted.
    /// </summary>
    private async Task<int> SeedTranslationlessCheckListAsync()
    {
        var checkList = new CheckList
        {
            Label = $"broken-{Guid.NewGuid()}",
            ParentId = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.CheckLists.AddAsync(checkList);
        await MicrotingDbContext.SaveChangesAsync();
        return checkList.Id;
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
        Scenario s, PlanningCase planningCase, Site site, Case sdkCase, int eformId)
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
    /// Compliance row for one occurrence. <c>PlanningCaseSiteId</c> deliberately
    /// stores the PlanningCase id — that is the (misnamed) convention
    /// EformParsedByServerHandler and EnsureComplianceRowAsync both use, and the
    /// repair pass's sibling-site lookup keys on it.
    /// </summary>
    private async Task<Compliance> SeedComplianceAsync(
        Scenario s, DateTime deadline, PlanningCase planningCase, Case sdkCase, int eformId)
    {
        var compliance = new Compliance
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

    private async Task<Case> ReadCaseAsync(int caseId) =>
        await MicrotingDbContext!.Cases.AsNoTracking().FirstAsync(x => x.Id == caseId);

    private async Task<Compliance> ReadComplianceAsync(int complianceId) =>
        await BackendConfigurationPnDbContext!.Compliances.AsNoTracking().FirstAsync(x => x.Id == complianceId);

    private async Task<PlanningCaseSite> ReadPlanningCaseSiteAsync(int id) =>
        await ItemsPlanningPnDbContext!.PlanningCaseSites.AsNoTracking().FirstAsync(x => x.Id == id);

    private async Task<PlanningCase> ReadPlanningCaseAsync(int id) =>
        await ItemsPlanningPnDbContext!.PlanningCases.AsNoTracking().FirstAsync(x => x.Id == id);

    private static bool IsLive(string workflowState) =>
        workflowState != Constants.WorkflowStates.Removed
        && workflowState != Constants.WorkflowStates.Retracted;

    // ------------------------------------------------------------------
    // 1. Deployed + open + single site.
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_DeployedOpenSingleSite_RetractsOldCaseAndRepointsRowsInPlace()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(4);
        var s = await SeedScenarioAsync("open-single", deadline.AddDays(-14));
        var site = await SeedSiteAsync(s, "repair-open-single", 6101);

        var oldCase = await SeedSdkCaseAsync(site, s.OldTemplateId, OpenCaseStatus);
        var planningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, site, oldCase, s.OldTemplateId);
        var compliance = await SeedComplianceAsync(s, deadline, planningCase, oldCase, s.OldTemplateId);
        var complianceIdBefore = compliance.Id;

        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, s.NewTemplateId);

        var reloadedCompliance = await ReadComplianceAsync(complianceIdBefore);
        var reloadedOldCase = await ReadCaseAsync(oldCase.Id);
        var newCase = await ReadCaseAsync(reloadedCompliance.MicrotingSdkCaseId);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);

        Assert.Multiple(() =>
        {
            // Old case retracted — the worker's device must not keep the old form.
            Assert.That(IsLive(reloadedOldCase.WorkflowState), Is.False,
                "the old SDK case must be retracted/removed, not left live alongside the new one");

            // The replacement case carries the NEW checklist for the SAME site.
            Assert.That(reloadedCompliance.MicrotingSdkCaseId, Is.Not.EqualTo(oldCase.Id));
            Assert.That(newCase.CheckListId, Is.EqualTo(s.NewTemplateId));
            Assert.That(newCase.SiteId, Is.EqualTo(site.Id));
            Assert.That(IsLive(newCase.WorkflowState), Is.True);

            // Compliance.Id is stable — the calendar UI holds complianceId.
            Assert.That(reloadedCompliance.Id, Is.EqualTo(complianceIdBefore));
            Assert.That(reloadedCompliance.Deadline.Date, Is.EqualTo(deadline.Date));
            Assert.That(IsLive(reloadedCompliance.WorkflowState), Is.True);

            // All three plugin-side eForm ids follow the new case.
            Assert.That(reloadedCompliance.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            Assert.That(reloadedPlanningCase.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseId,
                Is.EqualTo(reloadedCompliance.MicrotingSdkCaseId));
        });

        // No Compliance row was created or deleted — repaired strictly in place.
        var complianceCount = await BackendConfigurationPnDbContext!.Compliances
            .CountAsync(x => x.PlanningId == s.Planning.Id
                             && x.WorkflowState != Constants.WorkflowStates.Removed);
        Assert.That(complianceCount, Is.EqualTo(1));
    }

    // ------------------------------------------------------------------
    // 2. Deployed + completed → untouched.
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_DeployedCompletedOccurrence_LeavesCaseComplianceAndEformIdsUntouched()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(-3);
        var s = await SeedScenarioAsync("completed", deadline.AddDays(-14));
        var site = await SeedSiteAsync(s, "repair-completed", 6102);

        var doneAt = DateTime.UtcNow.AddDays(-2);
        var doneCase = await SeedSdkCaseAsync(site, s.OldTemplateId, CompletedStatus, doneAt);
        var planningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, site, doneCase, s.OldTemplateId);
        var compliance = await SeedComplianceAsync(s, deadline, planningCase, doneCase, s.OldTemplateId);

        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, s.NewTemplateId);

        var reloadedCase = await ReadCaseAsync(doneCase.Id);
        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);

        Assert.Multiple(() =>
        {
            // An answered case IS the record of what was filled in — never swapped.
            Assert.That(IsLive(reloadedCase.WorkflowState), Is.True, "a completed case must not be retracted");
            Assert.That(reloadedCase.CheckListId, Is.EqualTo(s.OldTemplateId));
            Assert.That(reloadedCase.Status, Is.EqualTo(CompletedStatus));

            Assert.That(reloadedCompliance.MicrotingSdkCaseId, Is.EqualTo(doneCase.Id));
            Assert.That(reloadedCompliance.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseId, Is.EqualTo(doneCase.Id));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId));
            Assert.That(reloadedPlanningCase.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId));
        });

        // No replacement case was created for this site.
        var casesForSite = await MicrotingDbContext!.Cases.AsNoTracking()
            .CountAsync(x => x.SiteId == site.Id);
        Assert.That(casesForSite, Is.EqualTo(1), "no replacement case may be created for a completed occurrence");
    }

    // ------------------------------------------------------------------
    // 3. Not yet deployed → deploys with the NEW eForm (regression guard).
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_NotYetDeployed_IsNoOp_AndLaterDeployUsesTheNewEform()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(3);
        var s = await SeedScenarioAsync("undeployed", deadline.AddDays(-14));
        var site = await SeedSiteAsync(s, "repair-undeployed", 6103);

        // The edit already happened: AreaRule/Planning carry the new eForm,
        // but nothing has been deployed for this occurrence yet.
        s.AreaRule.EformId = s.NewTemplateId;
        s.Planning.RelatedEFormId = s.NewTemplateId;
        await BackendConfigurationPnDbContext!.SaveChangesAsync();
        await ItemsPlanningPnDbContext!.SaveChangesAsync();

        var arp = await BackendConfigurationPnDbContext.AreaRulePlannings
            .Include(x => x.AreaRule)
            .FirstAsync(x => x.Id == s.Arp.Id);

        await s.Service.RepairEformForOpenOccurrencesAsync(arp, s.OldTemplateId, s.NewTemplateId);

        // Nothing to repair — the repair pass must not invent Compliance rows.
        var afterRepair = await BackendConfigurationPnDbContext.Compliances
            .CountAsync(x => x.PlanningId == s.Planning.Id);
        Assert.That(afterRepair, Is.EqualTo(0));

        // The on-demand deploy then materialises the occurrence with the NEW eForm.
        var result = await s.Service.EnsureComplianceForOccurrenceAsync(arp, deadline, site.Id);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.SdkCaseId, Is.GreaterThan(0));
        var deployedCase = await ReadCaseAsync(result.SdkCaseId);
        var deployedCompliance = await ReadComplianceAsync(result.ComplianceId);
        var deployedPlanningCaseSite = await ItemsPlanningPnDbContext.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.MicrotingSdkCaseId == result.SdkCaseId);

        Assert.Multiple(() =>
        {
            Assert.That(result.TemplateId, Is.EqualTo(s.NewTemplateId));
            Assert.That(deployedCase.CheckListId, Is.EqualTo(s.NewTemplateId));
            Assert.That(deployedCase.SiteId, Is.EqualTo(site.Id));
            Assert.That(deployedCompliance.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            Assert.That(deployedPlanningCaseSite.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
        });
    }

    // ------------------------------------------------------------------
    // 4a. Multi-site, mixed done/open — wizard (PairItemWithSiteHelper) shape:
    //     every assigned site shares ONE PlanningCase, so the Compliance-driven
    //     sibling lookup reaches all of them from the single Compliance row.
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_MultiSiteWizardShape_MixedDoneAndOpen_SwapsOnlyTheOpenSite()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(5);
        var s = await SeedScenarioAsync("multisite", deadline.AddDays(-14));
        var doneSite = await SeedSiteAsync(s, "repair-multisite-done", 6104);
        var openSite = await SeedSiteAsync(s, "repair-multisite-open", 6105);

        // One shared PlanningCase — the shape PairItemWithSiteHelper.Pair writes.
        var planningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);

        var doneCase = await SeedSdkCaseAsync(doneSite, s.OldTemplateId, CompletedStatus, DateTime.UtcNow.AddDays(-1));
        var openCase = await SeedSdkCaseAsync(openSite, s.OldTemplateId, OpenCaseStatus);

        var donePlanningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, doneSite, doneCase, s.OldTemplateId);
        var openPlanningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, openSite, openCase, s.OldTemplateId);

        // The Compliance row points at the OPEN site's case (one row per
        // occurrence — (PlanningId, Deadline) is unique).
        var compliance = await SeedComplianceAsync(s, deadline, planningCase, openCase, s.OldTemplateId);

        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, s.NewTemplateId);

        var reloadedDoneCase = await ReadCaseAsync(doneCase.Id);
        var reloadedOpenCase = await ReadCaseAsync(openCase.Id);
        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedDonePcs = await ReadPlanningCaseSiteAsync(donePlanningCaseSite.Id);
        var reloadedOpenPcs = await ReadPlanningCaseSiteAsync(openPlanningCaseSite.Id);
        var newCase = await ReadCaseAsync(reloadedCompliance.MicrotingSdkCaseId);

        Assert.Multiple(() =>
        {
            // The worker who already answered keeps their case AND their eForm.
            Assert.That(IsLive(reloadedDoneCase.WorkflowState), Is.True);
            Assert.That(reloadedDoneCase.CheckListId, Is.EqualTo(s.OldTemplateId));
            Assert.That(reloadedDonePcs.MicrotingSdkCaseId, Is.EqualTo(doneCase.Id));
            Assert.That(reloadedDonePcs.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId));

            // The open worker is swapped onto the new eForm.
            Assert.That(IsLive(reloadedOpenCase.WorkflowState), Is.False);
            Assert.That(reloadedOpenPcs.MicrotingSdkCaseId, Is.Not.EqualTo(openCase.Id));
            Assert.That(reloadedOpenPcs.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            Assert.That(newCase.Id, Is.EqualTo(reloadedOpenPcs.MicrotingSdkCaseId));
            Assert.That(newCase.CheckListId, Is.EqualTo(s.NewTemplateId));
            Assert.That(newCase.SiteId, Is.EqualTo(openSite.Id));

            // Compliance repaired in place.
            Assert.That(reloadedCompliance.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            Assert.That(reloadedCompliance.MicrotingSdkCaseId, Is.EqualTo(newCase.Id));
        });

        // Exactly one replacement case exists — the done site got none.
        var casesForDoneSite = await MicrotingDbContext!.Cases.AsNoTracking()
            .CountAsync(x => x.SiteId == doneSite.Id);
        Assert.That(casesForDoneSite, Is.EqualTo(1));
    }

    // ------------------------------------------------------------------
    // 4. Multi-site, mixed done/open — CALENDAR deploy shape. This is the shape
    //    the reported bug actually occurs in: DeployForRotationAsync creates a
    //    fresh PlanningCase + PlanningCaseSite PER SITE, while Compliances
    //    carries a UNIQUE (PlanningId, Deadline) index, so only the FIRST
    //    site's Compliance row survives and sites 2..n are reachable from no
    //    Compliance row at all. The Compliance-independent sweep must still
    //    swap every OPEN site, and must still leave the completed one alone.
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_MultiSiteCalendarDeployShape_SwapsEveryOpenSite_AndSparesTheCompletedOne()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(6);
        var s = await SeedScenarioAsync("multisite-calendar", deadline.AddDays(-14));
        var complianceOwnerSite = await SeedSiteAsync(s, "repair-cal-owner", 6106);
        var orphanOpenSite = await SeedSiteAsync(s, "repair-cal-orphan-open", 6107);
        var orphanDoneSite = await SeedSiteAsync(s, "repair-cal-orphan-done", 6113);

        // Calendar deploy shape: one PlanningCase PER site.
        var ownerPlanningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var orphanOpenPlanningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var orphanDonePlanningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);

        var ownerCase = await SeedSdkCaseAsync(complianceOwnerSite, s.OldTemplateId, OpenCaseStatus);
        var orphanOpenCase = await SeedSdkCaseAsync(orphanOpenSite, s.OldTemplateId, OpenCaseStatus);
        var orphanDoneCase = await SeedSdkCaseAsync(
            orphanDoneSite, s.OldTemplateId, CompletedStatus, DateTime.UtcNow.AddDays(-1));

        var ownerPcs = await SeedPlanningCaseSiteAsync(
            s, ownerPlanningCase, complianceOwnerSite, ownerCase, s.OldTemplateId);
        var orphanOpenPcs = await SeedPlanningCaseSiteAsync(
            s, orphanOpenPlanningCase, orphanOpenSite, orphanOpenCase, s.OldTemplateId);
        var orphanDonePcs = await SeedPlanningCaseSiteAsync(
            s, orphanDonePlanningCase, orphanDoneSite, orphanDoneCase, s.OldTemplateId);

        // Only ONE Compliance row can exist for (planning, deadline) — the
        // unique index swallows the INSERT for the other two sites.
        var compliance = await SeedComplianceAsync(s, deadline, ownerPlanningCase, ownerCase, s.OldTemplateId);
        var complianceIdBefore = compliance.Id;

        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, s.NewTemplateId);

        var reloadedOwnerCase = await ReadCaseAsync(ownerCase.Id);
        var reloadedOrphanOpenCase = await ReadCaseAsync(orphanOpenCase.Id);
        var reloadedOrphanDoneCase = await ReadCaseAsync(orphanDoneCase.Id);
        var reloadedOwnerPcs = await ReadPlanningCaseSiteAsync(ownerPcs.Id);
        var reloadedOrphanOpenPcs = await ReadPlanningCaseSiteAsync(orphanOpenPcs.Id);
        var reloadedOrphanDonePcs = await ReadPlanningCaseSiteAsync(orphanDonePcs.Id);
        var reloadedCompliance = await ReadComplianceAsync(complianceIdBefore);
        var ownerNewCase = await ReadCaseAsync(reloadedOwnerPcs.MicrotingSdkCaseId);
        var orphanNewCase = await ReadCaseAsync(reloadedOrphanOpenPcs.MicrotingSdkCaseId);

        Assert.Multiple(() =>
        {
            // Site that owns the Compliance row — repaired via the
            // Compliance-driven loop, Compliance updated in place.
            Assert.That(IsLive(reloadedOwnerCase.WorkflowState), Is.False);
            Assert.That(ownerNewCase.CheckListId, Is.EqualTo(s.NewTemplateId));
            Assert.That(ownerNewCase.SiteId, Is.EqualTo(complianceOwnerSite.Id));
            Assert.That(reloadedOwnerPcs.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            Assert.That(reloadedCompliance.Id, Is.EqualTo(complianceIdBefore),
                "the Compliance row is repaired in place — the calendar UI holds complianceId");
            Assert.That(reloadedCompliance.MicrotingSdkCaseId, Is.EqualTo(ownerNewCase.Id));
            Assert.That(reloadedCompliance.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));

            // Site reachable from NO Compliance row — repaired via the
            // Compliance-independent sweep. This is the multi-site half of the
            // bug: without the sweep this worker keeps the old form.
            Assert.That(IsLive(reloadedOrphanOpenCase.WorkflowState), Is.False,
                "every OPEN site must have its old case retracted, not only the Compliance owner");
            Assert.That(reloadedOrphanOpenPcs.MicrotingSdkCaseId, Is.Not.EqualTo(orphanOpenCase.Id));
            Assert.That(reloadedOrphanOpenPcs.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            Assert.That(orphanNewCase.CheckListId, Is.EqualTo(s.NewTemplateId));
            Assert.That(orphanNewCase.SiteId, Is.EqualTo(orphanOpenSite.Id));

            // Completed site — untouched even though it too is Compliance-less.
            Assert.That(IsLive(reloadedOrphanDoneCase.WorkflowState), Is.True);
            Assert.That(reloadedOrphanDoneCase.CheckListId, Is.EqualTo(s.OldTemplateId));
            Assert.That(reloadedOrphanDonePcs.MicrotingSdkCaseId, Is.EqualTo(orphanDoneCase.Id));
            Assert.That(reloadedOrphanDonePcs.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId));
        });

        // Exactly one replacement case per OPEN site, none for the done site.
        var ownerSiteCaseCount = await MicrotingDbContext!.Cases.AsNoTracking()
            .CountAsync(x => x.SiteId == complianceOwnerSite.Id);
        var orphanOpenSiteCaseCount = await MicrotingDbContext.Cases.AsNoTracking()
            .CountAsync(x => x.SiteId == orphanOpenSite.Id);
        var orphanDoneSiteCaseCount = await MicrotingDbContext.Cases.AsNoTracking()
            .CountAsync(x => x.SiteId == orphanDoneSite.Id);
        Assert.Multiple(() =>
        {
            Assert.That(ownerSiteCaseCount, Is.EqualTo(2));
            Assert.That(orphanOpenSiteCaseCount, Is.EqualTo(2));
            Assert.That(orphanDoneSiteCaseCount, Is.EqualTo(1));
        });

        // Still exactly one Compliance row — the sweep must never invent one
        // for the sites the unique index locked out.
        var complianceCount = await BackendConfigurationPnDbContext!.Compliances
            .CountAsync(x => x.PlanningId == s.Planning.Id
                             && x.WorkflowState != Constants.WorkflowStates.Removed);
        Assert.That(complianceCount, Is.EqualTo(1));
    }

    // ------------------------------------------------------------------
    // 9. Orphan-only planning — live PlanningCaseSites but NO usable Compliance
    //    row at all. Pins the widened early-return: "nothing to repair" must
    //    require BOTH to be empty, otherwise these sites would silently keep
    //    the old eForm.
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_OrphanCaseSitesWithoutAnyComplianceRow_AreStillSwapped()
    {
        var s = await SeedScenarioAsync("orphan-only", DateTime.UtcNow.Date.AddDays(-14));
        var openSite = await SeedSiteAsync(s, "repair-orphan-open", 6111);
        var doneSite = await SeedSiteAsync(s, "repair-orphan-done", 6112);

        var openCase = await SeedSdkCaseAsync(openSite, s.OldTemplateId, OpenCaseStatus);
        var doneCase = await SeedSdkCaseAsync(doneSite, s.OldTemplateId, CompletedStatus, DateTime.UtcNow.AddDays(-2));

        var openPlanningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var donePlanningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var openPcs = await SeedPlanningCaseSiteAsync(s, openPlanningCase, openSite, openCase, s.OldTemplateId);
        var donePcs = await SeedPlanningCaseSiteAsync(s, donePlanningCase, doneSite, doneCase, s.OldTemplateId);

        // Deliberately NO Compliance row for this planning.
        Assert.That(
            await BackendConfigurationPnDbContext!.Compliances.CountAsync(x => x.PlanningId == s.Planning.Id),
            Is.EqualTo(0));

        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, s.NewTemplateId);

        var reloadedOpenPcs = await ReadPlanningCaseSiteAsync(openPcs.Id);
        var reloadedDonePcs = await ReadPlanningCaseSiteAsync(donePcs.Id);
        var reloadedOpenCase = await ReadCaseAsync(openCase.Id);
        var reloadedDoneCase = await ReadCaseAsync(doneCase.Id);
        var newCase = await ReadCaseAsync(reloadedOpenPcs.MicrotingSdkCaseId);
        var reloadedOpenPlanningCase = await ReadPlanningCaseAsync(openPlanningCase.Id);

        Assert.Multiple(() =>
        {
            Assert.That(IsLive(reloadedOpenCase.WorkflowState), Is.False);
            Assert.That(reloadedOpenPcs.MicrotingSdkCaseId, Is.Not.EqualTo(openCase.Id));
            Assert.That(reloadedOpenPcs.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            Assert.That(reloadedOpenPlanningCase.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            Assert.That(newCase.CheckListId, Is.EqualTo(s.NewTemplateId));
            Assert.That(newCase.SiteId, Is.EqualTo(openSite.Id));

            Assert.That(IsLive(reloadedDoneCase.WorkflowState), Is.True);
            Assert.That(reloadedDoneCase.CheckListId, Is.EqualTo(s.OldTemplateId));
            Assert.That(reloadedDonePcs.MicrotingSdkCaseId, Is.EqualTo(doneCase.Id));
            Assert.That(reloadedDonePcs.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId));
        });

        // The sweep repairs in place — it must never manufacture a Compliance row.
        Assert.That(
            await BackendConfigurationPnDbContext.Compliances.CountAsync(x => x.PlanningId == s.Planning.Id),
            Is.EqualTo(0));
    }

    // ------------------------------------------------------------------
    // 5. Overdue but still open → treated exactly like a future occurrence.
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_OverdueButStillOpenOccurrence_IsSwappedLikeAFutureOne()
    {
        // Deliberately in the PAST — derived from now, never hardcoded.
        var deadline = DateTime.UtcNow.Date.AddDays(-9);
        var s = await SeedScenarioAsync("overdue", deadline.AddDays(-14));
        var site = await SeedSiteAsync(s, "repair-overdue", 6108);

        var oldCase = await SeedSdkCaseAsync(site, s.OldTemplateId, OpenCaseStatus);
        var planningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, site, oldCase, s.OldTemplateId);
        var compliance = await SeedComplianceAsync(s, deadline, planningCase, oldCase, s.OldTemplateId);

        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, s.NewTemplateId);

        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedOldCase = await ReadCaseAsync(oldCase.Id);
        var newCase = await ReadCaseAsync(reloadedCompliance.MicrotingSdkCaseId);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);

        Assert.Multiple(() =>
        {
            Assert.That(IsLive(reloadedOldCase.WorkflowState), Is.False);
            Assert.That(reloadedCompliance.Id, Is.EqualTo(compliance.Id));
            Assert.That(reloadedCompliance.MicrotingSdkCaseId, Is.Not.EqualTo(oldCase.Id));
            Assert.That(reloadedCompliance.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            // Deadline stays the true (past) rotation date even though
            // CreateSdkCaseForRotationAsync clamps mainElement.EndDate forward
            // so CaseCreateLocalOnly accepts the case.
            Assert.That(reloadedCompliance.Deadline.Date, Is.EqualTo(deadline.Date));
            Assert.That(newCase.CheckListId, Is.EqualTo(s.NewTemplateId));
            Assert.That(newCase.SiteId, Is.EqualTo(site.Id));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseId,
                Is.EqualTo(reloadedCompliance.MicrotingSdkCaseId));
        });
    }

    // ------------------------------------------------------------------
    // 7. No eForm change → zero retract, zero redeploy, zero SDK work.
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_NoEformChange_PerformsNoRetractNoRedeployAndNeverTouchesTheSdk()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(4);
        var s = await SeedScenarioAsync("nochange", deadline.AddDays(-14));
        var site = await SeedSiteAsync(s, "repair-nochange", 6109);

        var existingCase = await SeedSdkCaseAsync(site, s.OldTemplateId, OpenCaseStatus);
        var planningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, site, existingCase, s.OldTemplateId);
        var compliance = await SeedComplianceAsync(s, deadline, planningCase, existingCase, s.OldTemplateId);

        // GetCore() was already consumed by the fixture's own seeding, so count
        // from here rather than asserting an absolute zero.
        s.CoreHelper.ClearReceivedCalls();

        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, s.OldTemplateId);

        // The SDK is never reached: no ReadeForm, no CaseCreateLocalOnly, no
        // CaseDelete — the pass returns before resolving the Core at all.
        await s.CoreHelper.DidNotReceive().GetCore();

        var reloadedCase = await ReadCaseAsync(existingCase.Id);
        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var caseCount = await MicrotingDbContext!.Cases.AsNoTracking().CountAsync(x => x.SiteId == site.Id);

        Assert.Multiple(() =>
        {
            Assert.That(IsLive(reloadedCase.WorkflowState), Is.True, "nothing may be retracted");
            Assert.That(reloadedCase.CheckListId, Is.EqualTo(s.OldTemplateId));
            Assert.That(caseCount, Is.EqualTo(1), "nothing may be redeployed");
            Assert.That(reloadedCompliance.MicrotingSdkCaseId, Is.EqualTo(existingCase.Id));
            Assert.That(reloadedCompliance.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseId, Is.EqualTo(existingCase.Id));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId));
        });
    }

    // ------------------------------------------------------------------
    // 7b. Idempotence: a case ALREADY on the new eForm is left alone even
    //     when old != new (a second save of the same edit).
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_CaseAlreadyOnNewEform_IsSkipped()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(4);
        var s = await SeedScenarioAsync("idempotent", deadline.AddDays(-14));
        var site = await SeedSiteAsync(s, "repair-idempotent", 6110);

        // Already swapped by an earlier pass.
        var alreadyNewCase = await SeedSdkCaseAsync(site, s.NewTemplateId, OpenCaseStatus);
        var planningCase = await SeedPlanningCaseAsync(s, s.NewTemplateId);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, site, alreadyNewCase, s.NewTemplateId);
        var compliance = await SeedComplianceAsync(s, deadline, planningCase, alreadyNewCase, s.NewTemplateId);

        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, s.NewTemplateId);

        var reloadedCase = await ReadCaseAsync(alreadyNewCase.Id);
        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var caseCount = await MicrotingDbContext!.Cases.AsNoTracking().CountAsync(x => x.SiteId == site.Id);

        Assert.Multiple(() =>
        {
            Assert.That(IsLive(reloadedCase.WorkflowState), Is.True);
            Assert.That(caseCount, Is.EqualTo(1), "a second save of the same edit must not churn the case");
            Assert.That(reloadedCompliance.MicrotingSdkCaseId, Is.EqualTo(alreadyNewCase.Id));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseId, Is.EqualTo(alreadyNewCase.Id));
        });
    }
    // ------------------------------------------------------------------
    // 10. The site is no longer a live ASSIGNEE of the event (its
    //     items-planning PlanningSite was removed) but is still an active
    //     PropertyWorker — exactly what "unassign a worker" leaves behind.
    //     Its case must be RETRACTED and NOT recreated, while the worker who
    //     is still assigned is swapped in the same pass.
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_SiteNoLongerAssigned_RetractsWithoutRecreating_AndStillSwapsTheAssignedSite()
    {
        var s = await SeedScenarioAsync("unassigned", DateTime.UtcNow.Date.AddDays(-14));
        var removedSite = await SeedSiteAsync(s, "repair-unassigned-removed", 6114);
        var keptSite = await SeedSiteAsync(s, "repair-unassigned-kept", 6115);

        // The unassign already happened: only the items-planning PlanningSite is
        // gone. The PropertyWorker row deliberately stays active — that is the
        // whole point, because the deploy path's guard accepts a bare property
        // worker and would therefore have recreated the case here.
        var removedPlanningSite = await ItemsPlanningPnDbContext!.PlanningSites
            .FirstAsync(x => x.PlanningId == s.Planning.Id && x.SiteId == removedSite.Id);
        removedPlanningSite.WorkflowState = Constants.WorkflowStates.Removed;
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var removedCase = await SeedSdkCaseAsync(removedSite, s.OldTemplateId, OpenCaseStatus);
        var keptCase = await SeedSdkCaseAsync(keptSite, s.OldTemplateId, OpenCaseStatus);
        var planningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var removedPcs = await SeedPlanningCaseSiteAsync(s, planningCase, removedSite, removedCase, s.OldTemplateId);
        var keptPcs = await SeedPlanningCaseSiteAsync(s, planningCase, keptSite, keptCase, s.OldTemplateId);

        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, s.NewTemplateId);

        var reloadedRemovedCase = await ReadCaseAsync(removedCase.Id);
        var reloadedKeptCase = await ReadCaseAsync(keptCase.Id);
        var reloadedRemovedPcs = await ReadPlanningCaseSiteAsync(removedPcs.Id);
        var reloadedKeptPcs = await ReadPlanningCaseSiteAsync(keptPcs.Id);
        var keptNewCase = await ReadCaseAsync(reloadedKeptPcs.MicrotingSdkCaseId);

        var casesForRemovedSite = await MicrotingDbContext!.Cases.AsNoTracking()
            .CountAsync(x => x.SiteId == removedSite.Id);
        var casesForKeptSite = await MicrotingDbContext.Cases.AsNoTracking()
            .CountAsync(x => x.SiteId == keptSite.Id);

        Assert.Multiple(() =>
        {
            // The removed worker loses the old form and gets NOTHING back.
            Assert.That(IsLive(reloadedRemovedCase.WorkflowState), Is.False,
                "the unassigned worker's case must be retracted");
            Assert.That(casesForRemovedSite, Is.EqualTo(1),
                "the unassigned worker must NOT receive a brand-new case on the new eForm");
            Assert.That(reloadedRemovedPcs.MicrotingSdkCaseId, Is.EqualTo(removedCase.Id),
                "the row is not re-pointed — there is no replacement to point at");
            Assert.That(IsLive(reloadedRemovedPcs.WorkflowState), Is.False,
                "the PlanningCaseSite must not stay live on a case that no longer exists");

            // The worker who is still assigned is swapped as usual.
            Assert.That(IsLive(reloadedKeptCase.WorkflowState), Is.False);
            Assert.That(casesForKeptSite, Is.EqualTo(2));
            Assert.That(reloadedKeptPcs.MicrotingSdkCaseId, Is.Not.EqualTo(keptCase.Id));
            Assert.That(reloadedKeptPcs.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            Assert.That(keptNewCase.CheckListId, Is.EqualTo(s.NewTemplateId));
            Assert.That(keptNewCase.SiteId, Is.EqualTo(keptSite.Id));
        });
    }

    // ------------------------------------------------------------------
    // 11. The cloud delete branch. SDK cases created by the calendar carry a
    //     synthetic MicrotingUid, so RetractSdkCaseAsync calls
    //     Core.CaseDelete(microtingUid) — which CAN throw. The local row must
    //     still end up removed, and the swap must still complete.
    //
    //     The throw is induced offline and deterministically: a SECOND Case row
    //     sharing the MicrotingUid makes SqlController.CaseReadByMUId take its
    //     `Count(...) == 1` false branch, whose CheckListSites.FirstAsync finds
    //     nothing and throws — all of it BEFORE Core.CaseDelete reaches the
    //     communicator, so no network round-trip happens in CI.
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_CaseWithMicrotingUid_CloudDeleteThrows_LocalRowIsStillRemoved()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(4);
        var s = await SeedScenarioAsync("cloud-delete", deadline.AddDays(-14));
        var site = await SeedSiteAsync(s, "repair-cloud-delete", 6116);

        const int sharedMicrotingUid = 991_001;
        var oldCase = await SeedSdkCaseAsync(site, s.OldTemplateId, OpenCaseStatus, microtingUid: sharedMicrotingUid);
        // Decoy sharing the MicrotingUid. It is referenced by no PlanningCaseSite
        // and no Compliance row, so the repair pass never touches it itself.
        var decoyCase = await SeedSdkCaseAsync(site, s.OldTemplateId, OpenCaseStatus, microtingUid: sharedMicrotingUid);

        var planningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, site, oldCase, s.OldTemplateId);
        var compliance = await SeedComplianceAsync(s, deadline, planningCase, oldCase, s.OldTemplateId);

        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, s.NewTemplateId);

        var reloadedOldCase = await ReadCaseAsync(oldCase.Id);
        var reloadedDecoyCase = await ReadCaseAsync(decoyCase.Id);
        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var newCase = await ReadCaseAsync(reloadedCompliance.MicrotingSdkCaseId);

        Assert.Multiple(() =>
        {
            // The whole point: a throwing cloud delete must not stop the local
            // retraction, or the worker keeps the old form forever.
            Assert.That(IsLive(reloadedOldCase.WorkflowState), Is.False,
                "the local Case row must be removed even when Core.CaseDelete throws");
            Assert.That(reloadedOldCase.MicrotingUid, Is.EqualTo(sharedMicrotingUid));

            Assert.That(reloadedCompliance.MicrotingSdkCaseId, Is.Not.EqualTo(oldCase.Id));
            Assert.That(reloadedCompliance.MicrotingSdkeFormId, Is.EqualTo(s.NewTemplateId));
            Assert.That(newCase.CheckListId, Is.EqualTo(s.NewTemplateId));
            Assert.That(newCase.SiteId, Is.EqualTo(site.Id));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseId, Is.EqualTo(newCase.Id));

            // The unreferenced decoy is untouched.
            Assert.That(IsLive(reloadedDecoyCase.WorkflowState), Is.True);
            Assert.That(reloadedDecoyCase.CheckListId, Is.EqualTo(s.OldTemplateId));
        });
    }

    // ------------------------------------------------------------------
    // 12. Failure path. The old case is retracted before the replacement is
    //     created, so a create that fails leaves the site with NO case. The
    //     Compliance row must then be RELEASED (MicrotingSdkCaseId = 0) —
    //     that, not the deploy idempotence guard, is what lets the stuck-row
    //     recovery branch of EnsureDeployedAsync redeploy the occurrence.
    //
    //     The failure is induced with a root CheckList that PASSES the pre-flight
    //     probe but breaks ReadeForm (no translations) — an id no CheckList
    //     backs at all is now rejected before anything destructive happens, see
    //     the pre-flight test below.
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_ReplacementCannotBeCreated_ReleasesTheComplianceRowForRedeploy()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(4);
        var s = await SeedScenarioAsync("create-fails", deadline.AddDays(-14));
        var site = await SeedSiteAsync(s, "repair-create-fails", 6117);

        var oldCase = await SeedSdkCaseAsync(site, s.OldTemplateId, OpenCaseStatus);
        var planningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, site, oldCase, s.OldTemplateId);
        var compliance = await SeedComplianceAsync(s, deadline, planningCase, oldCase, s.OldTemplateId);

        var unresolvableEformId = await SeedTranslationlessCheckListAsync();

        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, unresolvableEformId);

        var reloadedOldCase = await ReadCaseAsync(oldCase.Id);
        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var casesForSite = await MicrotingDbContext!.Cases.AsNoTracking().CountAsync(x => x.SiteId == site.Id);

        Assert.Multiple(() =>
        {
            // Retract-then-create: the old case is gone and nothing replaced it.
            Assert.That(IsLive(reloadedOldCase.WorkflowState), Is.False);
            Assert.That(casesForSite, Is.EqualTo(1), "no replacement case may exist");

            // The row stays (the calendar UI holds complianceId) but is released.
            Assert.That(reloadedCompliance.Id, Is.EqualTo(compliance.Id));
            Assert.That(IsLive(reloadedCompliance.WorkflowState), Is.True);
            Assert.That(reloadedCompliance.MicrotingSdkCaseId, Is.EqualTo(0),
                "a failed swap must hand the row to the stuck-row recovery branch, not leave it pointing at a removed case");
            Assert.That(reloadedCompliance.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId),
                "the eForm id is only claimed once a case actually carries it");

            // The plugin-side row is NOT re-pointed at a case that was never created.
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseId, Is.EqualTo(oldCase.Id));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId));
        });
    }

    // ------------------------------------------------------------------
    // 13. PRE-FLIGHT. SwapCaseEformAsync retracts BEFORE it ever reads the new
    //     eForm, so an unusable new eForm id would strip every open occurrence
    //     of its case and recreate none of them. The pass must validate the id
    //     ONCE, up front, and abort without touching anything.
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_NewEformIdIsNotAUsableCheckList_AbortsBeforeRetractingAnything()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(4);
        var s = await SeedScenarioAsync("preflight", deadline.AddDays(-14));
        var site = await SeedSiteAsync(s, "repair-preflight", 6118);

        var oldCase = await SeedSdkCaseAsync(site, s.OldTemplateId, OpenCaseStatus);
        var planningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, site, oldCase, s.OldTemplateId);
        var compliance = await SeedComplianceAsync(s, deadline, planningCase, oldCase, s.OldTemplateId);

        // No CheckList carries this id at all — the shape a removed checklist or
        // a stale client cache produces.
        const int unusableEformId = int.MaxValue;

        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, unusableEformId);

        var reloadedOldCase = await ReadCaseAsync(oldCase.Id);
        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var reloadedPlanningCase = await ReadPlanningCaseAsync(planningCase.Id);
        var casesForSite = await MicrotingDbContext!.Cases.AsNoTracking().CountAsync(x => x.SiteId == site.Id);

        Assert.Multiple(() =>
        {
            // The whole point: NOTHING was retracted, so the worker keeps a
            // working form instead of losing it with no replacement.
            Assert.That(IsLive(reloadedOldCase.WorkflowState), Is.True,
                "an unusable new eForm must abort the pass BEFORE the destructive retract");
            Assert.That(reloadedOldCase.CheckListId, Is.EqualTo(s.OldTemplateId));
            Assert.That(casesForSite, Is.EqualTo(1), "no replacement case may be created");

            Assert.That(reloadedCompliance.MicrotingSdkCaseId, Is.EqualTo(oldCase.Id));
            Assert.That(reloadedCompliance.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkCaseId, Is.EqualTo(oldCase.Id));
            Assert.That(reloadedPlanningCaseSite.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId));
            Assert.That(reloadedPlanningCase.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId));
        });
    }

    // ------------------------------------------------------------------
    // 14. Deactivate, then REACTIVATE with an eForm change in one save.
    //     The wizard's deactivation branch soft-removes every Compliance row and
    //     sets PlanningCase.WorkflowState = Retracted, but leaves the
    //     PlanningCaseSite rows alone — and for calendar-created cases the cloud
    //     CaseDelete it performs is a verified no-op, so the SDK Case rows stay
    //     live too. The caller's "task is inactive after this save" guard does
    //     NOT cover the reactivating edit, so the pass itself must refuse to
    //     revive a deployment whose PARENT PlanningCase was cancelled — otherwise
    //     the worker gets brand-new live cases for long-past, cancelled
    //     occurrences.
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_ReactivationWithEformChange_DoesNotReviveCancelledDeployments()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(-20);
        var s = await SeedScenarioAsync("reactivated", DateTime.UtcNow.Date.AddDays(-40));
        var site = await SeedSiteAsync(s, "repair-reactivated", 6119);

        var staleCase = await SeedSdkCaseAsync(site, s.OldTemplateId, OpenCaseStatus);
        var planningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var stalePcs = await SeedPlanningCaseSiteAsync(s, planningCase, site, staleCase, s.OldTemplateId);
        var compliance = await SeedComplianceAsync(s, deadline, planningCase, staleCase, s.OldTemplateId);

        // Exactly what the deactivation branch leaves behind.
        planningCase.WorkflowState = Constants.WorkflowStates.Retracted;
        await ItemsPlanningPnDbContext!.SaveChangesAsync();
        compliance.WorkflowState = Constants.WorkflowStates.Removed;
        await BackendConfigurationPnDbContext!.SaveChangesAsync();

        // The reactivating save also changes the eForm, so the caller DOES invoke
        // the repair pass (areaRulePlanning.Status is true again).
        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, s.NewTemplateId);

        var reloadedStaleCase = await ReadCaseAsync(staleCase.Id);
        var reloadedStalePcs = await ReadPlanningCaseSiteAsync(stalePcs.Id);
        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var casesForSite = await MicrotingDbContext!.Cases.AsNoTracking().CountAsync(x => x.SiteId == site.Id);

        Assert.Multiple(() =>
        {
            Assert.That(casesForSite, Is.EqualTo(1),
                "a cancelled occurrence must never be resurrected as a brand-new live case dated today");
            Assert.That(IsLive(reloadedStaleCase.WorkflowState), Is.True,
                "the cancelled deployment is left exactly as the deactivation left it — not re-retracted");
            Assert.That(reloadedStaleCase.CheckListId, Is.EqualTo(s.OldTemplateId));
            Assert.That(reloadedStalePcs.MicrotingSdkCaseId, Is.EqualTo(staleCase.Id));
            Assert.That(reloadedStalePcs.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId));
            Assert.That(reloadedCompliance.MicrotingSdkCaseId, Is.EqualTo(staleCase.Id));
        });
    }

    // ------------------------------------------------------------------
    // 15. A site that is no longer a live assignee AND owns the Compliance row.
    //     The pass deliberately manufactures no replacement (that would hand a
    //     non-assignee a brand-new case), but it must still RELEASE the row:
    //     leaving MicrotingSdkCaseId pointing at the case it just removed makes
    //     the occurrence permanently dead, because every redeploy path keys its
    //     stuck-row recovery on SdkCaseId == 0.
    // ------------------------------------------------------------------
    [Test]
    public async Task Repair_UnassignedSiteOwningTheComplianceRow_ReleasesItInsteadOfStrandingIt()
    {
        var deadline = DateTime.UtcNow.Date.AddDays(3);
        var s = await SeedScenarioAsync("unassigned-compliance", deadline.AddDays(-14));
        var site = await SeedSiteAsync(s, "repair-unassigned-compliance", 6120);

        // Only the items-planning PlanningSite goes; the PropertyWorker row
        // deliberately stays active, which is what makes this site look like a
        // legitimate on-behalf-of deployer to the deploy path and a NON-assignee
        // to the repair pass.
        var planningSite = await ItemsPlanningPnDbContext!.PlanningSites
            .FirstAsync(x => x.PlanningId == s.Planning.Id && x.SiteId == site.Id);
        planningSite.WorkflowState = Constants.WorkflowStates.Removed;
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var oldCase = await SeedSdkCaseAsync(site, s.OldTemplateId, OpenCaseStatus);
        var planningCase = await SeedPlanningCaseAsync(s, s.OldTemplateId);
        var planningCaseSite = await SeedPlanningCaseSiteAsync(s, planningCase, site, oldCase, s.OldTemplateId);
        var compliance = await SeedComplianceAsync(s, deadline, planningCase, oldCase, s.OldTemplateId);

        await s.Service.RepairEformForOpenOccurrencesAsync(s.Arp, s.OldTemplateId, s.NewTemplateId);

        var reloadedOldCase = await ReadCaseAsync(oldCase.Id);
        var reloadedCompliance = await ReadComplianceAsync(compliance.Id);
        var reloadedPlanningCaseSite = await ReadPlanningCaseSiteAsync(planningCaseSite.Id);
        var casesForSite = await MicrotingDbContext!.Cases.AsNoTracking().CountAsync(x => x.SiteId == site.Id);

        Assert.Multiple(() =>
        {
            // Unchanged from the previous round: no case is manufactured for a
            // site that is not a live assignee.
            Assert.That(IsLive(reloadedOldCase.WorkflowState), Is.False,
                "the non-assignee's case must be retracted");
            Assert.That(casesForSite, Is.EqualTo(1),
                "a non-assignee must NOT receive a brand-new case on the new eForm");
            Assert.That(IsLive(reloadedPlanningCaseSite.WorkflowState), Is.False,
                "the PlanningCaseSite must not stay live on a case that no longer exists");

            // NEW: the row is released rather than stranded on a removed case.
            Assert.That(reloadedCompliance.Id, Is.EqualTo(compliance.Id),
                "the Compliance row is never deleted — the calendar UI holds complianceId");
            Assert.That(IsLive(reloadedCompliance.WorkflowState), Is.True);
            Assert.That(reloadedCompliance.MicrotingSdkCaseId, Is.EqualTo(0),
                "leaving the row pointing at the removed case would make the occurrence dead forever");
            Assert.That(reloadedCompliance.MicrotingSdkeFormId, Is.EqualTo(s.OldTemplateId),
                "the eForm id is only claimed once a case actually carries it");
        });
    }
}
