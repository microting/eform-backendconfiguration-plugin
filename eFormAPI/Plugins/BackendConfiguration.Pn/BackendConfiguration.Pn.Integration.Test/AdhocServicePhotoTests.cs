using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Adhoc;
using BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Drives <see cref="BackendConfigurationAdhocService"/>'s photo storage
/// methods (Task B4: <c>SavePhoto</c>/<c>GetPhoto</c>, and
/// <c>UpdateTask</c>'s photo-list reconciliation exercised end-to-end against
/// a photo actually created via <c>SavePhoto</c> rather than a bare row).
/// Storage is faked via <see cref="FakeAdhocPhotoStorage"/> - see that class's
/// doc comment for why (no S3/MinIO is available in this repo's local or CI
/// test environment) - except where a test passes its own storage (the
/// production selector over local disk, or one whose upload fails).
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class AdhocServicePhotoTests : TestBaseSetup
{
    private FakeAdhocPhotoStorage _photoStorage = new();

    /// <summary>
    /// SavePhoto/GetPhoto write the SDK's own <c>UploadedData</c> row via
    /// <c>coreHelper.GetCore().DbContextHelper</c>, so - unlike the other
    /// Adhoc*Tests fixtures, which never reach the SDK path - the substitute
    /// must be wired to a real <see cref="eFormCore.Core"/> from
    /// <see cref="TestBaseSetup.GetCore"/> (same pattern as
    /// EventDeployServiceTest/CalendarMultiLanguageTitleDescriptionTests).
    /// An unstubbed <c>Substitute.For&lt;IEFormCoreService&gt;()</c> returns a
    /// null Core, which NREs the moment SavePhoto dereferences it.
    /// </summary>
    private BackendConfigurationAdhocService CreateSut(eFormCore.Core core, IAdhocPhotoStorage? photoStorage = null)
    {
        _photoStorage = new FakeAdhocPhotoStorage();
        return new BackendConfigurationAdhocService(
            BackendConfigurationPnDbContext!,
            new BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext!),
            CoreHelperFor(core),
            photoStorage ?? _photoStorage);
    }

    private static IEFormCoreService CoreHelperFor(eFormCore.Core core)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return coreHelper;
    }

    private async Task<Property> CreatePropertyAsync()
    {
        var property = new Property
        {
            Name = Guid.NewGuid().ToString(),
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        return property;
    }

    private async Task GrantPropertyAccessAsync(int propertyId, int workerId)
    {
        var propertyWorker = new PropertyWorker
        {
            PropertyId = propertyId,
            WorkerId = workerId,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
        };
        await BackendConfigurationPnDbContext!.PropertyWorkers.AddAsync(propertyWorker);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private static AdhocTaskCreateModel MakeCreateModel(int propertyId, List<int>? assignedWorkerIds = null, List<int>? photoIds = null)
    {
        return new AdhocTaskCreateModel
        {
            Title = Guid.NewGuid().ToString(),
            Description = "desc",
            PropertyId = propertyId,
            AssignedWorkerIds = assignedWorkerIds ?? [],
            PhotoIds = photoIds ?? [],
        };
    }

    private static byte[] SomeBytes(string seed = "photo-bytes") => Encoding.UTF8.GetBytes(seed);

    // --- SavePhoto ---

    [Test]
    public async Task SavePhoto_PersistsUploadedDataAndPhotoRow_ReturnsPhotoId()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var core = await GetCore();
        var sut = CreateSut(core);
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        var photoId = await sut.SavePhoto(1, created.Id, SomeBytes(), "image/png");

        Assert.That(photoId, Is.GreaterThan(0));

        var photoRow = await BackendConfigurationPnDbContext!.AdhocTaskPhotos
            .FirstAsync(p => p.Id == photoId);
        Assert.That(photoRow.AdhocTaskId, Is.EqualTo(created.Id));
        Assert.That(photoRow.ContentType, Is.EqualTo("image/png"));
        Assert.That(photoRow.UploadedDataId, Is.GreaterThan(0));
    }

    [Test]
    public async Task SavePhoto_RoundTrips_ThroughGetPhoto()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var core = await GetCore();
        var sut = CreateSut(core);
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
        var bytes = SomeBytes("round-trip-bytes");

        var photoId = await sut.SavePhoto(1, created.Id, bytes, "image/jpeg");
        var (content, contentType) = await sut.GetPhoto(1, photoId);

        Assert.That(contentType, Is.EqualTo("image/jpeg"));
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        Assert.That(ms.ToArray(), Is.EqualTo(bytes));
    }

    [Test]
    public async Task SavePhoto_AllowsAssignedWorker_NotJustCreator()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 7);
        var core = await GetCore();
        var sut = CreateSut(core);
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, assignedWorkerIds: [7]));

        var photoId = await sut.SavePhoto(7, created.Id, SomeBytes(), "image/png");

        Assert.That(photoId, Is.GreaterThan(0));
    }

    [Test]
    public async Task SavePhoto_Throws_ForWorkerWithoutVisibility()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 99);
        var core = await GetCore();
        var sut = CreateSut(core);
        // assignedOnly, not assigned to 99, not the creator -> not visible.
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.SavePhoto(99, created.Id, SomeBytes(), "image/png"));
    }

    [Test]
    public async Task SavePhoto_IsAdmin_BypassesVisibilityCheck()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var core = await GetCore();
        var sut = CreateSut(core);
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        // Worker 0 (B6's dashboard caller identity) has no PropertyWorker row
        // and is not the creator/assigned/everyone-visible - isAdmin must still
        // let the upload through.
        var photoId = await sut.SavePhoto(0, created.Id, SomeBytes(), "image/png", isAdmin: true);

        Assert.That(photoId, Is.GreaterThan(0));
    }

    [Test]
    public async Task SavePhoto_Throws_NotFound_ForUnknownTask()
    {
        var core = await GetCore();
        var sut = CreateSut(core);

        Assert.ThrowsAsync<AdhocTaskNotFoundException>(async () =>
            await sut.SavePhoto(1, 987654, SomeBytes(), "image/png"));
    }

    [Test]
    public async Task SavePhoto_Throws_ForUnsupportedContentType()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var core = await GetCore();
        var sut = CreateSut(core);
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.SavePhoto(1, created.Id, SomeBytes(), "application/pdf"));
    }

    [Test]
    public async Task SavePhoto_Throws_ForEmptyBytes()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var core = await GetCore();
        var sut = CreateSut(core);
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.SavePhoto(1, created.Id, [], "image/png"));
    }

    [Test]
    public async Task SavePhoto_WhenStoragePutFails_RemovesUploadedDataRow_AndRethrows()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var core = await GetCore();
        var failure = new IOException("storage unavailable");
        var failingStorage = Substitute.For<IAdhocPhotoStorage>();
        failingStorage.PutAsync(Arg.Any<string>(), Arg.Any<Stream>()).Returns(Task.FromException(failure));
        var sut = CreateSut(core, failingStorage);
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
        // Unique bytes so the checksum below finds only this attempt's row.
        var bytes = SomeBytes(Guid.NewGuid().ToString());

        var thrown = Assert.ThrowsAsync<IOException>(async () =>
            await sut.SavePhoto(1, created.Id, bytes, "image/png"));
        Assert.That(thrown, Is.SameAs(failure));

        var photoRowExists = await BackendConfigurationPnDbContext!.AdhocTaskPhotos
            .IgnoreQueryFilters()
            .AnyAsync(p => p.AdhocTaskId == created.Id);
        Assert.That(photoRowExists, Is.False);

        var checksum = Convert.ToHexStringLower(MD5.HashData(bytes));
        var uploadedData = await MicrotingDbContext!.UploadedDatas
            .AsNoTracking()
            .SingleAsync(u => u.Checksum == checksum);
        Assert.That(uploadedData.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    /// <summary>
    /// With <c>s3Enabled=true</c> the production selector routes to S3; this
    /// Core started without S3, so the SDK has no client and the upload fails
    /// cleanly: the SDK's exception reaches the caller and the orphan row goes.
    /// </summary>
    [Test]
    public async Task SavePhoto_WithProductionStorage_WhenS3EnabledButNoClient_FailsAndRemovesUploadedDataRow()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        // GetCore() runs before the flip: a Core started with s3Enabled=true
        // would create the SDK's static S3 client for the rest of the run.
        var core = await GetCore();
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.s3Enabled, "true");
        try
        {
            var sut = CreateSut(core, new AdhocPhotoStorage(CoreHelperFor(core)));
            var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
            var bytes = SomeBytes(Guid.NewGuid().ToString());

            var thrown = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await sut.SavePhoto(1, created.Id, bytes, "image/png"));
            Assert.That(thrown!.Message, Does.Contain("no S3 client"));

            var checksum = Convert.ToHexStringLower(MD5.HashData(bytes));
            var uploadedData = await MicrotingDbContext!.UploadedDatas
                .AsNoTracking()
                .SingleAsync(u => u.Checksum == checksum);
            Assert.That(uploadedData.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        }
        finally
        {
            // The SDK database outlives this test; restore 420_SDK.sql's value.
            await core.SetSdkSetting(Microting.eForm.Dto.Settings.s3Enabled, "false");
        }
    }

    /// <summary>
    /// An s3Enabled value that is neither true nor false (the SDK answers
    /// "N/A" when it can't read the setting) must not silently pick local disk.
    /// </summary>
    [Test]
    public async Task SavePhoto_WithProductionStorage_WhenS3SettingUnreadable_RefusesToChooseStorage()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var core = await GetCore();
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.s3Enabled, "N/A");
        try
        {
            var sut = CreateSut(core, new AdhocPhotoStorage(CoreHelperFor(core)));
            var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

            var thrown = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await sut.SavePhoto(1, created.Id, SomeBytes(Guid.NewGuid().ToString()), "image/png"));
            Assert.That(thrown!.Message, Does.Contain("Cannot choose adhoc photo storage"));
        }
        finally
        {
            await core.SetSdkSetting(Microting.eForm.Dto.Settings.s3Enabled, "false");
        }
    }

    /// <summary>
    /// Wires the production <see cref="AdhocPhotoStorage"/> selector instead
    /// of the fake: with <c>s3Enabled=false</c> it must route to
    /// <see cref="LocalAdhocPhotoStorage"/>, so the bytes land in (and come
    /// back from) the temp-dir file rather than failing on the SDK's missing
    /// S3 client.
    /// </summary>
    [Test]
    public async Task SavePhoto_WithProductionStorage_RoundTripsThroughLocalDisk_WhenS3Disabled()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var core = await GetCore();
        // 420_SDK.sql already seeds false; pinned so the test states its premise.
        await core.SetSdkSetting(Microting.eForm.Dto.Settings.s3Enabled, "false");
        var sut = CreateSut(core, new AdhocPhotoStorage(CoreHelperFor(core)));
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
        var bytes = SomeBytes(Guid.NewGuid().ToString());

        var photoId = await sut.SavePhoto(1, created.Id, bytes, "image/png");

        var photoRow = await BackendConfigurationPnDbContext!.AdhocTaskPhotos
            .FirstAsync(p => p.Id == photoId);
        var uploadedData = await MicrotingDbContext!.UploadedDatas
            .AsNoTracking()
            .FirstAsync(u => u.Id == photoRow.UploadedDataId);
        var localPath = Path.Combine(LocalAdhocPhotoStorage.DefaultRootDirectory, uploadedData.FileName);
        try
        {
            Assert.That(System.IO.File.Exists(localPath), Is.True);

            var (content, _) = await sut.GetPhoto(1, photoId);
            await using (content)
            {
                using var ms = new MemoryStream();
                await content.CopyToAsync(ms);
                Assert.That(ms.ToArray(), Is.EqualTo(bytes));
            }
        }
        finally
        {
            System.IO.File.Delete(localPath);
        }
    }

    // --- GetPhoto ---

    [Test]
    public async Task GetPhoto_Throws_NotFound_ForUnknownPhotoId()
    {
        var core = await GetCore();
        var sut = CreateSut(core);

        Assert.ThrowsAsync<AdhocTaskPhotoNotFoundException>(async () =>
            await sut.GetPhoto(1, 987654));
    }

    [Test]
    public async Task GetPhoto_Throws_NotFound_ForRemovedPhoto()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var core = await GetCore();
        var sut = CreateSut(core);
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
        var photoId = await sut.SavePhoto(1, created.Id, SomeBytes(), "image/png");

        // Omit the photo from the update's PhotoIds -> UpdateTask soft-deletes it.
        var updateModel = MakeCreateModel(property.Id);
        await sut.UpdateTask(1, created.Id, updateModel);

        Assert.ThrowsAsync<AdhocTaskPhotoNotFoundException>(async () =>
            await sut.GetPhoto(1, photoId));
    }

    [Test]
    public async Task GetPhoto_Throws_ForWorkerWithoutVisibility()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 99);
        var core = await GetCore();
        var sut = CreateSut(core);
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
        var photoId = await sut.SavePhoto(1, created.Id, SomeBytes(), "image/png");

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.GetPhoto(99, photoId));
    }

    [Test]
    public async Task GetPhoto_IsAdmin_BypassesVisibilityCheck()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var core = await GetCore();
        var sut = CreateSut(core);
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
        var bytes = SomeBytes("admin-bypass-bytes");
        var photoId = await sut.SavePhoto(1, created.Id, bytes, "image/png");

        // Worker 0 has no PropertyWorker row and is not creator/assigned -
        // isAdmin must still let the read through.
        var (content, contentType) = await sut.GetPhoto(0, photoId, isAdmin: true);

        Assert.That(contentType, Is.EqualTo("image/png"));
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        Assert.That(ms.ToArray(), Is.EqualTo(bytes));
    }

    // --- UpdateTask photo-list reconciliation, exercised against a photo
    // actually created via SavePhoto (B2 already covers the bare-row case
    // in AdhocServiceTaskCrudTests.UpdateTask_ReconcilesPhotos_SoftDeletesMissingIds). ---

    [Test]
    public async Task UpdateTask_KeepsPhotoCreatedViaSavePhoto_WhenIdIsResubmitted()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var core = await GetCore();
        var sut = CreateSut(core);
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
        var photoId = await sut.SavePhoto(1, created.Id, SomeBytes(), "image/png");

        var updateModel = MakeCreateModel(property.Id, photoIds: [photoId]);
        var updated = await sut.UpdateTask(1, created.Id, updateModel);

        Assert.That(updated.Photos.Select(p => p.Id), Is.EquivalentTo(new[] { photoId }));
        // Still retrievable - not soft-deleted.
        var (content, _) = await sut.GetPhoto(1, photoId);
        Assert.That(content, Is.Not.Null);
    }

    [Test]
    public async Task UpdateTask_SoftDeletesPhotoCreatedViaSavePhoto_WhenOmittedFromPhotoIds()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var core = await GetCore();
        var sut = CreateSut(core);
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
        var photoId = await sut.SavePhoto(1, created.Id, SomeBytes(), "image/png");

        var updateModel = MakeCreateModel(property.Id); // PhotoIds defaults to [] - omits photoId.
        var updated = await sut.UpdateTask(1, created.Id, updateModel);

        Assert.That(updated.Photos, Is.Empty);

        var photoRow = await BackendConfigurationPnDbContext!.AdhocTaskPhotos
            .IgnoreQueryFilters()
            .FirstAsync(p => p.Id == photoId);
        Assert.That(photoRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }
}
