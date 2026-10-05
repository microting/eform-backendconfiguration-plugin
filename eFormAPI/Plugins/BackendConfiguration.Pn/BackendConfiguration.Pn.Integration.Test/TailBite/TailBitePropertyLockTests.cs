using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

[TestFixture]
public class TailBitePropertyLockTests : TailBiteTestBase
{
    [Test]
    public async Task UnknownProperty_ThrowsNotFound()
    {
        var sut = new TailBitePropertyLock(Db);
        await Assert.ThrowsAsync<TailBiteNotFoundException>(() => sut.RunLockedAsync(PropertyId, () => Task.FromResult(1)));
    }

    [Test]
    public async Task WorkThrowing_RollsBack_AndLeavesChangeTrackerClean()
    {
        await SeedTreeAsync();
        var sut = new TailBitePropertyLock(Db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunLockedAsync(PropertyId, async () =>
        {
            await new TailBiteLocation { PropertyId = PropertyId, ParentId = RootId, Name = "Ghost", QrCode = TailBiteDefaults.NewQrCode() }.Create(Db);
            throw new InvalidOperationException("boom");
        }));
        Assert.That(Db.ChangeTracker.Entries().Any(), Is.False);
        Assert.That(await Db.TailBiteLocations.AsNoTracking().AnyAsync(l => l.Name == "Ghost"), Is.False);
    }

    [Test]
    public async Task ConcurrentCallers_AreSerialized()
    {
        await SeedTreeAsync();
        await using var dbB = CreateFreshBackendConfigurationDbContext();
        var events = new ConcurrentQueue<string>();
        var aInside = new TaskCompletionSource();
        var releaseA = new TaskCompletionSource();

        var a = new TailBitePropertyLock(Db).RunLockedAsync(PropertyId, async () =>
        {
            events.Enqueue("A-enter");
            aInside.SetResult();
            await releaseA.Task;
            events.Enqueue("A-exit");
        });
        await aInside.Task;

        var b = new TailBitePropertyLock(dbB).RunLockedAsync(PropertyId, () =>
        {
            events.Enqueue("B-enter");
            return Task.CompletedTask;
        });
        // B has been started and must be blocked on the row lock while A still holds it.
        await Task.Delay(1000);
        Assert.That(b.IsCompleted, Is.False);
        releaseA.SetResult();
        await Task.WhenAll(a, b);

        Assert.That(events.ToArray(), Is.EqualTo(new[] { "A-enter", "A-exit", "B-enter" }));
    }
}
