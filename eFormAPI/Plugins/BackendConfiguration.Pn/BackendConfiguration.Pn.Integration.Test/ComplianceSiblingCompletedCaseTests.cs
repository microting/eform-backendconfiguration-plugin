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
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationCompliancesService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.ComplianceSiblingCaseRepair;
using BackendConfiguration.Pn.Services.EventDeployService;
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
/// #1371 — a task assigned to Worker A and Worker B deploys one SDK case per worker under
/// one items-planning <c>PlanningCase</c>, and the <c>Compliance</c> stores Worker A's.
/// Worker B completes it through the legacy device path: the service plugin soft-removes
/// the compliance without repointing it (its #588) and Worker A's case is retracted. Judged
/// by its own case the occurrence was "not done + removed" — deleted by a user — so it
/// vanished from Rapport/Detaljer/Oversigt and the calendar drew it as not completed.
///
/// <para>The readers now resolve the completed sibling (<c>CompletedSiblingCases</c>), and
/// the admin repair repoints the stored rows. Every SDK case is seeded with
/// <c>MicrotingUid = null</c> so no platform call is attempted.</para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ComplianceSiblingCompletedCaseTests : TestBaseSetup
{
    private const int CompletedStatus = 100;
    private const int InProgressStatus = 66;

    private Core? _core;
    private int _uidCounter = 971_000;

    private async Task<Core> SharedCore() => _core ??= await GetCore();

    /// <summary>
    /// The repair scans every removed compliance, so each test starts without the rows
    /// earlier tests left behind (the fixture's databases are shared across its tests).
    /// </summary>
    [SetUp]
    public async Task CleanTables()
    {
        BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptions);
        BackendConfigurationPnDbContext.Compliances.RemoveRange(BackendConfigurationPnDbContext.Compliances);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext!.PlanningCaseSites.RemoveRange(ItemsPlanningPnDbContext.PlanningCaseSites);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        ItemsPlanningPnDbContext.PlanningCases.RemoveRange(ItemsPlanningPnDbContext.PlanningCases);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        ClearTrackers();
    }

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

    private BackendConfigurationComplianceReportService BuildReportService(Core core)
    {
        var coreHelper = CoreHelper(core);
        return new BackendConfigurationComplianceReportService(
            new BackendConfigurationLocalizationService(), UserService(),
            BackendConfigurationPnDbContext!, coreHelper, ItemsPlanningPnDbContext!,
            TestContextLogger<BackendConfigurationComplianceReportService>.Instance,
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext));
    }

    private BackendConfigurationCalendarService BuildCalendarService(Core core)
    {
        var coreHelper = CoreHelper(core);
        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), UserService(),
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, Substitute.For<IBackendConfigurationTaskWizardService>(),
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            TestContextLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext));
    }

    private BackendConfigurationCompliancesService BuildCompliancesService(Core core)
        => new(
            ItemsPlanningPnDbContext!, BackendConfigurationPnDbContext!, UserService(),
            new BackendConfigurationLocalizationService(), CoreHelper(core), TimePlanningPnDbContext!);

    private ComplianceSiblingCaseRepairService BuildRepairService(Core core)
        => new(BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, CoreHelper(core),
            TestContextLogger<ComplianceSiblingCaseRepairService>.Instance);

    // ------------------------------------------------------------------
    // Seeding
    // ------------------------------------------------------------------

    private sealed class Series
    {
        public int PropertyId;
        public int AreaId;
        public int ArpId;
        public int PlanningId;
    }

    private sealed class Occurrence
    {
        public int ComplianceId;
        public int PlanningCaseId;
        public int OwnCaseId;
        public int OwnPlanningCaseSiteId;
        public int CompletedCaseId;
        public int CompletedPlanningCaseSiteId;
        public int CompleterSiteId;
        public DateTime CompletedAt;
    }

    private async Task<int> SeedSdkSite(string name)
    {
        var uid = ++_uidCounter;
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"{name}-{uid}", MicrotingUid = uid, LanguageId = language.Id,
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
            Label = $"sibling-case-{Guid.NewGuid()}", ParentId = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.CheckLists.AddAsync(checkList);
        await MicrotingDbContext.SaveChangesAsync();
        return checkList.Id;
    }

    /// <summary>
    /// A weekly Monday series — or, with <paramref name="monthlyFirstThursday"/>, a monthly
    /// "1st Thursday" series — with a report headline (Rapport excludes headline-less
    /// tasks, #1301).
    /// </summary>
    private async Task<Series> SeedSeries(string title, bool monthlyFirstThursday = false)
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
            Name = $"SiblingCase-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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
            Enabled = true, RepeatEvery = 1,
            RepeatType = monthlyFirstThursday ? RepeatType.Month : RepeatType.Week,
            StartDate = startDate,
            DayOfWeek = monthlyFirstThursday ? DayOfWeek.Thursday : DayOfWeek.Monday, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var headline = new PlanningTag
        {
            Name = $"SiblingCase headline {Guid.NewGuid()}",
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningTags.AddAsync(headline);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            ItemPlanningTagId = headline.Id,
            // #1325: missed occurrences are reported, so a not-done one would be "open".
            ComplianceEnabled = true,
            RepeatEvery = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        if (monthlyFirstThursday)
        {
            arp.RepeatType = 3;
            arp.RepeatOrdinalWeek = 1;
            arp.DayOfWeek = 4;
        }
        else
        {
            arp.RepeatType = 2;
            arp.RepeatWeekdaysCsv = "1";
            arp.DayOfWeek = 1;
        }
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Series
        {
            PropertyId = property.Id, AreaId = area.Id, ArpId = arp.Id, PlanningId = planning.Id
        };
    }

    private async Task<(int CaseId, int PlanningCaseSiteId)> SeedWorkerCase(
        Series series, int planningCaseId, int siteId, int checkListId, int status, DateTime? doneAt,
        string workflowState = Constants.WorkflowStates.Created)
    {
        var sdkCase = new Case
        {
            SiteId = siteId, Status = status, DoneAt = doneAt, DoneAtUserModifiable = doneAt,
            CheckListId = checkListId, MicrotingUid = null, WorkflowState = workflowState
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();

        var planningCaseSite = new PlanningCaseSite
        {
            PlanningId = series.PlanningId, PlanningCaseId = planningCaseId,
            MicrotingSdkSiteId = siteId, MicrotingSdkeFormId = checkListId,
            MicrotingSdkCaseId = sdkCase.Id, Status = status, MicrotingSdkCaseDoneAt = doneAt,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCaseSites.AddAsync(planningCaseSite);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        return (sdkCase.Id, planningCaseSite.Id);
    }

    /// <summary>
    /// The #1371 shape: one occurrence deployed to Worker A and Worker B under one
    /// PlanningCase; the compliance holds Worker A's case, which is retracted and not
    /// completed; the compliance is soft-removed. <paramref name="siblingCompleted"/>
    /// false seeds Worker B's case in progress too — the genuinely deleted shape.
    /// </summary>
    private async Task<Occurrence> SeedTwoWorkerOccurrence(
        Series series, DateTime deadline, bool siblingCompleted = true)
    {
        var checkListId = await SeedCheckList();
        var workerA = await SeedSdkSite("worker-a");
        var workerB = await SeedSdkSite("worker-b");
        var completedAt = DateTime.SpecifyKind(deadline.Date.AddHours(10), DateTimeKind.Utc);

        var planningCase = new PlanningCase
        {
            PlanningId = series.PlanningId, MicrotingSdkeFormId = checkListId,
            Status = siblingCompleted ? CompletedStatus : InProgressStatus,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var (ownCaseId, ownPcsId) = await SeedWorkerCase(series, planningCase.Id, workerA, checkListId,
            InProgressStatus, doneAt: null, Constants.WorkflowStates.Retracted);
        var (completedCaseId, completedPcsId) = await SeedWorkerCase(series, planningCase.Id, workerB, checkListId,
            siblingCompleted ? CompletedStatus : InProgressStatus, siblingCompleted ? completedAt : null);

        var compliance = new Compliance
        {
            ItemName = "Sibling case item",
            PlanningId = series.PlanningId, PropertyId = series.PropertyId, AreaId = series.AreaId,
            Deadline = DateTime.SpecifyKind(deadline.Date, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.Date.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = ownCaseId, MicrotingSdkeFormId = checkListId,
            PlanningCaseSiteId = planningCase.Id,
            WorkflowState = Constants.WorkflowStates.Removed,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        ClearTrackers();

        return new Occurrence
        {
            ComplianceId = compliance.Id, PlanningCaseId = planningCase.Id,
            OwnCaseId = ownCaseId, OwnPlanningCaseSiteId = ownPcsId,
            CompletedCaseId = completedCaseId, CompletedPlanningCaseSiteId = completedPcsId,
            CompleterSiteId = workerB, CompletedAt = completedAt
        };
    }

    // ------------------------------------------------------------------
    // Reading
    // ------------------------------------------------------------------

    private static (DateTime From, DateTime To) Window()
    {
        var today = DateTime.UtcNow.Date;
        return (today.AddDays(-90), today.AddDays(30));
    }

    private static ComplianceReportRequestModel ReportRequest(
        int propertyId, string status, DateTime? from = null, DateTime? to = null, bool includeProjected = false) =>
        new()
        {
            IncludeProjected = includeProjected,
            DateFrom = from ?? Window().From, DateTo = to ?? Window().To, Status = status, PropertyId = propertyId,
            BoardIds = [], TagIds = [], SiteIds = [], PageIndex = 0, PageSize = 0
        };

    private async Task<List<ComplianceReportRowModel>> Detaljer(
        Core core, int propertyId, string status, DateTime? from = null, DateTime? to = null,
        bool includeProjected = false)
    {
        var result = await BuildReportService(core).Index(
            ReportRequest(propertyId, status, from, to, includeProjected));
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model.Entities;
    }

    private async Task<List<ComplianceReportCaseModel>> Rapport(Core core, int propertyId)
    {
        var result = await BuildReportService(core).EformColumns(ReportRequest(propertyId, "all"));
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model.SelectMany(g => g.Templates).SelectMany(t => t.Cases).ToList();
    }

    private async Task<(int Total, int Done)> Oversigt(Core core, int propertyId)
    {
        var (from, to) = Window();
        var result = await BuildReportService(core).Overview(new ComplianceReportOverviewRequestModel
        {
            DateFrom = from, DateTo = to, PropertyId = propertyId, BoardIds = [], TagIds = [], SiteIds = []
        });
        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model.Rows.SingleOrDefault(r => r.PropertyId == propertyId);
        return row == null ? (0, 0) : (row.Total, row.Done);
    }

    private Task<List<CalendarTaskResponseModel>> TilesOn(
        Core core, int propertyId, DateTime monday, bool actionableOnly)
        => TilesOn(core, propertyId, monday, monday, actionableOnly);

    /// <summary>The tiles on <paramref name="date"/> in the week starting <paramref name="monday"/>.</summary>
    private async Task<List<CalendarTaskResponseModel>> TilesOn(
        Core core, int propertyId, DateTime monday, DateTime date, bool actionableOnly)
    {
        static string IsoUtc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var result = await BuildCalendarService(core).GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = IsoUtc(monday),
            WeekEnd = IsoUtc(monday.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = actionableOnly,
            BoardIds = [], TagNames = [], SiteIds = []
        });
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model.Where(t => t.TaskDate == Key(date)).ToList();
    }

    private static string Key(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime MondayOf(DateTime date) =>
        DateTime.SpecifyKind(date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7)), DateTimeKind.Utc);

    private static DateTime FirstThursdayOf(DateTime month)
    {
        var first = new DateTime(month.Year, month.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return first.AddDays(((int)DayOfWeek.Thursday - (int)first.DayOfWeek + 7) % 7);
    }

    private Task<string> SiteName(int siteId) =>
        MicrotingDbContext!.Sites.Where(s => s.Id == siteId).Select(s => s.Name).SingleAsync();

    private Task<Compliance> StoredCompliance(int complianceId) =>
        BackendConfigurationPnDbContext!.Compliances.AsNoTracking().SingleAsync(x => x.Id == complianceId);

    private void ClearTrackers()
    {
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();
        ItemsPlanningPnDbContext!.ChangeTracker.Clear();
        MicrotingDbContext!.ChangeTracker.Clear();
    }

    /// <summary>A Monday at least a week back — a rule date of the weekly Monday series, overdue.</summary>
    private static DateTime PastMonday()
    {
        var today = DateTime.UtcNow.Date;
        var sinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return DateTime.SpecifyKind(today.AddDays(-sinceMonday - 7), DateTimeKind.Utc);
    }

    private static DateTime NextMonday()
    {
        var today = DateTime.UtcNow.Date;
        var days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return DateTime.SpecifyKind(today.AddDays(days == 0 ? 7 : days), DateTimeKind.Utc);
    }

    // ==================================================================
    // Compliance report: Detaljer, Rapport, Oversigt
    // ==================================================================

    /// <summary>
    /// The occurrence Worker B completed counts as completed by Worker B in all three
    /// views. <b>Fails on the old code</b>: it was "not done + removed" and hidden.
    /// </summary>
    [Test]
    public async Task Report_CompletedBySecondWorker_IsListedAsDoneByThatWorker()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Completed by worker B");
        var occ = await SeedTwoWorkerOccurrence(series, PastMonday());
        var completerName = await SiteName(occ.CompleterSiteId);

        var all = await Detaljer(core, series.PropertyId, "all");
        var open = await Detaljer(core, series.PropertyId, "open");
        var rapport = await Rapport(core, series.PropertyId);

        var row = all.SingleOrDefault(r => r.ComplianceId == occ.ComplianceId);
        Assert.That(row, Is.Not.Null, "Detaljer: the completed log is listed");
        var rapportCase = rapport.SingleOrDefault(c => c.ComplianceId == occ.ComplianceId);
        Assert.That(rapportCase, Is.Not.Null, "Rapport: the completed log is listed");

        Assert.Multiple(async () =>
        {
            Assert.That(row!.Completed, Is.True, "Detaljer: done");
            Assert.That(row.DoneAt, Is.EqualTo(occ.CompletedAt), "Detaljer: Worker B's done date");
            Assert.That(row.WorkerNames, Is.EqualTo(new List<string> { completerName }), "Detaljer: Udført af");
            Assert.That(row.SdkCaseId, Is.EqualTo(occ.CompletedCaseId), "Detaljer: opens Worker B's case");

            Assert.That(rapportCase!.Completed, Is.True, "Rapport: done");
            Assert.That(rapportCase.DoneAt, Is.EqualTo(occ.CompletedAt), "Rapport: Worker B's done date");
            Assert.That(rapportCase.WorkerNames, Is.EqualTo(new List<string> { completerName }), "Rapport: Udført af");
            Assert.That(rapportCase.SdkCaseId, Is.EqualTo(occ.CompletedCaseId), "Rapport: Worker B's answers");

            Assert.That(open.Select(r => r.ComplianceId), Does.Not.Contain(occ.ComplianceId),
                "Detaljer \"Ikke udførte\" does not offer a completed occurrence for filling in");
            Assert.That(await Oversigt(core, series.PropertyId), Is.EqualTo((1, 1)), "Oversigt (Total, Done)");
        });
    }

    /// <summary>
    /// LOCK: with no completed sibling the row stays "not done + removed" — deleted by a
    /// user — and is hidden everywhere, exactly as before.
    /// </summary>
    [Test]
    public async Task Report_NoCompletedSibling_RemovedRowStaysHidden()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Deleted, nobody completed it");
        var occ = await SeedTwoWorkerOccurrence(series, PastMonday(), siblingCompleted: false);

        Assert.Multiple(async () =>
        {
            Assert.That((await Detaljer(core, series.PropertyId, "all")).Select(r => r.ComplianceId),
                Does.Not.Contain(occ.ComplianceId), "Detaljer");
            Assert.That((await Rapport(core, series.PropertyId)).Select(c => c.ComplianceId),
                Does.Not.Contain(occ.ComplianceId), "Rapport");
            Assert.That(await Oversigt(core, series.PropertyId), Is.EqualTo((0, 0)), "Oversigt (Total, Done)");
        });
    }

    /// <summary>
    /// Deleting the log from Rapport deletes the log Rapport shows — Worker B's completed
    /// case — instead of failing as "already deleted" on the compliance's own case.
    /// </summary>
    [Test]
    public async Task Delete_CompletedBySecondWorker_DeletesTheCompletedLog()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Delete worker B's log");
        var occ = await SeedTwoWorkerOccurrence(series, PastMonday());

        var result = await BuildCompliancesService(core).Delete(occ.ComplianceId);
        Assert.That(result.Success, Is.True, result.Message);
        ClearTrackers();

        var completedCase = await MicrotingDbContext!.Cases.AsNoTracking().SingleAsync(x => x.Id == occ.CompletedCaseId);
        var completedPcs = await ItemsPlanningPnDbContext!.PlanningCaseSites.AsNoTracking()
            .SingleAsync(x => x.Id == occ.CompletedPlanningCaseSiteId);
        Assert.Multiple(async () =>
        {
            Assert.That(completedCase.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed),
                "Worker B's answers are deleted");
            Assert.That(completedPcs.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
            Assert.That((await Detaljer(core, series.PropertyId, "all")).Select(r => r.ComplianceId),
                Does.Not.Contain(occ.ComplianceId), "the deleted log is gone from Detaljer");
        });
    }

    // ==================================================================
    // Calendar
    // ==================================================================

    /// <summary>
    /// The calendar draws the occurrence once, completed by Worker B. <b>Fails on the old
    /// code</b>: the compliance was dropped and the rule re-emitted an uncompleted tile.
    /// </summary>
    [Test]
    public async Task Calendar_CompletedBySecondWorker_RendersOneCompletedTile()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Calendar worker B");
        var monday = PastMonday();
        var occ = await SeedTwoWorkerOccurrence(series, monday);
        var completerName = await SiteName(occ.CompleterSiteId);

        var tiles = await TilesOn(core, series.PropertyId, monday, actionableOnly: false);

        Assert.That(tiles, Has.Count.EqualTo(1), "one tile, no phantom recurrence tile beside it");
        Assert.Multiple(() =>
        {
            Assert.That(tiles[0].IsFromCompliance, Is.True);
            Assert.That(tiles[0].ComplianceId, Is.EqualTo(occ.ComplianceId));
            Assert.That(tiles[0].Completed, Is.True);
            Assert.That(tiles[0].DoneByName, Is.EqualTo(completerName));
            Assert.That(tiles[0].DoneAt, Is.EqualTo(occ.CompletedAt));
            Assert.That(tiles[0].SdkCaseId, Is.EqualTo(occ.CompletedCaseId));
        });
    }

    /// <summary>
    /// The mobile (ActionableOnly) list neither offers the completed occurrence nor
    /// re-emits it from the rule.
    /// </summary>
    [Test]
    public async Task Calendar_ActionableOnly_CompletedBySecondWorker_NoPhantomTask()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Mobile worker B");
        var monday = NextMonday();
        await SeedTwoWorkerOccurrence(series, monday);

        Assert.That(await TilesOn(core, series.PropertyId, monday, actionableOnly: true), Is.Empty);
    }

    // ==================================================================
    // Completed-period backstops (#960 calendar, #1332 report projection)
    // ==================================================================

    /// <summary>
    /// A monthly "1st Thursday" rule whose occurrence in month M was completed by
    /// Worker B on a compliance dated in a DIFFERENT week of M (the shape a re-anchored
    /// rule leaves). Month M is completed, so the calendar draws no rule tile on its 1st
    /// Thursday — the per-date dedup cannot see it, only the period backstop can. Month
    /// M+1 still renders, proving the rule emits at all.
    /// </summary>
    [Test]
    public async Task Calendar_MonthlyRule_PeriodCompletedBySecondWorker_NoPhantomTile()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Monthly worker B", monthlyFirstThursday: true);
        var month = DateTime.UtcNow.Date.AddMonths(2);
        var ruleDate = FirstThursdayOf(month);
        await SeedTwoWorkerOccurrence(series, ruleDate.AddDays(15));
        var nextRuleDate = FirstThursdayOf(month.AddMonths(1));

        Assert.Multiple(async () =>
        {
            Assert.That(await TilesOn(core, series.PropertyId, MondayOf(ruleDate), ruleDate, actionableOnly: false),
                Is.Empty, "the month Worker B completed renders no further rule tile");
            Assert.That(await TilesOn(core, series.PropertyId, MondayOf(nextRuleDate), nextRuleDate, actionableOnly: false),
                Has.Count.EqualTo(1), "the next month still renders the rule");
        });
    }

    /// <summary>
    /// Same shape in the compliance report: the #1332 projection plans no row on the
    /// completed month's 1st Thursday, and still plans the next month's.
    /// </summary>
    [Test]
    public async Task Report_MonthlyRule_PeriodCompletedBySecondWorker_NoPlannedRow()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Monthly report worker B", monthlyFirstThursday: true);
        var month = DateTime.UtcNow.Date.AddMonths(2);
        var ruleDate = FirstThursdayOf(month);
        var occ = await SeedTwoWorkerOccurrence(series, ruleDate.AddDays(15));
        var nextRuleDate = FirstThursdayOf(month.AddMonths(1));

        var rows = await Detaljer(core, series.PropertyId, "all",
            from: ruleDate.AddDays(-7), to: nextRuleDate.AddDays(7), includeProjected: true);
        var datesOfSeries = rows.Where(r => r.PlanningId == series.PlanningId).Select(r => r.TaskDate).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(datesOfSeries, Does.Not.Contain(Key(ruleDate)),
                "no planned row in the month Worker B completed");
            Assert.That(datesOfSeries, Does.Contain(Key(nextRuleDate)), "the next month is still planned");
            Assert.That(rows.Single(r => r.ComplianceId == occ.ComplianceId).Completed, Is.True);
        });
    }

    // ==================================================================
    // Data repair (dry run → run?planHash=)
    // ==================================================================

    [Test]
    public async Task Repair_DryRun_ListsTheRepoint_AndWritesNothing()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Repair dry run");
        var occ = await SeedTwoWorkerOccurrence(series, PastMonday());
        var untouched = await SeedTwoWorkerOccurrence(series, PastMonday().AddDays(-7), siblingCompleted: false);

        var result = await BuildRepairService(core).DryRunAsync();
        Assert.That(result.Success, Is.True, result.Message);
        ClearTrackers();

        var repoint = result.Model.Repoints.Single();
        Assert.Multiple(async () =>
        {
            Assert.That(repoint.ComplianceId, Is.EqualTo(occ.ComplianceId));
            Assert.That(repoint.OldSdkCaseId, Is.EqualTo(occ.OwnCaseId));
            Assert.That(repoint.NewSdkCaseId, Is.EqualTo(occ.CompletedCaseId));
            Assert.That(result.Model.PlanHash, Is.Not.Empty);
            Assert.That((await StoredCompliance(occ.ComplianceId)).MicrotingSdkCaseId, Is.EqualTo(occ.OwnCaseId),
                "the dry run writes nothing");
            Assert.That(result.Model.Repoints.Select(r => r.ComplianceId), Does.Not.Contain(untouched.ComplianceId),
                "a removed row with no completed sibling is not repointed");
        });
    }

    [TestCase("", TestName = "Repair_Run_WithoutPlanHash_IsRefused")]
    [TestCase("0000", TestName = "Repair_Run_WithAStalePlanHash_IsRefused")]
    public async Task Repair_Run_WithoutTheReviewedPlanHash_IsRefused(string planHash)
    {
        var core = await SharedCore();
        var series = await SeedSeries("Repair refused");
        var occ = await SeedTwoWorkerOccurrence(series, PastMonday());

        var result = await BuildRepairService(core).RunAsync(planHash);
        ClearTrackers();

        Assert.That(result.Success, Is.False);
        Assert.That((await StoredCompliance(occ.ComplianceId)).MicrotingSdkCaseId, Is.EqualTo(occ.OwnCaseId));
    }

    [Test]
    public async Task Repair_Run_WithThePlanHash_RepointsTheCompliance_AndASecondRunHasNothingToDo()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Repair run");
        var occ = await SeedTwoWorkerOccurrence(series, PastMonday());
        var service = BuildRepairService(core);

        var dryRun = await service.DryRunAsync();
        var run = await service.RunAsync(dryRun.Model.PlanHash);
        ClearTrackers();

        Assert.That(run.Success, Is.True, run.Message);
        Assert.That(run.Model.Repointed, Is.EqualTo(1));
        var stored = await StoredCompliance(occ.ComplianceId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.MicrotingSdkCaseId, Is.EqualTo(occ.CompletedCaseId));
            Assert.That(stored.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed), "only the case id changes");
        });

        var secondDryRun = await service.DryRunAsync();
        Assert.That(secondDryRun.Model.Repoints, Is.Empty, "a repointed compliance drops out of the plan");
        var secondRun = await service.RunAsync(secondDryRun.Model.PlanHash);
        Assert.That(secondRun.Success, Is.False, "an empty plan is refused");
    }

    /// <summary>
    /// The compliance's OWN case is completed — later than a sibling's. It is an ordinary
    /// completed log and keeps its case: the helper never repoints a completed case.
    /// </summary>
    [Test]
    public async Task Repair_OwnCaseCompleted_SiblingCompletedEarlier_IsNotRepointed()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Own case completed");
        var occ = await SeedTwoWorkerOccurrence(series, PastMonday());
        var ownCase = await MicrotingDbContext!.Cases.SingleAsync(c => c.Id == occ.OwnCaseId);
        ownCase.Status = CompletedStatus;
        ownCase.DoneAt = occ.CompletedAt.AddHours(3);
        ownCase.WorkflowState = Constants.WorkflowStates.Created;
        await MicrotingDbContext.SaveChangesAsync();
        ClearTrackers();

        var plan = await BuildRepairService(core).DryRunAsync();

        Assert.That(plan.Success, Is.True, plan.Message);
        Assert.That(plan.Model.Repoints.Select(r => r.ComplianceId), Does.Not.Contain(occ.ComplianceId));
    }

    /// <summary>Two workers completed the same occurrence: the earliest completion wins.</summary>
    [Test]
    public async Task Repair_TwoCompletedSiblings_TheEarliestCompletionWins()
    {
        var core = await SharedCore();
        var series = await SeedSeries("Two completers");
        var occ = await SeedTwoWorkerOccurrence(series, PastMonday());
        var checkListId = (await StoredCompliance(occ.ComplianceId)).MicrotingSdkeFormId;
        var earlierCompleter = await SeedSdkSite("worker-c");
        var (earlierCaseId, _) = await SeedWorkerCase(series, occ.PlanningCaseId, earlierCompleter, checkListId,
            CompletedStatus, occ.CompletedAt.AddHours(-2));
        ClearTrackers();

        var plan = await BuildRepairService(core).DryRunAsync();

        Assert.That(plan.Model.Repoints.Single(r => r.ComplianceId == occ.ComplianceId).NewSdkCaseId,
            Is.EqualTo(earlierCaseId));
        Assert.That((await Detaljer(core, series.PropertyId, "all")).Single(r => r.ComplianceId == occ.ComplianceId).WorkerNames,
            Is.EqualTo(new List<string> { await SiteName(earlierCompleter) }), "the report agrees with the repair");
    }
}
