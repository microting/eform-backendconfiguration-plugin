using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.EventDeployService;

/// <summary>
/// Eagerly deploys SDK cases + Compliance rows for rotations inside the
/// requested date window so that flutter-eform can complete future events
/// (today+1, today+2) via the existing CompleteEvent path. Runs inline in
/// the gRPC handler; does NOT publish Rebus messages and does NOT mutate
/// scheduler-owned Planning state.
/// </summary>
public interface IEventDeployService
{
    Task EnsureDeployedAsync(
        string propertyId,
        IReadOnlyCollection<string> boardIds,
        string fromDateKey,
        string toDateKey,
        int sdkSiteId,
        CancellationToken cancellationToken);
}
