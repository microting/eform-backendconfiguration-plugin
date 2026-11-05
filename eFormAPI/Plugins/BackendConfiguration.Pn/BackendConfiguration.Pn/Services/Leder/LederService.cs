#nullable enable
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Services.ArchiveAccess;
using Microsoft.EntityFrameworkCore;
using Microting.EformAngularFrontendBase.Infrastructure.Data;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.Leder;

public class LederService(IUserService userService, BaseDbContext baseDbContext,
    BackendConfigurationPnDbContext dbContext, IEFormCoreService coreHelper) : ILederService
{
    // A failed first call stays cached for the request: the faulted task is returned again, not retried.
    private Task<bool>? _result;

    public Task<bool> IsLederOrAdminAsync(LederLiveness? known = null) => _result ??= ResolveAsync(known);

    private async Task<bool> ResolveAsync(LederLiveness? known)
    {
        var userId = userService.UserId;
        if (userId < 1)
        {
            return false;
        }

        if (userService.IsAdmin())
        {
            return true;
        }

        // D-L18: an active login whose live sites include a flagged one. A resigned or removed worker has no live
        // site, so its flag is inert until rehire.
        // Without known liveness, a tenant with no flagged site skips the login and SDK lookups altogether.
        if (known != null || await LederAssignmentStore.AnySiteLederAsync(dbContext))
        {
            var liveness = known ?? await ResolveLivenessAsync(userId);
            if (liveness.IsActive && await LederAssignmentStore.AnyLiveSiteAsync(dbContext, liveness.LiveSiteIds))
            {
                return true;
            }
        }

        return await BackendConfigurationAssignmentWorkerServiceHelper.IsAdminAsync(baseDbContext, userId);
    }

    /// <summary>The login's IsActive and the live sites its e-mail resolves to; no sites for an inactive or address-less login.</summary>
    private async Task<LederLiveness> ResolveLivenessAsync(int userId)
    {
        var login = await baseDbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.Email })
            .FirstOrDefaultAsync();
        if (login is not { IsActive: true })
        {
            return new LederLiveness(false, []);
        }

        if (string.IsNullOrWhiteSpace(login.Email))
        {
            return new LederLiveness(true, []);
        }

        var core = await coreHelper.GetCore();
        await using var sdk = core.DbContextHelper.GetDbContext();
        return new LederLiveness(true, (await CallerSites.ResolveAsync(sdk, login.Email)).LiveSiteIds);
    }
}
