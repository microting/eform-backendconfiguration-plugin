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

using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.GrpcServices;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

public interface ITailBiteAccess
{
    Task<int> RequireCallerSiteAsync();                       // 0 → Forbidden; ambiguous email → Forbidden
    Task<PropertyWorker> RequireWorkerAsync(int siteId, int propertyId);
    Task<PropertyWorker> RequireManagerAsync(int siteId, int propertyId);
    Task<bool> IsManagerAsync(int siteId, int propertyId);
}

public class TailBiteAccess(BackendConfigurationPnDbContext db, IGrpcSiteResolver resolver, ITailBiteWorkerEmailCounter emailCounter) : ITailBiteAccess
{
    public async Task<int> RequireCallerSiteAsync()
    {
        var site = await resolver.GetSdkSiteIdAsync();
        if (site == 0) throw new TailBiteForbiddenException("Caller has no resolvable worker identity.");
        if (await emailCounter.CountWorkersWithCallerEmailAsync() > 1)
            throw new TailBiteForbiddenException("Caller's email maps to several workers; ask an administrator to fix the worker list.");
        return site;
    }

    private IQueryable<PropertyWorker> ActiveWorkers(int siteId, int propertyId)
        => db.PropertyWorkers.Where(pw => pw.PropertyId == propertyId && pw.WorkerId == siteId
               && pw.WorkflowState != Constants.WorkflowStates.Removed);

    public async Task<PropertyWorker> RequireWorkerAsync(int siteId, int propertyId)
        => await ActiveWorkers(siteId, propertyId).FirstOrDefaultAsync()
           ?? throw new TailBiteForbiddenException("Caller is not a worker on this property.");

    public async Task<PropertyWorker> RequireManagerAsync(int siteId, int propertyId)
    {
        var pw = await RequireWorkerAsync(siteId, propertyId);
        return pw.TailBiteManager ? pw : throw new TailBiteForbiddenException("Caller is not a tail-bite manager on this property.");
    }

    public async Task<bool> IsManagerAsync(int siteId, int propertyId)
        => await ActiveWorkers(siteId, propertyId).AnyAsync(pw => pw.TailBiteManager);
}
