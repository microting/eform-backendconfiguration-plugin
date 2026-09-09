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
using System.Reflection;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Services.CalendarAssignmentReconciliation;
using BackendConfiguration.Pn.Services.CalendarChangeNotification;
using BackendConfiguration.Pn.Services.EventDeployService;
using BackendConfiguration.Pn.Services.WorkerTagMembership;
using Microsoft.Extensions.Logging.Abstractions;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using NSubstitute;
using CalendarSvc =
    BackendConfiguration.Pn.Services.BackendConfigurationCalendarService.BackendConfigurationCalendarService;
using ItemsPlanningRepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType;

/// <summary>
/// #1207 — a monthly rule must emit its own StartDate as occurrence #1.
///
/// The report: "I create a task on Tuesday 8 September 2026 and set the
/// repetition to every 12 months, monthly on the first Tuesday. The task is
/// created the first time on Tuesday 7 September 2027, which is wrong."
/// 1 September 2026 is itself a Tuesday, so the start date is the SECOND
/// Tuesday of its month and does not satisfy the rule the user picked. Both
/// enumerators treated StartDate purely as a lower bound on a pure pattern:
/// the start month's pattern date (2026-09-01) sorts before the anchor and was
/// discarded by the <c>&gt;= rangeStart</c> / <c>&gt;= weekStart</c> guard,
/// after which the cursor jumped a whole repeatEvery period. The first
/// occurrence was lost.
///
/// New semantics: <b>the anchor is occurrence #1; the pattern governs #2
/// onward</b>, for RepeatType.Month ONLY (both arms — Nth-weekday AND
/// day-of-month-number).
///
/// The start-month duplicate question is settled by option (b): the anchor is
/// emitted only when the start month's pattern date is STRICTLY EARLIER than
/// it, i.e. only when an occurrence would otherwise be lost. A month never
/// carries two occurrences, which is what CompletedPeriodKey's "M:yyyy-MM"
/// bucket depends on.
///
/// This fixture is pure — the enumerators are static and take a detached
/// Planning, so no database is involved. The sibling
/// <see cref="CalendarMonthlyAnchorRenderTests"/> drives the same series
/// through the real <c>GetTasksForWeek</c>.
/// </summary>
[TestFixture]
public class CalendarMonthlyAnchorOccurrenceTests
{
    private const int YearRepeatType = 4; // the items-planning enum has no Year member

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static Planning MonthlyPlanning(DateTime startDate, int repeatEvery, int? dayOfMonth) => new()
    {
        StartDate = startDate,
        RepeatType = ItemsPlanningRepeatType.Month,
        RepeatEvery = repeatEvery,
        DayOfMonth = dayOfMonth
    };

    private static List<DateTime> Enumerate(
        Planning planning, DateTime fromInclusive, DateTime toExclusive,
        string? repeatWeekdaysCsv = null, int? repeatOrdinalWeek = null, int? dayOfWeekOverride = null) =>
        CalendarSvc.EnumerateOccurrences(planning, fromInclusive, toExclusive,
            repeatWeekdaysCsv, repeatOrdinalWeek, dayOfWeekOverride).ToList();

