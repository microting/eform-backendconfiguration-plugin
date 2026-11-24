#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.LiveNotifier;
using BackendConfiguration.Pn.Services.LiveChanges;
using BackendConfiguration.Pn.Services.TaskReminders;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using Call = BackendConfiguration.Pn.Test.Services.LiveChanges.RecordingLiveNotifier.Call;

namespace BackendConfiguration.Pn.Test.Services.LiveChanges;

/// <summary>
/// Resolution and coalescing of one flush (<see cref="LiveChangePublisher"/>):
/// one hint per (property, event) with the most precise key the changes agree
/// on, a property-wide hint above the threshold, revocations before hints, and
/// no failure ever escaping.
/// </summary>
[TestFixture]
public class LiveChangePublisherTests
{
    private const int Property = 42;
    private const int OtherProperty = 43;

    private RecordingLiveNotifier _notifier = null!;
    private ILiveChangeResolver _resolver = null!;
    private CountingLogger<LiveChangePublisher> _logger = null!;
    private LiveChangePublisher _publisher = null!;
    private LiveConnectionRegistry _registry = null!;

    /// <summary>A registry with one live connection: the publisher does nothing without one.</summary>
    internal static LiveConnectionRegistry ConnectedRegistry()
    {
        var registry = new LiveConnectionRegistry(TimeProvider.System);
        registry.TryConnect("c1", 1, DateTimeOffset.MaxValue, () => { }, 10);
        return registry;
    }

    private static bool IsPairs(IReadOnlyCollection<(int PlanningId, int PropertyId)> pairs, int planningId, int propertyId) =>
        pairs.Count == 1 && pairs.First().PlanningId == planningId && pairs.First().PropertyId == propertyId;

    private LiveChangePublisher NewPublisher() =>
        new(_notifier, _resolver, _registry, new LiveChangeWarnings(), _logger, new TaskReminderSignals());

    [SetUp]
    public void SetUp()
    {
        _notifier = new RecordingLiveNotifier();
        _resolver = Substitute.For<ILiveChangeResolver>();
        _resolver.LoadAsync(default!, default).ReturnsForAnyArgs(LiveChangeLookups.Empty);
        _logger = new CountingLogger<LiveChangePublisher>();
        _registry = ConnectedRegistry();
        _publisher = NewPublisher();
    }

    private void Lookups(params LiveEventRef[] events) =>
        _resolver.LoadAsync(default!, default).ReturnsForAnyArgs(
            new LiveChangeLookups(events));

    private static readonly DateOnly Day = new(2026, 11, 23);

    [Test]
    public async Task FullKey_IsSentAsIs()
    {
        await _publisher.PublishAsync([LiveChange.ForTask(Property, 7, Day, 900)]);

        Assert.That(_notifier.Calls, Is.EqualTo(new[]
        {
            new Call("TaskChanged", Property, 7, Day, 900, TaskChangeKinds.Changed)
        }));
        await _resolver.DidNotReceiveWithAnyArgs().LoadAsync(default!, default);
    }

