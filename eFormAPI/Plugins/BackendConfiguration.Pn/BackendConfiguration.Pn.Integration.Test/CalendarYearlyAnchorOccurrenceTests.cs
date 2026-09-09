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
using System.Linq;
using System.Reflection;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using CalendarSvc =
    BackendConfiguration.Pn.Services.BackendConfigurationCalendarService.BackendConfigurationCalendarService;
using ItemsPlanningRepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType;

/// <summary>
/// #1217 — the Year twin of #1207, in the two recurrence enumerators.
///
/// <c>GetOccurrencesInWeek</c>'s Year branch gated its emit on
/// <c>candidate &gt;= weekStart</c> alone, with no <c>&gt;= startDate</c>.
/// <c>EnumerateOccurrences</c>' Year branch, by contrast, floors its range at
/// <c>startDate</c>. So for a yearly rule whose <c>planning.DayOfMonth</c>
/// sorts BEFORE <c>StartDate.Day</c>, the week grid painted a tile one or more
/// days before the series existed while the iterator yielded nothing — the
/// #922/#952 divergence class the file's own invariant exists to prevent
/// ("never a second implementation").
///
/// The reachable cohort is legacy data: <c>DeriveDayOfMonth</c> returns
/// <c>startDate.Day</c> for Year, so wizard-created series have
/// <c>DayOfMonth == StartDate.Day</c> and never diverge. Pre-#933 rows, where
/// the custom dialog hard-coded 1 January regardless of the chosen start date,
/// do.
///
/// <b>Two things are fixed here</b>, both inside the Year branch:
/// <list type="number">
/// <item>the missing <c>&gt;= startDate</c> guard, plus the "anchor is
/// occurrence #1" rule from #1207 so that first occurrence is emitted on the
/// anchor rather than silently lost;</item>
/// <item><c>if (yearsSinceStart &lt; 0) break;</c>, which threw away the start
/// week of any yearly series whose start week STRADDLES New Year — a second,
/// independent divergence in the same branch.</item>
/// </list>
///
/// <b>The issue's "week double-renders" claim did NOT reproduce.</b> It reads
/// the unconditional <c>occurrences.Add(startDate)</c> at the bottom of
/// <c>GetOccurrencesInWeek</c> as part of the Year branch; it is the
/// <c>default:</c> (non-recurring) arm, and C# has no fall-through. Pre-fix the
/// issue's repro rendered exactly ONE tile — the WRONG one (2026-09-07, before
/// the series begins), with the anchor missing entirely. The tests below assert
/// that shape, not a double-emit.
///
/// This fixture is pure — the enumerators are static and take a detached
/// Planning, so no database and no MariaDb container is involved. Every test is
/// annotated with what it does on PRE-FIX code.
/// </summary>
[TestFixture]
public class CalendarYearlyAnchorOccurrenceTests
{
    // The items-planning enum has no Year member; a yearly Planning stores the
    // plugin enum's Year = 4 via a cast, exactly as the service reads it.
    private const int YearRepeatType = 4;

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static Planning YearlyPlanning(DateTime startDate, int repeatEvery, int? dayOfMonth) => new()
    {
        StartDate = startDate,
        RepeatType = (ItemsPlanningRepeatType)YearRepeatType,
        RepeatEvery = repeatEvery,
        DayOfMonth = dayOfMonth
    };

    private static List<DateTime> Enumerate(Planning planning, DateTime fromInclusive, DateTime toExclusive) =>
        CalendarSvc.EnumerateOccurrences(planning, fromInclusive, toExclusive).ToList();

