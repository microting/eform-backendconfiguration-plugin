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
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using CalendarSvc =
    BackendConfiguration.Pn.Services.BackendConfigurationCalendarService.BackendConfigurationCalendarService;
using ItemsPlanningRepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType;

/// <summary>
/// #1289 — a "5th &lt;weekday&gt;" rule in a month that has only four of that
/// weekday falls back to the LAST (4th) occurrence, in the calendar exactly as in
/// the items-planning scheduler.
///
/// THE SCHEDULER'S RULE lives in another repository
/// (eform-service-items-planning-plugin,
/// ServiceItemsPlanningPlugin/Scheduler/Jobs/SearchListJob.cs, Month branch):
/// <code>
/// var target  = current.AddMonths(planning.RepeatEvery);
/// var snapped = NthWeekdayOfMonth(target.Year, target.Month, ordinal, dow);
/// snapped ??= NthWeekdayOfMonth(target.Year, target.Month, 4, dow)
///             ?? NthWeekdayOfMonth(target.Year, target.Month, 3, dow);
/// </code>
/// where its <c>NthWeekdayOfMonth</c> returns null on a spill.
/// <see cref="SchedulerNthWeekday"/> and <see cref="SchedulerSequence"/> below are
/// a verbatim transcription of that code, so these tests assert "the calendar
/// produces the date the scheduler deploys on" without a cross-repo reference.
///
/// Before #1289 all three calendar Month-date producers skipped such a month
/// (<c>NthWeekdayOfMonth</c> returned null): the scheduler deployed a case on
/// the 4th weekday that the calendar never painted, and the overdue backfill
/// silently skipped the month. The three producers — GetOccurrencesInWeek,
/// EnumerateOccurrences and NewPatternDateForPeriodOf (which also gates the
/// DESTRUCTIVE relocate-vs-retract branch) — must agree, so each is asserted
/// against the same scheduler date.
///
/// Pure: the producers are static and take detached entities, so absolute dates
/// are safe here (no clock involved).
/// </summary>
[TestFixture]
public class CalendarFifthWeekdayFallbackTests
{
    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    // ── Verbatim transcription of SearchListJob (see class doc) ──────────────

    private static DateTime? RawNth(int year, int month, int ordinal, int targetDow)
    {
        var firstOfMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        int dowOffset = (targetDow - (int)firstOfMonth.DayOfWeek + 7) % 7;
        var candidate = firstOfMonth.AddDays(dowOffset + (ordinal - 1) * 7);
        return candidate.Month != month ? null : candidate;
    }

    private static DateTime SchedulerNthWeekday(int year, int month, int ordinal, int dow) =>
        (RawNth(year, month, ordinal, dow)
         ?? RawNth(year, month, 4, dow)
         ?? RawNth(year, month, 3, dow))!.Value;

    /// <summary>
    /// The scheduler's NextExecutionTime chain from the anchor: advance
    /// RepeatEvery months, then snap. The anchor itself is the first execution.
    /// </summary>
    private static List<DateTime> SchedulerSequence(DateTime anchor, int repeatEvery, int ordinal, int dow, int count)
    {
        var result = new List<DateTime> { anchor };
        var current = anchor;
        while (result.Count < count)
        {
            var target = current.AddMonths(repeatEvery);
            current = SchedulerNthWeekday(target.Year, target.Month, ordinal, dow);
            result.Add(current);
        }
        return result;
    }

    // ── Calendar producers ───────────────────────────────────────────────────

    private static Planning MonthlyPlanning(DateTime startDate, int repeatEvery) => new()
    {
        StartDate = startDate,
        RepeatType = ItemsPlanningRepeatType.Month,
        RepeatEvery = repeatEvery,
        DayOfMonth = Math.Min(startDate.Day, 28)
    };

    private static AreaRulePlanning OrdinalArp(int ordinal, int dow) => new()
    {
        RepeatType = 3, RepeatEvery = 1, RepeatOrdinalWeek = ordinal, DayOfWeek = dow
    };

