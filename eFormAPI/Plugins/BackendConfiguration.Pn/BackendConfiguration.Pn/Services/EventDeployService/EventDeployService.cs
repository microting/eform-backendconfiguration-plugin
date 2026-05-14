using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BackendConfiguration.Pn.Services.EventDeployService;

public class EventDeployService(ILogger<EventDeployService> logger) : IEventDeployService
{
    public Task EnsureDeployedAsync(
        string propertyId,
        IReadOnlyCollection<string> boardIds,
        string fromDateKey,
        string toDateKey,
        int sdkSiteId,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "EventDeployService.EnsureDeployedAsync stub: propertyId={PropertyId} boardIds=[{BoardIds}] window={From}..{To} sdkSiteId={SdkSiteId}",
            propertyId,
            string.Join(",", boardIds),
            fromDateKey,
            toDateKey,
            sdkSiteId);
        return Task.CompletedTask;
    }
}
