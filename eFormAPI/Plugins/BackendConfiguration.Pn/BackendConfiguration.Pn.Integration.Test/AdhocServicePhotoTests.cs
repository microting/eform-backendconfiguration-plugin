using System;
using System.IO;
using System.Linq;
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
/// test environment).
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
    private BackendConfigurationAdhocService CreateSut(eFormCore.Core core)
    {
        _photoStorage = new FakeAdhocPhotoStorage();
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return new BackendConfigurationAdhocService(
            BackendConfigurationPnDbContext!,
            new BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext!),
            coreHelper,
            _photoStorage);
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
