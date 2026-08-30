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

        _wizard = new BackendConfigurationTaskWizardService(
            new BackendConfigurationLocalizationService(),
            userService,
            BackendConfigurationPnDbContext!,
            coreHelper,
            ItemsPlanningPnDbContext!,
            _deployService,
            // REAL. Substituting this is exactly what would make every assertion
            // below vacuous.
            new CalendarOccurrenceRetractionService(
                BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, coreHelper,
                NullLogger<CalendarOccurrenceRetractionService>.Instance),
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
