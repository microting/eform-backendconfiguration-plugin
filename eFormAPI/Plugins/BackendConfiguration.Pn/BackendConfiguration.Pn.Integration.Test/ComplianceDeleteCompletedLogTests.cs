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

using eFormCore;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.BackendConfigurationCompliancesService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

/// <summary>
/// #1290 — "Slet log" on a COMPLETED log in the Compliance Rapport view was a silent
/// no-op: completion had already soft-deleted the Compliance row, so
/// <c>BackendConfigurationCompliancesService.Delete</c> saved nothing and still reported
/// success, and the report (which deliberately keeps removed-but-completed rows) showed the
/// log again after the refresh.
///
/// <para>The fix marks a deleted completed occurrence with an <c>IsDeleted</c>
/// <see cref="CalendarOccurrenceException"/> for (lowest-Id live ARP, Deadline date) — the
/// row <c>BuildCandidateSet</c> already skips — and additionally sets the SDK case Removed,
/// soft-deletes the matching PlanningCaseSite and retracts the PlanningCase only when no
/// live sibling remains. Two tests here LOCK why neither the SDK case state nor the
/// PlanningCaseSite state is the marker (see the Delete XML doc).</para>
///
/// <para>Every SDK case is seeded with <c>MicrotingUid = null</c>, so the service's
/// best-effort platform retraction (<c>core.CaseDelete</c>) is never attempted — it is a
/// real HTTP call. Every assertion is scoped to the property the test seeded.</para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ComplianceDeleteCompletedLogTests : TestBaseSetup
{
    private const int CompletedStatus = 100;
    private const int OpenCaseStatus = 33;

    // GetCore() is expensive; the database is not reset per test, so one Core serves
    // the whole fixture.
    private Core? _core;
    private int _uidCounter = 960_000;

    private async Task<Core> SharedCore() => _core ??= await GetCore();

    // ------------------------------------------------------------------
    // Service construction
    // ------------------------------------------------------------------

    private static IEFormCoreService CoreHelper(Core core)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return coreHelper;
    }

    private static IUserService UserService()
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(
            new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));
        userService.GetCurrentUserLocale().Returns(Task.FromResult("en-US"));
        return userService;
    }

    private BackendConfigurationCompliancesService BuildCompliancesService(Core core)
        => new(
            ItemsPlanningPnDbContext!,
            BackendConfigurationPnDbContext!,
            UserService(),
            // Test-project stub that echoes the resource key.
            new BackendConfigurationLocalizationService(),
            CoreHelper(core),
            TimePlanningPnDbContext!);

    private BackendConfigurationComplianceReportService BuildReportService(Core core)
    {
        var coreHelper = CoreHelper(core);
        return new BackendConfigurationComplianceReportService(
            new BackendConfigurationLocalizationService(), UserService(),
            BackendConfigurationPnDbContext!, coreHelper, ItemsPlanningPnDbContext!,
            TestContextLogger<BackendConfigurationComplianceReportService>.Instance,
            new WorkerTagMembershipService(coreHelper));
    }

    // ------------------------------------------------------------------
    // Seeding
    // ------------------------------------------------------------------

    private sealed class Occurrence
    {
        public int PropertyId;
        public int AreaId;
        public int ArpId;
        public int PlanningId;
        public int SdkCaseId;
        public int ComplianceId;
        public int PlanningCaseId;
        public int PlanningCaseSiteId;
        public DateTime Deadline;
    }

    private async Task<int> SeedSdkSite()
    {
        var uid = ++_uidCounter;
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"delete-log-site-{uid}",
            MicrotingUid = uid,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    private async Task<int> SeedCheckList()
    {
        var checkList = new CheckList
        {
            Label = $"delete-log-{Guid.NewGuid()}",
            ParentId = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.CheckLists.AddAsync(checkList);
        await MicrotingDbContext.SaveChangesAsync();
        return checkList.Id;
    }

    /// <summary>
    /// Seeds Area → Property → AreaRule(+translation) → Planning → AreaRulePlanning. The
    /// series has NO occurrence yet; <see cref="SeedOccurrence"/> adds them.
    /// </summary>
    private async Task<Occurrence> SeedSeries(string title)
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var property = new Property
        {
            Name = $"DeleteLog-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = title,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var startDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-60), DateTimeKind.Utc);
        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = startDate, DayOfWeek = DayOfWeek.Monday, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = 2, RepeatEvery = 1, RepeatWeekdaysCsv = "1", DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Occurrence
        {
            PropertyId = property.Id, AreaId = area.Id, ArpId = arp.Id, PlanningId = planning.Id
        };
    }

    /// <summary>
    /// One occurrence of <paramref name="series"/> on <paramref name="deadline"/>: an SDK
    /// case, the PlanningCase/PlanningCaseSite pair and the Compliance row.
    /// <para>
    /// <paramref name="completed"/> seeds the shape completion leaves behind: case
    /// <c>Status = 100</c>, Compliance soft-removed. <paramref name="sdkCaseRemoved"/>
    /// additionally sets the case Removed — the shape every web/calendar/gRPC completion
    /// leaves after its <c>core.CaseDelete</c> device retraction succeeds.
    /// <paramref name="planningCaseId"/> reuses an existing PlanningCase (a shared one).
    /// </para>
    /// </summary>
    private async Task<Occurrence> SeedOccurrence(
        Occurrence series, DateTime deadline, bool completed, bool sdkCaseRemoved = false,
        int? planningCaseId = null)
    {
        var siteId = await SeedSdkSite();
        var checkListId = await SeedCheckList();

        var sdkCase = new Case
        {
            SiteId = siteId,
            Status = completed ? CompletedStatus : OpenCaseStatus,
            DoneAt = completed ? DateTime.SpecifyKind(deadline, DateTimeKind.Utc) : null,
            CheckListId = checkListId,
            MicrotingUid = null,
            WorkflowState = sdkCaseRemoved ? Constants.WorkflowStates.Removed : Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        if (planningCaseId == null)
        {
            var planningCase = new PlanningCase
            {
                PlanningId = series.PlanningId,
                MicrotingSdkeFormId = checkListId,
                MicrotingSdkCaseId = completed ? sdkCase.Id : 0,
                Status = completed ? CompletedStatus : 66,
                WorkflowState = completed ? Constants.WorkflowStates.Processed : Constants.WorkflowStates.Created,
                CreatedByUserId = 1, UpdatedByUserId = 1
            };
            await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
            await ItemsPlanningPnDbContext.SaveChangesAsync();
            planningCaseId = planningCase.Id;
        }

        var planningCaseSite = new PlanningCaseSite
        {
            PlanningId = series.PlanningId,
            PlanningCaseId = planningCaseId.Value,
            MicrotingSdkSiteId = siteId,
            MicrotingSdkeFormId = checkListId,
            MicrotingSdkCaseId = sdkCase.Id,
            Status = completed ? CompletedStatus : 66,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var compliance = new Compliance
        {
            ItemName = "Delete log item",
            PlanningId = series.PlanningId,
            PropertyId = series.PropertyId,
            AreaId = series.AreaId,
            Deadline = DateTime.SpecifyKind(deadline.Date, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.Date.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCase.Id,
            MicrotingSdkeFormId = checkListId,
            WorkflowState = completed ? Constants.WorkflowStates.Removed : Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Occurrence
        {
            PropertyId = series.PropertyId, AreaId = series.AreaId, ArpId = series.ArpId,
            PlanningId = series.PlanningId, SdkCaseId = sdkCase.Id, ComplianceId = compliance.Id,
            PlanningCaseId = planningCaseId.Value, PlanningCaseSiteId = planningCaseSite.Id,
            Deadline = compliance.Deadline
        };
    }

    // ------------------------------------------------------------------
    // Reading the three report views
    // ------------------------------------------------------------------

    private static (DateTime From, DateTime To) Window()
    {
        var today = DateTime.UtcNow.Date;
        return (today.AddDays(-90), today.AddDays(30));
    }

    /// <summary>Detaljer: the compliance ids Index returns for the property, status "all".</summary>
    private async Task<List<int>> DetaljerIds(Core core, int propertyId)
    {
        var (from, to) = Window();
        var result = await BuildReportService(core).Index(new ComplianceReportRequestModel
        {
            DateFrom = from, DateTo = to, Status = "all", PropertyId = propertyId,
            BoardIds = [], TagIds = [], SiteIds = [], PageIndex = 0, PageSize = 0
        });
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model.Entities.Select(r => r.ComplianceId).ToList();
    }

    /// <summary>Rapport: the compliance ids EformColumns returns for the property.</summary>
    private async Task<List<int>> RapportIds(Core core, int propertyId)
    {
        var (from, to) = Window();
        var result = await BuildReportService(core).EformColumns(new ComplianceReportRequestModel
        {
            DateFrom = from, DateTo = to, Status = "all", PropertyId = propertyId,
            BoardIds = [], TagIds = [], SiteIds = [], PageSize = 0
        });
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model
            .SelectMany(g => g.Templates)
            .SelectMany(t => t.Cases)
            .Select(c => c.ComplianceId)
            .ToList();
    }

    /// <summary>Oversigt: (Total, Done) of the property's row.</summary>
    private async Task<(int Total, int Done)> Oversigt(Core core, int propertyId)
    {
        var (from, to) = Window();
        var result = await BuildReportService(core).Overview(new ComplianceReportOverviewRequestModel
        {
            DateFrom = from, DateTo = to, PropertyId = propertyId,
            BoardIds = [], TagIds = [], SiteIds = []
        });
        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model.Rows.SingleOrDefault(r => r.PropertyId == propertyId);
        return row == null ? (0, 0) : (row.Total, row.Done);
    }

    private void ClearTrackers()
    {
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        ItemsPlanningPnDbContext!.ChangeTracker.Clear();
        MicrotingDbContext!.ChangeTracker.Clear();
    }

    private static DateTime PastDay(int daysAgo) => DateTime.UtcNow.Date.AddDays(-daysAgo);

    // ==================================================================
    // Locks: why neither the SDK case state nor the PCS state is the marker
    // ==================================================================

    /// <summary>
    /// LOCK. Every web/calendar/gRPC completion ends with <c>core.CaseDelete</c> on the
    /// completed case, which sets the SDK case Removed. Such a log is a perfectly normal
    /// completed log and must stay visible everywhere — this is why the deleted-log marker
    /// cannot be "SDK case Removed".
    /// </summary>
    [Test]
    public async Task CompletedLog_WhoseSdkCaseWasRemovedByCompletion_StaysVisibleInAllThreeViews()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Removed-by-completion");
        var occ = await SeedOccurrence(series, PastDay(7), completed: true, sdkCaseRemoved: true);

        Assert.That(await DetaljerIds(core, occ.PropertyId), Does.Contain(occ.ComplianceId), "Detaljer");
        Assert.That(await RapportIds(core, occ.PropertyId), Does.Contain(occ.ComplianceId), "Rapport");
        Assert.That(await Oversigt(core, occ.PropertyId), Is.EqualTo((1, 1)), "Oversigt (Total, Done)");
    }

    /// <summary>
    /// LOCK (status quo, #1290 coordinator requirement). Deleting a property or unassigning
    /// an area soft-deletes EVERY PlanningCaseSite of the planning — completed ones included
    /// — together with the Planning and the AreaRulePlanning, and leaves the Compliance rows.
    /// The completed history must stay in the reports, which is why the marker cannot be
    /// "PlanningCaseSite Removed". Simulated by applying exactly those soft-deletes: the
    /// real property/area delete paths also drive folders, entity groups and the platform.
    /// </summary>
    [Test]
    public async Task CompletedLog_AfterPropertyOrAreaDeleteRemovedItsPlanningCaseSite_StaysVisible()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Area-unassigned");
        var occ = await SeedOccurrence(series, PastDay(7), completed: true);

        var pcs = await ItemsPlanningPnDbContext!.PlanningCaseSites.SingleAsync(x => x.Id == occ.PlanningCaseSiteId);
        await pcs.Delete(ItemsPlanningPnDbContext);
        var planning = await ItemsPlanningPnDbContext.Plannings.SingleAsync(x => x.Id == occ.PlanningId);
        await planning.Delete(ItemsPlanningPnDbContext);
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.SingleAsync(x => x.Id == occ.ArpId);
        await arp.Delete(BackendConfigurationPnDbContext);
        ClearTrackers();

        Assert.That(await DetaljerIds(core, occ.PropertyId), Does.Contain(occ.ComplianceId), "Detaljer");
        Assert.That(await RapportIds(core, occ.PropertyId), Does.Contain(occ.ComplianceId), "Rapport");
        Assert.That(await Oversigt(core, occ.PropertyId), Is.EqualTo((1, 1)), "Oversigt (Total, Done)");
    }

    // ==================================================================
    // Deleting a completed log
    // ==================================================================

    /// <summary>
    /// REGRESSION (#1290). The old Delete returned success without writing anything, and
    /// the log was still in all three views afterwards.
    /// </summary>
    [TestCase(false, TestName = "DeleteCompletedLog_HidesItFromRapportDetaljerAndOversigt_SdkCaseLive")]
    [TestCase(true, TestName = "DeleteCompletedLog_HidesItFromRapportDetaljerAndOversigt_SdkCaseAlreadyRemovedByCompletion")]
    public async Task DeleteCompletedLog_HidesItFromRapportDetaljerAndOversigt(bool sdkCaseRemovedByCompletion)
    {
        var core = await SharedCore();
        var series = await SeedSeries("Delete-completed");
        var deleted = await SeedOccurrence(series, PastDay(14), completed: true, sdkCaseRemoved: sdkCaseRemovedByCompletion);
        var kept = await SeedOccurrence(series, PastDay(7), completed: true, sdkCaseRemoved: sdkCaseRemovedByCompletion);

        Assert.That(await Oversigt(core, series.PropertyId), Is.EqualTo((2, 2)), "premise: both logs counted");

        var result = await BuildCompliancesService(core).Delete(deleted.ComplianceId);
        Assert.That(result.Success, Is.True, result.Message);
        ClearTrackers();

        var detaljer = await DetaljerIds(core, series.PropertyId);
        var rapport = await RapportIds(core, series.PropertyId);
        Assert.Multiple(async () =>
        {
            Assert.That(detaljer, Does.Not.Contain(deleted.ComplianceId), "Detaljer still shows the deleted log");
            Assert.That(detaljer, Does.Contain(kept.ComplianceId), "Detaljer lost the sibling log");
            Assert.That(rapport, Does.Not.Contain(deleted.ComplianceId), "Rapport still shows the deleted log");
            Assert.That(rapport, Does.Contain(kept.ComplianceId), "Rapport lost the sibling log");
            Assert.That(await Oversigt(core, series.PropertyId), Is.EqualTo((1, 1)), "Oversigt (Total, Done)");
        });

        // The SDK case (answers + photos) is Removed; the sibling's case is untouched.
        var deletedCase = await MicrotingDbContext!.Cases.AsNoTracking().SingleAsync(x => x.Id == deleted.SdkCaseId);
        var keptCase = await MicrotingDbContext.Cases.AsNoTracking().SingleAsync(x => x.Id == kept.SdkCaseId);
        Assert.That(deletedCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(keptCase.WorkflowState, Is.EqualTo(
            sdkCaseRemovedByCompletion ? Constants.WorkflowStates.Removed : Constants.WorkflowStates.Created));

        // The PlanningCaseSite is soft-deleted and its (unshared) PlanningCase retracted.
        var pcs = await ItemsPlanningPnDbContext!.PlanningCaseSites.AsNoTracking()
            .SingleAsync(x => x.Id == deleted.PlanningCaseSiteId);
        var planningCase = await ItemsPlanningPnDbContext.PlanningCases.AsNoTracking()
            .SingleAsync(x => x.Id == deleted.PlanningCaseId);
        Assert.That(pcs.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(planningCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Retracted));

        // The marker: exactly one live IsDeleted exception for (ARP, Deadline date).
        var exceptions = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AsNoTracking()
            .Where(x => x.AreaRulePlanningId == series.ArpId)
            .ToListAsync();
        Assert.That(exceptions, Has.Count.EqualTo(1));
        Assert.That(exceptions[0].IsDeleted, Is.True);
        Assert.That(exceptions[0].OriginalDate.Date, Is.EqualTo(deleted.Deadline.Date));
        Assert.That(exceptions[0].WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
    }

    /// <summary>
    /// REGRESSION (#1290). The old Delete reported success on EVERY call, even though the
    /// second one — and in fact the first — wrote nothing.
    /// </summary>
    [Test]
    public async Task DeleteCompletedLog_Twice_SecondDeleteIsAFailureNotASuccess()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Delete-twice");
        var occ = await SeedOccurrence(series, PastDay(7), completed: true);
        var service = BuildCompliancesService(core);

        var first = await service.Delete(occ.ComplianceId);
        ClearTrackers();
        var second = await service.Delete(occ.ComplianceId);
        ClearTrackers();

        Assert.That(first.Success, Is.True, first.Message);
        Assert.That(second.Success, Is.False, "a second delete of the same log must not report success");
        Assert.That(second.Message, Is.EqualTo("ComplianceLogAlreadyDeleted"));

        var exceptions = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AsNoTracking()
            .Where(x => x.AreaRulePlanningId == series.ArpId)
            .ToListAsync();
        Assert.That(exceptions, Has.Count.EqualTo(1), "the second call must not write a second marker");
    }

    /// <summary>
    /// A shared PlanningCase with another LIVE PlanningCaseSite must survive; it is only
    /// retracted when the deleted site was its last live child.
    /// </summary>
    [Test]
    public async Task DeleteCompletedLog_PlanningCaseWithALiveSibling_IsKept()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Shared-planning-case");
        var deleted = await SeedOccurrence(series, PastDay(14), completed: true);
        // A second site's PlanningCaseSite on the SAME PlanningCase (a different
        // occurrence date so the Compliance (PlanningId, Deadline) key stays unique).
        var sibling = await SeedOccurrence(series, PastDay(7), completed: true, planningCaseId: deleted.PlanningCaseId);

        var result = await BuildCompliancesService(core).Delete(deleted.ComplianceId);
        Assert.That(result.Success, Is.True, result.Message);
        ClearTrackers();

        var planningCase = await ItemsPlanningPnDbContext!.PlanningCases.AsNoTracking()
            .SingleAsync(x => x.Id == deleted.PlanningCaseId);
        var deletedPcs = await ItemsPlanningPnDbContext.PlanningCaseSites.AsNoTracking()
            .SingleAsync(x => x.Id == deleted.PlanningCaseSiteId);
        var siblingPcs = await ItemsPlanningPnDbContext.PlanningCaseSites.AsNoTracking()
            .SingleAsync(x => x.Id == sibling.PlanningCaseSiteId);

        Assert.Multiple(() =>
        {
            Assert.That(deletedPcs.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(siblingPcs.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(planningCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Processed),
                "a PlanningCase with a live sibling site must not be retracted");
        });
    }

    /// <summary>
    /// A completed log whose planning has no live AreaRulePlanning cannot carry the marker
    /// the report reads (its exception lookup only consults live ARPs), so it is refused —
    /// and NOTHING is mutated, rather than deleting the answers of a log that would stay
    /// visible.
    /// </summary>
    [Test]
    public async Task DeleteCompletedLog_WithNoLiveAreaRulePlanning_FailsAndMutatesNothing()
    {
        var core = await SharedCore();
        var series = await SeedSeries("No-live-arp");
        var occ = await SeedOccurrence(series, PastDay(7), completed: true);
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings.SingleAsync(x => x.Id == occ.ArpId);
        await arp.Delete(BackendConfigurationPnDbContext);
        ClearTrackers();

        var result = await BuildCompliancesService(core).Delete(occ.ComplianceId);
        ClearTrackers();

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ComplianceLogCannotBeDeleted"));

        var sdkCase = await MicrotingDbContext!.Cases.AsNoTracking().SingleAsync(x => x.Id == occ.SdkCaseId);
        var pcs = await ItemsPlanningPnDbContext!.PlanningCaseSites.AsNoTracking()
            .SingleAsync(x => x.Id == occ.PlanningCaseSiteId);
        Assert.Multiple(async () =>
        {
            Assert.That(sdkCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(pcs.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AsNoTracking()
                .CountAsync(x => x.AreaRulePlanningId == occ.ArpId), Is.Zero);
        });
    }

    /// <summary>
    /// The marker is an UPSERT on the unique (AreaRulePlanningId, OriginalDate): an existing
    /// live override for the occurrence (here a start-hour change) is flipped to IsDeleted,
    /// and a soft-removed row is revived — never a second row, which the unique index would
    /// reject.
    /// </summary>
    [TestCase(false, TestName = "DeleteCompletedLog_ExistingLiveException_IsFlippedNotDuplicated")]
    [TestCase(true, TestName = "DeleteCompletedLog_ExistingRemovedException_IsRevivedNotDuplicated")]
    public async Task DeleteCompletedLog_ExistingException_IsUpserted(bool existingRemoved)
    {
        var core = await SharedCore();
        var series = await SeedSeries("Upsert-exception");
        var occ = await SeedOccurrence(series, PastDay(7), completed: true);

        var existing = new CalendarOccurrenceException
        {
            AreaRulePlanningId = occ.ArpId,
            OriginalDate = DateTime.SpecifyKind(occ.Deadline.Date, DateTimeKind.Utc),
            IsDeleted = false,
            StartHour = 13.0,
            WorkflowState = existingRemoved ? Constants.WorkflowStates.Removed : Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(existing);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        ClearTrackers();

        var result = await BuildCompliancesService(core).Delete(occ.ComplianceId);
        Assert.That(result.Success, Is.True, result.Message);
        ClearTrackers();

        var rows = await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.AsNoTracking()
            .Where(x => x.AreaRulePlanningId == occ.ArpId)
            .ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Id, Is.EqualTo(existing.Id));
        Assert.That(rows[0].IsDeleted, Is.True);
        Assert.That(rows[0].WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(await DetaljerIds(core, occ.PropertyId), Does.Not.Contain(occ.ComplianceId));
    }

    // ==================================================================
    // Deleting a not-done compliance: status quo, minus the false success
    // ==================================================================

    /// <summary>
    /// A not-done compliance is soft-deleted exactly as before — no SDK case delete, no
    /// PlanningCaseSite change, no exception row — and disappears from the reports.
    /// </summary>
    [Test]
    public async Task DeleteNotDoneCompliance_SoftDeletesTheComplianceOnly_AsBefore()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Delete-not-done");
        var occ = await SeedOccurrence(series, PastDay(7), completed: false);

        Assert.That(await DetaljerIds(core, occ.PropertyId), Does.Contain(occ.ComplianceId), "premise");

        var result = await BuildCompliancesService(core).Delete(occ.ComplianceId);
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Message, Is.EqualTo("TaskDeletedSuccessful"));
        ClearTrackers();

        var compliance = await BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
            .SingleAsync(x => x.Id == occ.ComplianceId);
        var sdkCase = await MicrotingDbContext!.Cases.AsNoTracking().SingleAsync(x => x.Id == occ.SdkCaseId);
        var pcs = await ItemsPlanningPnDbContext!.PlanningCaseSites.AsNoTracking()
            .SingleAsync(x => x.Id == occ.PlanningCaseSiteId);

        Assert.Multiple(async () =>
        {
            Assert.That(compliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That(sdkCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(pcs.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
            Assert.That(await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.AsNoTracking()
                .CountAsync(x => x.AreaRulePlanningId == occ.ArpId), Is.Zero);
            Assert.That(await DetaljerIds(core, occ.PropertyId), Does.Not.Contain(occ.ComplianceId));
        });
    }

    /// <summary>
    /// REGRESSION (#1290). Re-deleting an already-removed, not-done compliance used to
    /// report success over a no-op.
    /// </summary>
    [Test]
    public async Task DeleteNotDoneCompliance_Twice_SecondDeleteIsAFailure()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Delete-not-done-twice");
        var occ = await SeedOccurrence(series, PastDay(7), completed: false);
        var service = BuildCompliancesService(core);

        var first = await service.Delete(occ.ComplianceId);
        ClearTrackers();
        var second = await service.Delete(occ.ComplianceId);

        Assert.That(first.Success, Is.True, first.Message);
        Assert.That(second.Success, Is.False);
        Assert.That(second.Message, Is.EqualTo("ComplianceLogAlreadyDeleted"));
    }

    [Test]
    public async Task Delete_UnknownId_IsAFailure()
    {
        var core = await SharedCore();
        var result = await BuildCompliancesService(core).Delete(int.MaxValue);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("ComplianceNotFound"));
    }
    // ==================================================================
    // Durability: series-level exception purges keep the marker
    // ==================================================================

    /// <summary>
    /// The real calendar service over the REAL core — the purge helper reads the
    /// completed-ness of the marker's date off the SDK case. Everything the move and
    /// resize paths under test do not reach is substituted.
    /// </summary>
    private BackendConfigurationCalendarService BuildCalendarService(Core core)
    {
        var coreHelper = CoreHelper(core);
        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(),
            UserService(),
            BackendConfigurationPnDbContext!,
            coreHelper,
            Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!,
            Substitute.For<IBackendConfigurationTaskWizardService>(),
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper));
    }

    private static DateTime NextMonday()
    {
        var today = DateTime.UtcNow.Date;
        var days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(days == 0 ? 7 : days);
    }

    private async Task<CalendarOccurrenceException> SeedDeletedException(int arpId, DateTime date)
    {
        var exception = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
            IsDeleted = true,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(exception);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return exception;
    }

    private async Task<string?> ExceptionState(int exceptionId)
        => (await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AsNoTracking()
            .SingleAsync(x => x.Id == exceptionId)).WorkflowState;

    /// <summary>
    /// Everything one purge test needs: a series with a deleted COMPLETED log (deleted
    /// through the real Delete), plus the two IsDeleted markers whose purge must stay
    /// exactly as before — an "only this" delete of a FUTURE never-deployed occurrence,
    /// and one of a future occurrence that is deployed but NOT done.
    /// </summary>
    private async Task<(Occurrence Deleted, int DeletedMarkerId, int FutureMarkerId, int NotDoneMarkerId)>
        SeedDeletedLogAndOtherMarkers(Core core, string title)
    {
        var series = await SeedSeries(title);
        var deleted = await SeedOccurrence(series, PastDay(7), completed: true);
        var result = await BuildCompliancesService(core).Delete(deleted.ComplianceId);
        Assert.That(result.Success, Is.True, result.Message);
        ClearTrackers();

        var deletedMarker = await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AsNoTracking()
            .SingleAsync(x => x.AreaRulePlanningId == series.ArpId && x.IsDeleted);

        var futureMarker = await SeedDeletedException(series.ArpId, NextMonday().AddDays(14));
        var notDone = await SeedOccurrence(series, NextMonday().AddDays(21), completed: false);
        var notDoneMarker = await SeedDeletedException(series.ArpId, notDone.Deadline);
        ClearTrackers();

        return (deleted, deletedMarker.Id, futureMarker.Id, notDoneMarker.Id);
    }

    private async Task AssertDeletedLogStillHiddenAndOtherMarkersPurged(
        Core core, Occurrence deleted, int deletedMarkerId, int futureMarkerId, int notDoneMarkerId)
    {
        ClearTrackers();
        Assert.Multiple(async () =>
        {
            Assert.That(await ExceptionState(deletedMarkerId), Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "the deleted completed log's marker must survive the series edit");
            Assert.That(await DetaljerIds(core, deleted.PropertyId), Does.Not.Contain(deleted.ComplianceId),
                "Detaljer: the deleted log came back");
            Assert.That(await RapportIds(core, deleted.PropertyId), Does.Not.Contain(deleted.ComplianceId),
                "Rapport: the deleted log came back");
            Assert.That(await ExceptionState(futureMarkerId), Is.EqualTo(Constants.WorkflowStates.Removed),
                "a future 'only this' delete of a never-deployed occurrence is purged as before");
            Assert.That(await ExceptionState(notDoneMarkerId), Is.EqualTo(Constants.WorkflowStates.Removed),
                "an 'only this' delete of a deployed NOT-done occurrence is purged as before");
        });
    }

    /// <summary>
    /// REGRESSION (#1290 follow-up). MoveTask "all" soft-deleted EVERY exception of the
    /// series, including the IsDeleted marker of a deleted completed log — which then
    /// reappeared in the calendar and in all three report views.
    /// </summary>
    [Test]
    public async Task DeleteCompletedLog_ThenMoveSeriesAll_LogStaysHidden_OtherMarkersPurgedAsBefore()
    {
        var core = await SharedCore();
        var (deleted, deletedMarkerId, futureMarkerId, notDoneMarkerId) =
            await SeedDeletedLogAndOtherMarkers(core, "Move-all-after-delete");

        var from = NextMonday();
        var move = await BuildCalendarService(core).MoveTask(new CalendarTaskMoveRequestModel
        {
            Id = deleted.ArpId,
            OriginalDate = from.ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewDate = from.AddDays(1).ToString("yyyy-MM-dd") + "T00:00:00Z",
            NewStartHour = 9.0,
            Scope = "all"
        });
        Assert.That(move.Success, Is.True, move.Message);

        await AssertDeletedLogStillHiddenAndOtherMarkersPurged(
            core, deleted, deletedMarkerId, futureMarkerId, notDoneMarkerId);
    }

    /// <summary>REGRESSION (#1290 follow-up) — ResizeTask "all" purges every exception too.</summary>
    [Test]
    public async Task DeleteCompletedLog_ThenResizeSeriesAll_LogStaysHidden_OtherMarkersPurgedAsBefore()
    {
        var core = await SharedCore();
        var (deleted, deletedMarkerId, futureMarkerId, notDoneMarkerId) =
            await SeedDeletedLogAndOtherMarkers(core, "Resize-all-after-delete");

        var resize = await BuildCalendarService(core).ResizeTask(new CalendarTaskResizeRequestModel
        {
            Id = deleted.ArpId,
            NewStartHour = 10.0,
            NewDuration = 2.0,
            Scope = "all"
        });
        Assert.That(resize.Success, Is.True, resize.Message);

        await AssertDeletedLogStillHiddenAndOtherMarkersPurged(
            core, deleted, deletedMarkerId, futureMarkerId, notDoneMarkerId);
    }
}
