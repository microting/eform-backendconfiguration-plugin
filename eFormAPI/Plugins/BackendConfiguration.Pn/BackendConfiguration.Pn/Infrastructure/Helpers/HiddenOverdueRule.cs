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
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

/// <summary>
/// #1325 — a task with "Overskredet opgave vises ikke i app"
/// (<c>AreaRulePlanning.ComplianceEnabled = false</c>) hides its missed occurrences
/// everywhere: the app's task tracker, the web task tracker, the compliance list and its
/// stats, and the compliance report (Detaljer, Oversigt). A missed occurrence is neither
/// listed as open nor counted as overdue or in a percentage. Completed occurrences stay
/// visible and count as done.
///
/// <para>Every surface asks this one rule, so the app and the web cannot disagree again.
/// The rule only hides on READ: a GET never deletes the row (the task tracker used to
/// soft-delete it, which also removed today's and tomorrow's occurrences through its
/// <c>Deadline.AddDays(-1)</c>).</para>
/// </summary>
public static class HiddenOverdueRule
{
    /// <summary>True when the occurrence must not be shown or counted.</summary>
    public static bool IsHiddenOverdue(bool complianceEnabled, bool taskIsExpired, bool completed)
        => !complianceEnabled && taskIsExpired && !completed;

    /// <summary>
    /// Whether an occurrence dated <paramref name="taskDate"/> is past due. Date-level, in
    /// UTC like the rest of the calendar path, and dated by the occurrence itself
    /// (<c>Compliance.Deadline.Date</c>, or a moved occurrence's new date) — never by
    /// <c>Deadline.AddDays(-1)</c>, which only fits legacy end-of-period deadlines.
    /// </summary>
    public static bool IsPastDue(DateTime taskDate, DateTime utcNow)
        => taskDate.Date < utcNow.Date;

    /// <summary>
    /// The same rule as a query filter, for the surfaces that list or count in SQL (the
    /// compliance list and its stats). A live (not soft-removed) compliance row is an open
    /// occurrence: completion soft-removes the row. Past due is <c>Deadline &lt; today</c>,
    /// i.e. <see cref="IsPastDue"/> on the stored date.
    /// </summary>
    public static IQueryable<Compliance> ExcludeHiddenOverdue(
        IQueryable<Compliance> compliances,
        BackendConfigurationPnDbContext dbContext,
        DateTime utcNow)
        => ExcludeOpenRowsOfHidingTasksBefore(compliances, dbContext, utcNow.Date);

    /// <summary>
    /// For the legacy overdue checks that compare the stored deadline with the clock
    /// (<c>Deadline &lt; UtcNow</c> for the persisted property status,
    /// <c>Deadline.AddDays(-1) &lt; today</c> for the dashboard): those count a row dated
    /// TODAY as overdue, yet a task that hides missed occurrences is never overdue. So its
    /// open rows are excluded up to and including today; enabled tasks keep the legacy
    /// behaviour.
    /// </summary>
    public static IQueryable<Compliance> ExcludeNeverOverdue(
        IQueryable<Compliance> compliances,
        BackendConfigurationPnDbContext dbContext,
        DateTime utcNow)
        => ExcludeOpenRowsOfHidingTasksBefore(compliances, dbContext, utcNow.Date.AddDays(1));

    /// <summary>
    /// The planning ids among <paramref name="planningIds"/> with a live task that reports
    /// missed occurrences. A planning shared by several live tasks hides its missed
    /// occurrences only when none of them does — the in-memory surfaces pass
    /// <c>reportingPlanningIds.Contains(planningId)</c> as <c>complianceEnabled</c>.
    /// </summary>
    public static async Task<HashSet<int>> LoadReportingPlanningIdsAsync(
        BackendConfigurationPnDbContext dbContext,
        IReadOnlyCollection<int> planningIds)
        => (await dbContext.AreaRulePlannings
                .AsNoTracking()
                .Where(a => planningIds.Contains(a.ItemPlanningId)
                            && a.WorkflowState != Constants.WorkflowStates.Removed
                            && a.ComplianceEnabled)
                .Select(a => a.ItemPlanningId)
                .Distinct()
                .ToListAsync()
                .ConfigureAwait(false))
            .ToHashSet();

    private static IQueryable<Compliance> ExcludeOpenRowsOfHidingTasksBefore(
        IQueryable<Compliance> compliances,
        BackendConfigurationPnDbContext dbContext,
        DateTime before)
        => compliances.Where(c => !(
            c.WorkflowState != Constants.WorkflowStates.Removed
            && c.Deadline < before
            && dbContext.AreaRulePlannings.Any(a =>
                a.ItemPlanningId == c.PlanningId
                && a.WorkflowState != Constants.WorkflowStates.Removed
                && !a.ComplianceEnabled)
            && !dbContext.AreaRulePlannings.Any(a =>
                a.ItemPlanningId == c.PlanningId
                && a.WorkflowState != Constants.WorkflowStates.Removed
                && a.ComplianceEnabled)));
}