    /// <summary>
    /// GetOccurrencesInWeek is private; reached by reflection exactly as
    /// <see cref="CalendarOccurrencesWeekTests"/> does.
    /// </summary>
    private static List<DateTime> OccurrencesInWeek(
        Planning planning, DateTime weekStart, DateTime weekEnd,
        string? repeatWeekdaysCsv = null, int? repeatOrdinalWeek = null, int? dayOfWeekOverride = null)
    {
        var mi = typeof(BackendConfigurationCalendarService).GetMethod(
            "GetOccurrencesInWeek", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(mi, Is.Not.Null, "GetOccurrencesInWeek(...) signature changed — update this test");
        return (List<DateTime>)mi!.Invoke(null,
            new object?[] { planning, weekStart, weekEnd, repeatWeekdaysCsv, repeatOrdinalWeek, dayOfWeekOverride })!;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The customer's exact case
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public void Enumerate_CustomerCase_Every12MonthsFirstTuesday_StartsOnTheAnchorNotAYearLater()
    {
        // Start Tue 2026-09-08, every 12 months, "first Tuesday", DayOfMonth 0
        // (exactly what the custom dialog POSTs for monthlyFirstWeekday).
        var planning = MonthlyPlanning(Utc(2026, 9, 8), repeatEvery: 12, dayOfMonth: 0);

        var occurrences = Enumerate(planning, Utc(2026, 1, 1), Utc(2032, 1, 1),
            repeatOrdinalWeek: 1, dayOfWeekOverride: 2 /* Tuesday */);

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 9, 8),  // the anchor — was missing entirely before #1207
            Utc(2027, 9, 7),  // 1st Tuesday of September 2027
            Utc(2028, 9, 5),
            Utc(2029, 9, 4),
            Utc(2030, 9, 3),
            Utc(2031, 9, 2)
        }));
    }

    [Test]
    public void OccurrencesInWeek_CustomerCase_AnchorWeekRendersTheAnchorOnce()
    {
        var planning = MonthlyPlanning(Utc(2026, 9, 8), repeatEvery: 12, dayOfMonth: 0);

        // Mon 2026-09-07 .. Sun 2026-09-13.
        var anchorWeek = OccurrencesInWeek(planning, Utc(2026, 9, 7), Utc(2026, 9, 13),
            repeatOrdinalWeek: 1, dayOfWeekOverride: 2);
        Assert.That(anchorWeek, Is.EqualTo(new[] { Utc(2026, 9, 8) }));

        // Mon 2026-08-31 .. Sun 2026-09-06 — 2026-09-01 is the rule's date for
        // September but it precedes the series start and must not leak in.
        var weekBefore = OccurrencesInWeek(planning, Utc(2026, 8, 31), Utc(2026, 9, 6),
            repeatOrdinalWeek: 1, dayOfWeekOverride: 2);
        Assert.That(weekBefore, Is.Empty);

        // Mon 2027-09-06 .. Sun 2027-09-12 — the first patterned occurrence.
        var nextYearWeek = OccurrencesInWeek(planning, Utc(2027, 9, 6), Utc(2027, 9, 12),
            repeatOrdinalWeek: 1, dayOfWeekOverride: 2);
        Assert.That(nextYearWeek, Is.EqualTo(new[] { Utc(2027, 9, 7) }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // No duplicate when the anchor already satisfies the rule
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public void Enumerate_AnchorIsTheRulesOwnDate_EmitsItExactlyOnce()
    {
        // 2026-09-01 IS the 1st Tuesday of September 2026.
        var planning = MonthlyPlanning(Utc(2026, 9, 1), repeatEvery: 12, dayOfMonth: 0);

        var occurrences = Enumerate(planning, Utc(2026, 1, 1), Utc(2030, 1, 1),
            repeatOrdinalWeek: 1, dayOfWeekOverride: 2);

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 9, 1), Utc(2027, 9, 7), Utc(2028, 9, 5), Utc(2029, 9, 4)
        }));
        Assert.That(occurrences.Count(d => d == Utc(2026, 9, 1)), Is.EqualTo(1),
            "the anchor must not be emitted twice when it already is the pattern date");
    }

    [Test]
    public void OccurrencesInWeek_AnchorIsTheRulesOwnDate_StartWeekHasExactlyOneRow()
    {
        var planning = MonthlyPlanning(Utc(2026, 9, 1), repeatEvery: 12, dayOfMonth: 0);

        // Mon 2026-08-31 .. Sun 2026-09-06 contains the anchor 2026-09-01.
        var week = OccurrencesInWeek(planning, Utc(2026, 8, 31), Utc(2026, 9, 6),
            repeatOrdinalWeek: 1, dayOfWeekOverride: 2);

        Assert.That(week, Is.EqualTo(new[] { Utc(2026, 9, 1) }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // repeatEvery = 1 — the same defect, one month lost instead of one year
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public void Enumerate_EveryMonthFirstTuesday_MismatchedAnchor_KeepsTheAnchorAndThenPatterns()
    {
        var planning = MonthlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 0);

        var occurrences = Enumerate(planning, Utc(2026, 1, 1), Utc(2027, 1, 1),
            repeatOrdinalWeek: 1, dayOfWeekOverride: 2);

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 9, 8),   // anchor
            Utc(2026, 10, 6),  // 1st Tuesday of October
            Utc(2026, 11, 3),
            Utc(2026, 12, 1)
        }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The day-of-month-number arm has the identical defect
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public void Enumerate_DayOfMonthArm_MismatchedAnchor_KeepsTheAnchor()
    {
        // "the 1st of every month", anchored on the 8th.
        var planning = MonthlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 1);

        var occurrences = Enumerate(planning, Utc(2026, 1, 1), Utc(2026, 12, 1));

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 9, 8), Utc(2026, 10, 1), Utc(2026, 11, 1)
        }));
    }

    [Test]
    public void Enumerate_DayOfMonthArm_AnchorMatchesTheDayNumber_NoDuplicate()
    {
        var planning = MonthlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 8);

        var occurrences = Enumerate(planning, Utc(2026, 1, 1), Utc(2026, 12, 1));

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 9, 8), Utc(2026, 10, 8), Utc(2026, 11, 8)
        }));
    }

    [Test]
    public void Enumerate_DayOfMonthArm_EveryTwelveMonths_MismatchedAnchor_KeepsTheAnchor()
    {
        // #1207 CASE 4: the day-number arm loses the start date for a whole year.
        var planning = MonthlyPlanning(Utc(2026, 9, 8), repeatEvery: 12, dayOfMonth: 1);

        var occurrences = Enumerate(planning, Utc(2026, 1, 1), Utc(2030, 1, 1));

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 9, 8), Utc(2027, 9, 1), Utc(2028, 9, 1), Utc(2029, 9, 1)
        }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Option (b): the anchor is NOT added when the pattern date follows it
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The start-month duplicate decision, pinned. Start 2026-09-08 with "3rd
    /// Tuesday" already yields 2026-09-15 in the start month, so nothing is
    /// lost and the anchor is NOT added — September carries the 15th alone.
    /// Two occurrences in one month would break CompletedPeriodKey's
    /// one-per-"M:yyyy-MM" assumption: completing one would silently suppress
    /// the other.
    /// </summary>
    [Test]
    public void Enumerate_PatternDateAfterAnchorInStartMonth_AnchorIsNotAdded()
    {
        var planning = MonthlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 0);

        var occurrences = Enumerate(planning, Utc(2026, 1, 1), Utc(2026, 12, 1),
            repeatOrdinalWeek: 3, dayOfWeekOverride: 2);

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 9, 15), Utc(2026, 10, 20), Utc(2026, 11, 17)
        }));
        Assert.That(occurrences, Does.Not.Contain(Utc(2026, 9, 8)),
            "option (b): never two occurrences in the start month");
    }

    [Test]
    public void OccurrencesInWeek_PatternDateAfterAnchorInStartMonth_StartWeekIsEmpty()
    {
        var planning = MonthlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 0);

        // Mon 2026-09-07 .. Sun 2026-09-13 contains the anchor but not the 15th.
        var week = OccurrencesInWeek(planning, Utc(2026, 9, 7), Utc(2026, 9, 13),
            repeatOrdinalWeek: 3, dayOfWeekOverride: 2);

        Assert.That(week, Is.Empty);
    }

    [Test]
    public void Enumerate_DayOfMonthArm_PatternDateAfterAnchorInStartMonth_AnchorIsNotAdded()
    {
        // "the 20th of every month", anchored on the 8th: the 20th of the start
        // month is still ahead, so nothing is lost.
        var planning = MonthlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 20);

        var occurrences = Enumerate(planning, Utc(2026, 1, 1), Utc(2026, 12, 1));

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 9, 20), Utc(2026, 10, 20), Utc(2026, 11, 20)
        }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The render-path drift (#1207 EXTRA A) is closed
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// GetOccurrencesInWeek's Month branch gated only on <c>weekStart</c>, so a
    /// pattern date BEFORE the series start painted in the week grid while
    /// EnumerateOccurrences (which floors its range at StartDate) yielded
    /// nothing — the two enumerators disagreed. Start Fri 2026-09-04 with "1st
    /// Tuesday": the week Mon 2026-08-31..Sun 2026-09-06 used to return
    /// 2026-09-01.
    /// </summary>
    [Test]
    public void OccurrencesInWeek_PatternDateBeforeSeriesStart_DoesNotLeak()
    {
        var planning = MonthlyPlanning(Utc(2026, 9, 4), repeatEvery: 1, dayOfMonth: 0);

        var week = OccurrencesInWeek(planning, Utc(2026, 8, 31), Utc(2026, 9, 6),
            repeatOrdinalWeek: 1, dayOfWeekOverride: 2);

        Assert.That(week, Does.Not.Contain(Utc(2026, 9, 1)),
            "a pattern date before the series start must never render");
        Assert.That(week, Is.EqualTo(new[] { Utc(2026, 9, 4) }),
            "only the anchor itself, which #1207 now emits");

        // ... and the range enumerator agrees, which it did not before.
        var enumerated = Enumerate(planning, Utc(2026, 8, 31), Utc(2026, 9, 7),
            repeatOrdinalWeek: 1, dayOfWeekOverride: 2);
        Assert.That(enumerated, Is.EqualTo(week),
            "the two enumerators must agree over the same window");
    }

    [Test]
    public void OccurrencesInWeek_DayOfMonthArm_PatternDateBeforeSeriesStart_DoesNotLeak()
    {
        // Same shape on the day-number arm: "the 1st", anchored Fri 2026-09-04.
        var planning = MonthlyPlanning(Utc(2026, 9, 4), repeatEvery: 1, dayOfMonth: 1);

        var week = OccurrencesInWeek(planning, Utc(2026, 8, 31), Utc(2026, 9, 6));

        Assert.That(week, Is.EqualTo(new[] { Utc(2026, 9, 4) }));
    }

    /// <summary>
    /// The anchor and a LATER pattern date share one week: start Mon
    /// 2026-08-31 with "1st Tuesday" emits the anchor (August's 1st Tuesday,
    /// 2026-08-04, precedes it) and then September's 1st Tuesday, 2026-09-01,
    /// four days later — both inside Mon 2026-08-31..Sun 2026-09-06.
    ///
    /// HONEST SCOPE: this does NOT exercise <c>occurrences.Sort()</c>. Under
    /// option (b) the anchor is emitted only when the start month's pattern
    /// date precedes it, so the anchor is always the strict minimum of the
    /// week and it is added first — the list is ascending with or without the
    /// sort. What this test DOES discriminate is the #1207 behaviour itself:
    /// pre-fix the week returned <c>[2026-09-01]</c> alone (the anchor was
    /// never emitted), so the expected two-element value is red on old code.
    /// The <c>Is.Ordered</c> assertion is a cheap standing invariant, not
    /// coverage of the sort call.
    /// </summary>
    [Test]
    public void OccurrencesInWeek_AnchorAndPatternDateInOneWeek_IsAscending()
    {
        // Start Mon 2026-08-31 with "1st Tuesday": August's 1st Tuesday
        // (2026-08-04) precedes the anchor, so the anchor is emitted; the same
        // week also carries September's 1st Tuesday, 2026-09-01.
        var planning = MonthlyPlanning(Utc(2026, 8, 31), repeatEvery: 1, dayOfMonth: 0);

        var week = OccurrencesInWeek(planning, Utc(2026, 8, 31), Utc(2026, 9, 6),
            repeatOrdinalWeek: 1, dayOfWeekOverride: 2);

        Assert.That(week, Is.EqualTo(new[] { Utc(2026, 8, 31), Utc(2026, 9, 1) }));
        Assert.That(week, Is.Ordered, "the week list must always be ascending");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // After-N counting shifts by one — deliberate
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The anchor now counts as occurrence 1, so an "Efter N forekomster"
    /// series ends one period earlier than it used to. #1207 CASE 3 with N = 3
    /// used to end 2026-12-01; it now ends 2026-11-03.
    /// </summary>
    [Test]
    public void ApplyRepeatEndBound_AfterThreeOccurrences_CountsTheAnchorAsTheFirst()
    {
        var planning = MonthlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 0);
        var arp = new AreaRulePlanning
        {
            RepeatType = 3,
            RepeatEvery = 1,
            RepeatOrdinalWeek = 1,
            DayOfWeek = 2,
            RepeatEndMode = 1,
            RepeatOccurrences = 3,
            StartDate = planning.StartDate
        };

        var occurrences = Enumerate(planning, Utc(2026, 1, 1), Utc(2027, 1, 1),
            repeatOrdinalWeek: 1, dayOfWeekOverride: 2);
        CalendarSvc.ApplyRepeatEndBound(planning, arp, occurrences, Utc(2026, 12, 31));

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 9, 8), Utc(2026, 10, 6), Utc(2026, 11, 3)
        }));
        Assert.That(occurrences[^1], Is.EqualTo(Utc(2026, 11, 3)),
            "the anchor is occurrence 1, so the 3rd is November — not December");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Out-of-scope repeat types must stay exactly as they were
    // ═════════════════════════════════════════════════════════════════════════
    //
    // These are deliberate regression tripwires: #1207 is scoped to
    // RepeatType.Month only. A later "completion" of the fix that extends the
    // anchor rule to Week or Day turns them red.
    //
    // Year is NO LONGER out of scope: #1217 gave it its own anchor rule (and
    // its own helper, YearStartAnchorIsDroppedOccurrence) because a yearly rule
    // whose planning.DayOfMonth diverges from StartDate.Day has the identical
    // defect. The yearly tripwire below still passes UNCHANGED, and must: its
    // DayOfMonth is null, so the start year's pattern date EQUALS the anchor
    // and the strict "<" in the new helper declines to synthesise anything.
    // CalendarYearlyAnchorOccurrenceTests covers the diverging cohort.

    /// <summary>
    /// Green before AND after #1217 by construction — a yearly series with a
    /// null DayOfMonth anchors on StartDate's own month + day, so nothing about
    /// its sequence moves. Kept as the "the ordinary yearly cohort did not
    /// shift" guard.
    /// </summary>
    [Test]
    public void Enumerate_Yearly_IsUnchanged()
    {
        var planning = new Planning
        {
            StartDate = Utc(2026, 9, 8),
            RepeatType = (ItemsPlanningRepeatType)YearRepeatType,
            RepeatEvery = 1
        };

        var occurrences = Enumerate(planning, Utc(2026, 1, 1), Utc(2029, 1, 1));

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 9, 8), Utc(2027, 9, 8), Utc(2028, 9, 8)
        }));
    }

    [Test]
    public void Enumerate_WeeklyCsvExcludingTheStartWeekday_StillSkipsTheAnchor()
    {
        // Start Tue 2026-09-08 but the CSV says Wednesday only. Deselecting
        // your own start weekday is a coherent instruction, not a defect.
        var planning = new Planning
        {
            StartDate = Utc(2026, 9, 8),
            RepeatType = ItemsPlanningRepeatType.Week,
            RepeatEvery = 1
        };

        var occurrences = Enumerate(planning, Utc(2026, 9, 1), Utc(2026, 9, 25),
            repeatWeekdaysCsv: "3");

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 9, 9), Utc(2026, 9, 16), Utc(2026, 9, 23)
        }));
        Assert.That(occurrences, Does.Not.Contain(Utc(2026, 9, 8)));
    }

    [Test]
    public void Enumerate_Daily_IsUnchanged()
    {
        var planning = new Planning
        {
            StartDate = Utc(2026, 9, 8),
            RepeatType = ItemsPlanningRepeatType.Day,
            RepeatEvery = 3
        };

        var occurrences = Enumerate(planning, Utc(2026, 9, 1), Utc(2026, 9, 20));

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 9, 8), Utc(2026, 9, 11), Utc(2026, 9, 14), Utc(2026, 9, 17)
        }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Defensive: DayOfMonth = 0 with no ordinal must not throw
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>Math.Min(DayOfMonth ?? .., 28)</c> is 0 for the DayOfMonth = 0 rows
    /// the frontend writes, and <c>new DateTime(y, m, 0)</c> throws
    /// ArgumentOutOfRangeException. Two separate things are pinned here:
    ///
    /// <list type="number">
    /// <item>the ordinal arm NEVER evaluates the day-of-month expression, so
    /// the ordinary "monthlyFirstWeekday" row shape (RepeatOrdinalWeek = 1,
    /// DayOfMonth = 0) renders without throwing;</item>
    /// <item>the <c>dom &lt; 1</c> guard inside
    /// <c>MonthPatternDateForStartMonth</c> really does return null for the
    /// degenerate shape, asserted DIRECTLY on the private helper.</item>
    /// </list>
    ///
    /// The guard cannot be reached through <c>GetOccurrencesInWeek</c>: doing
    /// so needs RepeatOrdinalWeek = null with DayOfMonth = 0, and then the
    /// pattern LOOP's own pre-existing <c>new DateTime(y, m, 0)</c> throws
    /// before anything #1207 added runs. That crash predates #1207 and is out
    /// of scope, so the guard is exercised on the helper instead.
    /// </summary>
    [Test]
    public void OccurrencesInWeek_OrdinalRule_WithDayOfMonthZero_DoesNotThrow()
    {
        var planning = MonthlyPlanning(Utc(2026, 9, 8), repeatEvery: 12, dayOfMonth: 0);

        Assert.DoesNotThrow(() =>
        {
            OccurrencesInWeek(planning, Utc(2026, 9, 7), Utc(2026, 9, 13),
                repeatOrdinalWeek: 1, dayOfWeekOverride: 2);
        });

        // The dom < 1 guard itself, on the shared helper both enumerators use.
        Assert.That(MonthPatternDateForStartMonth(planning, Utc(2026, 9, 8),
                repeatOrdinalWeek: null, dayOfWeekOverride: null), Is.Null,
            "DayOfMonth = 0 is not a pattern date — the helper must return null, not throw");
        // ... and with an ordinal present the day-of-month expression is never
        // reached at all, so the same row shape yields a real date.
        Assert.That(MonthPatternDateForStartMonth(planning, Utc(2026, 9, 8),
                repeatOrdinalWeek: 1, dayOfWeekOverride: 2), Is.EqualTo(Utc(2026, 9, 1)));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The 28-cap cohort: every wizard monthly series started on the 29th–31st
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>BackendConfigurationTaskWizardService.DeriveDayOfMonth</c> caps a
    /// monthly <c>Planning.DayOfMonth</c> at 28
    /// (<c>dayOfMonth &gt; 28 ? 28 : dayOfMonth</c>), so EVERY plain wizard
    /// monthly series started on the 29th, 30th or 31st is stored with
    /// DayOfMonth = 28 and is therefore a MISMATCHED anchor: the start month's
    /// pattern date (the 28th) sorts strictly before the start date. No custom
    /// dialog and no legacy conversion is involved — this is the ordinary
    /// create path, and it is the largest cohort #1207 touches.
    ///
    /// Start Sat 2026-01-31, DayOfMonth 28, every 1 month, no ordinal:
    /// pre-#1207 the series' FIRST occurrence was 2026-02-28 — the start date
    /// itself never rendered and never deployed. It now leads the sequence.
    /// Note the pattern months come from <c>startDate.AddMonths(n)</c>, whose
    /// day component is irrelevant (only year+month are read), so the tail is
    /// a plain 28th-of-every-month.
    /// </summary>
    [Test]
    public void Enumerate_WizardTwentyEightCap_StartOnThe31st_KeepsTheAnchor()
    {
        var planning = MonthlyPlanning(Utc(2026, 1, 31), repeatEvery: 1, dayOfMonth: 28);

        var occurrences = Enumerate(planning, Utc(2026, 1, 1), Utc(2026, 6, 1));

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Utc(2026, 1, 31),  // the anchor — pre-#1207 the sequence began at 2026-02-28
            Utc(2026, 2, 28),
            Utc(2026, 3, 28),
            Utc(2026, 4, 28),
            Utc(2026, 5, 28)
        }));
        Assert.That(occurrences[0], Is.EqualTo(Utc(2026, 1, 31)),
            "the 28-cap makes the anchor mismatched; it must still be occurrence #1");
    }

    [Test]
    public void OccurrencesInWeek_WizardTwentyEightCap_StartOnThe31st_RendersAnchorAndNotTheCappedDay()
    {
        var planning = MonthlyPlanning(Utc(2026, 1, 31), repeatEvery: 1, dayOfMonth: 28);

        // Mon 2026-01-26 .. Sun 2026-02-01 holds BOTH the capped pattern date
        // (Wed 2026-01-28) and the anchor (Sat 2026-01-31). Only the anchor may
        // render: the 28th precedes the series start.
        var anchorWeek = OccurrencesInWeek(planning, Utc(2026, 1, 26), Utc(2026, 2, 1));
        Assert.That(anchorWeek, Is.EqualTo(new[] { Utc(2026, 1, 31) }),
            "the capped 28th precedes the series start and must not leak in");

        // Mon 2026-02-23 .. Sun 2026-03-01 — the first patterned occurrence.
        var febWeek = OccurrencesInWeek(planning, Utc(2026, 2, 23), Utc(2026, 3, 1));
        Assert.That(febWeek, Is.EqualTo(new[] { Utc(2026, 2, 28) }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The "all"-scope relocation must land on the SAME date the renderer paints
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>NewPatternDateForPeriodOf</c> is the third date-producing site for a
    /// Month rule: <c>RelocateNonCompletedComplianceRowsToNewPattern</c> (#960)
    /// physically moves deployed-but-not-completed Compliance rows onto it when
    /// an "all"-scope edit keeps the new anchor inside the same recurrence
    /// period and out of the past.
    ///
    /// Before this fix it returned the PURE pattern date, so a September row of
    /// a "1st Tuesday" series re-anchored to Tue 2026-09-08 was relocated to
    /// 2026-09-01 while the recurrence loop emitted 2026-09-08 — the compliance
    /// loop painted the 1st, <c>complianceDateSet</c> therefore did not contain
    /// the 8th, and September carried TWO tiles seven days apart. Both map to
    /// <c>CompletedPeriodKey</c> "M:2026-09", so completing either silently
    /// suppressed the other.
    ///
    /// Unit-style on purpose: the mapper is a pure private static, so this
    /// needs no Compliance rows, no SDK core and no database.
    /// </summary>
    [Test]
    public void NewPatternDateForPeriodOf_MonthStartMonth_MapsToTheAnchorNotThePatternDate()
    {
        var planning = MonthlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 0);
        var arp = new AreaRulePlanning
        {
            RepeatType = 3, RepeatEvery = 1,
            RepeatOrdinalWeek = 1, DayOfWeek = 2 /* Tuesday */, DayOfMonth = 0,
            StartDate = planning.StartDate
        };

        // A deployed row sitting anywhere in the START month maps to the anchor.
        Assert.That(NewPatternDateForPeriodOf(planning, arp, Utc(2026, 9, 20)),
            Is.EqualTo(Utc(2026, 9, 8)),
            "the start month's occurrence IS the anchor since #1207 — not 2026-09-01");

        // Every LATER month is untouched: the pure pattern date, as before.
        Assert.That(NewPatternDateForPeriodOf(planning, arp, Utc(2026, 10, 22)),
            Is.EqualTo(Utc(2026, 10, 6)), "1st Tuesday of October 2026");

        // And the mapper agrees with what the renderer paints, in both months —
        // the property whose absence produced two September tiles.
        var september = OccurrencesInWeek(planning, Utc(2026, 9, 7), Utc(2026, 9, 13),
            repeatOrdinalWeek: 1, dayOfWeekOverride: 2);
        Assert.That(september, Is.EqualTo(new[] { Utc(2026, 9, 8) }));
        Assert.That(Enumerate(planning, Utc(2026, 9, 1), Utc(2026, 11, 1),
                repeatOrdinalWeek: 1, dayOfWeekOverride: 2),
            Is.EqualTo(new[] { Utc(2026, 9, 8), Utc(2026, 10, 6) }),
            "exactly one occurrence per calendar month, which CompletedPeriodKey requires");
    }

    /// <summary>
    /// The day-of-month arm of the same mapper, on the 28-cap cohort: a
    /// January row of a series anchored 2026-01-31 with DayOfMonth 28 must
    /// relocate onto 2026-01-31, not onto the capped 2026-01-28.
    /// </summary>
    [Test]
    public void NewPatternDateForPeriodOf_DayOfMonthArm_TwentyEightCap_MapsToTheAnchor()
    {
        var planning = MonthlyPlanning(Utc(2026, 1, 31), repeatEvery: 1, dayOfMonth: 28);
        var arp = new AreaRulePlanning
        {
            RepeatType = 3, RepeatEvery = 1,
            RepeatOrdinalWeek = null, DayOfWeek = 6, DayOfMonth = 28,
            StartDate = planning.StartDate
        };

        Assert.That(NewPatternDateForPeriodOf(planning, arp, Utc(2026, 1, 15)),
            Is.EqualTo(Utc(2026, 1, 31)));
        Assert.That(NewPatternDateForPeriodOf(planning, arp, Utc(2026, 2, 10)),
            Is.EqualTo(Utc(2026, 2, 28)), "later months keep the pure pattern date");
    }

    /// <summary>
    /// Making the mapper anchor-aware must not move
    /// <see cref="BackendConfigurationCalendarService.IsSameRecurrencePeriod"/>,
    /// which applies it twice to decide whether an "all"-scope edit may take
    /// the destructive retract branch. It cannot: the mapper still sends every
    /// date in a calendar month to ONE date inside that same month, so the
    /// partition it induces is unchanged — only the start month's
    /// representative moved.
    /// </summary>
    [Test]
    public void IsSameRecurrencePeriod_MonthlyAnchorAwareness_DoesNotChangeAnyVerdict()
    {
        var planning = MonthlyPlanning(Utc(2026, 9, 8), repeatEvery: 1, dayOfMonth: 0);
        var arp = new AreaRulePlanning
        {
            RepeatType = 3, RepeatEvery = 1,
            RepeatOrdinalWeek = 1, DayOfWeek = 2, DayOfMonth = 0,
            StartDate = planning.StartDate
        };

        // Both inside the START month — same period (both now map to the anchor).
        Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, Utc(2026, 9, 2), Utc(2026, 9, 25)),
            Is.True);
        // One inside the start month, one outside — still a different period.
        Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, Utc(2026, 9, 25), Utc(2026, 10, 2)),
            Is.False);
        Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, Utc(2026, 8, 25), Utc(2026, 9, 2)),
            Is.False);
        // Two months, neither of them the start month — untouched.
        Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, Utc(2026, 10, 2), Utc(2026, 11, 2)),
            Is.False);
        Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, Utc(2026, 10, 2), Utc(2026, 10, 30)),
            Is.True);
    }

    /// <summary>
    /// <c>NewPatternDateForPeriodOf</c> is private static; reached by
    /// reflection exactly as <c>GetOccurrencesInWeek</c> is above.
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
    /// <c>MonthPatternDateForStartMonth</c> is private static; same reflection
    /// approach.
    /// </summary>
    private static DateTime? MonthPatternDateForStartMonth(
        Planning planning, DateTime startDate, int? repeatOrdinalWeek, int? dayOfWeekOverride)
    {
        var mi = typeof(BackendConfigurationCalendarService).GetMethod(
            "MonthPatternDateForStartMonth", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(mi, Is.Not.Null, "MonthPatternDateForStartMonth(...) signature changed — update this test");
        return (DateTime?)mi!.Invoke(null,
            new object?[] { planning, startDate, repeatOrdinalWeek, dayOfWeekOverride });
    }
}

