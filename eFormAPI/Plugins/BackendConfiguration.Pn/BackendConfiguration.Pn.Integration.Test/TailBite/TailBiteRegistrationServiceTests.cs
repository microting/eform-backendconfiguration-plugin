using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Microting.eForm.Infrastructure.Constants;
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

    private TailBiteRegistrationService NewSut(ITailBiteOutbreakNotifier? notifier = null, BackendConfigurationPnDbContext? db = null,
        IAdhocPhotoStorage? storage = null, TimeProvider? clock = null)
    {
        db ??= Db;
        return new(db, new TailBitePropertyLock(db), new TailBiteSnapshotLoader(db), new TailBiteDecisionWriter(db), NewAccess(db),
            new TailBitePhotoStorage(storage ?? new FakeAdhocPhotoStorage(), CoreHelper()), clock ?? Clock, notifier);
    }

    /// <summary>
    /// Counts stored blobs. Can fail the next put; or, after storing, signal <see cref="Entered"/>, hold until
    /// <see cref="Gate"/> opens and then fail (<see cref="FailAfterGate"/>).
    /// </summary>
    private sealed class CountingPhotoStorage : IAdhocPhotoStorage
    {
        private readonly FakeAdhocPhotoStorage _inner = new();
        private int _puts;

        public int Puts => _puts;
        public bool FailNext { get; set; }
        public bool FailAfterGate { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task PutAsync(string fileName, Stream content)
        {
            Interlocked.Increment(ref _puts);
            if (FailNext)
            {
                FailNext = false;
                throw new IOException("Simulated storage failure.");
            }
            await _inner.PutAsync(fileName, content);
            Entered.TrySetResult();
            if (Gate is not null) await Gate.Task;
            if (FailAfterGate) throw new IOException("Simulated storage failure after the bytes were written.");
        }

        public Task<Stream> GetAsync(string fileName) => _inner.GetAsync(fileName);
    }

    /// <summary>Runs an action once, right before the first command whose SQL contains the given text.</summary>
    private sealed class BeforeCommand(string sqlContains, Func<Task> action) : DbCommandInterceptor
    {
        private bool _done;

        private async Task RunOnceAsync(DbCommand command)
        {
            if (_done || !command.CommandText.Contains(sqlContains)) return;
            _done = true;
            await action();
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await RunOnceAsync(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await RunOnceAsync(command);
            return result;
        }
    }

    private const string InsertPhoto = "INSERT INTO `TailBiteRegistrationPhotos`";
    private const string UpdatePhoto = "UPDATE `TailBiteRegistrationPhotos`";

    // A photo row written directly: a placeholder by default, optionally released (removed) or with a set UpdatedAt.
    private async Task<TailBiteRegistrationPhoto> SeedPhotoAsync(Guid photo, Guid regUuid, int siteId = WorkerSiteId, int uploadedDataId = 0,
        bool removed = false, DateTime? updatedAt = null)
    {
        var row = new TailBiteRegistrationPhoto { PhotoUuid = photo, PropertyId = PropertyId, UploadedBySiteId = siteId,
            RegistrationClientUuid = regUuid, SdkUploadedDataId = uploadedDataId };
        await row.Create(Db);
        if (removed) await row.Delete(Db);
        if (updatedAt is { } at)
            await Db.TailBiteRegistrationPhotos.Where(p => p.Id == row.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.UpdatedAt, at));
        Db.ChangeTracker.Clear();
        return row;
    }

    private TailBiteRegistrationPhoto PhotoRow(Guid photo) => Db.TailBiteRegistrationPhotos.AsNoTracking().Single(p => p.PhotoUuid == photo);

    private static async Task AssertInProgress(Func<Task> call)
    {
        var ex = await Assert.ThrowsAsync<TailBiteConflictException>(() => call());
        Assert.That(ex!.Message, Is.EqualTo("Photo upload in progress; retry shortly."));
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
    public async Task Create_CountAbove10000_Rejected_NothingStored()
    {
        await Assert.ThrowsAsync<TailBiteValidationException>(() => NewSut().CreateAsync(WorkerSiteId, Cmd(minor: int.MaxValue)));
        await Assert.ThrowsAsync<TailBiteValidationException>(() => NewSut().CreateAsync(WorkerSiteId, Cmd(minor: 0, severe: 10001)));
        Assert.That(Db.TailBiteRegistrations.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task Create_CommentLongerThan2000_Rejected_2000Allowed()
    {
        await Assert.ThrowsAsync<TailBiteValidationException>(() => NewSut().CreateAsync(WorkerSiteId, Cmd() with { Comment = new string('x', 2001) }));
        Assert.That(Db.TailBiteRegistrations.Count(), Is.EqualTo(0));
        await Assert.DoesNotThrowAsync(() => NewSut().CreateAsync(WorkerSiteId, Cmd() with { Comment = new string('x', 2000) }));
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
    public async Task Create_RegisteredAtBefore2000_ValidationError()
    {
        var ex = await Assert.ThrowsAsync<TailBiteValidationException>(
            () => NewSut().CreateAsync(WorkerSiteId, Cmd(at: new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc))));
        Assert.That(ex!.Message, Is.EqualTo("registered_at is out of range."));
        Assert.That(Db.TailBiteRegistrations.Count(), Is.Zero);
    }

    [Test]
    public async Task Create_RegisteredAtExactly2000_Accepted()
    {
        var at = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var r = await NewSut().CreateAsync(WorkerSiteId, Cmd(at: at));
        Assert.That(Db.TailBiteRegistrations.Single(x => x.Id == r.RegistrationId).RegisteredAt, Is.EqualTo(at));
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

    [Test]
    public async Task Photo_ReplayBySameOwner_ReturnsSameUuid_StoresOnce()
    {
        var storage = new CountingPhotoStorage();
        var sut = NewSut(storage: storage);
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());
        Assert.That(await sut.SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"), Is.EqualTo(photo));
        Assert.That(await sut.SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"), Is.EqualTo(photo));
        Assert.That(storage.Puts, Is.EqualTo(1));
        Assert.That(Db.TailBiteRegistrationPhotos.Single(p => p.PhotoUuid == photo).SdkUploadedDataId, Is.Not.Zero);
    }

    [Test]
    public async Task Photo_SameUuidByAnotherWorker_Conflict()
    {
        await SeedWorkerAsync(OtherSiteId);
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());
        await NewSut().SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg");
        var ex = await Assert.ThrowsAsync<TailBiteConflictException>(
            () => NewSut().SavePhotoAsync(OtherSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"));
        Assert.That(ex!.Message, Is.EqualTo("This photo id is already in use."));
    }

    [Test]
    public async Task Photo_SameUuidSameWorkerOtherRegistration_Conflict()
    {
        var photo = Guid.NewGuid();
        await NewSut().SavePhotoAsync(WorkerSiteId, PropertyId, photo, Guid.NewGuid(), [1], "image/jpeg");
        var ex = await Assert.ThrowsAsync<TailBiteConflictException>(
            () => NewSut().SavePhotoAsync(WorkerSiteId, PropertyId, photo, Guid.NewGuid(), [1], "image/jpeg"));
        Assert.That(ex!.Message, Is.EqualTo("This photo id is already in use."));
    }

    [Test]
    public async Task Photo_StorageFails_LeavesNoLiveReservation_AndRetryStores()
    {
        var storage = new CountingPhotoStorage { FailNext = true };
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());
        await Assert.ThrowsAsync<IOException>(() => NewSut(storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"));
        Db.ChangeTracker.Clear();
        var released = Db.TailBiteRegistrationPhotos.AsNoTracking().Single(p => p.PhotoUuid == photo);
        Assert.That((released.WorkflowState, released.SdkUploadedDataId), Is.EqualTo((Constants.WorkflowStates.Removed, 0)));
        await Assert.ThrowsAsync<TailBiteNotFoundException>(() => NewSut().GetPhotoAsync(WorkerSiteId, photo));

        // The client retries the same photo: the released reservation is claimed again and the bytes are stored.
        Assert.That(await NewSut(storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"), Is.EqualTo(photo));
        Db.ChangeTracker.Clear();
        var stored = Db.TailBiteRegistrationPhotos.AsNoTracking().Single(p => p.PhotoUuid == photo);
        Assert.That(stored.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(stored.SdkUploadedDataId, Is.Not.Zero);
        Assert.That(storage.Puts, Is.EqualTo(2));
    }

    [Test]
    public async Task Photo_Placeholder_IsNotFound_AndNotAttached()
    {
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedPhotoAsync(photo, regUuid);
        await Assert.ThrowsAsync<TailBiteNotFoundException>(() => NewSut().GetPhotoAsync(WorkerSiteId, photo));
        var r = await NewSut().CreateAsync(WorkerSiteId, Cmd(regUuid));
        var reg = Db.TailBiteRegistrations.Single(x => x.Id == r.RegistrationId);
        Assert.That(Db.TailBiteRegistrationPhotos.Where(TailBitePhotoOwnership.BelongsTo(reg)).Any(), Is.False);
    }

    [Test]
    public async Task Photo_ConcurrentDuplicate_StoresBytesOnce_LoserRetriesLater()
    {
        var storage = new CountingPhotoStorage { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var dbB = CreateFreshBackendConfigurationDbContext();
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());

        // A reserves the photo and is held while storing its bytes; B uploads the same photo meanwhile.
        var a = NewSut(storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1, 2], "image/jpeg");
        await storage.Entered.Task;
        await AssertInProgress(() => NewSut(db: dbB, storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1, 2], "image/jpeg"));
        storage.Gate.SetResult();
        Assert.That(await a, Is.EqualTo(photo));

        // B's retry after A finished is a replay.
        Assert.That(await NewSut(db: dbB, storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1, 2], "image/jpeg"),
            Is.EqualTo(photo));
        Assert.That(storage.Puts, Is.EqualTo(1));
        Assert.That(PhotoRow(photo).SdkUploadedDataId, Is.Not.Zero);
    }

    [Test]
    public async Task Photo_FreshPlaceholder_InProgressConflict()
    {
        var storage = new CountingPhotoStorage();
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedPhotoAsync(photo, regUuid, updatedAt: Clock.GetUtcNow().UtcDateTime.AddMinutes(-1));
        await AssertInProgress(() => NewSut(storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"));
        Assert.That(storage.Puts, Is.Zero);
    }

    [Test]
    public async Task Photo_StalePlaceholder_TakenOverAndStoredOnce()
    {
        var storage = new CountingPhotoStorage();
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedPhotoAsync(photo, regUuid, updatedAt: Clock.GetUtcNow().UtcDateTime.AddMinutes(-10));
        Assert.That(await NewSut(storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"), Is.EqualTo(photo));
        Assert.That(await NewSut(storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"), Is.EqualTo(photo));
        Assert.That(storage.Puts, Is.EqualTo(1));
        Db.ChangeTracker.Clear();
        Assert.That(Db.TailBiteRegistrationPhotos.Count(p => p.PhotoUuid == photo), Is.EqualTo(1));
        Assert.That(PhotoRow(photo).SdkUploadedDataId, Is.Not.Zero);
    }

    [Test]
    public async Task Photo_LostClaimOfFailedReservation_InProgressConflict()
    {
        var storage = new CountingPhotoStorage();
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());
        var released = await SeedPhotoAsync(photo, regUuid, removed: true);
        // Another retry claims the reservation between this call's read and its conditional update.
        var rival = new BeforeCommand(UpdatePhoto, () => Db.TailBiteRegistrationPhotos.Where(p => p.Id == released.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.UpdatedAt, DateTime.UtcNow.AddSeconds(1))));
        await using var dbB = NewContext(interceptors: rival);
        await AssertInProgress(() => NewSut(db: dbB, storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"));
        Assert.That(storage.Puts, Is.Zero);
    }

    [Test]
    public async Task Photo_DuplicateKeyRace_ForeignWinner_Conflict()
    {
        await SeedWorkerAsync(OtherSiteId);
        var storage = new CountingPhotoStorage();
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());
        await using var dbB = NewContext(interceptors: new BeforeCommand(InsertPhoto, () => SeedPhotoAsync(photo, regUuid, OtherSiteId, 12345)));
        var ex = await Assert.ThrowsAsync<TailBiteConflictException>(
            () => NewSut(db: dbB, storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"));
        Assert.That(ex!.Message, Is.EqualTo("This photo id is already in use."));
        Assert.That(storage.Puts, Is.Zero);
    }

    [Test]
    public async Task Photo_DuplicateKeyRace_SameOwnerStored_Replays()
    {
        var storage = new CountingPhotoStorage();
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());
        await using var dbB = NewContext(interceptors: new BeforeCommand(InsertPhoto, () => SeedPhotoAsync(photo, regUuid, uploadedDataId: 12345)));
        Assert.That(await NewSut(db: dbB, storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"),
            Is.EqualTo(photo));
        Assert.That(storage.Puts, Is.Zero);
    }

    [Test]
    public async Task Photo_DuplicateKeyRace_SameOwnerPlaceholder_InProgressConflict()
    {
        var storage = new CountingPhotoStorage();
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());
        await using var dbB = NewContext(interceptors: new BeforeCommand(InsertPhoto, () => SeedPhotoAsync(photo, regUuid)));
        await AssertInProgress(() => NewSut(db: dbB, storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"));
        Assert.That(storage.Puts, Is.Zero);
    }

    [Test]
    public async Task Photo_DuplicateKeyRace_SameOwnerFailedReservation_InProgressConflict()
    {
        var storage = new CountingPhotoStorage();
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());
        await using var dbB = NewContext(interceptors: new BeforeCommand(InsertPhoto, () => SeedPhotoAsync(photo, regUuid, removed: true)));
        await AssertInProgress(() => NewSut(db: dbB, storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"));
        Assert.That(storage.Puts, Is.Zero);
    }

    [Test]
    public async Task Photo_UpdateAfterStoreFails_ReleasesReservation()
    {
        var storage = new CountingPhotoStorage();
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());
        // The update that records the stored bytes fails; the reservation must not stay live.
        var failing = new BeforeCommand(UpdatePhoto, () => throw new InvalidOperationException("Simulated failure recording the photo."));
        await using var dbB = NewContext(interceptors: failing);
        // EF may wrap the interceptor's exception in a DbUpdateException; either way the call fails.
        await Assert.CatchAsync(
            () => NewSut(db: dbB, storage: storage).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg"));
        var row = PhotoRow(photo);
        Assert.That((row.WorkflowState, row.SdkUploadedDataId), Is.EqualTo((Constants.WorkflowStates.Removed, 0)));
        Assert.That(storage.Puts, Is.EqualTo(1));
    }

    // ---------- a slow upload whose reservation is taken over ----------

    // The time a second upload sees: far enough after the first one's reservation to treat it as abandoned.
    private static FakeTimeProvider Later() => new(DateTimeOffset.UtcNow.AddMinutes(10));

    private async Task AssertStoredAndReadable(Guid photo, int expectedUploadedDataId, IAdhocPhotoStorage storage, byte[] expectedBytes)
    {
        var row = PhotoRow(photo);
        Assert.That(row.WorkflowState, Is.Not.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(row.SdkUploadedDataId, Is.EqualTo(expectedUploadedDataId).And.Not.Zero);
        var (content, _) = await NewSut(storage: storage).GetPhotoAsync(WorkerSiteId, photo);
        await using (content)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms);
            Assert.That(ms.ToArray(), Is.EqualTo(expectedBytes));
        }
        // The conditional writes keep the audit trail: the newest version row matches the live row.
        var last = Db.TailBiteRegistrationPhotoVersions.AsNoTracking().Where(v => v.TailBiteRegistrationPhotoId == row.Id)
            .OrderByDescending(v => v.Id).First();
        Assert.That((last.SdkUploadedDataId, last.WorkflowState, last.Version), Is.EqualTo((row.SdkUploadedDataId, row.WorkflowState, row.Version)));
    }

    [Test]
    public async Task Photo_TakenOver_SlowOriginalFinishesLater_TakeoverStands()
    {
        var storageA = new CountingPhotoStorage { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var storageB = new CountingPhotoStorage();
        await using var dbB = CreateFreshBackendConfigurationDbContext();
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());

        var a = NewSut(storage: storageA).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg");
        await storageA.Entered.Task; // A has stored its bytes and is held before recording them
        Assert.That(await NewSut(db: dbB, storage: storageB, clock: Later()).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [2], "image/jpeg"),
            Is.EqualTo(photo));
        var storedByB = PhotoRow(photo).SdkUploadedDataId;

        storageA.Gate.SetResult();
        Assert.That(await a, Is.EqualTo(photo)); // a replay of B's stored photo, not an overwrite
        await AssertStoredAndReadable(photo, storedByB, storageB, [2]);
    }

    [Test]
    public async Task Photo_TakenOver_TakeoverFails_SlowOriginalCompletes()
    {
        var storageA = new CountingPhotoStorage { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var storageB = new CountingPhotoStorage { FailNext = true };
        await using var dbB = CreateFreshBackendConfigurationDbContext();
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());

        var a = NewSut(storage: storageA).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg");
        await storageA.Entered.Task;
        await Assert.ThrowsAsync<IOException>(() =>
            NewSut(db: dbB, storage: storageB, clock: Later()).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [2], "image/jpeg"));
        Assert.That(PhotoRow(photo).WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed)); // B released what it took over

        storageA.Gate.SetResult();
        Assert.That(await a, Is.EqualTo(photo));
        var storedByA = PhotoRow(photo).SdkUploadedDataId;
        await AssertStoredAndReadable(photo, storedByA, storageA, [1]);
    }

    [Test]
    public async Task Photo_TakenOver_SlowOriginalFails_TakeoverStands()
    {
        var storageA = new CountingPhotoStorage
            { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), FailAfterGate = true };
        var storageB = new CountingPhotoStorage();
        await using var dbB = CreateFreshBackendConfigurationDbContext();
        var (photo, regUuid) = (Guid.NewGuid(), Guid.NewGuid());

        var a = NewSut(storage: storageA).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [1], "image/jpeg");
        await storageA.Entered.Task;
        Assert.That(await NewSut(db: dbB, storage: storageB, clock: Later()).SavePhotoAsync(WorkerSiteId, PropertyId, photo, regUuid, [2], "image/jpeg"),
            Is.EqualTo(photo));
        var storedByB = PhotoRow(photo).SdkUploadedDataId;

        storageA.Gate.SetResult();
        await Assert.ThrowsAsync<IOException>(() => a); // A's release no longer matches its stamp and does nothing
        await AssertStoredAndReadable(photo, storedByB, storageB, [2]);
    }
}
