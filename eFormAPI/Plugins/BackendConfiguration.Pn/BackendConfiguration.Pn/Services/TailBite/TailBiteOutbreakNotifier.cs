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
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

public class TailBiteOutbreakNotifier(BackendConfigurationPnDbContext db, ITailBitePushSender push) : ITailBiteOutbreakNotifier
{
    public async Task NotifyOpenedAsync(int propertyId, IReadOnlyList<OutbreakOutcome> outcomes)
    {
        var openedIds = outcomes.Where(o => o.Opened).Select(o => o.OutbreakId).ToList();
        if (openedIds.Count == 0) return;

        var managers = await db.PropertyWorkers
            .Where(pw => pw.PropertyId == propertyId && pw.TailBiteManager && pw.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(pw => pw.WorkerId).Distinct().ToListAsync();
        // The join intentionally includes removed locations: an outbreak opened on a since-removed location still notifies.
        var opened = await db.TailBiteOutbreaks.Where(o => openedIds.Contains(o.Id))
            .Join(db.TailBiteLocations, o => o.LocationId, l => l.Id, (o, l) => new { o.Id, l.Name }).ToListAsync();

        foreach (var outbreak in opened)
        {
            var data = new Dictionary<string, string> { ["type"] = "tailbite_outbreak", ["outbreak_id"] = outbreak.Id.ToString() };
            foreach (var site in managers)
                await push.SendToSiteAsync(site, "Udbrud af halebid", $"{outbreak.Name}: risikovurdering mangler.", data);
        }
    }
}
