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
using eFormCore;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
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
/// Regression coverage for the "thisAndFollowing" delete leaving deployed
/// occurrences behind.
///
/// <c>BackendConfigurationCalendarService.DeleteTask</c> with
/// <c>scope == "thisAndFollowing"</c> trims the series end
/// (<c>planning.RepeatUntil</c> / <c>arp.EndDate</c>) and deletes stale
/// <c>CalendarOccurrenceException</c>s, but did NOT retract the already-DEPLOYED
/// <c>Compliance</c> rows / SDK cases for occurrence dates on/after the clicked
/// date. The compliance-render loop in <c>GetTasksForWeek</c> is NOT bounded by
/// <c>RepeatUntil</c>/<c>EndDate</c> — it only skips per-occurrence
/// <c>CalendarOccurrenceException</c> with <c>IsDeleted == true</c> — so deployed
/// occurrences on/after the clicked date kept rendering ("not deleted").
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarDeleteThisAndFollowingTests : TestBaseSetup
{
    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime GetNextMonday()
    {
        var today = DateTime.UtcNow.Date;
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7; // always strictly future
        return DateTime.SpecifyKind(today.AddDays(daysUntilMonday), DateTimeKind.Utc);
    }

    [SetUp]
    public async Task CleanCalendarTables()
    {
        // FK-safe cleanup so each test starts fresh (base [SetUp] already ran).
        BackendConfigurationPnDbContext!.CalendarOccurrenceExceptionSites.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptionSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptions);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.Compliances.RemoveRange(
            BackendConfigurationPnDbContext.Compliances);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRulePlannings.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlannings);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // AreaRuleTranslation -> AreaRule is DeleteBehavior.Restrict, so the
        // children must go first. The SQL dump used to truncate this table
        // before every test; it now replays once per fixture, so leftover
        // translations would block the delete below with MySQL 1451.
        BackendConfigurationPnDbContext.AreaRuleTranslations.RemoveRange(
            BackendConfigurationPnDbContext.AreaRuleTranslations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AreaRules.RemoveRange(
            BackendConfigurationPnDbContext.AreaRules);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.Areas.RemoveRange(
            BackendConfigurationPnDbContext.Areas);
        BackendConfigurationPnDbContext.Properties.RemoveRange(
            BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext!.PlanningCaseSites.RemoveRange(
            ItemsPlanningPnDbContext.PlanningCaseSites);
        ItemsPlanningPnDbContext.PlanningCases.RemoveRange(
            ItemsPlanningPnDbContext.PlanningCases);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext.Plannings.RemoveRange(
            ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        MicrotingDbContext!.Cases.RemoveRange(MicrotingDbContext.Cases);
        await MicrotingDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds an SDK Site + Case with the given completion status and returns the
    /// Case Id. The Case is left with a null MicrotingUid (no real
    /// deployed-to-device backing): the retraction path guards
    /// <c>MicrotingUid != null</c> before <c>core.CaseDelete</c>, so this keeps the
    /// test deterministic while still exercising the Compliance + PlanningCase /
    /// PlanningCaseSite bookkeeping retraction (the same approach
    /// CalendarComplianceMoveTests uses).
    /// </summary>
    private async Task<int> SeedSdkCase(int status, int siteUid)
    {
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var sdkSite = new Site
        {
            Name = $"delete-thisandfollowing-site-{siteUid}",
            MicrotingUid = siteUid,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        var sdkCase = new Case
        {
            SiteId = sdkSite.Id,
            Status = status,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        return sdkCase.Id;
    }

    /// <summary>
    /// Seeds Area→Property→AreaRule(+translation)→Planning(daily)→
    /// AreaRulePlanning(daily)→CalendarConfiguration. Returns
    /// (arpId, propertyId, planningId, areaId).
    /// </summary>
    private async Task<(int ArpId, int PropertyId, int PlanningId, int AreaId)> SeedDailySeries(
        DateTime startDate)
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
            Name = $"DeleteThisAndFollowingTest-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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

        var areaRuleTranslation = new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = "Delete ThisAndFollowing Title",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(areaRuleTranslation);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Day,
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
            RelatedEFormId = 0, WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Daily series: RepeatType=1 (days), RepeatEvery=1.
        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id,
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc), Status = true,
            RepeatType = 1, RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(calConfig);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (arp.Id, property.Id, planning.Id, area.Id);
    }

    /// <summary>
    /// Seeds a DEPLOYED Compliance row (MicrotingSdkCaseId &gt; 0) at the given
    /// deadline, plus its backing PlanningCase + PlanningCaseSite bookkeeping.
    /// Returns the Compliance Id.
    /// </summary>
    private async Task<int> SeedDeployedCompliance(int planningId, int propertyId, int areaId,
        DateTime deadline, int sdkCaseId, int sdkSiteId)
    {
        var planningCase = new PlanningCase
        {
            PlanningId = planningId,
            MicrotingSdkCaseId = sdkCaseId,
            MicrotingSdkeFormId = 0,
            Status = 66,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var planningCaseSite = new PlanningCaseSite
        {
            PlanningCaseId = planningCase.Id,
            PlanningId = planningId,
            MicrotingSdkCaseId = sdkCaseId,
            MicrotingSdkSiteId = sdkSiteId,
            MicrotingSdkeFormId = 0,
            Status = 66,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var compliance = new Compliance
        {
            PlanningId = planningId,
            PropertyId = propertyId,
            AreaId = areaId,
            Deadline = DateTime.SpecifyKind(deadline, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.AddDays(-1), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCaseId,
            MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance.Id;
    }

    private BackendConfigurationCalendarService BuildService(Core core)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(
            new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        var taskWizardService = Substitute.For<IBackendConfigurationTaskWizardService>();
        taskWizardService.DeleteTask(Arg.Any<int>())
            .Returns(Task.FromResult(new OperationResult(true)));

        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, taskWizardService,
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            // #1122 — the calendar service now delegates the retract/backfill
            // halves of a cross-period re-anchor. Neither fires in these fixtures.
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            Substitute.For<IBackendConfigurationComplianceReportService>());
    }

    private async Task<List<CalendarTaskResponseModel>> TilesForWeek(
        BackendConfigurationCalendarService service, int propertyId, DateTime weekMonday, int planningId)
    {
        var result = await service.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = IsoUtc(weekMonday),
            WeekEnd = IsoUtc(weekMonday.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false,
            BoardIds = [], TagNames = [], SiteIds = []
        });
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.Not.Null);
        return result.Model.Where(t => t.PlanningId == planningId).ToList();
    }

    [Test]
    public async Task DeleteThisAndFollowing_RetractsDeployedOccurrencesOnOrAfterOriginalDate()
    {
        var core = await GetCore();

        var monday = GetNextMonday();
        var (arpId, propertyId, planningId, areaId) = await SeedDailySeries(monday);

        // The clicked ("originalDate") day is StartDate + 2 (strictly after StartDate
        // so it does NOT fall into DeleteEntireSeries).
        var originalDate = monday.AddDays(2);

        // SURVIVOR: a deployed occurrence on StartDate (before originalDate).
        var beforeCaseId = await SeedSdkCase(status: 66, siteUid: 7001);
        var beforeSdkSiteId = (await MicrotingDbContext!.Sites
            .OrderBy(s => s.Id).LastAsync()).Id;
        var beforeComplianceId = await SeedDeployedCompliance(
            planningId, propertyId, areaId, monday, beforeCaseId, beforeSdkSiteId);

        // RETRACT: deployed occurrence ON originalDate (StartDate + 2).
        var onCaseId = await SeedSdkCase(status: 66, siteUid: 7002);
        var onSdkSiteId = (await MicrotingDbContext.Sites
            .OrderBy(s => s.Id).LastAsync()).Id;
        var onComplianceId = await SeedDeployedCompliance(
            planningId, propertyId, areaId, originalDate, onCaseId, onSdkSiteId);

        // RETRACT: deployed occurrence on originalDate + 1 (StartDate + 3).
        var afterCaseId = await SeedSdkCase(status: 66, siteUid: 7003);
        var afterSdkSiteId = (await MicrotingDbContext.Sites
            .OrderBy(s => s.Id).LastAsync()).Id;
        var afterComplianceId = await SeedDeployedCompliance(
            planningId, propertyId, areaId, originalDate.AddDays(1), afterCaseId, afterSdkSiteId);

        var service = BuildService(core);

        // Sanity: before delete, the week shows tiles on/after originalDate.
        var before = await TilesForWeek(service, propertyId, monday, planningId);
        Assert.That(before.Any(t => t.TaskDate == Key(originalDate)), Is.True,
            "pre-delete sanity: originalDate occurrence renders");

        // ----- ACT: delete this and following -----
        var deleteResult = await service.DeleteTask(new CalendarTaskDeleteRequestModel
        {
            Id = arpId,
            Scope = "thisAndFollowing",
            OriginalDate = originalDate.ToString("yyyy-MM-dd")
        });
        Assert.That(deleteResult.Success, Is.True, deleteResult.Message);

        // ----- ASSERT (a): render-level -----
        var after = await TilesForWeek(service, propertyId, monday, planningId);
        Assert.Multiple(() =>
        {
            Assert.That(after.Any(t => t.TaskDate == Key(monday)), Is.True,
                "the occurrence BEFORE originalDate must survive and still render");
            Assert.That(after.Any(t => string.Compare(t.TaskDate, Key(originalDate),
                    StringComparison.Ordinal) >= 0), Is.False,
                "no occurrence on/after originalDate may render after thisAndFollowing delete");
        });

        // ----- ASSERT (b): Compliance soft-deleted on/after, survivor untouched -----
        var beforeCompliance = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().SingleAsync(x => x.Id == beforeComplianceId);
        var onCompliance = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().SingleAsync(x => x.Id == onComplianceId);
        var afterCompliance = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().SingleAsync(x => x.Id == afterComplianceId);
        Assert.Multiple(() =>
        {
            Assert.That(beforeCompliance.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "the survivor compliance (before originalDate) stays live");
            Assert.That(onCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the compliance ON originalDate is soft-deleted");
            Assert.That(afterCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the compliance AFTER originalDate is soft-deleted");
        });

        // ----- ASSERT (c): bookkeeping + SDK case retracted on/after -----
        var onPlanningCaseSites = await ItemsPlanningPnDbContext!.PlanningCaseSites
            .AsNoTracking()
            .Where(x => x.MicrotingSdkCaseId == onCaseId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        var afterPlanningCaseSites = await ItemsPlanningPnDbContext.PlanningCaseSites
            .AsNoTracking()
            .Where(x => x.MicrotingSdkCaseId == afterCaseId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        var survivorPlanningCaseSites = await ItemsPlanningPnDbContext.PlanningCaseSites
            .AsNoTracking()
            .Where(x => x.MicrotingSdkCaseId == beforeCaseId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(onPlanningCaseSites, Is.Empty,
                "the PlanningCaseSite for the originalDate occurrence is retracted");
            Assert.That(afterPlanningCaseSites, Is.Empty,
                "the PlanningCaseSite for the after-originalDate occurrence is retracted");
            Assert.That(survivorPlanningCaseSites, Has.Count.EqualTo(1),
                "the survivor's PlanningCaseSite is untouched");
        });
    }

    /// <summary>
    /// Default <c>scope == "thisAndFollowing"</c> preserves COMPLETED history:
    /// a deployed occurrence ON originalDate whose backing SDK Case is Status=100
    /// must STILL render (and its Compliance row must NOT be removed), while a
    /// pending occurrence after it is retracted. Locks the "completed = frozen"
    /// default behavior.
    /// </summary>
    [Test]
    public async Task DeleteThisAndFollowing_PreservesCompletedOccurrence()
    {
        var core = await GetCore();

        var monday = GetNextMonday();
        var (arpId, propertyId, planningId, areaId) = await SeedDailySeries(monday);

        var originalDate = monday.AddDays(2);

        // COMPLETED occurrence ON originalDate (Status=100 backing SDK case).
        var completedCaseId = await SeedSdkCase(status: 100, siteUid: 7101);
        var completedSdkSiteId = (await MicrotingDbContext!.Sites
            .OrderBy(s => s.Id).LastAsync()).Id;
        var completedComplianceId = await SeedDeployedCompliance(
            planningId, propertyId, areaId, originalDate, completedCaseId, completedSdkSiteId);

        // PENDING occurrence on originalDate + 1.
        var pendingCaseId = await SeedSdkCase(status: 66, siteUid: 7102);
        var pendingSdkSiteId = (await MicrotingDbContext.Sites
            .OrderBy(s => s.Id).LastAsync()).Id;
        var pendingComplianceId = await SeedDeployedCompliance(
            planningId, propertyId, areaId, originalDate.AddDays(1), pendingCaseId, pendingSdkSiteId);

        var service = BuildService(core);

        var before = await TilesForWeek(service, propertyId, monday, planningId);
        Assert.That(before.Any(t => t.TaskDate == Key(originalDate)), Is.True,
            "pre-delete sanity: completed originalDate occurrence renders");

        var deleteResult = await service.DeleteTask(new CalendarTaskDeleteRequestModel
        {
            Id = arpId,
            Scope = "thisAndFollowing",
            OriginalDate = originalDate.ToString("yyyy-MM-dd")
        });
        Assert.That(deleteResult.Success, Is.True, deleteResult.Message);

        // ----- ASSERT (a): render-level — completed survives, pending removed -----
        var after = await TilesForWeek(service, propertyId, monday, planningId);
        Assert.Multiple(() =>
        {
            Assert.That(after.Any(t => t.TaskDate == Key(originalDate)), Is.True,
                "the COMPLETED occurrence ON originalDate must still render (frozen history)");
            Assert.That(after.Any(t => t.TaskDate == Key(originalDate.AddDays(1))), Is.False,
                "the pending occurrence after originalDate is removed");
        });

        // ----- ASSERT (b): completed Compliance NOT removed, pending removed -----
        var completedCompliance = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().SingleAsync(x => x.Id == completedComplianceId);
        var pendingCompliance = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().SingleAsync(x => x.Id == pendingComplianceId);
        Assert.Multiple(() =>
        {
            Assert.That(completedCompliance.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "the COMPLETED compliance on originalDate stays live (frozen history)");
            Assert.That(pendingCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the pending compliance after originalDate is soft-deleted");
        });
    }

    /// <summary>
    /// New <c>scope == "thisAndFollowingIncludingCompleted"</c> ALSO retracts
    /// COMPLETED occurrences on/after originalDate (full removal, like
    /// DeleteEntireSeries). The completed occurrence must NOT render after the
    /// delete, and must be suppressed at the DB level (Compliance soft-deleted
    /// and/or CalendarOccurrenceException IsDeleted written).
    /// </summary>
    [Test]
    public async Task DeleteThisAndFollowingIncludingCompleted_RemovesCompletedOccurrence()
    {
        var core = await GetCore();

        var monday = GetNextMonday();
        var (arpId, propertyId, planningId, areaId) = await SeedDailySeries(monday);

        var originalDate = monday.AddDays(2);

        // SURVIVOR: completed occurrence BEFORE originalDate (must stay).
        var survivorCaseId = await SeedSdkCase(status: 100, siteUid: 7201);
        var survivorSdkSiteId = (await MicrotingDbContext!.Sites
            .OrderBy(s => s.Id).LastAsync()).Id;
        var survivorComplianceId = await SeedDeployedCompliance(
            planningId, propertyId, areaId, monday, survivorCaseId, survivorSdkSiteId);

        // COMPLETED occurrence ON originalDate (Status=100) — must be removed.
        var completedCaseId = await SeedSdkCase(status: 100, siteUid: 7202);
        var completedSdkSiteId = (await MicrotingDbContext.Sites
            .OrderBy(s => s.Id).LastAsync()).Id;
        var completedComplianceId = await SeedDeployedCompliance(
            planningId, propertyId, areaId, originalDate, completedCaseId, completedSdkSiteId);

        var service = BuildService(core);

        var before = await TilesForWeek(service, propertyId, monday, planningId);
        Assert.That(before.Any(t => t.TaskDate == Key(originalDate)), Is.True,
            "pre-delete sanity: completed originalDate occurrence renders");

        var deleteResult = await service.DeleteTask(new CalendarTaskDeleteRequestModel
        {
            Id = arpId,
            Scope = "thisAndFollowingIncludingCompleted",
            OriginalDate = originalDate.ToString("yyyy-MM-dd")
        });
        Assert.That(deleteResult.Success, Is.True, deleteResult.Message);

        // ----- ASSERT (a): render-level — completed on originalDate gone, survivor stays -----
        var after = await TilesForWeek(service, propertyId, monday, planningId);
        Assert.Multiple(() =>
        {
            Assert.That(after.Any(t => t.TaskDate == Key(monday)), Is.True,
                "the completed occurrence BEFORE originalDate must survive and still render");
            Assert.That(after.Any(t => t.TaskDate == Key(originalDate)), Is.False,
                "the COMPLETED occurrence ON originalDate must NOT render after includingCompleted delete");
        });

        // ----- ASSERT (b): DB-level suppression for the removed completed occurrence -----
        var completedCompliance = await BackendConfigurationPnDbContext!.Compliances
            .AsNoTracking().SingleAsync(x => x.Id == completedComplianceId);
        var survivorCompliance = await BackendConfigurationPnDbContext.Compliances
            .AsNoTracking().SingleAsync(x => x.Id == survivorComplianceId);
        var deletedException = await BackendConfigurationPnDbContext.CalendarOccurrenceExceptions
            .AsNoTracking()
            .Where(x => x.AreaRulePlanningId == arpId
                        && x.OriginalDate.Date == originalDate.Date
                        && x.IsDeleted
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(survivorCompliance.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed),
                "the survivor compliance (before originalDate) stays live");
            Assert.That(completedCompliance.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "the completed occurrence's Compliance row is soft-deleted on the includingCompleted path");
            Assert.That(deletedException, Is.Not.Empty,
                "an IsDeleted CalendarOccurrenceException is written so the completed tile stops rendering");
        });
    }
}
