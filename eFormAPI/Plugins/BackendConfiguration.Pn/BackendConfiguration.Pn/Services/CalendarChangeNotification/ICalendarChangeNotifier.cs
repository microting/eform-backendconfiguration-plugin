#nullable enable
namespace BackendConfiguration.Pn.Services.CalendarChangeNotification;

/// <summary>
/// Tells the flutter-eform apps of the workers in a
/// <see cref="CalendarChangeBatch"/> that their calendar changed, so a
/// backgrounded app re-syncs instead of showing an event it lost (or missing
/// one it gained) until its next refresh.
/// </summary>
public interface ICalendarChangeNotifier
{
    /// <summary>
    /// Schedules the batch's pushes and returns immediately, WITHOUT sending
    /// anything on the caller's thread.
    /// </summary>
    /// <remarks>
    /// Call this only AFTER the operation's own database work has been saved.
    /// The sender's token prune calls <c>PnBase.Delete</c>, which saves
    /// everything pending on the context it is given; the background dispatch
    /// therefore runs in a DI scope of its own so it can never commit a
    /// half-built unit of work belonging to the request.
    ///
    /// Never throws, and never reports failure: a push is a courtesy on top of
    /// the calendar edit that triggered it and must not be able to fail it.
    /// </remarks>
    void NotifyInBackground(CalendarChangeBatch batch);
}
