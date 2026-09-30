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

/// <summary>
/// Sync semantics against real UpdatedAt stamps (PnBase writes DateTime.UtcNow),
/// so the service runs on the system clock. Deltas are made deterministic by
/// backdating every row an hour before taking the token - no waiting.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalInventorySyncTests : ChemicalTestBase
{
    private static readonly ChemicalPermissionFlagsModel Handler =
        ChemicalPermissionFlagsModel.None with { View = true, Register = true, Stock = true };

    private sealed record World(int PropertyId, int HiddenPropertyId, int WorkerId, ChemicalCaller Caller,
        int LocationId, int ArchivedLocationId, int PlacementId, int OtherPlacementId, int ClosedLongAgoId, SeededChemical Chemical);

    private async Task<World> ArrangeAsync()
    {
        var property = await CreatePropertyAsync();
        var hidden = await CreatePropertyAsync();
        var worker = await AddWorkerAsync(property.Id);
        await AddWorkerAsync(hidden.Id, worker);
        await GrantAsync(property.Id, worker, Handler);
        await GrantAsync(hidden.Id, worker, ChemicalPermissionFlagsModel.None with { Register = true });
        await EnableStockAsync(property.Id);
        var location = await CreateLocationAsync(property.Id, "Kemirum", 1);
        var archived = await CreateLocationAsync(property.Id, "Gammelt skab", 2);
        await archived.Delete(BackendConfigurationPnDbContext!);
        await CreateLocationAsync(hidden.Id, "Skjult", 1);
        var chemical = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Synced", "3-456");
        var caller = ChemicalCaller.App(TestUserId, worker);
        var sut = CreateInventoryService();
        var first = await sut.RegisterPlacementAsync(caller, new ChemicalRegisterPlacementCommand(location.Id, chemical.ChemicalId, null, "",
            new ChemicalStockAmountModel(null, null, 2m, ChemicalStockUnitEnum.L, null, null, null)));
        var second = await sut.RegisterPlacementAsync(caller, new ChemicalRegisterPlacementCommand(location.Id, chemical.ChemicalId, null, "", null));
        var longAgo = await CreatePlacementAsync(location.Id, chemical.ChemicalId, removedAt: DateTime.UtcNow.AddMonths(-13));
        return new World(property.Id, hidden.Id, worker, caller, location.Id, archived.Id,
            first.Placements.Single().Id, second.Placements.Single().Id, longAgo.Id, chemical);
    }

    private async Task BackdateEverythingAsync()
    {
        DateTime? hourAgo = DateTime.UtcNow.AddHours(-1);
        var db = BackendConfigurationPnDbContext!;
        await db.ChemicalLocations.ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, hourAgo));
        await db.ChemicalPlacements.ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, hourAgo));
        await db.ChemicalStockEntries.ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, hourAgo));
        await db.ChemicalWorkerPermissions.ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, hourAgo));
        await db.PropertyWorkers.ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, hourAgo));
        await ChemicalsDbContext!.Chemicals.ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, hourAgo.Value));
        await ChemicalsDbContext.Products.ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, hourAgo.Value));
    }

    [Test]
    public async Task FullLoad_ReturnsEverythingVisible_AndNothingElse()
    {
        var w = await ArrangeAsync();

        var inventory = await CreateInventoryService().GetInventoryAsync(w.Caller, since: "");

        Assert.That(inventory.Full, Is.True);
        Assert.That(inventory.SyncToken, Does.StartWith("v1:"));
        Assert.That(inventory.Properties.Select(p => p.PropertyId), Is.EqualTo(new[] { w.PropertyId }));
        Assert.That(inventory.Properties.Single().StockEnabled, Is.True);
        Assert.That(inventory.Locations.Select(l => l.Id), Is.EquivalentTo(new[] { w.LocationId, w.ArchivedLocationId }));
        Assert.That(inventory.Locations.Single(l => l.Id == w.ArchivedLocationId).Archived, Is.True);
        Assert.That(inventory.Placements.Select(p => p.Id), Is.EquivalentTo(new[] { w.PlacementId, w.OtherPlacementId }));
        Assert.That(inventory.Placements.Single(p => p.Id == w.PlacementId).Balance, Is.EqualTo(2m));
        Assert.That(inventory.StockEntries.Select(e => e.PlacementId), Is.EqualTo(new[] { w.PlacementId }));
        Assert.That(inventory.RegisterEntries.Select(r => r.ChemicalId), Is.EqualTo(new[] { w.Chemical.ChemicalId }));
    }

    [Test]
    public async Task Delta_AfterAStockEntry_ResendsThatPlacementWithAllItsEntries_Only()
    {
        var w = await ArrangeAsync();
        await BackdateEverythingAsync();
        var token = ChemicalSyncToken.Create(DateTime.UtcNow);
        await CreateInventoryService().AddStockEntryAsync(w.Caller, new ChemicalAddStockEntryCommand(w.PlacementId,
            ChemicalStockEntryKindEnum.Consumed, new ChemicalStockAmountModel(null, null, 0.5m, ChemicalStockUnitEnum.L, null, null, null)));

        var delta = await CreateInventoryService().GetInventoryAsync(w.Caller, token);

        Assert.That(delta.Full, Is.False);
        Assert.That(delta.Properties.Select(p => p.PropertyId), Is.EqualTo(new[] { w.PropertyId }), "properties are always complete");
        Assert.That(delta.Locations, Is.Empty);
        Assert.That(delta.Placements.Select(p => p.Id), Is.EqualTo(new[] { w.PlacementId }));
        Assert.That(delta.Placements.Single().Balance, Is.EqualTo(1.5m));
        Assert.That(delta.StockEntries.Select(e => e.Kind),
            Is.EqualTo(new[] { ChemicalStockEntryKindEnum.Received, ChemicalStockEntryKindEnum.Consumed }));
        Assert.That(delta.RegisterEntries.Select(r => r.ChemicalId), Is.EqualTo(new[] { w.Chemical.ChemicalId }));
    }

    [Test]
    public async Task Delta_NewlyGrantedProperty_IsSentInFull()
    {
        var w = await ArrangeAsync();
        await BackdateEverythingAsync();
        var token = ChemicalSyncToken.Create(DateTime.UtcNow);
        await GrantAsync(w.HiddenPropertyId, w.WorkerId, ChemicalPermissionFlagsModel.None with { View = true });

        var delta = await CreateInventoryService().GetInventoryAsync(w.Caller, token);

        Assert.That(delta.Properties.Select(p => p.PropertyId), Is.EquivalentTo(new[] { w.PropertyId, w.HiddenPropertyId }));
        Assert.That(delta.Locations.Select(l => l.PropertyId), Is.All.EqualTo(w.HiddenPropertyId));
        Assert.That(delta.Locations, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Delta_RevokedView_DropsThePropertyFromTheList()
    {
        var w = await ArrangeAsync();
        var token = (await CreateInventoryService().GetInventoryAsync(w.Caller, "")).SyncToken;
        await GrantAsync(w.PropertyId, w.WorkerId, ChemicalPermissionFlagsModel.None with { Register = true });

        var delta = await CreateInventoryService().GetInventoryAsync(w.Caller, token);

        Assert.That(delta.Properties, Is.Empty);
        Assert.That(delta.Placements, Is.Empty);
    }

    [Test]
    public async Task Delta_RegisterChange_ResendsTheHeldChemical()
    {
        var w = await ArrangeAsync();
        await BackdateEverythingAsync();
        var token = ChemicalSyncToken.Create(DateTime.UtcNow);
        await ChemicalsDbContext!.Chemicals.Where(c => c.Id == w.Chemical.ChemicalId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, 6).SetProperty(c => c.UpdatedAt, DateTime.UtcNow));

        var delta = await CreateInventoryService().GetInventoryAsync(w.Caller, token);

        Assert.That(delta.Placements, Is.Empty);
        Assert.That(delta.RegisterEntries.Single().StatusText, Is.EqualTo("Produkt afmeldt"));
    }

    [Test]
    public async Task Sync_GarbageToken_ReturnsFullLoad()
    {
        var w = await ArrangeAsync();

        var inventory = await CreateInventoryService().GetInventoryAsync(w.Caller, "not-a-token");

        Assert.That(inventory.Full, Is.True);
        Assert.That(inventory.Placements, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task PropertyInventory_ForAWebAdmin_ReturnsThatPropertyOnly()
    {
        var w = await ArrangeAsync();

        var inventory = await CreateInventoryService().GetPropertyInventoryAsync(ChemicalCaller.Web(TestUserId), w.PropertyId);

        Assert.That(inventory.Properties.Select(p => p.PropertyId), Is.EqualTo(new[] { w.PropertyId }));
        Assert.That(inventory.Placements, Has.Count.EqualTo(2));
        Assert.That(inventory.SyncToken, Is.Empty);
    }
}
