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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

public interface ITailBiteDecisionWriter
{
    // returns (outbreakId, opened) per decision, in order
    Task<IReadOnlyList<(int OutbreakId, bool Opened)>> ApplyAsync(int propertyId, int openedByRegistrationId, IReadOnlyList<EvalDecision> decisions);
}

public class TailBiteDecisionWriter(BackendConfigurationPnDbContext db) : ITailBiteDecisionWriter
{
    public async Task<IReadOnlyList<(int OutbreakId, bool Opened)>> ApplyAsync(int propertyId, int openedByRegistrationId, IReadOnlyList<EvalDecision> decisions)
    {
        var result = new List<(int OutbreakId, bool Opened)>();
        foreach (var d in decisions)
        {
            switch (d)
            {
                case JoinOutbreak j:
                    await LinkAsync(j.OutbreakId, j.RowIds, skipExisting: true);
                    result.Add((j.OutbreakId, false));
                    break;
                case OpenNewOutbreak o:
                    var outbreak = new TailBiteOutbreak { PropertyId = propertyId, LocationId = o.SummingLocationId, RuleId = o.RuleId,
                        RuleVersion = o.RuleVersion, OpenedAt = o.OpenedAt, OpenedByRegistrationId = openedByRegistrationId };
                    await outbreak.Create(db);
                    await LinkAsync(outbreak.Id, o.RowIds, skipExisting: false);
                    result.Add((outbreak.Id, true));
                    break;
            }
        }
        return result;
    }

    private async Task LinkAsync(int outbreakId, IEnumerable<int> rowIds, bool skipExisting)
    {
        var existing = skipExisting
            ? await db.TailBiteOutbreakLinks.Where(k => k.OutbreakId == outbreakId).Select(k => k.RegistrationLocationId).ToListAsync()
            : [];
        foreach (var rowId in rowIds.Except(existing))
            await new TailBiteOutbreakLink { OutbreakId = outbreakId, RegistrationLocationId = rowId }.Create(db);
    }
}
