#nullable enable
namespace BackendConfiguration.Pn.Services.CalendarChangeNotification;

using System.Collections.Generic;

/// <summary>
/// One worker (SDK site) whose assignment to one calendar event
/// (AreaRulePlanning id) changed. A record struct so the accumulating
/// <see cref="CalendarChangeBatch"/> dedupes by value.
/// </summary>
public readonly record struct CalendarChangePair(int SiteId, int EventId);

/// <summary>
/// Accumulates the workers who gained or lost calendar events over ONE admin
/// operation, so the pushes for that operation can be sent once, at the end,
/// deduped.
///
/// This exists because of the volume shape of the reassignment paths, not for
/// tidiness. <c>CalendarAssignmentReconciliationService.ReconcileEventAsync</c>
/// plans an add/remove delta PER FUTURE OCCURRENCE, so one reassignment of a
/// weekly event with a year of future occurrences yields ~50 identical
/// (site, event) deltas; and
/// <c>ReconcileEventsForWorkerTagsAsync</c> multiplies that by every event
/// referencing the changed tag. Pushing at the point the delta is computed
/// would mean hundreds of FCM sends per admin click. The batch collapses all of
/// that to one entry per (worker, event).
///
/// Deliberately NOT thread-safe: one batch belongs to one operation on one
/// request thread, and is snapshotted before it is handed to a background
/// dispatch.
/// </summary>
public sealed class CalendarChangeBatch
{
    private readonly HashSet<CalendarChangePair> _pairs = [];

    /// <summary>
    /// Records that <paramref name="siteId"/>'s assignment to
    /// <paramref name="eventId"/> changed. Idempotent.
    /// </summary>
    public void Add(int siteId, int eventId) => _pairs.Add(new CalendarChangePair(siteId, eventId));

    public IReadOnlyCollection<CalendarChangePair> Pairs => _pairs;
}
