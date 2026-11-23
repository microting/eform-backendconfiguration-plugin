#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Hubs;
using BackendConfiguration.Pn.Services.LiveChanges;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using NSubstitute;
using NUnit.Framework;
using BcProperty = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.Property;
using Call = BackendConfiguration.Pn.Test.Services.LiveChanges.RecordingLiveNotifier.Call;

namespace BackendConfiguration.Pn.Test.Services.LiveChanges;

/// <summary>
/// Package 11b: the two new revocations (a property for everyone, a user's
/// connections) run at commit like the existing ones, and a removed Property
/// row asks for the first.
/// </summary>
[TestFixture]
public class LiveWebUserRevocationTests
{
    [Test]
    public void ARemovedPropertyRow_AsksToRevokeThePropertyForEveryone_AnEditOrAnAlreadyRemovedRowDoesNot()
    {
        var removed = new BcProperty { Id = 42, WorkflowState = Constants.WorkflowStates.Removed };
        var edited = new BcProperty { Id = 43, WorkflowState = Constants.WorkflowStates.Created };
        var stillRemoved = new BcProperty { Id = 44, WorkflowState = Constants.WorkflowStates.Removed };
        var deleted = new BcProperty { Id = 45, WorkflowState = Constants.WorkflowStates.Created };

        var changes = LiveChangeCapture.ToChanges(
        [
            new CapturedEntry(removed, EntityState.Modified, Constants.WorkflowStates.Created),
            new CapturedEntry(edited, EntityState.Modified, Constants.WorkflowStates.Created),
            new CapturedEntry(stillRemoved, EntityState.Modified, Constants.WorkflowStates.Removed),
            new CapturedEntry(deleted, EntityState.Deleted, Constants.WorkflowStates.Created),
        ]);

        Assert.That(changes, Is.EqualTo(new[]
        {
            LiveChange.ForRevokePropertyForEveryone(42),
            LiveChange.ForRevokePropertyForEveryone(45),
        }));
    }

    [Test]
    public async Task TheNewRevocations_RunAtCommit_BeforeAnyHint()
    {
        var notifier = new RecordingLiveNotifier();
        var resolver = Substitute.For<ILiveChangeResolver>();
        resolver.LoadAsync(default!, default).ReturnsForAnyArgs(LiveChangeLookups.Empty);
        var publisher = new LiveChangePublisher(notifier, resolver, LiveChangePublisherTests.ConnectedRegistry(),
            new LiveChangeWarnings(), new CountingLogger<LiveChangePublisher>());

        var rest = publisher.ApplyRevocations(
        [
            LiveChange.ForTask(42),
            LiveChange.ForRevokePropertyForEveryone(42),
            LiveChange.ForRevokeUser(7),
        ]);

        Assert.That(notifier.Calls, Is.EqualTo(new[]
        {
            new Call("RevokePropertyForEveryone", PropertyId: 42),
            new Call("RevokeUser", UserId: 7),
        }));
        Assert.That(rest, Is.EqualTo(new[] { LiveChange.ForTask(42) }));
        await publisher.WhenIdleAsync();
    }

    [Test]
    public void TheCollector_RevokesAUser_AtOnce()
    {
        var notifier = new RecordingLiveNotifier();
        var publisher = new LiveChangePublisher(notifier, Substitute.For<ILiveChangeResolver>(),
            LiveChangePublisherTests.ConnectedRegistry(), new LiveChangeWarnings(), new CountingLogger<LiveChangePublisher>());
        using var collector = new LiveChangeCollector(Substitute.For<IServiceProvider>(), publisher, new LiveChangeWarnings(),
            new CountingLogger<LiveChangeCollector>());

        collector.RevokeUser(7);

        Assert.That(notifier.Calls, Is.EqualTo(new[] { new Call("RevokeUser", UserId: 7) }));
    }
}
