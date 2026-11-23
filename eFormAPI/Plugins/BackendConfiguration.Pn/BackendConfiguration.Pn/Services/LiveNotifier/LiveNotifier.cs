#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BackendConfiguration.Pn.Services.LiveNotifier;

/// <summary>
/// Publishes live-update hints to the hub (<see cref="LiveHubContract"/>).
/// What later PRs call after a change has been committed (the change
/// collector, the explicit SDK-write paths, the feed reader).
/// <para>
/// Group membership lives only in <see cref="LiveConnectionRegistry"/>; there
/// are no SignalR groups. Every publish first ends the connections whose
/// token has expired (taken out of the registry, <c>Refused {code:
/// "not_authenticated"}</c>, aborted), then sends only to the connection ids
/// the registry lists as valid members of the group
/// (<see cref="LiveConnectionRegistry.EligibleMembers"/>): an expired or
/// revoked connection receives no further hints.
/// </para>
/// <para>
/// Safe to call from anywhere: never throws into the caller (a failure is
/// logged and dropped; clients resync on their safety poll and on
/// reconnect), and a no-op when nobody joined the target group. A hint, not
/// a delivery guarantee.
/// </para>
/// </summary>
public interface ILiveNotifier
{
    /// <summary>
    /// <c>TaskChanged</c> to the property's group. Pass what is known of the
    /// occurrence key (<paramref name="eventId"/> = AreaRulePlanning id,
    /// <paramref name="planDay"/> = the plan day, <paramref name="complianceId"/>);
    /// with no key the hint means "resync the property".
    /// <paramref name="kind"/> is one of <see cref="TaskChangeKinds"/>.
    /// </summary>
    Task TaskChangedAsync(int propertyId, int? eventId = null, DateOnly? planDay = null, long? complianceId = null,
        string kind = TaskChangeKinds.Changed);

    /// <summary><c>AssignmentsChanged</c> to the worker's own site group (properties or worker tags added/removed).</summary>
    Task AssignmentsChangedAsync(int sdkSiteId);

    /// <summary>
    /// Takes the site's live connections out of the property's group. Call
    /// after a PropertyWorker removal has been committed, then
    /// <see cref="AssignmentsChangedAsync"/>; a JoinProperty racing with it is
    /// undone.
    /// </summary>
    Task RevokePropertyAccessAsync(int sdkSiteId, int propertyId);

    /// <summary>
    /// Ends every live connection of the site: takes it out of the registry,
    /// sends <c>Refused {code: "revoked"}</c> (best effort) and aborts it. Call
    /// after a worker is removed or resigned (or <c>IsActive</c> set false), or
    /// its site removed, has been committed. A reconnect then goes through the
    /// connect checks again (and is refused with <c>account_disabled</c>).
    /// </summary>
    Task RevokeSiteAsync(int sdkSiteId);

    /// <summary>
    /// Takes every live connection, worker or web user, out of the property's
    /// group; the connections stay open. Call after the property's removal
    /// has been committed. A join racing with it is undone.
    /// </summary>
    Task RevokePropertyForEveryoneAsync(int propertyId);

    /// <summary>
    /// Ends every live connection of the host user (<c>EformUser.Id</c>),
    /// whatever its site: <c>Refused {code: "revoked"}</c>, then abort. Call
    /// after the login was deactivated or deleted.
    /// </summary>
    Task RevokeUserAsync(int userId);
}

