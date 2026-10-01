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
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalPlacementsTests : ChemicalTestBase
{
    private static readonly ChemicalPermissionFlagsModel Handler =
        ChemicalPermissionFlagsModel.None with { View = true, Register = true, Remove = true, Stock = true };

    private sealed record Arranged(int PropertyId, int LocationId, int OtherLocationId, ChemicalCaller Caller, SeededChemical Chemical, FixedTimeProvider Clock);

    private async Task<Arranged> ArrangeAsync(ChemicalPermissionFlagsModel flags, bool stock = true)
    {
        var property = await CreatePropertyAsync();
        var worker = await AddWorkerAsync(property.Id);
        await GrantAsync(property.Id, worker, flags);
        if (stock)
        {
            await EnableStockAsync(property.Id);
        }

        var location = await CreateLocationAsync(property.Id, "Kemirum", 1);
        var other = await CreateLocationAsync(property.Id, "Lade", 2);
        var chemical = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Roundup Flex", "1-234");
        // Whole seconds: datetime(6) keeps microseconds, DateTime.UtcNow has 100 ns ticks,
        // and the assertions compare the clock with values read back from MariaDB.
        var now = DateTime.UtcNow;
        var clock = new FixedTimeProvider(new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc));
        return new Arranged(property.Id, location.Id, other.Id, ChemicalCaller.App(TestUserId, worker), chemical, clock);
    }

    private static ChemicalStockAmountModel Liters(decimal amount) => new(null, null, amount, ChemicalStockUnitEnum.L, null, null, null);

    private async Task<int> RegisterWithStockAsync(Arranged a, decimal liters)
    {
        var change = await CreateInventoryService(a.Clock).RegisterPlacementAsync(a.Caller,
            new ChemicalRegisterPlacementCommand(a.LocationId, a.Chemical.ChemicalId, a.Chemical.ProductId, "Hylde 2", Liters(liters)));
        return change.Placements.Single().Id;
    }

    [Test]
    public async Task Register_CreatesAnOpenPlacement_WithRegisterDetailsAndObservedStatus()
    {
        var a = await ArrangeAsync(Handler, stock: false);

        var change = await CreateInventoryService(a.Clock).RegisterPlacementAsync(a.Caller,
            new ChemicalRegisterPlacementCommand(a.LocationId, a.Chemical.ChemicalId, null, "  Hylde 2  ", null));

        var placement = change.Placements.Single();
        Assert.That(placement.PropertyId, Is.EqualTo(a.PropertyId));
        Assert.That(placement.PlacementNote, Is.EqualTo("Hylde 2"));
        Assert.That(placement.RemovedAt, Is.Null);
        Assert.That(placement.RegisteredAt, Is.EqualTo(a.Clock.UtcNow));
        Assert.That(placement.RegisteredByName, Is.EqualTo($"User {TestUserId}"));
        Assert.That(placement.Unit, Is.Null);
        Assert.That(change.RegisterEntries.Single().ChemicalId, Is.EqualTo(a.Chemical.ChemicalId));
        var stored = await BackendConfigurationPnDbContext!.ChemicalPlacements.AsNoTracking().SingleAsync(p => p.Id == placement.Id);
        Assert.That(stored.ObservedStatus, Is.EqualTo(5));
    }

    [Test]
    public async Task Register_WithInitialStock_NeedsStockEnabled()
    {
        var a = await ArrangeAsync(Handler, stock: false);
        Assert.That(async () => await CreateInventoryService(a.Clock).RegisterPlacementAsync(a.Caller,
                new ChemicalRegisterPlacementCommand(a.LocationId, a.Chemical.ChemicalId, null, "", Liters(1))),
            Throws.InstanceOf<ChemicalPreconditionException>());

        await EnableStockAsync(a.PropertyId);
        var id = await RegisterWithStockAsync(a, 2.5m);
        var entry = await BackendConfigurationPnDbContext!.ChemicalStockEntries.AsNoTracking().SingleAsync(e => e.PlacementId == id);
        Assert.That(entry.Kind, Is.EqualTo(ChemicalStockEntryKindEnum.Received));
        Assert.That(entry.Amount, Is.EqualTo(2.5m));
    }

    [Test]
    public async Task Register_RejectsArchivedLocationsUnknownChemicalsAndForeignProducts()
    {
        var a = await ArrangeAsync(Handler);
        var other = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Other", "1-999");
        var sut = CreateInventoryService(a.Clock);
        var archived = await CreateLocationAsync(a.PropertyId);
        await archived.Delete(BackendConfigurationPnDbContext!);

        Assert.That(async () => await sut.RegisterPlacementAsync(a.Caller, new ChemicalRegisterPlacementCommand(archived.Id, a.Chemical.ChemicalId, null, "", null)),
            Throws.InstanceOf<ChemicalNotFoundException>());
        Assert.That(async () => await sut.RegisterPlacementAsync(a.Caller, new ChemicalRegisterPlacementCommand(a.LocationId, int.MaxValue, null, "", null)),
            Throws.InstanceOf<ChemicalNotFoundException>());
        Assert.That(async () => await sut.RegisterPlacementAsync(a.Caller, new ChemicalRegisterPlacementCommand(a.LocationId, a.Chemical.ChemicalId, other.ProductId, "", null)),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task Move_Full_ClosesSourceAsMoved_OpensTarget_AndCarriesTheBalance()
    {
        var a = await ArrangeAsync(Handler);
        var source = await RegisterWithStockAsync(a, 2m);

        var change = await CreateInventoryService(a.Clock).MovePlacementAsync(a.Caller,
            new ChemicalMovePlacementCommand(source, a.OtherLocationId, "Øverste hylde", null));

        var closed = change.Placements.Single(p => p.Id == source);
        var opened = change.Placements.Single(p => p.Id != source);
        Assert.That(closed.RemovedAt, Is.EqualTo(a.Clock.UtcNow));
        Assert.That(closed.RemovalReason, Is.EqualTo(ChemicalRemovalReasonEnum.Moved));
        Assert.That(closed.Balance, Is.EqualTo(0m));
        Assert.That(opened.LocationId, Is.EqualTo(a.OtherLocationId));
        Assert.That(opened.MovedFromPlacementId, Is.EqualTo(source));
        Assert.That(opened.Balance, Is.EqualTo(2m));
        Assert.That(opened.PlacementNote, Is.EqualTo("Øverste hylde"));
        Assert.That(change.StockEntries.Where(e => e.PlacementId == source).Select(e => e.Kind),
            Is.EqualTo(new[] { ChemicalStockEntryKindEnum.Received, ChemicalStockEntryKindEnum.MovedOut }));
        Assert.That(change.StockEntries.Single(e => e.PlacementId == opened.Id).Kind, Is.EqualTo(ChemicalStockEntryKindEnum.MovedIn));
    }

    [Test]
    public async Task Move_Partial_KeepsSourceOpenWithTheRemainder_EqualAmountIsAFullMove()
    {
        var a = await ArrangeAsync(Handler);
        var source = await RegisterWithStockAsync(a, 2m);
        var sut = CreateInventoryService(a.Clock);

        var partial = await sut.MovePlacementAsync(a.Caller, new ChemicalMovePlacementCommand(source, a.OtherLocationId, "", 0.5m));
        Assert.That(partial.Placements.Single(p => p.Id == source).RemovedAt, Is.Null);
        Assert.That(partial.Placements.Single(p => p.Id == source).Balance, Is.EqualTo(1.5m));
        Assert.That(partial.Placements.Single(p => p.Id != source).Balance, Is.EqualTo(0.5m));

        var rest = await sut.MovePlacementAsync(a.Caller, new ChemicalMovePlacementCommand(source, a.OtherLocationId, "", 1.5m));
        Assert.That(rest.Placements.Single(p => p.Id == source).RemovalReason, Is.EqualTo(ChemicalRemovalReasonEnum.Moved));
    }

    [Test]
    public async Task Move_CopiesTheObservedStatus_AndLinksTheSource()
    {
        var a = await ArrangeAsync(Handler);
        var chemical = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Status product", "3-456", status: 3);
        var sut = CreateInventoryService(a.Clock);
        var registered = await sut.RegisterPlacementAsync(a.Caller,
            new ChemicalRegisterPlacementCommand(a.LocationId, chemical.ChemicalId, null, "", null));
        var source = registered.Placements.Single().Id;

        // The register moves on; a moved placement keeps the status its source was observed with.
        var row = await ChemicalsDbContext!.Chemicals.SingleAsync(c => c.Id == chemical.ChemicalId);
        row.Status = 5;
        await ChemicalsDbContext.SaveChangesAsync();

        var change = await sut.MovePlacementAsync(a.Caller, new ChemicalMovePlacementCommand(source, a.OtherLocationId, "", null));

        var opened = change.Placements.Single(p => p.Id != source);
        Assert.That(opened.MovedFromPlacementId, Is.EqualTo(source));
        var stored = await BackendConfigurationPnDbContext!.ChemicalPlacements.AsNoTracking().SingleAsync(p => p.Id == opened.Id);
        Assert.That(stored.MovedFromPlacementId, Is.EqualTo(source));
        Assert.That(stored.ObservedStatus, Is.EqualTo(3));
    }

    [Test]
    public async Task Move_RejectsBadTargetsAndAmounts()
    {
        var a = await ArrangeAsync(Handler);
        var source = await RegisterWithStockAsync(a, 2m);
        var foreignProperty = await CreatePropertyAsync();
        var foreignLocation = await CreateLocationAsync(foreignProperty.Id);
        var sut = CreateInventoryService(a.Clock);

        Assert.That(async () => await sut.MovePlacementAsync(a.Caller, new ChemicalMovePlacementCommand(source, a.OtherLocationId, "", 2.5m)),
            Throws.InstanceOf<ArgumentException>());
        Assert.That(async () => await sut.MovePlacementAsync(a.Caller, new ChemicalMovePlacementCommand(source, foreignLocation.Id, "", null)),
            Throws.InstanceOf<ArgumentException>());
        Assert.That(async () => await sut.MovePlacementAsync(a.Caller, new ChemicalMovePlacementCommand(source, a.LocationId, "", null)),
            Throws.InstanceOf<ArgumentException>());

        await EnableStockAsync(a.PropertyId, enabled: false);
        Assert.That(async () => await sut.MovePlacementAsync(a.Caller, new ChemicalMovePlacementCommand(source, a.OtherLocationId, "", 1m)),
            Throws.InstanceOf<ChemicalPreconditionException>());
    }

    [TestCase(ChemicalRemovalReasonEnum.Used, ChemicalStockEntryKindEnum.Consumed)]
    [TestCase(ChemicalRemovalReasonEnum.Disposed, ChemicalStockEntryKindEnum.Adjusted)]
    public async Task Remove_WritesOffTheRemainingBalance(ChemicalRemovalReasonEnum reason, ChemicalStockEntryKindEnum writeOffKind)
    {
        var a = await ArrangeAsync(Handler);
        var id = await RegisterWithStockAsync(a, 2m);

        var change = await CreateInventoryService(a.Clock).RemovePlacementAsync(a.Caller,
            new ChemicalRemovePlacementCommand(id, reason, null, "Afleveret"));

        var placement = change.Placements.Single();
        Assert.That(placement.RemovalReason, Is.EqualTo(reason));
        Assert.That(placement.RemovalNote, Is.EqualTo("Afleveret"));
        Assert.That(placement.Balance, Is.EqualTo(0m));
        Assert.That(change.StockEntries.Last().Kind, Is.EqualTo(writeOffKind));
        Assert.That(change.StockEntries.Last().Amount, Is.EqualTo(-2m));
    }

    [Test]
    public async Task RemovePlacement_Twice_SecondFailsAndWritesNothing()
    {
        var a = await ArrangeAsync(Handler);
        var id = await RegisterWithStockAsync(a, 2m);
        var sut = CreateInventoryService(a.Clock);
        await sut.RemovePlacementAsync(a.Caller, new ChemicalRemovePlacementCommand(id, ChemicalRemovalReasonEnum.Used, null, ""));
        var entriesAfterFirst = await BackendConfigurationPnDbContext!.ChemicalStockEntries.CountAsync(e => e.PlacementId == id);

        Assert.That(async () => await sut.RemovePlacementAsync(a.Caller, new ChemicalRemovePlacementCommand(id, ChemicalRemovalReasonEnum.Used, null, "")),
            Throws.InstanceOf<ChemicalPreconditionException>());
        Assert.That(async () => await sut.MovePlacementAsync(a.Caller, new ChemicalMovePlacementCommand(id, a.OtherLocationId, "", null)),
            Throws.InstanceOf<ChemicalPreconditionException>());
        Assert.That(await BackendConfigurationPnDbContext.ChemicalStockEntries.CountAsync(e => e.PlacementId == id), Is.EqualTo(entriesAfterFirst));
    }

    [Test]
    public async Task Remove_RejectsMovedReasonFutureDatesAndDatesBeforeRegistration()
    {
        var a = await ArrangeAsync(Handler);
        var id = await RegisterWithStockAsync(a, 1m);
        var sut = CreateInventoryService(a.Clock);

        Assert.That(async () => await sut.RemovePlacementAsync(a.Caller, new ChemicalRemovePlacementCommand(id, ChemicalRemovalReasonEnum.Moved, null, "")),
            Throws.InstanceOf<ArgumentException>());
        Assert.That(async () => await sut.RemovePlacementAsync(a.Caller, new ChemicalRemovePlacementCommand(id, ChemicalRemovalReasonEnum.Used, a.Clock.UtcNow.AddDays(1), "")),
            Throws.InstanceOf<ArgumentException>());
        Assert.That(async () => await sut.RemovePlacementAsync(a.Caller, new ChemicalRemovePlacementCommand(id, ChemicalRemovalReasonEnum.Used, a.Clock.UtcNow.AddDays(-1), "")),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task Remove_NeedsTheRemoveFlag_NotRegister()
    {
        var a = await ArrangeAsync(Handler with { Remove = false });
        var id = await RegisterWithStockAsync(a, 1m);

        Assert.That(async () => await CreateInventoryService(a.Clock).RemovePlacementAsync(a.Caller,
                new ChemicalRemovePlacementCommand(id, ChemicalRemovalReasonEnum.Used, null, "")),
            Throws.InstanceOf<ChemicalPermissionDeniedException>());
    }

    [Test]
    public async Task UpdateNote_ChangesOpenPlacements_Only()
    {
        var a = await ArrangeAsync(Handler);
        var id = await RegisterWithStockAsync(a, 1m);
        var sut = CreateInventoryService(a.Clock);

        var change = await sut.UpdatePlacementNoteAsync(a.Caller, id, "Nederste hylde");
        Assert.That(change.Placements.Single().PlacementNote, Is.EqualTo("Nederste hylde"));

        await sut.RemovePlacementAsync(a.Caller, new ChemicalRemovePlacementCommand(id, ChemicalRemovalReasonEnum.Used, null, ""));
        Assert.That(async () => await sut.UpdatePlacementNoteAsync(a.Caller, id, "x"), Throws.InstanceOf<ChemicalPreconditionException>());
    }
}
