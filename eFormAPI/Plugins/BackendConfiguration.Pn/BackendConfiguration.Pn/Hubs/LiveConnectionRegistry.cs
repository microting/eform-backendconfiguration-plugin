#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BackendConfiguration.Pn.Hubs;

/// <summary>
/// The live connections: for each one its user, SDK site, token expiry, a
/// way to abort it, and the groups it joined. SignalR checks access only when
/// a group is joined, so everything that takes access away goes through here:
/// <list type="bullet">
/// <item>a removed property assignment (<see cref="Revoke"/>) takes the
/// site's connections out of that property's group;</item>
/// <item>a removed or resigned worker (<see cref="TakeSite"/>) and an expired
/// token (<see cref="TakeExpired"/>) end the connection.</item>
/// </list>
/// It also caps the connections per user (<see cref="TryConnect"/>).
/// Kanban's <c>HubConnectionRegistry</c> pattern.
/// <para>
/// Singleton, in memory, so per process: the web host runs one instance per
/// customer and there is no backplane (docs/live-updates-hub-contract.md).
/// </para>
/// </summary>
public class LiveConnectionRegistry(TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Connection> _connections = new();
    private readonly Dictionary<(int SdkSiteId, int PropertyId), long> _revocations = new();

    private sealed class Connection(int userId, DateTimeOffset expiresAt, Action abort)
    {
        public int UserId { get; } = userId;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public Action Abort { get; } = abort;
        public int SdkSiteId { get; set; }
        public HashSet<string> Groups { get; } = [];
    }

    /// <summary>A connection taken out of the registry: abort it and remove it from these SignalR groups.</summary>
    public sealed record Taken(string ConnectionId, Action Abort, IReadOnlyList<string> Groups);

    /// <summary>The registry's clock; the hub's expiry checks use the same one.</summary>
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    /// <summary>
    /// Registers a connection unless the user already has
    /// <paramref name="maxPerUser"/> live ones; check and add are atomic. The
    /// site is set later (<see cref="SetSite"/>), once the caller is resolved.
    /// False = refused, nothing registered.
    /// </summary>
    public bool TryConnect(string connectionId, int userId, DateTimeOffset expiresAt, Action abort, int maxPerUser)
    {
        lock (_gate)
        {
            if (_connections.Values.Count(c => c.UserId == userId) >= maxPerUser) return false;
            _connections[connectionId] = new Connection(userId, expiresAt, abort);
            return true;
        }
    }

    public void SetSite(string connectionId, int sdkSiteId)
    {
        lock (_gate)
            if (_connections.TryGetValue(connectionId, out var c)) c.SdkSiteId = sdkSiteId;
    }

    public void Disconnected(string connectionId)
    {
        lock (_gate)
            _connections.Remove(connectionId);
    }

    /// <summary>The site the connection was opened for, or null when it is not registered.</summary>
    public int? SiteOf(string connectionId)
    {
        lock (_gate)
            return _connections.TryGetValue(connectionId, out var c) ? c.SdkSiteId : null;
    }

    /// <summary>How many live connections the user has.</summary>
    public int ConnectionsOf(int userId)
    {
        lock (_gate)
            return _connections.Values.Count(c => c.UserId == userId);
    }

    /// <summary>Records a joined group. Ignored for a connection that is not (or no longer) registered.</summary>
    public void Joined(string connectionId, string group)
    {
        lock (_gate)
            if (_connections.TryGetValue(connectionId, out var c)) c.Groups.Add(group);
    }

    public void Left(string connectionId, string group)
    {
        lock (_gate)
            if (_connections.TryGetValue(connectionId, out var c)) c.Groups.Remove(group);
    }

    public bool IsIn(string connectionId, string group)
    {
        lock (_gate)
            return _connections.TryGetValue(connectionId, out var c) && c.Groups.Contains(group);
    }

    /// <summary>How many groups of the connection start with <paramref name="prefix"/>.</summary>
    public int CountGroups(string connectionId, string prefix)
    {
        lock (_gate)
            return _connections.TryGetValue(connectionId, out var c)
                ? c.Groups.Count(g => g.StartsWith(prefix, StringComparison.Ordinal))
                : 0;
    }

    /// <summary>True when at least one live connection joined the group.</summary>
    public bool HasMembers(string group)
    {
        lock (_gate)
            return _connections.Values.Any(c => c.Groups.Contains(group));
    }

    /// <summary>
    /// How many times the site's access to the property was revoked. A join
    /// reads it before its access check and compares after adding the
    /// connection to the group: a different value means a revoke ran in
    /// between, so the join undoes its add.
    /// </summary>
    public long RevocationCount(int sdkSiteId, int propertyId)
    {
        lock (_gate)
            return _revocations.GetValueOrDefault((sdkSiteId, propertyId));
    }

    /// <summary>
    /// Marks the site's access to the property as revoked and takes, atomically,
    /// every connection of the site that joined <paramref name="group"/>; the
    /// caller removes them from SignalR's group.
    /// </summary>
    public IReadOnlyList<string> Revoke(int sdkSiteId, int propertyId, string group)
    {
        var taken = new List<string>();
        lock (_gate)
        {
            _revocations[(sdkSiteId, propertyId)] = _revocations.GetValueOrDefault((sdkSiteId, propertyId)) + 1;
            foreach (var (id, c) in _connections)
                if (c.SdkSiteId == sdkSiteId && c.Groups.Remove(group))
                    taken.Add(id);
        }
        return taken;
    }

    /// <summary>Removes and returns every connection of the site (sites &gt; 0 only).</summary>
    public IReadOnlyList<Taken> TakeSite(int sdkSiteId) =>
        sdkSiteId > 0 ? TakeWhere(c => c.SdkSiteId == sdkSiteId) : [];

    /// <summary>Removes and returns every connection whose token expired (expiry at or before now).</summary>
    public IReadOnlyList<Taken> TakeExpired()
    {
        var now = UtcNow;
        return TakeWhere(c => c.ExpiresAt <= now);
    }

    private List<Taken> TakeWhere(Func<Connection, bool> match)
    {
        var taken = new List<Taken>();
        lock (_gate)
        {
            foreach (var (id, c) in _connections.Where(kv => match(kv.Value)).ToList())
            {
                _connections.Remove(id);
                taken.Add(new Taken(id, c.Abort, c.Groups.ToList()));
            }
        }
        return taken;
    }
}
