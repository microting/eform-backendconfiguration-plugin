#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Calendar;
using BackendConfiguration.Pn.Services.BackendConfigurationCalendarService;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.LiveChanges;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SdkConstants = Microting.eForm.Infrastructure.Constants.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.EformBackendConfigurationBase.Infrastructure.Helpers;

namespace BackendConfiguration.Pn.Services.TaskReminders;

/// <summary>Wire names of task reminders shared with clients (docs/task-reminders-contract.md).</summary>
public static class TaskReminderContract
{
    /// <summary>Listed by <c>GetServerInfo</c>: the server plans task reminders for opted-in installs.</summary>
    public const string Capability = "task_notifications";

    /// <summary>The app id whose push tokens receive task reminders.</summary>
    public const string AppId = "eform";
}

/// <summary>
/// What the planner must look at (singleton, in memory): the dirty properties and sites, and the
/// cached "is any eForm install opted in to reminders" flag that gates change capture. Marks are
/// coalesced; a drain takes them out.
/// </summary>
public sealed class TaskReminderSignals
{
    // Dirty property → its place in the queue (lower = earlier). Round-robin: the planner puts a
    // property it could not finish at the back (Requeue).
    private readonly ConcurrentDictionary<int, long> _properties = new();
    // Property → how many times a real change marked it; the planner keeps a half-done round only
    // while this has not moved.
    private readonly ConcurrentDictionary<int, long> _marks = new();
    private readonly ConcurrentDictionary<int, byte> _sites = new();
    private long _order;
    private volatile bool _anyOptedIn;

    /// <summary>
    /// True while at least one eForm install has reminders turned on. Refreshed by the planner's
    /// minute sweep and set at once when an install opts in. While false (and no hub client is
    /// connected) a save captures nothing.
    /// </summary>
    public bool AnyOptedIn
    {
        get => _anyOptedIn;
        set => _anyOptedIn = value;
    }

    /// <summary>A real change of the property: planned soon (keeps its place when already queued), from all workers.</summary>
    public void MarkProperty(int propertyId)
    {
        if (propertyId <= 0) return;
        _marks.AddOrUpdate(propertyId, 1, (_, n) => n + 1);
        _properties.TryAdd(propertyId, Interlocked.Increment(ref _order));
    }

    /// <summary>The planner's own re-queue (work left, rows in flight, the sweep): no new change; to the back of the queue.</summary>
    internal void Requeue(int propertyId)
    {
        if (propertyId > 0) _properties[propertyId] = Interlocked.Increment(ref _order);
    }

    /// <summary>How many real changes have marked the property so far.</summary>
    internal long MarkSequence(int propertyId) => _marks.GetValueOrDefault(propertyId);

    /// <summary>A worker's assignments or opt-in changed: plan every property the worker is (or was) on.</summary>
    public void MarkSite(int sdkSiteId)
    {
        if (sdkSiteId > 0) _sites.TryAdd(sdkSiteId, 0);
    }

    public bool IsDirty(int propertyId) => _properties.ContainsKey(propertyId);

    public int DirtyCount => _properties.Count;

    internal IReadOnlyList<int> DrainProperties(int max)
    {
        var taken = new List<int>();
        foreach (var id in _properties.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList())
        {
            if (taken.Count >= max) break;
            if (_properties.TryRemove(id, out _)) taken.Add(id);
        }

        return taken;
    }

    internal IReadOnlyList<int> DrainSites()
    {
        var taken = new List<int>();
        foreach (var id in _sites.Keys.OrderBy(k => k))
        {
            if (_sites.TryRemove(id, out _)) taken.Add(id);
        }

        return taken;
    }
}

