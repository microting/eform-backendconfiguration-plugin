#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BackendConfiguration.Pn.Hubs;

/// <summary>
/// The live connections: for each one its user, SDK site, token expiry, a
/// way to abort it, and the groups it joined. This is the only record of group
/// membership (the hub uses no SignalR groups; hints go to the ids listed
/// here), and access is checked only when a group is joined, so everything
/// that takes access away goes through here:
/// <list type="bullet">
/// <item>a removed property assignment (<see cref="Revoke"/>) takes the
/// site's connections out of that property's group;</item>
/// <item>a removed property (<see cref="RevokePropertyForEveryone"/>) takes
/// every connection out of its group;</item>
/// <item>a removed or resigned worker (<see cref="TakeSite"/>), a deactivated
/// login (<see cref="TakeUser"/>) and an expired token
/// (<see cref="TakeExpired"/>) end the connection.</item>
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
    private readonly Dictionary<int, long> _propertyRevocations = new();

    private sealed class Connection(int userId, DateTimeOffset expiresAt, Action abort)
    {
        public int UserId { get; } = userId;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public Action Abort { get; } = abort;
        public int SdkSiteId { get; set; }
        public HashSet<string> Groups { get; } = [];
    }

    /// <summary>A connection taken out of the registry (it receives nothing from now on); the caller ends it.</summary>
    public sealed record Taken(string ConnectionId, Action Abort);

    private volatile int _count;

    /// <summary>
    /// True while at least one connection is registered. A lock-free read for
    /// the write path: with nobody connected the live-change pipeline skips
    /// all work (a client that connects later resyncs on connect).
    /// </summary>
    public bool HasConnections => _count > 0;

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
            _count = _connections.Count;
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
        {
            _connections.Remove(connectionId);
            _count = _connections.Count;
        }
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

    /// <summary>
    /// The connections a hint to <paramref name="group"/> goes to: the ones
    /// this registry lists as members whose token is still valid. Group
    /// membership lives only here (no SignalR groups), so a connection taken
    /// out (expired, revoked) receives nothing. Bounded by the group's member
    /// count.
    /// </summary>
    public IReadOnlyList<string> EligibleMembers(string group)
    {
        var now = UtcNow;
        lock (_gate)
            return _connections.Where(kv => kv.Value.ExpiresAt > now && kv.Value.Groups.Contains(group))
                .Select(kv => kv.Key).ToList();
    }

    /// <summary>
    /// How many times the site's access to the property was revoked. A join
    /// reads it before its access check and compares after adding the
    /// connection to the group: a different value means a revoke ran in
    /// between, so the join undoes its add.
    /// </summary>
    public long RevocationCount(int sdkSiteId, int propertyId)
    {
        // A revoke of the property for everyone counts for every site (and for web users, site 0).
        lock (_gate)
            return _revocations.GetValueOrDefault((sdkSiteId, propertyId))
                   + _propertyRevocations.GetValueOrDefault(propertyId);
    }

    /// <summary>
    /// Marks the site's access to the property as revoked and takes, atomically,
    /// every connection of the site out of <paramref name="group"/>; returns
    /// their ids. The connections stay open (they keep their other groups).
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

    /// <summary>
    /// Marks the property as revoked for everyone (it was removed) and takes,
    /// atomically, every connection out of <paramref name="group"/>: workers
    /// and web users alike (web members have no PropertyWorker row to revoke
    /// by). Returns their ids; the connections stay open.
    /// </summary>
    public IReadOnlyList<string> RevokePropertyForEveryone(int propertyId, string group)
    {
        var taken = new List<string>();
        lock (_gate)
        {
            _propertyRevocations[propertyId] = _propertyRevocations.GetValueOrDefault(propertyId) + 1;
            foreach (var (id, c) in _connections)
                if (c.Groups.Remove(group))
                    taken.Add(id);
        }
        return taken;
    }

    /// <summary>Removes and returns every connection of the user (whatever its site).</summary>
    public IReadOnlyList<Taken> TakeUser(int userId) => TakeWhere(c => c.UserId == userId);

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
                taken.Add(new Taken(id, c.Abort));
            }

            _count = _connections.Count;
        }
        return taken;
    }
}
