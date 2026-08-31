#nullable enable
namespace BackendConfiguration.Pn.Services.CalendarChangeNotification;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.PushNotificationService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Turns a <see cref="CalendarChangeBatch"/> into silent FCM wake-ups for the
/// affected workers' flutter-eform apps.
///
/// The payload is the contract shared with the client (flutter-eform PR #905):
/// <c>{"type":"calendar_changed","event_id":"&lt;AreaRulePlanning id&gt;"}</c>,
/// data-only. Empty title and body are what make
/// <c>PushNotificationService</c> omit the Notification block and set
/// APNs content-available: there is no sensible user-facing text for "an event
/// you never saw was taken off you", and a visible banner for it would be
/// wrong, not merely noisy.
/// </summary>
public class CalendarChangeNotifier(
    IServiceScopeFactory scopeFactory,
    ILogger<CalendarChangeNotifier> logger) : ICalendarChangeNotifier
{
    public const string CalendarChangedType = "calendar_changed";
    internal const string TypeKey = "type";
    internal const string EventIdKey = "event_id";

    /// <summary>
    /// Distinct (worker, event) pairs one admin operation may push
    /// individually. Beyond it the operation collapses to one coarse push per
    /// distinct worker.
    ///
    /// 100 is chosen against the shape of the callers, not as a round number.
    /// A single-event reassignment - the overwhelmingly common case, from
    /// either UpdateTask scope - produces at most one pair per assignee of that
    /// one event, so it is nowhere near this. Only
    /// <c>ReconcileEventsForWorkerTagsAsync</c> can approach it, and it can
    /// exceed it without limit: it walks every AreaRulePlanning referencing a
    /// changed worker tag, so a tag on 60 events with 5 members each is 300
    /// pairs from one click. 100 keeps the per-event signal for every realistic
    /// multi-event tag change (say 10 events x 8 workers) while capping the
    /// worst case, and the collapse costs the client nothing: it refreshes its
    /// whole calendar window on this push and does not narrow by event_id, so
    /// the coarse form is equally correct, only less diagnostic.
    /// </summary>
    internal const int MaxDistinctPairsPerOperation = 100;

    /// <summary>
    /// The background dispatch started by the last
    /// <see cref="NotifyInBackground"/> call, or null when that call had
    /// nothing to send.
    /// </summary>
    /// <remarks>
    /// Exposed for tests, which otherwise could only assert against a race.
    /// Safe to keep on the instance because this service is transient - one
    /// instance serves one operation on one request thread. Production code
    /// must not read it: waiting on the dispatch is exactly what
    /// <see cref="NotifyInBackground"/> exists to avoid.
    /// </remarks>
    internal Task? LastDispatch { get; private set; }

    public void NotifyInBackground(CalendarChangeBatch batch)
    {
        if (batch == null || batch.Count == 0)
        {
            LastDispatch = null;
            return;
        }

        // Snapshot before leaving the caller's thread: the batch belongs to the
        // request and is not thread-safe.
        var pairs = batch.Pairs.ToList();

        // Off the request thread on purpose. Every send is a round trip to
        // Google, and above a handful of workers awaiting them inline turns an
        // admin's save into a visible stall - on the worker-tag path, one that
        // grows with how many events carry the tag. The edit is already
        // committed by the time this runs, and the client reconciles on its own
        // timer regardless, so a dispatch lost to a host recycle costs nothing.
        LastDispatch = Task.Run(() => DispatchAsync(pairs));
    }

    /// <summary>
    /// Sends the operation's pushes in a DI scope of its own.
    /// </summary>
    /// <remarks>
    /// The scope is MANDATORY, not hygiene.
    /// <c>BackendConfigurationPnDbContext</c> is registered with
    /// AddDbContextPool, so the request's instance is reset and returned to the
    /// pool when the request scope disposes; a sender resolved from that scope
    /// and used afterwards throws ObjectDisposedException, and used ALONGSIDE
    /// the still-running request throws "a second operation was started on this
    /// context instance". On top of that the sender's token prune calls
    /// PnBase.Delete, which saves everything pending on its context - so
    /// sharing the request's context would let a push commit a half-built unit
    /// of work.
    ///
    /// Guarded on its own because creating the scope and resolving the sender
    /// happen OUTSIDE <see cref="IPushNotificationService.SendToSiteAsync"/>'s
    /// own never-throws boundary, and because nothing awaits this task - an
    /// escaping fault would be an unobserved exception rather than a failed
    /// calendar edit, which is worse, not better.
    /// </remarks>
    internal async Task DispatchAsync(IReadOnlyCollection<CalendarChangePair> pairs)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var push = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
            await SendAsync(push, pairs).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to dispatch calendar-change push notifications for {PairCount} "
                + "(worker, event) pairs", pairs.Count);
        }
    }

    internal async Task SendAsync(
        IPushNotificationService push, IReadOnlyCollection<CalendarChangePair> pairs)
    {
        if (pairs.Count > MaxDistinctPairsPerOperation)
        {
            var siteIds = pairs.Select(p => p.SiteId).Distinct().ToList();
            logger.LogInformation(
                "Calendar-change push: {PairCount} (worker, event) pairs exceeds the "
                + "per-operation cap of {Cap}; collapsing to one event-less push for each "
                + "of {SiteCount} workers",
                pairs.Count, MaxDistinctPairsPerOperation, siteIds.Count);

            foreach (var siteId in siteIds)
            {
                await push.SendToSiteAsync(siteId, string.Empty, string.Empty,
                    new Dictionary<string, string> { [TypeKey] = CalendarChangedType })
                    .ConfigureAwait(false);
            }

            return;
        }

        foreach (var (siteId, eventId) in pairs)
        {
            await push.SendToSiteAsync(siteId, string.Empty, string.Empty,
                new Dictionary<string, string>
                {
                    [TypeKey] = CalendarChangedType,
                    [EventIdKey] = eventId.ToString(CultureInfo.InvariantCulture)
                })
                .ConfigureAwait(false);
        }
    }
}