    [Test]
    public async Task ManyChangesOfOneOccurrence_AreOneHint_WithTheFullKey()
    {
        // What one CompleteEvent-like request produces: the explicit full key plus
        // the intercepted rows of the same occurrence, which carry less of the key.
        await _publisher.PublishAsync(
        [
            LiveChange.ForTask(Property, 7, Day, 900),
            LiveChange.ForTask(Property, 7, complianceId: 900, kind: TaskChangeKinds.Removed),
            LiveChange.ForTask(Property, 7),
            LiveChange.ForTask(Property, 7, Day, 900)
        ]);

        Assert.That(_notifier.Calls, Is.EqualTo(new[]
        {
            new Call("TaskChanged", Property, 7, Day, 900, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task ChangesOfSeveralOccurrencesOfOneEvent_AreOneEventHint()
    {
        await _publisher.PublishAsync(
        [
            LiveChange.ForTask(Property, 7, Day, 900),
            LiveChange.ForTask(Property, 7, Day.AddDays(1), 901)
        ]);

        Assert.That(_notifier.Calls, Is.EqualTo(new[]
        {
            new Call("TaskChanged", Property, 7, null, null, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task OneComplianceWithoutDay_KeepsTheComplianceId()
    {
        await _publisher.PublishAsync(
        [
            LiveChange.ForTask(Property, 7, complianceId: 900),
            LiveChange.ForTask(Property, 7)
        ]);

        Assert.That(_notifier.Calls.Single(), Is.EqualTo(new Call("TaskChanged", Property, 7, null, 900, TaskChangeKinds.Changed)));
    }

    [Test]
    public async Task IdenticalRemovals_StayRemoved_ButARemovalMergedWithAChangeIsAChange()
    {
        await _publisher.PublishAsync(
        [
            LiveChange.ForTask(Property, 7, kind: TaskChangeKinds.Removed),
            LiveChange.ForTask(Property, 7, kind: TaskChangeKinds.Removed),
            LiveChange.ForTask(Property, 8, complianceId: 5, kind: TaskChangeKinds.Removed),
            LiveChange.ForTask(Property, 8, complianceId: 6, kind: TaskChangeKinds.Removed)
        ]);

        Assert.That(_notifier.Calls, Is.EquivalentTo(new[]
        {
            new Call("TaskChanged", Property, 7, null, null, TaskChangeKinds.Removed),
            // Two removed occurrences told as one event: "removed" with only the event id
            // would mean the whole task is gone, so it is a change.
            new Call("TaskChanged", Property, 8, null, null, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task UpToTheThreshold_EachEventIsTold_AboveIt_OnePropertyHint()
    {
        var atThreshold = Enumerable.Range(1, LiveChangeLimits.PropertyWideThreshold)
            .Select(e => LiveChange.ForTask(Property, e)).ToList();
        await _publisher.PublishAsync(atThreshold);
        Assert.That(_notifier.Calls, Has.Count.EqualTo(LiveChangeLimits.PropertyWideThreshold));
        Assert.That(_notifier.Calls.Select(c => c.Kind), Has.All.EqualTo(TaskChangeKinds.Changed));

        _notifier = new RecordingLiveNotifier();
        _publisher = NewPublisher();
        var above = Enumerable.Range(1, LiveChangeLimits.PropertyWideThreshold + 1)
            .Select(e => LiveChange.ForTask(Property, e, Day, e)).Append(LiveChange.ForTask(OtherProperty, 99)).ToList();
        await _publisher.PublishAsync(above);

        Assert.That(_notifier.Calls, Is.EquivalentTo(new[]
        {
            new Call("TaskChanged", Property, null, null, null, TaskChangeKinds.Property),
            new Call("TaskChanged", OtherProperty, 99, null, null, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task AChangeWithoutAnEvent_MakesThePropertyHintPropertyWide()
    {
        await _publisher.PublishAsync([LiveChange.ForTask(Property, 7, Day, 1), LiveChange.ForTask(Property)]);

        Assert.That(_notifier.Calls.Single(), Is.EqualTo(new Call("TaskChanged", Property, null, null, null, TaskChangeKinds.Property)));
    }

    [Test]
    public async Task MissingIds_AreResolvedInOneLookup_PlanningsWithAKnownPropertyByProperty()
    {
        // Event 7 and 8 on Property; planning 70 backs event 7, planning 80 backs events on two properties.
        Lookups(new LiveEventRef(7, 70, Property), new LiveEventRef(8, 80, Property), new LiveEventRef(9, 80, OtherProperty));

        await _publisher.PublishAsync(
        [
            LiveChange.ForEvent(7),
            LiveChange.ForPlanning(70, complianceId: 500),
            LiveChange.ForPlanning(80, propertyId: OtherProperty, complianceId: 600), // a compliance row knows its property
            LiveChange.ForEvent(12345), // unknown: dropped
        ]);

        await _resolver.Received(1).LoadAsync(
            Arg.Is<LiveChangeLookupRequest>(r => r.EventIds.SequenceEqual(new[] { 7, 12345 })
                                                 && IsPairs(r.PlanningsOnProperty, 80, OtherProperty)
                                                 && r.PlanningsAnywhere.SequenceEqual(new[] { 70 })),
            Arg.Any<CancellationToken>());
        Assert.That(_notifier.Calls, Is.EquivalentTo(new[]
        {
            new Call("TaskChanged", Property, 7, null, 500, TaskChangeKinds.Changed),
            new Call("TaskChanged", OtherProperty, 9, null, 600, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task APlanningOnlyRow_IsResolvedAcrossAllItsEvents_EvenBesideAComplianceOfThePlanning()
    {
        // PR #1525 review: the Compliance row's property is ITS scope only. Planning 70 also
        // backs event 9 on another property; the planning-only row (a PlanningCaseSite) must reach it.
        Lookups(new LiveEventRef(7, 70, Property), new LiveEventRef(9, 70, OtherProperty));

        await _publisher.PublishAsync([LiveChange.ForPlanning(70, Property, 500), LiveChange.ForPlanning(70)]);

        await _resolver.Received(1).LoadAsync(
            Arg.Is<LiveChangeLookupRequest>(r => IsPairs(r.PlanningsOnProperty, 70, Property)
                                                 && r.PlanningsAnywhere.SequenceEqual(new[] { 70 })),
            Arg.Any<CancellationToken>());
        Assert.That(_notifier.Calls, Is.EquivalentTo(new[]
        {
            new Call("TaskChanged", Property, 7, null, 500, TaskChangeKinds.Changed),
            new Call("TaskChanged", OtherProperty, 9, null, null, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task APlanningOnlyRow_IsResolvedAcrossAllItsEvents_EvenBesideATaskRowOfThePlanning()
    {
        // Two events can share a planning (CalendarPastSeriesBackfill, TaskListActiveWithoutPlanningRepair
        // copy ItemPlanningId): the task row names one of them only.
        Lookups(new LiveEventRef(7, 70, Property), new LiveEventRef(8, 70, OtherProperty));

        await _publisher.PublishAsync([LiveChange.ForAreaRulePlanning(7, 70, Property), LiveChange.ForPlanning(70)]);

        await _resolver.Received(1).LoadAsync(
            Arg.Is<LiveChangeLookupRequest>(r => r.PlanningsAnywhere.SequenceEqual(new[] { 70 })),
            Arg.Any<CancellationToken>());
        Assert.That(_notifier.Calls, Is.EquivalentTo(new[]
        {
            new Call("TaskChanged", Property, 7, null, null, TaskChangeKinds.Changed),
            new Call("TaskChanged", OtherProperty, 8, null, null, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task PlanningIdZero_IsNeverLookedUp_AComplianceOfItStillTellsItsProperty()
    {
        // ItemPlanningId 0 means "not deployed" and matches many task rows: never a lookup key.
        await _publisher.PublishAsync(
        [
            LiveChange.ForPlanning(0, Property, 500),
            LiveChange.ForPlanning(0),
            LiveChange.ForPlanning(-3),
            LiveChange.ForAreaRulePlanning(7, 0, OtherProperty),
        ]);

        await _resolver.DidNotReceiveWithAnyArgs().LoadAsync(default!, default);
        Assert.That(_notifier.Calls, Is.EquivalentTo(new[]
        {
            new Call("TaskChanged", Property, null, null, null, TaskChangeKinds.Property),
            new Call("TaskChanged", OtherProperty, 7, null, null, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task ATaskRowAlone_NeedsNoLookup()
    {
        await _publisher.PublishAsync([LiveChange.ForAreaRulePlanning(7, 70, Property)]);

        await _resolver.DidNotReceiveWithAnyArgs().LoadAsync(default!, default);
        Assert.That(_notifier.Calls.Single(), Is.EqualTo(new Call("TaskChanged", Property, 7, null, null, TaskChangeKinds.Changed)));
    }

    [Test]
    public async Task WithNoConnection_NothingIsLookedUpOrSent()
    {
        _registry = new LiveConnectionRegistry(TimeProvider.System);
        _publisher = NewPublisher();

        await _publisher.PublishAsync([LiveChange.ForEvent(7), LiveChange.ForAssignments(5)]);
        Assert.That(_publisher.ApplyRevocations([LiveChange.ForRevokeSite(6), LiveChange.ForRevokeProperty(6, Property)]),
            Is.Empty);

        await _resolver.DidNotReceiveWithAnyArgs().LoadAsync(default!, default);
        Assert.That(_notifier.Calls, Is.Empty);
        Assert.That(_publisher.HasAudience, Is.False);
    }

    [Test]
    public async Task OneAssignmentHintPerSite_AfterTheTaskHints()
    {
        await _publisher.PublishAsync(
        [
            LiveChange.ForAssignments(12),
            LiveChange.ForTask(Property, 7),
            LiveChange.ForAssignments(12)
        ]);

        Assert.That(_notifier.Calls, Is.EqualTo(new[]
        {
            new Call("TaskChanged", Property, 7, null, null, TaskChangeKinds.Changed),
            new Call("AssignmentsChanged", SdkSiteId: 12)
        }));
    }

    [Test]
    public void ApplyRevocations_RunsTheKnownOnesAtOnce_AndReturnsTheRest()
    {
        var rest = _publisher.ApplyRevocations(
        [
            LiveChange.ForRevokeProperty(11, Property),
            LiveChange.ForRevokeSite(12),
            LiveChange.ForAssignments(11)
        ]);

        Assert.That(_notifier.Calls, Is.EqualTo(new[]
        {
            new Call("RevokePropertyAccess", PropertyId: Property, SdkSiteId: 11),
            new Call("RevokeSite", SdkSiteId: 12)
        }));
        Assert.That(rest, Is.EqualTo(new[] { LiveChange.ForAssignments(11) }));
    }

    [Test]
    public async Task AThrowingNotifierOrResolver_NeverEscapes_AndIsLoggedOncePerKind()
    {
        _notifier.Throw = new InvalidOperationException("hub down");
        _resolver.LoadAsync(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThatAsync(() => _publisher.PublishAsync([LiveChange.ForTask(Property, 7), LiveChange.ForEvent(8)]), Throws.Nothing);
            Assert.DoesNotThrow(() => _publisher.ApplyRevocations([LiveChange.ForRevokeProperty(1, Property)]));
        }

        _publisher.Dispatch([LiveChange.ForTask(Property, 7)]);
        await _publisher.WhenIdleAsync();

        Assert.That(_logger.Warnings, Is.EqualTo(3), "one Warning each for resolve, send and revoke");
        Assert.That(_notifier.Calls.Count(c => c.Method == "TaskChanged"), Is.EqualTo(4), "the known hint is still tried every time");
    }

    [Test]
    public async Task Dispatch_ReturnsAtOnce_AndPublishesInTheBackground()
    {
        var gate = new TaskCompletionSource<LiveChangeLookups>(TaskCreationOptions.RunContinuationsAsynchronously);
        _resolver.LoadAsync(default!, default).ReturnsForAnyArgs(gate.Task);

        _publisher.Dispatch([LiveChange.ForEvent(7)]);
        Assert.That(_notifier.Calls, Is.Empty, "the caller does not wait for the lookup");

        gate.SetResult(new LiveChangeLookups([new LiveEventRef(7, 70, Property)]));
        await _publisher.WhenIdleAsync();
        Assert.That(_notifier.Calls.Single(), Is.EqualTo(new Call("TaskChanged", Property, 7, null, null, TaskChangeKinds.Changed)));
    }
}

/// <summary>Counts Warning-level entries.</summary>
internal sealed class CountingLogger<T> : ILogger<T>
{
    private int _warnings;
    public int Warnings => Volatile.Read(ref _warnings);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning) Interlocked.Increment(ref _warnings);
    }
}
