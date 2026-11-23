#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.LiveChanges;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Call = BackendConfiguration.Pn.Test.Services.LiveChanges.RecordingLiveNotifier.Call;

namespace BackendConfiguration.Pn.Test.Services.LiveChanges;

/// <summary>
/// The scoped collector (<see cref="ILiveChanges"/>): changes wait for the end
/// of the scope and go out as one flush; revocations go out at once; a read
/// path announces nothing but revocations; a background batch
/// (<see cref="LiveChangeScope"/>) flushes when it ends.
/// </summary>
[TestFixture]
public class LiveChangeCollectorTests
{
    private RecordingLiveNotifier _notifier = null!;
    private LiveChangePublisher _publisher = null!;
    private ServiceProvider _root = null!;

    [SetUp]
    public void SetUp()
    {
        _notifier = new RecordingLiveNotifier();
        var resolver = Substitute.For<ILiveChangeResolver>();
        resolver.LoadAsync(default!, default).ReturnsForAnyArgs(LiveChangeLookups.Empty);
        _publisher = new LiveChangePublisher(_notifier, resolver, LiveChangePublisherTests.ConnectedRegistry(),
            new LiveChangeWarnings(),
            NullLogger<LiveChangePublisher>.Instance);
        _root = new ServiceCollection()
            .AddSingleton(_publisher)
            .AddSingleton(new LiveChangeWarnings())
            .AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>))
            .AddScoped<LiveChangeCollector>()
            .AddScoped<ILiveChanges>(sp => sp.GetRequiredService<LiveChangeCollector>())
            .BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() => _root.Dispose();

    [Test]
    public async Task Changes_WaitForTheEndOfTheScope_AndGoOutAsOneFlush()
    {
        using (var scope = _root.CreateScope())
        {
            var live = scope.ServiceProvider.GetRequiredService<ILiveChanges>();
            live.TaskChanged(1, 7);
            live.TaskChanged(1, 7);
            live.AssignmentsChanged(11);
            live.AssignmentsChanged(11);
            await _publisher.WhenIdleAsync();
            Assert.That(_notifier.Calls, Is.Empty, "nothing before the scope ends");
        }

        await _publisher.WhenIdleAsync();
        Assert.That(_notifier.Calls, Is.EqualTo(new[]
        {
            new Call("TaskChanged", 1, 7, null, null, TaskChangeKinds.Changed),
            new Call("AssignmentsChanged", SdkSiteId: 11)
        }));
    }

    [Test]
    public void Revocations_GoOutAtOnce()
    {
        using var scope = _root.CreateScope();
        var live = scope.ServiceProvider.GetRequiredService<ILiveChanges>();

        live.RevokePropertyAccess(11, 1);
        live.RevokeSite(12);

        Assert.That(_notifier.Calls, Is.EqualTo(new[]
        {
            new Call("RevokePropertyAccess", PropertyId: 1, SdkSiteId: 11),
            new Call("RevokeSite", SdkSiteId: 12)
        }));
    }

    [Test]
    public async Task AReadPath_AnnouncesNothing_ButRevocations_AndEndsWithItsHandle()
    {
        using (var scope = _root.CreateScope())
        {
            var live = scope.ServiceProvider.GetRequiredService<ILiveChanges>();
            using (live.BeginReadPath())
            {
                using (live.BeginReadPath())
                {
                    live.TaskChanged(1, 7);
                }

                live.AssignmentsChanged(11);
                live.RevokeSite(12);
            }

            live.TaskChanged(2, 8);
        }

        await _publisher.WhenIdleAsync();
        Assert.That(_notifier.Calls, Is.EqualTo(new[]
        {
            new Call("RevokeSite", SdkSiteId: 12),
            new Call("TaskChanged", 2, 8, null, null, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task AReadPath_IsPerAsyncFlow_AParallelFlowOfTheSameScopeStillAnnounces()
    {
        using (var scope = _root.CreateScope())
        {
            var live = scope.ServiceProvider.GetRequiredService<ILiveChanges>();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var parallel = Task.Run(async () =>
            {
                await start.Task;
                live.TaskChanged(2, 8);
            });

            using (live.BeginReadPath())
            {
                start.SetResult();
                await parallel;
                live.TaskChanged(1, 7);
            }
        }

        await _publisher.WhenIdleAsync();
        Assert.That(_notifier.Calls, Is.EqualTo(new[]
        {
            new Call("TaskChanged", 2, 8, null, null, TaskChangeKinds.Changed)
        }));
    }

    [Test]
    public async Task ABackgroundBatch_FlushesWhenItEnds_BeforeTheScope()
    {
        using var scope = _root.CreateScope();
        using (LiveChangeScope.Begin(scope.ServiceProvider))
        {
            Assert.That(LiveChangeScope.Current, Is.SameAs(scope.ServiceProvider.GetRequiredService<LiveChangeCollector>()));
            scope.ServiceProvider.GetRequiredService<ILiveChanges>().TaskChanged(1, 7);
        }

        Assert.That(LiveChangeScope.Current, Is.Null);
        await _publisher.WhenIdleAsync();
        Assert.That(_notifier.Calls.Single(), Is.EqualTo(new Call("TaskChanged", 1, 7, null, null, TaskChangeKinds.Changed)));
    }

    [Test]
    public async Task AfterTheScope_LateChangesAreSentOnTheirOwn()
    {
        LiveChangeCollector collector;
        using (var scope = _root.CreateScope())
        {
            collector = scope.ServiceProvider.GetRequiredService<LiveChangeCollector>();
        }

        Assert.That(collector.IsClosed, Is.True);
        collector.TaskChanged(1, 7);
        await _publisher.WhenIdleAsync();
        Assert.That(_notifier.Calls.Single(), Is.EqualTo(new Call("TaskChanged", 1, 7, null, null, TaskChangeKinds.Changed)));
    }

    [Test]
    public void AThrowingNotifier_NeverReachesTheCaller()
    {
        _notifier.Throw = new InvalidOperationException("hub down");
        using var scope = _root.CreateScope();
        var live = scope.ServiceProvider.GetRequiredService<ILiveChanges>();

        Assert.DoesNotThrow(() =>
        {
            live.RevokeSite(1);
            live.RevokePropertyAccess(1, 2);
            live.TaskChanged(1);
        });
        Assert.DoesNotThrow(() => scope.Dispose());
    }
}
