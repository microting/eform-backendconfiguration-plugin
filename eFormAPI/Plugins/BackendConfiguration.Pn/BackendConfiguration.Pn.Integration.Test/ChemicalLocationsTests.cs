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

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class ChemicalLocationsTests : ChemicalTestBase
{
    private static readonly ChemicalPermissionFlagsModel Locations =
        ChemicalPermissionFlagsModel.None with { View = true, ManageLocations = true };

    [Test]
    public async Task Create_TrimsName_AndAppendsAfterTheLastLocation()
    {
        var (propertyId, _, caller) = await WorkerWith(Locations);
        await CreateLocationAsync(propertyId, "Lade", sortOrder: 4);

        var created = await CreateInventoryService().CreateLocationAsync(caller,
            new ChemicalCreateLocationCommand(propertyId, "  Kemirum  ", "Bag laden", null));

        Assert.That(created.Name, Is.EqualTo("Kemirum"));
        Assert.That(created.SortOrder, Is.EqualTo(5));
        Assert.That(created.Archived, Is.False);
    }

    [Test]
    public async Task Create_DuplicateActiveName_IsConflict_ArchivedNameIsFree()
    {
        var (propertyId, _, caller) = await WorkerWith(Locations);
        var sut = CreateInventoryService();
        var first = await sut.CreateLocationAsync(caller, new ChemicalCreateLocationCommand(propertyId, "Kemirum", "", null));

        Assert.That(async () => await sut.CreateLocationAsync(caller, new ChemicalCreateLocationCommand(propertyId, "kemirum", "", null)),
            Throws.InstanceOf<ChemicalConflictException>());

        await sut.ArchiveLocationAsync(caller, first.Id);
        Assert.That(async () => await sut.CreateLocationAsync(caller, new ChemicalCreateLocationCommand(propertyId, "Kemirum", "", null)),
            Throws.Nothing);
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task Create_BlankName_IsInvalid(string name)
    {
        var (propertyId, _, caller) = await WorkerWith(Locations);
        Assert.That(async () => await CreateInventoryService().CreateLocationAsync(caller, new ChemicalCreateLocationCommand(propertyId, name, "", null)),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task Create_WithoutManageLocations_IsDenied()
    {
        var (propertyId, _, caller) = await WorkerWith(ChemicalPermissionFlagsModel.None with { View = true, Register = true });
        Assert.That(async () => await CreateInventoryService().CreateLocationAsync(caller, new ChemicalCreateLocationCommand(propertyId, "X", "", null)),
            Throws.InstanceOf<ChemicalPermissionDeniedException>());
    }

    [Test]
    public async Task LocationWrites_WithoutManageLocations_AreDenied()
    {
        var (propertyId, _, caller) = await WorkerWith(ChemicalPermissionFlagsModel.All with { ManageLocations = false, Admin = false });
        var location = await CreateLocationAsync(propertyId);
        var sut = CreateInventoryService();

        Assert.That(async () => await sut.UpdateLocationAsync(caller, new ChemicalUpdateLocationCommand(location.Id, "X", "", null)),
            Throws.InstanceOf<ChemicalPermissionDeniedException>());
        Assert.That(async () => await sut.ArchiveLocationAsync(caller, location.Id),
            Throws.InstanceOf<ChemicalPermissionDeniedException>());
        Assert.That(async () => await sut.ReorderLocationsAsync(caller, propertyId, [location.Id]),
            Throws.InstanceOf<ChemicalPermissionDeniedException>());
        Assert.That(async () => await sut.SaveLocationPhotoAsync(caller, location.Id, [1], "image/jpeg"),
            Throws.InstanceOf<ChemicalPermissionDeniedException>());
    }

    [Test]
    public async Task Update_RenameToAnotherActiveLocationsName_IsConflict()
    {
        var (propertyId, _, caller) = await WorkerWith(Locations);
        await CreateLocationAsync(propertyId, "Kemirum");
        var other = await CreateLocationAsync(propertyId, "Lade");

        Assert.That(async () => await CreateInventoryService().UpdateLocationAsync(caller,
                new ChemicalUpdateLocationCommand(other.Id, "kemirum", "", null)),
            Throws.InstanceOf<ChemicalConflictException>());
    }

    [Test]
    public async Task Update_ChangesNameDescriptionAndSortOrder_ZeroKeepsOrder()
    {
        var (propertyId, _, caller) = await WorkerWith(Locations);
        var location = await CreateLocationAsync(propertyId, "Old", sortOrder: 3);
        var sut = CreateInventoryService();

        var renamed = await sut.UpdateLocationAsync(caller, new ChemicalUpdateLocationCommand(location.Id, "New", "Skab 2", 0));
        var moved = await sut.UpdateLocationAsync(caller, new ChemicalUpdateLocationCommand(location.Id, "New", "Skab 2", 1));

        Assert.That(renamed.Name, Is.EqualTo("New"));
        Assert.That(renamed.Description, Is.EqualTo("Skab 2"));
        Assert.That(renamed.SortOrder, Is.EqualTo(3));
        Assert.That(moved.SortOrder, Is.EqualTo(1));
    }

    [Test]
    public async Task Archive_WithAnOpenPlacement_IsPrecondition_EmptyLocationArchives()
    {
        var (propertyId, _, caller) = await WorkerWith(Locations);
        var busy = await CreateLocationAsync(propertyId);
        var empty = await CreateLocationAsync(propertyId);
        await CreatePlacementAsync(busy.Id, chemicalId: 1);
        await CreatePlacementAsync(empty.Id, chemicalId: 1, removedAt: DateTime.UtcNow.AddDays(-1));
        var sut = CreateInventoryService();

        Assert.That(async () => await sut.ArchiveLocationAsync(caller, busy.Id), Throws.InstanceOf<ChemicalPreconditionException>());
        var archived = await sut.ArchiveLocationAsync(caller, empty.Id);
        Assert.That(archived.Archived, Is.True);
        Assert.That(async () => await sut.UpdateLocationAsync(caller, new ChemicalUpdateLocationCommand(empty.Id, "X", "", null)),
            Throws.InstanceOf<ChemicalNotFoundException>());
    }

    [Test]
    public async Task Reorder_SetsOneBasedOrder_AndRejectsIncompleteLists()
    {
        var (propertyId, _, caller) = await WorkerWith(Locations);
        var a = await CreateLocationAsync(propertyId, sortOrder: 1);
        var b = await CreateLocationAsync(propertyId, sortOrder: 2);
        var c = await CreateLocationAsync(propertyId, sortOrder: 3);
        var sut = CreateInventoryService();

        var ordered = await sut.ReorderLocationsAsync(caller, propertyId, [c.Id, a.Id, b.Id]);

        Assert.That(ordered.Select(l => (l.Id, l.SortOrder)), Is.EqualTo(new[] { (c.Id, 1), (a.Id, 2), (b.Id, 3) }));
        Assert.That(async () => await sut.ReorderLocationsAsync(caller, propertyId, [c.Id, a.Id]), Throws.InstanceOf<ArgumentException>());
        Assert.That(async () => await sut.ReorderLocationsAsync(caller, propertyId, [c.Id, c.Id, a.Id]), Throws.InstanceOf<ArgumentException>());
    }

    /// <summary>
    /// Copilot on #1409: a reorder skips rows already at their position, so without a
    /// lock taken before anything is read, a reorder that waited for a concurrent one
    /// continues from its stale snapshot and leaves duplicate sort orders. The property's
    /// active locations are locked first, so the waiting reorder sees the committed order.
    /// </summary>
    [Test]
    public async Task Reorder_WhileAnotherReorderHoldsTheLocations_WaitsAndLeavesDistinctSortOrders()
    {
        var (propertyId, _, caller) = await WorkerWith(Locations);
        var a = await CreateLocationAsync(propertyId, sortOrder: 1);
        var b = await CreateLocationAsync(propertyId, sortOrder: 2);
        var c = await CreateLocationAsync(propertyId, sortOrder: 3);
        await using var other = NewContext();
        await using var writerDb = NewContext();
        await writerDb.Database.OpenConnectionAsync();
        var connectionId = (await writerDb.Database.SqlQuery<long>($"SELECT CONNECTION_ID() AS `Value`").ToListAsync()).Single();

        // Another reorder to [A, C, B], holding its transaction open after writing every active location.
        var held = await other.Database.BeginTransactionAsync();
        await other.ChemicalLocations.Where(l => l.PropertyId == propertyId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.SortOrder, l => l.Id == c.Id ? 2 : l.Id == b.Id ? 3 : 1));

        var pending = CreateInventoryService(writerDb, null).ReorderLocationsAsync(caller, propertyId, [b.Id, a.Id, c.Id]);
        try
        {
            // A bounded poll on a condition (the reorder is queued behind the held rows), not a settle-sleep.
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!await IsBlockedInAStatementAsync(other, connectionId))
            {
                Assert.That(pending.IsCompleted, Is.False, "the reorder finished without waiting for the other one");
                Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "the reorder never waited for the other one");
                await Task.Delay(50);
            }

            await held.CommitAsync();
            await pending;
        }
        finally
        {
            await held.DisposeAsync();
            await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(60)));
            _ = pending.Exception;
        }

        var stored = await BackendConfigurationPnDbContext!.ChemicalLocations.AsNoTracking()
            .Where(l => l.PropertyId == propertyId).OrderBy(l => l.SortOrder).Select(l => new { l.Id, l.SortOrder }).ToListAsync();
        Assert.That(stored.Select(l => (l.Id, l.SortOrder)), Is.EqualTo(new[] { (b.Id, 1), (a.Id, 2), (c.Id, 3) }),
            "the waiting reorder wins in full, with no duplicate positions");
    }

    /// <summary>
    /// Copilot on #1409: the reorder's lock statement must not move UpdatedAt backwards. A
    /// row the waiting reorder leaves in place keeps the newer stamp the other writer
    /// committed, so a delta sync taken in between cannot miss that row.
    /// </summary>
    [Test]
    public async Task Reorder_AfterWaiting_NeverMovesASkippedRowsUpdatedAtBackwards()
    {
        var (propertyId, _, caller) = await WorkerWith(Locations);
        var a = await CreateLocationAsync(propertyId, sortOrder: 1);
        var b = await CreateLocationAsync(propertyId, sortOrder: 2);
        var c = await CreateLocationAsync(propertyId, sortOrder: 3);
        await using var other = NewContext();
        await using var writerDb = NewContext();
        await writerDb.Database.OpenConnectionAsync();
        var connectionId = (await writerDb.Database.SqlQuery<long>($"SELECT CONNECTION_ID() AS `Value`").ToListAsync()).Single();

        // Another writer stamps every active location after the reorder below has started, and holds.
        var held = await other.Database.BeginTransactionAsync();
        var newer = new DateTime(DateTime.UtcNow.AddHours(1).Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        await other.ChemicalLocations.Where(l => l.PropertyId == propertyId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.UpdatedAt, newer));

        var pending = CreateInventoryService(writerDb, null).ReorderLocationsAsync(caller, propertyId, [b.Id, a.Id, c.Id]);
        try
        {
            // A bounded poll on a condition (the reorder is queued behind the held rows), not a settle-sleep.
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!await IsBlockedInAStatementAsync(other, connectionId))
            {
                Assert.That(pending.IsCompleted, Is.False, "the reorder finished without waiting for the other writer");
                Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "the reorder never waited for the other writer");
                await Task.Delay(50);
            }

            await held.CommitAsync();
            await pending;
        }
        finally
        {
            await held.DisposeAsync();
            await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(60)));
            _ = pending.Exception;
        }

        var skipped = await BackendConfigurationPnDbContext!.ChemicalLocations.AsNoTracking().SingleAsync(l => l.Id == c.Id);
        Assert.That(skipped.SortOrder, Is.EqualTo(3), "C was already in place, so the reorder left it");
        Assert.That(skipped.UpdatedAt, Is.EqualTo(newer), "C keeps the newer stamp the other writer committed");
    }

    [Test]
    public async Task Photo_UploadThenRead_RoundTrips_ReadNeedsOnlyView()
    {
        var (propertyId, _, admin) = await WorkerWith(Locations);
        var location = await CreateLocationAsync(propertyId);
        var viewer = await AddWorkerAsync(propertyId);
        await GrantAsync(propertyId, viewer, ChemicalPermissionFlagsModel.None with { View = true });
        var sut = CreateInventoryService();

        var updated = await sut.SaveLocationPhotoAsync(admin, location.Id, [1, 2, 3], "image/jpeg");
        var (content, contentType) = await sut.GetLocationPhotoAsync(ChemicalCaller.App(TestUserId, viewer), location.Id);

        Assert.That(updated.PhotoFileName, Does.StartWith($"chemical-location-{location.Id}-").And.EndWith(".jpg"));
        Assert.That(content, Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(contentType, Is.EqualTo("image/jpeg"));
    }

    [Test]
    public async Task Photo_OfAnArchivedLocation_StaysReadable_ForHistory()
    {
        var (propertyId, _, caller) = await WorkerWith(Locations);
        var location = await CreateLocationAsync(propertyId);
        var sut = CreateInventoryService();
        await sut.SaveLocationPhotoAsync(caller, location.Id, [7, 8], "image/png");
        await sut.ArchiveLocationAsync(caller, location.Id);

        var (content, contentType) = await sut.GetLocationPhotoAsync(caller, location.Id);

        Assert.That(content, Is.EqualTo(new byte[] { 7, 8 }));
        Assert.That(contentType, Is.EqualTo("image/png"));
    }

    [Test]
    public async Task Photo_WrongTypeEmptyOrMissing_IsRejected()
    {
        var (propertyId, _, caller) = await WorkerWith(Locations);
        var location = await CreateLocationAsync(propertyId);
        var sut = CreateInventoryService();

        Assert.That(async () => await sut.SaveLocationPhotoAsync(caller, location.Id, [1], "application/pdf"), Throws.InstanceOf<ArgumentException>());
        Assert.That(async () => await sut.SaveLocationPhotoAsync(caller, location.Id, [], "image/png"), Throws.InstanceOf<ArgumentException>());
        Assert.That(async () => await sut.SaveLocationPhotoAsync(caller, location.Id, new byte[ChemicalInventoryService.MaxPhotoBytes + 1], "image/png"),
            Throws.InstanceOf<ArgumentException>());
        Assert.That(async () => await sut.GetLocationPhotoAsync(caller, location.Id), Throws.InstanceOf<ChemicalNotFoundException>());
    }
}
