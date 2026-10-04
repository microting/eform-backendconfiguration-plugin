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

/// <summary>
/// #1382 — the date the task tracker ("Udfør senest") and the compliance list show for an
/// open compliance. One rule for both, so they cannot disagree.
///
/// <para>A legacy compliance stores <c>Deadline</c> as the END of its period (the next
/// run), so the last day to do it is <c>Deadline.AddDays(-1)</c>. A calendar task — its
/// AreaRulePlanning has a live <c>CalendarConfiguration</c> — stores the occurrence date
/// itself, which is also where the calendar and the compliance report place it. Shifting
/// that by a day showed today's occurrence with yesterday's date.</para>
/// </summary>
public static class ComplianceDisplayDeadline
{
    /// <summary>
    /// The deadline to display for a compliance stored on <paramref name="deadline"/>. The
    /// legacy branch keeps the stored time of day, exactly as the old <c>AddDays(-1)</c> did.
    /// </summary>
    public static DateTime For(DateTime deadline, bool isCalendarTask)
        => isCalendarTask ? deadline.Date : deadline.AddDays(-1);

    /// <summary>
    /// The planning ids among <paramref name="planningIds"/> that belong to a calendar task:
    /// a live AreaRulePlanning with a live CalendarConfiguration.
    /// </summary>
    public static async Task<HashSet<int>> LoadCalendarPlanningIdsAsync(
        BackendConfigurationPnDbContext dbContext,
        IReadOnlyCollection<int> planningIds)
        => (await dbContext.AreaRulePlannings
                .AsNoTracking()
                .Where(a => planningIds.Contains(a.ItemPlanningId)
                            && a.WorkflowState != Constants.WorkflowStates.Removed
                            && dbContext.CalendarConfigurations.Any(c =>
                                c.AreaRulePlanningId == a.Id
                                && c.WorkflowState != Constants.WorkflowStates.Removed))
                .Select(a => a.ItemPlanningId)
                .Distinct()
                .ToListAsync()
                .ConfigureAwait(false))
            .ToHashSet();
}
