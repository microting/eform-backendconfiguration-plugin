using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;

namespace BackendConfiguration.Pn.Services.GrpcServices;

public interface IGrpcSiteResolver
{
    Task<int> GetSdkSiteIdAsync();
}

public class GrpcSiteResolver(
    IUserService userService,
    IEFormCoreService coreHelper,
    ILogger<GrpcSiteResolver> logger)
    : IGrpcSiteResolver
{
    public async Task<int> GetSdkSiteIdAsync()
    {
        var user = await userService.GetCurrentUserAsync().ConfigureAwait(false);
        if (user?.Email == null)
        {
            return 0;
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        var worker = await sdkDbContext.Workers
            .Where(w => w.Email == user.Email && w.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (worker == null)
        {
            // Since #931 made gRPC reads site-scoped, a 0 here means the caller
            // sees an EMPTY calendar. Surface the misconfiguration so it can be
            // diagnosed rather than mistaken for "no tasks".
            logger.LogWarning(
                "GrpcSiteResolver: no non-removed SDK worker for the current user's email; "
                + "gRPC event reads will be site-scoped to 0 and return empty (#931).");
            return 0;
        }

        // Deterministic site selection: a worker enrolled on more than one site
        // must always resolve to the SAME site, otherwise the #931 site-scoped
        // reads would non-deterministically surface a different — or empty —
        // calendar depending on which SiteWorker row the engine returned first.
        var siteWorker = await sdkDbContext.SiteWorkers
            .Where(sw => sw.WorkerId == worker.Id && sw.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(sw => sw.Id)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (siteWorker == null)
        {
            logger.LogWarning(
                "GrpcSiteResolver: SDK worker {WorkerId} has no non-removed SiteWorker mapping; "
                + "gRPC event reads will be site-scoped to 0 and return empty (#931).",
                worker.Id);
            return 0;
        }

        return siteWorker.SiteId ?? 0;
    }
}
