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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using NSubstitute;

/// <summary>
/// DB-backed integration coverage for the NEW paged/sorted/multi-select endpoint
/// <see cref="BackendConfigurationComplianceReportService.Index"/>
/// (<c>POST api/backend-configuration-pn/compliance-report/index</c>) — issue #1161 §9.
///
/// <para>
/// The eleven pre-existing tests in <c>CalendarComplianceReportTests</c> still pin the
/// SHARED semantics (date window, the soft-removed rule, PropertyId/TagIds/SiteIds/BoardIds
/// single-value filtering, occurrence exceptions, taskDate-descending order) through the
/// calendar service's unpaged delegate. This fixture deliberately does NOT re-cover those;
/// it covers only what #1161 added:
/// </para>
/// <list type="bullet">
///   <item>paging (<c>PageIndex</c>/<c>PageSize</c>, the unpaged <c>PageSize &lt;= 0</c> path)</item>
///   <item><c>Total</c> being computed at the END of phase C — after the exception delete,
///         the NewDate range re-check, the board filter and the status filter</item>
///   <item>the six sort keys in both directions, their fallbacks, and sort-before-page</item>
///   <item>multi-select filtering that must not fan a row out into duplicates
///         (the <c>EXISTS</c> push-down for TagIds/SiteIds)</item>
///   <item>the new <c>CheckListId</c> row field, which comes from the SDK case and NOT from
///         <c>AreaRule.EformId</c> (#1160 finding 1)</item>
/// </list>
///
/// <para>
/// Each <c>TestBaseSetup</c> subclass owns its own MariaDB testcontainer, so this fixture
/// carries its own copies of the seeding helpers rather than sharing
/// <c>CalendarComplianceReportTests</c>'. Two of them differ from the originals:
/// <c>SeedSdkCase</c> takes a <c>checkListId</c> (the field under test), and
/// <c>SeedSeries</c> takes an <c>eformId</c> (the originals hardcode 0, and the
/// CheckListId tests need a differing value and a null).
/// </para>
///
/// <para>
/// Seeding invariants worth restating, because getting them wrong makes a test that passes
/// for the wrong reason: a row is DONE iff its backing SDK <c>Case.Status == 100</c>; a
/// soft-removed row that is NOT done is a user-deleted occurrence and is never returned for
/// any status; the default request <c>Status</c> is <c>"open"</c>, so any test seeding
/// completed rows must set it explicitly.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ComplianceReportIndexTests : TestBaseSetup
{
    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Monotonically-increasing counter for SDK Site MicrotingUid uniqueness across the whole
    // fixture's lifetime (the container/database is reused across every [Test] method).
    private int _uidCounter = 940_000;

    [SetUp]
    public async Task CleanCalendarTables()
    {
        // FK-safe cleanup (children before parents) so each test starts fresh. Because every
        // Compliance row in the database is one this fixture seeded, Total can be asserted as
        // an absolute number rather than filtered down to "our" rows.
        BackendConfigurationPnDbContext!.CalendarOccurrenceExceptionSites.RemoveRange(
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

        BackendConfigurationPnDbContext.Areas.RemoveRange(
            BackendConfigurationPnDbContext.Areas);
        BackendConfigurationPnDbContext.Properties.RemoveRange(
            BackendConfigurationPnDbContext.Properties);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext!.Plannings.RemoveRange(
            ItemsPlanningPnDbContext.Plannings);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        ItemsPlanningPnDbContext.PlanningTags.RemoveRange(
            ItemsPlanningPnDbContext.PlanningTags);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        MicrotingDbContext!.Cases.RemoveRange(MicrotingDbContext.Cases);
        await MicrotingDbContext.SaveChangesAsync();
    }

    /// <summary>
    /// The REAL service under test — mirrors how <c>CalendarComplianceReportTests.BuildService</c>
    /// constructs the same instance for the calendar delegate.
    /// </summary>
    private BackendConfigurationComplianceReportService BuildService(Core core)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(
            new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));

        // Completion is read off the backing SDK Case, so the core must be the real one.
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        return new BackendConfigurationComplianceReportService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, ItemsPlanningPnDbContext!,
            NullLogger<BackendConfigurationComplianceReportService>.Instance);
    }

    // ------------------------------------------------------------------
    // Seeding helpers
    // ------------------------------------------------------------------

    /// <summary>Seeds an SDK Site and returns its Id.</summary>
    private async Task<int> SeedSdkSite(string name)
    {
        var uid = ++_uidCounter;
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var sdkSite = new Site
        {
            Name = $"{name}-{uid}",
            MicrotingUid = uid,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkSite.Id;
    }

    /// <summary>
    /// Seeds a bare SDK CheckList so that <c>Case.CheckListId</c> satisfies its FK without
    /// paying for a full TemplateCreate. Returns the CheckList Id.
    /// </summary>
    private async Task<int> SeedCheckList(string label)
    {
        var checkList = new CheckList
        {
            Label = $"{label}-{Guid.NewGuid()}",
            ParentId = null,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.CheckLists.AddAsync(checkList);
        await MicrotingDbContext.SaveChangesAsync();
        return checkList.Id;
    }

    /// <summary>
    /// Seeds an SDK Case with the given completion status (100 = completed, &lt;100 = open)
    /// and optional DoneAt. Returns the Case Id.
    /// <para>
    /// Differs from <c>CalendarComplianceReportTests.SeedSdkCase</c> by the
    /// <paramref name="checkListId"/> parameter — the row's new <c>CheckListId</c> field is
    /// read straight off this column, so it must be settable per case.
    /// </para>
    /// </summary>
    private async Task<int> SeedSdkCase(
        int status, DateTime? doneAt = null, int? siteId = null, int? checkListId = null)
    {
        siteId ??= await SeedSdkSite("compliance-report-index-site");

        var sdkCase = new Case
        {
            SiteId = siteId,
            Status = status,
            DoneAt = doneAt,
            CheckListId = checkListId,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkCase.Id;
    }

    /// <summary>Seeds an Area + Property pair. Returns (areaId, propertyId).</summary>
    private async Task<(int AreaId, int PropertyId)> SeedAreaAndProperty(string propertyName)
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
            Name = $"{propertyName}-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (area.Id, property.Id);
    }

    /// <summary>
    /// Seeds Area→Property→AreaRule(+translation "title")→Planning→AreaRulePlanning
    /// (NO CalendarConfiguration — callers add one via <see cref="SeedCalendarConfig"/>).
    /// Returns (arpId, propertyId, planningId, areaId, areaRuleId).
    /// <para>
    /// Differs from <c>CalendarComplianceReportTests.SeedSeries</c> by the
    /// <paramref name="eformId"/> parameter: the original hardcodes
    /// <c>AreaRule.EformId = 0</c>, and the CheckListId tests need it set to a value that
    /// DIFFERS from the case's CheckListId, and to <c>null</c>.
    /// </para>
    /// </summary>
    private async Task<(int ArpId, int PropertyId, int PlanningId, int AreaId, int AreaRuleId)> SeedSeries(
        string propertyName, string title, DateTime startDate, int? eformId = 0)
    {
        var (areaId, propertyId) = await SeedAreaAndProperty(propertyName);

        var areaRule = new AreaRule
        {
            AreaId = areaId, PropertyId = propertyId, EformId = eformId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRuleTranslation = new AreaRuleTranslation
        {
            AreaRuleId = areaRule.Id, LanguageId = 1, Name = title,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRuleTranslations.AddAsync(areaRuleTranslation);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = 1, RepeatType = RepeatType.Week,
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
            DayOfWeek = DayOfWeek.Monday, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = propertyId, AreaId = areaId,
            ItemPlanningId = planning.Id,
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc), Status = true,
            RepeatType = 2, RepeatEvery = 1, RepeatWeekdaysCsv = "1", DayOfWeek = 1,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (arp.Id, propertyId, planning.Id, areaId, areaRule.Id);
    }

    private async Task<int> SeedCalendarConfig(
        int arpId, int? boardId = null, double startHour = 9.0, double duration = 1.0)
    {
        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arpId, BoardId = boardId, StartHour = startHour, Duration = duration,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarConfigurations.AddAsync(calConfig);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return calConfig.Id;
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

    private async Task<int> SeedCompliance(
        int planningId, int propertyId, int areaId, DateTime deadline, int sdkCaseId,
        string itemName = "Fallback Item Name", bool removed = false)
    {
        var compliance = new Compliance
        {
            ItemName = itemName,
            PlanningId = planningId,
            PropertyId = propertyId,
            AreaId = areaId,
            Deadline = DateTime.SpecifyKind(deadline, DateTimeKind.Utc),
            StartDate = DateTime.SpecifyKind(deadline.AddDays(-7), DateTimeKind.Utc),
            MicrotingSdkCaseId = sdkCaseId,
            MicrotingSdkeFormId = 0,
            WorkflowState = removed ? Constants.WorkflowStates.Removed : Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return compliance.Id;
    }

    private async Task<int> SeedTag(string name)
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

    private async Task SeedArpTag(int arpId, int tagId)
    {
        var arpTag = new AreaRulePlanningTag
        {
            AreaRulePlanningId = arpId, ItemPlanningTagId = tagId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.AreaRulePlanningTags.AddAsync(arpTag);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private async Task SeedPlanningSite(int arpId, int siteId, int areaId, int areaRuleId)
    {
        var planningSite = new Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite
        {
            AreaRulePlanningsId = arpId, SiteId = siteId,
            AreaId = areaId, AreaRuleId = areaRuleId, Status = 33,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.PlanningSites.AddAsync(planningSite);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private async Task<int> SeedException(
        int arpId, DateTime originalDate, bool isDeleted = false, DateTime? newDate = null,
        int? boardId = null, string title = null, double? startHour = null, double? duration = null)
    {
        var exception = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = DateTime.SpecifyKind(originalDate.Date, DateTimeKind.Utc),
            IsDeleted = isDeleted,
            NewDate = newDate.HasValue ? DateTime.SpecifyKind(newDate.Value.Date, DateTimeKind.Utc) : null,
            BoardId = boardId,
            Title = title,
            StartHour = startHour,
            Duration = duration,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(exception);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return exception.Id;
    }

    /// <summary>
    /// Request factory. <paramref name="pageSize"/> defaults to 0 — the unpaged path — so a
    /// test that is not about paging always sees the whole match set; the model's own default
    /// is 25 and would silently truncate.
    /// </summary>
    private static ComplianceReportRequestModel Request(
        DateTime from, DateTime to, string status = "open",
        int? propertyId = null, List<int> boardIds = null, List<int> tagIds = null,
        List<int> siteIds = null, int pageIndex = 0, int pageSize = 0,
        string sort = null, bool isSortDsc = true)
        => new()
        {
            DateFrom = from,
            DateTo = to,
            Status = status,
            PropertyId = propertyId,
            BoardIds = boardIds ?? [],
            TagIds = tagIds ?? [],
            SiteIds = siteIds ?? [],
            PageIndex = pageIndex,
            PageSize = pageSize,
            Sort = sort,
            IsSortDsc = isSortDsc
        };

    private static List<int> Ids(ComplianceReportPagedModel model)
        => model.Entities.Select(r => r.ComplianceId).ToList();

    // ==================================================================
    // PAGING
    // ==================================================================

    /// <summary>
    /// 5 matching rows, PageSize=2 — pages 0/1/2 hold 2/2/1 rows, page 3 holds none, and
    /// every page reports the same Total=5. Rows are on five distinct dates so the default
    /// taskDate-descending order fully determines which row lands on which page.
    /// </summary>
    [Test]
    public async Task ComplianceReportIndex_Paging_PageSizeTwo_SlicesTheSetAndKeepsTotalOnEveryPage()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "PagingProp", "Paging Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        var byDay = new List<int>();
        for (var day = 0; day < 5; day++)
        {
            var caseId = await SeedSdkCase(status: 50);
            byDay.Add(await SeedCompliance(planningId, propertyId, areaId, today.AddDays(day), caseId));
        }

        // Default order is taskDate DESCENDING → the last-seeded (latest) row comes first.
        var expectedOrder = Enumerable.Reverse(byDay).ToList();

        var service = BuildService(core);
        var from = today.AddDays(-1);
        var to = today.AddDays(5);

        var page0 = await service.Index(Request(from, to, pageIndex: 0, pageSize: 2));
        Assert.That(page0.Success, Is.True, page0.Message);
        Assert.Multiple(() =>
        {
            Assert.That(page0.Model!.Total, Is.EqualTo(5), "Total is the whole match set, not the page");
            Assert.That(page0.Model.Entities, Has.Count.EqualTo(2));
            Assert.That(Ids(page0.Model), Is.EqualTo(expectedOrder.Take(2).ToList()));
        });

        var page1 = await service.Index(Request(from, to, pageIndex: 1, pageSize: 2));
        Assert.That(page1.Success, Is.True, page1.Message);
        Assert.Multiple(() =>
        {
            Assert.That(page1.Model!.Total, Is.EqualTo(5));
            Assert.That(page1.Model.Entities, Has.Count.EqualTo(2));
            Assert.That(Ids(page1.Model), Is.EqualTo(expectedOrder.Skip(2).Take(2).ToList()));
        });

        var page2 = await service.Index(Request(from, to, pageIndex: 2, pageSize: 2));
        Assert.That(page2.Success, Is.True, page2.Message);
        Assert.Multiple(() =>
        {
            Assert.That(page2.Model!.Total, Is.EqualTo(5));
            Assert.That(page2.Model.Entities, Has.Count.EqualTo(1), "the last page holds the remainder");
            Assert.That(Ids(page2.Model), Is.EqualTo(expectedOrder.Skip(4).Take(1).ToList()));
        });

        var page3 = await service.Index(Request(from, to, pageIndex: 3, pageSize: 2));
        Assert.That(page3.Success, Is.True, page3.Message);
        Assert.Multiple(() =>
        {
            Assert.That(page3.Model!.Total, Is.EqualTo(5),
                "a page past the end still reports the full Total");
            Assert.That(page3.Model.Entities, Is.Empty, "a page past the end holds no rows");
        });

        // The three non-empty pages together are the whole set, with no row on two pages.
        var union = Ids(page0.Model!).Concat(Ids(page1.Model!)).Concat(Ids(page2.Model!)).ToList();
        Assert.That(union, Is.EquivalentTo(byDay), "paging must partition the set, not resample it");
    }

    /// <summary>
    /// PageSize &lt;= 0 is the unpaged path #1167/#1169 rely on: everything comes back, and
    /// Total still equals the row count.
    /// </summary>
    [Test]
    public async Task ComplianceReportIndex_PageSizeZeroOrNegative_ReturnsEveryRowUnpaged()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "UnpagedProp", "Unpaged Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        var seeded = new List<int>();
        for (var day = 0; day < 5; day++)
        {
            var caseId = await SeedSdkCase(status: 50);
            seeded.Add(await SeedCompliance(planningId, propertyId, areaId, today.AddDays(day), caseId));
        }

        var service = BuildService(core);
        var from = today.AddDays(-1);
        var to = today.AddDays(5);

        var zero = await service.Index(Request(from, to, pageSize: 0));
        Assert.That(zero.Success, Is.True, zero.Message);
        Assert.Multiple(() =>
        {
            Assert.That(zero.Model!.Total, Is.EqualTo(5));
            Assert.That(zero.Model.Entities, Has.Count.EqualTo(5), "PageSize=0 must not page");
            Assert.That(Ids(zero.Model), Is.EquivalentTo(seeded));
        });

        var negative = await service.Index(Request(from, to, pageSize: -1));
        Assert.That(negative.Success, Is.True, negative.Message);
        Assert.Multiple(() =>
        {
            Assert.That(negative.Model!.Total, Is.EqualTo(5));
            Assert.That(negative.Model.Entities, Has.Count.EqualTo(5), "PageSize=-1 must not page");
            Assert.That(Ids(negative.Model), Is.EquivalentTo(seeded));
        });
    }

    /// <summary>
    /// Total is counted at the END of the in-memory phase, not from a SQL count after the
    /// date/property/tag/site query. Five rows match in SQL; one is hidden by an IsDeleted
    /// exception and one is moved out of the requested window by a NewDate exception, so the
    /// answer is 3 — a CountAsync() at the end of phase A would say 5.
    /// </summary>
    [Test]
    public async Task ComplianceReportIndex_Total_IsPostFilter_ExcludesExceptionDeletedAndMovedOutOfRange()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "TotalPostFilterProp", "Total Post Filter Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        var byDay = new List<int>();
        for (var day = 0; day < 5; day++)
        {
            var caseId = await SeedSdkCase(status: 50);
            byDay.Add(await SeedCompliance(planningId, propertyId, areaId, today.AddDays(day), caseId));
        }

        // Day 1: hidden outright.
        await SeedException(arpId, today.AddDays(1), isDeleted: true);
        // Day 2: moved to a date far outside [today-1, today+5] → dropped by the range re-check.
        await SeedException(arpId, today.AddDays(2), newDate: today.AddDays(40));

        var survivors = new List<int> { byDay[0], byDay[3], byDay[4] };

        var service = BuildService(core);
        var from = today.AddDays(-1);
        var to = today.AddDays(5);

        // Sanity: all five rows really do survive the SQL half of the pipeline. Without the
        // exceptions the same request returns five, so the drop below is the exceptions'
        // doing and not a mis-seeded date window.
        BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.RemoveRange(
            BackendConfigurationPnDbContext.CalendarOccurrenceExceptions);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        var withoutExceptions = await service.Index(Request(from, to, pageSize: 0));
        Assert.That(withoutExceptions.Success, Is.True, withoutExceptions.Message);
        Assert.That(withoutExceptions.Model!.Total, Is.EqualTo(5),
            "all five rows must pass the SQL filters, so the exception drops below are real");

        await SeedException(arpId, today.AddDays(1), isDeleted: true);
        await SeedException(arpId, today.AddDays(2), newDate: today.AddDays(40));

        var unpaged = await service.Index(Request(from, to, pageSize: 0));
        Assert.That(unpaged.Success, Is.True, unpaged.Message);
        Assert.Multiple(() =>
        {
            Assert.That(unpaged.Model!.Total, Is.EqualTo(3),
                "Total must count NEITHER the IsDeleted row NOR the row moved out of range");
            Assert.That(unpaged.Model.Entities, Has.Count.EqualTo(3));
            Assert.That(Ids(unpaged.Model), Is.EquivalentTo(survivors));
            Assert.That(Ids(unpaged.Model), Does.Not.Contain(byDay[1]), "IsDeleted hides the row");
            Assert.That(Ids(unpaged.Model), Does.Not.Contain(byDay[2]), "NewDate moved the row out of range");
        });

        // Paged requests report the same post-filter Total — this is what #1163/#1165 render
        // as "Viser {from}-{to} af {n}", so a page-sized request must not resurrect the 5.
        var paged = await service.Index(Request(from, to, pageIndex: 0, pageSize: 2));
        Assert.That(paged.Success, Is.True, paged.Message);
        Assert.Multiple(() =>
        {
            Assert.That(paged.Model!.Total, Is.EqualTo(3));
            Assert.That(paged.Model.Entities, Has.Count.EqualTo(2));
        });

        var lastPage = await service.Index(Request(from, to, pageIndex: 1, pageSize: 2));
        Assert.That(lastPage.Success, Is.True, lastPage.Message);
        Assert.Multiple(() =>
        {
            Assert.That(lastPage.Model!.Total, Is.EqualTo(3));
            Assert.That(lastPage.Model.Entities, Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// Total is also computed after the status filter, which cannot run in SQL at all
    /// (done-ness lives on the SDK Case in a different database). 3 open + 2 done, asked for
    /// "open", must report Total=3.
    /// </summary>
    [Test]
    public async Task ComplianceReportIndex_Total_IsPostStatus_CountsOnlyTheRequestedStatus()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "TotalPostStatusProp", "Total Post Status Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        var openIds = new List<int>();
        for (var day = 0; day < 3; day++)
        {
            var caseId = await SeedSdkCase(status: 50);
            openIds.Add(await SeedCompliance(planningId, propertyId, areaId, today.AddDays(day), caseId));
        }

        // Completed occurrences are soft-removed but keep their MicrotingSdkCaseId — the
        // production shape, and the one that distinguishes "done" from "user-deleted".
        var doneIds = new List<int>();
        for (var day = 3; day < 5; day++)
        {
            var caseId = await SeedSdkCase(status: 100, doneAt: today.AddDays(day).AddHours(-1));
            doneIds.Add(await SeedCompliance(
                planningId, propertyId, areaId, today.AddDays(day), caseId, removed: true));
        }

        var service = BuildService(core);
        var from = today.AddDays(-1);
        var to = today.AddDays(5);

        var open = await service.Index(Request(from, to, status: "open", pageIndex: 0, pageSize: 10));
        Assert.That(open.Success, Is.True, open.Message);
        Assert.Multiple(() =>
        {
            Assert.That(open.Model!.Total, Is.EqualTo(3), "Total must exclude the two done rows");
            Assert.That(Ids(open.Model), Is.EquivalentTo(openIds));
            Assert.That(open.Model.Entities.All(r => !r.Completed), Is.True);
        });

        var done = await service.Index(Request(from, to, status: "done", pageIndex: 0, pageSize: 10));
        Assert.That(done.Success, Is.True, done.Message);
        Assert.Multiple(() =>
        {
            Assert.That(done.Model!.Total, Is.EqualTo(2));
            Assert.That(Ids(done.Model), Is.EquivalentTo(doneIds));
            Assert.That(done.Model.Entities.All(r => r.Completed), Is.True);
        });

        var all = await service.Index(Request(from, to, status: "all", pageIndex: 0, pageSize: 10));
        Assert.That(all.Success, Is.True, all.Message);
        Assert.That(all.Model!.Total, Is.EqualTo(5));
    }

    // ==================================================================
    // SORTING
    // ==================================================================

    [Test]
    public async Task ComplianceReportIndex_SortByTaskDate_BothDirections()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "SortTaskDateProp", "Sort TaskDate Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        // Seeded OUT of date order on purpose: ComplianceId order (middle, latest,
        // earliest) matches NEITHER the ascending nor the descending expectation, so
        // neither assertion can pass on the ThenBy(ComplianceId) tiebreak alone.
        var middleCase = await SeedSdkCase(status: 50);
        var middle = await SeedCompliance(planningId, propertyId, areaId, today, middleCase);
        var latestCase = await SeedSdkCase(status: 50);
        var latest = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(2), latestCase);
        var earliestCase = await SeedSdkCase(status: 50);
        var earliest = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-2), earliestCase);

        var service = BuildService(core);
        var from = today.AddDays(-3);
        var to = today.AddDays(3);

        var desc = await service.Index(Request(from, to, sort: "taskDate", isSortDsc: true));
        Assert.That(desc.Success, Is.True, desc.Message);
        Assert.That(Ids(desc.Model!), Is.EqualTo(new List<int> { latest, middle, earliest }));

        var asc = await service.Index(Request(from, to, sort: "taskDate", isSortDsc: false));
        Assert.That(asc.Success, Is.True, asc.Message);
        Assert.That(Ids(asc.Model!), Is.EqualTo(new List<int> { earliest, middle, latest }));
    }

    [Test]
    public async Task ComplianceReportIndex_SortByTitle_BothDirections()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        // Seeded out of alphabetical order, so a "sorted" result that is really just
        // insertion order (the ComplianceId tiebreak) fails.
        var charlie = await SeedRowWithTitle("Charlie Sorted Title", today);
        var alpha = await SeedRowWithTitle("Alpha Sorted Title", today);
        var bravo = await SeedRowWithTitle("Bravo Sorted Title", today);

        var service = BuildService(core);
        var from = today.AddDays(-1);
        var to = today.AddDays(1);

        var asc = await service.Index(Request(from, to, sort: "title", isSortDsc: false));
        Assert.That(asc.Success, Is.True, asc.Message);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(asc.Model!), Is.EqualTo(new List<int> { alpha, bravo, charlie }));
            Assert.That(asc.Model!.Entities.Select(r => r.Title), Is.EqualTo(new[]
            {
                "Alpha Sorted Title", "Bravo Sorted Title", "Charlie Sorted Title"
            }));
        });

        var desc = await service.Index(Request(from, to, sort: "title", isSortDsc: true));
        Assert.That(desc.Success, Is.True, desc.Message);
        Assert.That(Ids(desc.Model!), Is.EqualTo(new List<int> { charlie, bravo, alpha }));
    }

    [Test]
    public async Task ComplianceReportIndex_SortByPropertyName_BothDirections()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var ccc = await SeedRowOnProperty("Ccc Sorted Property", today);
        var aaa = await SeedRowOnProperty("Aaa Sorted Property", today);
        var bbb = await SeedRowOnProperty("Bbb Sorted Property", today);

        var service = BuildService(core);
        var from = today.AddDays(-1);
        var to = today.AddDays(1);

        var asc = await service.Index(Request(from, to, sort: "propertyName", isSortDsc: false));
        Assert.That(asc.Success, Is.True, asc.Message);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(asc.Model!), Is.EqualTo(new List<int> { aaa, bbb, ccc }));
            Assert.That(asc.Model!.Entities[0].PropertyName, Does.StartWith("Aaa Sorted Property"));
            Assert.That(asc.Model.Entities[2].PropertyName, Does.StartWith("Ccc Sorted Property"));
        });

        var desc = await service.Index(Request(from, to, sort: "propertyName", isSortDsc: true));
        Assert.That(desc.Success, Is.True, desc.Message);
        Assert.That(Ids(desc.Model!), Is.EqualTo(new List<int> { ccc, bbb, aaa }));
    }

    [Test]
    public async Task ComplianceReportIndex_SortByBoardName_BothDirections()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var ccc = await SeedRowOnBoard("Ccc Sorted Board", today);
        var aaa = await SeedRowOnBoard("Aaa Sorted Board", today);
        var bbb = await SeedRowOnBoard("Bbb Sorted Board", today);

        var service = BuildService(core);
        var from = today.AddDays(-1);
        var to = today.AddDays(1);

        var asc = await service.Index(Request(from, to, sort: "boardName", isSortDsc: false));
        Assert.That(asc.Success, Is.True, asc.Message);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(asc.Model!), Is.EqualTo(new List<int> { aaa, bbb, ccc }));
            Assert.That(asc.Model!.Entities.Select(r => r.BoardName), Is.EqualTo(new[]
            {
                "Aaa Sorted Board", "Bbb Sorted Board", "Ccc Sorted Board"
            }));
        });

        var desc = await service.Index(Request(from, to, sort: "boardName", isSortDsc: true));
        Assert.That(desc.Success, Is.True, desc.Message);
        Assert.That(Ids(desc.Model!), Is.EqualTo(new List<int> { ccc, bbb, aaa }));
    }

    [Test]
    public async Task ComplianceReportIndex_SortByCompleted_BothDirections()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "SortCompletedProp", "Sort Completed Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        // Interleaved so insertion order is neither ascending nor descending on Completed.
        var doneCase1 = await SeedSdkCase(status: 100, doneAt: today.AddHours(-5));
        var done1 = await SeedCompliance(planningId, propertyId, areaId, today, doneCase1, removed: true);
        var openCase1 = await SeedSdkCase(status: 50);
        var open1 = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(1), openCase1);
        var doneCase2 = await SeedSdkCase(status: 100, doneAt: today.AddHours(-4));
        var done2 = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(2), doneCase2, removed: true);
        var openCase2 = await SeedSdkCase(status: 50);
        var open2 = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(3), openCase2);

        var service = BuildService(core);
        var from = today.AddDays(-1);
        var to = today.AddDays(4);

        var asc = await service.Index(Request(from, to, status: "all", sort: "completed", isSortDsc: false));
        Assert.That(asc.Success, Is.True, asc.Message);
        Assert.Multiple(() =>
        {
            Assert.That(asc.Model!.Entities.Select(r => r.Completed),
                Is.EqualTo(new[] { false, false, true, true }));
            Assert.That(Ids(asc.Model).Take(2), Is.EquivalentTo(new[] { open1, open2 }));
            Assert.That(Ids(asc.Model).Skip(2), Is.EquivalentTo(new[] { done1, done2 }));
        });

        var desc = await service.Index(Request(from, to, status: "all", sort: "completed", isSortDsc: true));
        Assert.That(desc.Success, Is.True, desc.Message);
        Assert.Multiple(() =>
        {
            Assert.That(desc.Model!.Entities.Select(r => r.Completed),
                Is.EqualTo(new[] { true, true, false, false }));
            Assert.That(Ids(desc.Model).Take(2), Is.EquivalentTo(new[] { done1, done2 }));
            Assert.That(Ids(desc.Model).Skip(2), Is.EquivalentTo(new[] { open1, open2 }));
        });
    }

    [Test]
    public async Task ComplianceReportIndex_SortByDoneAt_BothDirections()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "SortDoneAtProp", "Sort DoneAt Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        // DoneAt deliberately does NOT track the deadline order, so a doneAt sort that
        // silently fell back to taskDate would produce a different sequence.
        var midCase = await SeedSdkCase(status: 100, doneAt: today.AddHours(-5));
        var mid = await SeedCompliance(planningId, propertyId, areaId, today, midCase, removed: true);
        var lateCase = await SeedSdkCase(status: 100, doneAt: today.AddHours(-1));
        var late = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(1), lateCase, removed: true);
        var earlyCase = await SeedSdkCase(status: 100, doneAt: today.AddHours(-9));
        var early = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(2), earlyCase, removed: true);

        var service = BuildService(core);
        var from = today.AddDays(-1);
        var to = today.AddDays(3);

        var asc = await service.Index(Request(from, to, status: "done", sort: "doneAt", isSortDsc: false));
        Assert.That(asc.Success, Is.True, asc.Message);
        Assert.That(Ids(asc.Model!), Is.EqualTo(new List<int> { early, mid, late }));

        var desc = await service.Index(Request(from, to, status: "done", sort: "doneAt", isSortDsc: true));
        Assert.That(desc.Success, Is.True, desc.Message);
        Assert.That(Ids(desc.Model!), Is.EqualTo(new List<int> { late, mid, early }));
    }

    /// <summary>
    /// An open task has no completion date, and it does not belong at the top of "sorted by
    /// completion" in either direction — so doneAt puts nulls LAST both ascending and
    /// descending.
    /// </summary>
    [Test]
    public async Task ComplianceReportIndex_SortByDoneAt_PutsNullsLastInBothDirections()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "SortDoneAtNullsProp", "Sort DoneAt Nulls Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        // Open rows first, so "nulls last" cannot be satisfied by insertion order alone.
        var openCase1 = await SeedSdkCase(status: 50);
        var open1 = await SeedCompliance(planningId, propertyId, areaId, today, openCase1);
        var openCase2 = await SeedSdkCase(status: 50);
        var open2 = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(1), openCase2);
        var doneCase1 = await SeedSdkCase(status: 100, doneAt: today.AddHours(-8));
        var done1 = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(2), doneCase1, removed: true);
        var doneCase2 = await SeedSdkCase(status: 100, doneAt: today.AddHours(-2));
        var done2 = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(3), doneCase2, removed: true);

        var service = BuildService(core);
        var from = today.AddDays(-1);
        var to = today.AddDays(4);

        var asc = await service.Index(Request(from, to, status: "all", sort: "doneAt", isSortDsc: false));
        Assert.That(asc.Success, Is.True, asc.Message);
        Assert.Multiple(() =>
        {
            Assert.That(asc.Model!.Entities, Has.Count.EqualTo(4));
            Assert.That(Ids(asc.Model).Take(2), Is.EqualTo(new List<int> { done1, done2 }),
                "ascending puts the earliest completion first");
            Assert.That(asc.Model.Entities.Skip(2).All(r => r.DoneAt == null), Is.True,
                "nulls must come last when ascending");
            Assert.That(Ids(asc.Model).Skip(2), Is.EquivalentTo(new[] { open1, open2 }));
        });

        var desc = await service.Index(Request(from, to, status: "all", sort: "doneAt", isSortDsc: true));
        Assert.That(desc.Success, Is.True, desc.Message);
        Assert.Multiple(() =>
        {
            Assert.That(desc.Model!.Entities, Has.Count.EqualTo(4));
            Assert.That(Ids(desc.Model).Take(2), Is.EqualTo(new List<int> { done2, done1 }),
                "descending puts the latest completion first");
            Assert.That(desc.Model.Entities.Skip(2).All(r => r.DoneAt == null), Is.True,
                "nulls must come last when descending TOO — not float to the top");
            Assert.That(Ids(desc.Model).Skip(2), Is.EquivalentTo(new[] { open1, open2 }));
        });
    }

    /// <summary>
    /// taskDate's default direction is descending, and its tiebreak is StartHour ASCENDING.
    /// The two same-date rows are seeded high-hour first, so the ComplianceId tiebreak alone
    /// would put them the other way round.
    /// </summary>
    [Test]
    public async Task ComplianceReportIndex_SortByTaskDate_DefaultsToDescendingWithStartHourAscTiebreak()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        // Same date, StartHour 15 — seeded FIRST (lowest ComplianceId).
        var (arpHigh, propHigh, planningHigh, areaHigh, _) = await SeedSeries(
            "TiebreakHighProp", "Tiebreak High Title", today.AddDays(-30));
        await SeedCalendarConfig(arpHigh, startHour: 15.0);
        var highCase = await SeedSdkCase(status: 50);
        var highHourRow = await SeedCompliance(planningHigh, propHigh, areaHigh, today, highCase);

        // Same date, StartHour 7 — seeded SECOND.
        var (arpLow, propLow, planningLow, areaLow, _) = await SeedSeries(
            "TiebreakLowProp", "Tiebreak Low Title", today.AddDays(-30));
        await SeedCalendarConfig(arpLow, startHour: 7.0);
        var lowCase = await SeedSdkCase(status: 50);
        var lowHourRow = await SeedCompliance(planningLow, propLow, areaLow, today, lowCase);

        // An earlier date, to prove the primary direction is descending.
        var earlierCase = await SeedSdkCase(status: 50);
        var earlierRow = await SeedCompliance(planningHigh, propHigh, areaHigh, today.AddDays(-2), earlierCase);

        var service = BuildService(core);

        // Sort left unset entirely: the model's own defaults (Sort=null, IsSortDsc=true).
        var result = await service.Index(new ComplianceReportRequestModel
        {
            DateFrom = today.AddDays(-3),
            DateTo = today.AddDays(1),
            Status = "open",
            PageSize = 0
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(Ids(result.Model!), Is.EqualTo(new List<int> { lowHourRow, highHourRow, earlierRow }),
                "taskDate descending, with StartHour ASCENDING as the same-date tiebreak");
            Assert.That(result.Model!.Entities.Select(r => r.StartHour).Take(2),
                Is.EqualTo(new[] { 7.0, 15.0 }));
            Assert.That(result.Model.Entities[0].TaskDate, Is.EqualTo(Key(today)));
            Assert.That(result.Model.Entities[2].TaskDate, Is.EqualTo(Key(today.AddDays(-2))));
        });
    }

    /// <summary>
    /// An unknown sort key must fall back to taskDate descending, not 500. Null, empty and
    /// whitespace do the same.
    /// </summary>
    [Test]
    public async Task ComplianceReportIndex_UnknownOrMissingSortKey_FallsBackToTaskDateDescending()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "SortFallbackProp", "Sort Fallback Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        var earliestCase = await SeedSdkCase(status: 50);
        var earliest = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-2), earliestCase);
        var middleCase = await SeedSdkCase(status: 50);
        var middle = await SeedCompliance(planningId, propertyId, areaId, today, middleCase);
        var latestCase = await SeedSdkCase(status: 50);
        var latest = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(2), latestCase);

        var expected = new List<int> { latest, middle, earliest };

        var service = BuildService(core);
        var from = today.AddDays(-3);
        var to = today.AddDays(3);

        foreach (var sort in new[] { "nonsense", null, "", "   " })
        {
            var result = await service.Index(Request(from, to, sort: sort));
            Assert.That(result.Success, Is.True,
                $"an unrecognised sort key must not fail the request (sort={sort ?? "null"}): {result.Message}");
            Assert.That(Ids(result.Model!), Is.EqualTo(expected),
                $"sort={sort ?? "null"} must fall back to taskDate descending");
        }
    }

    /// <summary>
    /// Sorting happens in phase D BEFORE Skip/Take, so page 0 of a one-row page holds the
    /// globally-first title — not the first row of an unsorted page that was then sorted.
    /// </summary>
    [Test]
    public async Task ComplianceReportIndex_SortIsAppliedBeforePaging()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        // Insertion order is Z, M, A — the exact reverse of the ascending title order.
        var zzz = await SeedRowWithTitle("Zzz Paged Sort Title", today);
        var mmm = await SeedRowWithTitle("Mmm Paged Sort Title", today);
        var aaa = await SeedRowWithTitle("Aaa Paged Sort Title", today);

        var service = BuildService(core);
        var from = today.AddDays(-1);
        var to = today.AddDays(1);

        var page0 = await service.Index(
            Request(from, to, pageIndex: 0, pageSize: 1, sort: "title", isSortDsc: false));
        Assert.That(page0.Success, Is.True, page0.Message);
        Assert.Multiple(() =>
        {
            Assert.That(page0.Model!.Total, Is.EqualTo(3));
            Assert.That(page0.Model.Entities, Has.Count.EqualTo(1));
            Assert.That(page0.Model.Entities[0].ComplianceId, Is.EqualTo(aaa),
                "page 0 must hold the GLOBALLY first title, not the first row of an unsorted page");
            Assert.That(page0.Model.Entities[0].Title, Is.EqualTo("Aaa Paged Sort Title"));
        });

        var page1 = await service.Index(
            Request(from, to, pageIndex: 1, pageSize: 1, sort: "title", isSortDsc: false));
        Assert.That(page1.Success, Is.True, page1.Message);
        Assert.That(page1.Model!.Entities[0].ComplianceId, Is.EqualTo(mmm));

        var page2 = await service.Index(
            Request(from, to, pageIndex: 2, pageSize: 1, sort: "title", isSortDsc: false));
        Assert.That(page2.Success, Is.True, page2.Message);
        Assert.That(page2.Model!.Entities[0].ComplianceId, Is.EqualTo(zzz));
    }

    // ==================================================================
    // MULTI-SELECT FILTERS
    // ==================================================================

    [Test]
    public async Task ComplianceReportIndex_BoardIdsMultiSelect_ReturnsBothBoardsAndIgnoresUnrelatedId()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var (arp1, prop1, planning1, area1, _) = await SeedSeries("BoardMultiA", "Board Multi A", today.AddDays(-30));
        var board1 = await SeedBoard(prop1, "Board Multi One");
        await SeedCalendarConfig(arp1, boardId: board1);
        var case1 = await SeedSdkCase(status: 50);
        var row1 = await SeedCompliance(planning1, prop1, area1, today, case1);

        var (arp2, prop2, planning2, area2, _) = await SeedSeries("BoardMultiB", "Board Multi B", today.AddDays(-30));
        var board2 = await SeedBoard(prop2, "Board Multi Two");
        await SeedCalendarConfig(arp2, boardId: board2);
        var case2 = await SeedSdkCase(status: 50);
        var row2 = await SeedCompliance(planning2, prop2, area2, today, case2);

        var (arp3, prop3, planning3, area3, _) = await SeedSeries("BoardMultiC", "Board Multi C", today.AddDays(-30));
        var board3 = await SeedBoard(prop3, "Board Multi Three");
        await SeedCalendarConfig(arp3, boardId: board3);
        var case3 = await SeedSdkCase(status: 50);
        var row3 = await SeedCompliance(planning3, prop3, area3, today, case3);

        // A board no row resolves to: it belongs to prop1, whose only row is pinned to
        // board1 by its CalendarConfiguration.
        var unrelatedBoard = await SeedBoard(prop1, "Board Multi Unrelated");

        var service = BuildService(core);
        var from = today.AddDays(-1);
        var to = today.AddDays(1);

        var two = await service.Index(Request(from, to, boardIds: [board1, board2]));
        Assert.That(two.Success, Is.True, two.Message);
        Assert.Multiple(() =>
        {
            Assert.That(two.Model!.Total, Is.EqualTo(2));
            Assert.That(Ids(two.Model), Is.EquivalentTo(new[] { row1, row2 }),
                "both requested boards must contribute their rows");
            Assert.That(Ids(two.Model), Does.Not.Contain(row3));
        });

        var withUnrelated = await service.Index(
            Request(from, to, boardIds: [board1, board2, unrelatedBoard]));
        Assert.That(withUnrelated.Success, Is.True, withUnrelated.Message);
        Assert.Multiple(() =>
        {
            Assert.That(withUnrelated.Model!.Total, Is.EqualTo(2),
                "a third id that matches nothing must not change the result");
            Assert.That(Ids(withUnrelated.Model), Is.EquivalentTo(new[] { row1, row2 }));
        });
    }

    /// <summary>
    /// TagIds is pushed into SQL as an EXISTS. A row carrying BOTH requested tags must come
    /// back exactly ONCE — a naive join would fan it out into one duplicate per matching tag,
    /// inflating both the page and Total.
    /// </summary>
    [Test]
    public async Task ComplianceReportIndex_TagIdsMultiSelect_MatchesEitherAndReturnsBothTaggedRowOnce()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var tag1 = await SeedTag("Tag Multi One");
        var tag2 = await SeedTag("Tag Multi Two");
        var tag3 = await SeedTag("Tag Multi Three");

        var (arpFirst, propFirst, planningFirst, areaFirst, _) =
            await SeedSeries("TagMultiA", "Tag Multi A", today.AddDays(-30));
        await SeedCalendarConfig(arpFirst);
        await SeedArpTag(arpFirst, tag1);
        var caseFirst = await SeedSdkCase(status: 50);
        var rowFirstTagOnly = await SeedCompliance(planningFirst, propFirst, areaFirst, today, caseFirst);

        var (arpSecond, propSecond, planningSecond, areaSecond, _) =
            await SeedSeries("TagMultiB", "Tag Multi B", today.AddDays(-30));
        await SeedCalendarConfig(arpSecond);
        await SeedArpTag(arpSecond, tag2);
        var caseSecond = await SeedSdkCase(status: 50);
        var rowSecondTagOnly = await SeedCompliance(planningSecond, propSecond, areaSecond, today, caseSecond);

        var (arpBoth, propBoth, planningBoth, areaBoth, _) =
            await SeedSeries("TagMultiBoth", "Tag Multi Both", today.AddDays(-30));
        await SeedCalendarConfig(arpBoth);
        await SeedArpTag(arpBoth, tag1);
        await SeedArpTag(arpBoth, tag2);
        var caseBoth = await SeedSdkCase(status: 50);
        var rowBothTags = await SeedCompliance(planningBoth, propBoth, areaBoth, today, caseBoth);

        var (arpNeither, propNeither, planningNeither, areaNeither, _) =
            await SeedSeries("TagMultiNeither", "Tag Multi Neither", today.AddDays(-30));
        await SeedCalendarConfig(arpNeither);
        await SeedArpTag(arpNeither, tag3);
        var caseNeither = await SeedSdkCase(status: 50);
        var rowNeitherTag = await SeedCompliance(planningNeither, propNeither, areaNeither, today, caseNeither);

        var service = BuildService(core);

        // Control: with the tag filter omitted, the row carrying neither requested tag IS
        // returned. Without this, "excluded" below could pass for the wrong reason — a
        // mis-seeded date window or a completed case would keep the row out regardless of
        // the filter, and Total == 3 would still hold.
        var unfiltered = await service.Index(
            Request(today.AddDays(-1), today.AddDays(1), tagIds: []));
        Assert.That(unfiltered.Success, Is.True, unfiltered.Message);
        Assert.That(Ids(unfiltered.Model!), Does.Contain(rowNeitherTag),
            "the row carrying neither requested tag must be a candidate the filter removes, "
            + "not a row that was never returnable");

        var result = await service.Index(
            Request(today.AddDays(-1), today.AddDays(1), tagIds: [tag1, tag2]));

        Assert.That(result.Success, Is.True, result.Message);
        var ids = Ids(result.Model!);
        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain(rowFirstTagOnly), "a row matching the first tag is returned");
            Assert.That(ids, Does.Contain(rowSecondTagOnly), "a row matching the second tag is returned");
            Assert.That(ids, Does.Contain(rowBothTags));
            Assert.That(ids, Does.Not.Contain(rowNeitherTag), "a row matching neither tag is excluded");
            Assert.That(ids.Count(id => id == rowBothTags), Is.EqualTo(1),
                "a row carrying BOTH requested tags must be returned exactly once — the EXISTS "
                + "push-down must not fan the row out into one copy per matching tag");
            Assert.That(result.Model!.Total, Is.EqualTo(3),
                "Total must not be inflated by the double-tagged row either");
            Assert.That(result.Model.Entities, Has.Count.EqualTo(3));
        });
    }

    /// <summary>
    /// The same three assertions for SiteIds, whose EXISTS push-down over PlanningSites has
    /// the identical fan-out hazard.
    /// </summary>
    [Test]
    public async Task ComplianceReportIndex_SiteIdsMultiSelect_MatchesEitherAndReturnsBothAssignedRowOnce()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var site1 = await SeedSdkSite("site-multi-one");
        var site2 = await SeedSdkSite("site-multi-two");
        var site3 = await SeedSdkSite("site-multi-three");

        var (arpFirst, propFirst, planningFirst, areaFirst, ruleFirst) =
            await SeedSeries("SiteMultiA", "Site Multi A", today.AddDays(-30));
        await SeedCalendarConfig(arpFirst);
        await SeedPlanningSite(arpFirst, site1, areaFirst, ruleFirst);
        var caseFirst = await SeedSdkCase(status: 50);
        var rowFirstSiteOnly = await SeedCompliance(planningFirst, propFirst, areaFirst, today, caseFirst);

        var (arpSecond, propSecond, planningSecond, areaSecond, ruleSecond) =
            await SeedSeries("SiteMultiB", "Site Multi B", today.AddDays(-30));
        await SeedCalendarConfig(arpSecond);
        await SeedPlanningSite(arpSecond, site2, areaSecond, ruleSecond);
        var caseSecond = await SeedSdkCase(status: 50);
        var rowSecondSiteOnly = await SeedCompliance(planningSecond, propSecond, areaSecond, today, caseSecond);

        var (arpBoth, propBoth, planningBoth, areaBoth, ruleBoth) =
            await SeedSeries("SiteMultiBoth", "Site Multi Both", today.AddDays(-30));
        await SeedCalendarConfig(arpBoth);
        await SeedPlanningSite(arpBoth, site1, areaBoth, ruleBoth);
        await SeedPlanningSite(arpBoth, site2, areaBoth, ruleBoth);
        var caseBoth = await SeedSdkCase(status: 50);
        var rowBothSites = await SeedCompliance(planningBoth, propBoth, areaBoth, today, caseBoth);

        var (arpNeither, propNeither, planningNeither, areaNeither, ruleNeither) =
            await SeedSeries("SiteMultiNeither", "Site Multi Neither", today.AddDays(-30));
        await SeedCalendarConfig(arpNeither);
        await SeedPlanningSite(arpNeither, site3, areaNeither, ruleNeither);
        var caseNeither = await SeedSdkCase(status: 50);
        var rowNeitherSite = await SeedCompliance(planningNeither, propNeither, areaNeither, today, caseNeither);

        var service = BuildService(core);

        // Control: with the site filter omitted, the row assigned to neither requested site
        // IS returned — so the exclusion below is the filter's doing and not a seeding slip
        // that put the row outside the window or gave it a completed case.
        var unfiltered = await service.Index(
            Request(today.AddDays(-1), today.AddDays(1), siteIds: []));
        Assert.That(unfiltered.Success, Is.True, unfiltered.Message);
        Assert.That(Ids(unfiltered.Model!), Does.Contain(rowNeitherSite),
            "the row assigned to neither requested site must be a candidate the filter removes, "
            + "not a row that was never returnable");

        var result = await service.Index(
            Request(today.AddDays(-1), today.AddDays(1), siteIds: [site1, site2]));

        Assert.That(result.Success, Is.True, result.Message);
        var ids = Ids(result.Model!);
        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain(rowFirstSiteOnly), "a row assigned to the first site is returned");
            Assert.That(ids, Does.Contain(rowSecondSiteOnly), "a row assigned to the second site is returned");
            Assert.That(ids, Does.Contain(rowBothSites));
            Assert.That(ids, Does.Not.Contain(rowNeitherSite), "a row assigned to neither site is excluded");
            Assert.That(ids.Count(id => id == rowBothSites), Is.EqualTo(1),
                "a row assigned to BOTH requested sites must be returned exactly once");
            Assert.That(result.Model!.Total, Is.EqualTo(3));
            Assert.That(result.Model.Entities, Has.Count.EqualTo(3));
        });
    }

    /// <summary>
    /// Empty multi-select lists mean "no filtering" — including for a row that carries no tag,
    /// no assigned site and no board at all.
    /// </summary>
    [Test]
    public async Task ComplianceReportIndex_EmptyFilterLists_ApplyNoFiltering()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var tagId = await SeedTag("Empty Filter Tag");
        var (arpTagged, propTagged, planningTagged, areaTagged, _) =
            await SeedSeries("EmptyFilterTagged", "Empty Filter Tagged", today.AddDays(-30));
        await SeedCalendarConfig(arpTagged);
        await SeedArpTag(arpTagged, tagId);
        var caseTagged = await SeedSdkCase(status: 50);
        var rowTagged = await SeedCompliance(planningTagged, propTagged, areaTagged, today, caseTagged);

        var (arpAssigned, propAssigned, planningAssigned, areaAssigned, ruleAssigned) =
            await SeedSeries("EmptyFilterAssigned", "Empty Filter Assigned", today.AddDays(-30));
        var boardId = await SeedBoard(propAssigned, "Empty Filter Board");
        await SeedCalendarConfig(arpAssigned, boardId: boardId);
        var siteId = await SeedSdkSite("empty-filter-site");
        await SeedPlanningSite(arpAssigned, siteId, areaAssigned, ruleAssigned);
        var caseAssigned = await SeedSdkCase(status: 50);
        var rowAssigned = await SeedCompliance(planningAssigned, propAssigned, areaAssigned, today, caseAssigned);

        // No tag, no assigned site, and no CalendarBoard on its property at all — so its
        // effective board resolves to 0. With BoardIds empty it must still be returned.
        var (arpBare, propBare, planningBare, areaBare, _) =
            await SeedSeries("EmptyFilterBare", "Empty Filter Bare", today.AddDays(-30));
        await SeedCalendarConfig(arpBare);
        var caseBare = await SeedSdkCase(status: 50);
        var rowBare = await SeedCompliance(planningBare, propBare, areaBare, today, caseBare);

        var service = BuildService(core);
        var result = await service.Index(new ComplianceReportRequestModel
        {
            DateFrom = today.AddDays(-1),
            DateTo = today.AddDays(1),
            Status = "open",
            PageSize = 0,
            BoardIds = [],
            TagIds = [],
            SiteIds = []
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Model!.Total, Is.EqualTo(3),
                "empty BoardIds/TagIds/SiteIds must not filter anything out");
            Assert.That(Ids(result.Model), Is.EquivalentTo(new[] { rowTagged, rowAssigned, rowBare }));
            Assert.That(result.Model.Entities.Single(r => r.ComplianceId == rowBare).BoardId, Is.Null,
                "a property with no board yields no BoardId, and is still returned when BoardIds is empty");
        });
    }

    /// <summary>
    /// NULL multi-select lists mean "no filtering" too. A JSON body that omits (or sends
    /// <c>null</c> for) boardIds/tagIds/siteIds deserialises to null, and the service guards
    /// each list with an <c>is { Count: &gt; 0 }</c> pattern precisely so that this does not
    /// NRE into the catch. The request is built by hand rather than through the
    /// <see cref="Request"/> helper because the helper coalesces every list with <c>?? []</c>
    /// and therefore cannot produce a null. Reverting any of the three guards to a plain
    /// <c>.Count &gt; 0</c> makes this test fail on Success.
    /// </summary>
    [Test]
    public async Task ComplianceReportIndex_NullFilterLists_ApplyNoFiltering()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var tagId = await SeedTag("Null Filter Tag");
        var (arpTagged, propTagged, planningTagged, areaTagged, ruleTagged) =
            await SeedSeries("NullFilterTagged", "Null Filter Tagged", today.AddDays(-30));
        var boardId = await SeedBoard(propTagged, "Null Filter Board");
        await SeedCalendarConfig(arpTagged, boardId: boardId);
        await SeedArpTag(arpTagged, tagId);
        var siteId = await SeedSdkSite("null-filter-site");
        await SeedPlanningSite(arpTagged, siteId, areaTagged, ruleTagged);
        var caseTagged = await SeedSdkCase(status: 50);
        var rowTagged = await SeedCompliance(planningTagged, propTagged, areaTagged, today, caseTagged);

        var (arpBare, propBare, planningBare, areaBare, _) =
            await SeedSeries("NullFilterBare", "Null Filter Bare", today.AddDays(-30));
        await SeedCalendarConfig(arpBare);
        var caseBare = await SeedSdkCase(status: 50);
        var rowBare = await SeedCompliance(planningBare, propBare, areaBare, today, caseBare);

        var service = BuildService(core);
        var result = await service.Index(new ComplianceReportRequestModel
        {
            DateFrom = today.AddDays(-1),
            DateTo = today.AddDays(1),
            Status = "open",
            PageSize = 0,
            BoardIds = null,
            TagIds = null,
            SiteIds = null
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Model!.Total, Is.EqualTo(2),
                "null BoardIds/TagIds/SiteIds must behave exactly like empty lists");
            Assert.That(Ids(result.Model), Is.EquivalentTo(new[] { rowTagged, rowBare }));
        });
    }

    // ==================================================================
    // NEW FIELD — CheckListId
    // ==================================================================

    [Test]
    public async Task ComplianceReportIndex_CheckListId_EqualsTheSdkCaseCheckListId()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var checkListId = await SeedCheckList("checklistid-match");

        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "CheckListMatchProp", "CheckList Match Title", today.AddDays(-30), eformId: checkListId);
        await SeedCalendarConfig(arpId);

        var caseId = await SeedSdkCase(status: 50, checkListId: checkListId);
        var complianceId = await SeedCompliance(planningId, propertyId, areaId, today, caseId);

        var service = BuildService(core);
        var result = await service.Index(Request(today.AddDays(-1), today.AddDays(1)));

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model!.Entities.Single(r => r.ComplianceId == complianceId);
        Assert.Multiple(() =>
        {
            Assert.That(row.CheckListId, Is.EqualTo(checkListId),
                "CheckListId is the template actually answered, read off the SDK case");
            Assert.That(row.SdkCaseId, Is.EqualTo(caseId));
        });
    }

    /// <summary>
    /// #1160 finding 1: <c>AreaRule.EformId</c> is the CURRENT configuration and disagrees
    /// with the answered template on 34 of the dev DB's 872 rows. The case wins.
    /// </summary>
    [Test]
    public async Task ComplianceReportIndex_CheckListId_ComesFromTheCaseNotAreaRuleEformId_WhenTheyDiffer()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var answeredTemplateId = await SeedCheckList("checklistid-answered");
        var configuredTemplateId = await SeedCheckList("checklistid-configured");
        Assert.That(answeredTemplateId, Is.Not.EqualTo(configuredTemplateId),
            "the two seeded templates must genuinely differ for this test to mean anything");

        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "CheckListMismatchProp", "CheckList Mismatch Title", today.AddDays(-30),
            eformId: configuredTemplateId);
        await SeedCalendarConfig(arpId);

        var caseId = await SeedSdkCase(status: 50, checkListId: answeredTemplateId);
        var complianceId = await SeedCompliance(planningId, propertyId, areaId, today, caseId);

        var service = BuildService(core);
        var result = await service.Index(Request(today.AddDays(-1), today.AddDays(1)));

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model!.Entities.Single(r => r.ComplianceId == complianceId);
        Assert.Multiple(() =>
        {
            Assert.That(row.CheckListId, Is.EqualTo(answeredTemplateId),
                "CheckListId must be the answered template from the SDK case");
            Assert.That(row.EformId, Is.EqualTo(configuredTemplateId),
                "EformId stays the AreaRule's configured template (kept on the row until #1170)");
            Assert.That(row.CheckListId, Is.Not.EqualTo(row.EformId),
                "the two fields are different concepts and must not be conflated");
        });
    }

    [Test]
    public async Task ComplianceReportIndex_CheckListId_IsPopulatedWhenAreaRuleEformIdIsNull()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var checkListId = await SeedCheckList("checklistid-null-eform");

        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "CheckListNullEformProp", "CheckList Null Eform Title", today.AddDays(-30), eformId: null);
        await SeedCalendarConfig(arpId);

        var caseId = await SeedSdkCase(status: 50, checkListId: checkListId);
        var complianceId = await SeedCompliance(planningId, propertyId, areaId, today, caseId);

        var service = BuildService(core);
        var result = await service.Index(Request(today.AddDays(-1), today.AddDays(1)));

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model!.Entities.Single(r => r.ComplianceId == complianceId);
        Assert.Multiple(() =>
        {
            Assert.That(row.EformId, Is.Null, "the AreaRule has no configured template");
            Assert.That(row.CheckListId, Is.EqualTo(checkListId),
                "CheckListId must still be populated — it does not come from AreaRule.EformId");
        });
    }

    // ------------------------------------------------------------------
    // Small composite seeders for the sorting fixtures. Each builds a
    // self-contained series + one open compliance row on <paramref name="date"/>.
    // ------------------------------------------------------------------

    private async Task<int> SeedRowWithTitle(string title, DateTime date)
    {
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "TitleSortProp", title, date.AddDays(-30));
        await SeedCalendarConfig(arpId);
        var caseId = await SeedSdkCase(status: 50);
        return await SeedCompliance(planningId, propertyId, areaId, date, caseId);
    }

    private async Task<int> SeedRowOnProperty(string propertyName, DateTime date)
    {
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            propertyName, "Property Sort Title", date.AddDays(-30));
        await SeedCalendarConfig(arpId);
        var caseId = await SeedSdkCase(status: 50);
        return await SeedCompliance(planningId, propertyId, areaId, date, caseId);
    }

    private async Task<int> SeedRowOnBoard(string boardName, DateTime date)
    {
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "BoardSortProp", "Board Sort Title", date.AddDays(-30));
        var boardId = await SeedBoard(propertyId, boardName);
        await SeedCalendarConfig(arpId, boardId: boardId);
        var caseId = await SeedSdkCase(status: 50);
        return await SeedCompliance(planningId, propertyId, areaId, date, caseId);
    }
}
