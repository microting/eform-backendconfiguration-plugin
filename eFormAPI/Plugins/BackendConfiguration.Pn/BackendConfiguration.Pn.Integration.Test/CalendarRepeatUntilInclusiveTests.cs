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
/// #1293 — "Til og med torsdag den 10. december 2026" means the task appears for
/// the LAST time ON 10 December. The until date is a calendar day.
///
/// Before the fix the browser sent the picked local midnight as
/// <c>toISOString()</c> — a UTC+1 user picking 10 Dec sent
/// <c>2026-12-09T23:00Z</c>, a UTC+2 (summer) user <c>…T22:00Z</c> — and
/// <c>ApplyRepeatEndBound</c> compared instants (<c>d &gt; RepeatUntilDate</c>),
/// so the 10 Dec occurrence (00:00) was dropped. The fix compares by DAY and
/// recovers the intended day from legacy tz-shifted rows on read
/// (<c>NormalizeRepeatUntilDate</c>, nearest-midnight rounding — the #966
/// pattern), so no data migration is needed.
///
/// Pure fixture: <c>ApplyRepeatEndBound</c> and <c>NormalizeRepeatUntilDate</c>
/// are static and take detached entities; the occurrence lists are written out
/// by hand so the bound is tested independently of the enumerators. Absolute
/// dates are safe here — nothing reads the clock.
/// </summary>
[TestFixture]
public class CalendarRepeatUntilInclusiveTests
{
    private static DateTime Day(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime Parse(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static Planning WeeklyPlanning(DateTime start) => new()
    {
        StartDate = start,
        RepeatType = ItemsPlanningRepeatType.Week,
        RepeatEvery = 1
    };

    private static AreaRulePlanning UntilArp(DateTime? repeatUntilDate) => new()
    {
        RepeatType = 2,
        RepeatEvery = 1,
        RepeatEndMode = 2,
        RepeatUntilDate = repeatUntilDate
    };

    /// <summary>Thursdays 19 Nov → 17 Dec 2026 (10 Dec 2026 is a Thursday).</summary>
    private static List<DateTime> ThursdaysAroundTenDecember() =>
    [
        Day(2026, 11, 19), Day(2026, 11, 26), Day(2026, 12, 3), Day(2026, 12, 10), Day(2026, 12, 17)
    ];

    // ═════════════════════════════════════════════════════════════════════════
    // NormalizeRepeatUntilDate — the stored value → the intended calendar day
    // ═════════════════════════════════════════════════════════════════════════

    [TestCase("2026-12-10T00:00:00", "2026-12-10", TestName = "Normalize_DateOnlyMidnight_IsUnchanged")]
    [TestCase("2026-12-09T23:00:00", "2026-12-10", TestName = "Normalize_LegacyUtcPlus1_2300_RoundsUpToTheIntendedDay")]
    [TestCase("2026-12-09T22:00:00", "2026-12-10", TestName = "Normalize_LegacyUtcPlus2_2200_RoundsUpToTheIntendedDay")]
    [TestCase("2026-12-10T05:00:00", "2026-12-10", TestName = "Normalize_LegacyUtcMinus5_0500_RoundsDownToTheIntendedDay")]
    [TestCase("2026-12-30T23:00:00", "2026-12-31", TestName = "Normalize_Legacy31December_StaysInTheOldYear")]
    [TestCase("2026-12-31T23:00:00", "2027-01-01", TestName = "Normalize_Legacy1January_CrossesIntoTheNewYear")]
    public void NormalizeRepeatUntilDate_RecoversTheIntendedDay(string stored, string expectedDay)
    {
        var normalized = CalendarSvc.NormalizeRepeatUntilDate(Parse(stored));

        Assert.Multiple(() =>
        {
            Assert.That(normalized, Is.Not.Null);
            Assert.That(normalized!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Is.EqualTo(expectedDay));
            Assert.That(normalized.Value.TimeOfDay, Is.EqualTo(TimeSpan.Zero), "a day, not an instant");
            Assert.That(normalized.Value.Kind, Is.EqualTo(DateTimeKind.Unspecified),
                "Unspecified serialises as yyyy-MM-ddT00:00:00 (no Z) so the browser reads the day, not a shifted instant");
        });
    }

    [Test]
    public void NormalizeRepeatUntilDate_Null_StaysNull()
    {
        Assert.That(CalendarSvc.NormalizeRepeatUntilDate(null), Is.Null);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ApplyRepeatEndBound — "until" is inclusive, by day
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The customer's case: weekly on Thursday, until Thursday 10 Dec 2026. The
    /// 10 Dec occurrence is the LAST one, for every way the until date may be
    /// stored: the date-only value the FE sends now, and the legacy UTC+1 /
    /// UTC+2 tz-shifted instants rows written before #1293 still carry.
    /// </summary>
    [TestCase("2026-12-10T00:00:00", TestName = "Until_OnAnOccurrenceDate_DateOnly_IncludesThatOccurrence")]
    [TestCase("2026-12-09T23:00:00", TestName = "Until_OnAnOccurrenceDate_LegacyUtcPlus1_IncludesThatOccurrence")]
    [TestCase("2026-12-09T22:00:00", TestName = "Until_OnAnOccurrenceDate_LegacyUtcPlus2Dst_IncludesThatOccurrence")]
    public void ApplyRepeatEndBound_UntilOnAnOccurrenceDate_IsInclusive(string stored)
    {
        var planning = WeeklyPlanning(Day(2026, 11, 19));
        var occurrences = ThursdaysAroundTenDecember();

        CalendarSvc.ApplyRepeatEndBound(planning, UntilArp(Parse(stored)), occurrences, Day(2026, 12, 31));

        Assert.That(occurrences, Is.EqualTo(new[]
        {
            Day(2026, 11, 19), Day(2026, 11, 26), Day(2026, 12, 3), Day(2026, 12, 10)
        }), "til og med 10. december: 10 Dec is the last occurrence, 17 Dec is dropped");
    }

    /// <summary>
    /// Until Saturday 12 Dec — not an occurrence day. The Thursday before it is
    /// the last occurrence; the Thursday after it is not.
    /// </summary>
    [TestCase("2026-12-12T00:00:00", TestName = "Until_OnANonOccurrenceDate_DateOnly_EndsAtThePrecedingOccurrence")]
    [TestCase("2026-12-11T23:00:00", TestName = "Until_OnANonOccurrenceDate_LegacyUtcPlus1_EndsAtThePrecedingOccurrence")]
    public void ApplyRepeatEndBound_UntilOnANonOccurrenceDate_EndsAtThePrecedingOccurrence(string stored)
    {
        var planning = WeeklyPlanning(Day(2026, 11, 19));
        var occurrences = ThursdaysAroundTenDecember();

        CalendarSvc.ApplyRepeatEndBound(planning, UntilArp(Parse(stored)), occurrences, Day(2026, 12, 31));

        Assert.That(occurrences.Last(), Is.EqualTo(Day(2026, 12, 10)));
        Assert.That(occurrences, Has.Count.EqualTo(4));
    }

    /// <summary>
    /// The day before the until date is not "the last day": until Wednesday
    /// 9 Dec (date-only) must drop the 10 Dec Thursday. Guards against an
    /// over-correction that would round every until date up a day.
    /// </summary>
    [Test]
    public void ApplyRepeatEndBound_UntilTheDayBeforeAnOccurrence_ExcludesThatOccurrence()
    {
        var planning = WeeklyPlanning(Day(2026, 11, 19));
        var occurrences = ThursdaysAroundTenDecember();

        CalendarSvc.ApplyRepeatEndBound(planning, UntilArp(Parse("2026-12-09T00:00:00")), occurrences,
            Day(2026, 12, 31));

        Assert.That(occurrences.Last(), Is.EqualTo(Day(2026, 12, 3)));
    }

    /// <summary>
    /// 31 Dec → 1 Jan year boundary on a daily series: until 31 Dec keeps 31 Dec
    /// and drops 1 Jan; until 1 Jan keeps 1 Jan. Both the date-only and the
    /// legacy UTC+1 form (which for 1 Jan is stored in the PREVIOUS year).
    /// </summary>
    [TestCase("2026-12-31T00:00:00", 2026, 12, 31, TestName = "Until_31December_DateOnly_KeepsNewYearsEveDropsNewYearsDay")]
    [TestCase("2026-12-30T23:00:00", 2026, 12, 31, TestName = "Until_31December_LegacyUtcPlus1_KeepsNewYearsEveDropsNewYearsDay")]
    [TestCase("2027-01-01T00:00:00", 2027, 1, 1, TestName = "Until_1January_DateOnly_KeepsNewYearsDay")]
    [TestCase("2026-12-31T23:00:00", 2027, 1, 1, TestName = "Until_1January_LegacyUtcPlus1StoredInTheOldYear_KeepsNewYearsDay")]
    public void ApplyRepeatEndBound_YearBoundary(string stored, int y, int m, int d)
    {
        var planning = new Planning
        {
            StartDate = Day(2026, 12, 29),
            RepeatType = ItemsPlanningRepeatType.Day,
            RepeatEvery = 1
        };
        var occurrences = Enumerable.Range(0, 6).Select(i => Day(2026, 12, 29).AddDays(i)).ToList(); // 29 Dec .. 3 Jan

        CalendarSvc.ApplyRepeatEndBound(planning, UntilArp(Parse(stored)), occurrences, Day(2027, 1, 31));

        Assert.That(occurrences.Last(), Is.EqualTo(Day(y, m, d)));
        Assert.That(occurrences, Has.None.GreaterThan(Day(y, m, d)));
    }

    /// <summary>
    /// An occurrence value that carries a time of day (a render path that keeps
    /// the start hour) is still compared by its DAY.
    /// </summary>
    [Test]
    public void ApplyRepeatEndBound_OccurrenceWithTimeOfDay_OnTheUntilDay_IsKept()
    {
        var planning = WeeklyPlanning(Day(2026, 11, 19));
        var occurrences = new List<DateTime> { Day(2026, 12, 10).AddHours(9), Day(2026, 12, 17).AddHours(9) };

        CalendarSvc.ApplyRepeatEndBound(planning, UntilArp(Parse("2026-12-10T00:00:00")), occurrences,
            Day(2026, 12, 31));

        Assert.That(occurrences, Is.EqualTo(new[] { Day(2026, 12, 10).AddHours(9) }));
    }

    [Test]
    public void ApplyRepeatEndBound_NeverEndMode_LeavesTheListUntouched()
    {
        var planning = WeeklyPlanning(Day(2026, 11, 19));
        var occurrences = ThursdaysAroundTenDecember();
        var arp = UntilArp(Parse("2026-12-09T23:00:00"));
        arp.RepeatEndMode = 0;

        CalendarSvc.ApplyRepeatEndBound(planning, arp, occurrences, Day(2026, 12, 31));

        Assert.That(occurrences, Has.Count.EqualTo(5));
    }
}
