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
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// Drives <see cref="BackendConfigurationAdhocService"/>'s reference-data
/// methods (properties/areas/workers/tags, Task B3). Worker display-name
/// resolution needs a real SDK Core, so tests that exercise
/// <c>ListWorkers</c> use <see cref="TestBaseSetup.GetCore"/> (the same
/// real-Core-over-a-substituted-<c>IEFormCoreService</c> pattern used by
/// <c>CalendarYearlyEnumerateTests</c>); the rest only touch
/// <see cref="TestBaseSetup.BackendConfigurationPnDbContext"/> so a bare
/// substitute is enough.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class AdhocServiceReferenceDataTests : TestBaseSetup
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

    private BackendConfigurationAdhocService CreateSut(IEFormCoreService? coreHelper = null)
    {
        return new BackendConfigurationAdhocService(
            BackendConfigurationPnDbContext!,
            new BackendConfigurationUserPropertyAccess(BackendConfigurationPnDbContext!),
            coreHelper ?? Substitute.For<IEFormCoreService>(),
            new FakeAdhocPhotoStorage());
    }

    private async Task<Property> CreatePropertyAsync(string? name = null)
    {
        var property = new Property
        {
            Name = name ?? Guid.NewGuid().ToString(),
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

    // --- ListProperties ---

    [Test]
    public async Task ListProperties_ReturnsOnlyAccessibleProperties()
    {
        var accessible = await CreatePropertyAsync("Accessible");
        var inaccessible = await CreatePropertyAsync("Inaccessible");
        await GrantPropertyAccessAsync(accessible.Id, 1);
        var sut = CreateSut();

        var result = await sut.ListProperties(1);

        Assert.That(result.Select(p => p.Id), Is.EquivalentTo(new[] { accessible.Id }));
        Assert.That(result.Single().Name, Is.EqualTo("Accessible"));
        Assert.That(result.Select(p => p.Id), Does.Not.Contain(inaccessible.Id));
    }

    [Test]
    public async Task ListProperties_IsAdmin_ReturnsAllProperties()
    {
        var propertyA = await CreatePropertyAsync();
        var propertyB = await CreatePropertyAsync();
        // Deliberately no PropertyWorker rows for worker 1.
        var sut = CreateSut();

        var result = await sut.ListProperties(1, isAdmin: true);

        Assert.That(result.Select(p => p.Id), Is.SupersetOf(new[] { propertyA.Id, propertyB.Id }));
    }

    // --- ListAreas ---

    [Test]
    public async Task ListAreas_ReturnsAreasForProperty()
    {
        var property = await CreatePropertyAsync();
        var otherProperty = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);

        var area = new AdhocArea { PropertyId = property.Id, Name = "Barn" };
        await area.Create(BackendConfigurationPnDbContext!);
        var otherArea = new AdhocArea { PropertyId = otherProperty.Id, Name = "Silo" };
        await otherArea.Create(BackendConfigurationPnDbContext!);

        var sut = CreateSut();

        var result = await sut.ListAreas(1, property.Id);

        Assert.That(result.Select(a => a.Id), Is.EquivalentTo(new[] { area.Id }));
        Assert.That(result.Single().Name, Is.EqualTo("Barn"));
    }

    [Test]
    public async Task ListAreas_Throws_WhenCallerHasNoPropertyAccess()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.ListAreas(1, property.Id));
    }

    [Test]
    public async Task ListAreas_IsAdmin_BypassesPropertyAccessCheck()
    {
        var property = await CreatePropertyAsync();
        var area = new AdhocArea { PropertyId = property.Id, Name = "Barn" };
        await area.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        var result = await sut.ListAreas(1, property.Id, isAdmin: true);

        Assert.That(result.Select(a => a.Id), Is.EquivalentTo(new[] { area.Id }));
    }

    [Test]
    public async Task ListAreas_ExcludesRemovedAreas()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var area = new AdhocArea { PropertyId = property.Id, Name = "Removed" };
        await area.Create(BackendConfigurationPnDbContext!);
        await area.Delete(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        var result = await sut.ListAreas(1, property.Id);

        Assert.That(result, Is.Empty);
    }

    // --- ListWorkers ---

    [Test]
    public async Task ListWorkers_ResolvesDisplayNamesFromSdkSites_AndPropertyIds()
    {
        var property = await CreatePropertyAsync();
        var otherProperty = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);

        var core = await GetCore();
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var language = await sdkDbContext.Languages.FirstAsync();
        var site = new Microting.eForm.Infrastructure.Data.Entities.Site
        {
            Name = "Worker Seven",
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created,
        };
        await sdkDbContext.Sites.AddAsync(site);
        await sdkDbContext.SaveChangesAsync();

        await GrantPropertyAccessAsync(property.Id, site.Id);
        await GrantPropertyAccessAsync(otherProperty.Id, site.Id);

        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var sut = CreateSut(coreHelper);

        var result = await sut.ListWorkers(1, property.Id);

        var worker = result.Single(w => w.WorkerId == site.Id);
        Assert.That(worker.DisplayName, Is.EqualTo("Worker Seven"));
        Assert.That(worker.PropertyIds, Is.EquivalentTo(new[] { property.Id, otherProperty.Id }));
    }

    [Test]
    public async Task ListWorkers_Throws_WhenCallerHasNoPropertyAccess()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.ListWorkers(1, property.Id));
    }

    [Test]
    public async Task ListWorkers_ReturnsEmpty_WhenNoWorkersAssignedToProperty()
    {
        // isAdmin bypasses the property-access check, so this exercises a
        // property with zero PropertyWorker rows at all (an access check
        // for a non-admin caller would always fail first, since having
        // access itself implies at least one PropertyWorker row exists).
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        var result = await sut.ListWorkers(1, property.Id, isAdmin: true);

        Assert.That(result, Is.Empty);
    }

    // --- ListTags / CreateTag / RenameTag / DeleteTag ---

    [Test]
    public async Task ListTags_ReturnsGlobalAndOwnTags_ButNotOthersPersonalTags()
    {
        var globalTag = new AdhocTag { Name = "global" };
        await globalTag.Create(BackendConfigurationPnDbContext!);
        var ownTag = new AdhocTag { Name = "mine", OwnerWorkerId = 1 };
        await ownTag.Create(BackendConfigurationPnDbContext!);
        var othersTag = new AdhocTag { Name = "theirs", OwnerWorkerId = 2 };
        await othersTag.Create(BackendConfigurationPnDbContext!);

        var sut = CreateSut();

        var result = await sut.ListTags(1);

        Assert.That(result.Select(t => t.Id), Is.EquivalentTo(new[] { globalTag.Id, ownTag.Id }));
        Assert.That(result.Single(t => t.Id == globalTag.Id).IsUserTag, Is.False);
        Assert.That(result.Single(t => t.Id == ownTag.Id).IsUserTag, Is.True);
    }

    [Test]
    public async Task CreateTag_AlwaysCreatesUserTag_OwnedByCaller()
    {
        var sut = CreateSut();

        var created = await sut.CreateTag(1, "urgent");

        Assert.That(created.Name, Is.EqualTo("urgent"));
        Assert.That(created.IsUserTag, Is.True);

        var row = await BackendConfigurationPnDbContext!.AdhocTags.FirstAsync(t => t.Id == created.Id);
        Assert.That(row.OwnerWorkerId, Is.EqualTo(1));
    }

    [Test]
    public void CreateTag_Throws_ForEmptyName()
    {
        var sut = CreateSut();

        Assert.ThrowsAsync<ArgumentException>(async () => await sut.CreateTag(1, "  "));
    }

    [Test]
    public async Task CreateTag_IsAdmin_CreatesGlobalTag_NotOwnedByCaller()
    {
        var sut = CreateSut();

        // Dashboard caller identity (workerId 0) curating a shared tag.
        var created = await sut.CreateTag(0, "shared", isAdmin: true);

        Assert.That(created.IsUserTag, Is.False);

        var row = await BackendConfigurationPnDbContext!.AdhocTags.FirstAsync(t => t.Id == created.Id);
        Assert.That(row.OwnerWorkerId, Is.Null);

        // Visible to every worker via ListTags' "global OR own" rule.
        var listedByOtherWorker = await sut.ListTags(42);
        Assert.That(listedByOtherWorker.Select(t => t.Id), Does.Contain(created.Id));
    }

    [Test]
    public async Task RenameTag_RenamesOwnTag()
    {
        var sut = CreateSut();
        var created = await sut.CreateTag(1, "old-name");

        var renamed = await sut.RenameTag(1, created.Id, "new-name");

        Assert.That(renamed.Name, Is.EqualTo("new-name"));
    }

    [Test]
    public async Task RenameTag_Throws_ForGlobalTag()
    {
        var globalTag = new AdhocTag { Name = "global" };
        await globalTag.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.RenameTag(1, globalTag.Id, "renamed"));
    }

    [Test]
    public async Task RenameTag_Throws_ForAnotherWorkersTag()
    {
        var sut = CreateSut();
        var created = await sut.CreateTag(1, "mine");

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.RenameTag(2, created.Id, "stolen"));
    }

    [Test]
    public void RenameTag_Throws_NotFound_ForUnknownId()
    {
        var sut = CreateSut();

        Assert.ThrowsAsync<AdhocTagNotFoundException>(async () =>
            await sut.RenameTag(1, 987654, "renamed"));
    }

    [Test]
    public async Task DeleteTag_SoftDeletesTagAndItsTaskJoins()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var sut = CreateSut();
        var tag = await sut.CreateTag(1, "to-delete");

        var task = new AdhocTaskEntity
        {
            Title = "t",
            Description = "d",
            PropertyId = property.Id,
            CreatedByWorkerId = 1,
        };
        await task.Create(BackendConfigurationPnDbContext!);
        var join = new AdhocTaskTag { AdhocTaskId = task.Id, AdhocTagId = tag.Id };
        await join.Create(BackendConfigurationPnDbContext!);

        await sut.DeleteTag(1, tag.Id);

        var tagRow = await BackendConfigurationPnDbContext!.AdhocTags
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == tag.Id);
        Assert.That(tagRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));

        var joinRow = await BackendConfigurationPnDbContext.AdhocTaskTags
            .IgnoreQueryFilters()
            .FirstAsync(tt => tt.Id == join.Id);
        Assert.That(joinRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public async Task DeleteTag_Throws_ForAnotherWorkersTag()
    {
        var sut = CreateSut();
        var created = await sut.CreateTag(1, "mine");

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.DeleteTag(2, created.Id));
    }

    // --- I1: tag management by the REST caller identity. Admins manage
    // global tags (OwnerWorkerId == null, previously unmanageable by
    // anyone); the non-admin REST pseudo-identity (workerId 0, isAdmin
    // false) is denied create/rename/delete outright - identity 0 owns
    // nothing (same principle as the C1 creator-gate guard). ---

    [Test]
    public async Task RenameTag_IsAdmin_RenamesGlobalTag()
    {
        var globalTag = new AdhocTag { Name = "global" };
        await globalTag.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        var renamed = await sut.RenameTag(0, globalTag.Id, "renamed-global", isAdmin: true);

        Assert.That(renamed.Name, Is.EqualTo("renamed-global"));
        Assert.That(renamed.IsUserTag, Is.False);
    }

    [Test]
    public async Task DeleteTag_IsAdmin_DeletesGlobalTag()
    {
        var globalTag = new AdhocTag { Name = "global" };
        await globalTag.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        await sut.DeleteTag(0, globalTag.Id, isAdmin: true);

        var tagRow = await BackendConfigurationPnDbContext!.AdhocTags
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == globalTag.Id);
        Assert.That(tagRow.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
    }

    [Test]
    public void CreateTag_Throws_ForNonAdminWorkerZero()
    {
        var sut = CreateSut();

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.CreateTag(0, "not-allowed"));
    }

    [Test]
    public async Task RenameTag_Throws_ForNonAdminWorkerZero_EvenOnAWorkerZeroOwnedTag()
    {
        // A tag stamped OwnerWorkerId = 0 (e.g. created before the C1/I1
        // guards existed) must still not be manageable through the shared
        // pseudo-identity without the admin flag.
        var zeroOwnedTag = new AdhocTag { Name = "zero-owned", OwnerWorkerId = 0 };
        await zeroOwnedTag.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.RenameTag(0, zeroOwnedTag.Id, "renamed"));
    }

    [Test]
    public async Task DeleteTag_Throws_ForNonAdminWorkerZero_EvenOnAWorkerZeroOwnedTag()
    {
        var zeroOwnedTag = new AdhocTag { Name = "zero-owned", OwnerWorkerId = 0 };
        await zeroOwnedTag.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.DeleteTag(0, zeroOwnedTag.Id));
    }
}
