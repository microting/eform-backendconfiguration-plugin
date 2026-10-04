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
using Case = Microting.eForm.Infrastructure.Data.Entities.Case;
using SdkDbContext = Microting.eForm.Infrastructure.MicrotingDbContext;

/// <summary>
/// #1371 — a task assigned to several workers deploys one SDK case per worker, all
/// under the same items-planning <c>PlanningCase</c>, but its <c>Compliance</c> stores
/// only one of them. When ANOTHER worker completes it on the device, the legacy
/// completion path soft-removes the compliance without repointing it, and the stored
/// case is retracted — so judged by its own case alone the occurrence is "not done +
/// removed", which every reader treats as deleted by a user.
///
/// <para>This finds, for each such compliance case, the case a sibling worker completed
/// for the same occurrence: a <c>PlanningCaseSite</c> on the same <c>PlanningCaseId</c>
/// with <c>Status == 100</c> whose SDK case is also <c>Status == 100</c> (the done-ness
/// every compliance reader uses). If several siblings completed it, the earliest
/// completion wins. A case that is itself completed is never resolved to a sibling,
/// whatever the caller passes. Two queries — one per database — whatever the number of
/// cases.</para>
///
/// <para>Callers pass only the case ids of REMOVED compliances whose own case they
/// already know is not completed — the rare #1371 shape; the own-case check here is the
/// safety net. This matters because <c>PlanningCaseSites</c> has no index on
/// <c>MicrotingSdkCaseId</c>/<c>PlanningCaseId</c>, so ordinary rows must not pay
/// for the lookup.</para>
///
/// <para>The PlanningCaseSites are deliberately NOT filtered on WorkflowState: property
/// delete and area unassign soft-delete completed ones too, and their history must stay.</para>
/// </summary>
public static class CompletedSiblingCases
{
    /// <summary>
    /// Maps each of <paramref name="caseIds"/> that is not completed itself but has a
    /// completed sibling to that sibling's SDK case (read without tracking). Every other
    /// id is absent from the result.
    /// </summary>
    public static async Task<Dictionary<int, Case>> FindAsync(
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        SdkDbContext sdkDbContext,
        IEnumerable<int> caseIds)
    {
        var ownCaseIds = caseIds.Where(id => id > 0).Distinct().ToList();
        if (ownCaseIds.Count == 0)
        {
            return new Dictionary<int, Case>();
        }

        var pairs = await (
                from own in itemsPlanningPnDbContext.PlanningCaseSites
                join sibling in itemsPlanningPnDbContext.PlanningCaseSites
                    on own.PlanningCaseId equals sibling.PlanningCaseId
                where ownCaseIds.Contains(own.MicrotingSdkCaseId)
                      && own.PlanningCaseId > 0
                      && sibling.Status == 100
                      && sibling.MicrotingSdkCaseId != own.MicrotingSdkCaseId
                select new { OwnCaseId = own.MicrotingSdkCaseId, SiblingCaseId = sibling.MicrotingSdkCaseId })
            .Distinct()
            .ToListAsync()
            .ConfigureAwait(false);
        if (pairs.Count == 0)
        {
            return new Dictionary<int, Case>();
        }

        // One SDK round trip: the completed siblings AND which own cases are completed
        // themselves (those keep their own case).
        var lookupIds = pairs.SelectMany(p => new[] { p.OwnCaseId, p.SiblingCaseId }).Distinct().ToList();
        var completedById = await sdkDbContext.Cases
            .AsNoTracking()
            .Where(c => lookupIds.Contains(c.Id) && c.Status == 100)
            .ToDictionaryAsync(c => c.Id)
            .ConfigureAwait(false);

        return pairs
            .Where(p => !completedById.ContainsKey(p.OwnCaseId) && completedById.ContainsKey(p.SiblingCaseId))
            .GroupBy(p => p.OwnCaseId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => completedById[p.SiblingCaseId])
                    .OrderBy(c => c.DoneAtUserModifiable ?? c.DoneAt ?? DateTime.MaxValue)
                    .ThenBy(c => c.Id)
                    .First());
    }
}
