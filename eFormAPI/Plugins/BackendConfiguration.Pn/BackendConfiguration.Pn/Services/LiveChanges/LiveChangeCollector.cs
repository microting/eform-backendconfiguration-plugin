#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using BackendConfiguration.Pn.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.LiveChanges;

/// <summary>
/// The explicit side of live updates, for write paths the SaveChanges
/// interceptor cannot see (SDK tables) and for revocations. Scoped: what is
/// added here is sent together with the scope's intercepted changes after the
/// scope ends (after the response, for a request). Every method is in-memory,
/// cheap and never throws. Call it after the write has been committed.
/// </summary>
public interface ILiveChanges
{
    /// <summary>A task hint for a property; pass what is known of the occurrence key.</summary>
    void TaskChanged(int propertyId, int? eventId = null, DateOnly? planDay = null, long? complianceId = null,
        string kind = TaskChangeKinds.Changed);

    /// <summary><c>AssignmentsChanged</c> to the worker's site group (SDK <c>Sites.Id</c>).</summary>
    void AssignmentsChanged(int sdkSiteId);

    /// <summary>Takes the site's connections out of the property group, at once.</summary>
    void RevokePropertyAccess(int sdkSiteId, int propertyId);

    /// <summary>Ends every connection of the site, at once (SDK <c>Sites.Id</c>).</summary>
    void RevokeSite(int sdkSiteId);

    /// <summary>
    /// Marks the scope as a read path until disposed: rows written meanwhile
    /// (eager deploys behind ListEvents / ListChanges / StreamEventChanges) are
    /// not announced. Installed apps poll every 5 s; announcing those writes
    /// would turn every poll into a hint storm. Revocations still go out.
    /// </summary>
    IDisposable BeginReadPath();
}

/// <summary>
/// One DI scope's live changes (<see cref="ILiveChanges"/>), filled by the
/// interceptor at commit time and by explicit calls, sent once when the scope
/// ends (<see cref="Dispose"/>) or when a <see cref="LiveChangeScope"/> ends.
/// For a request that is after the response: the caller never waits.
/// </summary>
public sealed class LiveChangeCollector(
    IServiceProvider scopeServices,
    LiveChangePublisher publisher,
    LiveChangeWarnings warnings,
    ILogger<LiveChangeCollector> logger) : ILiveChanges, IDisposable
{
    private readonly object _gate = new();
    private List<LiveChange> _queue = [];
    // Per async flow: a parallel flow of the same scope is not a read path.
    private readonly AsyncLocal<int> _readPathDepth = new();
    private volatile bool _closed;

    /// <summary>True while a <see cref="BeginReadPath"/> handle is open on this async flow.</summary>
    public bool IsReadPath => _readPathDepth.Value > 0;

    /// <summary>True once flushed for good; later changes are dispatched on their own.</summary>
    public bool IsClosed => _closed;

    public void TaskChanged(int propertyId, int? eventId = null, DateOnly? planDay = null, long? complianceId = null,
        string kind = TaskChangeKinds.Changed) =>
        AddExplicit(LiveChange.ForTask(propertyId, eventId, planDay, complianceId, kind));

    public void AssignmentsChanged(int sdkSiteId) => AddExplicit(LiveChange.ForAssignments(sdkSiteId));

    public void RevokePropertyAccess(int sdkSiteId, int propertyId) =>
        Accept([LiveChange.ForRevokeProperty(sdkSiteId, propertyId)]);

    public void RevokeSite(int sdkSiteId) => Accept([LiveChange.ForRevokeSite(sdkSiteId)]);

    public IDisposable BeginReadPath()
    {
        _readPathDepth.Value++;
        return new ReadPathHandle(this);
    }

    private void AddExplicit(LiveChange change)
    {
        if (IsReadPath) return;
        Accept([change]);
    }

    /// <summary>Takes committed changes: revocations run now, the rest waits for the flush.</summary>
    internal void Accept(IReadOnlyList<LiveChange> changes)
    {
        if (!publisher.HasAudience) return;
        try
        {
            lock (_gate)
            {
                if (_closed)
                {
                    publisher.Submit(changes);
                    return;
                }
            }

            var rest = publisher.ApplyRevocations(changes);
            if (rest.Count == 0) return;
            lock (_gate)
            {
                if (!_closed)
                {
                    _queue.AddRange(rest);
                    return;
                }
            }

            publisher.Dispatch(rest); // closed meanwhile
        }
        catch (Exception e)
        {
            warnings.Report(logger, "collect", e);
        }
    }

    /// <summary>Sends what was collected so far, in the background. Never throws.</summary>
    public void Flush()
    {
        try
        {
            List<LiveChange> batch;
            lock (_gate)
            {
                if (_queue.Count == 0) return;
                batch = _queue;
                _queue = [];
            }

            publisher.Dispatch(batch);
        }
        catch (Exception e)
        {
            warnings.Report(logger, "flush", e);
        }
    }

    /// <summary>The scope ends: flush, and send anything added later on its own.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;
        }

        Flush();
    }

    /// <summary>
    /// True when <paramref name="context"/> is this scope's own plugin or
    /// items-planning context. A Task.Run started from a request inherits the
    /// request's HttpContext but saves through a context of its own scope; such
    /// a save is not this collector's.
    /// </summary>
    internal bool Owns(DbContext context)
    {
        try
        {
            return context switch
            {
                BackendConfigurationPnDbContext => ReferenceEquals(
                    scopeServices.GetService<BackendConfigurationPnDbContext>(), context),
                ItemsPlanningPnDbContext => ReferenceEquals(
                    scopeServices.GetService<ItemsPlanningPnDbContext>(), context),
                _ => false
            };
        }
        catch (ObjectDisposedException)
        {
            return false; // the scope has ended under a background save
        }
    }

    private sealed class ReadPathHandle(LiveChangeCollector owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner._readPathDepth.Value--;
        }
    }
}
