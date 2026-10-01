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
using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// The plugin's DbContext runs under EnableRetryOnFailure, so a transient failure
/// (e.g. a Galera certification failure on COMMIT) makes the execution strategy run
/// a write transaction again. These tests fail the first COMMIT of a write on a
/// context with a retrying strategy and assert the retried write lands exactly once
/// and in full: no half-applied move, no lost grant, no duplicated registration.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalTransactionRetryTests : ChemicalTestBase
{
    private static readonly ChemicalPermissionFlagsModel Handler =
        ChemicalPermissionFlagsModel.None with { View = true, Register = true, Remove = true, Stock = true };

    private sealed class SimulatedTransientException() : Exception("Simulated transient failure on COMMIT.");

    /// <summary>A retrying strategy like EnableRetryOnFailure's, transient only for the simulated failure.</summary>
    private sealed class RetryOnSimulatedFailure(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(50))
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is SimulatedTransientException;
    }

    /// <summary>Throws before the next COMMIT once armed; the transaction is then rolled back.</summary>
    private sealed class FailNextCommit : DbTransactionInterceptor
    {
        public bool Armed { get; set; }

        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                Failures++;
                throw new SimulatedTransientException();
            }

            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private readonly List<BackendConfigurationPnDbContext> _contexts = [];

    [TearDown]
    public async Task DisposeFaultyContexts()
    {
        foreach (var context in _contexts)
        {
            await context.DisposeAsync();
        }

        _contexts.Clear();
    }

    /// <summary>An inventory service over its own context on the fixture database, whose next COMMIT fails once.</summary>
    private (ChemicalInventoryService Sut, FailNextCommit Fault) CreateServiceWithFailingCommit()
    {
        var connectionString = BackendConfigurationPnDbContext!.Database.GetConnectionString()!;
        var fault = new FailNextCommit();
        var options = new DbContextOptionsBuilder<BackendConfigurationPnDbContext>()
            .UseMySql(connectionString, new MariaDbServerVersion(ServerVersion.AutoDetect(connectionString)),
                builder => builder.ExecutionStrategy(dependencies => new RetryOnSimulatedFailure(dependencies)))
            .AddInterceptors(fault)
            .Options;
        var context = new BackendConfigurationPnDbContext(options);
        _contexts.Add(context);

        var sut = new ChemicalInventoryService(context, new ChemicalPermissionService(context),
            new ChemicalRegisterReader(ChemicalsDbContext!), Names, PhotoStorage, ChemicalBase, TimeProvider.System);
        return (sut, fault);
    }

    private static ChemicalStockAmountModel Liters(decimal amount) =>
        new(null, null, amount, ChemicalStockUnitEnum.L, null, null, null);

    private async Task<(int PropertyId, int LocationId, int OtherLocationId, ChemicalCaller Caller, SeededChemical Chemical)> ArrangeAsync()
    {
        var (propertyId, _, caller) = await WorkerWith(Handler);
        await EnableStockAsync(propertyId);
        var location = await CreateLocationAsync(propertyId, sortOrder: 1);
        var other = await CreateLocationAsync(propertyId, sortOrder: 2);
        var chemical = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, $"Retry {Guid.NewGuid():N}", "9-999");
        return (propertyId, location.Id, other.Id, caller, chemical);
    }

    private List<Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.ChemicalPlacement> PlacementsAt(params int[] locationIds) =>
        BackendConfigurationPnDbContext!.ChemicalPlacements.AsNoTracking()
            .Where(p => locationIds.Contains(p.LocationId))
            .OrderBy(p => p.Id)
            .ToList();

    [Test]
    public async Task Move_RetriedAfterAFailedCommit_IsAppliedOnceAndInFull()
    {
        var a = await ArrangeAsync();
        var registered = await CreateInventoryService().RegisterPlacementAsync(a.Caller,
            new ChemicalRegisterPlacementCommand(a.LocationId, a.Chemical.ChemicalId, null, "Hylde 1", Liters(5)));
        var sourceId = registered.Placements.Single().Id;
        var (sut, fault) = CreateServiceWithFailingCommit();
        fault.Armed = true;

        var change = await sut.MovePlacementAsync(a.Caller, new ChemicalMovePlacementCommand(sourceId, a.OtherLocationId, "Reol", null));

        Assert.That(fault.Failures, Is.EqualTo(1), "the first COMMIT must have failed and been retried");
        var placements = PlacementsAt(a.LocationId, a.OtherLocationId);
        Assert.That(placements, Has.Count.EqualTo(2), "the source and exactly one moved copy");
        var open = placements.Where(p => p.RemovedAt == null).ToList();
        Assert.That(open, Has.Count.EqualTo(1), "exactly one open placement after the move");
        Assert.That(open.Single().LocationId, Is.EqualTo(a.OtherLocationId));
        Assert.That(placements.Single(p => p.Id == sourceId).RemovalReason, Is.EqualTo(ChemicalRemovalReasonEnum.Moved));
        Assert.That(change.Placements.Single(p => p.Id != sourceId).Balance, Is.EqualTo(5m));
        Assert.That(change.Placements.Single(p => p.Id == sourceId).Balance, Is.EqualTo(0m));
    }

    [Test]
    public async Task Remove_RetriedAfterAFailedCommit_ClosesThePlacementAndWritesOffOnce()
    {
        var a = await ArrangeAsync();
        var registered = await CreateInventoryService().RegisterPlacementAsync(a.Caller,
            new ChemicalRegisterPlacementCommand(a.LocationId, a.Chemical.ChemicalId, null, "", Liters(3)));
        var placementId = registered.Placements.Single().Id;
        var (sut, fault) = CreateServiceWithFailingCommit();
        fault.Armed = true;

        var change = await sut.RemovePlacementAsync(a.Caller,
            new ChemicalRemovePlacementCommand(placementId, ChemicalRemovalReasonEnum.Used, null, ""));

        Assert.That(fault.Failures, Is.EqualTo(1));
        var placement = PlacementsAt(a.LocationId).Single();
        Assert.That(placement.RemovedAt, Is.Not.Null, "the placement must be closed, not left open with its stock written off");
        Assert.That(change.Placements.Single().Balance, Is.EqualTo(0m));
        Assert.That(change.StockEntries.Count(e => e.Kind == ChemicalStockEntryKindEnum.Consumed), Is.EqualTo(1));
    }

    [Test]
    public async Task Register_RetriedAfterAFailedCommit_CreatesOnePlacementWithOneInitialEntry()
    {
        var a = await ArrangeAsync();
        var (sut, fault) = CreateServiceWithFailingCommit();
        fault.Armed = true;

        var change = await sut.RegisterPlacementAsync(a.Caller,
            new ChemicalRegisterPlacementCommand(a.LocationId, a.Chemical.ChemicalId, null, "", Liters(2)));

        Assert.That(fault.Failures, Is.EqualTo(1));
        Assert.That(PlacementsAt(a.LocationId), Has.Count.EqualTo(1));
        Assert.That(change.StockEntries, Has.Count.EqualTo(1));
        Assert.That(change.Placements.Single().Balance, Is.EqualTo(2m));
    }

    [Test]
    public async Task SetWorkerPermissions_RetriedAfterAFailedCommit_KeepsTheGrant()
    {
        var (propertyId, _, _) = await WorkerWith(Handler);
        var target = await AddWorkerAsync(propertyId);
        await GrantAsync(propertyId, target, ChemicalPermissionFlagsModel.None with { View = true });
        var (sut, fault) = CreateServiceWithFailingCommit();
        fault.Armed = true;
        var admin = ChemicalPermissionFlagsModel.None with { View = true, Admin = true };

        await sut.SetWorkerPermissionsAsync(ChemicalCaller.Web(TestUserId), propertyId,
            [new ChemicalSetWorkerPermissionCommand(target, admin)]);

        Assert.That(fault.Failures, Is.EqualTo(1));
        var row = BackendConfigurationPnDbContext!.ChemicalWorkerPermissions.AsNoTracking()
            .Single(p => p.PropertyId == propertyId && p.WorkerId == target);
        Assert.That(ChemicalPermissionService.ToFlags(row), Is.EqualTo(admin), "the grant must survive the retry");
    }

    [Test]
    public async Task Reorder_RetriedAfterAFailedCommit_StoresTheNewOrder()
    {
        var a = await ArrangeAsync();
        var (sut, fault) = CreateServiceWithFailingCommit();
        fault.Armed = true;
        var caller = ChemicalCaller.Web(TestUserId);

        await sut.ReorderLocationsAsync(caller, a.PropertyId, [a.OtherLocationId, a.LocationId]);

        Assert.That(fault.Failures, Is.EqualTo(1));
        var stored = BackendConfigurationPnDbContext!.ChemicalLocations.AsNoTracking()
            .Where(l => l.PropertyId == a.PropertyId)
            .OrderBy(l => l.SortOrder)
            .Select(l => l.Id)
            .ToList();
        Assert.That(stored, Is.EqualTo(new[] { a.OtherLocationId, a.LocationId }));
    }
}
