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

    public Task<bool> IsLederOrAdminAsync(bool? knownLive = null) => _result ??= ResolveAsync(knownLive);

    private async Task<bool> ResolveAsync(bool? knownLive)
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

        // The flag first: one indexed plugin-DB lookup decides most Leders. Liveness only when there is a flag.
        if (await LederAssignmentStore.HasLiveRowAsync(dbContext, userId)
            && (knownLive ?? await IsLiveAsync(userId)))
        {
            return true;
        }

        return await BackendConfigurationAssignmentWorkerServiceHelper.IsAdminAsync(baseDbContext, userId);
    }

    /// <summary>Section 3 liveness: an active login whose address has a non-resigned worker on a live site.</summary>
    private async Task<bool> IsLiveAsync(int userId)
    {
        var login = await baseDbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.Email })
            .FirstOrDefaultAsync();
        if (login is not { IsActive: true } || string.IsNullOrWhiteSpace(login.Email))
        {
            return false;
        }

        var core = await coreHelper.GetCore();
        await using var sdk = core.DbContextHelper.GetDbContext();
        return (await CallerSites.ResolveAsync(sdk, login.Email)).LiveSiteIds.Count > 0;
    }
}
