using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Adhoc;
using BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Drives <see cref="BackendConfigurationAdhocService"/>'s visibility
/// predicates against the exact rules in the Dart <c>TaskVisibility</c>
/// class (<c>canSee</c>/<c>matchesScope</c>) plus the dashboard's
/// <c>isAdmin</c> bypass (plan Task B6's "REST caller identity" paragraph).
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class AdhocServiceVisibilityTests : TestBaseSetup
{
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

    private static AdhocTaskCreateModel MakeCreateModel(
        int propertyId,
        List<int>? assignedWorkerIds = null,
        int executionRule = 0)
    {
        return new AdhocTaskCreateModel
        {
            Title = Guid.NewGuid().ToString(),
            PropertyId = propertyId,
            AssignedWorkerIds = assignedWorkerIds ?? [],
            ExecutionRule = executionRule,
        };
    }

    [Test]
    public async Task ListTasks_ExcludesTasksOnPropertiesTheWorkerCannotAccess()
    {
        var accessibleProperty = await CreatePropertyAsync();
        var inaccessibleProperty = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(accessibleProperty.Id, 1);
        await GrantPropertyAccessAsync(inaccessibleProperty.Id, 1); // creator on the other property
        var sut = CreateSut();

        await sut.CreateTask(1, MakeCreateModel(accessibleProperty.Id));
        var otherTask = await sut.CreateTask(1, MakeCreateModel(inaccessibleProperty.Id));

        // Revoke worker 1's access to inaccessibleProperty by directly
        // soft-deleting the PropertyWorker row (simulating "no access").
        var pw = BackendConfigurationPnDbContext!.PropertyWorkers
            .First(p => p.PropertyId == inaccessibleProperty.Id && p.WorkerId == 1);
        await pw.Delete(BackendConfigurationPnDbContext);

        var visible = await sut.ListTasks(1, TaskScopeFilter.All, null);

        Assert.That(visible.Select(t => t.Id), Does.Not.Contain(otherTask.Id));
    }

    [Test]
    public async Task ListTasks_IncludesCreatorTask_EvenIfNotAssignedAndAssignedOnly()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        var visible = await sut.ListTasks(1, TaskScopeFilter.All, null);

        Assert.That(visible.Select(t => t.Id), Contains.Item(created.Id));
    }

    [Test]
    public async Task ListTasks_IncludesEveryoneRuleTask_ForNonCreatorNonAssignedWorker()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 2);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, executionRule: 1));

        var visible = await sut.ListTasks(2, TaskScopeFilter.All, null);

        Assert.That(visible.Select(t => t.Id), Contains.Item(created.Id));
    }

    [Test]
    public async Task ListTasks_ExcludesAssignedOnlyTask_ForNonCreatorNonAssignedWorker()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 2);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, executionRule: 0));

        var visible = await sut.ListTasks(2, TaskScopeFilter.All, null);

        Assert.That(visible.Select(t => t.Id), Does.Not.Contain(created.Id));
    }

    [Test]
    public async Task ListTasks_IncludesAssignedOnlyTask_ForAssignedWorker()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 2);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, assignedWorkerIds: [2]));

        var visible = await sut.ListTasks(2, TaskScopeFilter.All, null);

        Assert.That(visible.Select(t => t.Id), Contains.Item(created.Id));
    }

    [Test]
    public async Task ListTasks_ScopeAll_ReturnsCompletedAndOpenTasksAlike()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var open = await sut.CreateTask(1, MakeCreateModel(property.Id));
        var toComplete = await sut.CreateTask(1, MakeCreateModel(property.Id));
        await sut.SetCompleted(1, toComplete.Id, true);

        var visible = await sut.ListTasks(1, TaskScopeFilter.All, null);

        Assert.That(visible.Select(t => t.Id), Is.SupersetOf(new[] { open.Id, toComplete.Id }));
    }

    [Test]
    public async Task ListTasks_ScopeCompleted_ReturnsOnlyCompletedOrArchivedTasks()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var open = await sut.CreateTask(1, MakeCreateModel(property.Id));
        var completed = await sut.CreateTask(1, MakeCreateModel(property.Id));
        await sut.SetCompleted(1, completed.Id, true);
        var archived = await sut.CreateTask(1, MakeCreateModel(property.Id));
        await sut.Archive(1, archived.Id);

        var visible = await sut.ListTasks(1, TaskScopeFilter.Completed, null);
        var ids = visible.Select(t => t.Id).ToList();

        Assert.That(ids, Does.Not.Contain(open.Id));
        Assert.That(ids, Contains.Item(completed.Id));
        Assert.That(ids, Contains.Item(archived.Id));
    }

    [Test]
    public async Task ListTasks_ScopeMine_ReturnsOpenTasksAssignedToCaller()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 2);
        var sut = CreateSut();

        var assignedToMe = await sut.CreateTask(1, MakeCreateModel(property.Id, assignedWorkerIds: [2]));
        var everyoneTask = await sut.CreateTask(1, MakeCreateModel(property.Id, executionRule: 1));

        var visible = await sut.ListTasks(2, TaskScopeFilter.Mine, null);
        var ids = visible.Select(t => t.Id).ToList();

        Assert.That(ids, Contains.Item(assignedToMe.Id));
        Assert.That(ids, Does.Not.Contain(everyoneTask.Id));
    }

    [Test]
    public async Task ListTasks_ScopeEveryone_ExcludesTaskAssignedToCaller()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 2);
        var sut = CreateSut();

        // An "everyone" task that IS assigned to worker 2 counts as "mine",
        // not "everyone" - mirrors the Dart matchesScope carve-out.
        var everyoneButAssignedToMe = await sut.CreateTask(
            1, MakeCreateModel(property.Id, assignedWorkerIds: [2], executionRule: 1));
        var everyoneUnassigned = await sut.CreateTask(1, MakeCreateModel(property.Id, executionRule: 1));

        var visible = await sut.ListTasks(2, TaskScopeFilter.Everyone, null);
        var ids = visible.Select(t => t.Id).ToList();

        Assert.That(ids, Does.Not.Contain(everyoneButAssignedToMe.Id));
        Assert.That(ids, Contains.Item(everyoneUnassigned.Id));
    }

    [Test]
    public async Task ListTasks_PropertyFilter_NarrowsToRequestedProperty()
    {
        var propertyA = await CreatePropertyAsync();
        var propertyB = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(propertyA.Id, 1);
        await GrantPropertyAccessAsync(propertyB.Id, 1);
        var sut = CreateSut();

        var taskA = await sut.CreateTask(1, MakeCreateModel(propertyA.Id));
        var taskB = await sut.CreateTask(1, MakeCreateModel(propertyB.Id));

        var visible = await sut.ListTasks(1, TaskScopeFilter.All, propertyA.Id);
        var ids = visible.Select(t => t.Id).ToList();

        Assert.That(ids, Contains.Item(taskA.Id));
        Assert.That(ids, Does.Not.Contain(taskB.Id));
    }

    [Test]
    public async Task ListTasks_IsAdmin_BypassesPropertyAccessAndReturnsAllTasks()
    {
        var property = await CreatePropertyAsync();
        // No PropertyWorker row at all for worker 0 (the REST admin caller
        // identity, per plan Task B6: "workerId = 0 + isAdmin bypass").
        var sut = CreateSut();

        await GrantPropertyAccessAsync(property.Id, 1);
        var task = await sut.CreateTask(1, MakeCreateModel(property.Id));

        var visible = await sut.ListTasks(0, TaskScopeFilter.All, null, isAdmin: true);

        Assert.That(visible.Select(t => t.Id), Contains.Item(task.Id));
    }

    [Test]
    public async Task ListTasks_NotAdmin_WithNoPropertyAccess_ReturnsEmpty()
    {
        // Pins ListTasks' visibility predicate, not a caller: without the
        // admin flag, an identity with no PropertyWorker row sees nothing.
        // No production caller passes (0, false) since 2026-08-24 - the web
        // passes full access and gRPC rejects an unresolvable identity - but
        // this predicate is what keeps the gRPC path scoped, so it is asserted
        // directly rather than left to be inferred.
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        await GrantPropertyAccessAsync(property.Id, 1);
        await sut.CreateTask(1, MakeCreateModel(property.Id));

        var visible = await sut.ListTasks(0, TaskScopeFilter.All, null);

        Assert.That(visible, Is.Empty);
    }

    [Test]
    public async Task GetTask_IsAdmin_BypassesVisibilityCheck()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        var fetched = await sut.GetTask(0, created.Id, isAdmin: true);

        Assert.That(fetched.Id, Is.EqualTo(created.Id));
    }

    [Test]
    public async Task GetTask_Throws_ForWorkerWithoutPropertyAccess()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.GetTask(2, created.Id));
    }

    /// <summary>
    /// Regression test: <c>TaskVisibility.canSee</c> gates on property access
    /// FIRST, unconditionally - even the creator loses visibility if their
    /// property access is later revoked. This caught a bug where the
    /// service's CanSee predicate checked creator/everyone/assigned without
    /// ever consulting property access on the single-task paths (GetTask,
    /// SetCompleted, AddComment) - only ListTasks' bulk query filter enforced
    /// it, which every other predicate path silently bypassed.
    /// </summary>
    [Test]
    public async Task GetTask_Throws_ForCreator_WhosePropertyAccessWasRevoked()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        var pw = BackendConfigurationPnDbContext!.PropertyWorkers
            .First(p => p.PropertyId == property.Id && p.WorkerId == 1);
        await pw.Delete(BackendConfigurationPnDbContext);

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.GetTask(1, created.Id));
    }

    [Test]
    public async Task SetCompleted_Throws_ForAssignedWorker_WhosePropertyAccessWasRevoked()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 7);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id, assignedWorkerIds: [7]));

        var pw = BackendConfigurationPnDbContext!.PropertyWorkers
            .First(p => p.PropertyId == property.Id && p.WorkerId == 7);
        await pw.Delete(BackendConfigurationPnDbContext);

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.SetCompleted(7, created.Id, true));
    }

    [Test]
    public async Task ListTasks_IsAdmin_StillSeesTask_EvenWithoutAnyPropertyWorkerRow()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();

        var created = await sut.CreateTask(1, MakeCreateModel(property.Id));

        var pw = BackendConfigurationPnDbContext!.PropertyWorkers
            .First(p => p.PropertyId == property.Id && p.WorkerId == 1);
        await pw.Delete(BackendConfigurationPnDbContext);

        // isAdmin bypasses the property-access gate entirely, even for a
        // property that has zero active PropertyWorker rows.
        var visible = await sut.ListTasks(0, TaskScopeFilter.All, null, isAdmin: true);

        Assert.That(visible.Select(t => t.Id), Contains.Item(created.Id));
    }
}
