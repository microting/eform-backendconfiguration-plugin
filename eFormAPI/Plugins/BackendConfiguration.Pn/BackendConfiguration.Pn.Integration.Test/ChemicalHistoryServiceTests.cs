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
using BackendConfiguration.Pn.Services.ChemicalReporting;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// W6 (spec §5.1, §11): history is derived from version tables, stock entries and
/// ChemicalAlertLog. The scenario is driven through ChemicalInventoryService as a
/// web admin, so the version rows are the real ones production writes.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalHistoryServiceTests : ChemicalTestBase
{
    private static readonly ChemicalCaller Web = ChemicalCaller.Web(TestUserId);

    private ChemicalHistoryService CreateSut() =>
        new(BackendConfigurationPnDbContext!, new ChemicalRegisterReader(ChemicalsDbContext!), Names);

    private static ChemicalHistoryQuery Around(DateTime now) => new(now.Date.AddDays(-1), now.Date.AddDays(1), null, null, null, 0, 100);

    private static ChemicalStockAmountModel Liters(decimal amount) => new(null, null, amount, ChemicalStockUnitEnum.L, null, null, null);

    // Events carry list properties (reference equality), so pages are compared on a stable key.
    private static (DateTime, ChemicalHistoryActionEnum, int?) Key(ChemicalHistoryEventModel e) => (e.At, e.Action, e.PlacementId);

    private sealed record Scenario(int PropertyId, int Lade, int Kemirum, int Moved, int Target, int Removed, int Worker);

    private async Task<Scenario> ArrangeAsync()
    {
        var property = await CreatePropertyAsync();
        var inventory = CreateInventoryService();
        await inventory.SetSettingsAsync(Web, new ChemicalSetSettingsCommand(property.Id, true, []));
        var lade = await inventory.CreateLocationAsync(Web, new ChemicalCreateLocationCommand(property.Id, "Lade", "", null));
        var kemirum = await inventory.CreateLocationAsync(Web, new ChemicalCreateLocationCommand(property.Id, "Kemirum", "", null));
        await inventory.UpdateLocationAsync(Web, new ChemicalUpdateLocationCommand(kemirum.Id, "Kemirum 2", "", null));
        var roundup = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Roundup PowerMax", "1-285");
        var rodilon = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Rodilon Pasta", "616-4");

        var moved = (await inventory.RegisterPlacementAsync(Web,
            new ChemicalRegisterPlacementCommand(lade.Id, roundup.ChemicalId, null, "Øverste hylde", Liters(5)))).Placements.Single().Id;
        await inventory.AddStockEntryAsync(Web, new ChemicalAddStockEntryCommand(moved, ChemicalStockEntryKindEnum.Consumed, Liters(1)));
        var target = (await inventory.MovePlacementAsync(Web, new ChemicalMovePlacementCommand(moved, kemirum.Id, "Reol 1", null)))
            .Placements.Single(p => p.RemovedAt == null).Id;
        await inventory.UpdatePlacementNoteAsync(Web, target, "Reol 1, nederst");
        var removed = (await inventory.RegisterPlacementAsync(Web,
            new ChemicalRegisterPlacementCommand(lade.Id, rodilon.ChemicalId, null, "Rottekasse 4", null))).Placements.Single().Id;
        await inventory.RemovePlacementAsync(Web, new ChemicalRemovePlacementCommand(removed, ChemicalRemovalReasonEnum.Used, null, "tom"));

        var worker = await AddWorkerAsync(property.Id);
        await inventory.SetWorkerPermissionsAsync(Web, property.Id,
            [new ChemicalSetWorkerPermissionCommand(worker, ChemicalPermissionFlagsModel.None with { View = true, Register = true })]);

        foreach (var channel in new[] { ChemicalAlertChannelEnum.Push, ChemicalAlertChannelEnum.Push, ChemicalAlertChannelEnum.Email })
        {
            await new ChemicalAlertLog
            {
                PlacementId = target, Threshold = ChemicalAlertThresholdEnum.StatusChanged, Status = 6, Channel = channel,
                SentAt = DateTime.UtcNow, CreatedByUserId = 0, UpdatedByUserId = 0,
            }.Create(BackendConfigurationPnDbContext!);
        }

        return new Scenario(property.Id, lade.Id, kemirum.Id, moved, target, removed, worker);
    }

    [Test]
    public async Task Events_AreDerivedFromEverySource()
    {
        var s = await ArrangeAsync();

        var events = await CreateSut().GetAllAsync(s.PropertyId, Around(DateTime.UtcNow));

        Assert.That(events.Where(e => e.Action == ChemicalHistoryActionEnum.Registered).Select(e => e.PlacementId),
            Is.EquivalentTo(new int?[] { s.Moved, s.Removed }), "the move target is a Moved event, not a registration");

        var move = events.Single(e => e.Action == ChemicalHistoryActionEnum.Moved);
        Assert.That((move.LocationName, move.TargetLocationName, move.Amount, move.Unit),
            Is.EqualTo(("Lade", "Kemirum 2", (decimal?)4m, (ChemicalStockUnitEnum?)ChemicalStockUnitEnum.L)));
        Assert.That(move.ProductName, Is.EqualTo("Roundup PowerMax"));

        var received = events.Single(e => e.Action == ChemicalHistoryActionEnum.StockReceived && e.PlacementId == s.Moved);
        Assert.That((received.Amount, received.BalanceAfter), Is.EqualTo(((decimal?)5m, (decimal?)5m)));
        var consumed = events.Single(e => e.Action == ChemicalHistoryActionEnum.StockConsumed && e.PlacementId == s.Moved);
        Assert.That((consumed.Amount, consumed.BalanceAfter), Is.EqualTo(((decimal?)-1m, (decimal?)4m)));

        var note = events.Single(e => e.Action == ChemicalHistoryActionEnum.NoteChanged);
        Assert.That((note.PlacementId, note.OldValue, note.NewValue), Is.EqualTo(((int?)s.Target, "Reol 1", "Reol 1, nederst")));

        var removal = events.Single(e => e.Action == ChemicalHistoryActionEnum.Removed);
        Assert.That((removal.PlacementId, removal.RemovalReason, removal.Note),
            Is.EqualTo(((int?)s.Removed, (ChemicalRemovalReasonEnum?)ChemicalRemovalReasonEnum.Used, "tom")));

        Assert.That(events.Where(e => e.Action == ChemicalHistoryActionEnum.LocationCreated).Select(e => e.NewValue),
            Is.EquivalentTo(new[] { "Lade", "Kemirum" }));
        var rename = events.Single(e => e.Action == ChemicalHistoryActionEnum.LocationRenamed);
        Assert.That((rename.OldValue, rename.NewValue), Is.EqualTo(("Kemirum", "Kemirum 2")));

        var permission = events.Single(e => e.Action == ChemicalHistoryActionEnum.PermissionChanged);
        Assert.That(permission.WorkerName, Is.EqualTo($"Worker {s.Worker}"));
        Assert.That(permission.PermissionsAdded, Is.EquivalentTo(new[] { "View", "Register" }));
        Assert.That(permission.PermissionsRemoved, Is.Empty);

        var stock = events.Single(e => e.Action == ChemicalHistoryActionEnum.SettingsChanged && e.Field == "StockEnabled");
        Assert.That((stock.OldValue, stock.NewValue), Is.EqualTo(("false", "true")));

        var status = events.Single(e => e.Action == ChemicalHistoryActionEnum.StatusChanged);
        Assert.That((status.UserId, status.StatusText, status.PushCount, status.EmailCount), Is.EqualTo((0, "Produkt afmeldt", 2, 1)));

        Assert.That(events.Where(e => e.UserId == TestUserId).All(e => e.UserName == $"User {TestUserId}"));
        Assert.That(events.Select(e => e.At), Is.Ordered.Descending);
    }

    [Test]
    public async Task Removal_OfAStockedPlacement_FoldsTheWriteOffIntoTheRemovedEvent()
    {
        var property = await CreatePropertyAsync();
        var inventory = CreateInventoryService();
        await inventory.SetSettingsAsync(Web, new ChemicalSetSettingsCommand(property.Id, true, []));
        var lade = await inventory.CreateLocationAsync(Web, new ChemicalCreateLocationCommand(property.Id, "Lade", "", null));
        var roundup = await ChemicalRegisterSeed.AddChemicalAsync(ChemicalsDbContext!, "Roundup PowerMax", "1-285");
        var placement = (await inventory.RegisterPlacementAsync(Web,
            new ChemicalRegisterPlacementCommand(lade.Id, roundup.ChemicalId, null, "Hylde", Liters(5)))).Placements.Single().Id;
        await inventory.AddStockEntryAsync(Web, new ChemicalAddStockEntryCommand(placement, ChemicalStockEntryKindEnum.Consumed, Liters(1)));
        await inventory.RemovePlacementAsync(Web, new ChemicalRemovePlacementCommand(placement, ChemicalRemovalReasonEnum.Used, null, "brugt"));

        var events = await CreateSut().GetAllAsync(property.Id, Around(DateTime.UtcNow));

        var removal = events.Single(e => e.Action == ChemicalHistoryActionEnum.Removed);
        Assert.That((removal.Amount, removal.Unit, removal.RemovalReason, removal.Note),
            Is.EqualTo(((decimal?)-4m, (ChemicalStockUnitEnum?)ChemicalStockUnitEnum.L, (ChemicalRemovalReasonEnum?)ChemicalRemovalReasonEnum.Used, "brugt")));
        var consumed = events.Where(e => e.Action == ChemicalHistoryActionEnum.StockConsumed && e.PlacementId == placement).ToList();
        Assert.That(consumed.Select(e => e.Amount), Is.EqualTo(new decimal?[] { -1m }), "the write-off is not a separate stock line");
    }

    [Test]
    public async Task Page_FiltersPagesAndListsUsers()
    {
        var s = await ArrangeAsync();
        var sut = CreateSut();
        var all = await sut.GetAllAsync(s.PropertyId, Around(DateTime.UtcNow));

        var page = await sut.GetPageAsync(s.PropertyId, Around(DateTime.UtcNow) with { PageSize = 3, Page = 1 });
        Assert.That(page.Total, Is.EqualTo(all.Count));
        Assert.That(page.Events.Select(Key), Is.EqualTo(all.Skip(3).Take(3).Select(Key)));
        Assert.That(page.Users, Is.EqualTo(new[] { new ChemicalHistoryUserModel(TestUserId, $"User {TestUserId}") }));

        var system = await sut.GetAllAsync(s.PropertyId, Around(DateTime.UtcNow) with { UserId = 0 });
        Assert.That(system.Select(e => e.Action), Is.EqualTo(new[] { ChemicalHistoryActionEnum.StatusChanged }));

        var kemirum = await sut.GetAllAsync(s.PropertyId, Around(DateTime.UtcNow) with { LocationId = s.Kemirum });
        Assert.That(kemirum.Select(e => e.Action), Does.Contain(ChemicalHistoryActionEnum.Moved), "a move counts for its target location");
        Assert.That(kemirum.Any(e => e.PlacementId == s.Removed), Is.False);

        var removals = await sut.GetAllAsync(s.PropertyId, Around(DateTime.UtcNow) with { Action = ChemicalHistoryActionEnum.Removed });
        Assert.That(removals, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Query_EndDateIsInclusive_AndOtherPeriodsAreEmpty()
    {
        var s = await ArrangeAsync();
        var today = DateTime.UtcNow.Date;

        var sameDay = await CreateSut().GetAllAsync(s.PropertyId, new ChemicalHistoryQuery(today, today, null, null, null));
        var lastYear = await CreateSut().GetAllAsync(s.PropertyId, new ChemicalHistoryQuery(today.AddYears(-1), today.AddYears(-1).AddDays(30), null, null, null));

        Assert.That(sameDay, Is.Not.Empty);
        Assert.That(lastYear, Is.Empty);
    }

    [Test]
    public async Task Query_Validation_RejectsReversedAndOverlongPeriods()
    {
        var property = await CreatePropertyAsync();
        var today = DateTime.UtcNow.Date;
        var sut = CreateSut();

        Assert.That(async () => await sut.GetPageAsync(property.Id, new ChemicalHistoryQuery(today, today.AddDays(-1), null, null, null)),
            Throws.InstanceOf<ArgumentException>().With.Message.Contains("ends before"));
        Assert.That(async () => await sut.GetPageAsync(property.Id, new ChemicalHistoryQuery(today.AddDays(-366), today, null, null, null)),
            Throws.InstanceOf<ArgumentException>().With.Message.Contains("366"));
        Assert.That(async () => await sut.GetPageAsync(int.MaxValue, new ChemicalHistoryQuery(today, today, null, null, null)),
            Throws.InstanceOf<ChemicalNotFoundException>());
    }

    [Test]
    public async Task Query_Validation_RejectsPeriodsWithATimeOfDay()
    {
        var property = await CreatePropertyAsync();
        var today = DateTime.UtcNow.Date;
        var sut = CreateSut();

        Assert.That(async () => await sut.GetPageAsync(property.Id, new ChemicalHistoryQuery(today.AddHours(13), today, null, null, null)),
            Throws.InstanceOf<ArgumentException>().With.Message.Contains("dates only"));
        Assert.That(async () => await sut.GetAllAsync(property.Id, new ChemicalHistoryQuery(today, today.AddMinutes(1), null, null, null)),
            Throws.InstanceOf<ArgumentException>().With.Message.Contains("dates only"));
    }

    [Test]
    public async Task Page_ClampsPageSize()
    {
        var s = await ArrangeAsync();

        var page = await CreateSut().GetPageAsync(s.PropertyId, Around(DateTime.UtcNow) with { PageSize = 10_000, Page = -3 });

        Assert.That(page.Events.Count, Is.LessThanOrEqualTo(ChemicalHistoryService.MaxPageSize));
    }
}
