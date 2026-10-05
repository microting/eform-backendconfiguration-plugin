using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
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

    /// <summary>A retrying strategy like EnableRetryOnFailure's, treating a TimeoutException as transient.</summary>
    private sealed class RetryOnTimeout(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(50))
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TimeoutException;
    }

    /// <summary>Throws a transient-looking TimeoutException before the first COMMIT; later commits go through.</summary>
    private sealed class FailFirstCommit : DbTransactionInterceptor
    {
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Failures == 0)
            {
                Failures++;
                throw new TimeoutException("Simulated transient failure on COMMIT.");
            }
            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    [Test]
    public async Task CommitFailure_DoesNotRerunWork_AndSurfacesConflict()
    {
        await SeedTreeAsync();
        var fault = new FailFirstCommit();
        await using var db = NewContext(dependencies => new RetryOnTimeout(dependencies), fault);
        var runs = 0;

        var ex = await Assert.ThrowsAsync<TailBiteConflictException>(() => new TailBitePropertyLock(db).RunLockedAsync(PropertyId, async () =>
        {
            runs++;
            await new TailBiteLocation { PropertyId = PropertyId, ParentId = RootId, Name = "Once", QrCode = TailBiteDefaults.NewQrCode() }.Create(db);
        }));

        Assert.That(runs, Is.EqualTo(1));
        Assert.That(fault.Failures, Is.EqualTo(1));
        Assert.That(ex!.Message, Is.EqualTo("The change may have been saved; refresh and check before retrying."));
        Assert.That(ex.InnerException, Is.TypeOf<TimeoutException>());
        Assert.That(db.ChangeTracker.Entries().Any(), Is.False);
    }

    [Test]
    public async Task TransientFailureBeforeCommit_IsRetried()
    {
        await SeedTreeAsync();
        await using var db = NewContext(dependencies => new RetryOnTimeout(dependencies));
        var runs = 0;

        await new TailBitePropertyLock(db).RunLockedAsync(PropertyId, async () =>
        {
            runs++;
            await new TailBiteLocation { PropertyId = PropertyId, ParentId = RootId, Name = $"Try {runs}", QrCode = TailBiteDefaults.NewQrCode() }.Create(db);
            if (runs == 1) throw new TimeoutException("Simulated transient failure before COMMIT.");
        });

        Assert.That(runs, Is.EqualTo(2));
        var names = await Db.TailBiteLocations.AsNoTracking().Where(l => l.Name.StartsWith("Try ")).Select(l => l.Name).ToListAsync();
        Assert.That(names, Is.EqualTo(new[] { "Try 2" }));
    }
}
