using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using NUnit.Framework;
using ItemsPlanningRepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1332 — <c>GetWeekViewOccurrences</c>, which the compliance report projects planned
/// rows with, must return exactly what the calendar WEEK VIEW paints over the same
/// range: the union of <c>GetTasksForWeek</c>'s per-week recipe
/// (<c>GetOccurrencesInWeek</c> then <c>ApplyRepeatEndBound</c>) over the Monday-aligned
/// weeks the grid shows. The reference below walks those Monday weeks; the function
/// under test walks 7-day windows starting at <c>from</c>, which is mid-week in most
/// cases here — that difference is the point of the comparison.
///
/// Pure functions, no database. <c>GetOccurrencesInWeek</c> is private and reached by
/// reflection, as in <see cref="CalendarOccurrencesWeekTests"/>.
/// </summary>
[TestFixture]
public class CalendarWeekViewOccurrencesParityTests
{
    private static DateTime D(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static List<DateTime> GetOccurrencesInWeek(
        Planning planning, AreaRulePlanning arp, DateTime weekStart, DateTime weekEnd)
    {
        var mi = typeof(BackendConfigurationCalendarService).GetMethod(
            "GetOccurrencesInWeek", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(mi, Is.Not.Null, "GetOccurrencesInWeek(...) signature changed — update this test");
        return (List<DateTime>)mi!.Invoke(null, new object?[]
        {
            planning, weekStart, weekEnd, arp.RepeatWeekdaysCsv, arp.RepeatOrdinalWeek, arp.DayOfWeek
        })!;
    }

    /// <summary>The week view: one GetTasksForWeek recipe per Monday-aligned week.</summary>
    private static List<DateTime> WeekViewReference(Planning planning, AreaRulePlanning arp, DateTime from, DateTime to)
    {
        var result = new List<DateTime>();
        var monday = from.Date.AddDays(-(((int)from.DayOfWeek + 6) % 7));
        for (var weekStart = monday; weekStart <= to; weekStart = weekStart.AddDays(7))
        {
            var weekEnd = weekStart.AddDays(7).AddTicks(-1);
            var occurrences = GetOccurrencesInWeek(planning, arp, weekStart, weekEnd);
            BackendConfigurationCalendarService.ApplyRepeatEndBound(planning, arp, occurrences, weekEnd);
            result.AddRange(occurrences);
        }

        return result.Where(d => d >= from.Date && d <= to.Date).Distinct().OrderBy(d => d).ToList();
    }

    private static void AssertParity(Planning planning, AreaRulePlanning arp, DateTime from, DateTime to)
    {
        var expected = WeekViewReference(planning, arp, from, to);
        var actual = BackendConfigurationCalendarService.GetWeekViewOccurrences(planning, arp, from, to);

        Assert.That(expected, Is.Not.Empty, "the case must exercise at least one occurrence");
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void MultiDayWeekdays_EverySecondWeek_MidWeekFrom()
    {
        var start = D(2026, 1, 5); // Monday
        var planning = new Planning
        {
            StartDate = start, RepeatType = ItemsPlanningRepeatType.Week, RepeatEvery = 2
        };
        var arp = new AreaRulePlanning { RepeatWeekdaysCsv = "1,3,0", RepeatType = 2, RepeatEvery = 2 };

        AssertParity(planning, arp, D(2026, 3, 11) /* Wednesday */, D(2026, 9, 30));
    }

    [Test]
    public void Monthly_DayOfMonth()
    {
        var planning = new Planning
        {
            StartDate = D(2026, 1, 15), RepeatType = ItemsPlanningRepeatType.Month, RepeatEvery = 1,
            DayOfMonth = 15
        };
        var arp = new AreaRulePlanning { RepeatType = 3, RepeatEvery = 1 };

        AssertParity(planning, arp, D(2026, 2, 3), D(2027, 2, 3));
    }

    /// <param name="startDate">
    /// 2026-01-13 is the 2nd Tuesday itself; 2026-01-20 is a Tuesday AFTER the start
    /// month's pattern date (the #1207 anchor); 2026-01-22 is a Thursday — an
    /// off-weekday start anchor.
    /// </param>
    [TestCase(2026, 1, 13)]
    [TestCase(2026, 1, 20)]
    [TestCase(2026, 1, 22)]
    public void Monthly_NthWeekday(int y, int m, int d)
    {
        var planning = new Planning
        {
            StartDate = D(y, m, d), RepeatType = ItemsPlanningRepeatType.Month, RepeatEvery = 1,
            DayOfMonth = 0
        };
        var arp = new AreaRulePlanning
        {
            RepeatType = 3, RepeatEvery = 1, RepeatOrdinalWeek = 2, DayOfWeek = 2, RepeatWeekdaysCsv = "2"
        };

        AssertParity(planning, arp, D(2026, 1, 1), D(2026, 12, 31));
    }

    [Test]
    public void Yearly()
    {
        var planning = new Planning
        {
            StartDate = D(2025, 3, 10), RepeatType = (ItemsPlanningRepeatType)4, RepeatEvery = 1,
            DayOfMonth = 10
        };
        var arp = new AreaRulePlanning { RepeatType = 4, RepeatEvery = 1 };

        AssertParity(planning, arp, D(2026, 1, 1), D(2030, 12, 31));
    }

    /// <summary>A one-off task: the week view paints it on its start date.</summary>
    [Test]
    public void OneOff_FutureTask()
    {
        var planning = new Planning
        {
            StartDate = D(2026, 10, 22), RepeatType = (ItemsPlanningRepeatType)0, RepeatEvery = 0
        };
        var arp = new AreaRulePlanning { RepeatType = 0, RepeatEvery = 0 };

        AssertParity(planning, arp, D(2026, 10, 1), D(2026, 12, 31));
        Assert.That(BackendConfigurationCalendarService.GetWeekViewOccurrences(
            planning, arp, D(2026, 10, 1), D(2026, 12, 31)), Is.EqualTo(new[] { D(2026, 10, 22) }));
    }

    /// <summary>
    /// "After N": the count runs from the series start, long before <c>from</c>. The 60th
    /// weekly occurrence from 2025-01-06 is 2026-02-23, so the series ends inside the range.
    /// </summary>
    [Test]
    public void AfterNOccurrences_SeriesStartedLongBeforeFrom()
    {
        var planning = new Planning
        {
            StartDate = D(2025, 1, 6), RepeatType = ItemsPlanningRepeatType.Week, RepeatEvery = 1
        };
        var arp = new AreaRulePlanning
        {
            RepeatType = 2, RepeatEvery = 1, RepeatEndMode = 1, RepeatOccurrences = 60
        };

        AssertParity(planning, arp, D(2026, 1, 1), D(2026, 12, 31));
        Assert.That(BackendConfigurationCalendarService.GetWeekViewOccurrences(
            planning, arp, D(2026, 1, 1), D(2026, 12, 31)).Max(), Is.EqualTo(D(2026, 2, 23)));
    }
}
