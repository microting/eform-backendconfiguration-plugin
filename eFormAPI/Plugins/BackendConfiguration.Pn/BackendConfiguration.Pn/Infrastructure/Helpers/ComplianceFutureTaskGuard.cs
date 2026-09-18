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

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

using System;

/// <summary>
/// #1300 — the compliance pages (Detaljer, Rapport, the legacy <c>/compliances</c> table and
/// the task tracker) must not complete or delete an UNCOMPLETED task whose date lies after
/// today. "Today" is the Danish calendar date (Europe/Copenhagen), never the UTC date: with
/// UTC a task dated today would count as future between 00:00 and 01:00/02:00 Danish time.
/// The comparison is date-level; the task's start hour plays no part.
///
/// <para>The calendar keeps its intended EARLY completion. It shares the completion chain
/// (<c>prepare-complete</c> → <c>compliances/cases/calendar</c>) with Detaljer, so the
/// completion guard only runs when the caller identifies itself as a compliance page with
/// <see cref="ComplianceSource"/>. The flag can only ADD this check; no other check reads
/// it, so it can never relax anything. Delete is only reachable from the compliance pages
/// and is therefore guarded unconditionally.</para>
///
/// <para>Pure functions over an explicit <c>utcNow</c>: the services pass their own clock
/// (an instance-level <c>UtcNow</c> seam), so tests can pin "now" around Copenhagen
/// midnight without a process-wide static that parallel fixtures would share.</para>
/// </summary>
public static class ComplianceFutureTaskGuard
{
    /// <summary>The value of the <c>source</c> flag the compliance pages send.</summary>
    public const string ComplianceSource = "compliance";

    private const string CopenhagenIanaId = "Europe/Copenhagen";
    private const string CopenhagenWindowsId = "Romance Standard Time";

    private static readonly Lazy<TimeZoneInfo> Copenhagen = new(ResolveCopenhagen);

    /// <summary>True only for the exact compliance-page flag (case-insensitive).</summary>
    public static bool IsCompliancePageSource(string source)
        => string.Equals(source?.Trim(), ComplianceSource, StringComparison.OrdinalIgnoreCase);

    /// <summary>The Danish calendar date at <paramref name="utcNow"/>.</summary>
    public static DateTime TodayInCopenhagen(DateTime utcNow)
    {
        // An Unspecified instant is treated as UTC (that is what every caller passes);
        // a Local one is converted first.
        var utc = utcNow.Kind switch
        {
            DateTimeKind.Utc => utcNow,
            DateTimeKind.Local => utcNow.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Copenhagen.Value).Date;
    }

    /// <summary>
    /// True when <paramref name="taskDate"/>'s DATE is after today's Danish date. Only the
    /// date part of <paramref name="taskDate"/> is read (task dates are stored as a date at
    /// midnight, e.g. <c>Compliance.Deadline</c>).
    /// </summary>
    public static bool IsFutureTask(DateTime taskDate, DateTime utcNow)
        => taskDate.Date > TodayInCopenhagen(utcNow);

    /// <summary>
    /// The IANA id resolves on Linux (the containers ship tzdata) and, since .NET 6, on
    /// Windows with ICU. The Windows id and the hand-built EU rule are fallbacks so a host
    /// without zone data still gets Danish time instead of silently falling back to UTC.
    /// </summary>
    private static TimeZoneInfo ResolveCopenhagen()
    {
        foreach (var id in new[] { CopenhagenIanaId, CopenhagenWindowsId })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        // CET/CEST under the EU rule: summer time from the last Sunday of March 02:00 to
        // the last Sunday of October 03:00 (local).
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone(
            $"{CopenhagenIanaId} (fallback)", TimeSpan.FromHours(1), "CET", "CET", "CEST",
            [rule]);
    }
}
