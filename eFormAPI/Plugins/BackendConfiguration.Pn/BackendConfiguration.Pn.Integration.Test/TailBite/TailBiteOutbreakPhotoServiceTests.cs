using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.GrpcServices;
using BackendConfiguration.Pn.Services.TailBite;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

[TestFixture]
public class TailBiteOutbreakPhotoServiceTests : TailBiteTestBase
{
    private const int ManagerSite = 7;   // also the site SeedRegistrationAsync registers as
    private const int WorkerSite = 8;
    private const int ForeignSite = 9;   // a tail-bite manager, but of another enabled property

    private ITailBitePhotoStorage _storage = null!;

    [SetUp]
    public async Task SeedDefaults()
    {
        await SeedTreeAsync();
        await SeedWorkerAsync(ManagerSite, manager: true);
        await SeedWorkerAsync(WorkerSite);
        var other = new Property { Name = "Ejendom To" };
        await other.Create(Db);
        await new TailBiteProperty { PropertyId = other.Id, Enabled = true, EnabledAt = Now }.Create(Db);
        await new PropertyWorker { PropertyId = other.Id, WorkerId = ForeignSite, TailBiteManager = true }.Create(Db);
        _storage = Substitute.For<ITailBitePhotoStorage>();
        _storage.OpenAsync(Arg.Any<int>()).Returns(call => ((Stream)new MemoryStream([1, 2, 3]), "image/jpeg"));
    }

    private TailBiteOutbreakPhotoService Sut()
    {
        var resolver = Substitute.For<IGrpcSiteResolver>();
        resolver.GetDisplayNameAsync(ManagerSite).Returns("Jane Doe");
        return new(Db, NewAccess(), new TailBitePhotoQuery(Db), _storage, resolver);
    }

    private DateTime Now => Clock.GetUtcNow().UtcDateTime;

    private async Task<TailBiteOutbreak> OutbreakWithLinksAsync(params int[] rowIds)
    {
        var outbreak = new TailBiteOutbreak
        {
            PropertyId = PropertyId, LocationId = StableAId, RuleId = RuleId, RuleVersion = 1, OpenedAt = Now, OpenedByRegistrationId = 1
        };
        await outbreak.Create(Db);
        foreach (var rowId in rowIds)
            await new TailBiteOutbreakLink { OutbreakId = outbreak.Id, RegistrationLocationId = rowId }.Create(Db);
        return outbreak;
    }

    private async Task<Guid> PhotoAsync(TailBiteRegistration reg, int sdkUploadedDataId = 101)
    {
        var photo = new TailBiteRegistrationPhoto
        {
            PhotoUuid = Guid.NewGuid(), PropertyId = reg.PropertyId, UploadedBySiteId = reg.SiteId,
            RegistrationClientUuid = reg.ClientUuid, SdkUploadedDataId = sdkUploadedDataId
        };
        await photo.Create(Db);
        return photo.PhotoUuid;
    }

    private async Task SwitchOffAsync()
        => await Db.TailBiteProperties.Where(t => t.PropertyId == PropertyId).ExecuteUpdateAsync(x => x.SetProperty(t => t.Enabled, false));

    private static async Task AssertPhotoNotFound(Func<Task> call, string because)
    {
        var e = await Assert.ThrowsAsync<TailBiteNotFoundException>(() => call(), because);
        Assert.That(e!.Message, Is.EqualTo("Photo not found."), because);
    }

