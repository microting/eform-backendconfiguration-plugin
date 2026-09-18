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

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System.Globalization;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1289 — changing a task's start date (the task-list batch action "Skift
/// startdato") must re-derive the "Nth weekday of the month" ORDINAL from the new
/// date, not only the weekday.
///
/// The customer: a task created Thursday 10 Sept, "monthly on the 2nd Thursday",
/// re-anchored to Monday 7 Sept became "monthly on the 2nd Monday" although 7
/// Sept is the FIRST Monday — and the overdue backfill then put the red task on
/// Mon 14 Sept (or nowhere, before the 15th) instead of on 7 Sept.
///
/// Three layers, one per section below:
///  A. ORDINAL DERIVATION through the task-list service (calendar service
///     stubbed, the request it would receive is captured): the full boundary
///     matrix — every occurrence 1st..5th from source ordinals 1, 2 and 5, every
///     week boundary 7/8, 14/15, 21/22, 28/29 and the 29th–31st, plus the rule
///     kinds that must keep a NULL ordinal.
///  B. END TO END through the REAL calendar service (real retraction + real
///     backfill; only cloud-facing collaborators substituted): the customer
///     case, keep-both, compliance ON/OFF, anchor past/today/future, same
///     weekday other week, same week other weekday, RepeatEvery = 2 and the
///     5th-weekday fallback — each asserting that the PREVIEW's counts equal
///     what the APPLY actually did.
///  C. Direct API callers of UpdateTask ("all" / "thisAndFollowing") are
///     re-derived server-side, and a date-UNCHANGED edit (which is what every
///     other batch action sends, with a synthetic anchor of arbitrary ordinal)
///     is not.
///
/// EXPECTATIONS ARE COMPUTED BY BRUTE FORCE, never with the production formula:
/// <see cref="CountOfWeekdayUpTo"/> literally counts the same-weekday days of
/// the month up to and including the date, and <see cref="LastWeekdayOfMonth"/>
/// walks back from the month's end. So a wrong <c>OrdinalWeekOf</c> cannot make
/// its own test pass.
///
/// TODAY IS THE REAL CLOCK. The backfill and the #1122 gate read
/// <c>DateTime.UtcNow</c> directly (there is no clock seam), so every scenario is
/// built relative to <c>Today</c> with the SAME weekday/ordinal geometry as the
/// customer's, and absolute dates are never hard-coded. Where the geometry of
/// "today" matters (the customer's "today = 18 Sept") it is documented on the
/// test.
/// </summary>
public partial class TaskListBatchStartDateTest
{
    // ─────────────────────────────────────────────────────────────────────────
    // Date geometry helpers (brute force — independent of production code)
    // ─────────────────────────────────────────────────────────────────────────

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime FirstOfMonth(DateTime d) => Utc(d.Year, d.Month, 1);

    /// <summary>
    /// The first month at least <paramref name="monthsAhead"/> months after the
    /// current one that has 31 days — so every weekday of days 1, 2 and 3 occurs
    /// FIVE times (days 1/8/15/22/29, 2/9/…/30, 3/10/…/31).
    /// </summary>
    private DateTime ThirtyOneDayMonthAhead(int monthsAhead)
    {
        var m = FirstOfMonth(Today).AddMonths(monthsAhead);
        while (DateTime.DaysInMonth(m.Year, m.Month) != 31) m = m.AddMonths(1);
        return m;
    }

    /// <summary>How many days of d's month, up to and including d, share d's weekday.</summary>
    private static int CountOfWeekdayUpTo(DateTime d) =>
        Enumerable.Range(1, d.Day).Count(day => Utc(d.Year, d.Month, day).DayOfWeek == d.DayOfWeek);

    /// <summary>The first date in the month of <paramref name="month"/> on <paramref name="dow"/>.</summary>
    private static DateTime FirstWeekdayOfMonth(DateTime month, DayOfWeek dow)
    {
        var d = FirstOfMonth(month);
        while (d.DayOfWeek != dow) d = d.AddDays(1);
        return d;
    }

