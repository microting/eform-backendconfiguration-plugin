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
using System.Linq;
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
    /// The same rule as a query filter, for the surfaces that count in SQL (the compliance
    /// list and its stats). A live (not soft-removed) compliance row is an open occurrence:
    /// completion soft-removes the row. Past due is <c>Deadline &lt; today</c>, i.e.
    /// <see cref="IsPastDue"/> on the stored date. A planning shared by several live
    /// tasks is hidden only when none of them reports missed occurrences.
    /// </summary>
    public static IQueryable<Compliance> ExcludeHiddenOverdue(
        IQueryable<Compliance> compliances,
        BackendConfigurationPnDbContext dbContext,
        DateTime utcNow)
    {
        var today = utcNow.Date;
        return compliances.Where(c => !(
            c.WorkflowState != Constants.WorkflowStates.Removed
            && c.Deadline < today
            && dbContext.AreaRulePlannings.Any(a =>
                a.ItemPlanningId == c.PlanningId
                && a.WorkflowState != Constants.WorkflowStates.Removed
                && !a.ComplianceEnabled)
            && !dbContext.AreaRulePlannings.Any(a =>
                a.ItemPlanningId == c.PlanningId
                && a.WorkflowState != Constants.WorkflowStates.Removed
                && a.ComplianceEnabled)));
    }
}