/// <summary>
/// #1207 through the real week query. The pure sibling above pins the
/// enumerators; this fixture proves the customer's series actually renders on
/// its start date in <c>GetTasksForWeek</c> — the path
/// <c>EventDeployService.EnsureDeployedAsync</c> and every gRPC/mobile read
/// consume.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class CalendarMonthlyAnchorRenderTests : TestBaseSetup
{
    private static string IsoUtc(DateTime d) =>
        DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Seeded(int PropertyId, int ArpId, int PlanningId, int AreaId);

    /// <summary>
    /// Seeds a monthly calendar series directly (no wizard, no conversion) so
    /// the anchor / rule mismatch can be expressed exactly as the custom
    /// dialog persists it: RepeatOrdinalWeek + DayOfWeek on the ARP,
    /// DayOfMonth = 0.
    /// </summary>
    private async Task<Seeded> SeedMonthlySeries(
        DateTime startDate, int repeatEvery, int? repeatOrdinalWeek, int dayOfWeek, int dayOfMonth,
        int? repeatEndMode = null, int? repeatOccurrences = null)
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
            Name = $"MonthlyAnchor-{Guid.NewGuid()}", ItemPlanningTagId = 0,
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

        var planning = new Planning
        {
            Enabled = true, RepeatEvery = repeatEvery,
            RepeatType = ItemsPlanningRepeatType.Month,
            StartDate = startDate, DayOfMonth = dayOfMonth, RelatedEFormId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var arp = new AreaRulePlanning
        {
            AreaRuleId = areaRule.Id, PropertyId = property.Id, AreaId = area.Id,
            ItemPlanningId = planning.Id, StartDate = startDate, Status = true,
            RepeatType = 3, RepeatEvery = repeatEvery,
            RepeatOrdinalWeek = repeatOrdinalWeek, DayOfWeek = dayOfWeek, DayOfMonth = dayOfMonth,
            RepeatEndMode = repeatEndMode, RepeatOccurrences = repeatOccurrences,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(arp);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        await BackendConfigurationPnDbContext.CalendarConfigurations.AddAsync(new CalendarConfiguration
        {
            AreaRulePlanningId = arp.Id, StartHour = 8.0, Duration = 1.0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        return new Seeded(property.Id, arp.Id, planning.Id, area.Id);
    }

    private BackendConfigurationCalendarService BuildCalendarService(eFormCore.Core core)
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
            ItemsPlanningPnDbContext!, Substitute.For<IBackendConfigurationTaskWizardService>(),
            Substitute.For<ICalendarAssignmentReconciliationService>(),
            Substitute.For<ICalendarChangeNotifier>(),
            NullLogger<BackendConfigurationCalendarService>.Instance,
            Substitute.For<ICalendarOccurrenceRetractionService>(),
            Substitute.For<ICalendarPastSeriesBackfillService>(),
            Substitute.For<IBackendConfigurationComplianceReportService>(),
            new WorkerTagMembershipService(coreHelper));
    }

    private async Task<List<string>> QueryWeekDates(int propertyId, int arpId, DateTime weekStartMonday)
        => (await QueryWeekRows(propertyId, arpId, weekStartMonday)).Select(t => t.TaskDate).ToList();

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

    [Test]
    public async Task GetTasksForWeek_CustomerCase_RendersTheAnchorWeekAndThenTheYearlyPattern()
    {
        // Start Tue 2026-09-08, every 12 months, 1st Tuesday, DayOfMonth 0.
        var seeded = await SeedMonthlySeries(
            Utc(2026, 9, 8), repeatEvery: 12, repeatOrdinalWeek: 1, dayOfWeek: 2, dayOfMonth: 0);

        var anchorWeek = await QueryWeekDates(seeded.PropertyId, seeded.ArpId, Utc(2026, 9, 7));
        Assert.That(anchorWeek, Is.EqualTo(new[] { "2026-09-08" }),
            "the start week must render exactly one occurrence — on the start date");

        var weekBefore = await QueryWeekDates(seeded.PropertyId, seeded.ArpId, Utc(2026, 8, 31));
        Assert.That(weekBefore, Is.Empty,
            "2026-09-01 is the rule's date but precedes the series start — it must not leak in");

        var nextYearWeek = await QueryWeekDates(seeded.PropertyId, seeded.ArpId, Utc(2027, 9, 6));
        Assert.That(nextYearWeek, Is.EqualTo(new[] { "2027-09-07" }),
            "the first patterned occurrence is September 2027's 1st Tuesday");
    }

    [Test]
    public async Task GetTasksForWeek_AnchorMatchesTheRule_StartWeekHasExactlyOneRow()
    {
        // 2026-09-01 IS the 1st Tuesday — no duplicate may appear.
        var seeded = await SeedMonthlySeries(
            Utc(2026, 9, 1), repeatEvery: 12, repeatOrdinalWeek: 1, dayOfWeek: 2, dayOfMonth: 0);

        var startWeek = await QueryWeekDates(seeded.PropertyId, seeded.ArpId, Utc(2026, 8, 31));

        Assert.That(startWeek, Is.EqualTo(new[] { "2026-09-01" }));
    }

    [Test]
    public async Task GetTasksForWeek_PatternDateBeforeSeriesStart_DoesNotRender()
    {
        // Start Fri 2026-09-04 with "1st Tuesday" (#1207 EXTRA A).
        var seeded = await SeedMonthlySeries(
            Utc(2026, 9, 4), repeatEvery: 1, repeatOrdinalWeek: 1, dayOfWeek: 2, dayOfMonth: 0);

        var week = await QueryWeekDates(seeded.PropertyId, seeded.ArpId, Utc(2026, 8, 31));

        Assert.That(week, Does.Not.Contain("2026-09-01"),
            "a pattern date before the series start must never render");
        Assert.That(week, Is.EqualTo(new[] { "2026-09-04" }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // After-N ends one period earlier — through the real render path
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// #1207 item 3, reproduced end to end rather than by calling
    /// <c>ApplyRepeatEndBound</c> directly. The anchor now counts as occurrence
    /// 1, so an "Efter N forekomster" series stops one period earlier than it
    /// used to. Start Tue 2026-09-08, every 1 month, "1st Tuesday", N = 3:
    ///
    ///   pre-#1207  occurrences = 2026-10-06, 2026-11-03, 2026-12-01
    ///   #1207      occurrences = 2026-09-08, 2026-10-06, 2026-11-03
    ///
    /// Both ends of that shift are asserted through <c>GetTasksForWeek</c>,
    /// which is the path <c>EventDeployService.EnsureDeployedAsync</c> and
    /// every gRPC/mobile read consume — so this pins what actually deploys, not
    /// just what the bound helper computes. Every week here is queried in
    /// isolation, and <c>ApplyRepeatEndBound</c> re-derives the cumulative count
    /// from the series anchor each time, so the four queries are independent.
    ///
    /// Fixed 2026 dates on purpose: this is a pure render/bound assertion and
    /// no move-into-past guard is on the read path.
    /// </summary>
    [Test]
    public async Task GetTasksForWeek_AfterThreeOccurrences_LastOccurrenceIsNovemberNotDecember()
    {
        var seeded = await SeedMonthlySeries(
            Utc(2026, 9, 8), repeatEvery: 1, repeatOrdinalWeek: 1, dayOfWeek: 2, dayOfMonth: 0,
            repeatEndMode: 1, repeatOccurrences: 3);

        // #1 — the anchor. Pre-#1207 this week was empty and the count started
        // at October, which is exactly why the series ran one month too long.
        var anchorWeek = await QueryWeekDates(seeded.PropertyId, seeded.ArpId, Utc(2026, 9, 7));
        Assert.That(anchorWeek, Is.EqualTo(new[] { "2026-09-08" }),
            "the anchor is occurrence #1 and must render inside the after-N bound");

        // #2 and #3.
        var octoberWeek = await QueryWeekDates(seeded.PropertyId, seeded.ArpId, Utc(2026, 10, 5));
        Assert.That(octoberWeek, Is.EqualTo(new[] { "2026-10-06" }));

        var novemberWeek = await QueryWeekDates(seeded.PropertyId, seeded.ArpId, Utc(2026, 11, 2));
        Assert.That(novemberWeek, Is.EqualTo(new[] { "2026-11-03" }),
            "the 3rd occurrence still renders");

        // The pre-#1207 last occurrence. 2026-12-01 is the 1st Tuesday of
        // December and its week is Mon 2026-11-30..Sun 2026-12-06.
        var decemberWeek = await QueryWeekDates(seeded.PropertyId, seeded.ArpId, Utc(2026, 11, 30));
        Assert.That(decemberWeek, Is.Empty,
            "with the anchor counted as #1 the series ends at the 3rd — December is past the bound");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The 28-cap cohort — through the real render path
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>BackendConfigurationTaskWizardService.DeriveDayOfMonth</c> caps a
    /// monthly <c>Planning.DayOfMonth</c> at 28, so EVERY ordinary wizard
    /// monthly series started on the 29th, 30th or 31st is a mismatched anchor
    /// — no custom dialog and no legacy conversion involved. It is the largest
    /// cohort #1207 touches, and the pure sibling covers it only at the
    /// enumerator level.
    ///
    /// Start Sat 2026-01-31, DayOfMonth 28, every 1 month. The week
    /// Mon 2026-01-26..Sun 2026-02-01 holds BOTH the capped pattern date
    /// (Wed 2026-01-28) and the anchor (Sat 2026-01-31).
    ///
    /// Pre-#1207 that week rendered <c>2026-01-28</c> — a date BEFORE the
    /// series start, the render-path drift of EXTRA A — and never rendered the
    /// anchor at all. Both halves of the assertion below are therefore red on
    /// old code.
    /// </summary>
    [Test]
    public async Task GetTasksForWeek_WizardTwentyEightCap_StartOnThe31st_RendersTheAnchorNotTheCappedDay()
    {
        var seeded = await SeedMonthlySeries(
            Utc(2026, 1, 31), repeatEvery: 1, repeatOrdinalWeek: null, dayOfWeek: 6, dayOfMonth: 28);

        var anchorWeek = await QueryWeekDates(seeded.PropertyId, seeded.ArpId, Utc(2026, 1, 26));
        Assert.That(anchorWeek, Does.Not.Contain("2026-01-28"),
            "the capped 28th precedes the series start and must not render");
        Assert.That(anchorWeek, Is.EqualTo(new[] { "2026-01-31" }),
            "the anchor is occurrence #1 for the 28-cap cohort too");

        // The first patterned occurrence: Sat 2026-02-28, week Mon 2026-02-23.
        var febWeek = await QueryWeekDates(seeded.PropertyId, seeded.ArpId, Utc(2026, 2, 23));
        Assert.That(febWeek, Is.EqualTo(new[] { "2026-02-28" }),
            "the tail is a plain 28th-of-every-month");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The CompletedPeriodKey hazard is not reachable
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>CompletedPeriodKey</c> buckets a Month rule as <c>"M:yyyy-MM"</c> —
    /// one occurrence per calendar month. Option (b) exists to protect that:
    /// the anchor is emitted only when the start month's pattern date sorts
    /// STRICTLY EARLIER than it, so the start month can never carry two.
    ///
    /// This drives the hazard through the real week query. Series anchored Tue
    /// 2026-03-10 (the 2nd Tuesday) under a "1st Tuesday" rule — March's
    /// pattern date is 2026-03-03, which precedes the anchor, so #1207 emits
    /// 2026-03-10 as March's single occurrence. That occurrence is then
    /// COMPLETED (backing SDK case Status = 100, Compliance soft-deleted — the
    /// canonical completed shape). March must render exactly one tile, marked
    /// completed, and no sibling anywhere else in the month; April must be
    /// unaffected, because the freeze is period-scoped.
    ///
    /// HONEST SCOPE: this test is a TRIPWIRE, not a #1207 regression test — on
    /// pre-#1207 code every assertion below still passes. Each week passes for
    /// its own reason, and neither reason is the "M:2026-03" period
    /// suppression the scenario is built around:
    /// <list type="bullet">
    /// <item><b>Anchor week</b> (Mon 2026-03-09) — pre-#1207 the recurrence
    /// loop emits nothing here at all: March's pattern date 2026-03-03 sorts
    /// before weekStart and is dropped by the <c>&gt;= weekStart</c> guard, so
    /// the one tile is the compliance row on its own. Post-#1207 the anchor IS
    /// emitted and is then swallowed by the per-(planning, date) dedup gate,
    /// because the completed compliance occupies 2026-03-10 — the period gate
    /// sits behind it as a second line. So the count assertion is vacuous on
    /// old code but load-bearing on new code: it pins that #1207's new anchor
    /// row is absorbed by the completed compliance rather than stacked on it.
    /// </item>
    /// <item><b>Pattern week</b> (Mon 2026-03-02) — empty on old and new code
    /// alike, and NOT because anything suppressed the pattern date.
    /// <c>GetOccurrencesInWeek</c> leaves its Month arm immediately on the
    /// pre-existing <c>if (startDate &gt; weekEnd) break;</c> guard (the series
    /// starts 2026-03-10, this week ends 2026-03-08), and the compliance query
    /// is itself scoped to the requested week. The completed-period
    /// suppression is never reached, so 2026-03-03 is unreachable through the
    /// week query for this series in the first place — which is what the
    /// section heading above means by "not reachable".</item>
    /// </list>
    /// Note also that the option (a) / option (b) decision is NOT what this
    /// test discriminates: this series' pattern date sorts strictly before its
    /// anchor, so option (a) ("always emit the anchor") and option (b) emit
    /// exactly the same thing here. A test that trips on that decision would
    /// have to seed a series whose pattern date sorts AFTER its anchor.
    /// </summary>
    [Test]
    public async Task Tripwire_GetTasksForWeek_CompletedAnchorInStartMonth_MonthCarriesExactlyOneTile()
    {
        // 2026-03-01 is a Sunday, so the 1st Tuesday of March 2026 is the 3rd
        // and the 2nd Tuesday — the anchor — is the 10th.
        var seeded = await SeedMonthlySeries(
            Utc(2026, 3, 10), repeatEvery: 1, repeatOrdinalWeek: 1, dayOfWeek: 2, dayOfMonth: 0);

        // Boot a real SDK core first (same order as
        // CalendarCompletedPeriodSuppressionTests) so the completed case is
        // readable through the calendar service's own SDK context.
        await GetCore();
        var language = MicrotingDbContext!.Languages.OrderBy(x => x.Id).First();
        var sdkSite = new Microting.eForm.Infrastructure.Data.Entities.Site
        {
            Name = $"monthly-anchor-completed-{Guid.NewGuid()}",
            MicrotingUid = 7373,
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Sites.AddAsync(sdkSite);
        await MicrotingDbContext.SaveChangesAsync();

        var completedCase = new Microting.eForm.Infrastructure.Data.Entities.Case
        {
            SiteId = sdkSite.Id, Status = 100, WorkflowState = Constants.WorkflowStates.Created
        };
        await MicrotingDbContext.Cases.AddAsync(completedCase);
        await MicrotingDbContext.SaveChangesAsync();

        // The completed occurrence sits on the ANCHOR — the date #1207 made
        // renderable. The canonical complete path sets Status = 100 and then
        // soft-deletes the Compliance row, so that is the shape seeded here.
        var anchor = Utc(2026, 3, 10);
        await BackendConfigurationPnDbContext!.Compliances.AddAsync(new Compliance
        {
            PlanningId = seeded.PlanningId, PropertyId = seeded.PropertyId, AreaId = seeded.AreaId,
            Deadline = anchor, StartDate = anchor.AddDays(-30),
            MicrotingSdkCaseId = completedCase.Id, MicrotingSdkeFormId = 0,
            WorkflowState = Constants.WorkflowStates.Removed
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Anchor week Mon 2026-03-09..Sun 2026-03-15: exactly one tile, the
        // completed one. The recurrence loop also generates 2026-03-10 now, and
        // it must be suppressed rather than stacked on top.
        var anchorWeek = await QueryWeekRows(seeded.PropertyId, seeded.ArpId, Utc(2026, 3, 9));
        Assert.That(anchorWeek, Has.Count.EqualTo(1),
            "the completed anchor must render exactly once — no not-completed sibling on the same date");
        Assert.That(anchorWeek[0].TaskDate, Is.EqualTo("2026-03-10"));
        Assert.That(anchorWeek[0].Completed, Is.True);
        Assert.That(anchorWeek[0].IsFromCompliance, Is.True);

        // The rule's own March date (Tue 2026-03-03) is in the PREVIOUS week and
        // precedes the series start, so nothing may render there. See HONEST
        // SCOPE above: this week is empty on old and new code alike, and the
        // reason is the Month arm's `startDate > weekEnd` early return, not the
        // "M:2026-03" completed-period suppression — that gate is never
        // reached here.
        var patternWeek = await QueryWeekDates(seeded.PropertyId, seeded.ArpId, Utc(2026, 3, 2));
        Assert.That(patternWeek, Is.Empty,
            "March carries ONE occurrence; a second tile would share the 'M:2026-03' bucket");

        // April is a different period: the rule renders normally, not completed.
        var aprilWeek = await QueryWeekRows(seeded.PropertyId, seeded.ArpId, Utc(2026, 4, 6));
        Assert.That(aprilWeek.Select(t => t.TaskDate), Is.EqualTo(new[] { "2026-04-07" }),
            "the completed-period freeze is scoped to the start month");
        Assert.That(aprilWeek[0].Completed, Is.False);
    }
}
