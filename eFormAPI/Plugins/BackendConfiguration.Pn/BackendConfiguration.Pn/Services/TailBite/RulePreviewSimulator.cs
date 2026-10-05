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

#nullable enable

namespace BackendConfiguration.Pn.Services.TailBite;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Dry run of a candidate rule (§6.4). Pure: replays the snapshot's rows in EffectiveAt order with the candidate in place
/// of the rule on its location, and counts the outbreaks that would have opened. Nothing is written.
/// </summary>
public static class RulePreviewSimulator
{
    private const int CandidateRuleId = -1;

    public static RulePreview Simulate(EvaluationSnapshot snapshot, RuleInput candidate)
    {
        var rules = WithCandidate(snapshot.Rules, candidate);
        var windowByRuleId = rules.ToDictionary(r => r.Id, r => TimeSpan.FromDays(r.WindowDays));

        var seen = new List<EvalRow>();             // rows replayed so far; LinkedToClosed = consumed by a handled simulated outbreak
        var rowTime = new Dictionary<int, DateTime>();
        var open = new List<SimulatedOutbreak>();
        var perNode = new Dictionary<int, int>();
        var nextId = -1;
        // Rows of one registration share EffectiveAt, so grouping by it replays registration by registration.
        foreach (var group in snapshot.Rows.Where(r => !r.Cancelled).GroupBy(r => r.EffectiveAt).OrderBy(g => g.Key))
        {
            var t = group.Key;
            // A simulated outbreak is handled at (last linked row + window); a row at that moment or later no longer joins it.
            foreach (var handled in open.Where(o => t >= o.HandledAt).ToList())
            {
                open.Remove(handled);
                for (var i = 0; i < seen.Count; i++)
                    if (handled.RowIds.Contains(seen[i].RowId)) seen[i] = seen[i] with { LinkedToClosed = true };
            }
            foreach (var row in group)
            {
                seen.Add(row with { LinkedToClosed = false });
                rowTime[row.RowId] = row.EffectiveAt;
            }
            var simulated = new EvaluationSnapshot(snapshot.Locations, rules, seen.ToList(),
                open.Select(o => new EvalOpenOutbreak(o.Id, o.Node)).ToList());
            foreach (var decision in OutbreakEvaluator.Evaluate(simulated, t, group.Select(r => r.RowId).ToList()))
            {
                switch (decision)
                {
                    case JoinOutbreak j:
                        var target = open.Single(o => o.Id == j.OutbreakId);
                        target.RowIds.UnionWith(j.RowIds);
                        var lastJoined = j.RowIds.Max(id => rowTime[id]);
                        if (lastJoined > target.LastLinkedAt) target.LastLinkedAt = lastJoined;
                        break;
                    case OpenNewOutbreak n:
                        open.Add(new SimulatedOutbreak(nextId--, n.SummingLocationId, windowByRuleId[n.RuleId], n.RowIds.ToHashSet(),
                            n.RowIds.Max(id => rowTime[id])));
                        perNode[n.SummingLocationId] = perNode.GetValueOrDefault(n.SummingLocationId) + 1;
                        break;
                }
            }
        }
        return new RulePreview(perNode.Values.Sum(), perNode);
    }

    /// <summary>The stored rules with the candidate replacing the rule on its location (or added there).</summary>
    public static List<EvalRule> WithCandidate(IEnumerable<EvalRule> rules, RuleInput candidate)
        => rules.Where(r => r.LocationId != candidate.LocationId)
            .Append(new EvalRule(CandidateRuleId, 0, candidate.LocationId, candidate.MinBittenPigs, candidate.MinSevere,
                candidate.WindowDays, candidate.CountDepth))
            .ToList();

    private sealed class SimulatedOutbreak(int id, int node, TimeSpan window, HashSet<int> rowIds, DateTime lastLinkedAt)
    {
        public int Id { get; } = id;
        public int Node { get; } = node;
        public HashSet<int> RowIds { get; } = rowIds;
        public DateTime LastLinkedAt { get; set; } = lastLinkedAt;
        public DateTime HandledAt => LastLinkedAt + window;
    }
}
