using System;
using System.Collections.Generic;
using System.Reflection;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using NUnit.Framework;
using ItemsPlanningRepeatType = Microting.ItemsPlanningBase.Infrastructure.Enums.RepeatType;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Pure-function unit tests for the private static
/// <c>BackendConfigurationCalendarService.GetOccurrencesInWeek</c> occurrence
/// generator (invoked via reflection — no DB / core needed).
///
/// Guards the "event disappears from the calendar after edit" regression: a
/// weekly task whose StartDate landed on the trailing Sunday of a Mon-Sun
/// caller week was dropped because the multi-day-weekly branch bucketed the
/// whole caller week against a single Sunday-aligned week, computing
/// weeksFromAnchor = -1. The fix gates the stride per-candidate on the
/// candidate's own Sunday-aligned week.
///
/// June 2026 calendar reference: Mon 1st .. Sun 7th; Wed = 3rd, Thu = 4th.
/// </summary>
[TestFixture]
public class CalendarOccurrencesWeekTests
{
    private static readonly DateTime Monday = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Wednesday = new(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Thursday = new(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Sunday = new(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc);

    private static List<DateTime> GetOccurrencesInWeek(
        Planning planning, DateTime weekStart, DateTime weekEnd, string? repeatWeekdaysCsv)
    {
        var mi = typeof(BackendConfigurationCalendarService).GetMethod(
            "GetOccurrencesInWeek", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(mi, Is.Not.Null, "GetOccurrencesInWeek(...) signature changed — update this test");
        return (List<DateTime>)mi!.Invoke(null,
            new object?[] { planning, weekStart, weekEnd, repeatWeekdaysCsv, null, null })!;
    }

    private static Planning Weekly(DateTime startDate, int repeatEvery = 1) => new()
    {
        StartDate = startDate,
        RepeatType = ItemsPlanningRepeatType.Week,
        RepeatEvery = repeatEvery,
    };

    // The exact regression: anchor on the trailing Sunday must render in its
    // own Mon-Sun week. Pre-fix this returned empty and the event vanished.
    [Test]
    public void Weekly_SundayAnchor_RendersInOwnWeek()
    {
        var occ = GetOccurrencesInWeek(Weekly(Sunday), Monday, Sunday, "0");
        Assert.That(occ, Does.Contain(Sunday));
    }

    // Sibling weekdays were never affected — assert they still render so the
    // fix didn't change correct behaviour.
    [Test]
    public void Weekly_MondayAnchor_RendersInOwnWeek()
    {
        var occ = GetOccurrencesInWeek(Weekly(Monday), Monday, Sunday, "1");
        Assert.That(occ, Does.Contain(Monday));
    }

    [Test]
    public void Weekly_ThursdayAnchor_RendersInOwnWeek()
    {
        var occ = GetOccurrencesInWeek(Weekly(Thursday), Monday, Sunday, "4");
        Assert.That(occ, Does.Contain(Thursday));
    }

    // Mixed multi-day CSV whose days fall in different Sunday-weeks within one
    // Mon-Sun caller week (Wed in the 05-31 Sunday-week, Sun in the 06-07 one):
    // both must render.
    [Test]
    public void Weekly_WedSundayCsv_RendersBothDays()
    {
        var occ = GetOccurrencesInWeek(Weekly(Wednesday), Monday, Sunday, "3,0");
        Assert.That(occ, Does.Contain(Wednesday));
        Assert.That(occ, Does.Contain(Sunday));
    }

    // Bi-weekly Sunday anchor: renders in week 0, skipped in week 1, renders in
    // week 2 — proves the per-candidate stride still honours repeatEvery.
    [Test]
    public void Weekly_BiweeklySundayAnchor_RespectsStride()
    {
        var plan = Weekly(Sunday, repeatEvery: 2);

        var week0 = GetOccurrencesInWeek(plan, Monday, Sunday, "0");
        Assert.That(week0, Does.Contain(Sunday), "week 0 (anchor week) should render");

        var nextMonday = Monday.AddDays(7);
        var nextSunday = Sunday.AddDays(7);
        var week1 = GetOccurrencesInWeek(plan, nextMonday, nextSunday, "0");
        Assert.That(week1, Is.Empty, "week 1 is off-stride for a bi-weekly task");

        var week2Monday = Monday.AddDays(14);
        var week2Sunday = Sunday.AddDays(14);
        var week2 = GetOccurrencesInWeek(plan, week2Monday, week2Sunday, "0");
        Assert.That(week2, Does.Contain(week2Sunday), "week 2 is on-stride and should render");
    }
}
