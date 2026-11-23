#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.LiveChanges;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Helpers;
using FeedKinds = Microting.EformBackendConfigurationBase.Infrastructure.Const.TaskChangeKinds;

namespace BackendConfiguration.Pn.Services.LiveFeed;

/// <summary>Tuning of <see cref="LiveFeedReader"/>. The defaults are the production values.</summary>
public sealed class LiveFeedOptions
{
    /// <summary>Delay between polls while healthy.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Rows forwarded per poll at most; a reader further behind skips ahead (see <see cref="LiveFeedReader"/>).</summary>
    public int MaxRowsPerTick { get; set; } = 500;

    /// <summary>The late-row window of the base package's reader contract.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Longest delay between polls after repeated failures.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Feed rows older than this are deleted (product-owner approved hard delete, this table only).</summary>
    public TimeSpan RetentionAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How often retention runs; it runs whether or not anyone is connected.</summary>
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Delay of the first retention run after start (start-up is busy enough).</summary>
    public TimeSpan FirstRetentionDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Rows per DELETE statement.</summary>
    public int RetentionBatch { get; set; } = 10000;

    /// <summary>DELETE statements per retention run; the rest waits for the next run.</summary>
    public int RetentionMaxBatches { get; set; } = 10;
}

/// <summary>
/// Forwards the cross-process change feed (<c>TaskChangeFeedEntries</c>, written by the background
/// service) to the hub, following the base package's reader contract
/// (<see cref="TaskChangeFeed"/>): a cursor read, then the late-row window, each row forwarded once.
/// <list type="bullet">
///   <item><b>Nobody connected:</b> no query at all. The cursor is dropped, so the first poll after a
///     client connects jumps to the current last id (that client resyncs on connect): no replay.</item>
///   <item><b>Forwarding:</b> every row of a poll goes to <see cref="LiveChangePublisher"/> as ONE flush,
///     so coalescing and the "who receives" rules stay in one place (more than 5 events of a property
///     become one property-wide hint).</item>
///   <item><b>Bursts:</b> at most <see cref="LiveFeedOptions.MaxRowsPerTick"/> rows per poll. A reader
///     further behind skips the rest in one step: one query for the distinct properties and sites of the
///     skipped range, a property-wide hint per property and AssignmentsChanged per site, cursor to the end.</item>
///   <item><b>Failures:</b> caught in the tick, never out of ExecuteAsync (the host's default
///     BackgroundServiceExceptionBehavior stops the host). Back-off 2, 4, 8, 16, 30 s; one Warning per
///     failure streak (again every 5 minutes), Information on recovery. The cursor survives.</item>
///   <item><b>Retention:</b> rows older than 24 h, in capped batches, hourly, also while idle.</item>
/// </list>
/// Its own DI scope (a pooled, untracked context) per poll; nothing long-lived.
/// Single instance: two web-host replicas would each forward every row (duplicate hints, harmless).
/// </summary>
public sealed class LiveFeedReader(
    IServiceScopeFactory scopeFactory,
    LiveConnectionRegistry registry,
    LiveChangePublisher publisher,
    LiveFeedOptions options,
    TimeProvider timeProvider,
    ILogger<LiveFeedReader> logger) : BackgroundService
{
    private static readonly TimeSpan WarningRepeat = TimeSpan.FromMinutes(5);

    // Ids forwarded recently (id → when), so the window forwards a row at most once.
    private readonly Dictionary<long, DateTimeOffset> _forwarded = new();
    private int _failures;
    private DateTimeOffset _lastWarning = DateTimeOffset.MinValue;
    private DateTimeOffset? _nextRetention;

    /// <summary>How a poll gets its context from its scope. A seam for tests.</summary>
    internal Func<IServiceProvider, BackendConfigurationPnDbContext> ContextFor { get; set; } =
        sp => sp.GetRequiredService<BackendConfigurationPnDbContext>();

    /// <summary>The last forwarded id; null until placed (and again while nobody is connected).</summary>
    internal long? Cursor { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // never hold up the host's start
        logger.LogInformation("Live feed reader started");
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

        logger.LogInformation("Live feed reader stopped");
    }

    /// <summary>One poll (when someone is connected) and, when due, retention. Never throws; returns the delay before the next tick.</summary>
    internal async Task<TimeSpan> TickAsync(CancellationToken cancellationToken)
    {
        await RetentionIfDueAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PollOnceAsync(cancellationToken).ConfigureAwait(false);
            if (_failures > 0)
            {
                logger.LogInformation("Live feed reader recovered after {Failures} failed poll(s)", _failures);
                _failures = 0;
            }

            return options.PollInterval;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return options.PollInterval;
        }
        catch (Exception e)
        {
            _failures++;
            var now = timeProvider.GetUtcNow();
            if (_failures == 1 || now - _lastWarning >= WarningRepeat)
            {
                _lastWarning = now;
                logger.LogWarning(e, "Live feed reader: poll failed ({Failures} in a row); backing off", _failures);
            }
            else
            {
                logger.LogDebug(e, "Live feed reader: poll failed again ({Failures} in a row)", _failures);
            }

            var seconds = Math.Min(options.MaxBackoff.TotalSeconds,
                options.PollInterval.TotalSeconds * Math.Pow(2, Math.Min(_failures, 16)));
            return TimeSpan.FromSeconds(seconds);
        }
    }

    /// <summary>
    /// One poll of the reader contract, all or nothing: the new cursor and the forwarded ids are kept
    /// in a <see cref="Poll"/> and applied together with the publish at the end (<see cref="Commit"/>),
    /// so a poll that fails part-way (e.g. the window query after the cursor read) changes nothing and
    /// the next one retries from the old cursor. Throws on failure (the tick catches). Returns the rows
    /// read. Nothing at all while nobody is connected.
    /// </summary>
    internal async Task<int> PollOnceAsync(CancellationToken cancellationToken)
    {
        if (!registry.HasConnections)
        {
            // Idle: no query. The first poll after a connect starts afresh at the last id.
            Cursor = null;
            _forwarded.Clear();
            return 0;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = ContextFor(scope.ServiceProvider);
        var now = timeProvider.GetUtcNow();
        var windowStart = now.UtcDateTime - options.Window;
        var poll = new Poll(this, logger);

        if (Cursor is null)
        {
            // No replay: connected clients resync on connect. Rows already in the window count as forwarded.
            var last = await TaskChangeFeed.GetLastIdAsync(db, cancellationToken).ConfigureAwait(false);
            // Only ids up to the new cursor: a row committed after GetLastIdAsync (id above it) must
            // stay for the next cursor read.
            poll.Covered.UnionWith((await TaskChangeFeed.ReadIdsSinceAsync(db, windowStart, cancellationToken)
                .ConfigureAwait(false)).Where(id => id <= last));
            poll.Cursor = last;
            Commit(poll, now);
            return 0;
        }

        var rows = await TaskChangeFeed.ReadAfterAsync(db, Cursor.Value, options.MaxRowsPerTick, cancellationToken)
            .ConfigureAwait(false);
        foreach (var row in rows) poll.Forward(row);
        if (rows.Count > 0) poll.Cursor = rows[^1].Id;

        if (rows.Count == options.MaxRowsPerTick)
            await SkipAheadAsync(db, windowStart, poll, cancellationToken).ConfigureAwait(false);

        // The late-row window: ids at or below the cursor not forwarded yet.
        var windowIds = await TaskChangeFeed.ReadIdsSinceAsync(db, windowStart, cancellationToken).ConfigureAwait(false);
        var late = windowIds.Where(id => id <= poll.Cursor && !poll.IsForwarded(id)).ToList();
        var lateRows = await TaskChangeFeed.ReadByIdsAsync(db, late, cancellationToken).ConfigureAwait(false);
        foreach (var row in lateRows) poll.Forward(row);

        Commit(poll, now);
        return rows.Count + lateRows.Count;
    }

    /// <summary>The end of a successful poll: cursor, forwarded ids and the publish, together.</summary>
    private void Commit(Poll poll, DateTimeOffset now)
    {
        Cursor = poll.Cursor;
        foreach (var id in poll.Covered) _forwarded[id] = now;
        // Remembered a little longer than the window, so the window cannot forward an id twice.
        var before = now - (options.Window + TimeSpan.FromSeconds(5));
        foreach (var id in _forwarded.Where(kv => kv.Value < before).Select(kv => kv.Key).ToList())
            _forwarded.Remove(id);
        if (poll.Changes.Count > 0) publisher.Submit(poll.Changes);
    }

    /// <summary>What one poll has read so far; applied only by <see cref="Commit"/>.</summary>
    private sealed class Poll(LiveFeedReader reader, ILogger log)
    {
        public long? Cursor { get; set; } = reader.Cursor;
        public HashSet<long> Covered { get; } = [];
        public List<LiveChange> Changes { get; } = [];

        public bool IsForwarded(long id) => Covered.Contains(id) || reader._forwarded.ContainsKey(id);

        public void Forward(TaskChangeFeedEntry row)
        {
            if (IsForwarded(row.Id)) return; // once
            Covered.Add(row.Id);
            if (ToChange(row) is { } change)
                Changes.Add(change);
            else
                log.LogDebug("Live feed reader: skipped row {Id} (kind {Kind}): no routing id", row.Id, row.Kind);
        }
    }

    /// <summary>
    /// Behind by more than one poll: one query for the distinct properties and sites of the rest,
    /// one property-wide hint per property and AssignmentsChanged per site, cursor to the end.
    /// </summary>
    private async Task SkipAheadAsync(BackendConfigurationPnDbContext db, DateTime windowStart, Poll poll,
        CancellationToken cancellationToken)
    {
        var from = poll.Cursor!.Value;
        var last = await TaskChangeFeed.GetLastIdAsync(db, cancellationToken).ConfigureAwait(false);
        if (last <= from) return;
        // Coverage BEFORE the routing read: every covered row is then also in the routing read (it was
        // visible earlier), and a row that commits in between stays eligible for the late-row window.
        var covered = (await TaskChangeFeed.ReadIdsSinceAsync(db, windowStart, cancellationToken).ConfigureAwait(false))
            .Where(id => id > from && id <= last).ToList();
        var skipped = await db.TaskChangeFeedEntries.AsNoTracking()
            .Where(e => e.Id > from && e.Id <= last)
            .Select(e => new { e.PropertyId, e.SdkSiteId, e.Kind })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var properties = skipped.Where(x => x.PropertyId > 0).Select(x => x.PropertyId!.Value).Distinct().ToList();
        var sites = skipped.Where(x => x.Kind == FeedKinds.Assignments && x.SdkSiteId > 0)
            .Select(x => x.SdkSiteId!.Value).Distinct().ToList();
        poll.Changes.AddRange(properties.Select(p => LiveChange.ForTask(p)));
        poll.Changes.AddRange(sites.Select(LiveChange.ForAssignments));
        logger.LogInformation(
            "Live feed reader: {Count} id(s) behind, skipped ahead with {Properties} property-wide hint(s) and {Sites} site hint(s)",
            last - from, properties.Count, sites.Count);
        poll.Cursor = last;
        // The skipped rows are covered: the window must not forward them one by one.
        poll.Covered.UnionWith(covered);
    }

    /// <summary>
    /// A feed row as a <see cref="LiveChange"/>: changed/removed (unknown kinds count as changed) need the
    /// property; property gives a property-wide hint; assignments needs the site. Null = not routable.
    /// </summary>
    internal static LiveChange? ToChange(TaskChangeFeedEntry row)
    {
        if (row.Kind == FeedKinds.Assignments)
            return row.SdkSiteId is > 0 ? LiveChange.ForAssignments(row.SdkSiteId.Value) : null;
        if (row.PropertyId is not > 0) return null;
        var propertyId = row.PropertyId.Value;
        if (row.Kind == FeedKinds.Property) return LiveChange.ForTask(propertyId);
        var kind = row.Kind == FeedKinds.Removed ? TaskChangeKinds.Removed : TaskChangeKinds.Changed;
        if (row.AreaRulePlanningId is not > 0) return LiveChange.ForTask(propertyId);
        DateOnly? day = DateOnly.TryParseExact(row.PlanDayKey, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;
        return LiveChange.ForTask(propertyId, row.AreaRulePlanningId.Value, day,
            row.ComplianceId is > 0 ? row.ComplianceId : null, kind);
    }

    private async Task RetentionIfDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        _nextRetention ??= now + options.FirstRetentionDelay;
        if (now < _nextRetention) return;
        _nextRetention = now + options.RetentionInterval;
        try
        {
            await RunRetentionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Live feed reader: retention failed; next try in {Interval}", options.RetentionInterval);
        }
    }

    /// <summary>
    /// Deletes rows older than <see cref="LiveFeedOptions.RetentionAge"/> through the base package's
    /// helper, at most <see cref="LiveFeedOptions.RetentionMaxBatches"/> statements of
    /// <see cref="LiveFeedOptions.RetentionBatch"/> rows. Throws on failure. Returns the rows deleted.
    /// </summary>
    internal async Task<int> RunRetentionAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = ContextFor(scope.ServiceProvider);
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - options.RetentionAge;
        var total = 0;
        for (var i = 0; i < options.RetentionMaxBatches; i++)
        {
            var deleted = await TaskChangeFeed.DeleteOlderThanAsync(db, cutoff, options.RetentionBatch, cancellationToken)
                .ConfigureAwait(false);
            total += deleted;
            if (deleted < options.RetentionBatch) break;
        }

        if (total > 0) logger.LogInformation("Live feed reader: deleted {Count} feed row(s) older than {Age}", total, options.RetentionAge);
        return total;
    }
}

/// <summary>DI wiring of the feed reader.</summary>
public static class LiveFeedRegistration
{
    /// <summary>
    /// Registers the reader as a hosted service. The host calls the plugin's ConfigureServices with
    /// its own IServiceCollection while it builds the generic host, so the generic host starts it.
    /// </summary>
    public static IServiceCollection AddBackendConfigurationLiveFeed(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new LiveFeedOptions());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, LiveFeedReader>());
        return services;
    }
}
