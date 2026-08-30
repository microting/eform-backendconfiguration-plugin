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
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.EventDeployService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
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
using SdkSite = Microting.eForm.Infrastructure.Data.Entities.Site;

/// <summary>
/// #1123 Part A — the DATA-LOSS regression, driven through the REAL
/// <see cref="BackendConfigurationTaskWizardService"/> and the REAL
/// <see cref="CalendarOccurrenceRetractionService"/>.
///
/// WHAT WAS BROKEN. <c>DeactivateList</c> and <c>UpdateTask</c>'s deactivate
/// branch (<c>case true when !areaRulePlanning.Status</c>) were line-for-line
/// copies of one another, and both:
///   1. soft-deleted EVERY non-removed Compliance row of the planning, with no
///      completion filter at all;
///   2. CaseDeleted EVERY PlanningCaseSite's SDK case, with no completion guard;
///   3. set PlanningCase.WorkflowState = Retracted unconditionally.
/// That violates invariant R2 — completed occurrences are immutable. A
/// completed occurrence's Compliance row is the ONLY DB link between its
/// rotation date and the SDK case that answered it, so soft-removing it breaks
/// DoneByName/DoneAt rendering for that date permanently, and no later pass can
/// rebuild the link. Every other selective path in the codebase guards on
/// completion; these two did not, and the batch action added in Part B would
/// have multiplied the loss across an entire selection at once.
///
/// NOTHING IS SUBSTITUTED THAT COULD HIDE THE BUG. #1122's hard lesson was that
/// 25 fixtures substituted the very services under test, so inverting the gate's
/// condition failed no test whatsoever. Here the wizard is real and the
/// retraction service is real, so these tests fail if the completion skip is
/// removed from EITHER call site. Only <see cref="IEventDeployService"/> and
/// <see cref="IUserService"/> are substituted — neither participates in
/// retraction (the deploy service is not even reached: the repair pass is
/// skipped for a task that ends up inactive).
///
/// Every SDK case is seeded with MicrotingUid = null so <c>core.CaseDelete</c>
/// is skipped (there is no cloud in CI) while all the local bookkeeping still
/// runs — the same trick CalendarOccurrenceRetractionTests uses.
///
/// THE SCOPE GAP THE FIX OPENED, also covered below. The occurrence-driven
/// helper walks Compliance rows, so a deployed PlanningCaseSite that no
/// Compliance row covers became invisible to deactivation and stayed live on the
/// worker's device. RetractDeployedCasesWithoutComplianceAsync restores the old
/// planning-driven reach WITH the completion guard the old loop lacked — judging
/// completion from the SDK case's own Status/DoneAt, since with no Compliance row
/// there is no deadline. The tests in the second section pin both halves of that
/// (retract the open orphan, preserve the answered one) and pin that the sweep
/// claims nothing the occurrence pass already owns.
///
/// Deadlines are kept DISTINCT throughout: Compliances carries a UNIQUE index on
/// (PlanningId, Deadline).
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class TaskWizardDeactivateRetractionTests : TestBaseSetup
{
    /// <summary>Snapshotted once per test — never re-derived mid-test.</summary>
    private DateTime _today;

    private const int OpenStatus = 66;
    private const int CompletedStatus = 100;

    private IEventDeployService _deployService = null!;
    private BackendConfigurationTaskWizardService _wizard = null!;

    /// <summary>
    /// The SAME instance the wizard below is given, kept so the no-double-handling
    /// test can interrogate the orphan sweep directly instead of inferring its
    /// scope from the wizard's side effects.
    /// </summary>
    private CalendarOccurrenceRetractionService _retraction = null!;

    [SetUp]
    public async Task SetupDeactivateFixture()
    {
        _today = DateTime.UtcNow.Date;

        var core = await GetCore();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));

        _deployService = Substitute.For<IEventDeployService>();

        // REAL. Substituting this is exactly what would make every assertion
        // below vacuous.
        _retraction = new CalendarOccurrenceRetractionService(
            BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, coreHelper,
            NullLogger<CalendarOccurrenceRetractionService>.Instance);

        _wizard = new BackendConfigurationTaskWizardService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext!,
            coreHelper,
            ItemsPlanningPnDbContext!,
            _deployService,
            _retraction,
            NullLogger<BackendConfigurationTaskWizardService>.Instance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record Seeded(
        int ArpId, int PlanningId, int PropertyId, int AreaRuleId, int FolderId, int SdkSiteId);

    /// <summary>
    /// Seeds Folder → Area → Property → AreaRule → Planning → AreaRulePlanning
    /// for one ACTIVE task with a single assigned worker, wired on BOTH sides:
    /// a BC PlanningSite (what a later reactivation reads back) and an
    /// items-planning PlanningSite (what deactivation deletes).
    /// </summary>
    private async Task<Seeded> SeedActiveTask(string tag)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var folder = new Microting.eForm.Infrastructure.Data.Entities.Folder
        {
            Name = $"deact-{tag}-folder-{Guid.NewGuid()}", MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Folders.AddAsync(folder);
        await MicrotingDbContext.SaveChangesAsync();

        var sdkSite = new SdkSite
        {
            Name = $"deact-{tag}-site-{Guid.NewGuid()}", MicrotingUid = null,
            LanguageId = language.Id, WorkflowState = Constants.WorkflowStates.Created
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
            Name = $"Deactivate-{tag}-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 0, FolderId = folder.Id,
            CreatedInGuide = true,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Relative to the snapshotted today, never an absolute date: a hardcoded
        // future date rots, and a hardcoded past one changes which branch
        // PairItemWithSiteHelper would take.
        var anchor = DateTime.SpecifyKind(_today.AddDays(-28), DateTimeKind.Utc);

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = anchor, DayOfWeek = anchor.DayOfWeek, RelatedEFormId = 0,
            SdkFolderId = folder.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = anchor, Status = true,
            RepeatType = 2, RepeatEvery = 1, FolderId = folder.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = arp.Id, SiteId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = property.Id, WorkerId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await ItemsPlanningPnDbContext.PlanningSites.AddAsync(new IpPlanningSite
        {
            PlanningId = planning.Id, SiteId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        return new Seeded(arp.Id, planning.Id, property.Id, areaRule.Id, folder.Id, sdkSite.Id);
    }

    private sealed record Occurrence(int SdkCaseId, int ComplianceId, int PlanningCaseId, int PlanningCaseSiteId);

    /// <summary>
    /// Seeds one deployed occurrence: SDK Case (MicrotingUid null, so CaseDelete
    /// stays offline) + PlanningCase + PlanningCaseSite + Compliance.
    /// </summary>
    private async Task<Occurrence> SeedDeployedOccurrence(Seeded seeded, DateTime deadline, int status)
    {
        var sdkCase = new SdkCase
        {
            SiteId = seeded.SdkSiteId, Status = status, MicrotingUid = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var planningCase = new PlanningCase
        {
            PlanningId = seeded.PlanningId, Status = status, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var planningCaseSite = new PlanningCaseSite
        {
            PlanningId = seeded.PlanningId, PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = seeded.SdkSiteId, MicrotingSdkeFormId = 0,
            MicrotingSdkCaseId = sdkCase.Id, Status = status,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var compliance = new BcCompliance
        {
            PlanningId = seeded.PlanningId, PropertyId = seeded.PropertyId,
            Deadline = DateTime.SpecifyKind(deadline, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCase.Id, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Occurrence(sdkCase.Id, compliance.Id, planningCase.Id, planningCaseSite.Id);
    }

    private async Task<BcCompliance> ReloadCompliance(int id) =>
        await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().FirstAsync(x => x.Id == id);

    private async Task<PlanningCase> ReloadPlanningCase(int id) =>
        await ItemsPlanningPnDbContext!.PlanningCases
            .AsNoTracking().FirstAsync(x => x.Id == id);

    private async Task<SdkCase?> FindSdkCase(int id) =>
        await MicrotingDbContext!.Cases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

    private async Task<PlanningCaseSite> ReloadPlanningCaseSite(int id) =>
        await ItemsPlanningPnDbContext!.PlanningCaseSites
            .AsNoTracking().FirstAsync(x => x.Id == id);

    private sealed record Orphan(int SdkCaseId, int PlanningCaseId, int PlanningCaseSiteId);

    /// <summary>
    /// Seeds a deployed case with NO Compliance row — the gap the
    /// occurrence-driven retraction cannot see, because it walks Compliance rows
    /// and there is none to walk. Everything else is identical to
    /// <see cref="SeedDeployedOccurrence"/>: SDK Case (MicrotingUid null so
    /// CaseDelete stays offline) + PlanningCase + PlanningCaseSite.
    ///
    /// <paramref name="doneAt"/> is the SDK <c>Case.DoneAt</c> — the second half
    /// of the answered predicate (<c>Status == 100 || DoneAt.HasValue</c>). With
    /// no Compliance row there is no deadline, so the case's own Status/DoneAt is
    /// the ONLY thing completion can be judged from.
    /// </summary>
    private async Task<Orphan> SeedDeployedCaseWithoutCompliance(
        Seeded seeded, int status, DateTime? doneAt = null)
    {
        var sdkCase = new SdkCase
        {
            SiteId = seeded.SdkSiteId, Status = status, MicrotingUid = null, DoneAt = doneAt,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var planningCase = new PlanningCase
        {
            PlanningId = seeded.PlanningId, Status = status, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var planningCaseSite = new PlanningCaseSite
        {
            PlanningId = seeded.PlanningId, PlanningCaseId = planningCase.Id,
            MicrotingSdkSiteId = seeded.SdkSiteId, MicrotingSdkeFormId = 0,
            MicrotingSdkCaseId = sdkCase.Id, Status = status,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        return new Orphan(sdkCase.Id, planningCase.Id, planningCaseSite.Id);
    }

    /// <summary>
    /// The batch/wizard shape a deactivate arrives in: Status = NotActive with
    /// the CURRENT assignee list still attached, exactly as
    /// BackendConfigurationTaskListService.BuildUpdateModel round-trips it.
    /// </summary>
    private TaskWizardCreateModel DeactivateModel(Seeded seeded) =>
        new()
        {
            Id = seeded.ArpId,
            PropertyId = seeded.PropertyId,
            FolderId = seeded.FolderId,
            EformId = 0,
            StartDate = DateTime.SpecifyKind(_today.AddDays(-28), DateTimeKind.Utc),
            RepeatType = BackendConfiguration.Pn.Infrastructure.Enums.RepeatType.Week,
            RepeatEvery = 1,
            Status = BackendConfiguration.Pn.Infrastructure.Enums.TaskWizardStatuses.NotActive,
            Sites = [seeded.SdkSiteId],
            TagIds = [],
            Translates = [],
            ComplianceEnabled = false
        };

    // ═════════════════════════════════════════════════════════════════════════
    // THE regression: a completed occurrence survives deactivation
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// DeactivateList — the batch/list entry point. The completed occurrence's
    /// Compliance row, SDK case and PlanningCase must all be exactly as they
    /// were; the open one must be gone.
    ///
    /// Both are asserted in one test on purpose: the pre-fix code passed the
    /// "open one is retracted" half perfectly well, so an isolated preservation
    /// test could be satisfied by a service that simply stopped retracting
    /// anything.
    /// </summary>
    [Test]
    public async Task DeactivateList_PreservesCompletedOccurrenceAndRetractsTheOpenOne()
    {
        var seeded = await SeedActiveTask("list");
        // Distinct deadlines — UNIQUE (PlanningId, Deadline).
        var completed = await SeedDeployedOccurrence(seeded, _today.AddDays(-21), CompletedStatus);
        var open = await SeedDeployedOccurrence(seeded, _today.AddDays(-14), OpenStatus);

        var result = await _wizard.DeactivateList([seeded.ArpId]);

        Assert.That(result.Success, Is.True, result.Message);

        var completedCompliance = await ReloadCompliance(completed.ComplianceId);
        var openCompliance = await ReloadCompliance(open.ComplianceId);
        var completedPlanningCase = await ReloadPlanningCase(completed.PlanningCaseId);
        var completedSdkCase = await FindSdkCase(completed.SdkCaseId);
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == seeded.ArpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == seeded.PlanningId);

        Assert.Multiple(() =>
        {
            Assert.That(completedCompliance.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "R2: a completed occurrence's Compliance row is the only link between its date and the answered case — deactivation must not remove it");
            Assert.That(completedCompliance.MicrotingSdkCaseId, Is.EqualTo(completed.SdkCaseId),
                "and the link itself must still point at the same case");
            Assert.That(completedSdkCase, Is.Not.Null, "the answered SDK case must still exist");
            Assert.That(completedSdkCase!.Status, Is.EqualTo(CompletedStatus),
                "and must still read as completed");
            Assert.That(completedPlanningCase.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Retracted),
                "a completed occurrence's PlanningCase must not be retracted either");

            Assert.That(openCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "a NON-completed occurrence is still retracted — deactivation must actually deactivate");

            Assert.That(arp.Status, Is.False, "the task must actually have been deactivated");
            Assert.That(planning.Enabled, Is.False,
                "and the scheduler flag with it (SearchListJob filters on Planning.Enabled)");
        });

        // The items-planning PlanningSite rows are dropped (that write stays at
        // the call site, outside the helper), while BC's own PlanningSites — what
        // a later reactivation reads back — survive.
        var liveItemsPlanningSites = await ItemsPlanningPnDbContext.PlanningSites
            .AsNoTracking()
            .CountAsync(x => x.PlanningId == seeded.PlanningId
                             && x.WorkflowState != Constants.WorkflowStates.Removed);
        var liveBcPlanningSites = await BackendConfigurationPnDbContext.PlanningSites
            .AsNoTracking()
            .CountAsync(x => x.AreaRulePlanningsId == seeded.ArpId
                             && x.WorkflowState != Constants.WorkflowStates.Removed);

        Assert.Multiple(() =>
        {
            Assert.That(liveItemsPlanningSites, Is.EqualTo(0),
                "deactivation drops the items-planning PlanningSite rows");
            Assert.That(liveBcPlanningSites, Is.EqualTo(1),
                "but never BC's own, or a reactivate would send Sites = [] and be coerced back to inactive");
        });
    }

    /// <summary>
    /// The same contract through <c>UpdateTask</c>'s deactivate branch — the
    /// path the batch action, the task wizard's edit modal and the calendar edit
    /// modal all take. It carried its OWN copy of the destructive loop, so it
    /// needs its own test: fixing only DeactivateList would leave the far more
    /// frequently used path broken.
    /// </summary>
    [Test]
    public async Task UpdateTask_DeactivateBranch_PreservesCompletedOccurrenceAndRetractsTheOpenOne()
    {
        var seeded = await SeedActiveTask("update");
        var completed = await SeedDeployedOccurrence(seeded, _today.AddDays(-21), CompletedStatus);
        var open = await SeedDeployedOccurrence(seeded, _today.AddDays(-14), OpenStatus);

        var result = await _wizard.UpdateTask(DeactivateModel(seeded));

        Assert.That(result.Success, Is.True, result.Message);

        var completedCompliance = await ReloadCompliance(completed.ComplianceId);
        var openCompliance = await ReloadCompliance(open.ComplianceId);
        var completedPlanningCase = await ReloadPlanningCase(completed.PlanningCaseId);
        var completedSdkCase = await FindSdkCase(completed.SdkCaseId);
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == seeded.ArpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == seeded.PlanningId);

        Assert.Multiple(() =>
        {
            Assert.That(completedCompliance.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "R2 holds on the UpdateTask path too — this branch had its own copy of the destructive loop");
            Assert.That(completedCompliance.MicrotingSdkCaseId, Is.EqualTo(completed.SdkCaseId));
            Assert.That(completedSdkCase, Is.Not.Null);
            Assert.That(completedSdkCase!.Status, Is.EqualTo(CompletedStatus));
            Assert.That(completedPlanningCase.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Retracted));

            Assert.That(openCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the open occurrence is still pulled");

            Assert.That(arp.Status, Is.False);
            Assert.That(planning.Enabled, Is.False);
        });

        var liveBcPlanningSites = await BackendConfigurationPnDbContext.PlanningSites
            .AsNoTracking()
            .CountAsync(x => x.AreaRulePlanningsId == seeded.ArpId
                             && x.WorkflowState != Constants.WorkflowStates.Removed);
        Assert.That(liveBcPlanningSites, Is.EqualTo(1),
            "BC's PlanningSites survive, so a batch reactivate round-trips the assignee");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The scope gap the occurrence-driven helper left behind: a DEPLOYED
    // PlanningCaseSite that no Compliance row covers
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// RetractNonCompletedOccurrencesAsync walks Compliance rows, so a deployed
    /// case with NO Compliance row is invisible to it and used to stay live on
    /// the worker's device after the admin deactivated the task — a real
    /// regression against the pre-#1123 planning-driven loop.
    /// RetractDeployedCasesWithoutComplianceAsync closes that, and it must close
    /// it WITH the completion guard the old loop lacked.
    ///
    /// Both halves are asserted here on purpose: a sweep that simply retracted
    /// nothing would pass the preservation half, and the old destructive loop
    /// would pass the retraction half. Only a completion-guarded sweep passes
    /// both.
    /// </summary>
    [Test]
    public async Task DeactivateList_RetractsDeployedCaseWithNoComplianceRow_ButPreservesTheCompletedOne()
    {
        var seeded = await SeedActiveTask("orphan-list");
        var openOrphan = await SeedDeployedCaseWithoutCompliance(seeded, OpenStatus);
        var completedOrphan = await SeedDeployedCaseWithoutCompliance(seeded, CompletedStatus);

        var result = await _wizard.DeactivateList([seeded.ArpId]);

        Assert.That(result.Success, Is.True, result.Message);

        var openSite = await ReloadPlanningCaseSite(openOrphan.PlanningCaseSiteId);
        var openCase = await ReloadPlanningCase(openOrphan.PlanningCaseId);
        var completedSite = await ReloadPlanningCaseSite(completedOrphan.PlanningCaseSiteId);
        var completedCase = await ReloadPlanningCase(completedOrphan.PlanningCaseId);
        var completedSdkCase = await FindSdkCase(completedOrphan.SdkCaseId);

        Assert.Multiple(() =>
        {
            Assert.That(openSite.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "a deployed, OPEN case with no Compliance row must still be pulled — otherwise it stays live on the worker's device after deactivation");
            Assert.That(openCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Retracted),
                "and its PlanningCase goes with it, no live site left");

            Assert.That(completedSite.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "R2: the sweep is completion-guarded — the missing guard, not the PlanningCaseSite walk, was what made the old loop destructive");
            Assert.That(completedCase.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Retracted),
                "and the completed PlanningCase is not retracted either");
            Assert.That(completedSdkCase, Is.Not.Null, "the answered SDK case must still exist");
            Assert.That(completedSdkCase!.Status, Is.EqualTo(CompletedStatus));
        });
    }

    /// <summary>
    /// The same contract on <c>UpdateTask</c>'s deactivate branch. It is a
    /// separate call site with its own body, and it is the one the wizard's edit
    /// modal, the calendar edit modal and the batch action all take — wiring the
    /// sweep into only DeactivateList would leave the busier path leaking live
    /// cases.
    /// </summary>
    [Test]
    public async Task UpdateTask_DeactivateBranch_RetractsDeployedCaseWithNoComplianceRow_ButPreservesTheCompletedOne()
    {
        var seeded = await SeedActiveTask("orphan-update");
        var openOrphan = await SeedDeployedCaseWithoutCompliance(seeded, OpenStatus);
        var completedOrphan = await SeedDeployedCaseWithoutCompliance(seeded, CompletedStatus);

        var result = await _wizard.UpdateTask(DeactivateModel(seeded));

        Assert.That(result.Success, Is.True, result.Message);

        var openSite = await ReloadPlanningCaseSite(openOrphan.PlanningCaseSiteId);
        var completedSite = await ReloadPlanningCaseSite(completedOrphan.PlanningCaseSiteId);
        var completedCase = await ReloadPlanningCase(completedOrphan.PlanningCaseId);
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == seeded.ArpId);

        Assert.Multiple(() =>
        {
            Assert.That(openSite.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the UpdateTask branch must sweep the orphans too");
            Assert.That(completedSite.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "R2 holds on this path as well");
            Assert.That(completedCase.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Retracted));
            Assert.That(arp.Status, Is.False, "and the task really was deactivated");
        });
    }

    /// <summary>
    /// The second half of the answered predicate: <c>DoneAt</c> set while
    /// <c>Status</c> has not reached 100. EventDeployService spells its guard
    /// <c>Status == 100 || DoneAt.HasValue</c>, which is the evidence that this
    /// state is reachable; in it a Status-only gate would CaseDelete an answered
    /// case whose answer has no other record. The sweep therefore reads the same
    /// widened predicate, from the same loader, as the occurrence pass.
    ///
    /// The open orphan is the positive control: a status-99, DoneAt-null case
    /// still gets pulled, so this test cannot be passed by a sweep that has
    /// simply stopped working.
    /// </summary>
    [Test]
    public async Task DeactivateList_OrphanWithDoneAtButStatusUnder100_IsPreserved()
    {
        var seeded = await SeedActiveTask("orphan-doneat");
        var doneAt = DateTime.SpecifyKind(_today.AddDays(-3), DateTimeKind.Utc);
        // 99, deliberately just short of 100: only DoneAt can save this row.
        var answered = await SeedDeployedCaseWithoutCompliance(seeded, 99, doneAt);
        // Same status, no DoneAt — the control.
        var notAnswered = await SeedDeployedCaseWithoutCompliance(seeded, 99);

        var result = await _wizard.DeactivateList([seeded.ArpId]);

        Assert.That(result.Success, Is.True, result.Message);

        var answeredSite = await ReloadPlanningCaseSite(answered.PlanningCaseSiteId);
        var answeredCase = await ReloadPlanningCase(answered.PlanningCaseId);
        var controlSite = await ReloadPlanningCaseSite(notAnswered.PlanningCaseSiteId);

        Assert.Multiple(() =>
        {
            Assert.That(answeredSite.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "a case carrying DoneAt is answered even below status 100 — retracting it would destroy the only record of that answer");
            Assert.That(answeredCase.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Retracted));
            Assert.That(controlSite.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the identical row WITHOUT DoneAt is still retracted, so DoneAt is doing the work and the sweep has not simply stopped");
        });
    }

    /// <summary>
    /// No double-handling. The two passes divide the work by whether a Compliance
    /// row covers the case, so the sweep is asked DIRECTLY — before the wizard
    /// runs and mutates anything — what it claims for a planning whose every
    /// deployed case HAS a Compliance row. The answer must be "nothing at all".
    /// Without the exclusion gate this call would report Retracted = 1 and
    /// CompletedPreserved = 1, i.e. it would be racing the occurrence pass for
    /// the same two rows.
    ///
    /// The wizard is then run to confirm those rows are handled exactly once, by
    /// the pass that owns them: the open one retracted, the completed one intact.
    /// </summary>
    [Test]
    public async Task OrphanSweep_ClaimsNothingAlreadyCoveredByAComplianceRow()
    {
        var seeded = await SeedActiveTask("orphan-nodup");
        // Distinct deadlines — UNIQUE (PlanningId, Deadline).
        var open = await SeedDeployedOccurrence(seeded, _today.AddDays(-14), OpenStatus);
        var completed = await SeedDeployedOccurrence(seeded, _today.AddDays(-21), CompletedStatus);

        var arpEntity = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .FirstAsync(x => x.Id == seeded.ArpId);

        var sweep = await _retraction.RetractDeployedCasesWithoutComplianceAsync(arpEntity);

        Assert.Multiple(() =>
        {
            Assert.That(sweep.Retracted, Is.EqualTo(0),
                "every deployed case here is covered by a Compliance row, so the occurrence pass owns them — the sweep must not touch one");
            Assert.That(sweep.CompletedPreserved, Is.EqualTo(0),
                "and it must not even count them: overlapping scope is how the same row gets handled twice");
            Assert.That(sweep.Failed, Is.EqualTo(0));
        });

        var openSiteBefore = await ReloadPlanningCaseSite(open.PlanningCaseSiteId);
        Assert.That(openSiteBefore.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
            "the sweep reported nothing and must therefore have written nothing");

        var result = await _wizard.DeactivateList([seeded.ArpId]);
        Assert.That(result.Success, Is.True, result.Message);

        var openCompliance = await ReloadCompliance(open.ComplianceId);
        var openSite = await ReloadPlanningCaseSite(open.PlanningCaseSiteId);
        var openCase = await ReloadPlanningCase(open.PlanningCaseId);
        var completedCompliance = await ReloadCompliance(completed.ComplianceId);
        var completedSite = await ReloadPlanningCaseSite(completed.PlanningCaseSiteId);
        var completedCase = await ReloadPlanningCase(completed.PlanningCaseId);

        Assert.Multiple(() =>
        {
            Assert.That(openCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the open occurrence is retracted — once, by the occurrence pass");
            Assert.That(openSite.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(openCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Retracted));

            Assert.That(completedCompliance.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "and the completed one survives BOTH passes — the sweep running afterwards must not undo the preservation");
            Assert.That(completedSite.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(completedCase.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Retracted));
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Error isolation
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// DeactivateList used to have NO try/catch at all: the very first
    /// `.FirstOrDefaultAsync` returning null dereferenced into a
    /// NullReferenceException that escaped the loop, so every id AFTER a bad one
    /// was silently skipped while the ones before it stayed half-written.
    ///
    /// The bad id is placed FIRST on purpose — that is the ordering the old code
    /// failed on, and putting it last would pass even without the fix.
    /// </summary>
    [Test]
    public async Task DeactivateList_BadIdFirst_StillDeactivatesTheRemainingTasks()
    {
        const int unknownArpId = 999_999;
        var good = await SeedActiveTask("badid");
        var open = await SeedDeployedOccurrence(good, _today.AddDays(-14), OpenStatus);

        var result = await _wizard.DeactivateList([unknownArpId, good.ArpId]);

        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AsNoTracking().FirstAsync(x => x.Id == good.ArpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings
            .AsNoTracking().FirstAsync(x => x.Id == good.PlanningId);
        var openCompliance = await ReloadCompliance(open.ComplianceId);

        Assert.Multiple(() =>
        {
            Assert.That(arp.Status, Is.False,
                "the good id after the bad one must still have been processed");
            Assert.That(planning.Enabled, Is.False);
            Assert.That(openCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(result.Success, Is.True, "one of two succeeded, so overall Success is true");
            Assert.That(result.Message, Does.Contain("1/2"),
                "and the failure is reported rather than swallowed");
        });
    }

    /// <summary>
    /// The all-bad case: nothing to do, nothing thrown, and the result says so
    /// rather than claiming success.
    /// </summary>
    [Test]
    public async Task DeactivateList_AllIdsUnknown_ReportsFailureWithoutThrowing()
    {
        var result = await _wizard.DeactivateList([999_997, 999_998]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "no task was deactivated");
            Assert.That(result.Message, Does.Contain("0/2"));
        });
    }
}
