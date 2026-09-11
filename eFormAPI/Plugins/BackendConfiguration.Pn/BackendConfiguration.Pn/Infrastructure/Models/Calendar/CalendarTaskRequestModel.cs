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
    /// Optional worker-tag ("team") filter (#1212). Combines with
    /// <see cref="SiteIds"/> by OR, never AND — but the two are <em>not</em>
    /// symmetric. Before matching, the server widens this list into the
    /// <em>effective</em> tag set: this list plus every worker tag the requested
    /// <see cref="SiteIds"/> are members of. A task then survives the assignee
    /// filter when its explicit assignees intersect <see cref="SiteIds"/>
    /// <em>or</em> its assigned worker tags intersect that effective set.
    ///
    /// The widening runs one way only, sites → tags; there is no tags → sites
    /// expansion anywhere in the filter path. So selecting an individual
    /// <em>does</em> match tasks assigned to a team that individual belongs to,
    /// while selecting a team does <em>not</em> match that team's members' own
    /// individually-assigned tasks. (<c>CalendarTaskResponseModel.TeamAssigneeIds</c>
    /// is populated separately and never read by the filter — team membership
    /// matches only through the effective tag set.) Empty here is therefore not
    /// the same as "no tag matching": with <see cref="SiteIds"/> set, the
    /// effective set is still non-empty. Both lists empty = no assignee
    /// filtering at all.
    ///
    /// Not to be confused with <see cref="TagNames"/>, which is the
    /// planning/eForm tag filter — a different concept entirely.
    /// </summary>
    public List<int> WorkerTagIds { get; set; } = [];

    /// <summary>
    /// When true, the calendar emits only *actionable* compliance rows for the requested
    /// week — i.e. compliances whose backing SDK Case still exists, is not soft-deleted,
    /// and is not yet completed (Status != 100). This is intended for the mobile-worker
    /// gRPC path (<c>EventsGrpcService</c>) where non-actionable rows have no write
    /// handler to bind to and would just clutter the worker's view.
    ///
    /// Default <c>false</c> preserves the historical behavior used by the angular admin
    /// calendar (<c>CalendarController</c>) and other gRPC consumers
    /// (<c>CalendarGrpcService</c>): all in-week compliances surface, including missed
    /// and completed ones, so the admin can audit the full week.
    /// </summary>
    public bool ActionableOnly { get; set; } = false;

    /// <summary>
    /// Optional explicit language for Title/Description selection. Set by the
    /// mobile-worker gRPC path (<c>EventsGrpcService</c>) to the worker's
    /// SDK <c>Site.LanguageId</c> so the worker sees the task in their own
    /// language. Null (angular admin REST / <c>CalendarGrpcService</c>) → the
    /// current user's language, preserving existing behaviour.
    /// </summary>
    public int? LanguageId { get; set; }
}
