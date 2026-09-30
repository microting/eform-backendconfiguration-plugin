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

namespace BackendConfiguration.Pn.Services.ChemicalInventoryService;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.EformAngularFrontendBase.Infrastructure.Data;
using Microting.eFormApi.BasePn.Abstractions;

public class ChemicalNameDirectory(BaseDbContext baseDbContext, IEFormCoreService coreHelper) : IChemicalNameDirectory
{
    public async Task<IReadOnlyDictionary<int, string>> UserNamesAsync(IEnumerable<int> userIds)
    {
        var ids = userIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<int, string>();
        }

        var users = await baseDbContext.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync().ConfigureAwait(false);
        return users.ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());
    }

    public async Task<IReadOnlyDictionary<int, string>> WorkerNamesAsync(IEnumerable<int> sdkSiteIds)
    {
        var ids = sdkSiteIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<int, string>();
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var sites = await sdkDbContext.Sites.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToListAsync().ConfigureAwait(false);
        return sites.ToDictionary(s => s.Id, s => s.Name ?? string.Empty);
    }
}