/// <summary>Tuning of <see cref="TaskReminderPlanner"/>; the defaults are the production values.</summary>
public sealed class TaskReminderOptions
{
    public TimeSpan PassInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The sweep re-plans a property whose last plan is this old (the safety net for lost marks).</summary>
    public TimeSpan ReplanAfter { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long before the planned start the reminder is due (product decision: 10 minutes).</summary>
    public TimeSpan Lead { get; set; } = TimeSpan.FromMinutes(10);

    public int MaxPropertiesPerPass { get; set; } = 25;
    public TimeSpan PassBudget { get; set; } = TimeSpan.FromSeconds(1.5);
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Property-local times of tasks, as UTC instants.</summary>
public static class TaskReminderTime
{
    /// <summary>
    /// The UTC instant of <paramref name="startHour"/> (hours after midnight) on <paramref name="day"/>
    /// in <paramref name="zone"/>. DST-safe: a time skipped by the spring change is moved on by the
    /// gap (02:30 → 03:30); a time that happens twice in autumn is the first of the two.
    /// </summary>
    public static DateTime ToUtc(DateOnly day, double startHour, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue).AddMinutes(Math.Round(startHour * 60));
        if (zone.IsInvalidTime(local))
        {
            var rule = zone.GetAdjustmentRules().FirstOrDefault(r => r.DateStart <= local && local <= r.DateEnd);
            local += rule?.DaylightDelta is { } delta && delta > TimeSpan.Zero ? delta : TimeSpan.FromHours(1);
        }

        if (zone.IsAmbiguousTime(local))
        {
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
    }

    /// <summary>The property's calendar date at <paramref name="nowUtc"/>.</summary>
    public static DateOnly LocalToday(DateTime nowUtc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone));
}

/// <summary>
/// The reminder text, in ONE place: title = the task's name; body = the local start time and the
/// property's name ("Starts at 07:00 · Farm"), in the worker's language from the plugin's
/// localisation resources (English fallback). Cut to the outbox columns on a character boundary.
/// </summary>
public static class TaskReminderText
{
    public const int TitleMax = 200;
    public const int BodyMax = 400;
    internal const string StartsAtKey = "TaskReminderStartsAt";
    internal const string UntitledKey = "TaskReminderUntitled";
    private const string Fallback = "en-US";

    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Texts = new(Load);

    public static (string Title, string Body) Render(string? languageCode, string? taskTitle, TimeOnly localStart,
        string? propertyName)
    {
        var title = string.IsNullOrWhiteSpace(taskTitle) ? Lookup(UntitledKey, languageCode) : taskTitle.Trim();
        var startsAt = string.Format(CultureInfo.InvariantCulture, Lookup(StartsAtKey, languageCode),
            localStart.ToString("HH:mm", CultureInfo.InvariantCulture));
        var body = string.IsNullOrWhiteSpace(propertyName) ? startsAt : $"{startsAt} · {propertyName.Trim()}";
        return (Fit(title, TitleMax), Fit(body, BodyMax));
    }

    /// <summary>At most <paramref name="max"/> UTF-16 units, never cutting a character (surrogate pair, combining mark) in half.</summary>
    public static string Fit(string value, int max)
    {
        if (value.Length <= max) return value;
        var builder = new StringBuilder(max);
        var elements = StringInfo.GetTextElementEnumerator(value);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (builder.Length + element.Length > max) break;
            builder.Append(element);
        }

        return builder.ToString();
    }

    private static string Lookup(string key, string? languageCode)
    {
        var values = Texts.Value[key];
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            if (values.TryGetValue(languageCode, out var exact)) return exact;
            var language = languageCode.Split('-')[0];
            var match = values.FirstOrDefault(kv => string.Equals(kv.Key.Split('-')[0], language, StringComparison.OrdinalIgnoreCase));
            if (match.Value != null) return match.Value;
        }

        return values[Fallback];
    }

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var assembly = typeof(TaskReminderText).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("localization.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, Dictionary<string, string>>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var key = entry.GetProperty("Key").GetString();
            if (key is not (StartsAtKey or UntitledKey)) continue;
            result[key] = entry.GetProperty("LocalizedValue").EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }
}

/// <summary>One of today's open, timed tasks of a worker, as the planner sees it.</summary>
public sealed record ReminderCandidate(int EventId, int? ComplianceId, DateTime PlannedStartUtc, string Title,
    TimeOnly LocalStart);

