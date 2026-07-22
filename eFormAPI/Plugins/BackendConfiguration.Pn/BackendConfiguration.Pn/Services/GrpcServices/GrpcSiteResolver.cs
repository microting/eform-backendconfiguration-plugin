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

    /// <summary>
    /// The SDK Site.LanguageId of the calling worker's resolved site, so the
    /// gRPC path can serve Title/Description/eForm in the worker's own language.
    /// Null when the worker has no resolvable site / no language (caller falls
    /// back to its existing default language). Pass <paramref name="preResolvedSiteId"/>
    /// when the caller already resolved the site via <see cref="GetSdkSiteIdAsync"/>
    /// to avoid re-running the worker→site lookup.
    /// </summary>
    Task<int?> GetSiteLanguageIdAsync(int? preResolvedSiteId = null);

    /// <summary>
    /// The SDK <c>Site.Name</c> for <paramref name="sdkSiteId"/>, or an empty
    /// string when the site does not exist. Used by <c>AdhocGrpcService.GetCurrentWorker</c>
    /// (and mirrors the same lookup <c>BackendConfigurationAdhocService.ListWorkers</c>
    /// performs for other workers) to give the mobile client a human-readable
    /// label for the caller without requiring every façade to talk to the SDK
    /// core directly.
    /// </summary>
    Task<string> GetDisplayNameAsync(int sdkSiteId);
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

    public async Task<int?> GetSiteLanguageIdAsync(int? preResolvedSiteId = null)
    {
        var siteId = preResolvedSiteId ?? await GetSdkSiteIdAsync().ConfigureAwait(false);
        if (siteId == 0)
        {
            return null;
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        // `LanguageId > 0` guards a malformed Site (LanguageId 0): return null so
        // the caller falls back to its default rather than querying language 0.
        return await sdkDbContext.Sites
            .Where(s => s.Id == siteId && s.LanguageId > 0)
            .Select(s => (int?)s.LanguageId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task<string> GetDisplayNameAsync(int sdkSiteId)
    {
        if (sdkSiteId == 0)
        {
            return string.Empty;
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        return await sdkDbContext.Sites
            .Where(s => s.Id == sdkSiteId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false) ?? string.Empty;
    }
}
