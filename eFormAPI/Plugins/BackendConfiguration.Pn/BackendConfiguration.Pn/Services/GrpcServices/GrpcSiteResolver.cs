using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;

namespace BackendConfiguration.Pn.Services.GrpcServices;

public interface IGrpcSiteResolver
{
    Task<int> GetSdkSiteIdAsync();
}

public class GrpcSiteResolver(IUserService userService, IEFormCoreService coreHelper)
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
            return 0;
        }

        var siteWorker = await sdkDbContext.SiteWorkers
            .Where(sw => sw.WorkerId == worker.Id && sw.WorkflowState != Constants.WorkflowStates.Removed)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        return siteWorker?.SiteId ?? 0;
    }
}
