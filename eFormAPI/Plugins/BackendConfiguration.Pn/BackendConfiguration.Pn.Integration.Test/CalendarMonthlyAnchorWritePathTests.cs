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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using NSubstitute;
using ItemsPlanningRepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType;
using IpPlanningSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite;
using SdkCase = Microting.eForm.Infrastructure.Data.Entities.Case;
using SdkSite = Microting.eForm.Infrastructure.Data.Entities.Site;
using TaskWizardSvc =
    BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService.BackendConfigurationTaskWizardService;
using WizardRepeatType = BackendConfiguration.Pn.Infrastructure.Enums.RepeatType;

/// <summary>
/// #1207 through the two WRITE paths, end to end against a real database.
///
/// The sibling fixtures cover the read side: <c>CalendarMonthlyAnchorOccurrenceTests</c>
/// pins the enumerators (some of it by reflection on private statics) and
/// <c>CalendarMonthlyAnchorRenderTests</c> pins <c>GetTasksForWeek</c>. Neither
/// reproduces the two highest-risk behaviours of the change:
///
/// <list type="number">
/// <item><b>The two-tile relocation regression.</b> Making the enumerators
/// anchor-aware without making <c>NewPatternDateForPeriodOf</c> anchor-aware
/// puts a deployed Compliance row on a day the renderer no longer paints, and
/// the start month ends up carrying TWO tiles that share one
/// <c>CompletedPeriodKey</c> bucket. Today that is pinned only by a reflection
/// test on the pure mapper; here it is driven through the real
/// <c>UpdateTask</c> scope="all" path and read back through the real week
/// query.</item>
/// <item><b>A past-dated anchor surfaces but does not back-deploy.</b> #1207
/// claims a re-anchored past occurrence "renders as a recurrence-only row with
/// no SDK case"; that claim was reasoned from
/// <c>EventDeployService.cs:194</c>, never executed.</item>
/// </list>
///
/// Both need collaborators the read-only fixtures do not build (a task-wizard
/// stub that actually persists the anchor; a real <c>EventDeployService</c>),
/// which is why they live in their own fixture rather than in
/// <c>CalendarMonthlyAnchorRenderTests</c>.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarMonthlyAnchorWritePathTests : TestBaseSetup
{
    private const string NoCandidatesLogFragment = "no future-day recurrence rows to deploy";

    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string DateKey(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The first <paramref name="dayOfWeek"/> of the month
    /// <paramref name="month"/> is in. Computed here rather than reused from the
    /// service so the tests do not verify the enumerator against itself
    /// (same rationale as <c>CalendarCompletedPeriodSuppressionTests</c>).
    /// </summary>
    private static DateTime FirstWeekdayOfMonth(DateTime month, DayOfWeek dayOfWeek)
    {
        var first = new DateTime(month.Year, month.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return first.AddDays(((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7);
    }

    /// <summary>Monday of the Mon-Sun week containing <paramref name="d"/>.</summary>
    private static DateTime MondayOf(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private static DateTime FirstOfMonthUtc(DateTime d) =>
        new(d.Year, d.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed record SeededSeries(int PropertyId, int AreaId, int ArpId, int PlanningId);

    // ─────────────────────────────────────────────────────────────────────────
    // Seeding
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A monthly "Nth &lt;weekday&gt; of the month" calendar series, persisted
    /// exactly as the custom recurrence dialog does: RepeatOrdinalWeek +
    /// DayOfWeek on the ARP, DayOfMonth = 0, and <c>planning.DayOfMonth</c>
    /// derived the way the wizard derives it.
    /// </summary>
    private async Task<SeededSeries> SeedMonthlyOrdinalSeries(
        string namePrefix, DateTime startDate, int repeatEvery, int repeatOrdinalWeek, int dayOfWeek, int eformId)
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
            Name = $"{namePrefix}-{Guid.NewGuid()}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var areaRule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = eformId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = repeatEvery,
            RepeatType = ItemsPlanningRepeatType.Month,
            StartDate = startDate,
            // What BackendConfigurationTaskWizardService.DeriveDayOfMonth writes
            // for a Month rule (capped at 28).
            DayOfMonth = Math.Min(startDate.Day, 28),
            DayOfWeek = startDate.DayOfWeek,
            RepeatOrdinalWeek = repeatOrdinalWeek,
            RelatedEFormId = eformId,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = 3, RepeatEvery = repeatEvery,
            RepeatOrdinalWeek = repeatOrdinalWeek, DayOfWeek = dayOfWeek, DayOfMonth = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 9.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new SeededSeries(property.Id, area.Id, arp.Id, planning.Id);
    }

    private async Task<SdkSite> SeedSdkSite(string name, int microtingUid)
    {
        var language = await MicrotingDbContext!.Languages.OrderBy(x => x.Id).FirstAsync();
        var site = new SdkSite
        {
            Name = name, MicrotingUid = microtingUid, LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(site);
        await MicrotingDbContext.SaveChangesAsync();
        return site;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Collaborators
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A task-wizard substitute that actually PERSISTS what the real wizard
    /// persists for the "still active" update branch
    /// (<c>BackendConfigurationTaskWizardService.cs:878</c> and
    /// <c>:1143-1150</c>): the ARP anchor, then <c>planning.StartDate</c>,
    /// <c>planning.DayOfMonth</c> (via the real <c>DeriveDayOfMonth</c>) and
    /// <c>planning.DayOfWeek</c>.
    ///
    /// This matters for #1207. <c>NewPatternDateForPeriodOf</c> reads
    /// <c>planning.StartDate</c> to decide whether a row belongs to the START
    /// month, and <c>UpdateTask</c> re-reads the Planning after calling the
    /// wizard — so a wizard that writes nothing (the substitute the neighbouring
    /// fixtures use, deliberately, for a different invariant) would leave the
    /// relocation looking at the PRE-edit anchor and this test would prove
    /// nothing about production. Writing the same fields the real wizard writes
    /// is what makes the scope="all" path here the production path.
    /// </summary>
    private IBackendConfigurationTaskWizardService BuildPersistingWizardStub()
    {
        var wizard = Substitute.For<IBackendConfigurationTaskWizardService>();
        wizard.DeleteTask(Arg.Any<int>()).Returns(Task.FromResult(new OperationResult(true)));
        wizard.UpdateTask(Arg.Any<TaskWizardCreateModel>())
            .Returns(callInfo => ApplyWizardWritesAsync(callInfo.Arg<TaskWizardCreateModel>()));
        return wizard;
    }

    private async Task<OperationResult> ApplyWizardWritesAsync(TaskWizardCreateModel model)
    {
        var arp = await BackendConfigurationPnDbContext!.AreaRulePlannings
            .FirstAsync(x => x.Id == model.Id);
        arp.StartDate = model.StartDate;
        arp.RepeatType = (int?)model.RepeatType;
        arp.RepeatEvery = model.RepeatEvery;
        arp.UpdatedByUserId = 1;
        await arp.Update(BackendConfigurationPnDbContext);

        var planning = await ItemsPlanningPnDbContext!.Plannings
            .FirstAsync(x => x.Id == arp.ItemPlanningId);
        planning.StartDate = new DateTime(
            model.StartDate!.Value.Year, model.StartDate.Value.Month, model.StartDate.Value.Day,
            0, 0, 0, DateTimeKind.Utc);
        planning.DayOfMonth = TaskWizardSvc.DeriveDayOfMonth(model.RepeatType, planning.StartDate);
        planning.DayOfWeek = planning.StartDate.DayOfWeek;
        planning.RepeatType = (ItemsPlanningRepeatType)(int)model.RepeatType;
        planning.RepeatEvery = model.RepeatEvery;
        planning.UpdatedByUserId = 1;
        await planning.Update(ItemsPlanningPnDbContext);

        return new OperationResult(true);
    }

    private BackendConfigurationCalendarService BuildCalendarService(
        eFormCore.Core core, IBackendConfigurationTaskWizardService? wizard = null)
    {
        var language = MicrotingDbContext!.Languages.OrderBy(x => x.Id).First();
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return new BackendConfigurationCalendarService(
            new BackendConfigurationLocalizationService(), userService,
            BackendConfigurationPnDbContext!, coreHelper, Substitute.For<IEventDeployService>(),
            ItemsPlanningPnDbContext!, wizard ?? Substitute.For<IBackendConfigurationTaskWizardService>(),
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            Substitute.For<IBackendConfigurationComplianceReportService>(),
            new WorkerTagMembershipService(coreHelper));
    }

    private async Task<List<CalendarTaskResponseModel>> QueryWeekRows(
        int propertyId, int arpId, DateTime weekStartMonday)
    {
        var core = await GetCore();
        var svc = BuildCalendarService(core);
        var res = await svc.GetTasksForWeek(new CalendarTaskRequestModel
        {
            PropertyId = propertyId,
            WeekStart = IsoUtc(weekStartMonday),
            WeekEnd = IsoUtc(weekStartMonday.AddDays(6).AddHours(23).AddMinutes(59)),
            ActionableOnly = false, BoardIds = [], TagNames = [], SiteIds = []
        });
        Assert.That(res.Success, Is.True, res.Message);
        return res.Model!.Where(t => t.Id == arpId).ToList();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // A. The two-tile relocation regression, through the real write path
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// #1207's late-caught defect, reproduced end to end.
    ///
    /// A monthly "1st Tuesday" series with a DEPLOYED, not-completed Compliance
    /// row on the start month's pattern date. The user opens that occurrence and
    /// drags it a week forward with scope "all", so the series anchor becomes
    /// the SECOND Tuesday of that month while the rule stays "1st Tuesday" — the
    /// mismatched-anchor shape #1207 is about.
    ///
    /// Because the new anchor is in the future and stays inside the same
    /// calendar month, <c>UpdateTask</c> takes the non-destructive #960 branch:
    /// <c>IsSameRecurrencePeriod</c> answers true and
    /// <c>RelocateNonCompletedComplianceRowsToNewPattern</c> moves the deployed
    /// row onto <c>NewPatternDateForPeriodOf</c>'s answer for its period.
    ///
    /// The invariant: that answer must be the ANCHOR, because the anchor is what
    /// the renderer now paints for the start month. Three ways this can be wrong,
    /// each caught by a different assertion below:
    ///
    /// <list type="bullet">
    /// <item><b>Pre-#1207</b> — the enumerators drop the start month entirely and
    /// the mapper returns the pure pattern date, so the relocation no-ops and the
    /// anchor week renders NOTHING. The count assertion is red (0, not 1) and the
    /// Deadline assertion is red (still the 1st Tuesday).</item>
    /// <item><b>Enumerators fixed, mapper NOT</b> — the anchor week renders a
    /// recurrence tile while the Compliance row still sits on the old pattern
    /// date a week earlier: TWO tiles in one calendar month, both bucketed
    /// "M:yyyy-MM" by <c>CompletedPeriodKey</c>, so completing either silently
    /// suppresses the other. The Deadline assertion and the old-pattern-week
    /// assertion are both red.</item>
    /// <item><b>Shipped</b> — one row, on the anchor, backed by the relocated
    /// Compliance; the old pattern week is empty.</item>
    /// </list>
    ///
    /// Dates are derived from <c>DateTime.UtcNow</c>, not hard-coded: the
    /// relocate branch is gated on <c>!anchorIsInThePast</c>
    /// (<c>BackendConfigurationCalendarService.cs:1724</c>, the #1122 §3
    /// gate), so a fixed future date would silently switch this test onto the
    /// destructive retract branch once that date passed, and it would then
    /// assert nothing about relocation. The month is two ahead so both
    /// Tuesdays are comfortably in the future on any run day.
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeAll_MonthlyAnchorMovedInsideStartMonth_RelocatesComplianceOntoTheAnchor()
    {
        var core = await GetCore();
        var sdkSite = await SeedSdkSite($"monthly-anchor-relocate-{Guid.NewGuid()}", 9101);

        // Deployed but NOT completed (Status != 100) — the relocatable shape.
        var openCase = new SdkCase
        {
            SiteId = sdkSite.Id, Status = 66, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext!.Cases.AddAsync(openCase);
        await MicrotingDbContext.SaveChangesAsync();

        // Two months ahead, so both Tuesdays are in the future on any run day.
        var targetMonth = FirstOfMonthUtc(DateTime.UtcNow).AddMonths(2);
        var firstTuesday = FirstWeekdayOfMonth(targetMonth, DayOfWeek.Tuesday);
        var secondTuesday = firstTuesday.AddDays(7);

        // The series as created: anchored ON its own rule ("1st Tuesday"), so
        // nothing about the seeded state depends on #1207.
        var seeded = await SeedMonthlyOrdinalSeries(
            "MonthlyAnchorRelocate", firstTuesday,
            repeatEvery: 1, repeatOrdinalWeek: 1, dayOfWeek: (int)DayOfWeek.Tuesday, eformId: 0);

        // The deployed occurrence for the start month, on the pattern date.
        var deployedRow = new Compliance
        {
            PlanningId = seeded.PlanningId, PropertyId = seeded.PropertyId, AreaId = seeded.AreaId,
            Deadline = firstTuesday, StartDate = firstTuesday.AddDays(-30),
            MicrotingSdkCaseId = openCase.Id, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(deployedRow);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        var deployedRowId = deployedRow.Id;

        // Sanity: before the edit the series renders on the 1st Tuesday and
        // nowhere else that month. Anchoring the assertions below.
        var beforeEdit = await QueryWeekRows(seeded.PropertyId, seeded.ArpId, MondayOf(firstTuesday));
        Assert.That(beforeEdit.Select(t => t.TaskDate), Is.EqualTo(new[] { DateKey(firstTuesday) }),
            "pre-condition: the un-edited series renders its (matching) anchor exactly once");

        // The edit: scope "all", drag the 1st-Tuesday occurrence to the 2nd
        // Tuesday. The rule itself is untouched (still RepeatOrdinalWeek = 1,
        // Tuesday) — only the anchor moves, which is precisely how a mismatched
        // anchor is created in production.
        var service = BuildCalendarService(core, BuildPersistingWizardStub());
        var result = await service.UpdateTask(new CalendarTaskUpdateRequestModel
        {
            Id = seeded.ArpId,
            Scope = "all",
            OriginalDate = IsoUtc(firstTuesday),
            StartDate = secondTuesday,
            StartHour = 9.0,
            Duration = 1.0,
            Status = 1,
            RepeatType = 3,
            RepeatEvery = 1,
            RepeatOrdinalWeek = 1,
            DayOfMonth = null,
            ComplianceEnabled = false,
            PropertyId = seeded.PropertyId,
            EformId = 0,
            Sites = [sdkSite.Id],
            TagIds = [],
            Translates = []
        });
        Assert.That(result.Success, Is.True, result.Message);

        // 1. The deployed row moved onto the ANCHOR, not onto the pure pattern
        //    date. Pre-#1207 the mapper returned firstTuesday, the relocation
        //    no-opped, and this row stayed put.
        var reloaded = await BackendConfigurationPnDbContext.Compliances.AsNoTracking()
            .FirstAsync(c => c.Id == deployedRowId);
        Assert.That(reloaded.Deadline.Date, Is.EqualTo(secondTuesday.Date),
            "the start month's occurrence IS the anchor since #1207 — the deployed row must follow it");

        // 2. The anchor week renders EXACTLY ONE row, on the anchor. Not two
        //    (the mapper-not-fixed shape), not zero (the pre-#1207 shape).
        var anchorWeek = await QueryWeekRows(seeded.PropertyId, seeded.ArpId, MondayOf(secondTuesday));
        Assert.That(anchorWeek, Has.Count.EqualTo(1),
            "one tile on the anchor — a recurrence tile stacked on a stale compliance tile would be two");
        Assert.That(anchorWeek[0].TaskDate, Is.EqualTo(DateKey(secondTuesday)));
        Assert.That(anchorWeek[0].IsFromCompliance, Is.True,
            "the surviving tile is the relocated Compliance row, not a fresh recurrence-only row");
        Assert.That(anchorWeek[0].ComplianceId, Is.EqualTo(deployedRowId),
            "and it is the SAME deployed row — relocated, not re-created alongside the old one");

        // 3. The old pattern date is empty: no orphan tile left behind a week
        //    earlier. Pre-#1207 this week still held the un-relocated row.
        var oldPatternWeek = await QueryWeekRows(seeded.PropertyId, seeded.ArpId, MondayOf(firstTuesday));
        Assert.That(oldPatternWeek, Is.Empty,
            "the pre-edit pattern date must not keep a tile once the anchor moved");

        // 4. Whole-month tally, which is the property CompletedPeriodKey needs:
        //    exactly ONE occurrence in the start month across both weeks.
        Assert.That(anchorWeek.Count + oldPatternWeek.Count, Is.EqualTo(1),
            "a Month rule maps its whole month to one 'M:yyyy-MM' bucket — two tiles there would mean "
            + "completing one silently suppresses the other");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // B. A past-dated anchor surfaces but is never back-deployed
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// #1207's data-migration claim, executed rather than reasoned.
    ///
    /// Every existing monthly series whose start date does not satisfy its own
    /// rule gained an occurrence on that start date the moment #1207 shipped —
    /// retroactively, with no migration and no opt-in. For a series anchored in
    /// the PAST the issue claims the new occurrence "renders as a recurrence-only
    /// row with no SDK case and paints as overdue. It does NOT create a
    /// Compliance row by itself", citing
    /// <c>EventDeployService.cs:194</c> (<c>RotationDate &gt;= todayUtc</c>).
    ///
    /// Both halves are asserted here against a real <c>EventDeployService</c>
    /// wired to the real <c>GetTasksForWeek</c> — the exact composition
    /// production uses (<c>EnsureDeployedAsync</c> resolves
    /// <c>IBackendConfigurationCalendarService</c> out of the container).
    ///
    /// Honest scope, stated because it changes what this test is worth:
    /// <list type="bullet">
    /// <item>The RENDER half is a genuine #1207 regression assertion — pre-fix
    /// the past anchor week was empty.</item>
    /// <item>The NO-BACK-DEPLOY half is a claim verification, not a regression
    /// test. Pre-#1207 it passed vacuously (there was no row to deploy). It
    /// becomes meaningful only now that a row exists, and it is the assertion
    /// that would catch someone "helpfully" removing the
    /// <c>RotationDate &gt;= todayUtc</c> filter and thereby mass-deploying
    /// historical occurrences to every worker's device on upgrade.</item>
    /// </list>
    ///
    /// The control pass at the end rules out the boring explanation: it proves
    /// the same series, the same site and the same eForm DO get past every other
    /// candidate filter in a FUTURE week, so the past row was dropped by the date
    /// gate and not by the site-narrowing or eForm filters.
    /// </summary>
    [Test]
    public async Task EnsureDeployedAsync_PastDatedAnchor_RendersButIsNeverBackDeployed()
    {
        var core = await GetCore();
        var sdkSite = await SeedSdkSite($"monthly-anchor-pastdeploy-{Guid.NewGuid()}", 9102);

        // Two months back, so the anchor is unambiguously past on any run day.
        var pastMonth = FirstOfMonthUtc(DateTime.UtcNow).AddMonths(-2);
        var pastFirstTuesday = FirstWeekdayOfMonth(pastMonth, DayOfWeek.Tuesday);
        var pastAnchor = pastFirstTuesday.AddDays(7); // the 2nd Tuesday — mismatched
        Assert.That(pastAnchor, Is.LessThan(DateTime.UtcNow.Date),
            "pre-condition: the anchor must be in the past for this test to mean anything");

        // A non-existent eForm id: > 0 so the candidate filter accepts the row,
        // and unresolvable so the control pass below cannot actually create a
        // case (ReadeForm returns null; the per-rotation catch logs and moves on).
        const int bogusEformId = 999_999;
        var seeded = await SeedMonthlyOrdinalSeries(
            "MonthlyAnchorPastDeploy", pastAnchor,
            repeatEvery: 1, repeatOrdinalWeek: 1, dayOfWeek: (int)DayOfWeek.Tuesday, eformId: bogusEformId);

        // The deploy pass narrows candidates to plannings the calling site is
        // assigned to (items-planning PlanningSites, #935 defect A).
        await ItemsPlanningPnDbContext!.PlanningSites.AddAsync(new IpPlanningSite
        {
            PlanningId = seeded.PlanningId, SiteId = sdkSite.Id,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // --- Half 1: the past anchor RENDERS, as a recurrence-only row.
        var anchorWeekMonday = MondayOf(pastAnchor);
        var anchorWeek = await QueryWeekRows(seeded.PropertyId, seeded.ArpId, anchorWeekMonday);
        Assert.That(anchorWeek.Select(t => t.TaskDate), Is.EqualTo(new[] { DateKey(pastAnchor) }),
            "#1207: a past-dated mismatched anchor now surfaces in the week grid");
        Assert.That(anchorWeek[0].IsFromCompliance, Is.False,
            "it is a recurrence-only row — nothing has ever been deployed for it");
        Assert.That(anchorWeek[0].SdkCaseId.GetValueOrDefault(), Is.EqualTo(0),
            "and it has no backing SDK case");
        Assert.That(anchorWeek[0].TaskIsExpired, Is.True,
            "a past occurrence with no case paints as overdue");

        // --- Half 2: the deploy pass refuses to back-deploy it.
        var pastLogger = new CapturingLogger<EventDeployService>();
        var pastDeploy = BuildDeployService(core, pastLogger);
        await pastDeploy.EnsureDeployedAsync(
            seeded.PropertyId.ToString(CultureInfo.InvariantCulture),
            [],
            DateKey(anchorWeekMonday),
            DateKey(anchorWeekMonday.AddDays(6)),
            sdkSite.Id,
            CancellationToken.None);

        var complianceCount = await BackendConfigurationPnDbContext!.Compliances
            .CountAsync(c => c.PlanningId == seeded.PlanningId);
        Assert.That(complianceCount, Is.EqualTo(0),
            "EventDeployService.cs's RotationDate >= todayUtc filter must never back-deploy a past anchor");

        var planningCaseCount = await ItemsPlanningPnDbContext.PlanningCases
            .CountAsync(pc => pc.PlanningId == seeded.PlanningId);
        Assert.That(planningCaseCount, Is.EqualTo(0),
            "and it must be dropped at the candidate filter, before any PlanningCase is written");

        Assert.That(pastLogger.Messages.Any(m => m.Contains(NoCandidatesLogFragment)), Is.True,
            "the pass must reach the candidate filter and find nothing deployable there");

        // --- Control: the SAME series, site and eForm in a FUTURE week are NOT
        //     dropped by the candidate filter. Without this, half 2 could be
        //     passing because the site-narrowing or eForm filter ate the row.
        var futureMonth = FirstOfMonthUtc(DateTime.UtcNow).AddMonths(2);
        var futureOccurrence = FirstWeekdayOfMonth(futureMonth, DayOfWeek.Tuesday);
        var futureWeekMonday = MondayOf(futureOccurrence);

        var futureRender = await QueryWeekRows(seeded.PropertyId, seeded.ArpId, futureWeekMonday);
        Assert.That(futureRender.Select(t => t.TaskDate), Is.EqualTo(new[] { DateKey(futureOccurrence) }),
            "control pre-condition: the same series has a future patterned occurrence");

        var futureLogger = new CapturingLogger<EventDeployService>();
        var futureDeploy = BuildDeployService(core, futureLogger);
        await futureDeploy.EnsureDeployedAsync(
            seeded.PropertyId.ToString(CultureInfo.InvariantCulture),
            [],
            DateKey(futureWeekMonday),
            DateKey(futureWeekMonday.AddDays(6)),
            sdkSite.Id,
            CancellationToken.None);

        Assert.That(futureLogger.Messages.Any(m => m.Contains(NoCandidatesLogFragment)), Is.False,
            "the future occurrence survives the candidate filter — so the past one was dropped by the "
            + "date gate, not by the site/eForm narrowing");
    }

    private EventDeployService BuildDeployService(eFormCore.Core core, ILogger<EventDeployService> logger)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));

        // EnsureDeployedAsync resolves the calendar service out of the container
        // and consumes its GetTasksForWeek output verbatim, so registering the
        // REAL service is what makes this an end-to-end assertion about #1207's
        // render path rather than about a hand-written rotation list.
        var services = new ServiceCollection();
        services.AddSingleton<IBackendConfigurationCalendarService>(BuildCalendarService(core));
        var provider = services.BuildServiceProvider();

        return new EventDeployService(
            BackendConfigurationPnDbContext!,
            ItemsPlanningPnDbContext!,
            coreHelper,
            provider,
            logger);
    }

    /// <summary>
    /// Captures every log line so the tests can assert on the deploy pass's own
    /// "nothing to deploy" marker. Same shape as the private logger inside
    /// <c>EventDeployServiceTest</c>, which is not visible from here.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages => _messages;

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }
    }
}
