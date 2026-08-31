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
/// DB-backed integration coverage for
/// <see cref="BackendConfigurationCalendarService.GetComplianceReport"/> — the
/// flat, non-recurrence-expanding report over existing <c>Compliance</c> rows
/// used by the calendar compliance report view.
///
/// Semantics under test (see the method's own inline comments for the
/// authoritative rules):
///   - Deadline in [DateFrom, DateTo] (inclusive, end-of-day on DateTo).
///   - done  = backing SDK Case.Status == 100 (row may be soft-removed).
///   - open  = not soft-removed AND not done.
///   - soft-removed AND not done = user-deleted occurrence → never returned,
///     regardless of the requested status.
///   - status filter: "open" | "done" | "all".
///   - propertyId == null → all properties; propertyId set → that property only.
///   - board filter matches the EFFECTIVE board:
///       exception.BoardId ?? CalendarConfiguration.BoardId ?? property's
///       first-created (lowest Id) CalendarBoard.
///   - tag filter matches AreaRulePlanningTags.ItemPlanningTagId.
///   - site filter matches PlanningSites.SiteId.
///   - a CalendarOccurrenceException with IsDeleted=true hides the row; a
///     NewDate moves the effective TaskDate (and — if the new date falls
///     outside the requested range — excludes the row).
///   - rows are sorted TaskDate descending.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarComplianceReportTests : TestBaseSetup
{
    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Monotonically-increasing counter for SDK Site MicrotingUid uniqueness
    // across the whole fixture's lifetime (the container/database is reused
    // across every [Test] method in this class).
    private int _uidCounter = 900_000;

    [SetUp]
    public async Task CleanCalendarTables()
    {
        // FK-safe cleanup (children before parents) so each test starts fresh.
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

    private BackendConfigurationCalendarService BuildService(Core core)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(
            new Language { Id = 1, Name = "English", LanguageCode = "en-US" }));

        // The completion read consults the backing SDK Case, so the real
        // mocked coreHelper must be wired through (NOT null).
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
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>());
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
    /// Seeds an SDK Case with the given completion status (100 = completed,
    /// &lt;100 = open) and optional DoneAt. Returns the Case Id.
    /// </summary>
    private async Task<int> SeedSdkCase(int status, DateTime? doneAt = null, int? siteId = null)
    {
        siteId ??= await SeedSdkSite("compliance-report-test-site");

        var sdkCase = new Case
        {
            SiteId = siteId,
            Status = status,
            DoneAt = doneAt,
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
    /// Seeds Area→Property→AreaRule(+translation "title")→Planning→
    /// AreaRulePlanning (NO CalendarConfiguration — callers add one via
    /// <see cref="SeedCalendarConfig"/> if they need an explicit board/hours).
    /// Returns (arpId, propertyId, planningId, areaId, areaRuleId).
    /// </summary>
    private async Task<(int ArpId, int PropertyId, int PlanningId, int AreaId, int AreaRuleId)> SeedSeries(
        string propertyName, string title, DateTime startDate)
    {
        var (areaId, propertyId) = await SeedAreaAndProperty(propertyName);

        var areaRule = new AreaRule
        {
            AreaId = areaId, PropertyId = propertyId, EformId = 0,
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

    private async Task<int> SeedCalendarConfig(int arpId, int? boardId = null, double startHour = 9.0, double duration = 1.0)
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

    private static CalendarComplianceReportRequestModel Request(
        DateTime from, DateTime to, string status = "open",
        int? propertyId = null, List<int> boardIds = null, List<int> tagIds = null, List<int> siteIds = null)
        => new()
        {
            DateFrom = from,
            DateTo = to,
            Status = status,
            PropertyId = propertyId,
            BoardIds = boardIds ?? [],
            TagIds = tagIds ?? [],
            SiteIds = siteIds ?? []
        };

    // ------------------------------------------------------------------
    // 1 — Open row in range is returned once with correct core fields.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetComplianceReport_OpenRowInRange_ReturnedOnceWithCoreFields()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "OpenRowProp", "Open Row Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        var openCaseId = await SeedSdkCase(status: 50);
        var complianceId = await SeedCompliance(planningId, propertyId, areaId, today, openCaseId);

        var service = BuildService(core);
        var result = await service.GetComplianceReport(
            Request(today.AddDays(-3), today.AddDays(3), status: "open"));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model, Is.Not.Null);
        var rows = result.Model.Where(r => r.ComplianceId == complianceId).ToList();
        Assert.That(rows, Has.Count.EqualTo(1), "the open row must be returned exactly once");
        var row = rows[0];
        Assert.Multiple(() =>
        {
            Assert.That(row.Completed, Is.False);
            Assert.That(row.Title, Is.EqualTo("Open Row Title"));
            Assert.That(row.PropertyId, Is.EqualTo(propertyId));
            Assert.That(row.PlanningId, Is.EqualTo(planningId));
            Assert.That(row.ComplianceId, Is.EqualTo(complianceId));
            Assert.That(row.TaskDate, Is.EqualTo(Key(today)));
        });
    }

    // ------------------------------------------------------------------
    // 2 — Completed (soft-removed compliance + SDK Case Status=100) is only
    //     returned for "done"/"all", with Completed=true and DoneAt populated.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetComplianceReport_CompletedRow_OnlyReturnedForDoneAndAll()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "CompletedRowProp", "Completed Row Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        var doneAt = today.AddHours(-2);
        var completedCaseId = await SeedSdkCase(status: 100, doneAt: doneAt);
        var complianceId = await SeedCompliance(
            planningId, propertyId, areaId, today, completedCaseId, removed: true);

        var service = BuildService(core);

        var openResult = await service.GetComplianceReport(
            Request(today.AddDays(-3), today.AddDays(3), status: "open"));
        Assert.That(openResult.Success, Is.True, openResult.Message);
        Assert.That(openResult.Model!.Any(r => r.ComplianceId == complianceId), Is.False,
            "a completed row must not be returned for status=open");

        var doneResult = await service.GetComplianceReport(
            Request(today.AddDays(-3), today.AddDays(3), status: "done"));
        Assert.That(doneResult.Success, Is.True, doneResult.Message);
        var doneRows = doneResult.Model!.Where(r => r.ComplianceId == complianceId).ToList();
        Assert.That(doneRows, Has.Count.EqualTo(1), "the completed row must be returned for status=done");
        Assert.Multiple(() =>
        {
            Assert.That(doneRows[0].Completed, Is.True);
            Assert.That(doneRows[0].DoneAt, Is.EqualTo(doneAt));
        });

        var allResult = await service.GetComplianceReport(
            Request(today.AddDays(-3), today.AddDays(3), status: "all"));
        Assert.That(allResult.Success, Is.True, allResult.Message);
        Assert.That(allResult.Model!.Any(r => r.ComplianceId == complianceId), Is.True,
            "the completed row must be returned for status=all");
    }

    // ------------------------------------------------------------------
    // 3 — User-deleted (soft-removed, backing case NOT completed) is never
    //     returned, for any status.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetComplianceReport_UserDeletedRow_NeverReturnedForAnyStatus()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "DeletedRowProp", "Deleted Row Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        var openCaseId = await SeedSdkCase(status: 50);
        var complianceId = await SeedCompliance(
            planningId, propertyId, areaId, today, openCaseId, removed: true);

        var service = BuildService(core);

        foreach (var status in new[] { "open", "done", "all" })
        {
            var result = await service.GetComplianceReport(
                Request(today.AddDays(-3), today.AddDays(3), status: status));
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Model!.Any(r => r.ComplianceId == complianceId), Is.False,
                $"a user-deleted row must never be returned (status={status})");
        }
    }

    // ------------------------------------------------------------------
    // 4 — Date-range boundaries: DateFrom/DateTo inclusive, day before/after
    //     excluded.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetComplianceReport_DateRangeBoundaries_InclusiveFromAndTo()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "BoundaryProp", "Boundary Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        var dateFrom = today;
        var dateTo = today.AddDays(2);

        var beforeCaseId = await SeedSdkCase(status: 50);
        var beforeId = await SeedCompliance(planningId, propertyId, areaId, dateFrom.AddDays(-1), beforeCaseId);

        var onFromCaseId = await SeedSdkCase(status: 50);
        var onFromId = await SeedCompliance(planningId, propertyId, areaId, dateFrom, onFromCaseId);

        // Exactly on DateTo, but at the end of that day — still inside the
        // inclusive end-of-day boundary the service computes.
        var onToCaseId = await SeedSdkCase(status: 50);
        var onToId = await SeedCompliance(
            planningId, propertyId, areaId, dateTo.AddHours(23).AddMinutes(59), onToCaseId);

        var afterCaseId = await SeedSdkCase(status: 50);
        var afterId = await SeedCompliance(planningId, propertyId, areaId, dateTo.AddDays(1), afterCaseId);

        var service = BuildService(core);
        var result = await service.GetComplianceReport(Request(dateFrom, dateTo, status: "open"));
        Assert.That(result.Success, Is.True, result.Message);

        var ids = result.Model!.Select(r => r.ComplianceId).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Not.Contain(beforeId), "the day before DateFrom must be excluded");
            Assert.That(ids, Does.Contain(onFromId), "DateFrom itself must be included");
            Assert.That(ids, Does.Contain(onToId), "DateTo itself must be included (end-of-day inclusive)");
            Assert.That(ids, Does.Not.Contain(afterId), "the day after DateTo must be excluded");
        });
    }

    // ------------------------------------------------------------------
    // 5 — PropertyId filter: null returns rows from multiple properties;
    //     set restricts to that property only.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetComplianceReport_PropertyIdFilter_NullReturnsAllSetReturnsOne()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var (arp1, prop1, planning1, area1, _) = await SeedSeries("PropA", "Prop A Title", today.AddDays(-30));
        await SeedCalendarConfig(arp1);
        var case1 = await SeedSdkCase(status: 50);
        var compliance1 = await SeedCompliance(planning1, prop1, area1, today, case1);

        var (arp2, prop2, planning2, area2, _) = await SeedSeries("PropB", "Prop B Title", today.AddDays(-30));
        await SeedCalendarConfig(arp2);
        var case2 = await SeedSdkCase(status: 50);
        var compliance2 = await SeedCompliance(planning2, prop2, area2, today, case2);

        var service = BuildService(core);

        var allResult = await service.GetComplianceReport(
            Request(today.AddDays(-1), today.AddDays(1), status: "open", propertyId: null));
        Assert.That(allResult.Success, Is.True, allResult.Message);
        var allIds = allResult.Model!.Select(r => r.ComplianceId).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(allIds, Does.Contain(compliance1));
            Assert.That(allIds, Does.Contain(compliance2));
        });

        var scopedResult = await service.GetComplianceReport(
            Request(today.AddDays(-1), today.AddDays(1), status: "open", propertyId: prop1));
        Assert.That(scopedResult.Success, Is.True, scopedResult.Message);
        var scopedIds = scopedResult.Model!.Select(r => r.ComplianceId).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(scopedIds, Does.Contain(compliance1));
            Assert.That(scopedIds, Does.Not.Contain(compliance2));
            Assert.That(scopedResult.Model!.All(r => r.PropertyId == prop1), Is.True);
        });
    }

    // ------------------------------------------------------------------
    // 6 — TagIds filter matches AreaRulePlanningTags; a non-matching tag
    //     excludes the row.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetComplianceReport_TagIdsFilter_MatchesArpTagsExcludesOthers()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "TagProp", "Tag Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        var matchingTagId = await SeedTag("Matching Tag");
        var unrelatedTagId = await SeedTag("Unrelated Tag");
        await SeedArpTag(arpId, matchingTagId);

        var caseId = await SeedSdkCase(status: 50);
        var complianceId = await SeedCompliance(planningId, propertyId, areaId, today, caseId);

        var service = BuildService(core);

        var matchResult = await service.GetComplianceReport(
            Request(today.AddDays(-1), today.AddDays(1), status: "open", tagIds: [matchingTagId]));
        Assert.That(matchResult.Success, Is.True, matchResult.Message);
        Assert.That(matchResult.Model!.Any(r => r.ComplianceId == complianceId), Is.True,
            "the matching tag filter must include the row");

        var nonMatchResult = await service.GetComplianceReport(
            Request(today.AddDays(-1), today.AddDays(1), status: "open", tagIds: [unrelatedTagId]));
        Assert.That(nonMatchResult.Success, Is.True, nonMatchResult.Message);
        Assert.That(nonMatchResult.Model!.Any(r => r.ComplianceId == complianceId), Is.False,
            "a non-matching tag filter must exclude the row");
    }

    // ------------------------------------------------------------------
    // 7 — SiteIds filter via PlanningSites.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetComplianceReport_SiteIdsFilter_MatchesPlanningSitesExcludesOthers()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, areaRuleId) = await SeedSeries(
            "SiteProp", "Site Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        var assignedSiteId = await SeedSdkSite("assigned-site");
        var otherSiteId = await SeedSdkSite("other-site");
        await SeedPlanningSite(arpId, assignedSiteId, areaId, areaRuleId);

        var caseId = await SeedSdkCase(status: 50);
        var complianceId = await SeedCompliance(planningId, propertyId, areaId, today, caseId);

        var service = BuildService(core);

        var matchResult = await service.GetComplianceReport(
            Request(today.AddDays(-1), today.AddDays(1), status: "open", siteIds: [assignedSiteId]));
        Assert.That(matchResult.Success, Is.True, matchResult.Message);
        Assert.That(matchResult.Model!.Any(r => r.ComplianceId == complianceId), Is.True,
            "the assigned site filter must include the row");

        var nonMatchResult = await service.GetComplianceReport(
            Request(today.AddDays(-1), today.AddDays(1), status: "open", siteIds: [otherSiteId]));
        Assert.That(nonMatchResult.Success, Is.True, nonMatchResult.Message);
        Assert.That(nonMatchResult.Model!.Any(r => r.ComplianceId == complianceId), Is.False,
            "a non-assigned site filter must exclude the row");
    }

    // ------------------------------------------------------------------
    // 8 — BoardIds filter via CalendarConfiguration.BoardId, and via the
    //     property's first-created board fallback when no CalendarConfiguration
    //     exists.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetComplianceReport_BoardIdsFilter_ExplicitConfigAndDefaultFallback()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        // (a) Explicit board via CalendarConfiguration.BoardId.
        var (arpExplicit, propExplicit, planningExplicit, areaExplicit, _) =
            await SeedSeries("ExplicitBoardProp", "Explicit Board Title", today.AddDays(-30));
        var boardX = await SeedBoard(propExplicit, "Board X");
        var boardY = await SeedBoard(propExplicit, "Board Y");
        await SeedCalendarConfig(arpExplicit, boardId: boardX);
        var explicitCaseId = await SeedSdkCase(status: 50);
        var explicitComplianceId = await SeedCompliance(
            planningExplicit, propExplicit, areaExplicit, today, explicitCaseId);

        // (b) Default-board fallback: the ARP has NO CalendarConfiguration, so
        // the effective board is the property's first-created (lowest Id) board.
        var (arpDefault, propDefault, planningDefault, areaDefault, _) =
            await SeedSeries("DefaultBoardProp", "Default Board Title", today.AddDays(-30));
        var oldestBoard = await SeedBoard(propDefault, "Oldest Board");
        var newerBoard = await SeedBoard(propDefault, "Newer Board");
        // Intentionally NO CalendarConfiguration for arpDefault.
        var defaultCaseId = await SeedSdkCase(status: 50);
        var defaultComplianceId = await SeedCompliance(
            planningDefault, propDefault, areaDefault, today, defaultCaseId);

        var service = BuildService(core);
        var range = (From: today.AddDays(-1), To: today.AddDays(1));

        var explicitMatch = await service.GetComplianceReport(
            Request(range.From, range.To, status: "open", boardIds: [boardX]));
        Assert.That(explicitMatch.Model!.Any(r => r.ComplianceId == explicitComplianceId), Is.True,
            "filtering by the configured board must include the row");

        var explicitNonMatch = await service.GetComplianceReport(
            Request(range.From, range.To, status: "open", boardIds: [boardY]));
        Assert.That(explicitNonMatch.Model!.Any(r => r.ComplianceId == explicitComplianceId), Is.False,
            "filtering by a different board must exclude the row");

        var defaultMatch = await service.GetComplianceReport(
            Request(range.From, range.To, status: "open", boardIds: [oldestBoard]));
        Assert.That(defaultMatch.Model!.Any(r => r.ComplianceId == defaultComplianceId), Is.True,
            "filtering by the property's first-created board must include the unconfigured row");

        var defaultNonMatch = await service.GetComplianceReport(
            Request(range.From, range.To, status: "open", boardIds: [newerBoard]));
        Assert.That(defaultNonMatch.Model!.Any(r => r.ComplianceId == defaultComplianceId), Is.False,
            "filtering by a non-default board must exclude the unconfigured row");
    }

    // ------------------------------------------------------------------
    // 9 — CalendarOccurrenceException.IsDeleted=true hides the row.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetComplianceReport_ExceptionIsDeleted_HidesRow()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "ExceptionDeletedProp", "Exception Deleted Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        var caseId = await SeedSdkCase(status: 50);
        var complianceId = await SeedCompliance(planningId, propertyId, areaId, today, caseId);

        await SeedException(arpId, today, isDeleted: true);

        var service = BuildService(core);
        var result = await service.GetComplianceReport(
            Request(today.AddDays(-1), today.AddDays(1), status: "all"));
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.Any(r => r.ComplianceId == complianceId), Is.False,
            "an IsDeleted exception must hide the occurrence for any status");
    }

    // ------------------------------------------------------------------
    // 10 — CalendarOccurrenceException.NewDate moves the effective TaskDate;
    //      a NewDate outside the requested range excludes the row.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetComplianceReport_ExceptionNewDate_MovesTaskDateAndOutOfRangeExcludes()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        // (a) NewDate moves the row to a date still inside the range.
        var (arpMoved, propMoved, planningMoved, areaMoved, _) =
            await SeedSeries("MovedProp", "Moved Title", today.AddDays(-30));
        await SeedCalendarConfig(arpMoved);
        var movedCaseId = await SeedSdkCase(status: 50);
        var movedComplianceId = await SeedCompliance(planningMoved, propMoved, areaMoved, today, movedCaseId);
        var newDateInRange = today.AddDays(2);
        await SeedException(arpMoved, today, newDate: newDateInRange);

        // (b) NewDate moves the row OUTSIDE the requested range → excluded.
        var (arpOut, propOut, planningOut, areaOut, _) =
            await SeedSeries("OutOfRangeProp", "Out Of Range Title", today.AddDays(-30));
        await SeedCalendarConfig(arpOut);
        var outCaseId = await SeedSdkCase(status: 50);
        var outComplianceId = await SeedCompliance(planningOut, propOut, areaOut, today, outCaseId);
        var newDateOutOfRange = today.AddDays(30);
        await SeedException(arpOut, today, newDate: newDateOutOfRange);

        var service = BuildService(core);
        var result = await service.GetComplianceReport(
            Request(today.AddDays(-1), today.AddDays(3), status: "open"));
        Assert.That(result.Success, Is.True, result.Message);

        var movedRows = result.Model!.Where(r => r.ComplianceId == movedComplianceId).ToList();
        Assert.That(movedRows, Has.Count.EqualTo(1), "the moved row must still be returned exactly once");
        Assert.That(movedRows[0].TaskDate, Is.EqualTo(Key(newDateInRange)),
            "the row's TaskDate must reflect the exception's NewDate");

        Assert.That(result.Model!.Any(r => r.ComplianceId == outComplianceId), Is.False,
            "a NewDate outside the requested range must exclude the row");
    }

    // ------------------------------------------------------------------
    // 11 — Rows are sorted TaskDate descending.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetComplianceReport_ReturnsRowsSortedByTaskDateDescending()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var (arpId, propertyId, planningId, areaId, _) = await SeedSeries(
            "SortProp", "Sort Title", today.AddDays(-30));
        await SeedCalendarConfig(arpId);

        var earliestCase = await SeedSdkCase(status: 50);
        var earliestId = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(-2), earliestCase);

        var middleCase = await SeedSdkCase(status: 50);
        var middleId = await SeedCompliance(planningId, propertyId, areaId, today, middleCase);

        var latestCase = await SeedSdkCase(status: 50);
        var latestId = await SeedCompliance(planningId, propertyId, areaId, today.AddDays(2), latestCase);

        var service = BuildService(core);
        var result = await service.GetComplianceReport(
            Request(today.AddDays(-3), today.AddDays(3), status: "open"));
        Assert.That(result.Success, Is.True, result.Message);

        var ourRows = result.Model!
            .Where(r => r.ComplianceId == earliestId || r.ComplianceId == middleId || r.ComplianceId == latestId)
            .ToList();
        Assert.That(ourRows, Has.Count.EqualTo(3));
        Assert.That(ourRows.Select(r => r.ComplianceId), Is.EqualTo(new[] { latestId, middleId, earliestId }),
            "rows must be sorted by TaskDate descending");
    }
}