    [Test]
    public async Task List_ManagerSeesNewestFirst_WithPensAndName()
    {
        var (older, olderRows) = await SeedRegistrationAsync(Now.AddDays(-1), false, (Pen309Id, 2, 0));
        var (newer, newerRows) = await SeedRegistrationAsync(Now.AddHours(-1), false, (Pen309Id, 1, 0), (Pen310Id, 1, 1));
        var (cancelled, cancelledRows) = await SeedRegistrationAsync(Now.AddHours(-2), true, (Pen310Id, 3, 0));
        var olderPhoto = await PhotoAsync(older);
        var newerFirst = await PhotoAsync(newer);
        var newerSecond = await PhotoAsync(newer);
        await PhotoAsync(cancelled);
        var outbreak = await OutbreakWithLinksAsync(olderRows[0], newerRows[0], newerRows[1], cancelledRows[0]);

        var photos = await Sut().ListAsync(ManagerSite, outbreak.Id);

        // A registration covering two pens is listed once per photo, with both pens; the cancelled registration's photo is left out.
        Assert.That(photos.Select(p => (p.PhotoUuid, p.RegistrationId)), Is.EqualTo(new[]
        {
            (newerFirst, newer.Id), (newerSecond, newer.Id), (olderPhoto, older.Id),
        }));
        Assert.That(photos[0].LocationIds, Is.EqualTo(new[] { Pen309Id, Pen310Id }));
        Assert.That(photos[2].LocationIds, Is.EqualTo(new[] { Pen309Id }));
        Assert.That(photos.Select(p => (p.SiteId, p.SiteName)).Distinct(), Is.EqualTo(new[] { (ManagerSite, "Jane Doe") }));
        Assert.That(photos[0].EffectiveAt, Is.EqualTo(Now.AddHours(-1)));
        Assert.That(photos[0].EffectiveAt.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public async Task List_WorkerForeignAndSwitchedOffRefusedAlike()
    {
        var (reg, rows) = await SeedRegistrationAsync(Now.AddHours(-1), false, (Pen309Id, 2, 0));
        await PhotoAsync(reg);
        var outbreak = await OutbreakWithLinksAsync(rows[0]);
        Assert.That(await Sut().ListAsync(ManagerSite, outbreak.Id), Has.Count.EqualTo(1));

        await AssertRefusedAlike(
            () => Sut().ListAsync(ManagerSite, int.MaxValue),
            () => Sut().ListAsync(WorkerSite, outbreak.Id));
        await AssertRefusedAlike(
            () => Sut().ListAsync(ManagerSite, int.MaxValue),
            () => Sut().ListAsync(ForeignSite, outbreak.Id));
        await SwitchOffAsync();
        await AssertRefusedAlike(
            () => Sut().ListAsync(ManagerSite, int.MaxValue),
            () => Sut().ListAsync(ManagerSite, outbreak.Id));
    }

    [Test]
    public async Task OpenForManager_ManagerGetsTheStoredBytes()
    {
        var (reg, _) = await SeedRegistrationAsync(Now.AddHours(-1), false, (Pen309Id, 2, 0));
        var photo = await PhotoAsync(reg, sdkUploadedDataId: 202);

        var (content, contentType) = await Sut().OpenForManagerAsync(ManagerSite, photo);

        await using (content) Assert.That(contentType, Is.EqualTo("image/jpeg"));
        await _storage.Received().OpenAsync(202);
    }

    [Test]
    public async Task OpenForManager_WorkerForeignSwitchedOffCancelledAndUnknown_AllNotFound()
    {
        var (reg, _) = await SeedRegistrationAsync(Now.AddHours(-1), false, (Pen309Id, 2, 0));
        var (cancelled, _) = await SeedRegistrationAsync(Now.AddHours(-2), true, (Pen310Id, 2, 0));
        var shown = await PhotoAsync(reg);
        var ofCancelled = await PhotoAsync(cancelled);

        await AssertPhotoNotFound(() => Sut().OpenForManagerAsync(WorkerSite, shown), "worker");
        await AssertPhotoNotFound(() => Sut().OpenForManagerAsync(ForeignSite, shown), "foreign");
        await AssertPhotoNotFound(() => Sut().OpenForManagerAsync(ManagerSite, ofCancelled), "cancelled");
        await AssertPhotoNotFound(() => Sut().OpenForManagerAsync(ManagerSite, Guid.NewGuid()), "unknown");
        await SwitchOffAsync();
        await AssertPhotoNotFound(() => Sut().OpenForManagerAsync(ManagerSite, shown), "switched off");
        await _storage.DidNotReceiveWithAnyArgs().OpenAsync(default);
    }
}
