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
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
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
using BcPlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

/// <summary>
/// #1332 — Detaljer lists the planned occurrences after today that no Compliance row
/// backs yet ("Planlagt"), so a long period such as "År til dato + 1 år" no longer stops
/// at the mobile watch window.
///
/// <para>
/// The occurrence rule is the calendar WEEK VIEW's (<c>GetWeekViewOccurrences</c>). The
/// seeded series is a weekly Monday rule (<c>RepeatWeekdaysCsv "1"</c>) started weeks
/// ago, so every expected date below is simply "a Monday", counted independently of the
/// enumerator under test. Projection starts TOMORROW (UTC): today's occurrence is never
/// projected, whatever weekday today is.
/// </para>
///
/// <para>
/// Every request opts in with <c>IncludeProjected</c> unless the test is about the flag
/// itself — Detaljer and its export send it; every other caller keeps the deployed-rows
/// contract (pinned by <c>ComplianceReportIndexTests</c>).
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ComplianceReportProjectionTests : TestBaseSetup
{
    /// <summary>
    /// "Today" for the current test, read once in <see cref="CleanTables"/> rather than at
    /// fixture load, so a fixture that runs across midnight does not compare against a
    /// stale day. (The service reads the clock itself; a test straddling midnight can still
    /// disagree with it by a day, as every clock-based test in this project can.)
    /// </summary>
    private DateTime Today { get; set; }

    private int _uidCounter = 960_000;

    [SetUp]
    public async Task CleanTables()
    {
        Today = DateTime.UtcNow.Date;

        await BackendConfigurationPnDbContext!.Database
            .ExecuteSqlRawAsync("DELETE FROM `AreaRulePlanningWorkerTags`;");

        BackendConfigurationPnDbContext.CalendarOccurrenceExceptionSites.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptionSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.CalendarOccurrenceExceptions.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptions);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.PlanningSites.RemoveRange(
            BackendConfigurationPnDbContext.PlanningSites);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.AreaRulePlanningTags.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlanningTags);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.Compliances.RemoveRange(
            BackendConfigurationPnDbContext.Compliances);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.CalendarConfigurations.RemoveRange(
            BackendConfigurationPnDbContext.CalendarConfigurations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.CalendarBoards.RemoveRange(
            BackendConfigurationPnDbContext.CalendarBoards);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.AreaRulePlannings.RemoveRange(
            BackendConfigurationPnDbContext.AreaRulePlannings);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.AreaRuleTranslations.RemoveRange(
            BackendConfigurationPnDbContext.AreaRuleTranslations);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.AreaRules.RemoveRange(
            BackendConfigurationPnDbContext.AreaRules);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.PropertyWorkers.RemoveRange(
            BackendConfigurationPnDbContext.PropertyWorkers);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        BackendConfigurationPnDbContext.Areas.RemoveRange(BackendConfigurationPnDbContext.Areas);
        BackendConfigurationPnDbContext.Properties.RemoveRange(BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext!.Plannings.RemoveRange(ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        ItemsPlanningPnDbContext.PlanningTags.RemoveRange(ItemsPlanningPnDbContext.PlanningTags);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        MicrotingDbContext!.Cases.RemoveRange(MicrotingDbContext.Cases);
        await MicrotingDbContext.SaveChangesAsync();
    }

    /// <param name="utcNow">
    /// The service's clock. Defaults to noon UTC of <see cref="Today"/>, which is the same
    /// date in Copenhagen, so the projection starts the day after <see cref="Today"/> in
    /// every test that does not pin its own instant.
    /// </param>
    private BackendConfigurationComplianceReportService BuildService(Core core, DateTime? utcNow = null)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(
            new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        return new BackendConfigurationComplianceReportService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, ItemsPlanningPnDbContext!,
            TestContextLogger<BackendConfigurationComplianceReportService>.Instance,
            new WorkerTagMembershipService(coreHelper, BackendConfigurationPnDbContext))
        {
            UtcNow = () => utcNow ?? DateTime.SpecifyKind(Today.AddHours(12), DateTimeKind.Utc)
        };
    }

    // ------------------------------------------------------------------
    // Seeding
    // ------------------------------------------------------------------

    private sealed record Series(int ArpId, int PropertyId, int PlanningId, int AreaId, int AreaRuleId);

    /// <summary>The Monday of the week <paramref name="weeksAgo"/> weeks before today's.</summary>
    private DateTime MondayWeeksAgo(int weeksAgo) =>
        Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7)).AddDays(-7 * weeksAgo);

    /// <summary>Every date in [from, to] that falls on <paramref name="day"/>.</summary>
    private static List<DateTime> DaysOf(DayOfWeek day, DateTime from, DateTime to)
    {
        var result = new List<DateTime>();
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
        {
            if (d.DayOfWeek == day) result.Add(d);
        }
        return result;
    }

    /// <summary>The first Monday strictly after <paramref name="after"/>.</summary>
    private static DateTime NextMondayAfter(DateTime after) =>
        DaysOf(DayOfWeek.Monday, after.AddDays(1), after.AddDays(7)).First();

    private async Task<(int AreaId, int PropertyId)> SeedAreaAndProperty()
    {
        var area = new Area
        {
            Type = AreaTypesEnum.Type1, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Areas.AddAsync(area);
        var property = new Property
        {
            Name = $"Projection Property {Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return (area.Id, property.Id);
    }

    /// <summary>
    /// A weekly Monday series (or a daily one with <paramref name="daily"/>) that started
    /// eight weeks ago and is open-ended unless <paramref name="repeatUntil"/> is given.
    /// </summary>
    private async Task<Series> SeedSeries(
        string title, bool daily = false, bool complianceEnabled = true, bool active = true,
        DateTime? repeatUntil = null, int? propertyId = null, DateTime? start = null)
    {
        var (areaId, seededPropertyId) = await SeedAreaAndProperty();
        propertyId ??= seededPropertyId;
        var startDate = DateTime.SpecifyKind(start ?? MondayWeeksAgo(8), DateTimeKind.Utc);

        var areaRule = new AreaRule
        {
            AreaId = areaId, PropertyId = propertyId.Value, EformId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = title,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = daily ? RepeatType.Day : RepeatType.Week,
            StartDate = startDate, DayOfWeek = DayOfWeek.Monday, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = propertyId.Value, AreaId = areaId,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = active,
            RepeatType = daily ? 1 : 2, RepeatEvery = 1,
            RepeatWeekdaysCsv = daily ? null : "1", DayOfWeek = 1,
            RepeatEndMode = repeatUntil.HasValue ? 2 : 0,
            RepeatUntilDate = repeatUntil.HasValue
                ? DateTime.SpecifyKind(repeatUntil.Value, DateTimeKind.Utc)
                : null,
            ComplianceEnabled = complianceEnabled,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Series(arp.Id, propertyId.Value, planning.Id, areaId, areaRule.Id);
    }

    private async Task<int> SeedCompliance(
        Series series, DateTime deadline, int sdkCaseId = 0, bool removed = false)
    {
        var compliance = new Compliance
        {
            ItemName = "Fallback Item Name",
            PlanningId = series.PlanningId, PropertyId = series.PropertyId, AreaId = series.AreaId,
            Deadline = DateTime.SpecifyKind(deadline, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCaseId, MicrotingSdkeFormId = 0,
            WorkflowState = removed ? Constants.WorkflowStates.Removed : Constants.WorkflowStates.Created,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance.Id;
    }

    private async Task<int> SeedSdkSite()
    {
        var uid = ++_uidCounter;
        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var site = new Site
        {
            Name = $"Jane Doe {uid}", MicrotingUid = uid, LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site.Id;
    }

    private async Task<int> SeedSdkCase(int status)
    {
        var sdkCase = new Case
        {
            SiteId = await SeedSdkSite(), Status = status,
            DoneAt = status == 100 ? DateTime.UtcNow : null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkCase.Id;
    }

    private async Task<int> SeedBoard(int propertyId, string name)
    {
        var board = new CalendarBoard
        {
            Name = name, Color = "#112233", PropertyId = propertyId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarBoards.AddAsync(board);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return board.Id;
    }

    private async Task SeedCalendarConfig(int arpId, int? boardId)
    {
        await BackendConfigurationPnDbContext!.CalendarConfigurations.AddAsync(new CalendarConfiguration
        {
            AreaRulePlanningId = arpId, BoardId = boardId, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private async Task SeedException(
        int arpId, DateTime originalDate, bool isDeleted = false, DateTime? newDate = null)
    {
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = DateTime.SpecifyKind(originalDate.Date, DateTimeKind.Utc),
            IsDeleted = isDeleted,
            NewDate = newDate.HasValue ? DateTime.SpecifyKind(newDate.Value.Date, DateTimeKind.Utc) : null,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private async Task<int> SeedPlanningTag(string name)
    {
        var tag = new PlanningTag
        {
            Name = name,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(tag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        return tag.Id;
    }

    private async Task TagSeries(Series series, int tagId)
    {
        await BackendConfigurationPnDbContext!.AreaRulePlanningTags.AddAsync(new AreaRulePlanningTag
        {
            AreaRulePlanningId = series.ArpId, ItemPlanningTagId = tagId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private async Task AssignSite(Series series, int siteId)
    {
        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(new BcPlanningSite
        {
            AreaRulePlanningsId = series.ArpId, SiteId = siteId,
            AreaId = series.AreaId, AreaRuleId = series.AreaRuleId, Status = 33,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Team assignment (#1232/#1256): the series is assigned to an SDK worker tag, the
    /// site is a live member of it and is linked to the series' property. No
    /// PlanningSites row.
    /// </summary>
    private async Task AssignTeamWithMember(Series series, int memberSiteId)
    {
        var tag = new Tag { Name = $"Team {Guid.NewGuid()}", WorkflowState = Constants.WorkflowStates.Created };
        await MicrotingDbContext!.Tags.AddAsync(tag);
        await MicrotingDbContext.SaveChangesAsync();
        await MicrotingDbContext.SiteTags.AddAsync(new SiteTag
        {
            TagId = tag.Id, SiteId = memberSiteId, WorkflowState = Constants.WorkflowStates.Created
        });
        await MicrotingDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(new PropertyWorker
        {
            PropertyId = series.PropertyId, WorkerId = memberSiteId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.AreaRulePlanningWorkerTags.AddAsync(new AreaRulePlanningWorkerTag
        {
            AreaRulePlanningId = series.ArpId, TagId = tag.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    // ------------------------------------------------------------------
    // Requests
    // ------------------------------------------------------------------

    private static ComplianceReportRequestModel Request(
        DateTime from, DateTime to, string status = "all", bool includeProjected = true,
        int? propertyId = null, List<int>? boardIds = null, List<int>? tagIds = null,
        List<int>? siteIds = null, int pageIndex = 0, int pageSize = 0, bool isSortDsc = false)
        => new()
        {
            DateFrom = from, DateTo = to, Status = status, IncludeProjected = includeProjected,
            PropertyId = propertyId, BoardIds = boardIds ?? [], TagIds = tagIds ?? [],
            SiteIds = siteIds ?? [], PageIndex = pageIndex, PageSize = pageSize,
            IsSortDsc = isSortDsc
        };

    private async Task<ComplianceReportPagedModel> Index(Core core, ComplianceReportRequestModel request)
    {
        var result = await BuildService(core).Index(request);
        Assert.That(result.Success, Is.True, result.Message);
        return result.Model!;
    }

    private static List<DateTime> Dates(IEnumerable<ComplianceReportRowModel> rows) =>
        rows.Select(r => DateTime.ParseExact(r.TaskDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();

    private static List<DateTime> PlannedDates(ComplianceReportPagedModel model) =>
        Dates(model.Entities.Where(r => r.IsProjected)).OrderBy(d => d).ToList();

    // ==================================================================
    // The reported defect: a year ahead, one row per weekly occurrence
    // ==================================================================

    /// <summary>
    /// A weekly open-ended task with no future Compliance rows, period today → today + 1
    /// year: one row per Monday after today, all planned, and Total counts them.
    /// </summary>
    [Test]
    public async Task Index_WeeklyOpenEndedSeries_ProjectsEveryMondayOfTheComingYear()
    {
        var core = await GetCore();
        var series = await SeedSeries("Task A");
        var to = Today.AddYears(1);
        var expected = DaysOf(DayOfWeek.Monday, Today.AddDays(1), to);

        var model = await Index(core, Request(Today, to));

        Assert.That(expected.Count, Is.InRange(52, 53));
        Assert.That(model.Total, Is.EqualTo(expected.Count));
        Assert.That(PlannedDates(model), Is.EqualTo(expected));
        Assert.That(model.Entities, Has.All.Matches<ComplianceReportRowModel>(r =>
            r!.IsProjected && r.ComplianceId == 0 && r.SdkCaseId == 0 && !r.Completed
            && r.DoneAt == null && r.CheckListId == null
            && r.PlanningId == series.PlanningId && r.AreaRulePlanningId == series.ArpId
            && r.PropertyId == series.PropertyId && r.Title == "Task A"));
    }

    /// <summary>Without the opt-in the report keeps its deployed-rows contract.</summary>
    [Test]
    public async Task Index_WithoutIncludeProjected_ReturnsNoPlannedRows()
    {
        var core = await GetCore();
        await SeedSeries("Task A");

        var model = await Index(core, Request(Today, Today.AddYears(1), includeProjected: false));

        Assert.That(model.Total, Is.EqualTo(0));
        Assert.That(model.Entities, Is.Empty);
    }

    /// <summary>
    /// Projection happens before the sort and the page: walking every page yields
    /// exactly the unpaged ("Vis alle") list, in the same order, and every page reports
    /// the same Total.
    /// </summary>
    [Test]
    public async Task Index_PagedAndUnpaged_Agree()
    {
        var core = await GetCore();
        var series = await SeedSeries("Task A");
        var deployedMonday = NextMondayAfter(Today.AddDays(14));
        await SeedCompliance(series, deployedMonday, await SeedSdkCase(status: 66));
        var to = Today.AddYears(1);

        var unpaged = await Index(core, Request(Today, to));

        var paged = new List<ComplianceReportRowModel>();
        for (var pageIndex = 0; ; pageIndex++)
        {
            var page = await Index(core, Request(Today, to, pageIndex: pageIndex, pageSize: 10));
            Assert.That(page.Total, Is.EqualTo(unpaged.Total));
            if (page.Entities.Count == 0) break;
            paged.AddRange(page.Entities);
        }

        Assert.That(paged.Select(r => (r.TaskDate, r.ComplianceId, r.IsProjected)),
            Is.EqualTo(unpaged.Entities.Select(r => (r.TaskDate, r.ComplianceId, r.IsProjected))));
        Assert.That(unpaged.Total, Is.EqualTo(DaysOf(DayOfWeek.Monday, Today.AddDays(1), to).Count));
    }

    // ==================================================================
    // Dedup, exceptions and the repeat end
    // ==================================================================

    /// <summary>
    /// A Monday with a Compliance row appears once, as that row (with its id, not
    /// planned). A Monday with a SOFT-REMOVED row — a deleted or retracted occurrence,
    /// which the deployer never re-deploys — appears not at all.
    /// </summary>
    [Test]
    public async Task Index_AnOccurrenceWithAComplianceRow_IsNotProjected()
    {
        var core = await GetCore();
        var series = await SeedSeries("Task A");
        var deployedMonday = NextMondayAfter(Today.AddDays(7));
        var retractedMonday = deployedMonday.AddDays(7);
        var complianceId = await SeedCompliance(series, deployedMonday, await SeedSdkCase(status: 66));
        await SeedCompliance(series, retractedMonday, sdkCaseId: 0, removed: true);

        var model = await Index(core, Request(Today, Today.AddYears(1)));

        var onDeployed = model.Entities.Where(r => r.TaskDate == deployedMonday.ToString("yyyy-MM-dd")).ToList();
        Assert.That(onDeployed, Has.Count.EqualTo(1));
        Assert.That(onDeployed[0].ComplianceId, Is.EqualTo(complianceId));
        Assert.That(onDeployed[0].IsProjected, Is.False);
        Assert.That(PlannedDates(model), Does.Not.Contain(retractedMonday));
        Assert.That(Dates(model.Entities), Does.Not.Contain(retractedMonday));
    }

    /// <summary>
    /// The week view's completed-period backstop: a single-weekday weekly rule whose
    /// week already holds a completed occurrence (dated on the Tuesday) renders no
    /// Monday in that week, so none is planned there either.
    /// </summary>
    [Test]
    public async Task Index_AWeekWithACompletedOccurrence_HasNoPlannedMonday()
    {
        var core = await GetCore();
        var series = await SeedSeries("Task A");
        var monday = NextMondayAfter(Today.AddDays(7));
        await SeedCompliance(series, monday.AddDays(1), await SeedSdkCase(status: 100), removed: true);

        var model = await Index(core, Request(Today, Today.AddYears(1)));

        Assert.That(PlannedDates(model), Does.Not.Contain(monday));
        Assert.That(PlannedDates(model), Does.Contain(monday.AddDays(7)));
    }

    /// <summary>
    /// A deleted occurrence is not planned; a moved one is planned on its NEW date, and
    /// not on its original Monday. An occurrence moved in from the PAST is planned on
    /// its new date too.
    /// </summary>
    [Test]
    public async Task Index_OccurrenceExceptions_DeleteAndMoveThePlannedRows()
    {
        var core = await GetCore();
        var series = await SeedSeries("Task A");
        var deletedMonday = NextMondayAfter(Today.AddDays(7));
        var movedMonday = deletedMonday.AddDays(14);
        var movedTo = movedMonday.AddDays(2);
        var pastMonday = MondayWeeksAgo(2);
        var movedInTo = movedMonday.AddDays(3);
        await SeedException(series.ArpId, deletedMonday, isDeleted: true);
        await SeedException(series.ArpId, movedMonday, newDate: movedTo);
        await SeedException(series.ArpId, pastMonday, newDate: movedInTo);

        var model = await Index(core, Request(Today, Today.AddYears(1)));
        var planned = PlannedDates(model);

        Assert.That(planned, Does.Not.Contain(deletedMonday));
        Assert.That(planned, Does.Not.Contain(movedMonday));
        Assert.That(planned, Does.Contain(movedTo));
        Assert.That(planned, Does.Contain(movedInTo));
        Assert.That(planned, Has.Count.EqualTo(DaysOf(DayOfWeek.Monday, Today.AddDays(1), Today.AddYears(1)).Count));
    }

    /// <summary>An "until date" inside the period ends the planned rows on that day.</summary>
    [Test]
    public async Task Index_RepeatUntilDateInsideThePeriod_EndsTheProjection()
    {
        var core = await GetCore();
        var until = Today.AddDays(60);
        await SeedSeries("Task A", repeatUntil: until);

        var model = await Index(core, Request(Today, Today.AddYears(1)));

        Assert.That(PlannedDates(model), Is.EqualTo(DaysOf(DayOfWeek.Monday, Today.AddDays(1), until)));
    }

    /// <summary>
    /// An anchor the rule does not produce (an exception on a Thursday of a Monday
    /// series, neither deleted nor moved — what a "this and following" move leaves
    /// behind) is planned on its own date, as the week view's orphan pass renders it.
    /// </summary>
    [Test]
    public async Task Index_OrphanAnchorWithoutMove_IsPlannedOnItsDate()
    {
        var core = await GetCore();
        var series = await SeedSeries("Task A");
        var thursday = NextMondayAfter(Today.AddDays(7)).AddDays(3);
        await SeedException(series.ArpId, thursday);

        var model = await Index(core, Request(Today, Today.AddYears(1)));

        Assert.That(PlannedDates(model), Does.Contain(thursday));
        Assert.That(model.Total, Is.EqualTo(DaysOf(DayOfWeek.Monday, Today.AddDays(1), Today.AddYears(1)).Count + 1));
    }

    /// <summary>An inactive task deploys nothing, so nothing is planned for it.</summary>
    [Test]
    public async Task Index_InactiveTask_IsNotProjected()
    {
        var core = await GetCore();
        await SeedSeries("Task A", active: false);

        var model = await Index(core, Request(Today, Today.AddYears(1)));

        Assert.That(model.Total, Is.EqualTo(0));
    }

    /// <summary>
    /// Today is never projected — the projection starts tomorrow — and a period that
    /// ends today (or in the past) projects nothing.
    /// </summary>
    [Test]
    public async Task Index_PeriodEndingToday_ProjectsNothing()
    {
        var core = await GetCore();
        await SeedSeries("Task A", daily: true);

        var model = await Index(core, Request(Today.AddDays(-30), Today));

        Assert.That(model.Entities.Where(r => r.IsProjected), Is.Empty);
    }

    // ==================================================================
    // Filters
    // ==================================================================

    [Test]
    public async Task Index_StatusFilter_PlannedRowsAreOpenNeverDone()
    {
        var core = await GetCore();
        await SeedSeries("Task A");
        var to = Today.AddYears(1);
        var expected = DaysOf(DayOfWeek.Monday, Today.AddDays(1), to).Count;

        Assert.That((await Index(core, Request(Today, to, status: "open"))).Total, Is.EqualTo(expected));
        Assert.That((await Index(core, Request(Today, to, status: "all"))).Total, Is.EqualTo(expected));
        Assert.That((await Index(core, Request(Today, to, status: "done"))).Total, Is.EqualTo(0));
    }

    [Test]
    public async Task Index_PropertyAndBoardFilters_ApplyToPlannedRows()
    {
        var core = await GetCore();
        var seriesA = await SeedSeries("Task A");
        var seriesB = await SeedSeries("Task B");
        var boardA = await SeedBoard(seriesA.PropertyId, "Board A");
        var boardB = await SeedBoard(seriesB.PropertyId, "Board B");
        await SeedCalendarConfig(seriesA.ArpId, boardA);
        await SeedCalendarConfig(seriesB.ArpId, boardB);
        var to = Today.AddYears(1);

        var byBoard = await Index(core, Request(Today, to, boardIds: [boardA]));
        var byProperty = await Index(core, Request(Today, to, propertyId: seriesB.PropertyId));

        Assert.That(byBoard.Total, Is.GreaterThan(0));
        Assert.That(byBoard.Entities, Has.All.Matches<ComplianceReportRowModel>(r =>
            r!.AreaRulePlanningId == seriesA.ArpId && r.BoardId == boardA && r.BoardName == "Board A"));
        Assert.That(byProperty.Total, Is.GreaterThan(0));
        Assert.That(byProperty.Entities, Has.All.Matches<ComplianceReportRowModel>(r =>
            r!.AreaRulePlanningId == seriesB.ArpId));
    }

    [Test]
    public async Task Index_TagFilter_AppliesToPlannedRows()
    {
        var core = await GetCore();
        var seriesA = await SeedSeries("Task A");
        await SeedSeries("Task B");
        var tagId = await SeedPlanningTag("Example Tag");
        await TagSeries(seriesA, tagId);

        var model = await Index(core, Request(Today, Today.AddYears(1), tagIds: [tagId]));

        Assert.That(model.Total, Is.GreaterThan(0));
        Assert.That(model.Entities, Has.All.Matches<ComplianceReportRowModel>(r =>
            r!.AreaRulePlanningId == seriesA.ArpId && r.Tags.Contains("Example Tag")));
    }

    /// <summary>
    /// The employee filter reaches planned rows both through an explicit assignment
    /// (PlanningSites) and through a team the employee is a live member of (#1232/#1256).
    /// </summary>
    [Test]
    public async Task Index_EmployeeFilter_AppliesToPlannedRows_IndividualAndTeam()
    {
        var core = await GetCore();
        var assigned = await SeedSeries("Task Individual");
        var teamAssigned = await SeedSeries("Task Team");
        await SeedSeries("Task Nobody");
        var individual = await SeedSdkSite();
        var member = await SeedSdkSite();
        await AssignSite(assigned, individual);
        await AssignTeamWithMember(teamAssigned, member);
        var to = Today.AddYears(1);

        var byIndividual = await Index(core, Request(Today, to, siteIds: [individual]));
        var byMember = await Index(core, Request(Today, to, siteIds: [member]));

        Assert.That(byIndividual.Total, Is.GreaterThan(0));
        Assert.That(byIndividual.Entities, Has.All.Matches<ComplianceReportRowModel>(r =>
            r!.AreaRulePlanningId == assigned.ArpId && r.WorkerSiteIds.Contains(individual)));
        Assert.That(byMember.Total, Is.GreaterThan(0));
        Assert.That(byMember.Entities, Has.All.Matches<ComplianceReportRowModel>(r =>
            r!.AreaRulePlanningId == teamAssigned.ArpId && r.TeamAssigneeIds.Contains(member)));
    }

    // ==================================================================
    // "Overskredet opgave vises ikke i app", Oversigt and Rapport
    // ==================================================================

    /// <summary>
    /// A task that hides missed occurrences (#1325) is projected like any other: planned
    /// rows are future, so they are never missed.
    /// </summary>
    [Test]
    public async Task Index_TaskHidingMissedOccurrences_IsProjected()
    {
        var core = await GetCore();
        await SeedSeries("Task A", complianceEnabled: false);
        var to = Today.AddYears(1);

        var model = await Index(core, Request(Today, to, status: "open"));

        Assert.That(PlannedDates(model), Is.EqualTo(DaysOf(DayOfWeek.Monday, Today.AddDays(1), to)));
    }

    /// <summary>
    /// Oversigt does not project: Total, overdue and the percentage count deployed rows
    /// only, exactly as before #1332.
    /// </summary>
    [Test]
    public async Task Overview_IsUnchangedByProjection()
    {
        var core = await GetCore();
        var series = await SeedSeries("Task A", complianceEnabled: false);
        var reported = await SeedSeries("Task B");
        await SeedCompliance(reported, MondayWeeksAgo(1), await SeedSdkCase(status: 66));
        await SeedCompliance(reported, NextMondayAfter(Today), await SeedSdkCase(status: 66));
        await SeedCompliance(series, NextMondayAfter(Today), await SeedSdkCase(status: 66));

        var result = await BuildService(core).Overview(new ComplianceReportOverviewRequestModel
        {
            DateFrom = MondayWeeksAgo(4), DateTo = Today.AddYears(1),
            BoardIds = [], TagIds = [], SiteIds = []
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.Totals.Total, Is.EqualTo(3));
        Assert.That(result.Model!.Totals.Overdue, Is.EqualTo(1));
        Assert.That(result.Model!.Totals.DueTotal, Is.EqualTo(1));
        Assert.That(result.Model!.Totals.CompliancePct, Is.EqualTo(0));
    }

    /// <summary>
    /// Rapport shows answers, and a planned occurrence has none: the flag is ignored and
    /// the report is empty for a series with no answered case.
    /// </summary>
    [Test]
    public async Task EformColumns_IsUnaffectedByProjection()
    {
        var core = await GetCore();
        await SeedSeries("Task A");

        var result = await BuildService(core).EformColumns(Request(Today, Today.AddYears(1)));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!, Is.Empty);
    }

    // ==================================================================
    // Limits
    // ==================================================================

    /// <summary>
    /// Planned rows count towards Total like any other row, but when the unpaged list
    /// exceeds <see cref="BackendConfigurationComplianceReportService.MaxRowsReturned"/>
    /// they are the ones dropped — the furthest-future first — so the deployed rows
    /// survive even under the default newest-first sort, where a plain cut would have
    /// kept only planned rows. Four daily tasks over four years (~5,800 rows) is well
    /// over the cap.
    /// </summary>
    [Test]
    public async Task Index_RowCap_DropsTheFurthestFuturePlannedRowsFirst()
    {
        var core = await GetCore();
        var allSeries = new List<Series>();
        foreach (var title in new[] { "Task A", "Task B", "Task C", "Task D" })
        {
            allSeries.Add(await SeedSeries(title, daily: true));
        }
        // Three deployed rows, the OLDEST in the window — the first a newest-first
        // cut would lose. Each replaces the planned row of its day.
        var deployedIds = new List<int>();
        for (var day = 1; day <= 3; day++)
        {
            deployedIds.Add(await SeedCompliance(allSeries[0], Today.AddDays(day), await SeedSdkCase(status: 66)));
        }
        var to = Today.AddYears(4);
        // Every day in [tomorrow, to], per series.
        var expectedTotal = 4 * (to - Today).Days;

        var model = await Index(core, Request(Today, to, isSortDsc: true));

        Assert.That(expectedTotal, Is.GreaterThan(BackendConfigurationComplianceReportService.MaxRowsReturned));
        Assert.That(model.Total, Is.EqualTo(expectedTotal));
        Assert.That(model.Entities, Has.Count.EqualTo(BackendConfigurationComplianceReportService.MaxRowsReturned));
        Assert.That(model.Entities.Where(r => !r.IsProjected).Select(r => r.ComplianceId),
            Is.EquivalentTo(deployedIds), "every deployed row survives the cap");

        // The planned rows kept are the NEAREST ones: nothing dropped is earlier than
        // anything kept.
        var keptPlanned = Dates(model.Entities.Where(r => r.IsProjected));
        var lastKept = keptPlanned.Max();
        Assert.That(lastKept, Is.LessThan(to), "the furthest-future planned rows were dropped");
        var keptPerDay = keptPlanned.GroupBy(d => d).ToDictionary(g => g.Key, g => g.Count());
        for (var day = Today.AddDays(4); day < lastKept; day = day.AddDays(1))
        {
            Assert.That(keptPerDay.GetValueOrDefault(day), Is.EqualTo(4),
                $"every planned row before {lastKept:yyyy-MM-dd} is kept ({day:yyyy-MM-dd})");
        }
        // Newest-first order is preserved in what is returned.
        Assert.That(Dates(model.Entities), Is.Ordered.Descending);
    }

    /// <summary>
    /// The projection starts the day after the COPENHAGEN date, the product's future-task
    /// boundary. In summer (CEST, UTC+2) 21:30 UTC on 15 July is still the 15th in
    /// Copenhagen, so the 16th is the first planned day; 22:30 UTC is already 00:30 on the
    /// 16th there — the local today, whose occurrence the scheduler owns — so the 17th is.
    /// A UTC "tomorrow" would have planned the 16th in both cases.
    /// </summary>
    [TestCase(21, 16)]
    [TestCase(22, 17)]
    [TestCase(23, 17)]
    public async Task Index_ProjectionStartsAfterTheCopenhagenToday(int utcHour, int firstPlannedDay)
    {
        var core = await GetCore();
        await SeedSeries("Task A", daily: true, start: new DateTime(2026, 1, 5));
        var utcNow = new DateTime(2026, 7, 15, utcHour, 30, 0, DateTimeKind.Utc);

        var result = await BuildService(core, utcNow).Index(
            Request(new DateTime(2026, 7, 10), new DateTime(2026, 7, 31)));

        Assert.That(result.Success, Is.True, result.Message);
        var planned = Dates(result.Model!.Entities.Where(r => r.IsProjected)).OrderBy(d => d).ToList();
        Assert.That(planned.First(), Is.EqualTo(new DateTime(2026, 7, firstPlannedDay)));
        Assert.That(planned.Last(), Is.EqualTo(new DateTime(2026, 7, 31)));
        Assert.That(planned, Has.Count.EqualTo(31 - firstPlannedDay + 1));
    }

    /// <summary>
    /// An open-ended period ending on the last day of the calendar must not overflow the
    /// end-of-day boundary: Detaljer answers, and the projection stops at the horizon.
    /// </summary>
    [Test]
    public async Task Index_PeriodEndingOnDateTimeMaxValue_Succeeds()
    {
        var core = await GetCore();
        await SeedSeries("Task A");
        var horizon = Today.AddYears(BackendConfigurationComplianceReportService.MaxProjectionYears);

        var model = await Index(core, Request(Today, DateTime.MaxValue.Date));

        Assert.That(PlannedDates(model), Is.EqualTo(DaysOf(DayOfWeek.Monday, Today.AddDays(1), horizon)));
    }

    /// <summary>
    /// Oversigt and Rapport share the window normalisation, so the same period does not
    /// fail them either.
    /// </summary>
    [Test]
    public async Task OverviewAndEformColumns_PeriodEndingOnDateTimeMaxValue_Succeed()
    {
        var core = await GetCore();
        await SeedSeries("Task A");
        var service = BuildService(core);

        var overview = await service.Overview(new ComplianceReportOverviewRequestModel
        {
            DateFrom = Today, DateTo = DateTime.MaxValue.Date, BoardIds = [], TagIds = [], SiteIds = []
        });
        var report = await service.EformColumns(Request(Today, DateTime.MaxValue.Date));

        Assert.That(overview.Success, Is.True, overview.Message);
        Assert.That(report.Success, Is.True, report.Message);
    }

    /// <summary>A period reaching past the projection horizon is projected up to it only.</summary>
    [Test]
    public async Task Index_PeriodBeyondTheHorizon_IsProjectedUpToTheHorizon()
    {
        var core = await GetCore();
        await SeedSeries("Task A");
        var horizon = Today.AddYears(BackendConfigurationComplianceReportService.MaxProjectionYears);

        var model = await Index(core, Request(Today, Today.AddYears(20)));

        Assert.That(PlannedDates(model), Is.EqualTo(DaysOf(DayOfWeek.Monday, Today.AddDays(1), horizon)));
    }
}
