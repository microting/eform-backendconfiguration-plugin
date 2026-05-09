using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Calendar;

public class CalendarTaskRequestModel
{
    public int PropertyId { get; set; }
    public string WeekStart { get; set; }
    public string WeekEnd { get; set; }
    public List<int> BoardIds { get; set; } = [];
    public List<string> TagNames { get; set; } = [];
    public List<int> SiteIds { get; set; } = [];

    /// <summary>
    /// When true, the calendar emits only *actionable* compliance rows for the requested
    /// week — i.e. compliances whose backing SDK Case still exists, is not soft-deleted,
    /// and is not yet completed (Status != 100). This is intended for the mobile-worker
    /// gRPC path (<c>OpgaverGrpcService</c>) where non-actionable rows have no write
    /// handler to bind to and would just clutter the worker's view.
    ///
    /// Default <c>false</c> preserves the historical behavior used by the angular admin
    /// calendar (<c>CalendarController</c>) and other gRPC consumers
    /// (<c>CalendarGrpcService</c>): all in-week compliances surface, including missed
    /// and completed ones, so the admin can audit the full week.
    /// </summary>
    public bool ActionableOnly { get; set; } = false;
}