    private static List<DateTime> Enumerate(Planning planning, DateTime from, DateTime toExclusive, int ordinal, int dow) =>
        CalendarSvc.EnumerateOccurrences(planning, from, toExclusive, null, ordinal, dow).ToList();

    private static List<DateTime> OccurrencesInWeek(Planning planning, DateTime weekStart, int ordinal, int dow)
    {
        var mi = typeof(CalendarSvc).GetMethod("GetOccurrencesInWeek", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(mi, Is.Not.Null, "GetOccurrencesInWeek(...) signature changed — update this test");
        return (List<DateTime>)mi!.Invoke(null,
            new object?[] { planning, weekStart, weekStart.AddDays(6), null, ordinal, dow })!;
    }

    private static DateTime? NewPatternDateForPeriodOf(Planning planning, AreaRulePlanning arp, DateTime probe)
    {
        var mi = typeof(CalendarSvc).GetMethod("NewPatternDateForPeriodOf", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(mi, Is.Not.Null, "NewPatternDateForPeriodOf(...) signature changed — update this test");
        return (DateTime?)mi!.Invoke(null, new object?[] { planning, arp, probe });
    }

    private static DateTime MondayOf(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>
    /// The first date in 2026 that is the 5th occurrence of <paramref name="dow"/>
    /// — every weekday has one (a year has 52 weeks + 1–2 days, and 5th
    /// occurrences exist in several months).
    /// </summary>
    private static DateTime FirstFifthOccurrenceIn2026(int dow)
    {
        for (var m = 1; m <= 12; m++)
        {
            var d = RawNth(2026, m, 5, dow);
            if (d.HasValue) return d.Value;
        }
        throw new InvalidOperationException("unreachable");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // All three producers == the scheduler, for every weekday, ordinals 1..5
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// EnumerateOccurrences over two years from a 5th-weekday anchor equals the
    /// scheduler's NextExecutionTime chain — one cell per weekday × RepeatEvery.
    /// Months with only four of the weekday carry the 4th (the last).
    /// </summary>
    [Test]
    public void Enumerate_FifthWeekday_MatchesTheSchedulerChain(
        [Range(0, 6)] int dow, [Values(1, 2)] int repeatEvery)
    {
        var anchor = FirstFifthOccurrenceIn2026(dow);
        var planning = MonthlyPlanning(anchor, repeatEvery);

        var calendar = Enumerate(planning, anchor, anchor.AddYears(2), 5, dow);
        var scheduler = SchedulerSequence(anchor, repeatEvery, 5, dow, calendar.Count + 1)
            .Where(d => d < anchor.AddYears(2)).ToList();

        Assert.That(calendar, Is.EqualTo(scheduler));
        Assert.That(calendar.Any(d => d.Day <= 28), Is.True,
            "premise: at least one month in range has only four — the fallback is exercised");
    }

    /// <summary>Ordinals 1..4 always exist, so the fallback must not change them.</summary>
    [Test]
    public void Enumerate_OrdinalsOneToFour_MatchTheSchedulerChain([Range(1, 4)] int ordinal, [Range(0, 6)] int dow)
    {
        var anchor = RawNth(2026, 1, ordinal, dow)!.Value;
        var planning = MonthlyPlanning(anchor, 1);

        var end = anchor.AddYears(1);
        var calendar = Enumerate(planning, anchor, end, ordinal, dow);

        Assert.That(calendar, Is.EqualTo(SchedulerSequence(anchor, 1, ordinal, dow, 14).Where(d => d < end)));
    }

    /// <summary>
    /// GetOccurrencesInWeek: the week containing each month's scheduler date
    /// renders exactly that date — including four-weekday months, which rendered
    /// NOTHING before #1289.
    /// </summary>
    [Test]
    public void OccurrencesInWeek_FifthWeekday_RendersTheSchedulerDateEveryMonth([Range(0, 6)] int dow)
    {
        var anchor = FirstFifthOccurrenceIn2026(dow);
        var planning = MonthlyPlanning(anchor, 1);

        foreach (var expected in SchedulerSequence(anchor, 1, 5, dow, 13))
        {
            var week = OccurrencesInWeek(planning, MondayOf(expected), 5, dow);
            Assert.That(week, Is.EqualTo(new[] { expected }),
                $"week of {expected:yyyy-MM-dd}: the calendar must paint the scheduler's date");
        }
    }

    /// <summary>
    /// NewPatternDateForPeriodOf (relocation target + the #1122 retract gate):
    /// every probe day in a month maps to that month's scheduler date — never
    /// null any more for a four-weekday month.
    /// </summary>
    [Test]
    public void NewPatternDateForPeriodOf_FifthWeekday_MapsEveryMonthToTheSchedulerDate([Range(0, 6)] int dow)
    {
        var anchor = FirstFifthOccurrenceIn2026(dow);
        var planning = MonthlyPlanning(anchor, 1);
        var arp = OrdinalArp(5, dow);

        foreach (var expected in SchedulerSequence(anchor, 1, 5, dow, 13).Skip(1))
        {
            foreach (var probeDay in new[] { 1, 15, 28 })
            {
                var probe = Utc(expected.Year, expected.Month, probeDay);
                Assert.That(NewPatternDateForPeriodOf(planning, arp, probe), Is.EqualTo(expected),
                    $"probe {probe:yyyy-MM-dd}");
            }
        }
    }

    /// <summary>
    /// The three producers agree with each other for the 5th-weekday rule, for a
    /// two-year window, weekday by weekday — the invariant that keeps the
    /// destructive retract branch honest.
    /// </summary>
    [Test]
    public void AllThreeProducers_FifthWeekday_Agree([Range(0, 6)] int dow)
    {
        var anchor = FirstFifthOccurrenceIn2026(dow);
        var planning = MonthlyPlanning(anchor, 1);
        var arp = OrdinalArp(5, dow);

        foreach (var enumerated in Enumerate(planning, anchor, anchor.AddYears(2), 5, dow))
        {
            Assert.That(OccurrencesInWeek(planning, MondayOf(enumerated), 5, dow), Does.Contain(enumerated));
            Assert.That(NewPatternDateForPeriodOf(planning, arp, enumerated), Is.EqualTo(enumerated));
        }
    }

    /// <summary>
    /// The #1122 gate on a four-weekday month now gives a DEFINITE verdict (it
    /// used to answer null = "don't know" there): same month ⇒ same period,
    /// different month ⇒ different period. The partition is still one period per
    /// calendar month, so no pair can flip into a false "different" verdict.
    /// </summary>
    [Test]
    public void IsSameRecurrencePeriod_FifthWeekday_FourWeekdayMonth_IsDefinite()
    {
        // 2026-01-29 is the 5th Thursday of January 2026; February 2026 has four.
        var planning = MonthlyPlanning(Utc(2026, 1, 29), 1);
        var arp = OrdinalArp(5, (int)DayOfWeek.Thursday);

        Assert.Multiple(() =>
        {
            Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, Utc(2026, 2, 3), Utc(2026, 2, 20)),
                Is.True, "two days of February share February's period");
            Assert.That(CalendarSvc.IsSameRecurrencePeriod(planning, arp, Utc(2026, 2, 3), Utc(2026, 3, 3)),
                Is.False, "February and March are different periods");
            Assert.That(NewPatternDateForPeriodOf(planning, arp, Utc(2026, 2, 10)), Is.EqualTo(Utc(2026, 2, 26)),
                "February 2026's last Thursday — what SearchListJob deploys on");
        });
    }

    /// <summary>OrdinalWeekOf against a brute-force count, for every day 1..31.</summary>
    [Test]
    public void OrdinalWeekOf_MatchesBruteForce([Range(1, 31)] int day)
    {
        var d = Utc(2026, 12, day); // December has 31 days
        var bruteForce = Enumerable.Range(1, day).Count(x => Utc(2026, 12, x).DayOfWeek == d.DayOfWeek);
        Assert.That(CalendarSvc.OrdinalWeekOf(d), Is.EqualTo(bruteForce));
    }
}
