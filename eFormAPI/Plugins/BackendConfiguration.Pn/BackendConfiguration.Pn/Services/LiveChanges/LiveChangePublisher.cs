#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.LiveNotifier;
using Microsoft.Extensions.Logging;

namespace BackendConfiguration.Pn.Services.LiveChanges;

/// <summary>
/// Turns committed <see cref="LiveChange"/>s into hub hints (singleton).
/// <para>
/// Revocations are in-memory and run at once (<see cref="ApplyRevocations"/>),
/// at commit time, so a connection that lost access stops receiving before
/// anything else is sent. Everything else is published by
/// <see cref="Dispatch"/>, on the thread pool: the caller never waits for the
/// resolution query or the hub.
/// </para>
/// <para>
/// Coalescing, per flush: one <c>TaskChanged</c> per (property, event), the
/// most precise key all of that event's changes agree on; more than
/// <see cref="LiveChangeLimits.PropertyWideThreshold"/> events of one property
/// (or a change without an event) become one <c>kind: property</c> hint. One
/// <c>AssignmentsChanged</c> per site.
/// </para>
/// Never throws.
/// </summary>
public sealed class LiveChangePublisher(
    ILiveNotifier notifier,
    ILiveChangeResolver resolver,
    LiveConnectionRegistry registry,
    LiveChangeWarnings warnings,
    ILogger<LiveChangePublisher> logger)
{
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();

    /// <summary>
    /// False while no hub connection exists (the common state until apps use
    /// the hub): then nothing is captured, looked up or sent. A client that
    /// connects later resyncs on connect, so nothing is lost.
    /// </summary>
    public bool HasAudience => registry.HasConnections;

    /// <summary>
    /// Runs the revocations among <paramref name="changes"/> that need no
    /// lookup (in-memory registry operations; a site's <c>Refused</c> sends
    /// continue in the background) and returns the rest.
    /// </summary>
    public IReadOnlyList<LiveChange> ApplyRevocations(IEnumerable<LiveChange> changes)
    {
        var rest = new List<LiveChange>();
        if (!HasAudience) return rest;
        foreach (var change in changes)
        {
            try
            {
                switch (change)
                {
                    case { Kind: LiveChangeKind.RevokeProperty, SdkSiteId: { } site, PropertyId: { } property }:
                        Track(notifier.RevokePropertyAccessAsync(site, property));
                        continue;
                    case { Kind: LiveChangeKind.RevokeSite, SdkSiteId: { } sdkSite }:
                        Track(notifier.RevokeSiteAsync(sdkSite));
                        continue;
                }
            }
            catch (Exception e)
            {
                warnings.Report(logger, "revoke", e);
                continue;
            }

            rest.Add(change);
        }

        return rest;
    }

    /// <summary>Revokes now and publishes the rest in the background (a change with no collector).</summary>
    public void Submit(IEnumerable<LiveChange> changes) => Dispatch(ApplyRevocations(changes));

    /// <summary>Publishes <paramref name="changes"/> in the background; returns at once.</summary>
    public void Dispatch(IReadOnlyList<LiveChange> changes)
    {
        if (changes.Count == 0) return;
        try
        {
            Track(Task.Run(() => PublishAsync(changes)));
        }
        catch (Exception e)
        {
            warnings.Report(logger, "dispatch", e);
        }
    }

    /// <summary>Completes when every dispatched publish (and revocation send) has finished. For tests and shutdown.</summary>
    public async Task WhenIdleAsync()
    {
        while (!_inFlight.IsEmpty)
        {
            await Task.WhenAll(_inFlight.Keys.ToList()).ConfigureAwait(false);
        }
    }

    private void Track(Task task)
    {
        if (task.IsCompleted) return;
        _inFlight.TryAdd(task, 0);
        _ = task.ContinueWith(t => _inFlight.TryRemove(t, out _), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Resolves, coalesces and sends one flush. Never throws.</summary>
    public async Task PublishAsync(IReadOnlyList<LiveChange> changes)
    {
        if (!HasAudience) return;
        try
        {
            var lookups = await LoadLookupsAsync(changes).ConfigureAwait(false);
            var siteOf = (LiveChange c) => c.SdkSiteId
                                           ?? (c.SiteMicrotingUid is { } uid
                                               && lookups.SdkSiteIdByMicrotingUid.TryGetValue(uid, out var id)
                                               ? id
                                               : null);

            // 1. Revocations that needed a lookup.
            foreach (var site in changes.Where(c => c.Kind == LiveChangeKind.RevokeSite).Select(siteOf)
                         .OfType<int>().Distinct())
            {
                await SendAsync(() => notifier.RevokeSiteAsync(site)).ConfigureAwait(false);
            }

            // 2. Task hints.
            foreach (var hint in Coalesce(Resolve(changes, lookups)))
            {
                await SendAsync(() => notifier.TaskChangedAsync(hint.PropertyId, hint.EventId, hint.PlanDay,
                    hint.ComplianceId, hint.Kind)).ConfigureAwait(false);
            }

            // 3. Assignment hints.
            foreach (var site in changes.Where(c => c.Kind == LiveChangeKind.Assignments).Select(siteOf)
                         .OfType<int>().Where(s => s > 0).Distinct())
            {
                await SendAsync(() => notifier.AssignmentsChangedAsync(site)).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            warnings.Report(logger, "publish", e);
        }
    }

    private async Task SendAsync(Func<Task> send)
    {
        try
        {
            await send().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            warnings.Report(logger, "send", e);
        }
    }

    private async Task<LiveChangeLookups> LoadLookupsAsync(IReadOnlyList<LiveChange> changes)
    {
        var request = LookupRequestFor(changes);
        if (request.IsEmpty) return LiveChangeLookups.Empty;

        try
        {
            return await resolver.LoadAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // What was on the rows is still sent; the rest falls to the clients' safety poll.
            warnings.Report(logger, "resolve", e);
            return LiveChangeLookups.Empty;
        }
    }

    /// <summary>
    /// What the flush must look up, using what its own rows already say:
    /// <list type="bullet">
    ///   <item>a task row (event + planning + property) maps its planning in memory;</item>
    ///   <item>a planning whose property a Compliance row of the flush names is looked up by property
    ///     (the PropertyId index), also for its planning-only rows (PlanningCase(Site), Planning...);</item>
    ///   <item>only the remaining plannings go to the unindexed <c>ItemPlanningId</c> query.</item>
    /// </list>
    /// </summary>
    internal static LiveChangeLookupRequest LookupRequestFor(IReadOnlyList<LiveChange> changes)
    {
        var tasks = changes.Where(c => c.Kind == LiveChangeKind.Task).ToList();
        var eventIds = tasks.Where(c => c is { EventId: not null, PropertyId: null })
            .Select(c => c.EventId!.Value).Distinct().ToList();
        var mapped = tasks.Where(c => c is { EventId: not null, ItemPlanningId: not null, PropertyId: not null })
            .Select(c => c.ItemPlanningId!.Value).ToHashSet();
        var planningRows = tasks.Where(c => c is { EventId: null, ItemPlanningId: not null }
                                            && !mapped.Contains(c.ItemPlanningId!.Value)).ToList();
        var onProperty = planningRows.Where(c => c.PropertyId is not null)
            .Select(c => (PlanningId: c.ItemPlanningId!.Value, PropertyId: c.PropertyId!.Value)).Distinct().ToList();
        var known = onProperty.Select(p => p.PlanningId).ToHashSet();
        var anywhere = planningRows.Where(c => c.PropertyId is null && !known.Contains(c.ItemPlanningId!.Value))
            .Select(c => c.ItemPlanningId!.Value).Distinct().ToList();
        var siteUids = changes.Where(c => c is { SdkSiteId: null, SiteMicrotingUid: not null })
            .Select(c => c.SiteMicrotingUid!.Value).Distinct().ToList();
        return new LiveChangeLookupRequest(eventIds, onProperty, anywhere, siteUids);
    }

    /// <summary>A fully resolved task hint.</summary>
    internal readonly record struct TaskHint(int PropertyId, int? EventId, DateOnly? PlanDay, long? ComplianceId, string Kind);

    /// <summary>Every task change with its property (and event, when it has one) filled in; unresolvable ones are dropped.</summary>
    internal static List<TaskHint> Resolve(IEnumerable<LiveChange> changes, LiveChangeLookups lookups)
    {
        var propertyOfEvent = new Dictionary<int, int>();
        var eventsOfPlanning = new Dictionary<int, List<LiveEventRef>>();
        var list = changes.ToList();
        var inBatch = list.Where(c => c is { Kind: LiveChangeKind.Task, EventId: not null, ItemPlanningId: not null, PropertyId: not null })
            .Select(c => new LiveEventRef(c.EventId!.Value, c.ItemPlanningId!.Value, c.PropertyId!.Value));
        foreach (var e in lookups.Events.Concat(inBatch).Distinct())
        {
            propertyOfEvent.TryAdd(e.EventId, e.PropertyId);
            if (!eventsOfPlanning.TryGetValue(e.ItemPlanningId, out var ofPlanning))
                eventsOfPlanning[e.ItemPlanningId] = ofPlanning = [];
            ofPlanning.Add(e);
        }

        var hints = new List<TaskHint>();
        foreach (var c in list.Where(c => c.Kind == LiveChangeKind.Task))
        {
            if (c.EventId is { } eventId)
            {
                var property = c.PropertyId ?? (propertyOfEvent.TryGetValue(eventId, out var p) ? p : (int?)null);
                if (property is { } pid) hints.Add(new TaskHint(pid, eventId, c.PlanDay, c.ComplianceId, c.TaskKind));
            }
            else if (c.ItemPlanningId is { } planningId)
            {
                var matched = false;
                if (eventsOfPlanning.TryGetValue(planningId, out var events))
                {
                    foreach (var e in events.Where(e => c.PropertyId is null || e.PropertyId == c.PropertyId))
                    {
                        matched = true;
                        hints.Add(new TaskHint(e.PropertyId, e.EventId, c.PlanDay, c.ComplianceId, c.TaskKind));
                    }
                }

                // A planning with no event row (yet) on a known property: the property may have changed.
                if (!matched && c.PropertyId is { } known)
                    hints.Add(new TaskHint(known, null, null, null, TaskChangeKinds.Property));
            }
            else if (c.PropertyId is { } propertyOnly)
            {
                hints.Add(new TaskHint(propertyOnly, null, null, null, TaskChangeKinds.Property));
            }
        }

        return hints;
    }

    /// <summary>The coalescing rules of the class summary.</summary>
    internal static List<TaskHint> Coalesce(IEnumerable<TaskHint> hints)
    {
        var result = new List<TaskHint>();
        foreach (var byProperty in hints.GroupBy(h => h.PropertyId))
        {
            var byEvent = byProperty.Where(h => h.EventId is not null).GroupBy(h => h.EventId!.Value).ToList();
            if (byProperty.Any(h => h.EventId is null) || byEvent.Count > LiveChangeLimits.PropertyWideThreshold)
            {
                result.Add(new TaskHint(byProperty.Key, null, null, null, TaskChangeKinds.Property));
                continue;
            }

            foreach (var group in byEvent)
            {
                result.Add(MergeEvent(byProperty.Key, group.Key, group.ToList()));
            }
        }

        return result;
    }

    private static TaskHint MergeEvent(int propertyId, int eventId, List<TaskHint> changes)
    {
        var distinct = changes.Select(c => (c.PlanDay, c.ComplianceId, c.Kind)).Distinct().ToList();
        if (distinct.Count == 1)
        {
            var only = distinct[0];
            return new TaskHint(propertyId, eventId, only.PlanDay, only.ComplianceId, only.Kind);
        }

        // Several changes of one event: keep the full key only when every change is
        // about the same occurrence; otherwise the event alone. "removed" survives a
        // merge only when the changes were identical (above), because a removed key
        // made less precise would claim more than was removed.
        var days = changes.Where(c => c.PlanDay is not null).Select(c => (c.PlanDay, c.ComplianceId)).Distinct().ToList();
        var compliances = changes.Where(c => c.ComplianceId is not null).Select(c => c.ComplianceId).Distinct().ToList();
        if (days.Count == 1 && compliances.All(id => id == days[0].ComplianceId))
            return new TaskHint(propertyId, eventId, days[0].PlanDay, days[0].ComplianceId, TaskChangeKinds.Changed);
        if (days.Count == 0 && compliances.Count == 1)
            return new TaskHint(propertyId, eventId, null, compliances[0], TaskChangeKinds.Changed);
        return new TaskHint(propertyId, eventId, null, null, TaskChangeKinds.Changed);
    }
}
