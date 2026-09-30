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
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalStockTests : ChemicalTestBase
{
    private static readonly ChemicalPermissionFlagsModel StockKeeper =
        ChemicalPermissionFlagsModel.None with { View = true, Register = true, Stock = true };

    private async Task<(int PropertyId, int PlacementId, ChemicalCaller Caller)> ArrangeAsync(decimal initialLiters = 2m)
    {
        var property = await CreatePropertyAsync();
        var worker = await AddWorkerAsync(property.Id);
        await GrantAsync(property.Id, worker, StockKeeper);
        await EnableStockAsync(property.Id);
        var location = await CreateLocationAsync(property.Id);
        var chemical = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Stock product", "2-345");
        var caller = ChemicalCaller.App(TestUserId, worker);
        var change = await CreateInventoryService().RegisterPlacementAsync(caller, new ChemicalRegisterPlacementCommand(
            location.Id, chemical.ChemicalId, null, "", new ChemicalStockAmountModel(null, null, initialLiters, ChemicalStockUnitEnum.L, null, null, null)));
        return (property.Id, change.Placements.Single().Id, caller);
    }

    private static ChemicalAddStockEntryCommand Entry(int placementId, ChemicalStockEntryKindEnum kind, decimal? amount,
        ChemicalStockUnitEnum unit = ChemicalStockUnitEnum.L, decimal? containerSize = null, int? containerCount = null, string? batchLot = null) =>
        new(placementId, kind, new ChemicalStockAmountModel(containerSize, containerCount, amount, unit, batchLot, null, null));

    [Test]
    public async Task Received_ContainerSizeTimesCount_KeepsContainerDetails()
    {
        var (_, placementId, caller) = await ArrangeAsync();

        var change = await CreateInventoryService().AddStockEntryAsync(caller,
            Entry(placementId, ChemicalStockEntryKindEnum.Received, null, containerSize: 5m, containerCount: 4, batchLot: "LOT-9"));

        var entry = change.StockEntries.Last();
        Assert.That(entry.Amount, Is.EqualTo(20m));
        Assert.That(entry.ContainerSize, Is.EqualTo(5m));
        Assert.That(entry.ContainerCount, Is.EqualTo(4));
        Assert.That(entry.BatchLot, Is.EqualTo("LOT-9"));
        Assert.That(change.Placements.Single().Balance, Is.EqualTo(22m));
    }

    [Test]
    public async Task AddStockEntry_ConsumingMoreThanTheBalance_IsInvalid_AndBalanceIsUnchanged()
    {
        var (_, placementId, caller) = await ArrangeAsync(2m);
        var sut = CreateInventoryService();

        Assert.That(async () => await sut.AddStockEntryAsync(caller, Entry(placementId, ChemicalStockEntryKindEnum.Consumed, 2.001m)),
            Throws.InstanceOf<ArgumentException>());
        var ok = await sut.AddStockEntryAsync(caller, Entry(placementId, ChemicalStockEntryKindEnum.Consumed, 2m));
        Assert.That(ok.Placements.Single().Balance, Is.EqualTo(0m));
    }

    [Test]
    public async Task AddStockEntry_AnotherUnit_IsInvalid()
    {
        var (_, placementId, caller) = await ArrangeAsync();
        Assert.That(async () => await CreateInventoryService().AddStockEntryAsync(caller,
                Entry(placementId, ChemicalStockEntryKindEnum.Received, 1m, ChemicalStockUnitEnum.Kg)),
            Throws.InstanceOf<ArgumentException>());
    }

    [TestCase("0")]
    [TestCase("1.0001")]
    public async Task AddStockEntry_ZeroOrTooPrecise_IsInvalid(string raw)
    {
        var (_, placementId, caller) = await ArrangeAsync();
        var amount = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(async () => await CreateInventoryService().AddStockEntryAsync(caller, Entry(placementId, ChemicalStockEntryKindEnum.Received, amount)),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task Adjusted_StoresTheDifferenceToTheCountedBalance_NoChangeIsInvalid()
    {
        var (_, placementId, caller) = await ArrangeAsync(2m);
        var sut = CreateInventoryService();

        var change = await sut.AddStockEntryAsync(caller, Entry(placementId, ChemicalStockEntryKindEnum.Adjusted, 1.25m));

        Assert.That(change.StockEntries.Last().Amount, Is.EqualTo(-0.75m));
        Assert.That(change.Placements.Single().Balance, Is.EqualTo(1.25m));
        Assert.That(async () => await sut.AddStockEntryAsync(caller, Entry(placementId, ChemicalStockEntryKindEnum.Adjusted, 1.25m)),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task AddStockEntry_MoveKindsAreNotAccepted()
    {
        var (_, placementId, caller) = await ArrangeAsync();
        Assert.That(async () => await CreateInventoryService().AddStockEntryAsync(caller, Entry(placementId, ChemicalStockEntryKindEnum.MovedIn, 1m)),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task AddStockEntry_StockDisabled_IsPrecondition_WithoutStockFlag_IsDenied()
    {
        var (propertyId, placementId, caller) = await ArrangeAsync();
        var sut = CreateInventoryService();

        await EnableStockAsync(propertyId, enabled: false);
        Assert.That(async () => await sut.AddStockEntryAsync(caller, Entry(placementId, ChemicalStockEntryKindEnum.Received, 1m)),
            Throws.InstanceOf<ChemicalPreconditionException>());

        await EnableStockAsync(propertyId);
        await GrantAsync(propertyId, caller.WorkerId!.Value, StockKeeper with { Stock = false });
        Assert.That(async () => await sut.AddStockEntryAsync(caller, Entry(placementId, ChemicalStockEntryKindEnum.Received, 1m)),
            Throws.InstanceOf<ChemicalPermissionDeniedException>());
    }
}
