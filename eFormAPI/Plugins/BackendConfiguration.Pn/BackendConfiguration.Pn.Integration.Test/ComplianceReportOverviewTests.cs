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
/// DB-backed integration coverage for the Oversigt aggregation
/// <see cref="BackendConfigurationComplianceReportService.Overview"/>
/// (<c>POST api/backend-configuration-pn/compliance-report/overview</c>) — issue #1162 §6.
///
/// <para>
/// The ten maths cases of the prototype suite
/// (<c>lorem-ipsum/kalender/tests/compliance-overview.test.js</c>, the
/// <c>buildCompanySummaries</c> group: <c>:12 :21 :35 :43 :54 :68 :80 :88 :92 :101</c>)
/// are ported here. The other fourteen of its twenty-four cases are sorting,
/// formatting, banding and rendering — client-side presentation, and #1164's, not this
/// fixture's.
/// </para>
///
/// <para>
/// Eight cases have no prototype counterpart and are added here: the rounding MIDPOINT
/// (which the prototype cannot express, because JS has only one rounding mode), the
/// unparseable-date branch, "status is genuinely ignored", the two soft-removed
/// asymmetries, occurrence-exception flow-through, filter parity with
/// <c>Index</c>, and the board/tag/site filters.
/// </para>
///
/// <para>
/// Each <c>TestBaseSetup</c> subclass owns its own MariaDB testcontainer, so this fixture
/// carries its own copies of the seeding helpers rather than sharing
/// <c>ComplianceReportIndexTests</c>'. One differs from that fixture's:
/// <c>SeedAreaAndProperty</c> takes the property name VERBATIM (no GUID suffix), because
/// the documented row order is by <c>PropertyName</c> ordinal and a random suffix would
/// make it unassertable. Per-test cleanup makes the names safe to reuse across tests.
/// </para>
///
/// <para>
/// Seeding invariants, restated because getting one wrong makes a test pass for the wrong
/// reason: a row is DONE iff its backing SDK <c>Case.Status == 100</c>; a soft-removed row
/// that is NOT done is a user-deleted occurrence and reaches no counter; a soft-removed row
/// that IS done still counts. The Overview request model has no <c>Status</c> at all — the
/// candidate set is always built with the <c>all</c> behaviour.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ComplianceReportOverviewTests : TestBaseSetup
{
    // Monotonically-increasing counter for SDK Site MicrotingUid uniqueness across the whole
    // fixture's lifetime (the container/database is reused across every [Test] method).
    private int _uidCounter = 960_000;

    [SetUp]
    public async Task CleanCalendarTables()
    {
        // FK-safe cleanup (children before parents) so each test starts fresh. Because every
        // Compliance row in the database is one this fixture seeded, the aggregation can be
        // asserted as absolute numbers rather than filtered down to "our" rows.
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

    /// <summary>The REAL service under test — same wiring as ComplianceReportIndexTests.</summary>
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
    /// Seeds an SDK Case. <paramref name="status"/> 100 = completed, anything less = open —
    /// that single column is the ONLY definition of done-ness the aggregation has.
    /// </summary>
    private async Task<int> SeedSdkCase(int status, int? siteId = null)
    {
        siteId ??= await SeedSdkSite("compliance-report-overview-site");

        var sdkCase = new Case
        {
            SiteId = siteId,
            Status = status,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(sdkCase);
        await MicrotingDbContext.SaveChangesAsync();
        return sdkCase.Id;
    }

    /// <summary>
    /// Seeds an Area + Property pair. The property name is used VERBATIM (unlike
    /// ComplianceReportIndexTests, which appends a GUID) so the documented
    /// PropertyName-ordinal row order is assertable.
    /// </summary>
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
            Name = propertyName, ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return (area.Id, property.Id);
    }

    /// <summary>
    /// Seeds Area→Property→AreaRule(+translation)→Planning→AreaRulePlanning.
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

    private async Task<int> SeedCalendarConfig(int arpId, int? boardId = null)
    {
        var calConfig = new CalendarConfiguration
        {
            AreaRulePlanningId = arpId, BoardId = boardId, StartHour = 9.0, Duration = 1.0,
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
        bool removed = false)
    {
        var compliance = new Compliance
        {
            ItemName = "Fallback Item Name",
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
        int arpId, DateTime originalDate, bool isDeleted = false, DateTime? newDate = null)
    {
        var exception = new CalendarOccurrenceException
        {
            AreaRulePlanningId = arpId,
            OriginalDate = DateTime.SpecifyKind(originalDate.Date, DateTimeKind.Utc),
            IsDeleted = isDeleted,
            NewDate = newDate.HasValue ? DateTime.SpecifyKind(newDate.Value.Date, DateTimeKind.Utc) : null,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext!.CalendarOccurrenceExceptions.AddAsync(exception);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return exception.Id;
    }

    /// <summary>
    /// Request factory. The window straddles today by default so that "future task" and
    /// "overdue" cases are both inside it — a window bounded above by today would make the
    /// future-task tests vacuous.
    /// </summary>
    private static ComplianceReportOverviewRequestModel Request(
        DateTime from, DateTime to, int? propertyId = null,
        List<int>? boardIds = null, List<int>? tagIds = null, List<int>? siteIds = null)
        => new()
        {
            DateFrom = from,
            DateTo = to,
            PropertyId = propertyId,
            BoardIds = boardIds ?? [],
            TagIds = tagIds ?? [],
            SiteIds = siteIds ?? []
        };

    // ==================================================================
    // PORTED — the prototype's buildCompanySummaries group (10 cases)
    // ==================================================================

    /// <summary>
    /// Prototype <c>:12</c> — "empty input gives no rows and zeroed totals", plus
    /// <c>:88</c> — "percent is null rather than NaN when a group has no cases".
    /// Totals must be PRESENT, not null: #1164's empty state renders it.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_EmptyInput_NoRowsAndZeroedTotalsWithNullPercent()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;
        var service = BuildService(core);

        var result = await service.Overview(Request(today.AddDays(-30), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.Rows, Is.Empty);
        Assert.That(result.Model.Totals, Is.Not.Null, "Totals is never null, even for an empty result");
        Assert.Multiple(() =>
        {
            Assert.That(result.Model.Totals.Total, Is.Zero);
            Assert.That(result.Model.Totals.Done, Is.Zero);
            Assert.That(result.Model.Totals.Overdue, Is.Zero);
            Assert.That(result.Model.Totals.DueTotal, Is.Zero);
            Assert.That(result.Model.Totals.DueDone, Is.Zero);
            Assert.That(result.Model.Totals.CompliancePct, Is.Null,
                "null, never 0 and never NaN, when nothing has fallen due");
        });
    }

    /// <summary>
    /// Prototype <c>:21</c> — "groups cases by property and counts done". Two properties,
    /// three rows: 1 done + 1 open on the first, 1 done on the second.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_GroupsCasesByPropertyAndCountsDone()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var a = await SeedSeries("Ejendom 1", "T", today.AddDays(-30));
        await SeedCalendarConfig(a.ArpId);
        await SeedCompliance(a.PlanningId, a.PropertyId, a.AreaId, today.AddDays(-1),
            await SeedSdkCase(100));
        await SeedCompliance(a.PlanningId, a.PropertyId, a.AreaId, today.AddDays(-1),
            await SeedSdkCase(50));

        var b = await SeedSeries("Ejendom 2", "T", today.AddDays(-30));
        await SeedCalendarConfig(b.ArpId);
        await SeedCompliance(b.PlanningId, b.PropertyId, b.AreaId, today.AddDays(-1),
            await SeedSdkCase(100));

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-30), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.Rows, Has.Count.EqualTo(2));

        var first = result.Model.Rows.Single(r => r.PropertyId == a.PropertyId);
        Assert.Multiple(() =>
        {
            Assert.That(first.PropertyName, Is.EqualTo("Ejendom 1"));
            Assert.That(first.Total, Is.EqualTo(2));
            Assert.That(first.Done, Is.EqualTo(1));
            Assert.That(first.DueTotal, Is.EqualTo(2));
            Assert.That(first.DueDone, Is.EqualTo(1));
            Assert.That(first.CompliancePct, Is.EqualTo(50));
        });

        var second = result.Model.Rows.Single(r => r.PropertyId == b.PropertyId);
        Assert.Multiple(() =>
        {
            Assert.That(second.Total, Is.EqualTo(1));
            Assert.That(second.Done, Is.EqualTo(1));
            Assert.That(second.CompliancePct, Is.EqualTo(100));
        });
    }

    /// <summary>
    /// Prototype <c>:35</c> — "a company with no cases produces no row". A property that
    /// exists, has a series and a board, but no compliance row in the window, is ABSENT —
    /// not a zeroed row.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_PropertyWithNoMatchingRows_ProducesNoRow()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var withRows = await SeedSeries("Ejendom 1", "T", today.AddDays(-30));
        await SeedCalendarConfig(withRows.ArpId);
        await SeedCompliance(withRows.PlanningId, withRows.PropertyId, withRows.AreaId,
            today.AddDays(-1), await SeedSdkCase(100));

        // Fully configured, but every compliance row falls OUTSIDE the requested window.
        var empty = await SeedSeries("Ejendom Tom", "T", today.AddDays(-400));
        await SeedCalendarConfig(empty.ArpId);
        await SeedBoard(empty.PropertyId, "Board");
        await SeedCompliance(empty.PlanningId, empty.PropertyId, empty.AreaId,
            today.AddDays(-365), await SeedSdkCase(50));

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-30), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.Rows, Has.Count.EqualTo(1));
        Assert.That(result.Model.Rows[0].PropertyId, Is.EqualTo(withRows.PropertyId));
        Assert.That(result.Model.Rows.Any(r => r.PropertyId == empty.PropertyId), Is.False,
            "a property with no matching rows must be absent, not a zeroed row");
    }

    /// <summary>
    /// The other half of "no row for a property with nothing to show", and the one the
    /// date-window test above cannot reach: a property whose rows ALL survive phase A —
    /// they are inside the window, on a property with no filter excluding it — and are
    /// dropped later, in phase C. It must produce NO row at all, not an all-zero one.
    ///
    /// <para>
    /// This is what pins the LAZY row creation in <c>Aggregate</c>. A regression that
    /// created the row eagerly, from the phase-A candidate list, would still count
    /// correctly for every other case in this fixture and would pass the whole suite.
    /// </para>
    ///
    /// <para>
    /// Two distinct phase-C drop reasons are used, so the test does not hang off one
    /// branch: an <c>IsDeleted</c> occurrence exception, and the user-deleted-occurrence
    /// rule (soft-removed AND not done).
    /// </para>
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_PropertyWhoseRowsAreAllDroppedInPhaseC_ProducesNoRow()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        // Property A — one ordinary open row, so the endpoint cannot pass this test by
        // returning nothing at all.
        var kept = await SeedSeries("Ejendom A", "T", today.AddDays(-60));
        await SeedCalendarConfig(kept.ArpId);
        await SeedCompliance(kept.PlanningId, kept.PropertyId, kept.AreaId,
            today.AddDays(-1), await SeedSdkCase(50));

        // Property B — every row passes phase A (Deadline inside the window, and either
        // non-removed or removed-with-a-case), and every row is dropped in phase C.
        var dropped = await SeedSeries("Ejendom B", "T", today.AddDays(-60));
        await SeedCalendarConfig(dropped.ArpId);

        // Drop reason 1 — occurrence exception with IsDeleted. The row itself is
        // WorkflowState Created, so phase A selects it unconditionally; phase C's
        // "a deleted occurrence is never returned, for ANY status" skip removes it.
        await SeedCompliance(dropped.PlanningId, dropped.PropertyId, dropped.AreaId,
            today.AddDays(-3), await SeedSdkCase(50));
        await SeedException(dropped.ArpId, today.AddDays(-3), isDeleted: true);

        // Drop reason 2 — user-deleted occurrence: soft-removed AND not done. It survives
        // phase A only because MicrotingSdkCaseId > 0 (the removed-but-deployed arm), and
        // is on a DIFFERENT date than the exception above so that the IsDeleted skip
        // cannot be what removes it.
        await SeedCompliance(dropped.PlanningId, dropped.PropertyId, dropped.AreaId,
            today.AddDays(-2), await SeedSdkCase(50), removed: true);

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-60), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.Rows, Has.Count.EqualTo(1));
        Assert.That(result.Model.Rows[0].PropertyId, Is.EqualTo(kept.PropertyId));
        Assert.That(result.Model.Rows.Any(r => r.PropertyId == dropped.PropertyId), Is.False,
            "a property whose every row is dropped in phase C must be ABSENT — rows are "
            + "created lazily on the first SURVIVING candidate, never from the phase-A list");

        Assert.Multiple(() =>
        {
            Assert.That(result.Model.Totals.Total, Is.EqualTo(1), "Totals reflect property A only");
            Assert.That(result.Model.Totals.Done, Is.Zero);
            Assert.That(result.Model.Totals.Overdue, Is.EqualTo(1));
            Assert.That(result.Model.Totals.DueTotal, Is.EqualTo(1));
            Assert.That(result.Model.Totals.DueDone, Is.Zero);
            Assert.That(result.Model.Totals.CompliancePct, Is.Zero);
        });
    }

    /// <summary>
    /// Prototype <c>:43</c> — "overdue counts only incomplete cases dated before today".
    /// Four rows on one property: not-done yesterday (OVERDUE), not-done tomorrow (not due
    /// at all), not-done TODAY (due, so it lowers the percentage, but NOT overdue), and
    /// done yesterday.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_OverdueCountsOnlyIncompleteCasesDatedBeforeToday()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var p = await SeedSeries("Ejendom 1", "T", today.AddDays(-30));
        await SeedCalendarConfig(p.ArpId);
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
            await SeedSdkCase(50));
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(1),
            await SeedSdkCase(50));
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today,
            await SeedSdkCase(50));
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
            await SeedSdkCase(100));

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-30), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model!.Rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Total, Is.EqualTo(4));
            Assert.That(row.Overdue, Is.EqualTo(1),
                "only the not-done row dated STRICTLY before today is overdue");
            Assert.That(row.DueTotal, Is.EqualTo(3),
                "yesterday x2 and today are due; tomorrow is not");
            Assert.That(row.DueDone, Is.EqualTo(1));
            Assert.That(row.Done, Is.EqualTo(1));
            // The not-done row dated TODAY is in the denominator (33 %) but not in Overdue.
            Assert.That(row.CompliancePct, Is.EqualTo(33));
        });
    }

    /// <summary>
    /// Prototype <c>:54</c> — "compliance ignores future tasks: 18 done, 0 overdue,
    /// 1 upcoming is 100 %". The future row raises <c>Total</c> but not <c>DueTotal</c>.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_FutureTasksIgnored_EighteenDoneAndOneUpcomingIsHundredPercent()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var p = await SeedSeries("Ejendom 7", "T", today.AddDays(-60));
        await SeedCalendarConfig(p.ArpId);
        var siteId = await SeedSdkSite("shared");

        for (var i = 0; i < 18; i++)
        {
            await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
                await SeedSdkCase(100, siteId));
        }
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(1),
            await SeedSdkCase(50, siteId));

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-60), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model!.Rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Total, Is.EqualTo(19), "the upcoming task still counts in Total");
            Assert.That(row.DueTotal, Is.EqualTo(18), "but not in the denominator");
            Assert.That(row.Overdue, Is.Zero);
            Assert.That(row.CompliancePct, Is.EqualTo(100));
        });
    }

    /// <summary>
    /// Prototype <c>:68</c> — "a future task lowers neither the row nor the totals
    /// percentage". Same shape as <c>:54</c> but asserted on <c>Totals</c> too.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_FutureTask_LowersNeitherRowNorTotalsPercentage()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var p = await SeedSeries("Ejendom 1", "T", today.AddDays(-60));
        await SeedCalendarConfig(p.ArpId);
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
            await SeedSdkCase(100));
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
            await SeedSdkCase(50));
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(1),
            await SeedSdkCase(50));
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(30),
            await SeedSdkCase(50));

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-60), today.AddDays(40)));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Model!.Rows.Single().CompliancePct, Is.EqualTo(50));
            Assert.That(result.Model.Totals.CompliancePct, Is.EqualTo(50));
            Assert.That(result.Model.Totals.Total, Is.EqualTo(4));
            Assert.That(result.Model.Totals.DueTotal, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// Prototype <c>:80</c> — "a company with only future tasks has no percentage yet".
    /// <c>DueTotal == 0</c> must give <c>null</c>, NOT 0: a red "0 %" for work that is
    /// simply not due yet would be a lie.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_OnlyFutureTasks_HasNullPercentageNotZero()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var p = await SeedSeries("Ejendom 5", "T", today.AddDays(-30));
        await SeedCalendarConfig(p.ArpId);
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(1),
            await SeedSdkCase(50));

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-30), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model!.Rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Total, Is.EqualTo(1));
            Assert.That(row.DueTotal, Is.Zero);
            Assert.That(row.Overdue, Is.Zero);
            Assert.That(row.CompliancePct, Is.Null);
            Assert.That(result.Model.Totals.CompliancePct, Is.Null);
        });
    }

    /// <summary>
    /// Prototype <c>:92</c> — "percent rounds half-down at 80.49 to 80". 33 done of 41 due
    /// is 80.4878…, which is 80 under BOTH rounding modes; this case guards the general
    /// path and deliberately does NOT discriminate between banker's and away-from-zero.
    /// The case that does is
    /// <see cref="ComplianceReportOverview_Rounding_OneOfEightDue_IsThirteenNotTwelve"/>.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_Rounding_ThirtyThreeOfFortyOneDue_IsEighty()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var p = await SeedSeries("Ejendom 8", "T", today.AddDays(-60));
        await SeedCalendarConfig(p.ArpId);
        var siteId = await SeedSdkSite("shared");

        for (var i = 0; i < 41; i++)
        {
            await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
                await SeedSdkCase(i < 33 ? 100 : 50, siteId));
        }

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-60), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model!.Rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.DueTotal, Is.EqualTo(41));
            Assert.That(row.DueDone, Is.EqualTo(33));
            Assert.That(row.CompliancePct, Is.EqualTo(80));
        });
    }

    /// <summary>
    /// Prototype <c>:101</c> — "total percent is weighted, not an average of row percents".
    /// One property at 1/1 (100 %) and one at 0/100 (0 %). The weighted answer is
    /// 1/101 → <b>1</b>. An average of the two row percentages would give 50 — wrong by a
    /// factor of 50, and the single most seductive way to get this endpoint wrong.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_TotalsAreWeighted_NotAnAverageOfRowPercentages()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var a = await SeedSeries("Ejendom 1", "T", today.AddDays(-60));
        await SeedCalendarConfig(a.ArpId);
        await SeedCompliance(a.PlanningId, a.PropertyId, a.AreaId, today.AddDays(-1),
            await SeedSdkCase(100));

        var b = await SeedSeries("Ejendom 2", "T", today.AddDays(-60));
        await SeedCalendarConfig(b.ArpId);
        var siteId = await SeedSdkSite("shared");
        for (var i = 0; i < 100; i++)
        {
            await SeedCompliance(b.PlanningId, b.PropertyId, b.AreaId, today.AddDays(-1),
                await SeedSdkCase(50, siteId));
        }

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-60), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Model!.Rows.Single(r => r.PropertyId == a.PropertyId).CompliancePct,
                Is.EqualTo(100));
            Assert.That(result.Model.Rows.Single(r => r.PropertyId == b.PropertyId).CompliancePct,
                Is.EqualTo(0), "0 %, not null — these ARE due, they are just not done");
            Assert.That(result.Model.Totals.DueDone, Is.EqualTo(1));
            Assert.That(result.Model.Totals.DueTotal, Is.EqualTo(101));
            Assert.That(result.Model.Totals.CompliancePct, Is.EqualTo(1),
                "weighted 1/101 → 1; an average of 100 and 0 would give 50");
        });
    }

    // ==================================================================
    // NEW — no prototype counterpart (#1162 §6 "Add these")
    // ==================================================================

    /// <summary>
    /// The rounding MIDPOINT the prototype cannot express, because JS has only one rounding
    /// mode. 1 done of 8 due is exactly 12.5: <c>Math.Round(12.5)</c> is <b>12</b>
    /// (banker's, C#'s default and WRONG here) and
    /// <c>Math.Round(12.5, MidpointRounding.AwayFromZero)</c> is <b>13</b> — which is what
    /// JS <c>Math.round(12.5)</c> gives. This test fails the instant somebody drops the
    /// <c>MidpointRounding</c> argument; the 33/41 test does not.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_Rounding_OneOfEightDue_IsThirteenNotTwelve()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var p = await SeedSeries("Ejendom Midpoint", "T", today.AddDays(-60));
        await SeedCalendarConfig(p.ArpId);
        var siteId = await SeedSdkSite("shared");

        for (var i = 0; i < 8; i++)
        {
            await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
                await SeedSdkCase(i < 1 ? 100 : 50, siteId));
        }

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-60), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model!.Rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.DueDone, Is.EqualTo(1));
            Assert.That(row.DueTotal, Is.EqualTo(8));
            Assert.That(row.CompliancePct, Is.EqualTo(13),
                "12.5 rounds AWAY FROM ZERO (JS Math.round), not to even");
            Assert.That(result.Model.Totals.CompliancePct, Is.EqualTo(13),
                "the totals row uses the same rounding");
        });
    }

    /// <summary>
    /// An unparseable <c>TaskDate</c> counts as DUE but is NOT overdue, and does not throw.
    ///
    /// <para>
    /// This is the prototype's NaN branch (<c>compliance-overview.js:15-20</c>, <c>:50</c>):
    /// <c>!(NaN &gt; today)</c> is true, so the row stays in the denominator rather than
    /// silently vanishing from it, while <c>NaN &lt; today</c> is false, so it is not
    /// overdue. The asymmetry is deliberate.
    /// </para>
    ///
    /// <para>
    /// It is asserted against the pure aggregation entry point rather than through the
    /// database, because the database CANNOT produce it: <c>Compliance.Deadline</c> is a
    /// non-nullable <c>DateTime</c>. Being honest about that is the point — the branch is
    /// unreachable from today's schema and exists so the semantics survive a future caller
    /// that feeds the aggregator from a looser source.
    /// </para>
    /// </summary>
    [Test]
    public void ComplianceReportOverview_UnparseableTaskDate_IsDueButNotOverdue()
    {
        var today = new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);

        var model = BackendConfigurationComplianceReportService.Aggregate(
        [
            new BackendConfigurationComplianceReportService.OverviewCandidate
            {
                PropertyId = 1, PropertyName = "Ejendom 1", TaskDate = "not-a-date", Completed = false
            },
            new BackendConfigurationComplianceReportService.OverviewCandidate
            {
                PropertyId = 1, PropertyName = "Ejendom 1", TaskDate = "2026-08-10", Completed = true
            }
        ], today);

        var row = model.Rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Total, Is.EqualTo(2));
            Assert.That(row.DueTotal, Is.EqualTo(2), "an unreadable date stays in the denominator");
            Assert.That(row.DueDone, Is.EqualTo(1));
            Assert.That(row.Overdue, Is.Zero, "but it is NOT overdue — NaN < today is false");
            Assert.That(row.CompliancePct, Is.EqualTo(50));
        });
    }

    /// <summary>
    /// Status is genuinely ignored: the request model has no <c>Status</c> at all, and the
    /// internal candidate builder really runs the <c>all</c> path. 3 done + 2 open on one
    /// property, all due, gives <c>DueTotal == 5</c> and <c>DueDone == 3</c> — if the
    /// builder had defaulted to <c>open</c> this would read 2/0, and to <c>done</c> 3/3.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_StatusIsIgnored_DoneAndOpenAreBothCounted()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var p = await SeedSeries("Ejendom Status", "T", today.AddDays(-60));
        await SeedCalendarConfig(p.ArpId);
        var siteId = await SeedSdkSite("shared");

        for (var i = 0; i < 3; i++)
        {
            await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
                await SeedSdkCase(100, siteId));
        }
        for (var i = 0; i < 2; i++)
        {
            await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
                await SeedSdkCase(50, siteId));
        }

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-60), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model!.Rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Total, Is.EqualTo(5));
            Assert.That(row.Done, Is.EqualTo(3));
            Assert.That(row.DueTotal, Is.EqualTo(5));
            Assert.That(row.DueDone, Is.EqualTo(3));
            Assert.That(row.Overdue, Is.EqualTo(2));
            Assert.That(row.CompliancePct, Is.EqualTo(60));
        });
    }

    /// <summary>
    /// A user-deleted occurrence — soft-removed AND not done — appears in NO counter:
    /// not <c>Total</c>, not <c>DueTotal</c>, not <c>Overdue</c>. Deleting an occurrence is
    /// not a compliance failure. Mirrors
    /// <c>CalendarComplianceReportTests.GetComplianceReport_UserDeletedRow_NeverReturnedForAnyStatus</c>.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_UserDeletedOccurrence_IsInNoCounter()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var p = await SeedSeries("Ejendom Deleted", "T", today.AddDays(-60));
        await SeedCalendarConfig(p.ArpId);
        var siteId = await SeedSdkSite("shared");

        // Kept: an ordinary open row, so the property still produces a row to assert on.
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
            await SeedSdkCase(50, siteId));
        // Dropped: soft-removed and NOT done. MicrotingSdkCaseId > 0 so it survives phase A
        // and is genuinely rejected by the phase-C rule rather than never being selected.
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
            await SeedSdkCase(50, siteId), removed: true);

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-60), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model!.Rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Total, Is.EqualTo(1), "the user-deleted occurrence is not in Total");
            Assert.That(row.DueTotal, Is.EqualTo(1), "nor in the denominator");
            Assert.That(row.Overdue, Is.EqualTo(1), "nor does it add a second overdue");
            Assert.That(row.CompliancePct, Is.Zero);
        });
    }

    /// <summary>
    /// The other half of the soft-removed asymmetry (#1161 §6): a COMPLETED occurrence is
    /// soft-removed but keeps its <c>MicrotingSdkCaseId</c>, and it DOES count — in
    /// <c>Total</c>, <c>Done</c>, <c>DueTotal</c> and <c>DueDone</c>.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_CompletedSoftRemovedOccurrence_IsCounted()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var p = await SeedSeries("Ejendom SoftRemovedDone", "T", today.AddDays(-60));
        await SeedCalendarConfig(p.ArpId);

        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
            await SeedSdkCase(100), removed: true);

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-60), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model!.Rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Total, Is.EqualTo(1));
            Assert.That(row.Done, Is.EqualTo(1));
            Assert.That(row.DueTotal, Is.EqualTo(1));
            Assert.That(row.DueDone, Is.EqualTo(1));
            Assert.That(row.Overdue, Is.Zero);
            Assert.That(row.CompliancePct, Is.EqualTo(100));
        });
    }

    /// <summary>
    /// The highest-value new case: <c>CalendarOccurrenceException</c> handling flows through
    /// the SHARED candidate builder into the aggregation. Two exceptions on one property —
    /// an <c>IsDeleted</c> that removes its row from every counter, and a <c>NewDate</c>
    /// that moves a row from yesterday to tomorrow, flipping it from overdue to not-yet-due.
    /// If the aggregation had its own copy of the candidate set, this is the test that
    /// catches it.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_OccurrenceExceptions_FlowThroughTheSharedBuilder()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var p = await SeedSeries("Ejendom Exceptions", "T", today.AddDays(-60));
        await SeedCalendarConfig(p.ArpId);
        var siteId = await SeedSdkSite("shared");

        // Kept as-is: not done, yesterday → overdue.
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
            await SeedSdkCase(50, siteId));
        // Deleted occurrence (dated 3 days ago) → in no counter at all.
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-3),
            await SeedSdkCase(50, siteId));
        await SeedException(p.ArpId, today.AddDays(-3), isDeleted: true);
        // Moved occurrence: originally 2 days ago (overdue), relocated to tomorrow →
        // neither overdue nor due.
        await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-2),
            await SeedSdkCase(50, siteId));
        await SeedException(p.ArpId, today.AddDays(-2), newDate: today.AddDays(1));

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-60), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        var row = result.Model!.Rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Total, Is.EqualTo(2), "the deleted occurrence is gone entirely");
            Assert.That(row.Overdue, Is.EqualTo(1),
                "the moved occurrence is no longer overdue — it is now dated tomorrow");
            Assert.That(row.DueTotal, Is.EqualTo(1), "and it is no longer due either");
            Assert.That(row.DueDone, Is.Zero);
            Assert.That(row.CompliancePct, Is.Zero);
        });
    }

    /// <summary>
    /// Filter parity with <see cref="BackendConfigurationComplianceReportService.Index"/>:
    /// for the same filters, <c>Overview</c>'s <c>Totals.Total</c> equals <c>Index</c>'s
    /// <c>Total</c> at <c>Status = "all"</c>. This is the single assertion guarding against
    /// the two paths drifting — the failure mode being a customer reporting that Oversigt
    /// says 84 % while Detaljer lists a different number of rows for the same filters.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_FilterParityWithIndex_TotalsTotalEqualsIndexTotal()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        var a = await SeedSeries("Ejendom A", "T", today.AddDays(-60));
        await SeedCalendarConfig(a.ArpId);
        var b = await SeedSeries("Ejendom B", "T", today.AddDays(-60));
        await SeedCalendarConfig(b.ArpId);
        var siteId = await SeedSdkSite("shared");

        // A spread that exercises every phase-C branch at once: done, open, future,
        // soft-removed-and-done (counted), soft-removed-and-open (dropped), deleted
        // occurrence (dropped) and a moved one.
        await SeedCompliance(a.PlanningId, a.PropertyId, a.AreaId, today.AddDays(-1),
            await SeedSdkCase(100, siteId));
        await SeedCompliance(a.PlanningId, a.PropertyId, a.AreaId, today.AddDays(-1),
            await SeedSdkCase(50, siteId));
        await SeedCompliance(a.PlanningId, a.PropertyId, a.AreaId, today.AddDays(2),
            await SeedSdkCase(50, siteId));
        await SeedCompliance(a.PlanningId, a.PropertyId, a.AreaId, today.AddDays(-4),
            await SeedSdkCase(100, siteId), removed: true);
        await SeedCompliance(a.PlanningId, a.PropertyId, a.AreaId, today.AddDays(-5),
            await SeedSdkCase(50, siteId), removed: true);
        await SeedCompliance(a.PlanningId, a.PropertyId, a.AreaId, today.AddDays(-6),
            await SeedSdkCase(50, siteId));
        await SeedException(a.ArpId, today.AddDays(-6), isDeleted: true);
        await SeedCompliance(a.PlanningId, a.PropertyId, a.AreaId, today.AddDays(-7),
            await SeedSdkCase(50, siteId));
        await SeedException(a.ArpId, today.AddDays(-7), newDate: today.AddDays(3));
        await SeedCompliance(b.PlanningId, b.PropertyId, b.AreaId, today.AddDays(-1),
            await SeedSdkCase(50, siteId));

        var service = BuildService(core);
        var from = today.AddDays(-60);
        var to = today.AddDays(30);

        var overview = await service.Overview(Request(from, to));
        var index = await service.Index(new ComplianceReportRequestModel
        {
            DateFrom = from, DateTo = to, Status = "all",
            BoardIds = [], TagIds = [], SiteIds = [],
            PageIndex = 0, PageSize = 0
        });

        Assert.That(overview.Success, Is.True, overview.Message);
        Assert.That(index.Success, Is.True, index.Message);
        Assert.Multiple(() =>
        {
            Assert.That(overview.Model!.Totals.Total, Is.EqualTo(index.Model!.Total),
                "Overview and Index must count the same rows for the same filters");
            Assert.That(overview.Model.Totals.Done,
                Is.EqualTo(index.Model.Entities.Count(e => e.Completed)));
            Assert.That(overview.Model.Rows.Sum(r => r.Total), Is.EqualTo(index.Model.Total),
                "and the per-property rows must partition that same set");
        });
    }

    /// <summary>
    /// The board, tag and site filters apply. A silently-ignored filter here would be
    /// invisible on screen — the percentage would just be quietly computed over the wrong
    /// set — so each gets an assertion.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_BoardTagAndSiteFilters_Apply()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        // Property A: board X, tag T, site S — the row that must survive every filter.
        var a = await SeedSeries("Ejendom A", "T", today.AddDays(-60));
        var boardA = await SeedBoard(a.PropertyId, "Board A");
        await SeedCalendarConfig(a.ArpId, boardA);
        var tagA = await SeedTag("Tag A");
        await SeedArpTag(a.ArpId, tagA);
        var siteA = await SeedSdkSite("Site A");
        await SeedPlanningSite(a.ArpId, siteA, a.AreaId, a.AreaRuleId);
        await SeedCompliance(a.PlanningId, a.PropertyId, a.AreaId, today.AddDays(-1),
            await SeedSdkCase(100, siteA));

        // Property B: a different board, tag and site — filtered out by each in turn.
        var b = await SeedSeries("Ejendom B", "T", today.AddDays(-60));
        var boardB = await SeedBoard(b.PropertyId, "Board B");
        await SeedCalendarConfig(b.ArpId, boardB);
        var tagB = await SeedTag("Tag B");
        await SeedArpTag(b.ArpId, tagB);
        var siteB = await SeedSdkSite("Site B");
        await SeedPlanningSite(b.ArpId, siteB, b.AreaId, b.AreaRuleId);
        await SeedCompliance(b.PlanningId, b.PropertyId, b.AreaId, today.AddDays(-1),
            await SeedSdkCase(50, siteB));

        var service = BuildService(core);
        var from = today.AddDays(-60);
        var to = today.AddDays(30);

        var unfiltered = await service.Overview(Request(from, to));
        Assert.That(unfiltered.Success, Is.True, unfiltered.Message);
        Assert.That(unfiltered.Model!.Rows, Has.Count.EqualTo(2), "baseline: both properties");

        var byBoard = await service.Overview(Request(from, to, boardIds: [boardA]));
        Assert.That(byBoard.Success, Is.True, byBoard.Message);
        Assert.Multiple(() =>
        {
            Assert.That(byBoard.Model!.Rows, Has.Count.EqualTo(1));
            Assert.That(byBoard.Model.Rows[0].PropertyId, Is.EqualTo(a.PropertyId));
            Assert.That(byBoard.Model.Totals.Total, Is.EqualTo(1));
        });

        var byTag = await service.Overview(Request(from, to, tagIds: [tagA]));
        Assert.That(byTag.Success, Is.True, byTag.Message);
        Assert.Multiple(() =>
        {
            Assert.That(byTag.Model!.Rows, Has.Count.EqualTo(1));
            Assert.That(byTag.Model.Rows[0].PropertyId, Is.EqualTo(a.PropertyId));
        });

        var bySite = await service.Overview(Request(from, to, siteIds: [siteA]));
        Assert.That(bySite.Success, Is.True, bySite.Message);
        Assert.Multiple(() =>
        {
            Assert.That(bySite.Model!.Rows, Has.Count.EqualTo(1));
            Assert.That(bySite.Model.Rows[0].PropertyId, Is.EqualTo(a.PropertyId));
        });

        var byProperty = await service.Overview(Request(from, to, propertyId: b.PropertyId));
        Assert.That(byProperty.Success, Is.True, byProperty.Message);
        Assert.Multiple(() =>
        {
            Assert.That(byProperty.Model!.Rows, Has.Count.EqualTo(1));
            Assert.That(byProperty.Model.Rows[0].PropertyId, Is.EqualTo(b.PropertyId));
            Assert.That(byProperty.Model.Rows[0].CompliancePct, Is.Zero);
        });
    }

    /// <summary>
    /// The documented default order: <c>PropertyName</c> ascending, ORDINAL. Ordinal, not
    /// culture-aware — "Zulu" sorts before "alpha" because uppercase letters precede
    /// lowercase ones in the code-point order. #1164 re-sorts client-side; this order exists
    /// so the response is reproducible in CI.
    /// </summary>
    [Test]
    public async Task ComplianceReportOverview_RowsAreOrderedByPropertyNameOrdinalAscending()
    {
        var core = await GetCore();
        var today = DateTime.UtcNow.Date;

        foreach (var name in new[] { "alpha", "Zulu", "Alpha" })
        {
            var p = await SeedSeries(name, "T", today.AddDays(-60));
            await SeedCalendarConfig(p.ArpId);
            await SeedCompliance(p.PlanningId, p.PropertyId, p.AreaId, today.AddDays(-1),
                await SeedSdkCase(100));
        }

        var service = BuildService(core);
        var result = await service.Overview(Request(today.AddDays(-60), today.AddDays(30)));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Model!.Rows.Select(r => r.PropertyName).ToList(),
            Is.EqualTo(new List<string> { "Alpha", "Zulu", "alpha" }),
            "ordinal: 'Zulu' precedes 'alpha'");
        Assert.Multiple(() =>
        {
            Assert.That(result.Model.Totals.PropertyId, Is.Zero, "the totals row carries no property");
            Assert.That(result.Model.Totals.PropertyName, Is.Null,
                "and no label — #1164 supplies 'I alt'; the API carries no Danish");
        });
    }
}
