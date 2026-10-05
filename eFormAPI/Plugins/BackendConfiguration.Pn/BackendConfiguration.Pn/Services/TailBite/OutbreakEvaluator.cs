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

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

#nullable enable

namespace BackendConfiguration.Pn.Services.TailBite;

using System;
using System.Collections.Generic;
using System.Linq;

public static class OutbreakEvaluator
{
    public static IReadOnlyList<EvalDecision> Evaluate(EvaluationSnapshot snapshot, DateTime t, IReadOnlyList<int> newRowIds)
    {
        var rowsById = snapshot.Rows.ToDictionary(r => r.RowId);
        var rulesByLocation = snapshot.Rules.GroupBy(r => r.LocationId).ToDictionary(g => g.Key, g => g.First());
        var decisions = new List<EvalDecision>();

        // A row counts only at its own summing node (nearest rule's CountDepth); rows with no rule never count.
        int? OwnNode(EvalRow row)
        {
            var rule = NearestRule(snapshot, rulesByLocation, row.LocationId);
            return rule is null ? null : SummingNode(snapshot, row.LocationId, rule.CountDepth);
        }

        // Group the new rows by (summing node, rule): §6.2 steps 1-2.
        var groups = new Dictionary<int, (EvalRule Rule, List<int> RowIds)>();
        foreach (var rowId in newRowIds)
        {
            var row = rowsById[rowId];
            var rule = NearestRule(snapshot, rulesByLocation, row.LocationId);
            if (rule is null) continue;
            var node = SummingNode(snapshot, row.LocationId, rule.CountDepth);
            if (!groups.TryGetValue(node, out var group)) groups[node] = group = (rule, new List<int>());
            group.RowIds.Add(rowId);
        }

        foreach (var (node, (rule, rowIds)) in groups)
        {
            // Step 3: join an open outbreak on this node.
            var open = snapshot.OpenOutbreaks.FirstOrDefault(o => o.LocationId == node);
            if (open is not null)
            {
                decisions.Add(new JoinOutbreak(open.OutbreakId, rowIds));
                continue;
            }

            // Step 4: eligible rows owned by this node.
            var window = TimeSpan.FromDays(rule.WindowDays);
            var horizon = t + window;
            var eligible = snapshot.Rows
                .Where(r => !r.Cancelled && !r.LinkedToClosed && OwnNode(r) == node)
                .ToList();

            // Step 5: every window end among eligible EffectiveAt in [t, t+W].
            var ends = eligible.Select(r => r.EffectiveAt).Where(x => x >= t && x <= horizon).Distinct().OrderBy(x => x);
            foreach (var windowEnd in ends)
            {
                var inWindow = eligible.Where(r => r.EffectiveAt > windowEnd - window && r.EffectiveAt <= windowEnd).ToList();
                var bitten = inWindow.Sum(r => (long)r.Minor + r.Severe);
                var severe = inWindow.Sum(r => (long)r.Severe);
                var fires = (rule.MinBittenPigs is { } mb && bitten >= mb) || (rule.MinSevere is { } ms && severe >= ms);
                if (!fires) continue;

                // Step 6: open at the earliest window end and link eligible rows in (end-W, t+W].
                var linked = eligible.Where(r => r.EffectiveAt > windowEnd - window && r.EffectiveAt <= horizon).Select(r => r.RowId).ToList();
                decisions.Add(new OpenNewOutbreak(node, rule.Id, rule.Version, windowEnd, linked));
                break;
            }
        }
        return decisions;
    }

    public static int Depth(EvaluationSnapshot snapshot, int locationId)
        => SelfAndAncestors(snapshot, locationId).Count() - 1;

    public static bool IsDescendantOrSelf(EvaluationSnapshot snapshot, int locationId, int ancestorId)
        => SelfAndAncestors(snapshot, locationId).Contains(ancestorId);

    /// <summary>The location itself, then each ancestor up to the root.</summary>
    private static IEnumerable<int> SelfAndAncestors(EvaluationSnapshot snapshot, int id)
    {
        for (int? cur = id; cur is { } c; cur = snapshot.Locations[c].ParentId)
            yield return c;
    }

    private static EvalRule? NearestRule(EvaluationSnapshot snapshot, IReadOnlyDictionary<int, EvalRule> rulesByLocation, int locationId)
        => SelfAndAncestors(snapshot, locationId)
            .Select(id => rulesByLocation.GetValueOrDefault(id))
            .FirstOrDefault(rule => rule is not null);

    private static int SummingNode(EvaluationSnapshot snapshot, int locationId, int countDepth)
    {
        var rootToLocation = SelfAndAncestors(snapshot, locationId).Reverse().ToList();
        return countDepth < rootToLocation.Count ? rootToLocation[countDepth] : locationId;
    }
}
