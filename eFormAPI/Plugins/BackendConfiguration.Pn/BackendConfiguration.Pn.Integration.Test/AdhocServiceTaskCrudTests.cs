using System;
using System.Collections.Generic;
using System.Linq;
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

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class AdhocServiceTaskCrudTests : TestBaseSetup
{
    // FK-safe cleanup so each test starts fresh (mirrors
    // CalendarUpdateTaskScopeTests / CalendarTaskListIndexTest /
    // TaskListBatchWorkersTest). The Adhoc* tables were added after the raw
    // SQL bootstrap script (SQL/420_eform-backend-configuration-plugin.sql)
    // was last regenerated, so TestBaseSetup.Setup's DROP+CREATE pass never
    // touches them and rows would otherwise accumulate across every test in
    // this fixture.
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

    private BackendConfigurationAdhocService CreateSut()
    {
        return new BackendConfigurationAdhocService(
            BackendConfigurationPnDbContext!,
            new BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext!),
            Substitute.For<IEFormCoreService>(),
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

    private static AdhocTaskCreateModel MakeCreateModel(int propertyId, int? areaId = null, List<int>? assignedWorkerIds = null, List<int>? tagIds = null)
    {
        return new AdhocTaskCreateModel
        {
            Title = Guid.NewGuid().ToString(),
            Description = "desc",
            PropertyId = propertyId,
            AreaId = areaId,
            AssignedWorkerIds = assignedWorkerIds ?? [],
            TagIds = tagIds ?? [],
        };
    }

    [Test]
    public async Task CreateTask_StampsCreatedByAndAssignmentLog_WhenAssignmentsNonEmpty()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var model = MakeCreateModel(property.Id, assignedWorkerIds: [7, 9]);
        var result = await sut.CreateTask(1, model);

        Assert.That(result.CreatedByWorkerId, Is.EqualTo(1));
        Assert.That(result.AssignedWorkerIds, Is.EquivalentTo(new[] { 7, 9 }));
        Assert.That(result.AssignmentLog, Has.Count.EqualTo(1));
        Assert.That(result.AssignmentLog[0].ChangedByWorkerId, Is.EqualTo(1));
        Assert.That(result.AssignmentLog[0].FromWorkerIds, Is.Empty);
        Assert.That(result.AssignmentLog[0].ToWorkerIds, Is.EquivalentTo(new[] { 7, 9 }));

        var savedAssignments = await BackendConfigurationPnDbContext!.AdhocTaskAssignments
            .Where(a => a.AdhocTaskId == result.Id && a.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        Assert.That(savedAssignments, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task CreateTask_NoAssignmentLog_WhenNoAssignmentsProvided()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var result = await sut.CreateTask(1, MakeCreateModel(property.Id));

        Assert.That(result.AssignmentLog, Is.Empty);
    }

    [Test]
    public async Task CreateTask_WritesTagJoinRows()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var tag = new AdhocTag { Name = "urgent" };
        await tag.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        var result = await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tag.Id]));

        Assert.That(result.TagIds, Is.EquivalentTo(new[] { tag.Id }));
    }

    [Test]
    public async Task CreateTask_Throws_WhenAreaDoesNotBelongToProperty()
    {
        var property = await CreatePropertyAsync();
        var otherProperty = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);

        var area = new AdhocArea { PropertyId = otherProperty.Id, Name = "Barn" };
        await area.Create(BackendConfigurationPnDbContext!);

        var sut = CreateSut();

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.CreateTask(1, MakeCreateModel(property.Id, areaId: area.Id)));
    }

    [Test]
    public async Task CreateTask_Throws_WhenCallerHasNoPropertyAccess()
    {
        var property = await CreatePropertyAsync();
        // Deliberately no PropertyWorker row for worker 1.
        var sut = CreateSut();

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.CreateTask(1, MakeCreateModel(property.Id)));
    }

    [Test]
    public async Task CreateTask_IsAdmin_BypassesPropertyAccessCheck()
    {
        var property = await CreatePropertyAsync();
        // Deliberately no PropertyWorker row for worker 0 (B6's dashboard
        // caller identity) — isAdmin must still let the create through.
        var sut = CreateSut();

        var result = await sut.CreateTask(0, MakeCreateModel(property.Id), isAdmin: true);

        Assert.That(result.PropertyId, Is.EqualTo(property.Id));
        Assert.That(result.CreatedByWorkerId, Is.EqualTo(0));
    }

    [Test]
    public async Task UpdateTask_Throws_WhenCallerIsNotCreator()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 2);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.UpdateTask(2, created.Id, MakeCreateModel(property.Id)));
    }

    [Test]
    public async Task UpdateTask_RecomputesAssignmentDelta_AppendsOneLogRow()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, assignedWorkerIds: [7, 9]));

        var updateModel = MakeCreateModel(property.Id, assignedWorkerIds: [9, 11]);
        var updated = await sut.UpdateTask(1, created.Id, updateModel);

        Assert.That(updated.AssignedWorkerIds, Is.EquivalentTo(new[] { 9, 11 }));
        Assert.That(updated.AssignmentLog, Has.Count.EqualTo(2));
        var secondLog = updated.AssignmentLog[1];
        Assert.That(secondLog.FromWorkerIds, Is.EquivalentTo(new[] { 7, 9 }));
        Assert.That(secondLog.ToWorkerIds, Is.EquivalentTo(new[] { 9, 11 }));

        // The removed assignment (worker 7) must be soft-deleted, not hard-deleted.
        var removedRow = await BackendConfigurationPnDbContext!.AdhocTaskAssignments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.AdhocTaskId == created.Id && a.WorkerId == 7);
        Assert.That(removedRow, Is.Not.Null);
        Assert.That(removedRow!.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task UpdateTask_NoAssignmentChange_DoesNotAppendLogRow()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, assignedWorkerIds: [7]));
        var updated = await sut.UpdateTask(1, created.Id, MakeCreateModel(property.Id, assignedWorkerIds: [7]));

        Assert.That(updated.AssignmentLog, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task UpdateTask_ReplacesTagJoins_SoftDeletingRemoved()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var tagA = new AdhocTag { Name = "a" };
        await tagA.Create(BackendConfigurationPnDbContext!);
        var tagB = new AdhocTag { Name = "b" };
        await tagB.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tagA.Id]));
        var updated = await sut.UpdateTask(1, created.Id, MakeCreateModel(property.Id, tagIds: [tagB.Id]));

        Assert.That(updated.TagIds, Is.EquivalentTo(new[] { tagB.Id }));

        var removedJoin = await BackendConfigurationPnDbContext!.AdhocTaskTags
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(tt => tt.AdhocTaskId == created.Id && tt.AdhocTagId == tagA.Id);
        Assert.That(removedJoin, Is.Not.Null);
        Assert.That(removedJoin!.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task UpdateTask_ReconcilesPhotos_SoftDeletesMissingIds()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        var photoToKeep = new AdhocTaskPhoto { AdhocTaskId = created.Id, UploadedDataId = 1, ContentType = "image/jpeg" };
        await photoToKeep.Create(BackendConfigurationPnDbContext!);
        var photoToRemove = new AdhocTaskPhoto { AdhocTaskId = created.Id, UploadedDataId = 2, ContentType = "image/jpeg" };
        await photoToRemove.Create(BackendConfigurationPnDbContext!);

        var updateModel = MakeCreateModel(property.Id);
        updateModel.PhotoIds = [photoToKeep.Id];
        var updated = await sut.UpdateTask(1, created.Id, updateModel);

        Assert.That(updated.Photos.Select(p => p.Id), Is.EquivalentTo(new[] { photoToKeep.Id }));

        var removedPhotoRow = await BackendConfigurationPnDbContext!.AdhocTaskPhotos
            .IgnoreQueryFilters()
            .FirstAsync(p => p.Id == photoToRemove.Id);
        Assert.That(removedPhotoRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task SetCompleted_True_StampsCompletedByAndAt_ForAssignedWorker()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 7);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, assignedWorkerIds: [7]));
        var completed = await sut.SetCompleted(7, created.Id, true);

        Assert.That(completed.Completed, Is.True);
        Assert.That(completed.CompletedByWorkerId, Is.EqualTo(7));
        Assert.That(completed.CompletedAt, Is.Not.Null);
    }

    [Test]
    public async Task SetCompleted_False_ClearsCompletionFields()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
        await sut.SetCompleted(1, created.Id, true);
        var undone = await sut.SetCompleted(1, created.Id, false);

        Assert.That(undone.Completed, Is.False);
        Assert.That(undone.CompletedByWorkerId, Is.Null);
        Assert.That(undone.CompletedAt, Is.Null);
    }

    [Test]
    public async Task SetCompleted_Throws_ForWorkerWithoutVisibility()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 99);
        var sut = CreateSut();

        // assignedOnly, not assigned to 99, not the creator -> not visible.
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.SetCompleted(99, created.Id, true));
    }

    // --- SetCompleted with completedByWorkerId (M5/P3 dashboard "Udfør opgave" performer select) ---

    [Test]
    public async Task SetCompleted_WithCompletedByWorkerId_StampsThatWorkerInstead_OfTheAdminCaller()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 7);
        var sut = CreateSut();
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        // Dashboard caller identity (workerId 0, isAdmin true) picks worker 7
        // as the performer from the "Vælg hvem der udfører opgaven" select.
        var completed = await sut.SetCompleted(0, created.Id, true, isAdmin: true, completedByWorkerId: 7);

        Assert.That(completed.Completed, Is.True);
        Assert.That(completed.CompletedByWorkerId, Is.EqualTo(7));
        Assert.That(completed.CompletedAt, Is.Not.Null);
    }

    [Test]
    public async Task SetCompleted_AdminWithoutPerformer_StampsNullCompletedByWorkerId()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();
        var created = await sut.CreateTask(0, MakeCreateModel(property.Id), isAdmin: true);

        // Dashboard admin (synthetic workerId 0) completes WITHOUT selecting
        // a performer - worker 0 is not a real SDK site, so nothing must be
        // recorded as the performer (null, not 0).
        var completed = await sut.SetCompleted(0, created.Id, true, isAdmin: true);

        Assert.That(completed.Completed, Is.True);
        Assert.That(completed.CompletedByWorkerId, Is.Null);
        Assert.That(completed.CompletedAt, Is.Not.Null);
    }

    [Test]
    public async Task SetCompleted_WithCompletedByWorkerId_Throws_WhenPerformerHasNoAccessToTheProperty()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        // Worker 99 has no PropertyWorker row for this property.
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.SetCompleted(0, created.Id, true, isAdmin: true, completedByWorkerId: 99));
    }

    [Test]
    public async Task Archive_Throws_ForNonCreator()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 2);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.Archive(2, created.Id));
    }

    [Test]
    public async Task Archive_SetsArchivedFields_ForCreator()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
        var archived = await sut.Archive(1, created.Id);

        Assert.That(archived.Archived, Is.True);
        Assert.That(archived.ArchivedAt, Is.Not.Null);
    }

    [Test]
    public async Task Reopen_ClearsArchivedAndCompletedFields_ForCreator()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
        await sut.SetCompleted(1, created.Id, true);
        await sut.Archive(1, created.Id);

        var reopened = await sut.Reopen(1, created.Id);

        Assert.That(reopened.Archived, Is.False);
        Assert.That(reopened.ArchivedAt, Is.Null);
        Assert.That(reopened.Completed, Is.False);
        Assert.That(reopened.CompletedByWorkerId, Is.Null);
        Assert.That(reopened.CompletedAt, Is.Null);
    }

    [Test]
    public async Task Reopen_Throws_ForNonCreator()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 2);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
        await sut.Archive(1, created.Id);

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.Reopen(2, created.Id));
    }

    [Test]
    public async Task Delete_Throws_ForNonCreator()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 2);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.Delete(2, created.Id));
    }

    // --- C1: pseudo-identity 0 never satisfies the creator gate. Web-created
    // tasks are stamped CreatedByWorkerId = 0, so without the workerId == 0
    // guard in RequireCreator a (0, false) caller would pass the creator check
    // on every web-created task.
    //
    // These pin RequireCreator's predicate, not a caller. Since 2026-08-24 no
    // production caller produces (0, false): AdhocController passes full access
    // on every route, and AdhocGrpcService rejects an unresolvable identity
    // with Unauthenticated before the service is reached. Keep them anyway -
    // the predicate is what keeps the gRPC path narrow, and it is exactly the
    // kind of code a later "simplification" would widen without any other test
    // noticing. Do not delete them as unreachable. ---

    [Test]
    public async Task Archive_Throws_ForNonAdminWorkerZero_OnWorkerZeroCreatedTask()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        var created = await sut.CreateTask(0, MakeCreateModel(property.Id), isAdmin: true);

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.Archive(0, created.Id));
    }

    [Test]
    public async Task Reopen_Throws_ForNonAdminWorkerZero_OnWorkerZeroCreatedTask()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        var created = await sut.CreateTask(0, MakeCreateModel(property.Id), isAdmin: true);
        await sut.Archive(0, created.Id, isAdmin: true);

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.Reopen(0, created.Id));
    }

    [Test]
    public async Task Delete_Throws_ForNonAdminWorkerZero_OnWorkerZeroCreatedTask()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        var created = await sut.CreateTask(0, MakeCreateModel(property.Id), isAdmin: true);

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.Delete(0, created.Id));
    }

    [Test]
    public async Task UpdateTask_Throws_ForNonAdminWorkerZero_OnWorkerZeroCreatedTask()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        var created = await sut.CreateTask(0, MakeCreateModel(property.Id), isAdmin: true);

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.UpdateTask(0, created.Id, MakeCreateModel(property.Id)));
    }

    [Test]
    public async Task Archive_Reopen_Delete_Allowed_ForAdminWorkerZero_OnWorkerZeroCreatedTask()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        var created = await sut.CreateTask(0, MakeCreateModel(property.Id), isAdmin: true);

        var archived = await sut.Archive(0, created.Id, isAdmin: true);
        Assert.That(archived.Archived, Is.True);

        var reopened = await sut.Reopen(0, created.Id, isAdmin: true);
        Assert.That(reopened.Archived, Is.False);

        Assert.DoesNotThrowAsync(async () => await sut.Delete(0, created.Id, isAdmin: true));
    }

    [Test]
    public async Task Delete_CascadesSoftDeleteToAllChildren()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var tag = new AdhocTag { Name = "cascade-tag" };
        await tag.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, assignedWorkerIds: [7], tagIds: [tag.Id]));
        await sut.AddComment(1, created.Id, "a comment");
        var photo = new AdhocTaskPhoto { AdhocTaskId = created.Id, UploadedDataId = 1, ContentType = "image/jpeg" };
        await photo.Create(BackendConfigurationPnDbContext!);

        await sut.Delete(1, created.Id);

        var taskRow = await BackendConfigurationPnDbContext!.AdhocTasks
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == created.Id);
        Assert.That(taskRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var assignmentRow = await BackendConfigurationPnDbContext.AdhocTaskAssignments
            .IgnoreQueryFilters()
            .FirstAsync(a => a.AdhocTaskId == created.Id);
        Assert.That(assignmentRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var assignmentLogRow = await BackendConfigurationPnDbContext.AdhocTaskAssignmentLogs
            .IgnoreQueryFilters()
            .FirstAsync(l => l.AdhocTaskId == created.Id);
        Assert.That(assignmentLogRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var commentRow = await BackendConfigurationPnDbContext.AdhocTaskComments
            .IgnoreQueryFilters()
            .FirstAsync(c => c.AdhocTaskId == created.Id);
        Assert.That(commentRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var photoRow = await BackendConfigurationPnDbContext.AdhocTaskPhotos
            .IgnoreQueryFilters()
            .FirstAsync(p => p.AdhocTaskId == created.Id);
        Assert.That(photoRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var tagJoinRow = await BackendConfigurationPnDbContext.AdhocTaskTags
            .IgnoreQueryFilters()
            .FirstAsync(tt => tt.AdhocTaskId == created.Id);
        Assert.That(tagJoinRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task Delete_FullAccess_SoftDeletesTaskCreatedByAnotherWorker_CascadingToAllChildren()
    {
        // Accepted consequence of the "web is unrestricted" decision
        // (2026-08-24): the web passes (workerId 0, full access) on DELETE, so
        // RequireCreator is bypassed and a task a worker created on their
        // PHONE can be removed from the dashboard - together with their
        // assignments, assignment log, comments and photos. It is the Microting
        // soft delete (WorkflowState = Removed): the rows survive and stay
        // recoverable in the database, but the task is gone from every list and
        // from the mobile client. This pins that DECISION, not the wiring.
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 7);
        var tag = new AdhocTag { Name = "phone-task-tag" };
        await tag.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        var created = await sut.CreateTask(7, MakeCreateModel(property.Id, assignedWorkerIds: [9], tagIds: [tag.Id]));
        Assert.That(created.CreatedByWorkerId, Is.EqualTo(7));
        await sut.AddComment(7, created.Id, "from the phone");
        var photo = new AdhocTaskPhoto { AdhocTaskId = created.Id, UploadedDataId = 1, ContentType = "image/jpeg" };
        await photo.Create(BackendConfigurationPnDbContext!);

        await sut.Delete(0, created.Id, isAdmin: true);

        var taskRow = await BackendConfigurationPnDbContext!.AdhocTasks
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == created.Id);
        Assert.That(taskRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var assignmentRow = await BackendConfigurationPnDbContext.AdhocTaskAssignments
            .IgnoreQueryFilters()
            .FirstAsync(a => a.AdhocTaskId == created.Id);
        Assert.That(assignmentRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var assignmentLogRow = await BackendConfigurationPnDbContext.AdhocTaskAssignmentLogs
            .IgnoreQueryFilters()
            .FirstAsync(l => l.AdhocTaskId == created.Id);
        Assert.That(assignmentLogRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var commentRow = await BackendConfigurationPnDbContext.AdhocTaskComments
            .IgnoreQueryFilters()
            .FirstAsync(c => c.AdhocTaskId == created.Id);
        Assert.That(commentRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var photoRow = await BackendConfigurationPnDbContext.AdhocTaskPhotos
            .IgnoreQueryFilters()
            .FirstAsync(p => p.AdhocTaskId == created.Id);
        Assert.That(photoRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var tagJoinRow = await BackendConfigurationPnDbContext.AdhocTaskTags
            .IgnoreQueryFilters()
            .FirstAsync(tt => tt.AdhocTaskId == created.Id);
        Assert.That(tagJoinRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task AddComment_AllowedForAssignedWorker_AddsCommentRow()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 7);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, assignedWorkerIds: [7]));
        var withComment = await sut.AddComment(7, created.Id, "on it");

        Assert.That(withComment.Comments, Has.Count.EqualTo(1));
        Assert.That(withComment.Comments[0].AuthorWorkerId, Is.EqualTo(7));
        Assert.That(withComment.Comments[0].Text, Is.EqualTo("on it"));
    }

    [Test]
    public async Task AddComment_Throws_ForWorkerWithoutVisibility()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 99);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.AddComment(99, created.Id, "nope"));
    }

    [Test]
    public async Task GetTask_Throws_NotFound_ForRemovedTask()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));
        await sut.Delete(1, created.Id);

        Assert.ThrowsAsync<AdhocTaskNotFoundException>(async () =>
            await sut.GetTask(1, created.Id));
    }

    [Test]
    public async Task GetTask_Throws_NotFound_ForUnknownId()
    {
        var sut = CreateSut();

        Assert.ThrowsAsync<AdhocTaskNotFoundException>(async () =>
            await sut.GetTask(1, 987654));
    }

    // The gRPC client sends a full-replacement UpdateTaskRequest even when the
    // worker only added a photo, and an assigned-but-not-creating worker is the
    // only role that can reach that path. Rejecting it parked the mobile
    // outbox row as a permanent failure, whose per-task head-of-line rule then
    // blocked the photo upload queued behind it - and every later comment on
    // that task. So the request is accepted and ignored rather than denied.
    [Test]
    public async Task UpdateTask_AllowedForAssignedWorker_ReturnsTaskUnchanged()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 7);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, assignedWorkerIds: [7]));

        var result = await sut.UpdateTask(7, created.Id, MakeCreateModel(property.Id, assignedWorkerIds: [7]));

        Assert.That(result.Id, Is.EqualTo(created.Id));
        Assert.That(result.Title, Is.EqualTo(created.Title));
    }

    // Ignore-semantics, not a field diff: whatever a stale (or hostile) client
    // sends on the model, a non-creator mutates nothing.
    [Test]
    public async Task UpdateTask_ForAssignedWorker_IgnoresAttemptedTitleChange()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 7);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, assignedWorkerIds: [7]));

        var hostile = MakeCreateModel(property.Id, assignedWorkerIds: [7]);
        hostile.Title = "hijacked";
        var result = await sut.UpdateTask(7, created.Id, hostile);

        Assert.That(result.Title, Is.EqualTo(created.Title));

        var row = await BackendConfigurationPnDbContext!.AdhocTasks
            .FirstAsync(t => t.Id == created.Id);
        Assert.That(row.Title, Is.EqualTo(created.Title));
    }

    // ReconcilePhotosAsync soft-deletes by omission, so honouring a
    // non-creator's PhotoIds would let a stale client wipe the creator's
    // photos. The assignee's own photo arrives via SavePhoto instead.
    [Test]
    public async Task UpdateTask_ForAssignedWorker_DoesNotSoftDeleteOmittedPhotos()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 7);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, assignedWorkerIds: [7]));

        var creatorPhoto = new AdhocTaskPhoto { AdhocTaskId = created.Id, UploadedDataId = 1, ContentType = "image/jpeg" };
        await creatorPhoto.Create(BackendConfigurationPnDbContext!);

        var modelWithNoPhotos = MakeCreateModel(property.Id, assignedWorkerIds: [7]);
        modelWithNoPhotos.PhotoIds = [];
        var result = await sut.UpdateTask(7, created.Id, modelWithNoPhotos);

        Assert.That(result.Photos.Select(p => p.Id), Is.EquivalentTo(new[] { creatorPhoto.Id }));

        var photoRow = await BackendConfigurationPnDbContext!.AdhocTaskPhotos
            .IgnoreQueryFilters()
            .FirstAsync(p => p.Id == creatorPhoto.Id);
        Assert.That(photoRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    [Test]
    public async Task UpdateTask_StillThrows_ForWorkerWithoutVisibility()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 3);
        var sut = CreateSut();

        // Worker 3 has property access but is neither creator nor assignee,
        // and the task is AssignedOnly - so CanSee is false.
        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.UpdateTask(3, created.Id, MakeCreateModel(property.Id)));
    }

    // The actual reported production scenario: office creates the task on
    // the web dashboard (CreatedByWorkerId == 0), assigns a real worker, and
    // that worker's phone later sends the accept-and-ignore no-op UpdateTask
    // over gRPC with its own real, positive site id. Pins that the fix
    // covers the web-created case, not just a worker-created one, and that
    // the persisted row - not just the returned model - is untouched.
    [Test]
    public async Task UpdateTask_AcceptsAndIgnoresUpdate_ForAssignedWorkerOnWebCreatedTask()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 7);
        var sut = CreateSut();

        var created = await sut.CreateTask(0, MakeCreateModel(property.Id, assignedWorkerIds: [7]), isAdmin: true);
        Assert.That(created.CreatedByWorkerId, Is.EqualTo(0));

        var hostile = MakeCreateModel(property.Id, assignedWorkerIds: [7]);
        hostile.Title = "hijacked";
        var result = await sut.UpdateTask(7, created.Id, hostile);

        Assert.That(result.Id, Is.EqualTo(created.Id));
        Assert.That(result.Title, Is.EqualTo(created.Title));

        var row = await BackendConfigurationPnDbContext!.AdhocTasks
            .FirstAsync(t => t.Id == created.Id);
        Assert.That(row.Title, Is.EqualTo(created.Title));
    }

    // CanSee returns true for ANY worker with property access when the
    // task's ExecutionRule is Everyone - a strictly larger acceptance
    // surface than "assignee". Pins that such a worker (property access,
    // NOT assigned) also lands in the accept-and-ignore branch rather than
    // being rejected, and mutates nothing.
    //
    // AdhocExecutionRuleValue is a private enum on the service, so the
    // literal 1 below is what AdhocExecutionRuleValue.Everyone maps onto
    // AdhocTaskCreateModel.ExecutionRule (an int).
    [Test]
    public async Task UpdateTask_AcceptsAndIgnoresUpdate_ForNonAssignedWorkerWithAccess_WhenExecutionRuleIsEveryone()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 42);
        var sut = CreateSut();

        var createModel = MakeCreateModel(property.Id);
        createModel.ExecutionRule = 1; // AdhocExecutionRuleValue.Everyone
        var created = await sut.CreateTask(1, createModel);

        // Worker 42 has property access but is neither creator nor assignee.
        var hostile = MakeCreateModel(property.Id);
        hostile.Title = "hijacked";
        var result = await sut.UpdateTask(42, created.Id, hostile);

        Assert.That(result.Id, Is.EqualTo(created.Id));
        Assert.That(result.Title, Is.EqualTo(created.Title));

        var row = await BackendConfigurationPnDbContext!.AdhocTasks
            .FirstAsync(t => t.Id == created.Id);
        Assert.That(row.Title, Is.EqualTo(created.Title));
    }
}
