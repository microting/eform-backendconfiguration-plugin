#nullable enable
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Grpc.Core;
using Microsoft.AspNetCore.SignalR;
using Microting.eFormApi.BasePn.Abstractions;

namespace BackendConfiguration.Pn.Hubs;

/// <summary>
/// A hub caller: the host user and, for a worker, the SDK site the gRPC
/// handlers resolve for the same user. <see cref="SdkSiteId"/> 0 = not a
/// worker (a web user, or a worker with no site), exactly as
/// <see cref="IGrpcSiteResolver.GetSdkSiteIdAsync"/> answers 0.
/// </summary>
public sealed record LiveCaller(int UserId, int SdkSiteId)
{
    public bool IsWorker => SdkSiteId > 0;
}

/// <summary>Token user id to <see cref="LiveCaller"/>.</summary>
public interface ILiveCallerResolver
{
    /// <summary>
    /// Throws <see cref="HubException"/> <see cref="LiveHubContract.Errors.NotAuthenticated"/>
    /// when the user does not exist, and <see cref="LiveHubContract.Errors.AccountDisabled"/>
    /// when the gRPC handlers would refuse the same user (disabled account,
    /// resigned worker).
    /// </summary>
    Task<LiveCaller> ResolveAsync(int userId);
}

/// <summary>
/// One identity model with the gRPC handlers: the same user row the host's
/// <c>IUserService.GetCurrentUserAsync</c> loads (by id), then the same
/// <see cref="GrpcSiteResolver"/> lookups and refusals (user to email to SDK
/// worker to first SiteWorker).
/// </summary>
public class LiveCallerResolver(IUserService userService, IGrpcSiteResolver siteResolver) : ILiveCallerResolver
{
    public async Task<LiveCaller> ResolveAsync(int userId)
    {
        var user = await userService.GetByIdAsync(userId).ConfigureAwait(false);
        if (user is null)
        {
            throw new HubException(LiveHubContract.Errors.NotAuthenticated);
        }

        try
        {
            var siteId = await siteResolver.GetSdkSiteIdForUserAsync(user).ConfigureAwait(false);
            return new LiveCaller(userId, siteId);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Unauthenticated)
        {
            throw new HubException(LiveHubContract.Errors.AccountDisabled);
        }
    }
}

/// <summary>
/// Who may join a property's group. A seam: today only workers
/// (<see cref="WorkerLivePropertyAccess"/>); the web calendar (package 11)
/// adds the rule its REST endpoints use for non-worker web users.
/// </summary>
public interface ILivePropertyAccess
{
    Task<bool> CanJoinPropertyAsync(LiveCaller caller, int propertyId);
}

/// <summary>
/// The gRPC <c>Events</c> rule, verbatim: a worker reads a property exactly
/// when <see cref="IBackendConfigurationUserPropertyAccess.HasAccessAsync"/>
/// says so (a non-removed PropertyWorker row for the site). A non-worker is
/// refused.
/// </summary>
public class WorkerLivePropertyAccess(IBackendConfigurationUserPropertyAccess propertyAccess) : ILivePropertyAccess
{
    public Task<bool> CanJoinPropertyAsync(LiveCaller caller, int propertyId) =>
        caller.IsWorker
            ? propertyAccess.HasAccessAsync(caller.SdkSiteId, propertyId)
            : Task.FromResult(false);
}
