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

using BackendConfiguration.Pn.Infrastructure.Models.Chemicals;
using BackendConfiguration.Pn.Services.ChemicalInventoryService;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// The derived ledger fields (proto 2026-10-05): balance after each entry, the
/// entry origin, the move counterpart and the removal write-off link. They are
/// computed when entries are read, so rows written before the change carry them too.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalStockLedgerTests : ChemicalTestBase
{
    private static readonly ChemicalPermissionFlagsModel Handler =
        ChemicalPermissionFlagsModel.None with { View = true, Register = true, Remove = true, Stock = true };

    private sealed record Arranged(int PropertyId, int LocationId, int OtherLocationId, int ThirdLocationId,
        ChemicalCaller Caller, SeededChemical Chemical, FixedTimeProvider Clock);

    private async Task<Arranged> ArrangeAsync()
    {
        var property = await CreatePropertyAsync();
        var worker = await AddWorkerAsync(property.Id);
        await GrantAsync(property.Id, worker, Handler);
        await EnableStockAsync(property.Id);
        var location = await CreateLocationAsync(property.Id, "Kemirum", 1);
        var other = await CreateLocationAsync(property.Id, "Lade", 2);
        var third = await CreateLocationAsync(property.Id, "Værksted", 3);
        var chemical = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Ledger product", "4-567");
        // Whole seconds: datetime(6) keeps microseconds, DateTime ticks are 100 ns.
        var now = DateTime.UtcNow;
        var clock = new FixedTimeProvider(new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc));
        return new Arranged(property.Id, location.Id, other.Id, third.Id, ChemicalCaller.App(TestUserId, worker), chemical, clock);
    }

    private static ChemicalStockAmountModel Liters(decimal amount, DateTime? at = null) =>
        new(null, null, amount, ChemicalStockUnitEnum.L, null, null, at);

    private async Task<int> RegisterWithStockAsync(Arranged a, decimal liters)
    {
        var change = await CreateInventoryService(a.Clock).RegisterPlacementAsync(a.Caller,
            new ChemicalRegisterPlacementCommand(a.LocationId, a.Chemical.ChemicalId, null, "", Liters(liters)));
        return change.Placements.Single().Id;
    }

    private static ChemicalStockEntryModel NewestEntry(ChemicalPlacementChangeModel change, int placementId) =>
        change.StockEntries.Where(e => e.PlacementId == placementId).MaxBy(e => e.Id)!;

    [Test]
    public async Task BalanceAfter_ReceiveConsumeAdjust_IsTheRunningBalance()
    {
        var a = await ArrangeAsync();
        var id = await RegisterWithStockAsync(a, 2m);
        var sut = CreateInventoryService(a.Clock);

        var received = await sut.AddStockEntryAsync(a.Caller, new ChemicalAddStockEntryCommand(id, ChemicalStockEntryKindEnum.Received, Liters(3m)));
        Assert.That(NewestEntry(received, id).BalanceAfter, Is.EqualTo(5m), "receive");
        var consumed = await sut.AddStockEntryAsync(a.Caller, new ChemicalAddStockEntryCommand(id, ChemicalStockEntryKindEnum.Consumed, Liters(1.5m)));
        Assert.That(NewestEntry(consumed, id).BalanceAfter, Is.EqualTo(3.5m), "consume");
        var adjusted = await sut.AddStockEntryAsync(a.Caller, new ChemicalAddStockEntryCommand(id, ChemicalStockEntryKindEnum.Adjusted, Liters(1.25m)));
        Assert.That(NewestEntry(adjusted, id).BalanceAfter, Is.EqualTo(1.25m), "adjust: the counted balance");

        var entries = adjusted.StockEntries.OrderBy(e => e.Id).ToList();
        Assert.That(entries.Select(e => e.BalanceAfter), Is.EqualTo(new[] { 2m, 5m, 3.5m, 1.25m }));
        Assert.That(entries.Select(e => e.Origin), Has.All.EqualTo(ChemicalStockEntryOriginEnum.Manual));
        Assert.That(entries.Select(e => e.CounterpartPlacementId), Has.All.Null);
        Assert.That(adjusted.Placements.Single().Balance, Is.EqualTo(entries[^1].BalanceAfter));
        Assert.That(adjusted.Placements.Single().WriteOffEntryId, Is.Null);
    }

    [Test]
    public async Task BalanceAfter_OfABackdatedReceipt_IsTheBalanceWhenItWasWritten()
    {
        var a = await ArrangeAsync();
        var id = await RegisterWithStockAsync(a, 2m);
        var sut = CreateInventoryService(a.Clock);
        await sut.AddStockEntryAsync(a.Caller, new ChemicalAddStockEntryCommand(id, ChemicalStockEntryKindEnum.Consumed, Liters(0.5m)));

        var change = await sut.AddStockEntryAsync(a.Caller,
            new ChemicalAddStockEntryCommand(id, ChemicalStockEntryKindEnum.Received, Liters(1m, a.Clock.UtcNow.AddHours(-1))));

        // Entries are listed by `at`, the balance follows write (id) order.
        Assert.That(change.StockEntries.Select(e => (e.Kind, e.BalanceAfter)), Is.EqualTo(new[]
        {
            (ChemicalStockEntryKindEnum.Received, 2.5m),
            (ChemicalStockEntryKindEnum.Received, 2m),
            (ChemicalStockEntryKindEnum.Consumed, 1.5m),
        }));
    }

    [Test]
    public async Task Move_Partial_LinksBothEntries_AndEachSideCarriesItsOwnBalance()
    {
        var a = await ArrangeAsync();
        var source = await RegisterWithStockAsync(a, 2m);

        var change = await CreateInventoryService(a.Clock).MovePlacementAsync(a.Caller,
            new ChemicalMovePlacementCommand(source, a.OtherLocationId, "", 0.5m));

        var target = change.Placements.Single(p => p.Id != source).Id;
        var movedOut = change.StockEntries.Single(e => e.Kind == ChemicalStockEntryKindEnum.MovedOut);
        var movedIn = change.StockEntries.Single(e => e.Kind == ChemicalStockEntryKindEnum.MovedIn);
        Assert.That((movedOut.PlacementId, movedOut.CounterpartPlacementId, movedOut.BalanceAfter, movedOut.Origin),
            Is.EqualTo((source, (int?)target, 1.5m, ChemicalStockEntryOriginEnum.Move)));
        Assert.That((movedIn.PlacementId, movedIn.CounterpartPlacementId, movedIn.BalanceAfter, movedIn.Origin),
            Is.EqualTo((target, (int?)source, 0.5m, ChemicalStockEntryOriginEnum.Move)));
        Assert.That(change.Placements.Select(p => p.WriteOffEntryId), Has.All.Null);
    }

    [Test]
    public async Task Move_Full_LinksBothEntries_AndTheClosedSourceHasNoWriteOff()
    {
        var a = await ArrangeAsync();
        var source = await RegisterWithStockAsync(a, 2m);

        var change = await CreateInventoryService(a.Clock).MovePlacementAsync(a.Caller,
            new ChemicalMovePlacementCommand(source, a.OtherLocationId, "", null));

        var closed = change.Placements.Single(p => p.Id == source);
        var target = change.Placements.Single(p => p.Id != source).Id;
        var movedOut = change.StockEntries.Single(e => e.Kind == ChemicalStockEntryKindEnum.MovedOut);
        var movedIn = change.StockEntries.Single(e => e.Kind == ChemicalStockEntryKindEnum.MovedIn);
        Assert.That(closed.RemovalReason, Is.EqualTo(ChemicalRemovalReasonEnum.Moved));
        Assert.That(closed.WriteOffEntryId, Is.Null);
        Assert.That((movedOut.CounterpartPlacementId, movedOut.BalanceAfter), Is.EqualTo(((int?)target, 0m)));
        Assert.That((movedIn.CounterpartPlacementId, movedIn.BalanceAfter), Is.EqualTo(((int?)source, 2m)));
    }

    [Test]
    public async Task Move_TwoPartialMovesInTheSameSecond_EachMovedOutLinksItsOwnTarget_EvenWhenOnlyTheSourceIsReturned()
    {
        var a = await ArrangeAsync();
        var source = await RegisterWithStockAsync(a, 2m);
        var sut = CreateInventoryService(a.Clock);

        var first = await sut.MovePlacementAsync(a.Caller, new ChemicalMovePlacementCommand(source, a.OtherLocationId, "", 0.5m));
        var second = await sut.MovePlacementAsync(a.Caller, new ChemicalMovePlacementCommand(source, a.ThirdLocationId, "", 0.25m));
        var toOther = first.Placements.Single(p => p.LocationId == a.OtherLocationId).Id;
        var toThird = second.Placements.Single(p => p.LocationId == a.ThirdLocationId).Id;

        // Only the source is in this change, so its targets are looked up outside the loaded set.
        var consumed = await sut.AddStockEntryAsync(a.Caller,
            new ChemicalAddStockEntryCommand(source, ChemicalStockEntryKindEnum.Consumed, Liters(0.25m)));

        Assert.That(consumed.Placements.Select(p => p.Id), Is.EqualTo(new[] { source }));
        Assert.That(consumed.StockEntries.OrderBy(e => e.Id).Select(e => (e.Kind, e.CounterpartPlacementId, e.BalanceAfter)), Is.EqualTo(new[]
        {
            (ChemicalStockEntryKindEnum.Received, (int?)null, 2m),
            (ChemicalStockEntryKindEnum.MovedOut, (int?)toOther, 1.5m),
            (ChemicalStockEntryKindEnum.MovedOut, (int?)toThird, 1.25m),
            (ChemicalStockEntryKindEnum.Consumed, (int?)null, 1m),
        }));
    }

    [TestCase(ChemicalRemovalReasonEnum.Used, ChemicalStockEntryKindEnum.Consumed)]
    [TestCase(ChemicalRemovalReasonEnum.Disposed, ChemicalStockEntryKindEnum.Adjusted)]
    public async Task Remove_WithABalance_LinksTheWriteOffEntry(ChemicalRemovalReasonEnum reason, ChemicalStockEntryKindEnum writeOffKind)
    {
        var a = await ArrangeAsync();
        var id = await RegisterWithStockAsync(a, 2m);
        var sut = CreateInventoryService(a.Clock);
        await sut.AddStockEntryAsync(a.Caller, new ChemicalAddStockEntryCommand(id, writeOffKind, Liters(writeOffKind == ChemicalStockEntryKindEnum.Consumed ? 0.5m : 1.5m)));

        var change = await sut.RemovePlacementAsync(a.Caller, new ChemicalRemovePlacementCommand(id, reason, null, ""));

        var entries = change.StockEntries.OrderBy(e => e.Id).ToList();
        var writeOff = entries[^1];
        Assert.That(change.Placements.Single().WriteOffEntryId, Is.EqualTo(writeOff.Id));
        Assert.That((writeOff.Kind, writeOff.Amount, writeOff.BalanceAfter, writeOff.Origin),
            Is.EqualTo((writeOffKind, -1.5m, 0m, ChemicalStockEntryOriginEnum.RemovalWriteOff)));
        Assert.That(entries.SkipLast(1).Select(e => e.Origin), Has.All.EqualTo(ChemicalStockEntryOriginEnum.Manual),
            "the same kind written by hand before the removal is not the write-off");
    }

    [Test]
    public async Task Remove_WithAZeroBalanceOrNoStock_HasNoWriteOff()
    {
        var a = await ArrangeAsync();
        var emptied = await RegisterWithStockAsync(a, 2m);
        var sut = CreateInventoryService(a.Clock);
        await sut.AddStockEntryAsync(a.Caller, new ChemicalAddStockEntryCommand(emptied, ChemicalStockEntryKindEnum.Consumed,
            Liters(2m, a.Clock.UtcNow.AddMinutes(-10))));
        var noStock = (await sut.RegisterPlacementAsync(a.Caller,
            new ChemicalRegisterPlacementCommand(a.LocationId, a.Chemical.ChemicalId, null, "", null))).Placements.Single().Id;

        var removedEmpty = await sut.RemovePlacementAsync(a.Caller, new ChemicalRemovePlacementCommand(emptied, ChemicalRemovalReasonEnum.Used, null, ""));
        var removedNoStock = await sut.RemovePlacementAsync(a.Caller, new ChemicalRemovePlacementCommand(noStock, ChemicalRemovalReasonEnum.Disposed, null, ""));

        Assert.That(removedEmpty.Placements.Single().WriteOffEntryId, Is.Null);
        Assert.That(removedEmpty.StockEntries.Select(e => e.Origin), Has.All.EqualTo(ChemicalStockEntryOriginEnum.Manual));
        Assert.That(removedEmpty.StockEntries.OrderBy(e => e.Id).Select(e => e.BalanceAfter), Is.EqualTo(new[] { 2m, 0m }));
        Assert.That(removedNoStock.Placements.Single().WriteOffEntryId, Is.Null);
        Assert.That(removedNoStock.StockEntries, Is.Empty);
    }

    [Test]
    public async Task Sync_RowsWrittenBeforeThisChange_CarryBalanceAfterAndLinks_InTheAppAndTheWebInventory()
    {
        var a = await ArrangeAsync();
        var t0 = a.Clock.UtcNow.AddDays(-3);
        var removedAt = a.Clock.UtcNow.AddDays(-2);
        var movedAt = a.Clock.UtcNow.AddDays(-1);

        // Written straight to the tables, as by the server before the derived fields existed.
        var removed = await SeedPlacementAsync(a.LocationId, a.Chemical.ChemicalId, t0, removedAt: removedAt, reason: ChemicalRemovalReasonEnum.Disposed);
        await SeedEntryAsync(removed.Id, ChemicalStockEntryKindEnum.Received, 3m, t0);
        var writeOff = await SeedEntryAsync(removed.Id, ChemicalStockEntryKindEnum.Adjusted, -3m, removedAt);
        var source = await SeedPlacementAsync(a.LocationId, a.Chemical.ChemicalId, t0);
        await SeedEntryAsync(source.Id, ChemicalStockEntryKindEnum.Received, 2m, t0);
        var target = await SeedPlacementAsync(a.OtherLocationId, a.Chemical.ChemicalId, movedAt, movedFrom: source.Id);
        var movedOut = await SeedEntryAsync(source.Id, ChemicalStockEntryKindEnum.MovedOut, -0.5m, movedAt);
        var movedIn = await SeedEntryAsync(target.Id, ChemicalStockEntryKindEnum.MovedIn, 0.5m, movedAt);

        var app = await CreateInventoryService(a.Clock).GetInventoryAsync(a.Caller, "");
        var web = await CreateInventoryService(a.Clock).GetPropertyInventoryAsync(ChemicalCaller.Web(TestUserId), a.PropertyId);

        foreach (var (name, inventory) in new[] { ("app", app), ("web", web) })
        {
            var byId = inventory.StockEntries.ToDictionary(e => e.Id);
            Assert.That(inventory.Placements.Single(p => p.Id == removed.Id).WriteOffEntryId, Is.EqualTo(writeOff.Id), name);
            Assert.That(inventory.Placements.Where(p => p.Id != removed.Id).Select(p => p.WriteOffEntryId), Has.All.Null, name);
            Assert.That((byId[writeOff.Id].BalanceAfter, byId[writeOff.Id].Origin),
                Is.EqualTo((0m, ChemicalStockEntryOriginEnum.RemovalWriteOff)), name);
            Assert.That((byId[movedOut.Id].BalanceAfter, byId[movedOut.Id].CounterpartPlacementId, byId[movedOut.Id].Origin),
                Is.EqualTo((1.5m, (int?)target.Id, ChemicalStockEntryOriginEnum.Move)), name);
            Assert.That((byId[movedIn.Id].BalanceAfter, byId[movedIn.Id].CounterpartPlacementId, byId[movedIn.Id].Origin),
                Is.EqualTo((0.5m, (int?)source.Id, ChemicalStockEntryOriginEnum.Move)), name);
        }
    }

    private async Task<ChemicalPlacement> SeedPlacementAsync(int locationId, int chemicalId, DateTime registeredAt,
        DateTime? removedAt = null, ChemicalRemovalReasonEnum? reason = null, int? movedFrom = null)
    {
        var placement = new ChemicalPlacement
        {
            LocationId = locationId, ChemicalId = chemicalId, PlacementNote = string.Empty,
            RegisteredByUserId = TestUserId, RegisteredAt = registeredAt, MovedFromPlacementId = movedFrom,
            RemovedAt = removedAt, RemovedByUserId = removedAt.HasValue ? TestUserId : null, RemovalReason = reason,
            CreatedByUserId = TestUserId, UpdatedByUserId = TestUserId,
        };
        await placement.Create(BackendConfigurationPnDbContext!);
        return placement;
    }

    private async Task<ChemicalStockEntry> SeedEntryAsync(int placementId, ChemicalStockEntryKindEnum kind, decimal amount, DateTime at)
    {
        var entry = new ChemicalStockEntry
        {
            PlacementId = placementId, Kind = kind, Unit = ChemicalStockUnitEnum.L, Amount = amount, ByUserId = TestUserId, At = at,
            CreatedByUserId = TestUserId, UpdatedByUserId = TestUserId,
        };
        await entry.Create(BackendConfigurationPnDbContext!);
        return entry;
    }
}