/// <summary>
/// Plans task reminders (package 12) into the base package's outbox (<see cref="TaskNotificationOutbox"/>);
/// the background service delivers them.
/// <list type="bullet">
///   <item><b>Inputs:</b> <see cref="TaskReminderSignals"/>, fed by the live-changes publisher (web-host
///     writes), the feed reader (background-service writes) and push-token registrations; plus a sweep
///     every minute that re-plans a property on a new local day or 15 minutes after its last plan, only
///     for properties with an opted-in install.</item>
///   <item><b>A property's plan:</b> for each worker on it with an opted-in eForm install, the worker's
///     tasks of the property's local today exactly as the app's list gets them
///     (<see cref="EventsGrpcService.WorkerTaskListRequest"/> into <c>GetTasksForWeek</c>), inside the
///     read-path suppression and without the eager deploy (the planner writes no task rows). Wanted:
///     open tasks with a start time that start later than now; due 10 minutes before (at once at short
///     notice). Diffed against the day's outbox rows: missing ones planned, unwanted ones cancelled or
///     withdrawn, renamed ones re-rendered. A row the helper cannot settle keeps the property dirty.</item>
///   <item><b>Bounded:</b> at most <see cref="TaskReminderOptions.MaxPropertiesPerPass"/> properties and
///     <see cref="TaskReminderOptions.PassBudget"/> per pass; the rest stays dirty for the next pass.</item>
///   <item><b>Host safety:</b> nothing escapes the loop, one DI scope per property plan, back-off on
///     failure, prompt stop. Logs carry ids and counts only.</item>
/// </list>
/// </summary>
public sealed class TaskReminderPlanner(
    IServiceScopeFactory scopeFactory,
    TaskReminderSignals signals,
    TaskReminderOptions options,
    TimeProvider timeProvider,
    ILogger<TaskReminderPlanner> logger) : BackgroundService
{
    private static readonly TimeSpan WarningRepeat = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<int, (DateTime At, string DayKey)> _lastPlanned = new();
    // Workers of a property a pass did not reach (budget spent), done by the next pass, valid only
    // while no real change marked the property since and its local day is the same.
    private readonly ConcurrentDictionary<int, (HashSet<int> Sites, long MarkSequence, string DayKey)> _remaining = new();
    private static readonly TimeSpan PropertyWarningRepeat = TimeSpan.FromHours(1);
    // The last worker read per property: a restarted round begins after it and wraps around, so a
    // property marked again before every pass still reaches every worker within one rotation.
    private readonly ConcurrentDictionary<int, int> _lastSite = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _propertyWarnings = new();
    private DateTimeOffset? _nextSweep;
    private int _failures;
    private DateTimeOffset _lastWarning = DateTimeOffset.MinValue;

    /// <summary>How a plan gets its context from its scope. A seam for tests.</summary>
    internal Func<IServiceProvider, BackendConfigurationPnDbContext> ContextFor { get; set; } =
        sp => sp.GetRequiredService<BackendConfigurationPnDbContext>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // never hold up the host's start
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await TickAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>The sweep when due, then one pass. Never throws; returns the delay before the next tick.</summary>
    internal async Task<TimeSpan> TickAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = timeProvider.GetUtcNow();
            if (_nextSweep is null || now >= _nextSweep)
            {
                // Scheduled only once it succeeds: while the database is down every tick fails here
                // and backs off.
                await SweepAsync(cancellationToken).ConfigureAwait(false);
                _nextSweep = now + options.SweepInterval;
            }

            await RunPassAsync(cancellationToken).ConfigureAwait(false);
            if (_failures > 0)
            {
                logger.LogInformation("Task reminder planner recovered after {Failures} failed tick(s)", _failures);
                _failures = 0;
            }

            return options.PassInterval;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return options.PassInterval;
        }
        catch (Exception e)
        {
            _failures++;
            var now = timeProvider.GetUtcNow();
            if (_failures == 1 || now - _lastWarning >= WarningRepeat)
            {
                _lastWarning = now;
                logger.LogWarning(e, "Task reminder planner: tick failed ({Failures} in a row); backing off", _failures);
            }
            else
            {
                logger.LogDebug(e, "Task reminder planner: tick failed again ({Failures} in a row)", _failures);
            }

            return TimeSpan.FromSeconds(Math.Min(options.MaxBackoff.TotalSeconds,
                options.PassInterval.TotalSeconds * Math.Pow(2, Math.Min(_failures, 16))));
        }
    }

    /// <summary>
    /// The minute sweep: refreshes the opt-in flag (one query; nothing else when no install is
    /// opted in) and marks the opted-in workers' properties whose plan is missing, of another
    /// local day, or older than <see cref="TaskReminderOptions.ReplanAfter"/>. Throws on failure.
    /// </summary>
    internal async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = ContextFor(scope.ServiceProvider);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var sites = await OptedInTokens(db).Select(t => t.SdkSiteId).Distinct().ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        signals.AnyOptedIn = sites.Count > 0;
        if (sites.Count == 0)
        {
            _lastPlanned.Clear();
            return;
        }

        var propertyIds = db.PropertyWorkers
            .Where(pw => sites.Contains(pw.WorkerId) && pw.WorkflowState != SdkConstants.WorkflowStates.Removed)
            .Select(pw => pw.PropertyId);
        var properties = await db.Properties.AsNoTracking()
            .Where(p => propertyIds.Contains(p.Id) && p.WorkflowState != SdkConstants.WorkflowStates.Removed)
            .Select(p => new { p.Id, p.TimeZone })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var marked = 0;
        foreach (var property in properties)
        {
            var dayKey = DayKeyOf(TaskReminderTime.LocalToday(now, PropertyTimeZones.Resolve(property.TimeZone)));
            if (_lastPlanned.TryGetValue(property.Id, out var last) && last.DayKey == dayKey
                && now - last.At < options.ReplanAfter)
            {
                continue;
            }

            signals.Requeue(property.Id); // a safety net, not a change: an unfinished round continues
            marked++;
        }

        if (marked > 0) logger.LogDebug("Task reminder sweep: {Count} propert(y/ies) to re-plan", marked);
    }

    /// <summary>
    /// One bounded pass over the dirty properties (dirty sites are first turned into the properties
    /// the worker is or was linked to). The budget is checked before every worker's calendar read, so
    /// a large property is spread over several passes (<see cref="PlanPropertyAsync"/> keeps its
    /// progress). A property whose plan fails is NOT re-queued: the minute sweep retries it, and it
    /// warns at most once an hour. Returns the number of properties worked on. Throws only when the
    /// dirty sites cannot be resolved.
    /// </summary>
    internal async Task<int> RunPassAsync(CancellationToken cancellationToken)
    {
        var sites = signals.DrainSites();
        if (sites.Count > 0)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = ContextFor(scope.ServiceProvider);
                // Removed links too: a worker taken off a property must lose its reminders there.
                foreach (var propertyId in await db.PropertyWorkers.AsNoTracking()
                             .Where(pw => sites.Contains(pw.WorkerId)).Select(pw => pw.PropertyId).Distinct()
                             .ToListAsync(cancellationToken).ConfigureAwait(false))
                {
                    signals.MarkProperty(propertyId);
                }
            }
            catch
            {
                foreach (var site in sites) signals.MarkSite(site);
                throw;
            }
        }

        var properties = signals.DrainProperties(options.MaxPropertiesPerPass);
        var budget = Stopwatch.StartNew();
        var worked = 0;
        var unfinished = new List<int>();
        for (var i = 0; i < properties.Count; i++)
        {
            if (worked > 0 && budget.Elapsed > options.PassBudget)
            {
                RequeueFrom(i);
                break;
            }

            worked++;
            try
            {
                await PlanPropertyAsync(properties[i], cancellationToken, budget).ConfigureAwait(false);
                if (_remaining.ContainsKey(properties[i])) unfinished.Add(properties[i]);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                RequeueFrom(i);
                throw;
            }
            catch (Exception e)
            {
                // Per property: one that keeps failing must not starve the ones after it.
                Failed(properties[i], e);
            }
        }

        // Round-robin: a property with workers left goes behind the ones this pass did not reach.
        foreach (var propertyId in unfinished) signals.Requeue(propertyId);
        return worked;

        void RequeueFrom(int from)
        {
            for (var j = from; j < properties.Count; j++) signals.Requeue(properties[j]);
        }
    }

    /// <summary>
    /// Plans one property's reminders for its local today, worker by worker: each worker's own task
    /// list is read (the app's list call) and diffed against that worker's rows only, so a property
    /// half done when the budget runs out never touches the rows of a worker it has not read yet.
    /// Rows of workers no longer opted in or assigned are retracted. One worker's failure does not
    /// stop the others. Returns true when settled; false when workers are left for the next pass
    /// (<paramref name="budget"/> spent) or the outbox reported rows in flight. A failed worker
    /// leaves the property to the minute sweep (warned once an hour). Throws on a database failure.
    /// </summary>
    internal async Task<bool> PlanPropertyAsync(int propertyId, CancellationToken cancellationToken,
        Stopwatch? budget = null)
    {
        var markSequence = signals.MarkSequence(propertyId); // before reading: a later mark restarts the round
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = ContextFor(services);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var property = await db.Properties.AsNoTracking()
            .Where(p => p.Id == propertyId)
            .Select(p => new { p.Name, p.TimeZone, p.WorkflowState })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var zone = PropertyTimeZones.Resolve(property?.TimeZone);
        var day = TaskReminderTime.LocalToday(now, zone);
        var dayKey = DayKeyOf(day);

        var sites = new List<int>();
        if (property != null && property.WorkflowState != SdkConstants.WorkflowStates.Removed)
        {
            var assigned = db.PropertyWorkers
                .Where(pw => pw.PropertyId == propertyId && pw.WorkflowState != SdkConstants.WorkflowStates.Removed)
                .Select(pw => pw.WorkerId);
            sites = await OptedInTokens(db).Where(t => assigned.Contains(t.SdkSiteId))
                .Select(t => t.SdkSiteId).Distinct().OrderBy(s => s).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        var existing = (await TaskNotificationOutbox.ListForPropertyDayAsync(db, propertyId, dayKey, cancellationToken)
                .ConfigureAwait(false))
            .Where(n => n.Kind == TaskNotificationKinds.TaskDue)
            .ToLookup(n => n.SdkSiteId);

        var retry = false;
        // Workers whose rows remain although they are no longer opted in or assigned: retracted, no read needed.
        foreach (var gone in existing.Select(g => g.Key).Where(s => !sites.Contains(s)))
        {
            retry |= await DiffWorkerAsync(db, gone, [], existing[gone], now, cancellationToken).ConfigureAwait(false);
        }

        // This round's workers: the ones a previous pass did not reach, if nothing has changed since
        // (no new mark, same local day); otherwise all of them again.
        if (sites.Count == 0) _lastSite.TryRemove(propertyId, out _);
        var rotation = _lastSite.TryGetValue(propertyId, out var lastSite)
            ? sites.Where(s => s > lastSite).Concat(sites.Where(s => s <= lastSite)).ToList()
            : sites;
        var round = _remaining.TryRemove(propertyId, out var left) && left.MarkSequence == markSequence
                                                                     && left.DayKey == dayKey
            ? rotation.Where(left.Sites.Contains).ToList()
            : rotation;
        var failed = false;
        if (round.Count > 0)
        {
            var languages = await SiteLanguagesAsync(services, round, cancellationToken).ConfigureAwait(false);
            var calendar = services.GetRequiredService<IBackendConfigurationCalendarService>();
            // The planner only reads: nothing it might write is announced, and it never deploys.
            using var readPath = services.GetService<ILiveChanges>()?.BeginReadPath();
            for (var i = 0; i < round.Count; i++)
            {
                if (i > 0 && budget != null && budget.Elapsed > options.PassBudget)
                {
                    _remaining[propertyId] = (round.Skip(i).ToHashSet(), markSequence, dayKey);
                    break;
                }

                var site = round[i];
                _lastSite[propertyId] = site;
                try
                {
                    var (languageId, languageCode) = languages[site];
                    var result = await calendar.GetTasksForWeek(EventsGrpcService.WorkerTaskListRequest(
                        propertyId, dayKey, dayKey, site, languageId)).ConfigureAwait(false);
                    if (!result.Success || result.Model == null)
                    {
                        failed = true; // this worker's rows are left as they are
                        continue;
                    }

                    var present = Candidates(result.Model, day, zone).Select(task =>
                    {
                        var (title, body) = TaskReminderText.Render(languageCode, task.Title, task.LocalStart, property!.Name);
                        return new TaskNotificationPlan(site, propertyId, task.EventId, dayKey, task.ComplianceId,
                            TaskNotificationKinds.TaskDue, task.PlannedStartUtc, task.PlannedStartUtc - options.Lead, title, body);
                    }).ToList();
                    retry |= await DiffWorkerAsync(db, site, present, existing[site], now, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    failed = true;
                    logger.LogDebug(e, "Task reminder planner: site {SdkSiteId} on property {PropertyId} failed", site, propertyId);
                }
            }
        }

        var unfinished = _remaining.ContainsKey(propertyId);
        if (failed)
        {
            // Not retried every pass: the minute sweep re-plans a property with no recent plan.
            _lastPlanned.TryRemove(propertyId, out _);
            WarnOncePerHour(propertyId, null);
        }
        else if (!unfinished)
        {
            _lastPlanned[propertyId] = (now, dayKey);
        }

        // Workers left for the next pass, or rows in flight (the outbox's contract): plan again soon.
        if (unfinished || retry) signals.Requeue(propertyId);
        return !unfinished && !retry;
    }

    /// <summary>
    /// One worker's diff against that worker's rows of the day: wanted (present and starting later
    /// than now) keys are planned (or revived), Pending rows with new text re-rendered, rows of keys
    /// no longer present retracted. A present task that has started keeps what was sent for it.
    /// Returns true when the outbox reported rows in flight (plan again soon).
    /// </summary>
    private async Task<bool> DiffWorkerAsync(BackendConfigurationPnDbContext db, int site,
        IReadOnlyList<TaskNotificationPlan> present, IEnumerable<TaskNotification> rows, DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = rows.GroupBy(n => (n.AreaRulePlanningId, n.PlannedStartUtc))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.Id).First());
        var presentKeys = present.Select(p => (p.AreaRulePlanningId, p.PlannedStartUtc)).ToHashSet();
        var toPlan = new List<TaskNotificationPlan>();
        var texts = new List<(long Id, string Title, string Body)>();
        foreach (var plan in present.Where(p => p.PlannedStartUtc > now))
        {
            if (!existing.TryGetValue((plan.AreaRulePlanningId, plan.PlannedStartUtc), out var row)
                || row.State is TaskNotificationState.Cancelled or TaskNotificationState.Withdrawn
                    or TaskNotificationState.WithdrawPending)
            {
                toPlan.Add(plan);
            }
            else if (row.State == TaskNotificationState.Pending && (row.Title != plan.Title || row.Body != plan.Body))
            {
                texts.Add((row.Id, plan.Title, plan.Body));
            }
        }

        var unwanted = existing.Where(e => !presentKeys.Contains(e.Key)
                                           && e.Value.State is TaskNotificationState.Pending or TaskNotificationState.Sending
                                               or TaskNotificationState.Sent)
            .Select(e => e.Value.Id).ToList();

        var retry = false;
        int planned = 0, retracted = 0;
        if (toPlan.Count > 0)
        {
            var result = await TaskNotificationOutbox.PlanAsync(db, toPlan, now, cancellationToken).ConfigureAwait(false);
            retry |= result.RetryIds.Count > 0;
            planned = result.Inserted + result.Revived;
        }

        if (unwanted.Count > 0)
        {
            var result = await TaskNotificationOutbox.CancelOrWithdrawAsync(db, unwanted, now, cancellationToken)
                .ConfigureAwait(false);
            retry |= result.RetryIds.Count > 0;
            retracted = result.Cancelled + result.WithdrawRequested;
        }

        foreach (var (id, title, body) in texts)
        {
            await TaskNotificationOutbox.UpdateTextAsync(db, id, title, body, now, cancellationToken).ConfigureAwait(false);
        }

        if (planned + retracted + texts.Count > 0 || retry)
        {
            logger.LogDebug("Task reminders for site {SdkSiteId}: {Planned} planned, {Retracted} retracted, {Renamed} re-rendered, in flight: {Retry}",
                site, planned, retracted, texts.Count, retry);
        }

        return retry;
    }

    private void Failed(int propertyId, Exception e)
    {
        _lastPlanned.TryRemove(propertyId, out _);
        _remaining.TryRemove(propertyId, out _);
        WarnOncePerHour(propertyId, e);
    }

    /// <summary>At most one Warning per property per hour (ids only), then Debug.</summary>
    private void WarnOncePerHour(int propertyId, Exception? e)
    {
        var now = timeProvider.GetUtcNow();
        if (_propertyWarnings.TryGetValue(propertyId, out var last) && now - last < PropertyWarningRepeat)
        {
            logger.LogDebug(e, "Task reminder planner: property {PropertyId} failed again", propertyId);
            return;
        }

        _propertyWarnings[propertyId] = now;
        logger.LogWarning(e, "Task reminder planner: property {PropertyId} could not be planned; the minute sweep retries it",
            propertyId);
    }

    /// <summary>Open tasks of <paramref name="day"/> with a start time (not all-day), one per (event, start).</summary>
    internal static List<ReminderCandidate> Candidates(IEnumerable<CalendarTaskResponseModel> tasks, DateOnly day,
        TimeZoneInfo zone)
    {
        var key = DayKeyOf(day);
        return tasks
            .Where(t => t.TaskDate == key && !t.IsAllDay && !t.Completed)
            .Select(t =>
            {
                var minutes = (int)Math.Round(t.StartHour * 60) % (24 * 60);
                return new ReminderCandidate(t.Id, t.ComplianceId is > 0 ? t.ComplianceId : null,
                    TaskReminderTime.ToUtc(day, t.StartHour, zone), t.Title ?? string.Empty,
                    TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Math.Max(0, minutes))));
            })
            .GroupBy(c => (c.EventId, c.PlannedStartUtc))
            .Select(g => g.First())
            .OrderBy(c => c.PlannedStartUtc).ThenBy(c => c.EventId)
            .ToList();
    }

    private static string DayKeyOf(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The eForm installs that receive task reminders (as the push sender selects tokens).</summary>
    private static IQueryable<DeviceToken> OptedInTokens(BackendConfigurationPnDbContext db) =>
        db.DeviceTokens.AsNoTracking().Where(t => t.AppId == TaskReminderContract.AppId && t.TaskReminders
                                                  && t.WorkflowState == SdkConstants.WorkflowStates.Created
                                                  && t.FcmToken != null && t.FcmToken != "");

    /// <summary>
    /// Each site's SDK language (id and code), the language the app's task list is in. Never null: a
    /// site without a language (LanguageId 0) gets the code base's default, Danish ("da", as the
    /// plugin uses wherever it needs a language), else the first language. A null language would make
    /// the calendar ask for the current request user, which a background service does not have.
    /// </summary>
    private static async Task<Dictionary<int, (int Id, string? Code)>> SiteLanguagesAsync(IServiceProvider services,
        IReadOnlyCollection<int> sites, CancellationToken cancellationToken)
    {
        var core = await services.GetRequiredService<IEFormCoreService>().GetCore().ConfigureAwait(false);
        await using var sdk = core.DbContextHelper.GetDbContext();
        var siteLanguages = await sdk.Sites.AsNoTracking().Where(s => sites.Contains(s.Id))
            .Select(s => new { s.Id, s.LanguageId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var languages = await sdk.Languages.AsNoTracking().Select(l => new { l.Id, l.LanguageCode })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var fallback = languages.FirstOrDefault(l => l.LanguageCode == DefaultLanguageCode)
                       ?? languages.OrderBy(l => l.Id).First();
        var byId = languages.ToDictionary(l => l.Id);
        return sites.ToDictionary(site => site, site =>
        {
            var languageId = siteLanguages.FirstOrDefault(s => s.Id == site)?.LanguageId ?? 0;
            var language = byId.GetValueOrDefault(languageId) ?? fallback;
            return (language.Id, (string?)language.LanguageCode);
        });
    }

    private const string DefaultLanguageCode = "da";
}

/// <summary>DI wiring of task reminders.</summary>
public static class TaskReminderRegistration
{
    public static IServiceCollection AddBackendConfigurationTaskReminders(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TaskReminderSignals>();
        services.TryAddSingleton(new TaskReminderOptions());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TaskReminderPlanner>());
        return services;
    }
}
