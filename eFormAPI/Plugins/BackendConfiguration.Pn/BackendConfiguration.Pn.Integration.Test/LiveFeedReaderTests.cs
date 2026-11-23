#nullable enable

namespace BackendConfiguration.Pn.Integration.Test;

using System.Collections.Concurrent;
using System.Data.Common;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.LiveChanges;
using BackendConfiguration.Pn.Services.LiveFeed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Helpers;
using Call = RecordingLiveNotifier.Call;
using FeedKinds = Microting.EformBackendConfigurationBase.Infrastructure.Const.TaskChangeKinds;

/// <summary>
/// The hosted change-feed reader (package 10, step 5) on the real feed table:
/// the base package's reader contract (cursor + late-row window, each row
/// once), no work while nobody is connected, kind mapping, bursts, failure
/// back-off, retention, and that it runs with a generic host.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LiveFeedReaderTests : TestBaseSetup
{
    private LiveChangeTestHost _host = null!;
    private CommandCounter _commands = null!;
    private ThrowOnceOnWindowQuery _thrower = null!;
    private BeforeWindowQuery _inject = null!;
    private LiveFeedOptions _options = null!;
    private LiveFeedReader _reader = null!;
    private static int _nextProperty = 7000;

    [SetUp]
    public void SetUpReader()
    {
        _commands = new CommandCounter();
        _thrower = new ThrowOnceOnWindowQuery();
        _inject = new BeforeWindowQuery();
        _host = new LiveChangeTestHost(ConnectionStringFor("420_eform-backend-configuration-plugin"),
            ConnectionStringFor("420_eform-angular-items-planning-plugin"), null,
            extraInterceptors: [_commands, _thrower, _inject]);
        _options = new LiveFeedOptions { FirstRetentionDelay = TimeSpan.FromDays(1) };
        _reader = NewReader(_host, _options);
    }

    [TearDown]
    public void TearDownReader()
    {
        _reader.Dispose();
        _host.Dispose();
    }

    private static LiveFeedReader NewReader(LiveChangeTestHost host, LiveFeedOptions options) =>
        new(host.Provider.GetRequiredService<IServiceScopeFactory>(), host.Registry, host.Publisher, options,
            TimeProvider.System, host.Provider.GetRequiredService<ILogger<LiveFeedReader>>());

    private static int NewProperty() => Interlocked.Increment(ref _nextProperty);

    private async Task<TaskChangeFeedEntry> AppendAsync(string kind, int? propertyId = null, int? eventId = null,
        string? day = null, int? complianceId = null, int? site = null)
    {
        await using var db = CreateFreshBackendConfigurationDbContext();
        return await TaskChangeFeed.AppendAndSaveAsync(db, kind, propertyId, eventId, day, complianceId, site, "test");
    }

    /// <summary>A row written directly: an unknown kind, a missing routing id, an explicit id or an old time.</summary>
    private async Task<long> InsertRawAsync(string kind, int? propertyId = null, int? eventId = null, int? site = null,
        long? id = null, DateTime? createdAt = null)
    {
        await using var db = CreateFreshBackendConfigurationDbContext();
        var entry = new TaskChangeFeedEntry
        {
            Kind = kind, PropertyId = propertyId, AreaRulePlanningId = eventId, SdkSiteId = site,
            CreatedAt = createdAt ?? DateTime.UtcNow, Source = "test"
        };
        if (id is { } explicitId) entry.Id = explicitId;
        db.TaskChangeFeedEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<long> LastIdAsync()
    {
        await using var db = CreateFreshBackendConfigurationDbContext();
        return await TaskChangeFeed.GetLastIdAsync(db);
    }

    private async Task<List<Call>> PollAsync()
    {
        _host.Notifier.Clear();
        await _reader.PollOnceAsync(CancellationToken.None);
        await _host.Publisher.WhenIdleAsync();
        return _host.Notifier.Calls;
    }

    // ------------------------------------------------------------------
    // Reader contract
    // ------------------------------------------------------------------

    [Test]
    public async Task StartsAtTheLastId_WithoutReplay_ThenForwardsEachRowOnce()
    {
        var p = NewProperty();
        await AppendAsync(FeedKinds.Changed, p, 1);

        Assert.That(await PollAsync(), Is.Empty, "the first poll only places the cursor");
        Assert.That(_reader.Cursor, Is.EqualTo(await LastIdAsync()));

        await AppendAsync(FeedKinds.Changed, p, 2, "2026-11-23", 9);
        Assert.That(await PollAsync(), Is.EqualTo(new[]
        {
            new Call("TaskChanged", p, 2, new DateOnly(2026, 11, 23), 9, TaskChangeKinds.Changed)
        }));
        Assert.That(await PollAsync(), Is.Empty, "once");
    }

    [Test]
    public async Task ALateVisibleLowerIdRow_IsForwardedOnce_ThroughTheWindow()
    {
        var p = NewProperty();
        await PollAsync(); // cursor at the current end
        var gapStart = await LastIdAsync();
        await InsertRawAsync(FeedKinds.Changed, p, 20, id: gapStart + 100);
        Assert.That(await PollAsync(), Has.Count.EqualTo(1));

        // A lower id that only becomes visible now (another writer committed later).
        await InsertRawAsync(FeedKinds.Changed, p, 21, id: gapStart + 50);

        Assert.That(await PollAsync(), Is.EqualTo(new[]
        {
            new Call("TaskChanged", p, 21, null, null, TaskChangeKinds.Changed)
        }));
        Assert.That(await PollAsync(), Is.Empty, "the window does not forward it again");
    }

    [Test]
    public async Task NobodyConnected_NothingIsQueried_AndTheFirstConnectJumpsTheCursor()
    {
        using var host = new LiveChangeTestHost(ConnectionStringFor("420_eform-backend-configuration-plugin"),
            ConnectionStringFor("420_eform-angular-items-planning-plugin"), null, connected: false,
            extraInterceptors: [_commands]);
        using var reader = NewReader(host, _options);
        var p = NewProperty();
        await AppendAsync(FeedKinds.Changed, p, 1);

        _commands.Reset();
        for (var i = 0; i < 5; i++) await reader.TickAsync(CancellationToken.None);
        Assert.That(_commands.Count, Is.Zero, "no query while idle");
        await AppendAsync(FeedKinds.Changed, p, 2); // written while idle: covered by the connect resync

        host.Registry.TryConnect("c", 1, DateTimeOffset.MaxValue, () => { }, 10);
        await reader.PollOnceAsync(CancellationToken.None);
        await host.Publisher.WhenIdleAsync();
        Assert.That(host.Notifier.Calls, Is.Empty, "no replay of what was written while idle");
        Assert.That(reader.Cursor, Is.EqualTo(await LastIdAsync()));

        await AppendAsync(FeedKinds.Changed, p, 3);
        await reader.PollOnceAsync(CancellationToken.None);
        await host.Publisher.WhenIdleAsync();
        Assert.That(host.Notifier.Calls, Is.EqualTo(new[] { new Call("TaskChanged", p, 3, null, null, TaskChangeKinds.Changed) }));
    }

    [Test]
    public async Task Kinds_MapToTheHubCalls()
    {
        var p = NewProperty();
        var q = NewProperty();
        var r = NewProperty();
        await PollAsync();
        await AppendAsync(FeedKinds.Changed, p, 5, "2026-11-24", 50);
        await AppendAsync(FeedKinds.Removed, q, 6);
        await AppendAsync(FeedKinds.Property, r);
        await AppendAsync(FeedKinds.Assignments, site: 4242);
        var unknownWithProperty = NewProperty();
        await InsertRawAsync("rescheduled", unknownWithProperty, 7);  // unknown → changed
        await InsertRawAsync("rescheduled");                          // unknown, no property → skipped
        await InsertRawAsync(FeedKinds.Changed, eventId: 8);          // no routing id → skipped
        await InsertRawAsync(FeedKinds.Assignments);                  // no site → skipped

        Assert.That(await PollAsync(), Is.EquivalentTo(new[]
        {
            new Call("TaskChanged", p, 5, new DateOnly(2026, 11, 24), 50, TaskChangeKinds.Changed),
            new Call("TaskChanged", q, 6, null, null, TaskChangeKinds.Removed),
            new Call("TaskChanged", r, null, null, null, TaskChangeKinds.Property),
            new Call("TaskChanged", unknownWithProperty, 7, null, null, TaskChangeKinds.Changed),
            new Call("AssignmentsChanged", SdkSiteId: 4242)
        }));
    }

    // ------------------------------------------------------------------
    // Bursts
    // ------------------------------------------------------------------

    [Test]
    public async Task ABurst_IsCoalescedPerProperty_LikeARequestFlush()
    {
        var p = NewProperty();
        await PollAsync();
        for (var e = 1; e <= 30; e++) await AppendAsync(FeedKinds.Changed, p, e);

        Assert.That(await PollAsync(), Is.EqualTo(new[]
        {
            new Call("TaskChanged", p, null, null, null, TaskChangeKinds.Property)
        }));
    }

    [Test]
    public async Task FarBehind_ATickIsBounded_SkipsAheadWithPropertyWideHints()
    {
        _options.MaxRowsPerTick = 10;
        var props = new[] { NewProperty(), NewProperty(), NewProperty() };
        await PollAsync();
        var site = 777;
        for (var i = 0; i < 25; i++) await AppendAsync(FeedKinds.Changed, props[i % 3], 100 + i);
        await AppendAsync(FeedKinds.Assignments, site: site);

        _commands.Reset();
        var calls = await PollAsync();

        Assert.That(calls, Is.EquivalentTo(props.Select(x => new Call("TaskChanged", x, null, null, null, TaskChangeKinds.Property))
            .Append(new Call("AssignmentsChanged", SdkSiteId: site))));
        Assert.That(_reader.Cursor, Is.EqualTo(await LastIdAsync()), "caught up in one tick");
        Assert.That(_commands.Count, Is.LessThanOrEqualTo(6), "a bounded number of queries per tick");
        Assert.That(await PollAsync(), Is.Empty, "nothing forwarded twice");
    }

    // ------------------------------------------------------------------
    // Failures
    // ------------------------------------------------------------------

    [Test]
    public async Task AMissingTable_BacksOffUpTo30s_AndRecoversWithoutRestart()
    {
        await PollAsync();
        var real = _reader.ContextFor;
        // The items-planning database has no feed table: the rolling-start case.
        _reader.ContextFor = _ => new BackendConfigurationPnDbContext(
            new DbContextOptionsBuilder<BackendConfigurationPnDbContext>()
                .UseMySql(ConnectionStringFor("420_eform-angular-items-planning-plugin"),
                    ServerVersion.AutoDetect(ConnectionStringFor("420_eform-angular-items-planning-plugin"))).Options);

        var delays = new List<TimeSpan>();
        for (var i = 0; i < 7; i++) delays.Add(await _reader.TickAsync(CancellationToken.None));

        Assert.That(delays.Select(d => d.TotalSeconds), Is.EqualTo(new double[] { 2, 4, 8, 16, 30, 30, 30 }));
        Assert.That(_host.Warnings.WarningsOf<LiveFeedReader>(), Is.EqualTo(1), "one warning for the whole streak");

        _reader.ContextFor = real;
        var p = NewProperty();
        await AppendAsync(FeedKinds.Changed, p, 1);
        _host.Notifier.Clear();
        Assert.That(await _reader.TickAsync(CancellationToken.None), Is.EqualTo(_options.PollInterval), "recovered");
        await _host.Publisher.WhenIdleAsync();
        Assert.That(_host.Notifier.Calls, Is.EqualTo(new[] { new Call("TaskChanged", p, 1, null, null, TaskChangeKinds.Changed) }),
            "the cursor survived the failures");
    }

    [Test]
    public async Task APollThatFailsAfterItsCursorRead_IsRetriedWhole_AndItsRowsAreForwardedOnce()
    {
        // Review round 1: the window query fails after the cursor read returned rows.
        var p = NewProperty();
        await PollAsync();
        await AppendAsync(FeedKinds.Changed, p, 1);
        _thrower.Arm();

        Assert.That(await _reader.TickAsync(CancellationToken.None), Is.EqualTo(TimeSpan.FromSeconds(2)), "the poll failed");
        Assert.That(_thrower.Fired, Is.True);
        await _host.Publisher.WhenIdleAsync();
        Assert.That(_host.Notifier.Calls, Is.Empty, "nothing of the failed poll was sent");

        Assert.That(await PollAsync(), Is.EqualTo(new[] { new Call("TaskChanged", p, 1, null, null, TaskChangeKinds.Changed) }),
            "retried from the old cursor");
        Assert.That(await PollAsync(), Is.Empty, "and only once");
    }

    [Test]
    public async Task ARowCommittedDuringTheFirstPoll_AboveItsLastId_IsStillForwarded()
    {
        // PR #1526 review: the row commits between GetLastIdAsync and the window read of the
        // cursor-placing poll. Its id is above the new cursor: the next cursor read must forward it.
        var p = NewProperty();
        _inject.Arm(async () => await AppendAsync(FeedKinds.Changed, p, 1));

        await PollAsync(); // places the cursor; the insert happens inside it
        Assert.That(_inject.Fired, Is.True);

        Assert.That(await PollAsync(), Is.EqualTo(new[] { new Call("TaskChanged", p, 1, null, null, TaskChangeKinds.Changed) }));
        Assert.That(await PollAsync(), Is.Empty, "once");
    }

    [Test]
    public async Task ARowCommittedDuringSkipAhead_BelowItsLastId_StillGetsAHint()
    {
        // PR #1526 review: during skip-ahead a lower-id row of another property commits after the
        // routing read and before the coverage read. It must not be marked covered unhinted.
        _options.MaxRowsPerTick = 2;
        var a = NewProperty();
        var b = NewProperty();
        await PollAsync();
        await AppendAsync(FeedKinds.Changed, a, 1);
        var second = await AppendAsync(FeedKinds.Changed, a, 2);
        await InsertRawAsync(FeedKinds.Changed, a, 3, id: second.Id + 10);
        // The poll's first window-style read is skip-ahead's coverage read.
        _inject.Arm(async () => await InsertRawAsync(FeedKinds.Changed, b, 4, id: second.Id + 5));

        var calls = await PollAsync();
        Assert.That(_inject.Fired, Is.True);
        calls.AddRange(await PollAsync());

        Assert.That(calls.Where(c => c.PropertyId == b), Is.Not.Empty, "the late row's property got a hint");
    }

    [Test]
    public async Task AThrowingContext_NeverEscapesTheTick()
    {
        _reader.ContextFor = _ => throw new InvalidOperationException("database down");

        Assert.That(await _reader.TickAsync(CancellationToken.None), Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(await _reader.TickAsync(CancellationToken.None), Is.EqualTo(TimeSpan.FromSeconds(4)));
    }

    // ------------------------------------------------------------------
    // Retention
    // ------------------------------------------------------------------

    [Test]
    public async Task Retention_DeletesOnlyOldRows_InCappedBatches_EvenWithNobodyConnected()
    {
        using var host = new LiveChangeTestHost(ConnectionStringFor("420_eform-backend-configuration-plugin"),
            ConnectionStringFor("420_eform-angular-items-planning-plugin"), null, connected: false);
        var options = new LiveFeedOptions { RetentionBatch = 2, RetentionMaxBatches = 2, FirstRetentionDelay = TimeSpan.Zero };
        using var reader = NewReader(host, options);
        var marker = NewProperty();
        var old = new List<long>();
        for (var i = 0; i < 5; i++)
            old.Add(await InsertRawAsync(FeedKinds.Property, marker, createdAt: DateTime.UtcNow.AddHours(-25)));
        var fresh = await InsertRawAsync(FeedKinds.Property, marker);

        async Task<List<long>> RemainingAsync()
        {
            await using var db = CreateFreshBackendConfigurationDbContext();
            return await db.TaskChangeFeedEntries.Where(x => x.PropertyId == marker).Select(x => x.Id).OrderBy(x => x).ToListAsync();
        }

        // Through the idle tick: retention is due at once and runs although nobody is connected.
        await reader.TickAsync(CancellationToken.None);
        var afterFirst = await RemainingAsync();
        Assert.That(afterFirst, Does.Contain(fresh));
        Assert.That(afterFirst.Count(id => old.Contains(id)), Is.LessThanOrEqualTo(1), "at most 2 x 2 rows per run");

        await reader.RunRetentionAsync(CancellationToken.None);
        Assert.That(await RemainingAsync(), Is.EqualTo(new[] { fresh }), "only the old rows are deleted");

        // Not due again for an hour: the next idle tick deletes nothing.
        var again = await InsertRawAsync(FeedKinds.Property, marker, createdAt: DateTime.UtcNow.AddHours(-25));
        await reader.TickAsync(CancellationToken.None);
        Assert.That(await RemainingAsync(), Does.Contain(again));
    }

    // ------------------------------------------------------------------
    // Hosting
    // ------------------------------------------------------------------

    [Test]
    public async Task TheReader_RunsWithAGenericHost_AndStopsCleanly()
    {
        var p = NewProperty();
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                _host.RegisterInto(services);
                services.AddSingleton(new LiveFeedOptions { PollInterval = TimeSpan.FromMilliseconds(100) });
                services.AddBackendConfigurationLiveFeed();
            })
            .Build();

        await host.StartAsync();
        var reader = host.Services.GetServices<IHostedService>().OfType<LiveFeedReader>().Single();
        await WaitForAsync(() => reader.Cursor != null, "the reader placed its cursor");
        _host.Notifier.Clear();
        await AppendAsync(FeedKinds.Changed, p, 1);
        await WaitForAsync(() => _host.Notifier.Calls.Count > 0, "a row was forwarded by the running host");

        var stop = host.StopAsync();
        Assert.That(await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(stop), "stops promptly");
        Assert.That(reader.ExecuteTask, Is.Not.Null);
        Assert.That(reader.ExecuteTask!.IsFaulted, Is.False);
        Assert.That(_host.Notifier.Calls.First(), Is.EqualTo(new Call("TaskChanged", p, 1, null, null, TaskChangeKinds.Changed)));
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > until) Assert.Fail("timed out waiting: " + what);
            await Task.Delay(50);
        }
    }

    /// <summary>When armed, runs an action (an insert) once, right before the next window-style read (CreatedAt).</summary>
    private sealed class BeforeWindowQuery : DbCommandInterceptor
    {
        private Func<Task>? _action;
        public bool Fired { get; private set; }
        public void Arm(Func<Task> action) => Interlocked.Exchange(ref _action, action);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("`CreatedAt` >=") && Interlocked.Exchange(ref _action, null) is { } action)
            {
                Fired = true;
                await action();
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>When armed, fails the next window query (the CreatedAt read) once.</summary>
    private sealed class ThrowOnceOnWindowQuery : DbCommandInterceptor
    {
        private int _armed;
        public bool Fired { get; private set; }
        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("`CreatedAt` >=") && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Fired = true;
                // Not a transient error class, so the retrying execution strategy does not hide it.
                throw new InvalidOperationException("simulated failure of the window query");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
