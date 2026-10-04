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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using SdkDbContext = Microting.eForm.Infrastructure.MicrotingDbContext;

/// <summary>
/// #1370 / #1373 (option B) — a COMPLETED log is placed on the day it was done, not on its
/// deadline: Rapport, Detaljer, Oversigt and the calendar week view all ask this one rule.
///
/// <para>The scheduler dates a cycle's <c>Compliance</c> with the NEXT execution, so the
/// deadline of a log completed early can lie a whole repeat interval after the completion
/// (a yearly task done in December carries next year's deadline). The done date is what the
/// user sees as "Udført dato", so a period, a week and a date edit all follow it.</para>
///
/// <para>The done date is <c>DoneAtUserModifiable ?? DoneAt</c>, read as the Danish calendar
/// date — the browser renders the stored UTC instant in Danish time. An OPEN occurrence keeps
/// its task date (exception <c>NewDate</c>, else <c>Deadline</c>), and so does a completed one
/// with no timestamp at all. Everything keyed by the occurrence itself — delete markers,
/// recurrence suppression, the completed-period backstop — stays keyed by the
/// <c>Deadline</c>.</para>
/// </summary>
public static class CompletedLogPlacement
{
    /// <summary>The Danish done date, or <c>null</c> when the case carries no timestamp.</summary>
    public static DateTime? DoneDate(DateTime? doneAtUserModifiable, DateTime? doneAt)
        => (doneAtUserModifiable ?? doneAt) is { } instant
            ? ComplianceFutureTaskGuard.DateInCopenhagen(instant)
            : null;

    /// <summary>
    /// How far a completed log's deadline may lie from the period it was done in and still be
    /// found there, by default. The scheduler dates a cycle with the NEXT execution, so an
    /// early completion is at most one repeat interval before its deadline — a year for the
    /// yearly tasks of #1370, plus a margin; a late one is rarely completed more than a year
    /// overdue. Bounding the reach keeps the lookup proportional to the period rather than to
    /// all history. Tasks repeating less often than that get their own, wider reach — see
    /// <see cref="LoadLongIntervalPlanningsAsync"/>.
    /// </summary>
    public const int DeadlineReachDays = 400;

    /// <summary>Added to a long interval, so a log done a full interval early is still found.</summary>
    private const int LongIntervalMarginDays = 35;

    /// <summary>
    /// The deadline range [<c>From</c>, <c>To</c>) in which a log done in the period
    /// [<paramref name="fromDate"/>, <paramref name="toDate"/>] is looked for, clamped to the
    /// calendar.
    /// </summary>
    public static (DateTime From, DateTime To) DeadlineReach(
        DateTime fromDate, DateTime toDate, int reachDays = DeadlineReachDays)
    {
        var from = fromDate.Date > DateTime.MinValue.Date.AddDays(reachDays)
            ? fromDate.Date.AddDays(-reachDays)
            : DateTime.MinValue;
        var to = toDate.Date < DateTime.MaxValue.Date.AddDays(-reachDays - 1)
            ? toDate.Date.AddDays(reachDays + 1)
            : DateTime.MaxValue;
        return (from, to);
    }

    /// <summary>
    /// The plannings that repeat less often than <see cref="DeadlineReachDays"/> covers (the
    /// wizard offers intervals up to 120 months), and the reach that covers the longest of
    /// them: its interval plus a margin. Callers run a second, planning-scoped lookup with
    /// that reach, so a log of such a task done a whole interval early is not lost. The set
    /// is small — long intervals are rare — and read without a join (another database).
    /// </summary>
    public static async Task<(List<int> PlanningIds, int ReachDays)> LoadLongIntervalPlanningsAsync(
        ItemsPlanningPnDbContext itemsPlanningPnDbContext)
    {
        // Longer than a year (the default reach covers a year plus the margin).
        var plannings = await itemsPlanningPnDbContext.Plannings
            .Where(p => (p.RepeatType == RepeatType.Day && p.RepeatEvery > 366)
                        || (p.RepeatType == RepeatType.Week && p.RepeatEvery > 52)
                        || (p.RepeatType == RepeatType.Month && p.RepeatEvery > 12)
                        || (int)p.RepeatType > 3)
            .Select(p => new { p.Id, p.RepeatType, p.RepeatEvery })
            .ToListAsync()
            .ConfigureAwait(false);

        var ids = new List<int>();
        var reach = DeadlineReachDays;
        foreach (var planning in plannings)
        {
            var intervalReach = IntervalDays((int)planning.RepeatType, Math.Max(1, planning.RepeatEvery))
                                + LongIntervalMarginDays;
            ids.Add(planning.Id);
            reach = Math.Max(reach, intervalReach);
        }

        return (ids, reach);
    }

    /// <summary>An upper bound on one repeat interval, in days. Anything past monthly counts as yearly.</summary>
    private static int IntervalDays(int repeatType, int repeatEvery) => repeatType switch
    {
        1 => repeatEvery,
        2 => 7 * repeatEvery,
        3 => (int)Math.Ceiling(30.5 * repeatEvery),
        _ => 366 * repeatEvery
    };

    /// <summary>
    /// Of <paramref name="caseIds"/>, the completed SDK cases (<c>Status == 100</c>) whose done
    /// date can fall in [<paramref name="fromDate"/>, <paramref name="toDate"/>]. Led by the
    /// case ids — the primary key — in chunks, so the SDK Cases table is never scanned. The
    /// SQL bound compares stored UTC instants against Danish dates. Copenhagen is UTC+1/+2, so
    /// a Danish date D spans the UTC instants from 22:00 or 23:00 on D-1 up to 22:00 or 23:00
    /// on D: the bound starts a day early (<c>from - 1 day</c>) and ends at the UTC midnight
    /// after <paramref name="toDate"/>, which covers every such instant. Callers apply the
    /// exact <see cref="DoneDate"/> check afterwards.
    /// </summary>
    public static async Task<HashSet<int>> FilterCaseIdsDoneBetweenAsync(
        SdkDbContext sdkDbContext, IReadOnlyCollection<int> caseIds, DateTime fromDate, DateTime toDate)
    {
        var done = new HashSet<int>();
        if (caseIds.Count == 0)
        {
            return done;
        }

        var lo = fromDate.Date > DateTime.MinValue.Date ? fromDate.Date.AddDays(-1) : DateTime.MinValue;
        // Exclusive. The last day of the calendar has no next day to step to.
        var hi = toDate.Date < DateTime.MaxValue.Date ? toDate.Date.AddDays(1) : DateTime.MaxValue;

        foreach (var chunk in caseIds.Distinct().Chunk(ChunkSize))
        {
            // The OR form rather than COALESCE, so each arm is a plain column comparison.
            var ids = await sdkDbContext.Cases
                .Where(c => chunk.Contains(c.Id) && c.Status == 100)
                .Where(c => (c.DoneAtUserModifiable >= lo && c.DoneAtUserModifiable < hi)
                            || (c.DoneAtUserModifiable == null && c.DoneAt >= lo && c.DoneAt < hi))
                .Select(c => c.Id)
                .ToListAsync()
                .ConfigureAwait(false);
            done.UnionWith(ids);
        }

        return done;
    }

    /// <summary>IN-list size per round trip.</summary>
    private const int ChunkSize = 1000;
}
