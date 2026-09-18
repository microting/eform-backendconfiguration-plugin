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
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using CalendarSvc =
    BackendConfiguration.Pn.Services.BackendConfigurationCalendarService.BackendConfigurationCalendarService;
using ItemsPlanningRepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType;

/// <summary>
/// #1302 / #1140 — the task list opened its edit modal on the SERIES START
/// (<c>tasks/index</c> TaskDate = <c>arp.StartDate</c>), so every series that
/// started before today opened read-only. The index now also reports the next
/// rule occurrences (<c>GetUpcomingOccurrenceDates</c>), built only from the
/// calendar's own <c>EnumerateOccurrences</c> + <c>ApplyRepeatEndBound</c>.
///
/// One case per repeat kind × start position (long past / recent past /
/// future) plus the end bounds (until date, after N — both live and
/// exhausted), a suppressed (deleted/moved) occurrence and a one-off.
///
/// Pure fixture: the helper is static and takes detached entities and an
/// explicit "from" date, so absolute dates are safe — nothing reads the clock.
/// FROM is Thursday 17 September 2026.
/// </summary>
[TestFixture]
public class TaskListUpcomingOccurrenceTests
{
    private static readonly DateTime From = Day(2026, 9, 17);

    private static DateTime Day(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// ARP repeat type (1 day, 2 week, 3 month, 4 year) and the matching
    /// items-planning Planning, exactly as CreateTask persists the pair.
    /// </summary>
    private static (Planning Planning, AreaRulePlanning Arp) Series(
        int repeatType, int repeatEvery, DateTime start,
        string? weekdaysCsv = null, int? ordinalWeek = null, int? dayOfWeek = null, int? dayOfMonth = null,
        int? endMode = null, int? occurrences = null, DateTime? until = null)
    {
        var planning = new Planning
        {
            StartDate = start,
            RepeatType = (ItemsPlanningRepeatType)repeatType,
            RepeatEvery = repeatEvery,
            DayOfMonth = dayOfMonth,
        };
        var arp = new AreaRulePlanning
        {
            StartDate = start,
            RepeatType = repeatType,
            RepeatEvery = repeatEvery,
            RepeatWeekdaysCsv = weekdaysCsv,
            RepeatOrdinalWeek = ordinalWeek,
            DayOfWeek = dayOfWeek ?? 0,
            DayOfMonth = dayOfMonth ?? 0,
            RepeatEndMode = endMode,
            RepeatOccurrences = occurrences,
            RepeatUntilDate = until,
        };
        return (planning, arp);
    }

    private static List<string> Upcoming((Planning Planning, AreaRulePlanning Arp) series,
        ICollection<DateTime>? suppressed = null)
        => CalendarSvc.GetUpcomingOccurrenceDates(series.Planning, series.Arp, From,
                suppressed ?? new HashSet<DateTime>(), CalendarSvc.UpcomingOccurrenceCount)
            .Select(Iso).ToList();

    private static IEnumerable<TestCaseData> KindCases()
    {
        // Started three years ago: the client-side getAllOccurrences caps a
        // daily rule at its first 250 dates, so it could not answer this one.
        yield return new TestCaseData(Series(1, 1, Day(2023, 9, 17)),
                new[] { "2026-09-17", "2026-09-18", "2026-09-19" })
            .SetName("Daily_StartedYearsAgo_TodayAndOn");
        // Phase kept from the anchor: 1, 4, 7, …, 16, 19.
        yield return new TestCaseData(Series(1, 3, Day(2026, 9, 1)),
                new[] { "2026-09-19", "2026-09-22", "2026-09-25" })
            .SetName("EveryThreeDays_PhaseFromAnchor");
        // Legacy single-day weekly (no CSV): the start's weekday (Monday).
        yield return new TestCaseData(Series(2, 1, Day(2025, 1, 6)),
                new[] { "2026-09-21", "2026-09-28", "2026-10-05" })
            .SetName("WeeklyLegacySingleDay_StartWeekday");
        // Multi-day weekly (Mon + Wed).
        yield return new TestCaseData(Series(2, 1, Day(2025, 1, 6), weekdaysCsv: "1,3"),
                new[] { "2026-09-21", "2026-09-23", "2026-09-28" })
            .SetName("WeeklyMultiDay_MonWed");
        // Every 2nd week on Thursday, anchored 3 Sep: 3 Sep, 17 Sep, 1 Oct, 15 Oct.
        yield return new TestCaseData(Series(2, 2, Day(2026, 9, 3), weekdaysCsv: "4"),
                new[] { "2026-09-17", "2026-10-01", "2026-10-15" })
            .SetName("EveryTwoWeeks_Thursday_IncludesToday");
        // Monthly on the 10th — September's is already past.
        yield return new TestCaseData(Series(3, 1, Day(2026, 1, 10), dayOfMonth: 10),
                new[] { "2026-10-10", "2026-11-10", "2026-12-10" })
            .SetName("MonthlyDayOfMonth_SkipsThisMonthsPastDate");
        // Monthly on the 2nd Tuesday (8 Sep is past).
        yield return new TestCaseData(Series(3, 1, Day(2026, 1, 13), ordinalWeek: 2, dayOfWeek: 2),
                new[] { "2026-10-13", "2026-11-10", "2026-12-08" })
            .SetName("MonthlyNthWeekday_SecondTuesday");
        // Yearly on 14 March.
        yield return new TestCaseData(Series(4, 1, Day(2024, 3, 14), dayOfMonth: 14),
                new[] { "2027-03-14", "2028-03-14", "2029-03-14" })
            .SetName("Yearly_NextYearsDate");
        // Every 2nd year from 2024: 2026 is past, so 2028 onward.
        yield return new TestCaseData(Series(4, 2, Day(2024, 3, 14), dayOfMonth: 14),
                new[] { "2028-03-14", "2030-03-14", "2032-03-14" })
            .SetName("EveryTwoYears_PhaseFromAnchor");
        // A series starting in the FUTURE on a non-rule weekday: its first
        // real occurrence, never the bare start date (1 Oct is a Thursday).
        yield return new TestCaseData(Series(2, 1, Day(2026, 10, 1), weekdaysCsv: "1"),
                new[] { "2026-10-05", "2026-10-12", "2026-10-19" })
            .SetName("FutureStart_FirstRealOccurrence");
    }

    [TestCaseSource(nameof(KindCases))]
    public void UpcomingOccurrences_PerRepeatKind((Planning Planning, AreaRulePlanning Arp) series,
        string[] expected)
    {
        Assert.That(Upcoming(series), Is.EqualTo(expected));
    }

    [Test]
    public void UntilDate_TruncatesInclusively()
    {
        var series = Series(1, 1, Day(2026, 9, 1), endMode: 2, until: Day(2026, 9, 18));
        Assert.That(Upcoming(series), Is.EqualTo(new[] { "2026-09-17", "2026-09-18" }));
    }

    [Test]
    public void UntilDate_InThePast_SeriesEnded_IsEmpty()
    {
        var series = Series(1, 1, Day(2026, 9, 1), endMode: 2, until: Day(2026, 9, 10));
        Assert.That(Upcoming(series), Is.Empty);
    }

    [Test]
    public void AfterN_CountsFromTheAnchor_NotFromToday()
    {
        // 9 daily occurrences from 10 Sep: 10 … 18 Sep.
        var series = Series(1, 1, Day(2026, 9, 10), endMode: 1, occurrences: 9);
        Assert.That(Upcoming(series), Is.EqualTo(new[] { "2026-09-17", "2026-09-18" }));
    }

    [Test]
    public void AfterN_Exhausted_IsEmpty()
    {
        var series = Series(1, 1, Day(2026, 9, 10), endMode: 1, occurrences: 5);
        Assert.That(Upcoming(series), Is.Empty);
    }

    [Test]
    public void SuppressedOriginalDate_IsSkipped()
    {
        // Today's occurrence was deleted (or moved away) via an exception:
        // it no longer renders on 17 Sep, so it must not be offered.
        var series = Series(1, 1, Day(2026, 9, 1));
        Assert.That(Upcoming(series, new HashSet<DateTime> { Day(2026, 9, 17) }),
            Is.EqualTo(new[] { "2026-09-18", "2026-09-19", "2026-09-20" }));
    }

    [TestCase(0, TestName = "OneOff_RepeatTypeZero_IsEmpty")]
    [TestCase(null, TestName = "OneOff_RepeatTypeNull_IsEmpty")]
    public void NonRecurring_IsEmpty(int? arpRepeatType)
    {
        var series = Series(1, 1, Day(2026, 9, 1));
        series.Arp.RepeatType = arpRepeatType;
        Assert.That(Upcoming(series), Is.Empty);
    }
}
