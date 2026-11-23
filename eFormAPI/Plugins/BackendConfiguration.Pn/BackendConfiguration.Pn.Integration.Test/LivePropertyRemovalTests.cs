#nullable enable

namespace BackendConfiguration.Pn.Integration.Test;

using BackendConfiguration.Pn.Services.LiveChanges;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Call = RecordingLiveNotifier.Call;

/// <summary>
/// Package 11b: a property removed through any plugin write path takes every
/// connection out of its group, workers and web calendar users alike (web
/// members have no PropertyWorker row to revoke by). Seen by the SaveChanges
/// interceptor on the real, pooled plugin context (MariaDB).
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class LivePropertyRemovalTests : TestBaseSetup
{
    private LiveChangeTestHost _host = null!;

    [SetUp]
    public void SetUpHost() => _host = new LiveChangeTestHost(ConnectionStringFor("420_eform-backend-configuration-plugin"),
        ConnectionStringFor("420_eform-angular-items-planning-plugin"), null);

    [TearDown]
    public void TearDownHost() => _host.Dispose();

    private static BackendConfigurationPnDbContext Bc(IServiceProvider sp) => sp.GetRequiredService<BackendConfigurationPnDbContext>();

    private async Task<Property> PropertyAsync()
    {
        var property = new Property
        {
            Name = $"LiveRemoval-{Guid.NewGuid():N}", ItemPlanningTagId = 0,
            WorkflowState = Constants.WorkflowStates.Created, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await property.Create(BackendConfigurationPnDbContext!);
        return property;
    }

    [Test]
    public async Task RemovingAProperty_RevokesItForEveryone_AtCommit_AndOnlyOnce()
    {
        var property = await PropertyAsync();

        var calls = await _host.InRequestAsync(async sp =>
        {
            var row = await Bc(sp).Properties.SingleAsync(x => x.Id == property.Id);
            await row.Delete(Bc(sp));
            Assert.That(_host.Notifier.Calls, Is.EqualTo(new[] { new Call("RevokePropertyForEveryone", PropertyId: property.Id) }),
                "the revoke is not deferred to the end of the request");
            // A second delete, and an edit of the removed row, change nothing.
            await row.Delete(Bc(sp));
            row.Name += " (edited)";
            await row.Update(Bc(sp));
        });

        Assert.That(calls, Is.EqualTo(new[] { new Call("RevokePropertyForEveryone", PropertyId: property.Id) }));
    }

    /// <summary>
    /// Review round 1: an admin removes property P while nobody is connected;
    /// before that save commits, a web user connects and joins P (P is not
    /// removed yet, so JoinCalendarProperty passes). The commit must still take
    /// it out of property-P: with no audience the save keeps the revocation
    /// rows (PropertyWorker AND Property), not only PropertyWorker.
    /// </summary>
    [Test]
    public async Task ARemovalStartedWithNobodyConnected_StillRevokes_AWebUserWhoJoinedBeforeTheCommit()
    {
        _host.Dispose();
        _host = new LiveChangeTestHost(ConnectionStringFor("420_eform-backend-configuration-plugin"),
            ConnectionStringFor("420_eform-angular-items-planning-plugin"), null, connected: false);
        var property = await PropertyAsync();
        var group = Hubs.LiveHubContract.PropertyGroup(property.Id);

        var calls = await _host.InBackgroundAsync(async sp =>
        {
            var db = Bc(sp);
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                var row = await db.Properties.SingleAsync(x => x.Id == property.Id);
                await row.Delete(db); // saved while nobody is connected
                // A web user (site 0) connects and joins P before the commit.
                _host.Registry.TryConnect("web", 6, DateTimeOffset.MaxValue, () => { }, 10);
                _host.Registry.Joined("web", group);
                await tx.CommitAsync();
            });
        });

        Assert.That(calls, Does.Contain(new Call("RevokePropertyForEveryone", PropertyId: property.Id)));
    }

    [Test]
    public async Task EditingAProperty_RevokesNothing()
    {
        var property = await PropertyAsync();

        var calls = await _host.InRequestAsync(async sp =>
        {
            var row = await Bc(sp).Properties.SingleAsync(x => x.Id == property.Id);
            row.Name += " (edited)";
            await row.Update(Bc(sp));
        });

        Assert.That(calls.Where(c => c.Method == "RevokePropertyForEveryone"), Is.Empty);
    }
}
