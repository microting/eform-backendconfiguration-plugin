using System;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.TailBite;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Integration.Test.TailBite;

[TestFixture]
public class TailBitePhotoQueryTests : TailBiteTestBase
{
    private const int WorkerSiteId = 7; // the site SeedRegistrationAsync registers as
    private const int OtherSiteId = 8;

    [SetUp]
    public async Task SeedDefaults() => await SeedTreeAsync();

    private TailBitePhotoQuery Sut() => new(Db);

    private DateTime Now => Clock.GetUtcNow().UtcDateTime;

    // A photo row written directly: stored on the registration's property by its worker unless told otherwise.
    private async Task<Guid> SeedPhotoAsync(TailBiteRegistration reg, int uploadedDataId = 101, int? siteId = null,
        int? propertyId = null, Guid? registrationClientUuid = null, bool removed = false)
    {
        var row = new TailBiteRegistrationPhoto
        {
            PhotoUuid = Guid.NewGuid(), PropertyId = propertyId ?? reg.PropertyId, UploadedBySiteId = siteId ?? reg.SiteId,
            RegistrationClientUuid = registrationClientUuid ?? reg.ClientUuid, SdkUploadedDataId = uploadedDataId
        };
        await row.Create(Db);
        if (removed) await row.Delete(Db);
        return row.PhotoUuid;
    }

    [Test]
    public async Task ForRegistrations_OwnedStoredPhotosOnly()
    {
        var (reg, _) = await SeedRegistrationAsync(Now, false, (Pen309Id, 2, 0));
        var other = new Property { Name = "Ejendom To" };
        await other.Create(Db);
        var owned = await SeedPhotoAsync(reg, uploadedDataId: 101);
        await SeedPhotoAsync(reg, siteId: OtherSiteId);       // another worker's upload
        await SeedPhotoAsync(reg, propertyId: other.Id);      // uploaded on another property
        await SeedPhotoAsync(reg, uploadedDataId: 0);         // a reservation, no bytes
        await SeedPhotoAsync(reg, removed: true);

        var photos = await Sut().ForRegistrationsAsync([reg]);

        Assert.That(photos, Is.EqualTo(new[] { new RegistrationPhoto(owned, reg.Id, 101) }));
    }

    [Test]
    public async Task ForRegistrations_CancelledRegistrationHasNone()
    {
        var (live, _) = await SeedRegistrationAsync(Now, false, (Pen309Id, 2, 0));
        var (cancelled, _) = await SeedRegistrationAsync(Now, true, (Pen310Id, 2, 0));
        var shown = await SeedPhotoAsync(live);
        await SeedPhotoAsync(cancelled);

        var photos = await Sut().ForRegistrationsAsync([live, cancelled]);

        Assert.That(photos.Select(p => (p.PhotoUuid, p.RegistrationId)), Is.EqualTo(new[] { (shown, live.Id) }));
        Assert.That(await Sut().ForRegistrationsAsync([cancelled]), Is.Empty);
    }

    [Test]
    public async Task ForRegistrations_MultiPenRegistrationListedOnce()
    {
        var (reg, _) = await SeedRegistrationAsync(Now, false, (Pen309Id, 2, 0), (Pen310Id, 1, 1));
        var first = await SeedPhotoAsync(reg);
        var second = await SeedPhotoAsync(reg);

        // A caller flattening the registration's pen rows may hand the same registration in twice.
        var photos = await Sut().ForRegistrationsAsync([reg, reg]);

        Assert.That(photos.Select(p => p.PhotoUuid), Is.EqualTo(new[] { first, second }));
        Assert.That(photos.Select(p => p.RegistrationId), Is.All.EqualTo(reg.Id));
    }

    [Test]
    public async Task FindShown_NullForCancelledOrphanAndUnstored()
    {
        var (live, _) = await SeedRegistrationAsync(Now, false, (Pen309Id, 2, 0));
        var (cancelled, _) = await SeedRegistrationAsync(Now, true, (Pen310Id, 2, 0));
        var ofCancelled = await SeedPhotoAsync(cancelled);
        var orphan = await SeedPhotoAsync(live, registrationClientUuid: Guid.NewGuid()); // no registration with that uuid
        var otherSite = await SeedPhotoAsync(live, siteId: OtherSiteId);                  // the uuid matches, the uploader does not
        var unstored = await SeedPhotoAsync(live, uploadedDataId: 0);

        foreach (var uuid in new[] { ofCancelled, orphan, otherSite, unstored, Guid.NewGuid() })
            Assert.That(await Sut().FindShownAsync(uuid), Is.Null, uuid.ToString());
    }

    [Test]
    public async Task FindShown_ReturnsPhotoAndRegistration()
    {
        var (reg, _) = await SeedRegistrationAsync(Now, false, (Pen309Id, 2, 0));
        var photo = await SeedPhotoAsync(reg, uploadedDataId: 202);

        var found = await Sut().FindShownAsync(photo);

        Assert.That(found, Is.Not.Null);
        Assert.That((found!.PhotoUuid, found.RegistrationClientUuid, found.PropertyId, found.SdkUploadedDataId),
            Is.EqualTo((photo, reg.ClientUuid, PropertyId, 202)));
    }
}
