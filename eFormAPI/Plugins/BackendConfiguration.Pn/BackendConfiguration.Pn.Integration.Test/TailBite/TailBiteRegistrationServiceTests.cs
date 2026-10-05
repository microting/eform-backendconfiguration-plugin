using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

[TestFixture]
public class TailBiteRegistrationServiceTests : TailBiteTestBase
{
    private const int WorkerSiteId = 7;
    private const int OtherSiteId = 8;

    // The tree and the worker most tests need; tests that need something else seed their own.
    [SetUp]
    public async Task SeedDefaults()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(WorkerSiteId);
    }

    private TailBiteRegistrationService NewSut(ITailBiteOutbreakNotifier? notifier = null, BackendConfigurationPnDbContext? db = null)
    {
        db ??= Db;
        return new(db, new TailBitePropertyLock(db), new TailBiteSnapshotLoader(db), new TailBiteDecisionWriter(db), NewAccess(db),
            new TailBitePhotoStorage(new FakeAdhocPhotoStorage(), CoreHelper()), Clock, notifier);
    }

    private CreateRegistrationCommand Cmd(Guid? uuid = null, int minor = 2, int severe = 0, int? loc = null, DateTime? at = null)
        => new(uuid ?? Guid.NewGuid(), PropertyId, at ?? Clock.GetUtcNow().UtcDateTime,
            [new RegistrationLocationInput(loc ?? Pen309Id, minor, severe)], [], null);

    [Test]
    public async Task Create_BelowThreshold_NoOutbreak()
    {
        var r = await NewSut().CreateAsync(WorkerSiteId, Cmd(minor: 2));
        Assert.That(r.Outbreaks, Is.Empty);
    }

    [Test]
    public async Task Create_Severe_OpensOutbreakOnStable()
    {
        var notifier = Substitute.For<ITailBiteOutbreakNotifier>();
        var r = await NewSut(notifier).CreateAsync(WorkerSiteId, Cmd(minor: 0, severe: 1));
        Assert.That(r.Outbreaks.Single().Opened, Is.True);
        Assert.That(Db.TailBiteOutbreaks.Single().LocationId, Is.EqualTo(StableAId));
        await notifier.Received(1).NotifyOpenedAsync(PropertyId, Arg.Any<IReadOnlyList<OutbreakOutcome>>());
    }

    [Test]
    public async Task Create_SameUuidTwice_ReturnsSameResult()
    {
        var uuid = Guid.NewGuid();
        var a = await NewSut().CreateAsync(WorkerSiteId, Cmd(uuid, severe: 1, minor: 0));
        var b = await NewSut().CreateAsync(WorkerSiteId, Cmd(uuid, severe: 1, minor: 0));
        Assert.That(b.RegistrationId, Is.EqualTo(a.RegistrationId));
        Assert.That(b.Outbreaks, Is.EqualTo(a.Outbreaks));
        Assert.That(Db.TailBiteRegistrations.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task Create_ReplaySameUuid_NotifiesOnce()
    {
        var notifier = Substitute.For<ITailBiteOutbreakNotifier>();
        var uuid = Guid.NewGuid();
        await NewSut(notifier).CreateAsync(WorkerSiteId, Cmd(uuid, minor: 0, severe: 1));
        await NewSut(notifier).CreateAsync(WorkerSiteId, Cmd(uuid, minor: 0, severe: 1));
        await notifier.Received(1).NotifyOpenedAsync(PropertyId, Arg.Any<IReadOnlyList<OutbreakOutcome>>());
    }

    [Test]
    public async Task Create_NotifierThrows_StillReturnsResult()
    {
        var notifier = Substitute.For<ITailBiteOutbreakNotifier>();
        notifier.NotifyOpenedAsync(Arg.Any<int>(), Arg.Any<IReadOnlyList<OutbreakOutcome>>())
            .Returns(Task.FromException(new InvalidOperationException("push down")));
        var r = await NewSut(notifier).CreateAsync(WorkerSiteId, Cmd(minor: 0, severe: 1));
        Assert.That(r.Outbreaks.Single().Opened, Is.True);
        Assert.That(Db.TailBiteRegistrations.Count(x => x.Id == r.RegistrationId), Is.EqualTo(1));
    }

    [Test]
    public async Task CreateRegistration_ConcurrentDuplicate_YieldsOneRegistration()
    {
        var uuid = Guid.NewGuid();
        // Two independent contexts, as two in-flight requests would have. The property lock serializes them, so the
        // second one takes the in-lock replay path; the DbUpdateException backstop is not exercised by this test.
        var (s1, s2) = (NewSut(db: CreateFreshBackendConfigurationDbContext()), NewSut(db: CreateFreshBackendConfigurationDbContext()));
        var results = await Task.WhenAll(
            s1.CreateAsync(WorkerSiteId, Cmd(uuid, minor: 0, severe: 1)),
            s2.CreateAsync(WorkerSiteId, Cmd(uuid, minor: 0, severe: 1)));
        Assert.That(results[0].RegistrationId, Is.EqualTo(results[1].RegistrationId));
        Assert.That(results[0].Outbreaks, Is.EqualTo(results[1].Outbreaks));
        Assert.That(CreateFreshBackendConfigurationDbContext().TailBiteRegistrations.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task Create_ReplayUuidOwnedByAnotherSite_Forbidden()
    {
        await SeedWorkerAsync(OtherSiteId);
        var uuid = Guid.NewGuid();
        await NewSut().CreateAsync(WorkerSiteId, Cmd(uuid));
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => NewSut().CreateAsync(OtherSiteId, Cmd(uuid)));
    }

    [Test]
    public async Task Create_ForeignLocation_Rejected()
    {
        await Assert.ThrowsAsync<TailBiteValidationException>(() => NewSut().CreateAsync(WorkerSiteId, Cmd(loc: 999_999)));
    }

    [Test]
    public async Task Create_LocationOfAnotherProperty_Rejected()
    {
        var other = new Property { Name = "Ejendom To" };
        await other.Create(Db);
        var foreign = new TailBiteLocation { PropertyId = other.Id, Name = "Stald X", QrCode = TailBiteDefaults.NewQrCode() };
        await foreign.Create(Db);
        await foreign.Delete(Db); // soft-deleted, on another property
        Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<TailBiteValidationException>(() => NewSut().CreateAsync(WorkerSiteId, Cmd(loc: foreign.Id)));
    }

    [Test]
    public async Task Create_WithDeletedActionType_Rejected()
    {
        var type = new TailBiteActionType { PropertyId = PropertyId, Code = "HALM", Name = "Halm" };
        await type.Create(Db);
        await type.Delete(Db);
        Db.ChangeTracker.Clear();
        var cmd = Cmd() with { ActionTypeIds = [type.Id] };
        await Assert.ThrowsAsync<TailBiteValidationException>(() => NewSut().CreateAsync(WorkerSiteId, cmd));
        Assert.That(Db.TailBiteRegistrations.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task Create_OnSoftDeletedLocation_Accepted()
    {
        var loc = await Db.TailBiteLocations.SingleAsync(l => l.Id == Pen309Id);
        await loc.Delete(Db);
        Db.ChangeTracker.Clear();
        var r = await NewSut().CreateAsync(WorkerSiteId, Cmd(minor: 2));
        Assert.That(Db.TailBiteRegistrationLocations.Count(x => x.RegistrationId == r.RegistrationId && x.LocationId == Pen309Id), Is.EqualTo(1));
    }

    [Test]
    public async Task Create_DuplicateLocation_Rejected()
    {
        var cmd = new CreateRegistrationCommand(Guid.NewGuid(), PropertyId, Clock.GetUtcNow().UtcDateTime,
            [new RegistrationLocationInput(Pen309Id, 1, 0), new RegistrationLocationInput(Pen309Id, 2, 0)], [], null);
        await Assert.ThrowsAsync<TailBiteValidationException>(() => NewSut().CreateAsync(WorkerSiteId, cmd));
        Assert.That(Db.TailBiteRegistrations.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task Create_AllZeroCounts_Rejected()
    {
        await Assert.ThrowsAsync<TailBiteValidationException>(() => NewSut().CreateAsync(WorkerSiteId, Cmd(minor: 0, severe: 0)));
    }

    [Test]
    public async Task Create_NotWorkerOnProperty_Forbidden()
    {
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => NewSut().CreateAsync(OtherSiteId, Cmd()));
    }

    [Test]
    public async Task Create_DisabledProperty_Rejected()
    {
        var prop = await Db.TailBiteProperties.SingleAsync(p => p.PropertyId == PropertyId);
        prop.Enabled = false;
        await prop.Update(Db);
        Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<TailBiteConflictException>(() => NewSut().CreateAsync(WorkerSiteId, Cmd()));
    }

    [Test]
    public async Task Create_FutureClock_ClampsEffectiveAt()
    {
        var r = await NewSut().CreateAsync(WorkerSiteId, Cmd(at: Clock.GetUtcNow().UtcDateTime.AddHours(5)));
        var reg = Db.TailBiteRegistrations.Single(x => x.Id == r.RegistrationId);
        Assert.That(reg.EffectiveAt, Is.EqualTo(Clock.GetUtcNow().UtcDateTime));
    }

    [Test]
    public async Task Photo_BeforeAndAfterRegistration_BothAttachable_AndIdempotent()
    {
        var sut = NewSut();
        var regUuid = Guid.NewGuid();
        var photo = Guid.NewGuid();
        await sut.SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1, 2, 3], "image/jpeg");
        await sut.SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1, 2, 3], "image/jpeg"); // retry
        Assert.That(Db.TailBiteRegistrationPhotos.Count(p => p.RegistrationClientUuid == regUuid), Is.EqualTo(1));
        var r = await sut.CreateAsync(WorkerSiteId, Cmd(regUuid));
        var second = Guid.NewGuid();
        await sut.SavePhotoAsync(WorkerSiteId, PropertyId, second, regUuid, [4, 5], "image/png"); // registration exists now

        var reg = Db.TailBiteRegistrations.Single(x => x.Id == r.RegistrationId);
        Assert.That(Db.TailBiteRegistrationPhotos.Count(p => p.RegistrationClientUuid == regUuid), Is.EqualTo(2));
        Assert.That(Db.TailBiteRegistrationPhotos.Where(TailBitePhotoOwnership.BelongsTo(reg)).Select(p => p.PhotoUuid).ToList(),
            Is.EquivalentTo(new[] { photo, second }));
    }

    [Test]
    public async Task Photo_FromOtherWorker_NotAttachedToRegistration()
    {
        await SeedWorkerAsync(OtherSiteId);
        var sut = NewSut();
        var regUuid = Guid.NewGuid();
        // the other worker uploads first, naming the not-yet-synced registration uuid
        await sut.SavePhotoAsync(OtherSiteId, PropertyId, Guid.NewGuid(), regUuid, [1], "image/jpeg");
        var mine = Guid.NewGuid();
        await sut.SavePhotoAsync(WorkerSiteId, PropertyId, mine, regUuid, [2], "image/jpeg");
        var r = await sut.CreateAsync(WorkerSiteId, Cmd(regUuid));

        var reg = Db.TailBiteRegistrations.Single(x => x.Id == r.RegistrationId);
        Assert.That(Db.TailBiteRegistrationPhotos.Where(TailBitePhotoOwnership.BelongsTo(reg)).Select(p => p.PhotoUuid).ToList(),
            Is.EqualTo(new[] { mine }));
        // once the registration exists, the other worker cannot attach to it at all
        await Assert.ThrowsAsync<TailBiteForbiddenException>(
            () => sut.SavePhotoAsync(OtherSiteId, PropertyId, Guid.NewGuid(), regUuid, [3], "image/jpeg"));
    }

    [Test]
    public async Task GetPhoto_OtherProperty_Forbidden()
    {
        var other = new Property { Name = "Ejendom To" };
        await other.Create(Db);
        await new PropertyWorker { PropertyId = other.Id, WorkerId = OtherSiteId }.Create(Db); // works elsewhere only
        var photo = Guid.NewGuid();
        await NewSut().SavePhotoAsync(WorkerSiteId, PropertyId, photo, Guid.NewGuid(), [1], "image/jpeg");
        await Assert.ThrowsAsync<TailBiteForbiddenException>(() => NewSut().GetPhotoAsync(OtherSiteId, photo));
    }
}
