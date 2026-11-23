#nullable enable
using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BackendConfiguration.Pn.Hubs;

/// <summary>
/// Live-update hints for the mobile task list (package 10), mapped by the
/// plugin itself at <see cref="LiveHubContract.HubPath"/>; the contract is
/// <see cref="LiveHubContract"/> and docs/live-updates-hub-contract.md.
/// <para>
/// No [Authorize]: the host maps plugin endpoints without UseAuthorization,
/// so authorization metadata would fail every request (kanban's KanbanHub,
/// plan deviation 1). Each connection is authenticated in
/// <see cref="OnConnectedAsync"/> instead, with the host JWT (header, or
/// <c>?access_token=</c> for WebSockets) and the gRPC handlers' caller rules.
/// </para>
/// <para>
/// Groups: the worker's own site group, joined on connect; one group per
/// property, joined only through <see cref="JoinProperty"/> after the same
/// property-access check as the gRPC Events calls. Group membership lives
/// only in <see cref="LiveConnectionRegistry"/> (no SignalR groups): hints are
/// sent to the connection ids it lists, so a revoke takes effect at once.
/// </para>
/// <para>
/// A connection ends when its token expires: JoinProperty refuses and aborts
/// it, and every publish first aborts the expired connections
/// (<see cref="Services.LiveNotifier.LiveNotifier"/>), so an expired token
/// receives no further hints. The client reconnects with a fresh token.
/// </para>
/// <para>Logs carry connection, user and site ids only (no email, no token).</para>
/// </summary>
public class BackendConfigurationHub(
    LiveHubAuthenticator authenticator,
    ILiveCallerResolver callers,
    ILivePropertyAccess propertyAccess,
    LiveConnectionRegistry registry,
    ILogger<BackendConfigurationHub> logger) : Hub
{
    internal const string UserIdKey = "backend-configuration.live.user-id";
    internal const string ValidUntilKey = "backend-configuration.live.valid-until";

    /// <summary>
    /// Authenticates the connection. A refusal does not throw (SignalR would
    /// log every refused reconnect at Error with a stack trace): the caller
    /// gets <see cref="LiveHubContract.ServerMethods.Refused"/> with the code,
    /// then the connection is aborted, and the refusal is logged at
    /// Information. Order: token and expiry, then the per-user connection cap,
    /// and only then the database lookups of the caller.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var auth = await authenticator.AuthenticateAsync(Context.User, Context.GetHttpContext());
        var userId = auth?.UserId;
        var validUntil = auth?.ValidUntil;
        if (userId is null || validUntil is null || validUntil <= registry.UtcNow)
        {
            await RefuseAsync(LiveHubContract.Errors.NotAuthenticated, userId);
            return;
        }

        if (!registry.TryConnect(Context.ConnectionId, userId.Value, validUntil.Value, Context.Abort,
                LiveHubLimits.MaxConnectionsPerUser))
        {
            await RefuseAsync(LiveHubContract.Errors.TooManyConnections, userId);
            return;
        }

        try
        {
            LiveCaller caller;
            try
            {
                caller = await callers.ResolveAsync(userId.Value);
            }
            catch (HubException e)
            {
                registry.Disconnected(Context.ConnectionId);
                await RefuseAsync(e.Message, userId);
                return;
            }

            registry.SetSite(Context.ConnectionId, caller.SdkSiteId);
            Context.Items[UserIdKey] = userId.Value;
            Context.Items[ValidUntilKey] = validUntil.Value;
            if (caller.IsWorker)
            {
                registry.Joined(Context.ConnectionId, LiveHubContract.SiteGroup(caller.SdkSiteId));
            }

            await base.OnConnectedAsync();
            logger.LogDebug("Live hub: connection {ConnectionId} of user {UserId}, site {SdkSiteId}",
                Context.ConnectionId, caller.UserId, caller.SdkSiteId);
        }
        catch
        {
            // Unexpected failure: SignalR does not call OnDisconnectedAsync for a
            // connection whose OnConnectedAsync failed.
            registry.Disconnected(Context.ConnectionId);
            throw;
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.Disconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Tells the caller why (best effort), then closes the connection. Never logs the token.</summary>
    private async Task RefuseAsync(string code, int? userId)
    {
        logger.LogInformation("Live hub: refused connection {ConnectionId} of user {UserId}: {Code}",
            Context.ConnectionId, userId, code);
        try
        {
            // The base interface's Caller: the IHubCallerClients one is a default
            // interface member (ISingleClientProxy) that test doubles cannot stub.
            IHubCallerClients<IClientProxy> clients = Clients;
            await clients.Caller.SendAsync(LiveHubContract.ServerMethods.Refused, new RefusedMessage { Code = code });
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Live hub: could not send the refusal to {ConnectionId}", Context.ConnectionId);
        }

        Context.Abort();
    }

    /// <summary>
    /// Joins the property's group. Answers a <see cref="JoinPropertyResult"/>
    /// (code <see cref="LiveHubContract.Results.Ok"/> or one of
    /// <see cref="LiveHubContract.Errors"/>); only an unexpected fault throws.
    /// One refusal, <see cref="LiveHubContract.Errors.PropertyNotAvailable"/>,
    /// for a property that does not exist, is not the caller's, whose
    /// assignment was removed, a malformed id and a non-worker, so ids cannot
    /// be probed. The caller is resolved again (a worker resigned since
    /// connect is refused). An expired token is answered
    /// <see cref="LiveHubContract.Errors.NotAuthenticated"/> and the connection
    /// is closed.
    /// </summary>
    public async Task<JoinPropertyResult> JoinProperty(string propertyId)
    {
        if (!Context.Items.TryGetValue(UserIdKey, out var item) || item is not int userId)
            return Result(LiveHubContract.Errors.NotAuthenticated);
        if (Context.Items.TryGetValue(ValidUntilKey, out var until) && until is DateTimeOffset validUntil
            && validUntil <= registry.UtcNow)
        {
            // The client must reconnect with a fresh token.
            Context.Abort();
            return Result(LiveHubContract.Errors.NotAuthenticated);
        }

        LiveCaller caller;
        try
        {
            caller = await callers.ResolveAsync(userId);
        }
        catch (HubException e)
        {
            // The caller is disabled, resigned or gone since connect (possibly in the
            // host's user admin, which revokes nothing itself): end the connection, so
            // it also stops receiving the groups it had joined.
            registry.Disconnected(Context.ConnectionId);
            Context.Items.Remove(UserIdKey);
            await RefuseAsync(e.Message, userId);
            return Result(e.Message);
        }

        if (!TryParseId(propertyId, out var id) || registry.SiteOf(Context.ConnectionId) != caller.SdkSiteId)
            return Result(LiveHubContract.Errors.PropertyNotAvailable);

        var group = LiveHubContract.PropertyGroup(id);
        if (!registry.IsIn(Context.ConnectionId, group)
            && registry.CountGroups(Context.ConnectionId, LiveHubContract.PropertyGroupPrefix) >= LiveHubLimits.MaxPropertyGroupsPerConnection)
            return Result(LiveHubContract.Errors.TooManyProperties);

        // Read BEFORE the access check: a revoke between the check and the
        // add changes it, and the join is undone (kanban's KanbanHub.Join).
        var revocationsBefore = registry.RevocationCount(caller.SdkSiteId, id);
        if (!await propertyAccess.CanJoinPropertyAsync(caller, id))
            return Result(LiveHubContract.Errors.PropertyNotAvailable);

        registry.Joined(Context.ConnectionId, group);
        if (registry.RevocationCount(caller.SdkSiteId, id) != revocationsBefore)
        {
            registry.Left(Context.ConnectionId, group);
            return Result(LiveHubContract.Errors.PropertyNotAvailable);
        }

        return Result(LiveHubContract.Results.Ok);
    }

    /// <summary>Leaves the property's group. Idempotent; a malformed or unknown id is a no-op.</summary>
    public Task LeaveProperty(string propertyId)
    {
        if (TryParseId(propertyId, out var id))
            registry.Left(Context.ConnectionId, LiveHubContract.PropertyGroup(id));
        return Task.CompletedTask;
    }

    private static JoinPropertyResult Result(string code) => new() { Code = code };

    /// <summary>A gRPC wire id: plain decimal digits, positive.</summary>
    private static bool TryParseId(string? value, out int id) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}
