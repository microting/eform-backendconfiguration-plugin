using System.Collections.Generic;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.UserPropertyAccess;

public interface IBackendConfigurationUserPropertyAccess
{
    Task<List<int>> GetAccessiblePropertyIdsAsync(int sdkSiteId);
    Task<bool> HasAccessAsync(int sdkSiteId, int propertyId);
}
