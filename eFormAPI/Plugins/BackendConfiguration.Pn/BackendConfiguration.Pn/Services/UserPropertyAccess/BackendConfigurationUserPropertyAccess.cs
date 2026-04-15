using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.UserPropertyAccess;

public class BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext dbContext)
    : IBackendConfigurationUserPropertyAccess
{
    public Task<List<int>> GetAccessiblePropertyIdsAsync(int sdkSiteId)
    {
        return dbContext.PropertyWorkers
            .Where(x => x.WorkerId == sdkSiteId
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.PropertyId)
            .Distinct()
            .ToListAsync();
    }

    public Task<bool> HasAccessAsync(int sdkSiteId, int propertyId)
    {
        return dbContext.PropertyWorkers
            .AnyAsync(x => x.WorkerId == sdkSiteId
                           && x.PropertyId == propertyId
                           && x.WorkflowState != Constants.WorkflowStates.Removed);
    }
}
