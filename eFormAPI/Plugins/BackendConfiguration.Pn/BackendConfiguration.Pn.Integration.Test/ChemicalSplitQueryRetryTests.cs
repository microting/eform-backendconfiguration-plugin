/*
The MIT License (MIT)
Copyright (c) 2007 - 2026 Microting A/S
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System.Data.Common;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using ChemicalsBase.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// ChemicalRegisterReader loads chemicals with a split query. Since EF Core 11 a register sync
/// changing rows between the split statements throws <see cref="DbQueryConcurrencyException"/>,
/// and the reader retries the read up to three times. These tests fail the second statement of
/// a split read (after the first one has run, as the real race does) and assert the retry:
/// a transient failure still returns the full entry, a persistent one surfaces after exactly
/// three attempts, and other failures are not retried.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalSplitQueryRetryTests : ChemicalTestBase
{
    /// <summary>Throws on the second reader of a read attempt while failures remain; counts attempts.</summary>
    private sealed class FailSplitQueryContinuation(Func<Exception> failure) : DbCommandInterceptor
    {
        private int _readersInAttempt;

        public int FailuresRemaining { get; set; }

        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (FailuresRemaining > 0 && ++_readersInAttempt == 2)
            {
                _readersInAttempt = 0;
                FailuresRemaining--;
                Failures++;
                throw failure();
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private readonly List<ChemicalsDbContext> _contexts = [];

    [TearDown]
    public async Task DisposeFaultyContexts()
    {
        foreach (var context in _contexts)
        {
            await context.DisposeAsync();
        }

        _contexts.Clear();
    }

    private (ChemicalRegisterReader Sut, FailSplitQueryContinuation Fault) CreateReaderWithFault(Func<Exception> failure)
    {
        var connectionString = ChemicalsDbContext!.Database.GetConnectionString()!;
        var fault = new FailSplitQueryContinuation(failure);
        var options = new DbContextOptionsBuilder<ChemicalsDbContext>()
            .UseMySql(connectionString, new MariaDbServerVersion(ServerVersion.AutoDetect(connectionString)))
            .AddInterceptors(fault)
            .Options;
        var context = new ChemicalsDbContext(options);
        _contexts.Add(context);
        return (new ChemicalRegisterReader(context), fault);
    }

    private static DbQueryConcurrencyException ConcurrentChange() =>
        new("Simulated concurrent change between split query statements.");

    [TestCase(1)]
    [TestCase(2)]
    public async Task GetByIds_SplitQueryConcurrencyFailure_IsRetriedAndReturnsTheFullEntry(int failures)
    {
        var seeded = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, $"Split {Guid.NewGuid():N}", "7-777",
            barcode: "5701234567892", sdsFileName: "c0ffee");
        var (sut, fault) = CreateReaderWithFault(ConcurrentChange);
        fault.FailuresRemaining = failures;

        var entry = (await sut.GetByIdsAsync([seeded.ChemicalId])).Single();

        Assert.That(fault.Failures, Is.EqualTo(failures), "each failed attempt must have hit the split continuation");
        Assert.That(entry.ChemicalId, Is.EqualTo(seeded.ChemicalId));
        Assert.That(entry.Products.Single().Barcode, Is.EqualTo("5701234567892"), "the retried read must load the collections");
        Assert.That(entry.HazardStatements, Is.Not.Empty);
        Assert.That(entry.ActiveSubstances, Is.Not.Empty);
    }

    [Test]
    public async Task GetByIds_PersistentSplitQueryConcurrencyFailure_ThrowsAfterThreeAttempts()
    {
        var seeded = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, $"Split {Guid.NewGuid():N}", "7-778");
        var (sut, fault) = CreateReaderWithFault(ConcurrentChange);
        fault.FailuresRemaining = int.MaxValue;

        await Assert.ThrowsAsync<DbQueryConcurrencyException>(async () => await sut.GetByIdsAsync([seeded.ChemicalId]));

        Assert.That(fault.Failures, Is.EqualTo(3), "the read is attempted exactly three times");
    }

    [Test]
    public async Task GetByIds_OtherFailure_IsNotRetried()
    {
        var seeded = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, $"Split {Guid.NewGuid():N}", "7-779");
        var (sut, fault) = CreateReaderWithFault(() => new InvalidOperationException("Not a concurrency failure."));
        fault.FailuresRemaining = int.MaxValue;

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.GetByIdsAsync([seeded.ChemicalId]));

        Assert.That(fault.Failures, Is.EqualTo(1), "only DbQueryConcurrencyException is retried");
    }
}