    /// <summary>
    /// GetOccurrencesInWeek is private; reached by reflection exactly as
    /// <see cref="CalendarMonthlyAnchorOccurrenceTests"/> does.
    /// </summary>
    private static List<DateTime> OccurrencesInWeek(Planning planning, DateTime weekStart, DateTime weekEnd)
    {
        var mi = typeof(BackendConfigurationCalendarService).GetMethod(
            "GetOccurrencesInWeek", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(mi, Is.Not.Null, "GetOccurrencesInWeek(...) signature changed — update this test");
        return (List<DateTime>)mi!.Invoke(null,
            new object?[] { planning, weekStart, weekEnd, null, null, null })!;
    }

    /// <summary>
    /// <c>NewPatternDateForPeriodOf</c> is private static; same reflection
    /// approach.
    /// </summary>
    private static DateTime? NewPatternDateForPeriodOf(
        Planning planning, AreaRulePlanning arp, DateTime oldDeadline)
    {
        var mi = typeof(BackendConfigurationCalendarService).GetMethod(
            "NewPatternDateForPeriodOf", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(mi, Is.Not.Null, "NewPatternDateForPeriodOf(...) signature changed — update this test");
        return (DateTime?)mi!.Invoke(null, new object?[] { planning, arp, oldDeadline });
    }

    /// <summary>
    /// The two enumerators must return the SAME dates for the same week.
    /// GetOccurrencesInWeek's weekEnd is inclusive, EnumerateOccurrences'
    /// toExclusive is not — hence the +1 day, exactly as the service itself
    /// does when it counts occurrences.
    /// </summary>
    private static void AssertEnumeratorsAgree(Planning planning, DateTime firstMonday, int weeks, string because)
    {
        for (var i = 0; i < weeks; i++)
        {
            var weekStart = firstMonday.AddDays(i * 7);
            var weekEnd = weekStart.AddDays(6);
            var week = OccurrencesInWeek(planning, weekStart, weekEnd);
            var enumerated = Enumerate(planning, weekStart, weekEnd.AddDays(1));
            Assert.That(week, Is.EqualTo(enumerated),
                $"{because} — the enumerators disagree for the week of {weekStart:yyyy-MM-dd}");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The issue's exact repro: StartDate 2026-09-08, yearly, DayOfMonth 7
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Acceptance criterion 1 + 4. Mon 2026-09-07 .. Sun 2026-09-13 must render
    /// exactly one row, 2026-09-08, and never 2026-09-07.
    ///
    /// PRE-FIX this returns <c>[2026-09-07]</c>: the equality assertion fails on
    /// BOTH counts — the pre-start pattern date is present and the anchor is
    /// absent.
    /// </summary>
    [Test]
    public void OccurrencesInWeek_IssueRepro_StartWeekRendersTheAnchorAloneNotThePreStartPatternDate()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 7);

        var startWeek = OccurrencesInWeek(planning, Utc(2026, 9, 7), Utc(2026, 9, 13));

        Assert.Multiple(() =>
        {
            Assert.That(startWeek, Is.EqualTo(new[] { Utc(2026, 9, 8) }),
                "the start week renders the anchor alone");
            Assert.That(startWeek, Does.Not.Contain(Utc(2026, 9, 7)),
                "2026-09-07 is one day before the series begins and must never be painted");
            Assert.That(startWeek, Has.Count.EqualTo(1),
                "exactly one row in the start week");
        });
    }

    /// <summary>
    /// The iterator side of the same series. PRE-FIX the returned sequence
    /// starts at 2027-09-07: the start year's pattern date (2026-09-07) sorts
    /// before rangeStart and was dropped, after which the cursor jumped a whole
    /// year — the series' first occurrence was lost entirely. The
    /// <c>Is.EqualTo</c> fails on the missing leading 2026-09-08.
    /// </summary>
    [Test]
    public void Enumerate_IssueRepro_StartYearYieldsTheAnchorNotThePreStartPatternDate()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 7);

        var occurrences = Enumerate(planning, Utc(2026, 1, 1), Utc(2030, 1, 1));

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 9, 8),  // the anchor — missing entirely before #1217
            Utc(2027, 9, 7),
            Utc(2028, 9, 7),
            Utc(2029, 9, 7)
        }));
    }

    /// <summary>
    /// Acceptance criterion 2 — the two enumerators agree over the whole range
    /// for the issue's series.
    ///
    /// PRE-FIX the week of Mon 2026-09-07 is <c>[2026-09-07]</c> from
    /// GetOccurrencesInWeek and <c>[]</c> from EnumerateOccurrences: the
    /// <c>Is.EqualTo(enumerated)</c> inside the sweep fails on that week.
    /// </summary>
    [Test]
    public void EnumeratorsAgree_IssueRepro_OverThreeYearsOfWeeks()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 7);

        // Mon 2026-08-24 .. ~three years of weeks.
        AssertEnumeratorsAgree(planning, Utc(2026, 8, 24), weeks: 160,
            "yearly rule with DayOfMonth 7 and StartDate.Day 8");
    }

    /// <summary>
    /// Acceptance criterion 1, restated as a sweep: the pre-start pattern date
    /// must not appear in ANY week.
    ///
    /// PRE-FIX 2026-09-07 is returned by the week of Mon 2026-09-07, so the
    /// <c>Does.Not.Contain</c> fails there.
    /// </summary>
    [Test]
    public void OccurrencesInWeek_IssueRepro_ThePreStartPatternDateAppearsInNoWeek()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 7);

        for (var i = 0; i < 12; i++)
        {
            var weekStart = Utc(2026, 8, 24).AddDays(i * 7);
            Assert.That(OccurrencesInWeek(planning, weekStart, weekStart.AddDays(6)),
                Does.Not.Contain(Utc(2026, 9, 7)),
                $"week of {weekStart:yyyy-MM-dd} must not paint the pre-start pattern date");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The pre-#933 cohort proper: DayOfMonth 1 with a mid-month start
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The custom dialog hard-coded day 1 regardless of the chosen start date.
    /// Start Tue 2026-09-08 with DayOfMonth 1: the start year's pattern date is
    /// 2026-09-01, a full week before the anchor.
    ///
    /// PRE-FIX the sequence began at 2027-09-01 — the whole first year was lost
    /// — so <c>Is.EqualTo</c> fails on the missing leading 2026-09-08.
    /// </summary>
    [Test]
    public void Enumerate_LegacyDayOneCohort_KeepsTheAnchorThenPatterns()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 1);

        Assert.That(Enumerate(planning, Utc(2026, 1, 1), Utc(2029, 1, 1)), Is.EqualTo(new[]
        {
            Utc(2026, 9, 8), Utc(2027, 9, 1), Utc(2028, 9, 1)
        }));
    }

    /// <summary>
    /// The render side of the same cohort. PRE-FIX the start week was EMPTY
    /// (2026-09-01 falls in the previous week, which the <c>startDate &gt;
    /// weekEnd</c> guard already rejected), so the series never rendered on its
    /// own start date. <c>Is.EqualTo(new[] { 2026-09-08 })</c> fails on empty.
    /// </summary>
    [Test]
    public void OccurrencesInWeek_LegacyDayOneCohort_StartWeekRendersTheAnchor()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 1);

        Assert.Multiple(() =>
        {
            // Mon 2026-08-31 .. Sun 2026-09-06 holds the pure pattern date, which
            // precedes the series start: nothing may render.
            Assert.That(OccurrencesInWeek(planning, Utc(2026, 8, 31), Utc(2026, 9, 6)), Is.Empty,
                "2026-09-01 precedes the series start");
            // Mon 2026-09-07 .. Sun 2026-09-13 holds the anchor.
            Assert.That(OccurrencesInWeek(planning, Utc(2026, 9, 7), Utc(2026, 9, 13)),
                Is.EqualTo(new[] { Utc(2026, 9, 8) }));
            // Mon 2027-08-30 .. Sun 2027-09-05 holds the first patterned date.
            Assert.That(OccurrencesInWeek(planning, Utc(2027, 8, 30), Utc(2027, 9, 5)),
                Is.EqualTo(new[] { Utc(2027, 9, 1) }));
        });
    }

    /// <summary>
    /// Every-N-years cadence, mismatched anchor. PRE-FIX the sequence began at
    /// 2028-09-07 and the first occurrence was two years late.
    /// </summary>
    [Test]
    public void Enumerate_EveryTwoYears_MismatchedAnchor_KeepsTheAnchor()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 2, dayOfMonth: 7);

        Assert.That(Enumerate(planning, Utc(2026, 1, 1), Utc(2033, 1, 1)), Is.EqualTo(new[]
        {
            Utc(2026, 9, 8), Utc(2028, 9, 7), Utc(2030, 9, 7), Utc(2032, 9, 7)
        }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Acceptance criteria 3 and 4, swept across the whole day-of-month space
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Acceptance criterion 4: the start week of a yearly series never renders
    /// two tiles — and (the part that actually discriminates) never renders a
    /// date before the series begins.
    ///
    /// PRE-FIX the <c>Is.GreaterThanOrEqualTo(startDate)</c> assertion fails for
    /// DayOfMonth 7, where the start week rendered 2026-09-07. The
    /// <c>Count &lt;= 1</c> half is green either way: the issue's claimed
    /// double-emit does not exist (see the fixture summary), and this pins that
    /// it stays absent now that an anchor arm was added.
    /// </summary>
    [Test]
    public void OccurrencesInWeek_StartWeek_IsNeverTwoTilesAndNeverBeforeTheSeriesStart(
        [Range(1, 28)] int dayOfMonth)
    {
        var startDate = Utc(2026, 9, 8);
        var planning = YearlyPlanning(startDate, repeatEvery: 1, dayOfMonth: dayOfMonth);

        var startWeek = OccurrencesInWeek(planning, Utc(2026, 9, 7), Utc(2026, 9, 13));

        Assert.That(startWeek, Has.Count.LessThanOrEqualTo(1),
            $"DayOfMonth {dayOfMonth}: the start week must never carry two tiles — "
            + "CompletedPeriodKey buckets a yearly occurrence as \"Y:yyyy\"");
        foreach (var d in startWeek)
            Assert.That(d, Is.GreaterThanOrEqualTo(startDate),
                $"DayOfMonth {dayOfMonth}: nothing may render before the series begins");
    }

    /// <summary>
    /// Acceptance criterion 2, swept: whatever the legacy DayOfMonth column
    /// holds, the two enumerators must agree.
    ///
    /// PRE-FIX this fails at DayOfMonth 7 (the leak lands inside the start
    /// week). It is green pre-fix for 1..6 — there BOTH enumerators dropped the
    /// first occurrence, which is the loss the two
    /// <c>Enumerate_LegacyDayOneCohort_*</c> tests above catch instead.
    /// </summary>
    [Test]
    public void EnumeratorsAgree_AcrossTheLegacyDayOfMonthCohort([Range(1, 28)] int dayOfMonth)
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: dayOfMonth);

        AssertEnumeratorsAgree(planning, Utc(2026, 8, 24), weeks: 64,
            $"yearly rule with DayOfMonth {dayOfMonth} and StartDate.Day 8");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Regression guards — GREEN ON PRE-FIX CODE BY DESIGN
    // ═════════════════════════════════════════════════════════════════════════
    //
    // Acceptance criterion 3. These do not discriminate the fix; they pin that
    // the ordinary wizard cohort (DayOfMonth == StartDate.Day) is untouched.

    /// <summary>
    /// Regression guard (green pre-fix). DeriveDayOfMonth returns
    /// <c>startDate.Day</c> for Year, so this is every wizard-created yearly
    /// series. The pattern date for the start year EQUALS the anchor, so the
    /// strict <c>&lt;</c> in YearStartAnchorIsDroppedOccurrence returns false
    /// and the anchor is emitted exactly once, by the ordinary pattern loop.
    /// </summary>
    [Test]
    public void Enumerate_AnchorMatchesDayOfMonth_IsUnchangedAndNeverDuplicated()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 8);

        var occurrences = Enumerate(planning, Utc(2026, 1, 1), Utc(2029, 1, 1));

        Assert.Multiple(() =>
        {
            Assert.That(occurrences, Is.EqualTo(new[]
            {
                Utc(2026, 9, 8), Utc(2027, 9, 8), Utc(2028, 9, 8)
            }));
            Assert.That(occurrences.Count(d => d == Utc(2026, 9, 8)), Is.EqualTo(1),
                "the anchor must not be emitted twice when it already IS the pattern date");
        });
    }

    /// <summary>
    /// Regression guard (green pre-fix). The same series with a NULL
    /// DayOfMonth, which falls back to <c>startDate.Day</c>.
    /// </summary>
    [Test]
    public void Enumerate_NullDayOfMonth_IsUnchanged()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: null);

        Assert.That(Enumerate(planning, Utc(2026, 1, 1), Utc(2029, 1, 1)), Is.EqualTo(new[]
        {
            Utc(2026, 9, 8), Utc(2027, 9, 8), Utc(2028, 9, 8)
        }));
    }

    /// <summary>
    /// Regression guard (green pre-fix). The render side of the matched cohort:
    /// one row, on the anchor.
    /// </summary>
    [Test]
    public void OccurrencesInWeek_AnchorMatchesDayOfMonth_StartWeekHasExactlyOneRow()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 8);

        Assert.Multiple(() =>
        {
            Assert.That(OccurrencesInWeek(planning, Utc(2026, 9, 7), Utc(2026, 9, 13)),
                Is.EqualTo(new[] { Utc(2026, 9, 8) }));
            Assert.That(OccurrencesInWeek(planning, Utc(2027, 9, 6), Utc(2027, 9, 12)),
                Is.EqualTo(new[] { Utc(2027, 9, 8) }));
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The option-(b) decision, pinned — GREEN ON PRE-FIX CODE BY DESIGN
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Decision pin (green pre-fix). When the start year's pattern date falls
    /// AFTER the anchor (start 2026-09-08, DayOfMonth 20) nothing is lost, so
    /// the anchor is NOT synthesised and 2026 keeps the 20th alone. Two
    /// occurrences in one year would break <c>CompletedPeriodKey</c>'s
    /// one-per-"Y:yyyy" assumption: completing one would silently suppress the
    /// other. Identical to #1207's option (b) for Month.
    /// </summary>
    [Test]
    public void Enumerate_PatternDateAfterAnchorInStartYear_AnchorIsNotAdded()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 20);

        Assert.That(Enumerate(planning, Utc(2026, 1, 1), Utc(2029, 1, 1)), Is.EqualTo(new[]
        {
            Utc(2026, 9, 20), Utc(2027, 9, 20), Utc(2028, 9, 20)
        }));
    }

    /// <summary>
    /// Decision pin (green pre-fix). The render side of the same decision: the
    /// start week is empty because the start year's occurrence is the 20th, a
    /// week later.
    /// </summary>
    [Test]
    public void OccurrencesInWeek_PatternDateAfterAnchorInStartYear_StartWeekIsEmpty()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 20);

        Assert.Multiple(() =>
        {
            Assert.That(OccurrencesInWeek(planning, Utc(2026, 9, 7), Utc(2026, 9, 13)), Is.Empty,
                "the anchor is not synthesised when the year's pattern date follows it");
            // Mon 2026-09-14 .. Sun 2026-09-20 carries the start year's single row.
            Assert.That(OccurrencesInWeek(planning, Utc(2026, 9, 14), Utc(2026, 9, 20)),
                Is.EqualTo(new[] { Utc(2026, 9, 20) }));
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The second divergence: a start week that straddles New Year
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>GetOccurrencesInWeek</c>'s Year branch bailed out on
    /// <c>if (yearsSinceStart &lt; 0) break;</c>. For a series starting Thu
    /// 2026-01-01, the week Mon 2025-12-29 .. Sun 2026-01-04 has
    /// <c>weekStart.Year</c> = 2025, so the branch returned EMPTY while
    /// <c>EnumerateOccurrences</c> — which floors its range at startDate —
    /// yielded 2026-01-01.
    ///
    /// PRE-FIX both the <c>Is.EqualTo(new[] { 2026-01-01 })</c> assertion and
    /// the enumerator-agreement assertion fail (week = [], enumerated =
    /// [2026-01-01]).
    /// </summary>
    [Test]
    public void OccurrencesInWeek_StartWeekStraddlingNewYear_StillRendersTheFirstOccurrence()
    {
        var planning = YearlyPlanning(Utc(2026, 1, 1), repeatEvery: 1, dayOfMonth: null);

        var week = OccurrencesInWeek(planning, Utc(2025, 12, 29), Utc(2026, 1, 4));
        var enumerated = Enumerate(planning, Utc(2025, 12, 29), Utc(2026, 1, 5));

        Assert.Multiple(() =>
        {
            Assert.That(week, Is.EqualTo(new[] { Utc(2026, 1, 1) }),
                "the series' own first occurrence must render in its start week");
            Assert.That(week, Is.EqualTo(enumerated),
                "the two enumerators must agree on a week that straddles New Year");
        });
    }

    /// <summary>
    /// Both Year defects at once: a start week that straddles New Year AND a
    /// legacy DayOfMonth that precedes the start day. Start Fri 2026-01-02 with
    /// DayOfMonth 1 — the week Mon 2025-12-29 .. Sun 2026-01-04 contains both
    /// the pattern date (2026-01-01, before the series) and the anchor.
    ///
    /// PRE-FIX the week returned EMPTY (the <c>yearsSinceStart &lt; 0</c> bail
    /// fired first), so <c>Is.EqualTo(new[] { 2026-01-02 })</c> fails.
    /// </summary>
    [Test]
    public void OccurrencesInWeek_NewYearStraddleWithLegacyDayOfMonth_RendersTheAnchorAlone()
    {
        var planning = YearlyPlanning(Utc(2026, 1, 2), repeatEvery: 1, dayOfMonth: 1);

        var week = OccurrencesInWeek(planning, Utc(2025, 12, 29), Utc(2026, 1, 4));
        var enumerated = Enumerate(planning, Utc(2025, 12, 29), Utc(2026, 1, 5));

        Assert.Multiple(() =>
        {
            Assert.That(week, Is.EqualTo(new[] { Utc(2026, 1, 2) }),
                "the anchor renders; 2026-01-01 precedes the series and must not");
            Assert.That(week, Is.EqualTo(enumerated), "the two enumerators must agree");
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The third date-producing site: the "all"-scope relocation mapper
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>NewPatternDateForPeriodOf</c> physically moves deployed-but-not-
    /// completed Compliance rows onto the rule's current pattern date (#960).
    /// Its Year arm returned the PURE pattern date for every year including the
    /// start year, so once the enumerators started painting the anchor, a start-
    /// year row would have been relocated onto 2026-09-07 — a day the renderer
    /// does NOT paint — while the renderer still emitted 2026-09-08. Both bucket
    /// as <c>CompletedPeriodKey</c> "Y:2026", so completing either would
    /// silently suppress the other: TWO tiles in one calendar year. Exactly the
    /// failure #1207 documented for Month.
    ///
    /// PRE-FIX the first assertion returns 2026-09-07 and fails. (Pre-fix this
    /// was self-consistent with the pre-fix renderer; it becomes wrong the
    /// moment the enumerators become anchor-aware, which is why it moves in the
    /// same change.)
    /// </summary>
    [Test]
    public void NewPatternDateForPeriodOf_YearStartYear_MapsToTheAnchorNotThePatternDate()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 7);
        var arp = new AreaRulePlanning
        {
            RepeatType = YearRepeatType, RepeatEvery = 1,
            RepeatOrdinalWeek = null, DayOfWeek = 2, DayOfMonth = 7,
            StartDate = planning.StartDate
        };

        Assert.Multiple(() =>
        {
            // A deployed row sitting anywhere in the START year maps to the anchor.
            Assert.That(NewPatternDateForPeriodOf(planning, arp, Utc(2026, 9, 7)),
                Is.EqualTo(Utc(2026, 9, 8)),
                "the start year's occurrence IS the anchor since #1217 — not 2026-09-07");
            Assert.That(NewPatternDateForPeriodOf(planning, arp, Utc(2026, 3, 2)),
                Is.EqualTo(Utc(2026, 9, 8)),
                "any date in the start year maps to the start year's single occurrence");
            // Every LATER year is untouched: the pure pattern date, as before.
            Assert.That(NewPatternDateForPeriodOf(planning, arp, Utc(2027, 11, 30)),
                Is.EqualTo(Utc(2027, 9, 7)), "later years keep the pure pattern date");
        });
    }

    /// <summary>
    /// Regression guard (green pre-fix) for the matched cohort: with
    /// DayOfMonth == StartDate.Day the mapper is unchanged in every year.
    /// </summary>
    [Test]
    public void NewPatternDateForPeriodOf_YearMatchedAnchor_IsUnchanged()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 8);
        var arp = new AreaRulePlanning
        {
            RepeatType = YearRepeatType, RepeatEvery = 1,
            RepeatOrdinalWeek = null, DayOfWeek = 2, DayOfMonth = 8,
            StartDate = planning.StartDate
        };

        Assert.Multiple(() =>
        {
            Assert.That(NewPatternDateForPeriodOf(planning, arp, Utc(2026, 3, 2)),
                Is.EqualTo(Utc(2026, 9, 8)));
            Assert.That(NewPatternDateForPeriodOf(planning, arp, Utc(2027, 11, 30)),
                Is.EqualTo(Utc(2027, 9, 8)));
        });
    }

    /// <summary>
    /// Making the Year arm of the mapper anchor-aware must not move
    /// <see cref="BackendConfigurationCalendarService.IsSameRecurrencePeriod"/>,
    /// which applies it twice to decide whether an "all"-scope edit may take the
    /// destructive retract branch. It cannot: the mapper still sends every date
    /// in a calendar year to ONE date inside that same year, so the partition it
    /// induces is unchanged — only the start year's representative moved.
    ///
    /// Green pre-fix by design; it is the invariant, not the fix, that is pinned.
    /// </summary>
    [Test]
    public void IsSameRecurrencePeriod_YearlyAnchorAwareness_DoesNotChangeAnyVerdict()
    {
        var planning = YearlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 7);
        var arp = new AreaRulePlanning
        {
            RepeatType = YearRepeatType, RepeatEvery = 1,
            RepeatOrdinalWeek = null, DayOfWeek = 2, DayOfMonth = 7,
            StartDate = planning.StartDate
        };

        Assert.Multiple(() =>
        {
            // Both inside the START year — same period (both map to the anchor).
            Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, Utc(2026, 2, 3), Utc(2026, 11, 24)),
                Is.True);
            // One inside the start year, one outside — a different period.
            Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, Utc(2026, 11, 24), Utc(2027, 2, 3)),
                Is.False);
            // Two later years — untouched.
            Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, Utc(2027, 2, 3), Utc(2028, 2, 3)),
                Is.False);
            Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, Utc(2027, 2, 3), Utc(2027, 12, 30)),
                Is.True);
        });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Crash guard: legacy DayOfMonth = 0 in the Year date-constructions
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A CRASH GUARD, not a #1217 regression test — the #1217 behaviour it
    /// pins is only "the fix did not make this cohort worse".
    ///
    /// <c>Planning.DayOfMonth</c> is <c>int?</c> and the frontend writes 0 for
    /// some rule shapes (see the note on <c>MonthPatternDateForStartMonth</c>);
    /// <c>DeriveDayOfMonth</c> never returns 0, so this is legacy data only.
    /// All three Year date-constructions built
    /// <c>new DateTime(y, m, Math.Min(0, daysInMonth))</c> — i.e.
    /// <c>new DateTime(y, m, 0)</c> — and threw
    /// <c>ArgumentOutOfRangeException</c>.
    ///
    /// That is PRE-EXISTING, but #1217 widened its reach in
    /// <c>GetOccurrencesInWeek</c>: removing <c>if (yearsSinceStart &lt; 0)
    /// break;</c> newly routes a New-Year-straddling week into the throwing
    /// loop. The week below returned <c>[]</c> before #1217 and threw after it,
    /// and <c>GetTasksForWeek</c>'s outer try turns that into a failure of the
    /// WHOLE week for the property, not just this one series.
    ///
    /// All three sites now go through <c>DayOfMonthPatternDate</c>, whose
    /// <c>dom &lt; 1</c> guard yields null: no pattern date, no anchor (the
    /// anchor predicate is false on a null pattern date), so the series simply
    /// contributes nothing, and the mapper reports the tri-state null that
    /// <c>IsSameRecurrencePeriod</c> already treats as "don't know".
    /// </summary>
    [Test]
    public void YearlyWithZeroDayOfMonth_DoesNotThrowAndContributesNothing()
    {
        var planning = YearlyPlanning(Utc(2026, 1, 1), repeatEvery: 1, dayOfMonth: 0);
        var arp = new AreaRulePlanning
        {
            RepeatType = YearRepeatType, RepeatEvery = 1,
            RepeatOrdinalWeek = null, DayOfWeek = 4, DayOfMonth = 0,
            StartDate = planning.StartDate
        };

        List<DateTime>? straddleWeek = null;
        List<DateTime>? laterWeek = null;
        List<DateTime>? enumerated = null;
        DateTime? mapped = null;

        Assert.Multiple(() =>
        {
            // GetOccurrencesInWeek — the week #1217 newly routed into the loop.
            Assert.That(() => { straddleWeek = OccurrencesInWeek(planning, Utc(2025, 12, 29), Utc(2026, 1, 4)); },
                Throws.Nothing,
                "the New-Year-straddling start week must not throw for a legacy DayOfMonth = 0 row");
            // ... and an ordinary week well inside the series, which reached the
            // same construction before #1217 too.
            Assert.That(() => { laterWeek = OccurrencesInWeek(planning, Utc(2027, 1, 4), Utc(2027, 1, 10)); },
                Throws.Nothing, "an ordinary later week must not throw either");
            // EnumerateOccurrences — its Year loop is unbounded, so the null must
            // break it rather than be skipped.
            Assert.That(() => { enumerated = Enumerate(planning, Utc(2026, 1, 1), Utc(2031, 1, 1)); },
                Throws.Nothing, "the iterator must terminate, not spin or throw");
            // NewPatternDateForPeriodOf — the "all"-scope relocation mapper.
            Assert.That(() => { mapped = NewPatternDateForPeriodOf(planning, arp, Utc(2026, 6, 15)); },
                Throws.Nothing, "the relocation mapper must not throw");
        });

        Assert.Multiple(() =>
        {
            Assert.That(straddleWeek, Is.Empty,
                "a 0 day-of-month has no pattern date, and the anchor predicate is false without one");
            Assert.That(laterWeek, Is.Empty, "same for any other week");
            Assert.That(enumerated, Is.Empty, "and the iterator yields nothing at all");
            Assert.That(mapped, Is.Null,
                "no representable per-period anchor — null, which the relocate path no-ops on");
            Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, Utc(2026, 2, 3), Utc(2027, 2, 3)),
                Is.Null,
                "a null from the mapper is the tri-state \"don't know\"; it must never read as false, "
                + "which is the only value that unlocks the destructive retract branch");
        });
    }

    /// <summary>
    /// The companion to the guard above: nothing about <c>DayOfMonth &gt;= 1</c>
    /// moved when the three sites started sharing
    /// <c>DayOfMonthPatternDate</c>. In particular NO 28-cap leaked into the
    /// Year path — the helper clamps to the candidate month's length only, so a
    /// yearly rule on the 29th/30th/31st still renders on its real day (#922).
    /// Green both before and after the crash guard; it pins the no-op.
    /// </summary>
    [Test]
    public void YearlyWithHighDayOfMonth_KeepsTheRealDayNoTwentyEightCap()
    {
        // 31 January, and 31 clamped only where the month is shorter.
        var jan = YearlyPlanning(Utc(2026, 1, 31), repeatEvery: 1, dayOfMonth: 31);
        // 30 April — a month with 30 days, day-of-month 31 must clamp to 30, not 28.
        var apr = YearlyPlanning(Utc(2026, 4, 1), repeatEvery: 1, dayOfMonth: 31);
        // 29 February in a leap year, 28 in a common one — month-length clamping.
        var feb = YearlyPlanning(Utc(2024, 2, 29), repeatEvery: 1, dayOfMonth: 29);

        Assert.Multiple(() =>
        {
            Assert.That(Enumerate(jan, Utc(2026, 1, 1), Utc(2029, 1, 1)),
                Is.EqualTo(new[] { Utc(2026, 1, 31), Utc(2027, 1, 31), Utc(2028, 1, 31) }),
                "day 31 survives in a 31-day month — no 28-cap");
            Assert.That(OccurrencesInWeek(apr, Utc(2026, 4, 27), Utc(2026, 5, 3)),
                Is.EqualTo(new[] { Utc(2026, 4, 30) }),
                "day 31 clamps to the month's length (30), not to 28");
            Assert.That(Enumerate(feb, Utc(2024, 1, 1), Utc(2027, 1, 1)),
                Is.EqualTo(new[] { Utc(2024, 2, 29), Utc(2025, 2, 28), Utc(2026, 2, 28) }),
                "29 February clamps to 28 in common years by month length, again not by a 28-cap");
        });
    }
}
