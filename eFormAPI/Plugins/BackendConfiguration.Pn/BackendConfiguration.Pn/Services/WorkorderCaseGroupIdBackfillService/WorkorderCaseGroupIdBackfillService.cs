/*
The MIT License (MIT)

Copyright (c) 2007 - 2022 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.WorkorderCaseGroupIdBackfillService;

public class WorkorderCaseGroupIdBackfillService(
    BackendConfigurationPnDbContext dbContext,
    ILogger<WorkorderCaseGroupIdBackfillService> logger)
{
    public async Task RunIfNeededAsync()
    {
        var hasMissing = await dbContext.WorkorderCases
            .AnyAsync(x => x.GroupId == null);

        if (!hasMissing)
        {
            return;
        }

        logger.LogInformation("WorkorderCase GroupId backfill: starting");

        // Load ALL cases regardless of WorkflowState — deleted cases are still part of the chain
        var allCases = await dbContext.WorkorderCases
            .IgnoreQueryFilters()
            .ToListAsync();

        // Build children-by-parent lookup (Id -> list of direct children)
        var childrenByParent = allCases
            .Where(x => x.ParentWorkorderCaseId.HasValue)
            .GroupBy(x => x.ParentWorkorderCaseId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Identify "new-family boundary" cases: these are intake cases created by
        // WorkorderCaseCompletedHandler when a device user submits a form. They have
        // LeadingCase=false, CaseId=0, and act as roots of a new task family rather
        // than continuations of the parent task. Each gets its own fresh GroupId.
        var newFamilyBoundaries = allCases
            .Where(x => !x.LeadingCase && x.CaseId == 0 && childrenByParent.ContainsKey(x.Id))
            .Select(x => x.Id)
            .ToHashSet();

        // All cases with null parent are true roots — each starts a new family.
        var roots = allCases.Where(x => x.ParentWorkorderCaseId == null).ToList();

        foreach (var root in roots)
        {
            var groupId = root.GroupId ?? Guid.NewGuid();
            PropagateGroupId(root, groupId, childrenByParent, newFamilyBoundaries);
        }

        // Handle orphans: cases whose ParentWorkorderCaseId points to a non-existent
        // case — treat each as its own root.
        var allIds = allCases.Select(x => x.Id).ToHashSet();
        var orphans = allCases
            .Where(x => x.GroupId == null
                        && x.ParentWorkorderCaseId.HasValue
                        && !allIds.Contains(x.ParentWorkorderCaseId.Value))
            .ToList();

        foreach (var orphan in orphans)
        {
            var groupId = Guid.NewGuid();
            PropagateGroupId(orphan, groupId, childrenByParent, newFamilyBoundaries);
        }

        await dbContext.SaveChangesAsync();

        var updated = allCases.Count(x => x.GroupId.HasValue);
        logger.LogInformation("WorkorderCase GroupId backfill: completed, {Count} cases assigned", updated);
    }

    private static void PropagateGroupId(
        WorkorderCase node,
        Guid groupId,
        Dictionary<int, List<WorkorderCase>> childrenByParent,
        HashSet<int> newFamilyBoundaries)
    {
        node.GroupId = groupId;

        if (!childrenByParent.TryGetValue(node.Id, out var children))
        {
            return;
        }

        foreach (var child in children)
        {
            if (newFamilyBoundaries.Contains(child.Id))
            {
                // This child is a WorkorderCaseCompletedHandler intake case —
                // it starts a new, unrelated task family with its own GroupId.
                var childGroupId = child.GroupId ?? Guid.NewGuid();
                PropagateGroupId(child, childGroupId, childrenByParent, newFamilyBoundaries);
            }
            else
            {
                // Regular task-management child — same family, same GroupId.
                PropagateGroupId(child, groupId, childrenByParent, newFamilyBoundaries);
            }
        }
    }
}
