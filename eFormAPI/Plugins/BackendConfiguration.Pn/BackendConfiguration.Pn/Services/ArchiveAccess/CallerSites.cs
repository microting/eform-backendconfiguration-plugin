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
    /// workers through live SiteWorker rows whose Site is not removed too.
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

        return new CallerSiteResolution(false, await LiveSitesOfWorkersAsync(sdk, liveWorkerIds));
    }

    /// <summary>
    /// The distinct live sites of every non-removed SDK worker with <paramref name="email"/>, resigned workers included:
    /// where a per-login Leder flag goes when it is converted to its workers (D-L19). A resigned Leder's flag stays on
    /// its site, inert until rehire (D-L18).
    /// </summary>
    public static async Task<List<int>> SitesOfWorkersAsync(MicrotingDbContext sdk, string email)
    {
        var workerIds = await sdk.Workers.AsNoTracking()
            .Where(w => w.Email == email && w.WorkflowState != Removed)
            .Select(w => w.Id)
            .ToListAsync();
        if (workerIds.Count == 0)
        {
            return [];
        }

        var siteIds = await LiveSitesOfWorkersAsync(sdk, workerIds);
        siteIds.Sort();
        return siteIds;
    }

    /// <summary>
    /// The distinct sites of <paramref name="workerIds"/> through live SiteWorker rows whose Site is not removed. Also
    /// the sites a worker edit touches: H2 asks whether any of them carries the Leder flag.
    /// </summary>
    internal static Task<List<int>> LiveSitesOfWorkersAsync(MicrotingDbContext sdk, IReadOnlyCollection<int> workerIds) =>
        (from sw in sdk.SiteWorkers.AsNoTracking()
            join site in sdk.Sites.AsNoTracking() on sw.SiteId equals site.Id
            where sw.WorkerId != null && workerIds.Contains(sw.WorkerId.Value)
                  && sw.WorkflowState != Removed && site.WorkflowState != Removed
            select site.Id)
        .Distinct()
        .ToListAsync();
}
