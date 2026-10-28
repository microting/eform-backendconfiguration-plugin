#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using static Microting.eForm.Infrastructure.Constants.Constants.WorkflowStates;

namespace BackendConfiguration.Pn.Services.ArchiveAccess;

/// <summary>
/// The SDK side of a login, found by its e-mail. <see cref="AllWorkersResigned"/> drives D7; <see cref="LiveSiteIds"/>
/// feeds the medarbejdergrupper and the Leder liveness (Leder spec section 3).
/// </summary>
public sealed record CallerSiteResolution(bool AllWorkersResigned, IReadOnlyList<int> LiveSiteIds);

/// <summary>One rule for "which sites does this login work on", shared by ArchiveAccessService and LederService.</summary>
public static class CallerSites
{
    /// <summary>
    /// The non-removed SDK workers with <paramref name="email"/>. When there are some and every one is resigned, the
    /// login is disabled (the same choice as GrpcSiteResolver, #1326). Otherwise the distinct sites of the non-resigned
    /// workers through live SiteWorker rows; core.SiteDelete removes those rows, so a deleted site never counts.
    /// </summary>
    public static async Task<CallerSiteResolution> ResolveAsync(MicrotingDbContext sdk, string email)
    {
        var workers = await sdk.Workers.AsNoTracking()
            .Where(w => w.Email == email && w.WorkflowState != Removed)
            .Select(w => new { w.Id, w.Resigned })
            .ToListAsync();
        if (workers.Count > 0 && workers.All(w => w.Resigned))
        {
            return new CallerSiteResolution(true, []);
        }

        var liveWorkerIds = workers.Where(w => !w.Resigned).Select(w => w.Id).ToList();
        if (liveWorkerIds.Count == 0)
        {
            return new CallerSiteResolution(false, []);
        }

        var siteIds = await sdk.SiteWorkers.AsNoTracking()
            .Where(sw => sw.WorkerId != null && liveWorkerIds.Contains(sw.WorkerId.Value)
                         && sw.SiteId != null && sw.WorkflowState != Removed)
            .Select(sw => sw.SiteId!.Value)
            .Distinct()
            .ToListAsync();
        return new CallerSiteResolution(false, siteIds);
    }
}
