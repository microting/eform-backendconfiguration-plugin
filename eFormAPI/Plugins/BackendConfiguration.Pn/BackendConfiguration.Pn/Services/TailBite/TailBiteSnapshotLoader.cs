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
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

public interface ITailBiteSnapshotLoader
{
    Task<EvaluationSnapshot> LoadAsync(int propertyId, DateTime from, DateTime to); // rows with EffectiveAt in (from, to]
    Task<EvaluationSnapshot> LoadTreeAsync(int propertyId);                          // locations + rules + open outbreaks, no rows
}

public class TailBiteSnapshotLoader(BackendConfigurationPnDbContext db) : ITailBiteSnapshotLoader
{
    public async Task<EvaluationSnapshot> LoadTreeAsync(int propertyId)
    {
        var (locations, rules, open) = await LoadTreePartsAsync(propertyId);
        return new EvaluationSnapshot(locations, rules, [], open);
    }

    public async Task<EvaluationSnapshot> LoadAsync(int propertyId, DateTime from, DateTime to)
    {
        var (locations, rules, open) = await LoadTreePartsAsync(propertyId);
        // A soft-deleted outbreak, or a soft-deleted link to a closed one, does not consume its rows.
        var closedRowIds = from link in db.TailBiteOutbreakLinks
                           join o in db.TailBiteOutbreaks on link.OutbreakId equals o.Id
                           where o.PropertyId == propertyId && o.ClosedAt != null && o.WorkflowState != Constants.WorkflowStates.Removed
                                 && link.WorkflowState != Constants.WorkflowStates.Removed
                           select link.RegistrationLocationId;
        var rows = await (from row in db.TailBiteRegistrationLocations
                          join reg in db.TailBiteRegistrations on row.RegistrationId equals reg.Id
                          where reg.PropertyId == propertyId && reg.EffectiveAt > @from && reg.EffectiveAt <= to
                                && row.WorkflowState != Constants.WorkflowStates.Removed && !row.CountUnknown
                          select new EvalRow(row.Id, row.LocationId, reg.EffectiveAt, row.MinorCount, row.SevereCount,
                              reg.CancelledAt != null,
                              closedRowIds.Contains(row.Id)))
                         .ToListAsync();
        return new EvaluationSnapshot(locations, rules, rows, open);
    }

    private async Task<(Dictionary<int, EvalLocation>, List<EvalRule>, List<EvalOpenOutbreak>)> LoadTreePartsAsync(int propertyId)
    {
        var locations = await db.TailBiteLocations.Where(l => l.PropertyId == propertyId)
            .Select(l => new EvalLocation(l.Id, l.ParentId)).ToDictionaryAsync(l => l.Id);
        var locIds = locations.Keys.ToList();
        var rules = await db.TailBiteRules
            .Where(r => locIds.Contains(r.LocationId) && r.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(r => new EvalRule(r.Id, r.Version, r.LocationId, r.MinBittenPigs, r.MinSevere, r.WindowDays, r.CountDepth))
            .ToListAsync();
        var open = await db.TailBiteOutbreaks
            .Where(o => o.PropertyId == propertyId && o.ClosedAt == null && o.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(o => new EvalOpenOutbreak(o.Id, o.LocationId)).ToListAsync();
        return (locations, rules, open);
    }
}
