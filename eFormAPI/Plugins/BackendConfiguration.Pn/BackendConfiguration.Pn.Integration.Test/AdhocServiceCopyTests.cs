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

using System;
using System.Collections.Generic;
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
/// Drives <see cref="BackendConfigurationAdhocService.CopyTask"/> (M5/P3).
/// Photo duplication needs a real SDK Core (photos are created via
/// <c>SavePhoto</c>, same as <c>AdhocServicePhotoTests</c>), so every test
/// wires <see cref="TestBaseSetup.GetCore"/> through the substituted
/// <see cref="IEFormCoreService"/>.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class AdhocServiceCopyTests : TestBaseSetup
{
    // See AdhocServiceReferenceDataTests' identical [SetUp] doc comment for
    // why this fixture-local cleanup exists.
    [SetUp]
    public async Task CleanAdhocTables()
    {
        BackendConfigurationPnDbContext!.AdhocTaskTags.RemoveRange(
            BackendConfigurationPnDbContext.AdhocTaskTags);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AdhocTaskAssignments.RemoveRange(
            BackendConfigurationPnDbContext.AdhocTaskAssignments);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AdhocTaskAssignmentLogs.RemoveRange(
            BackendConfigurationPnDbContext.AdhocTaskAssignmentLogs);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AdhocTaskComments.RemoveRange(
            BackendConfigurationPnDbContext.AdhocTaskComments);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AdhocTaskPhotos.RemoveRange(
            BackendConfigurationPnDbContext.AdhocTaskPhotos);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AdhocTasks.RemoveRange(
            BackendConfigurationPnDbContext.AdhocTasks);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AdhocTags.RemoveRange(
            BackendConfigurationPnDbContext.AdhocTags);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        BackendConfigurationPnDbContext.AdhocAreas.RemoveRange(
            BackendConfigurationPnDbContext.AdhocAreas);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private BackendConfigurationAdhocService CreateSut(eFormCore.Core core)
    {
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        return new BackendConfigurationAdhocService(
            BackendConfigurationPnDbContext!,
            new BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext!),
            coreHelper,
            new FakeAdhocPhotoStorage());
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

    private static AdhocTaskCreateModel MakeCreateModel(
        int propertyId,
        List<int>? tagIds = null,
        List<int>? assignedWorkerIds = null)
    {
        return new AdhocTaskCreateModel
        {
            Title = "Original title",
            Description = "Original description",
            Urgent = true,
            PropertyId = propertyId,
            TagIds = tagIds ?? [],
            AssignedWorkerIds = assignedWorkerIds ?? [],
        };
    }

    [Test]
    public async Task CopyTask_DuplicatesCoreFields_AndStampsCopierAsCreator()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var source = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);

        // Copier is worker 9 (not the original creator, worker 1).
        var copy = await sut.CopyTask(9, isAdmin: true, source.Id, includeComments: false);

        Assert.That(copy.Id, Is.Not.EqualTo(source.Id));
        Assert.That(copy.Title, Is.EqualTo("Original title"));
        Assert.That(copy.Description, Is.EqualTo("Original description"));
        Assert.That(copy.Urgent, Is.True);
        Assert.That(copy.PropertyId, Is.EqualTo(property.Id));
        Assert.That(copy.CreatedByWorkerId, Is.EqualTo(9));
    }

    [Test]
    public async Task CopyTask_StartsUncompletedAndUnarchived_EvenWhenSourceWasArchived()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var source = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        await sut.SetCompleted(1, source.Id, true, isAdmin: true);
        await sut.Archive(1, source.Id, isAdmin: true);

        var copy = await sut.CopyTask(1, isAdmin: true, source.Id, includeComments: false);

        Assert.That(copy.Completed, Is.False);
        Assert.That(copy.CompletedAt, Is.Null);
        Assert.That(copy.Archived, Is.False);
        Assert.That(copy.ArchivedAt, Is.Null);
    }

    [Test]
    public async Task CopyTask_DuplicatesTagsAndAssignments_WithFreshAssignmentLogEntry()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 7);
        var core = await GetCore();
        var sut = CreateSut(core);
        var tag = await sut.CreateTag(1, "urgent");
        var source = await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tag.Id], assignedWorkerIds: [7]), isAdmin: true);

        var copy = await sut.CopyTask(1, isAdmin: true, source.Id, includeComments: false);

        Assert.That(copy.TagIds, Is.EquivalentTo(new[] { tag.Id }));
        Assert.That(copy.AssignedWorkerIds, Is.EquivalentTo(new[] { 7 }));
        Assert.That(copy.AssignmentLog, Has.Count.EqualTo(1));
        Assert.That(copy.AssignmentLog.Single().ToWorkerIds, Is.EquivalentTo(new[] { 7 }));
    }

    [Test]
    public async Task CopyTask_DuplicatesPhotos_SharingTheSameUploadedDataId()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var source = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        var photoBytes = Encoding.UTF8.GetBytes("photo-bytes");
        var photoId = await sut.SavePhoto(1, source.Id, photoBytes, "image/png", isAdmin: true);
        var sourcePhotoRow = await BackendConfigurationPnDbContext!.AdhocTaskPhotos.FirstAsync(p => p.Id == photoId);

        var copy = await sut.CopyTask(1, isAdmin: true, source.Id, includeComments: false);

        Assert.That(copy.Photos, Has.Count.EqualTo(1));
        var copiedPhotoRow = await BackendConfigurationPnDbContext.AdhocTaskPhotos
            .FirstAsync(p => p.Id == copy.Photos.Single().Id);
        Assert.That(copiedPhotoRow.Id, Is.Not.EqualTo(sourcePhotoRow.Id));
        Assert.That(copiedPhotoRow.UploadedDataId, Is.EqualTo(sourcePhotoRow.UploadedDataId));

        // The shared bytes are still retrievable through EITHER task's photo row.
        var (sourceContent, _) = await sut.GetPhoto(1, photoId, isAdmin: true);
        using var sourceStream = new System.IO.MemoryStream();
        await sourceContent.CopyToAsync(sourceStream);
        Assert.That(sourceStream.ToArray(), Is.EqualTo(photoBytes));
    }

    [Test]
    public async Task CopyTask_DeletingSourcePhoto_DoesNotBreakTheCopiedSiblingPhoto()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var source = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        var photoId = await sut.SavePhoto(1, source.Id, Encoding.UTF8.GetBytes("bytes"), "image/png", isAdmin: true);
        var copy = await sut.CopyTask(1, isAdmin: true, source.Id, includeComments: false);
        var copiedPhotoId = copy.Photos.Single().Id;

        // Soft-delete the SOURCE task's photo row (e.g. via UpdateTask's
        // photo-list reconciliation omitting it) - the sibling copy's photo
        // row, sharing the same UploadedDataId, must remain readable.
        var updateModel = MakeCreateModel(property.Id); // PhotoIds defaults to [] - omits photoId.
        await sut.UpdateTask(1, source.Id, updateModel, isAdmin: true);

        var (content, _) = await sut.GetPhoto(1, copiedPhotoId, isAdmin: true);
        Assert.That(content, Is.Not.Null);
    }

    [Test]
    public async Task CopyTask_ExcludesComments_WhenIncludeCommentsIsFalse()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var source = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        await sut.AddComment(1, source.Id, "a comment", isAdmin: true);

        var copy = await sut.CopyTask(1, isAdmin: true, source.Id, includeComments: false);

        Assert.That(copy.Comments, Is.Empty);
    }

    [Test]
    public async Task CopyTask_IncludesComments_PreservingAuthorAndTextVerbatim_WhenIncludeCommentsIsTrue()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 7);
        var core = await GetCore();
        var sut = CreateSut(core);
        var source = await sut.CreateTask(1, MakeCreateModel(property.Id));
        await sut.AddComment(7, source.Id, "from the other worker", isAdmin: true);

        var copy = await sut.CopyTask(1, isAdmin: true, source.Id, includeComments: true);

        Assert.That(copy.Comments, Has.Count.EqualTo(1));
        var copiedComment = copy.Comments.Single();
        Assert.That(copiedComment.Text, Is.EqualTo("from the other worker"));
        Assert.That(copiedComment.AuthorWorkerId, Is.EqualTo(7));
    }

    [Test]
    public async Task CopyTask_Throws_ForWorkerWithoutVisibilityToSource()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 99);
        var core = await GetCore();
        var sut = CreateSut(core);
        var source = await sut.CreateTask(1, MakeCreateModel(property.Id));

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.CopyTask(99, isAdmin: false, source.Id, includeComments: false));
    }

    [Test]
    public async Task CopyTask_IsAdmin_BypassesVisibilityCheck()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var core = await GetCore();
        var sut = CreateSut(core);
        var source = await sut.CreateTask(1, MakeCreateModel(property.Id));

        // Dashboard caller identity (workerId 0) has no PropertyWorker row.
        var copy = await sut.CopyTask(0, isAdmin: true, source.Id, includeComments: false);

        Assert.That(copy.Id, Is.Not.EqualTo(source.Id));
    }

    [Test]
    public void CopyTask_Throws_NotFound_ForUnknownTask()
    {
        var sut = CreateSut(null!);

        Assert.ThrowsAsync<AdhocTaskNotFoundException>(async () =>
            await sut.CopyTask(1, isAdmin: true, 987654, includeComments: false));
    }
}