    /// <summary>
    /// The LAST date in the month on <paramref name="dow"/> — which is exactly the
    /// "5th, or the last one when the month has only four" the scheduler's
    /// fallback produces (a 5th occurrence, when it exists, IS the last one).
    /// </summary>
    private static DateTime LastWeekdayOfMonth(DateTime month, DayOfWeek dow)
    {
        var d = Utc(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
        while (d.DayOfWeek != dow) d = d.AddDays(-1);
        return d;
    }

    private static string Iso(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private int DeployCallCount() =>
        _deployService.ReceivedCalls().Count(c => c.GetMethodInfo().Name
            == nameof(IEventDeployService.EnsureComplianceForOccurrenceAsync));

    private async Task<(int Open, int Completed)> LiveComplianceRows(int planningId)
    {
        var rows = await BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
            .Where(c => c.PlanningId == planningId && c.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(c => c.MicrotingSdkCaseId)
            .ToListAsync();
        var completed = await MicrotingDbContext!.Cases.AsNoTracking()
            .CountAsync(c => rows.Contains(c.Id) && c.Status == CompletedStatus);
        return (rows.Count - completed, completed);
    }

    private BackendConfigurationCalendarService BuildRealCalendarService() => new(
        _localizationService,
        _userService,
        BackendConfigurationPnDbContext!,
        _coreHelper,
        _deployService,
        ItemsPlanningPnDbContext!,
        _taskWizardService,
        Substitute.For<ICalendarAssignmentReconciliationService>(),
        Substitute.For<ICalendarChangeNotifier>(),
        TestContextLogger<BackendConfigurationCalendarService>.Instance,
        _retractionService,
        _backfillService,
        new WorkerTagMembershipService(_coreHelper));

    /// <summary>
    /// Runs the PREVIEW, then the real APPLY, on the same data, and reports both
    /// sides so every end-to-end cell can assert preview == apply.
    /// </summary>
    private async Task<PreviewAndApply> PreviewThenApply(Seeded seeded, DateTime newAnchor)
    {
        var preview = await _taskListService.ChangeStartDatePreview(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = newAnchor
        });
        Assert.That(preview.Success, Is.True, preview.Message);

        var before = await LiveComplianceRows(seeded.PlanningId);
        var deployCallsBefore = DeployCallCount();

        ConfigureWizardToPersistTheAnchor();
        var result = await BuildTaskListServiceWithRealCalendar().ChangeStartDate(
            new TaskListBatchStartDateModel { TaskIds = [seeded.ArpId], StartDate = newAnchor });
        Assert.That(result.Success, Is.True, result.Message);

        var after = await LiveComplianceRows(seeded.PlanningId);
        var arp = await ReloadArp(seeded.ArpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.AsNoTracking()
            .FirstAsync(x => x.Id == seeded.PlanningId);

        return new PreviewAndApply(
            preview.Model,
            Retracted: before.Open - after.Open,
            CompletedBefore: before.Completed,
            CompletedAfter: after.Completed,
            Created: DeployCallCount() - deployCallsBefore,
            Arp: arp,
            Planning: planning);
    }

    private sealed record PreviewAndApply(
        TaskListBatchStartDatePreviewModel Preview,
        int Retracted,
        int CompletedBefore,
        int CompletedAfter,
        int Created,
        AreaRulePlanning Arp,
        Microting.ItemsPlanningBase.Infrastructure.Data.Entities.Planning Planning);

    /// <summary>The four preview == apply equalities every end-to-end cell must satisfy.</summary>
    private static void AssertPreviewEqualsApply(PreviewAndApply r)
    {
        Assert.That(r.Preview.OccurrencesToRetract, Is.EqualTo(r.Retracted),
            "preview's retract count must equal the rows the apply actually retracted");
        Assert.That(r.Preview.OverdueToCreate, Is.EqualTo(r.Created),
            "preview's overdue count must equal the (occurrence x site) pairs the apply back-deployed");
        Assert.That(r.CompletedAfter, Is.EqualTo(r.CompletedBefore),
            "completed history is never touched (R2)");
        Assert.That(r.Preview.TaskCount, Is.EqualTo(1));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // A. Ordinal derivation — the full boundary matrix (calendar service stubbed)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Move onto the 1st..5th occurrence of a weekday, starting from a rule whose
    /// stored ordinal is 1, 2 or 5 — one cell per (from, to) pair. The source
    /// sits on the weekday of day 1 (days 1/8/15/22/29) and the target on the
    /// weekday of day 3 (days 3/10/17/24/31) of a 31-day month, so both weekdays
    /// have five occurrences and every pair is reachable. Pre-#1289 the captured
    /// ordinal was the SOURCE ordinal in every row where from != to.
    /// </summary>
    [TestCase(1, 1)]
    [TestCase(1, 2)]
    [TestCase(1, 3)]
    [TestCase(1, 4)]
    [TestCase(1, 5)]
    [TestCase(2, 1)]
    [TestCase(2, 2)]
    [TestCase(2, 3)]
    [TestCase(2, 4)]
    [TestCase(2, 5)]
    [TestCase(5, 1)]
    [TestCase(5, 2)]
    [TestCase(5, 3)]
    [TestCase(5, 4)]
    [TestCase(5, 5)]
    public async Task ChangeStartDate_MoveOntoNthOccurrence_ReDerivesOrdinalFromTheNewDate(
        int fromOrdinal, int toOccurrence)
    {
        var month = ThirtyOneDayMonthAhead(2);
        var source = Utc(month.Year, month.Month, 1 + 7 * (fromOrdinal - 1));
        var target = Utc(month.Year, month.Month, 3 + 7 * (toOccurrence - 1));
        Assume.That(CountOfWeekdayUpTo(source), Is.EqualTo(fromOrdinal), "premise: source geometry");
        Assume.That(CountOfWeekdayUpTo(target), Is.EqualTo(toOccurrence), "premise: target geometry");

        var seeded = await SeedTask(source, repeatType: 3, siteIds: [100], repeatOrdinalWeek: fromOrdinal);

        var result = await _taskListService.ChangeStartDate(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = target
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls, Has.Count.EqualTo(1));
        Assert.That(_updateCalls[0].RepeatOrdinalWeek, Is.EqualTo(toOccurrence),
            $"moved onto occurrence #{toOccurrence} of its weekday, the rule must say {toOccurrence} (was {fromOrdinal})");
    }

    /// <summary>
    /// Every week boundary, one cell per day: 7/8, 14/15, 21/22, 28/29 and the
    /// 29th–31st (all ordinal 5). Source ordinal 2, like the customer's task.
    /// </summary>
    [TestCase(1, 1)]
    [TestCase(7, 1)]
    [TestCase(8, 2)]
    [TestCase(14, 2)]
    [TestCase(15, 3)]
    [TestCase(21, 3)]
    [TestCase(22, 4)]
    [TestCase(28, 4)]
    [TestCase(29, 5)]
    [TestCase(30, 5)]
    [TestCase(31, 5)]
    public async Task ChangeStartDate_DayBoundary_ReDerivesOrdinal(int day, int expectedOrdinal)
    {
        var month = ThirtyOneDayMonthAhead(2);
        var target = Utc(month.Year, month.Month, day);
        Assume.That(CountOfWeekdayUpTo(target), Is.EqualTo(expectedOrdinal), "premise: brute-force ordinal");

        var seeded = await SeedTask(Utc(month.Year, month.Month, 10), repeatType: 3, siteIds: [100],
            repeatOrdinalWeek: 2);

        var result = await _taskListService.ChangeStartDate(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = target
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(_updateCalls[0].RepeatOrdinalWeek, Is.EqualTo(expectedOrdinal));
    }

    /// <summary>
    /// The picked date is derived through NormalizeStartDateToLocalDay FIRST — the
    /// same #966 rounding UpdateTask applies — so a browser east of UTC that sends
    /// "local midnight of the 8th" as 7th T22:00Z still gets the 8th's ordinal
    /// (2), not the 7th's (1).
    /// </summary>
    [TestCase(-2)] // UTC+2 (Danish summer time)
    [TestCase(-1)] // UTC+1
    [TestCase(0)]  // tz-stable midnight
    public async Task ChangeStartDate_TzShiftedPick_OrdinalOfTheIntendedLocalDay(int offsetHours)
    {
        var month = ThirtyOneDayMonthAhead(2);
        var intended = Utc(month.Year, month.Month, 8);
        var seeded = await SeedTask(Utc(month.Year, month.Month, 10), repeatType: 3, siteIds: [100],
            repeatOrdinalWeek: 2);

        await _taskListService.ChangeStartDate(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = intended.AddHours(offsetHours)
        });

        Assert.That(_updateCalls[0].RepeatOrdinalWeek, Is.EqualTo(2),
            "the 8th is the 2nd occurrence of its weekday, wherever the browser sits");
    }

    /// <summary>
    /// Plain day-of-month Month rules, Week, Day and Year rules have NO ordinal and
    /// must keep it null — a re-derived value there would silently convert the rule
    /// into an Nth-weekday rule.
    /// </summary>
    [TestCase(1, null)]  // Day
    [TestCase(2, null)]  // Week
    [TestCase(3, 10)]    // Month, day-of-month 10
    [TestCase(4, 10)]    // Year
    public async Task ChangeStartDate_RuleWithoutOrdinal_KeepsItNull(int repeatType, int? dayOfMonth)
    {
        var month = ThirtyOneDayMonthAhead(2);
        var seeded = await SeedTask(Utc(month.Year, month.Month, 10), repeatType, siteIds: [100],
            dayOfMonth: dayOfMonth);

        await _taskListService.ChangeStartDate(new TaskListBatchStartDateModel
        {
            TaskIds = [seeded.ArpId], StartDate = Utc(month.Year, month.Month, 7)
        });

        Assert.That(_updateCalls[0].RepeatOrdinalWeek, Is.Null);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // B. End to end through the REAL calendar service — preview == apply
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// THE CUSTOMER CASE, compliance ON, with its completed occurrence kept.
    ///
    /// Customer: "2nd Thursday" task anchored Thu 10 Sept (completed), re-anchored
    /// to Mon 7 Sept, today = 18 Sept ⇒ rule "1st Monday", exactly one overdue row
    /// on 7 Sept, and the completed 10 Sept stays (keep both).
    ///
    /// Relative geometry: day 7 of ANY month is the 1st occurrence of its weekday
    /// and day 10 is the 2nd occurrence of its weekday, so "10th (ordinal 2) → 7th"
    /// reproduces the customer's move in every month. When today is on/after the
    /// 18th, month M is the CURRENT month — the customer's exact situation
    /// (7th, 10th and 14th all behind us, next 1st-weekday in the next month) — and
    /// exactly ONE overdue row must be created. Otherwise M is the previous month
    /// and the expected rows are enumerated by brute force (every day ≤ 7 on that
    /// weekday in [7th of M, today)).
    ///
    /// On the OLD code the rule stayed "2nd &lt;weekday&gt;" and the only overdue
    /// row of M landed on the 14th — asserted absent below.
    /// </summary>
    [Test]
    public async Task ChangeStartDate_CustomerCase_SecondThursdayToFirstMonday_OneOverdueOnTheNewAnchor_CompletedKept()
    {
        var month = Today.Day >= 18 ? FirstOfMonth(Today) : FirstOfMonth(Today).AddMonths(-1);
        var oldAnchor = Utc(month.Year, month.Month, 10);   // "Thu 10 Sept", 2nd occurrence
        var newAnchor = Utc(month.Year, month.Month, 7);    // "Mon 7 Sept", 1st occurrence
        var wrongOldDate = newAnchor.AddDays(7);            // "Mon 14 Sept", where the bug put it

        var sdkSiteId = await SeedSdkSite();
        var seeded = await SeedTask(oldAnchor, repeatType: 3, siteIds: [sdkSiteId],
            complianceEnabled: true, repeatOrdinalWeek: 2);
        await SeedDeployedOccurrence(seeded.PlanningId, oldAnchor, CompletedStatus, sdkSiteId);

        var expected = new List<DateTime>();
        for (var d = newAnchor; d < Today; d = d.AddDays(1))
        {
            if (d.Day <= 7 && d.DayOfWeek == newAnchor.DayOfWeek) expected.Add(d);
        }

        var r = await PreviewThenApply(seeded, newAnchor);

        Assert.Multiple(() =>
        {
            AssertPreviewEqualsApply(r);
            Assert.That(r.Arp.RepeatOrdinalWeek, Is.EqualTo(1), "7th = 1st occurrence ⇒ '1st <weekday>'");
            Assert.That(r.Planning.RepeatOrdinalWeek, Is.EqualTo(1), "mirrored for SearchListJob");
            Assert.That(r.Arp.DayOfWeek, Is.EqualTo((int)newAnchor.DayOfWeek));
            Assert.That(DeployedDeadlines(), Is.EqualTo(expected));
            Assert.That(DeployedDeadlines().First(), Is.EqualTo(newAnchor), "the overdue task lies on the new anchor");
            Assert.That(DeployedDeadlines(), Does.Not.Contain(wrongOldDate), "the 2nd-weekday date is the bug");
            Assert.That(r.CompletedAfter, Is.EqualTo(1), "the completed 10th is kept alongside the new 7th");
            if (month == FirstOfMonth(Today))
            {
                Assert.That(r.Created, Is.EqualTo(1), "customer geometry: exactly one overdue task");
            }
        });
    }

    /// <summary>Same move with compliance OFF: ordinal fixed, no overdue rows (by design).</summary>
    [Test]
    public async Task ChangeStartDate_CustomerCase_ComplianceOff_OrdinalFixed_NoOverdue()
    {
        var month = Today.Day >= 18 ? FirstOfMonth(Today) : FirstOfMonth(Today).AddMonths(-1);
        var sdkSiteId = await SeedSdkSite();
        var seeded = await SeedTask(Utc(month.Year, month.Month, 10), repeatType: 3, siteIds: [sdkSiteId],
            complianceEnabled: false, repeatOrdinalWeek: 2);

        var r = await PreviewThenApply(seeded, Utc(month.Year, month.Month, 7));

        Assert.Multiple(() =>
        {
            AssertPreviewEqualsApply(r);
            Assert.That(r.Arp.RepeatOrdinalWeek, Is.EqualTo(1));
            Assert.That(r.Planning.RepeatOrdinalWeek, Is.EqualTo(1));
            Assert.That(r.Created, Is.Zero, "no red tasks exist for a compliance-off series");
        });
    }

    /// <summary>
    /// Past anchor on each occurrence 1..5 of a weekday, from ordinals 1, 2 and 5:
    /// the overdue rows land on THAT occurrence in every month (last occurrence
    /// when a month has no 5th — the scheduler's fallback), and preview == apply.
    /// Month M is the most recent 31-day month at least three months back, so
    /// days 1/8/15/22/29 all exist and at least two later months are in the past.
    /// </summary>
    [TestCase(1, 1)]
    [TestCase(1, 3)]
    [TestCase(1, 5)]
    [TestCase(2, 1)]
    [TestCase(2, 2)]
    [TestCase(2, 4)]
    [TestCase(2, 5)]
    [TestCase(5, 1)]
    [TestCase(5, 5)]
    public async Task ChangeStartDate_PastAnchor_OverdueRowsFollowTheReDerivedOrdinal(int fromOrdinal, int toOccurrence)
    {
        var month = FirstOfMonth(Today).AddMonths(-3);
        while (DateTime.DaysInMonth(month.Year, month.Month) != 31) month = month.AddMonths(-1);
        var newAnchor = Utc(month.Year, month.Month, 1 + 7 * (toOccurrence - 1));
        var dow = newAnchor.DayOfWeek;
        // Source: a different weekday (day 2's) on its fromOrdinal-th occurrence.
        var source = Utc(month.Year, month.Month, 2 + 7 * (fromOrdinal - 1));

        var sdkSiteId = await SeedSdkSite();
        var seeded = await SeedTask(source, repeatType: 3, siteIds: [sdkSiteId],
            complianceEnabled: true, repeatOrdinalWeek: fromOrdinal);

        // Brute force: in each month from M, the toOccurrence-th `dow` — or the
        // month's LAST `dow` when it has fewer (only possible for 5).
        var expected = new List<DateTime>();
        for (var m = month; m <= FirstOfMonth(Today); m = m.AddMonths(1))
        {
            var candidates = Enumerable.Range(1, DateTime.DaysInMonth(m.Year, m.Month))
                .Select(d => Utc(m.Year, m.Month, d)).Where(d => d.DayOfWeek == dow).ToList();
            var date = candidates.Count >= toOccurrence ? candidates[toOccurrence - 1] : candidates[^1];
            if (date >= newAnchor && date < Today) expected.Add(date);
        }

        var r = await PreviewThenApply(seeded, newAnchor);

        Assert.Multiple(() =>
        {
            AssertPreviewEqualsApply(r);
            Assert.That(r.Arp.RepeatOrdinalWeek, Is.EqualTo(toOccurrence));
            Assert.That(r.Planning.RepeatOrdinalWeek, Is.EqualTo(toOccurrence));
            Assert.That(DeployedDeadlines(), Is.EqualTo(expected));
        });
    }

    /// <summary>
    /// RepeatEvery = 2, past anchor on a 1st weekday five months back: overdue rows
    /// on the 1st weekday of M, M+2 and M+4 — the re-derived ordinal applied every
    /// other month. Source "2nd weekday" would have produced the 2nd ones.
    /// </summary>
    [Test]
    public async Task ChangeStartDate_RepeatEvery2_PastAnchor_OverdueEveryOtherMonthOnTheNewOrdinal()
    {
        var month = FirstOfMonth(Today).AddMonths(-5);
        var newAnchor = Utc(month.Year, month.Month, 5);   // day 5 ⇒ 1st occurrence
        var dow = newAnchor.DayOfWeek;

        var sdkSiteId = await SeedSdkSite();
        var seeded = await SeedTask(Utc(month.Year, month.Month, 12), repeatType: 3, siteIds: [sdkSiteId],
            complianceEnabled: true, repeatOrdinalWeek: 2, repeatEvery: 2);

        var expected = new[] { 0, 2, 4, 6 }
            .Select(k => FirstWeekdayOfMonth(month.AddMonths(k), dow))
            .Where(d => d >= newAnchor && d < Today)
            .ToList();

        var r = await PreviewThenApply(seeded, newAnchor);

        Assert.Multiple(() =>
        {
            AssertPreviewEqualsApply(r);
            Assert.That(r.Arp.RepeatOrdinalWeek, Is.EqualTo(1));
            Assert.That(r.Arp.RepeatEvery, Is.EqualTo(2));
            Assert.That(DeployedDeadlines(), Is.EqualTo(expected));
            Assert.That(expected, Has.Count.GreaterThanOrEqualTo(3), "premise: M, M+2, M+4 are all past");
        });
    }

    /// <summary>
    /// 5th-weekday FALLBACK end to end: a past anchor on a 5th occurrence (day 29)
    /// makes the rule "5th &lt;weekday&gt;"; months without a 5th must get their
    /// LAST occurrence (the scheduler's rule) — before #1289 the calendar skipped
    /// them, so the overdue backfill silently dropped those months.
    /// </summary>
    [Test]
    public async Task ChangeStartDate_FifthWeekdayAnchor_FourWeekdayMonthsFallBackToTheLastOccurrence()
    {
        var month = FirstOfMonth(Today).AddMonths(-6);
        while (DateTime.DaysInMonth(month.Year, month.Month) != 31) month = month.AddMonths(-1);
        var newAnchor = Utc(month.Year, month.Month, 29);
        var dow = newAnchor.DayOfWeek;

        var sdkSiteId = await SeedSdkSite();
        var seeded = await SeedTask(Utc(month.Year, month.Month, 3), repeatType: 3, siteIds: [sdkSiteId],
            complianceEnabled: true, repeatOrdinalWeek: 1);

        var expected = new List<DateTime>();
        for (var m = month; m <= FirstOfMonth(Today); m = m.AddMonths(1))
        {
            var last = LastWeekdayOfMonth(m, dow);
            if (last >= newAnchor && last < Today) expected.Add(last);
        }
        // Premise: at least one past month in the window has only four `dow`s —
        // otherwise this test would not exercise the fallback at all.
        Assume.That(expected.Any(d => d.Day <= 28), Is.True, "premise: a four-weekday month is in range");

        var r = await PreviewThenApply(seeded, newAnchor);

        Assert.Multiple(() =>
        {
            AssertPreviewEqualsApply(r);
            Assert.That(r.Arp.RepeatOrdinalWeek, Is.EqualTo(5));
            Assert.That(DeployedDeadlines(), Is.EqualTo(expected),
                "every month gets its 5th weekday, or its last (4th) when there is no 5th");
        });
    }

    /// <summary>
    /// Anchor TODAY: not in the past (the backfill range [today, today) is empty),
    /// and the ordinal is today's own — computed by brute force.
    /// </summary>
    [Test]
    public async Task ChangeStartDate_AnchorToday_OrdinalOfToday_NoOverdue()
    {
        var sdkSiteId = await SeedSdkSite();
        var future = FirstOfMonth(Today).AddMonths(2);
        var seeded = await SeedTask(future.AddDays(9), repeatType: 3, siteIds: [sdkSiteId],
            complianceEnabled: true, repeatOrdinalWeek: 2);

        var r = await PreviewThenApply(seeded, Today);

        Assert.Multiple(() =>
        {
            AssertPreviewEqualsApply(r);
            Assert.That(r.Arp.RepeatOrdinalWeek, Is.EqualTo(CountOfWeekdayUpTo(Today)));
            Assert.That(r.Arp.DayOfWeek, Is.EqualTo((int)Today.DayOfWeek));
            Assert.That(r.Created, Is.Zero);
        });
    }

    /// <summary>
    /// FUTURE anchor, SAME weekday in a DIFFERENT week of the same month (2nd →
    /// 3rd): same recurrence period, so the apply RELOCATES the open deployed row
    /// onto the new pattern date (3rd weekday) and retracts nothing — the preview
    /// must say 0 as well. Before #1289 the rule stayed "2nd" and the row stayed
    /// on the 2nd weekday while the anchor sat on the 3rd.
    /// </summary>
    [Test]
    public async Task ChangeStartDate_Future_SameWeekdayOtherWeek_RelocatesOntoTheNewOrdinal()
    {
        var month = ThirtyOneDayMonthAhead(2);
        var oldAnchor = Utc(month.Year, month.Month, 8);    // 2nd occurrence
        var newAnchor = oldAnchor.AddDays(7);               // 3rd occurrence, same weekday
        var sdkSiteId = await SeedSdkSite();
        var seeded = await SeedTask(oldAnchor, repeatType: 3, siteIds: [sdkSiteId],
            complianceEnabled: true, repeatOrdinalWeek: 2);
        await SeedDeployedOccurrence(seeded.PlanningId, oldAnchor, OpenStatus, sdkSiteId);

        var r = await PreviewThenApply(seeded, newAnchor);

        var deadlines = await BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
            .Where(c => c.PlanningId == seeded.PlanningId && c.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(c => c.Deadline.Date).ToListAsync();
        Assert.Multiple(() =>
        {
            AssertPreviewEqualsApply(r);
            Assert.That(r.Retracted, Is.Zero, "same month + future ⇒ relocate, not retract");
            Assert.That(r.Arp.RepeatOrdinalWeek, Is.EqualTo(3));
            Assert.That(deadlines, Is.EqualTo(new[] { newAnchor }),
                "the open row is relocated onto the 3rd weekday — the anchor");
        });
    }

    /// <summary>
    /// FUTURE anchor, SAME WEEK, DIFFERENT weekday — the customer's literal
    /// geometry: the first future month (≥ 2 ahead) whose 1st is a Tuesday, so
    /// day 10 is the 2nd Thursday and day 7 the 1st Monday of the SAME Mon–Sun
    /// week (exactly Thu 10 / Mon 7 Sept 2026). Weekday AND ordinal (1) follow
    /// the new date; the open row is relocated onto it.
    /// </summary>
    [Test]
    public async Task ChangeStartDate_Future_SameWeekOtherWeekday_RelocatesOntoTheNewWeekdayAndOrdinal()
    {
        var month = FirstOfMonth(Today).AddMonths(2);
        while (month.DayOfWeek != DayOfWeek.Tuesday) month = month.AddMonths(1);
        var oldAnchor = Utc(month.Year, month.Month, 10);   // Thursday, 2nd occurrence
        var newAnchor = Utc(month.Year, month.Month, 7);    // Monday of the same week, 1st occurrence
        Assume.That(oldAnchor.DayOfWeek, Is.EqualTo(DayOfWeek.Thursday));
        Assume.That(newAnchor.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
        var sdkSiteId = await SeedSdkSite();
        var seeded = await SeedTask(oldAnchor, repeatType: 3, siteIds: [sdkSiteId],
            complianceEnabled: true, repeatOrdinalWeek: 2);
        await SeedDeployedOccurrence(seeded.PlanningId, oldAnchor, OpenStatus, sdkSiteId);

        var r = await PreviewThenApply(seeded, newAnchor);

        var deadlines = await BackendConfigurationPnDbContext!.Compliances.AsNoTracking()
            .Where(c => c.PlanningId == seeded.PlanningId && c.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(c => c.Deadline.Date).ToListAsync();
        Assert.Multiple(() =>
        {
            AssertPreviewEqualsApply(r);
            Assert.That(r.Retracted, Is.Zero);
            Assert.That(r.Arp.RepeatOrdinalWeek, Is.EqualTo(1));
            Assert.That(r.Arp.DayOfWeek, Is.EqualTo((int)newAnchor.DayOfWeek));
            Assert.That(deadlines, Is.EqualTo(new[] { newAnchor }));
        });
    }

    /// <summary>
    /// FUTURE anchor in a DIFFERENT month: the destructive retract branch. The
    /// preview must promise exactly what the apply retracts; the completed row is
    /// preserved. The re-derived ordinal is what both sides read.
    /// </summary>
    [Test]
    public async Task ChangeStartDate_Future_OtherMonth_RetractCountsMatchAndOrdinalReDerived()
    {
        var month = ThirtyOneDayMonthAhead(2);
        var oldAnchor = Utc(month.Year, month.Month, 10);
        var newAnchor = Utc(month.Year, month.Month, 1).AddMonths(1).AddDays(21); // day 22 ⇒ 4th
        var sdkSiteId = await SeedSdkSite();
        var seeded = await SeedTask(oldAnchor, repeatType: 3, siteIds: [sdkSiteId],
            complianceEnabled: true, repeatOrdinalWeek: 2);
        await SeedDeployedOccurrence(seeded.PlanningId, oldAnchor, OpenStatus, sdkSiteId);
        await SeedDeployedOccurrence(seeded.PlanningId, oldAnchor.AddHours(1), CompletedStatus, sdkSiteId);

        var r = await PreviewThenApply(seeded, newAnchor);

        Assert.Multiple(() =>
        {
            AssertPreviewEqualsApply(r);
            Assert.That(r.Retracted, Is.EqualTo(1));
            Assert.That(r.Preview.CompletedPreserved, Is.EqualTo(1));
            Assert.That(r.Arp.RepeatOrdinalWeek, Is.EqualTo(4));
            Assert.That(r.Created, Is.Zero, "future anchor ⇒ nothing to backfill");
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // C. Direct API callers of UpdateTask
    // ═════════════════════════════════════════════════════════════════════════

    private CalendarTaskUpdateRequestModel DirectUpdate(
        Seeded seeded, int siteId, string scope, DateTime originalDate, DateTime startDate, int? ordinal) => new()
    {
        Id = seeded.ArpId,
        Scope = scope,
        OriginalDate = Iso(originalDate),
        StartDate = startDate,
        StartHour = 9.0,
        Duration = 1.0,
        Status = 1,
        RepeatType = 3,
        RepeatEvery = 1,
        RepeatOrdinalWeek = ordinal,
        DayOfMonth = 0,
        ComplianceEnabled = false,
        PropertyId = 0,
        EformId = 7,
        Sites = [siteId],
        TagIds = [],
        Translates = []
    };

    /// <summary>
    /// A direct API caller that moves an occurrence of a "2nd &lt;weekday&gt;" series
    /// onto a 1st-occurrence day but sends the STALE ordinal 2: the server
    /// re-derives it (arp + planning mirror) on BOTH series scopes.
    /// </summary>
    [TestCase("all")]
    [TestCase("thisAndFollowing")]
    public async Task UpdateTask_DateChanged_StaleRequestOrdinal_IsReDerivedServerSide(string scope)
    {
        ConfigureWizardToPersistTheAnchor();
        var month = FirstOfMonth(Today).AddMonths(2);
        var sdkSiteId = await SeedSdkSite();
        var seeded = await SeedTask(Utc(month.Year, month.Month, 10), repeatType: 3, siteIds: [sdkSiteId],
            repeatOrdinalWeek: 2);
        var nextMonth = month.AddMonths(1);
        var clicked = Utc(nextMonth.Year, nextMonth.Month, 10);
        var moved = Utc(nextMonth.Year, nextMonth.Month, 6);

        var result = await BuildRealCalendarService().UpdateTask(
            DirectUpdate(seeded, sdkSiteId, scope, clicked, moved, ordinal: 2));
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await ReloadArp(seeded.ArpId);
        var planning = await ItemsPlanningPnDbContext!.Plannings.AsNoTracking()
            .FirstAsync(x => x.Id == seeded.PlanningId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.RepeatOrdinalWeek, Is.EqualTo(1), "day 6 is the 1st occurrence of its weekday");
            Assert.That(planning.RepeatOrdinalWeek, Is.EqualTo(1));
            Assert.That(arp.DayOfWeek, Is.EqualTo((int)moved.DayOfWeek));
        });
    }

    /// <summary>
    /// The gate is "the date changed". A pure field edit (OriginalDate ==
    /// StartDate) keeps the request's ordinal — which is what the OTHER task-list
    /// batch actions rely on: BuildUpdateModel hands UpdateTask a synthetic
    /// "nearest future same-weekday" StartDate with an arbitrary ordinal, and
    /// re-deriving from THAT would corrupt every reassigned/renamed task.
    /// </summary>
    [Test]
    public async Task UpdateTask_ScopeAll_DateUnchanged_KeepsTheStoredOrdinal()
    {
        ConfigureWizardToPersistTheAnchor();
        var month = FirstOfMonth(Today).AddMonths(2);
        var sdkSiteId = await SeedSdkSite();
        var seeded = await SeedTask(Utc(month.Year, month.Month, 10), repeatType: 3, siteIds: [sdkSiteId],
            repeatOrdinalWeek: 2);
        // Same weekday as the anchor, but the 4th occurrence: what a synthetic
        // same-weekday anchor can look like.
        var synthetic = Utc(month.Year, month.Month, 24);

        var result = await BuildRealCalendarService().UpdateTask(
            DirectUpdate(seeded, sdkSiteId, "all", synthetic, synthetic, ordinal: 2));
        Assert.That(result.Success, Is.True, result.Message);

        var arp = await ReloadArp(seeded.ArpId);
        Assert.Multiple(() =>
        {
            Assert.That(arp.RepeatOrdinalWeek, Is.EqualTo(2), "a date-unchanged edit must not re-derive");
            Assert.That(arp.StartDate!.Value.Date, Is.EqualTo(Utc(month.Year, month.Month, 10)),
                "and the anchor is preserved");
        });
    }
}