public class LiveNotifier(
    IHubContext<BackendConfigurationHub> hubContext,
    LiveConnectionRegistry registry,
    ILogger<LiveNotifier> logger) : ILiveNotifier
{
    public Task TaskChangedAsync(int propertyId, int? eventId = null, DateOnly? planDay = null, long? complianceId = null,
        string kind = TaskChangeKinds.Changed) =>
        SendAsync(LiveHubContract.PropertyGroup(propertyId), LiveHubContract.ServerMethods.TaskChanged,
            () => new TaskChangedMessage
            {
                PropertyId = propertyId.ToString(CultureInfo.InvariantCulture),
                EventId = eventId?.ToString(CultureInfo.InvariantCulture),
                PlanDayKey = planDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ComplianceId = complianceId,
                Kind = kind
            });

    public Task AssignmentsChangedAsync(int sdkSiteId) =>
        SendAsync(LiveHubContract.SiteGroup(sdkSiteId), LiveHubContract.ServerMethods.AssignmentsChanged,
            () => new AssignmentsChangedMessage());

    public Task RevokePropertyAccessAsync(int sdkSiteId, int propertyId)
    {
        // Membership lives only in the registry, so taking the site's
        // connections out of the group there is the whole revoke.
        var group = LiveHubContract.PropertyGroup(propertyId);
        try
        {
            var taken = registry.Revoke(sdkSiteId, propertyId, group);
            if (taken.Count > 0)
                logger.LogInformation("Live hub: took {Count} connection(s) of site {SdkSiteId} out of {Group}",
                    taken.Count, sdkSiteId, group);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Live hub: revoking site {SdkSiteId} from {Group} failed", sdkSiteId, group);
        }

        return Task.CompletedTask;
    }

    public Task RevokePropertyForEveryoneAsync(int propertyId)
    {
        var group = LiveHubContract.PropertyGroup(propertyId);
        try
        {
            var taken = registry.RevokePropertyForEveryone(propertyId, group);
            if (taken.Count > 0)
                logger.LogInformation("Live hub: took {Count} connection(s) out of {Group} (property removed)",
                    taken.Count, group);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Live hub: revoking {Group} for everyone failed", group);
        }

        return Task.CompletedTask;
    }

    public async Task RevokeUserAsync(int userId)
    {
        try
        {
            await EndAsync(registry.TakeUser(userId), LiveHubContract.Errors.Revoked).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Live hub: revoking user {UserId} failed", userId);
        }
    }

    private void Abort(string connectionId, Action abort)
    {
        try
        {
            abort();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Live hub: aborting connection {ConnectionId} failed", connectionId);
        }
    }

    public async Task RevokeSiteAsync(int sdkSiteId)
    {
        try
        {
            await EndAsync(registry.TakeSite(sdkSiteId), LiveHubContract.Errors.Revoked).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Live hub: revoking site {SdkSiteId} failed", sdkSiteId);
        }
    }

    /// <summary>Tells the taken (already unlisted) connections why, then aborts them; per connection, never throws.</summary>
    private async Task EndAsync(IReadOnlyList<LiveConnectionRegistry.Taken> taken, string code)
    {
        foreach (var connection in taken)
        {
            try
            {
                // Clients(list), not Client(id): the latter is a default interface member.
                await hubContext.Clients.Clients([connection.ConnectionId])
                    .SendAsync(LiveHubContract.ServerMethods.Refused, new RefusedMessage { Code = code })
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Live hub: ending connection {ConnectionId} ({Code}) failed", connection.ConnectionId, code);
            }
            finally
            {
                Abort(connection.ConnectionId, connection.Abort);
            }
        }

        if (taken.Count > 0)
            logger.LogInformation("Live hub: ended {Count} connection(s): {Code}", taken.Count, code);
    }

    private async Task SendAsync(string group, string method, Func<object> payload)
    {
        try
        {
            await EndAsync(registry.TakeExpired(), LiveHubContract.Errors.NotAuthenticated).ConfigureAwait(false);
            // To the registry's eligible members: a connection taken out by a
            // concurrent sweep or revoke is no longer listed, so it cannot receive.
            var recipients = registry.EligibleMembers(group);
            if (recipients.Count == 0) return;
            await hubContext.Clients.Clients(recipients).SendAsync(method, payload()).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Live hub: {Method} to {Group} failed", method, group);
        }
    }
}
