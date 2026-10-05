using System;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

[TestFixture]
public class TailBiteSnapshotLoaderTests : TailBiteTestBase
{
    [Test]
    public async Task LoadAsync_FlagsRowsLinkedToClosedOutbreak_AndCancelled()
    {
        await SeedTreeAsync();
        var db = Db;
        var t = Clock.GetUtcNow().UtcDateTime;
        // one registration with two rows; the Sti 309 row was consumed by a closed outbreak
        var (reg, rows) = await SeedRegistrationAsync(t.AddDays(-2), false, (Pen309Id, 2, 0), (Pen310Id, 1, 0));
        var closed = new TailBiteOutbreak { PropertyId = PropertyId, LocationId = StableAId, RuleId = RuleId, RuleVersion = 1,
            OpenedAt = t.AddDays(-2), OpenedByRegistrationId = reg.Id, ClosedAt = t.AddDays(-1), ClosedBySiteId = 7 };
        await closed.Create(db);
        await new TailBiteOutbreakLink { OutbreakId = closed.Id, RegistrationLocationId = rows[0] }.Create(db);
        // a second, cancelled registration
        var (_, cancelledRows) = await SeedRegistrationAsync(t.AddDays(-1), true, (Pen501Id, 0, 1));

        var snap = await new TailBiteSnapshotLoader(db).LoadAsync(PropertyId, t.AddDays(-7), t);

        var byId = snap.Rows.ToDictionary(r => r.RowId);
        Assert.That(byId.Keys, Is.EquivalentTo(new[] { rows[0], rows[1], cancelledRows[0] }));
        Assert.That((byId[rows[0]].LinkedToClosed, byId[rows[0]].Cancelled), Is.EqualTo((true, false)));
        Assert.That((byId[rows[1]].LinkedToClosed, byId[rows[1]].Cancelled), Is.EqualTo((false, false)));
        Assert.That((byId[cancelledRows[0]].LinkedToClosed, byId[cancelledRows[0]].Cancelled), Is.EqualTo((false, true)));
        Assert.That(snap.OpenOutbreaks, Is.Empty);
    }

    [Test]
    public async Task LoadTreeAsync_ReturnsNoRows()
    {
        await SeedTreeAsync();
        await SeedRegistrationAsync(Clock.GetUtcNow().UtcDateTime.AddDays(-1), false, (Pen309Id, 2, 0));

        var snap = await new TailBiteSnapshotLoader(Db).LoadTreeAsync(PropertyId);

        Assert.That(snap.Locations, Has.Count.EqualTo(7));
        Assert.That(snap.Rules, Has.Count.EqualTo(1));
        Assert.That(snap.Rows, Is.Empty);
    }

    [Test]
    public async Task LoadAsync_ExcludesCountUnknown_RespectsWindow_IncludesRemovedLocations()
    {
        await SeedTreeAsync();
        var t = Clock.GetUtcNow().UtcDateTime;
        var from = t.AddDays(-7);
        var (_, atFrom) = await SeedRegistrationAsync(from, false, (Pen309Id, 1, 0));
        var (_, inside) = await SeedRegistrationAsync(t.AddDays(-3), false, (Pen309Id, 1, 0));
        var (_, atTo) = await SeedRegistrationAsync(t, false, (Pen309Id, 1, 0));
        var (unknownReg, _) = await SeedRegistrationAsync(t.AddDays(-2), false);
        await new TailBiteRegistrationLocation { RegistrationId = unknownReg.Id, LocationId = Pen310Id, CountUnknown = true }.Create(Db);
        var gone = await Db.TailBiteLocations.SingleAsync(l => l.Id == Pen501Id);
        await gone.Delete(Db);

        var snap = await new TailBiteSnapshotLoader(Db).LoadAsync(PropertyId, from, t);

        Assert.That(snap.Rows.Select(r => r.RowId), Is.EquivalentTo(new[] { inside[0], atTo[0] }));
        Assert.That(snap.Rows.Select(r => r.RowId), Does.Not.Contain(atFrom[0]));
        Assert.That(snap.Locations.ContainsKey(Pen501Id), Is.True);
    }

    [Test]
    public async Task LoadAsync_ListsOnlyOpenOutbreaks_AndRemovedClosedOutbreakDoesNotConsumeRows()
    {
        await SeedTreeAsync();
        var t = Clock.GetUtcNow().UtcDateTime;
        var (reg, rows) = await SeedRegistrationAsync(t.AddDays(-2), false, (Pen309Id, 2, 0));
        TailBiteOutbreak Outbreak(DateTime? closedAt, int locationId = 0) => new()
        {
            PropertyId = PropertyId, LocationId = locationId == 0 ? StableAId : locationId, RuleId = RuleId, RuleVersion = 1,
            OpenedAt = t.AddDays(-2), OpenedByRegistrationId = reg.Id, ClosedAt = closedAt, ClosedBySiteId = closedAt == null ? null : 7
        };
        var open = Outbreak(null); await open.Create(Db);
        var closed = Outbreak(t.AddDays(-1)); await closed.Create(Db);
        var removedOpen = Outbreak(null, StableBId); await removedOpen.Create(Db); await removedOpen.Delete(Db);
        var removedClosed = Outbreak(t.AddDays(-1)); await removedClosed.Create(Db);
        await new TailBiteOutbreakLink { OutbreakId = removedClosed.Id, RegistrationLocationId = rows[0] }.Create(Db);
        await removedClosed.Delete(Db);

        var snap = await new TailBiteSnapshotLoader(Db).LoadAsync(PropertyId, t.AddDays(-7), t);

        Assert.That(snap.OpenOutbreaks.Select(o => o.OutbreakId), Is.EqualTo(new[] { open.Id }));
        Assert.That(snap.Rows.Single().LinkedToClosed, Is.False);
    }
}
