using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Abstractions;

namespace BackendConfiguration.Pn.Services.GrpcServices;

public interface IGrpcSiteResolver
{
    /// <summary>
    /// The SDK site id of the calling worker, or 0 when the caller has no
    /// resolvable worker/site. Throws <see cref="RpcException"/>
    /// (<see cref="StatusCode.Unauthenticated"/>, detail
    /// <see cref="GrpcSiteResolver.AccountDisabledDetail"/>) when the caller's
    /// account is disabled — see <see cref="EnsureCallerActiveAsync"/> (#1326).
    /// </summary>
    Task<int> GetSdkSiteIdAsync();

    /// <summary>
    /// Refuses a caller whose account is disabled: <c>EformUser.IsActive</c> is
    /// false, or every non-removed SDK <c>Worker</c> for the caller's email is
    /// <c>Resigned</c>. For RPCs that do not need the caller's site but must not
    /// serve a resigned worker either (e.g. <c>TemplatesGrpcService.GetTemplate</c>).
    /// A caller with no user or no worker at all is left to the RPC's own
    /// handling, exactly as <see cref="GetSdkSiteIdAsync"/> leaves it (#1326).
    /// </summary>
    Task EnsureCallerActiveAsync();

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
    /// <summary>
    /// Stable <see cref="Status.Detail"/> of the <see cref="StatusCode.Unauthenticated"/>
    /// status a disabled account gets, so clients can tell "sign out, you were
    /// resigned" apart from an ordinary expired/invalid token (#1326).
    /// </summary>
    public const string AccountDisabledDetail = "account_disabled";

    public async Task<int> GetSdkSiteIdAsync()
    {
        var (worker, sdkDbContext) = await ResolveActiveCallerAsync().ConfigureAwait(false);
        if (sdkDbContext == null)
        {
            return 0;
        }

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

    public Task EnsureCallerActiveAsync() => ResolveActiveCallerAsync();

    /// <summary>
    /// Loads the current user and their SDK worker, and throws
    /// <c>Unauthenticated</c>/<see cref="AccountDisabledDetail"/> when the account
    /// is disabled. Resigning a worker (#1282) sets <c>EformUser.IsActive = false</c>
    /// and refuses new logins, but an issued JWT stays valid for up to 24h and the
    /// SDK Site/Worker stay <c>created</c> — without this check an open session
    /// kept being served. <c>Worker.Resigned</c> also catches accounts whose
    /// <c>IsActive</c> was never written (legacy rows, #1286).
    /// <para>
    /// <c>sdkDbContext</c> is null when there is no current user/email; <c>worker</c>
    /// is null when no non-removed worker matches the email.
    /// </para>
    /// </summary>
    private async Task<(Worker worker, MicrotingDbContext sdkDbContext)> ResolveActiveCallerAsync()
    {
        var user = await userService.GetCurrentUserAsync().ConfigureAwait(false);
        if (user?.Email == null)
        {
            return (null, null);
        }

        if (!user.IsActive)
        {
            throw AccountDisabled(user.Id, "EformUser.IsActive is false");
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        // Non-resigned first: a re-hired worker whose old, resigned Worker row
        // is still around (same email) must resolve to the live row, not be
        // refused. Only when every non-removed row is resigned is the caller
        // disabled. Id keeps the pick deterministic.
        var worker = await sdkDbContext.Workers
            .Where(w => w.Email == user.Email && w.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(w => w.Resigned)
            .ThenBy(w => w.Id)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (worker is { Resigned: true })
        {
            throw AccountDisabled(user.Id, "SDK worker is resigned");
        }

        return (worker, sdkDbContext);
    }

    private RpcException AccountDisabled(int userId, string reason)
    {
        // User id only — never the email (the log is not a place for personal data).
        logger.LogInformation(
            "GrpcSiteResolver: refused gRPC call for user {UserId}: {Reason} (#1326).",
            userId, reason);
        return new RpcException(new Status(StatusCode.Unauthenticated, AccountDisabledDetail));
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
