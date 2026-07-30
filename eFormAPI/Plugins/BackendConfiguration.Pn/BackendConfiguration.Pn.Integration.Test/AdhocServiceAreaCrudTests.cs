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
/// Drives the area mutation trio (CreateAreas/RenameArea/DeleteArea, spec
/// 2026-07-30-adhoc-area-management-design.md). Same fixture conventions as
/// <see cref="AdhocServiceReferenceDataTests"/>: FK-safe [SetUp] cleanup
/// because the Adhoc* tables postdate the raw SQL bootstrap script.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class AdhocServiceAreaCrudTests : TestBaseSetup
{
    [SetUp]
    public async Task CleanAdhocTables()
    {
        BackendConfigurationPnDbContext!.AdhocTasks.RemoveRange(
            BackendConfigurationPnDbContext.AdhocTasks);
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

    // --- CreateAreas ---

    [Test]
    public async Task CreateAreas_CreatesTrimmedAreas_AndReturnsRefreshedList()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        var result = await sut.CreateAreas(0, property.Id, ["  Lade  ", "Stald"], isAdmin: true);

        Assert.That(result.Select(a => a.Name), Is.EquivalentTo(new[] { "Lade", "Stald" }));
        var stored = await BackendConfigurationPnDbContext!.AdhocAreas.ToListAsync();
        Assert.That(stored.Select(a => a.Name), Is.EquivalentTo(new[] { "Lade", "Stald" }));
        Assert.That(stored.All(a => a.PropertyId == property.Id), Is.True);
    }

    [Test]
    public async Task CreateAreas_SkipsEmpties_InBatchDuplicates_AndExistingActives_CaseInsensitively()
    {
        var property = await CreatePropertyAsync();
        var existing = new AdhocArea { PropertyId = property.Id, Name = "Lade" };
        await existing.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        var result = await sut.CreateAreas(
            0, property.Id, ["", "  ", "lade", "Stald", "STALD", "Mark"], isAdmin: true);

        Assert.That(result.Select(a => a.Name), Is.EquivalentTo(new[] { "Lade", "Stald", "Mark" }));
        Assert.That(await BackendConfigurationPnDbContext!.AdhocAreas.CountAsync(), Is.EqualTo(3));
    }

    [Test]
    public async Task CreateAreas_IsIdempotent_OnResubmit()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        await sut.CreateAreas(0, property.Id, ["Lade", "Stald"], isAdmin: true);
        var second = await sut.CreateAreas(0, property.Id, ["Lade", "Stald"], isAdmin: true);

        Assert.That(second, Has.Count.EqualTo(2));
        Assert.That(await BackendConfigurationPnDbContext!.AdhocAreas.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task CreateAreas_DoesNotCollideWith_RemovedAreaName()
    {
        var property = await CreatePropertyAsync();
        var removed = new AdhocArea { PropertyId = property.Id, Name = "Lade" };
        await removed.Create(BackendConfigurationPnDbContext!);
        await removed.Delete(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        var result = await sut.CreateAreas(0, property.Id, ["Lade"], isAdmin: true);

        Assert.That(result.Single().Name, Is.EqualTo("Lade"));
        Assert.That(result.Single().Id, Is.Not.EqualTo(removed.Id));
    }

    [Test]
    public async Task CreateAreas_Throws_ForUnknownProperty()
    {
        var sut = CreateSut();

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.CreateAreas(0, 999999, ["Lade"], isAdmin: true));
    }

    // --- RenameArea ---

    [Test]
    public async Task RenameArea_Succeeds_AndTrims()
    {
        var property = await CreatePropertyAsync();
        var area = new AdhocArea { PropertyId = property.Id, Name = "Lade" };
        await area.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        var result = await sut.RenameArea(0, area.Id, "  Maskinhal  ", isAdmin: true);

        Assert.That(result.Name, Is.EqualTo("Maskinhal"));
        var reloaded = await BackendConfigurationPnDbContext!.AdhocAreas.SingleAsync(a => a.Id == area.Id);
        Assert.That(reloaded.Name, Is.EqualTo("Maskinhal"));
    }

    [Test]
    public async Task RenameArea_Throws_OnDuplicateName_CaseInsensitive()
    {
        var property = await CreatePropertyAsync();
        var area = new AdhocArea { PropertyId = property.Id, Name = "Lade" };
        await area.Create(BackendConfigurationPnDbContext!);
        var other = new AdhocArea { PropertyId = property.Id, Name = "Stald" };
        await other.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.RenameArea(0, area.Id, "stald", isAdmin: true));
    }

    [Test]
    public async Task RenameArea_AllowsSameNameOnOtherProperty()
    {
        var property = await CreatePropertyAsync();
        var otherProperty = await CreatePropertyAsync();
        var area = new AdhocArea { PropertyId = property.Id, Name = "Lade" };
        await area.Create(BackendConfigurationPnDbContext!);
        var otherArea = new AdhocArea { PropertyId = otherProperty.Id, Name = "Stald" };
        await otherArea.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        var result = await sut.RenameArea(0, area.Id, "Stald", isAdmin: true);

        Assert.That(result.Name, Is.EqualTo("Stald"));
    }

    [Test]
    public async Task RenameArea_Throws_ForUnknownOrRemovedArea()
    {
        var property = await CreatePropertyAsync();
        var removed = new AdhocArea { PropertyId = property.Id, Name = "Lade" };
        await removed.Create(BackendConfigurationPnDbContext!);
        await removed.Delete(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        Assert.ThrowsAsync<AdhocAreaNotFoundException>(async () =>
            await sut.RenameArea(0, removed.Id, "Ny", isAdmin: true));
        Assert.ThrowsAsync<AdhocAreaNotFoundException>(async () =>
            await sut.RenameArea(0, 999999, "Ny", isAdmin: true));
    }

    [Test]
    public async Task RenameArea_Throws_OnEmptyName()
    {
        var property = await CreatePropertyAsync();
        var area = new AdhocArea { PropertyId = property.Id, Name = "Lade" };
        await area.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.RenameArea(0, area.Id, "   ", isAdmin: true));
    }

    // --- DeleteArea ---

    [Test]
    public async Task DeleteArea_SoftDeletes_AndListAreasStopsReturningIt()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        var area = new AdhocArea { PropertyId = property.Id, Name = "Lade" };
        await area.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        await sut.DeleteArea(0, area.Id, isAdmin: true);

        var reloaded = await BackendConfigurationPnDbContext!.AdhocAreas.SingleAsync(a => a.Id == area.Id);
        Assert.That(reloaded.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(await sut.ListAreas(1, property.Id), Is.Empty);
    }

    [Test]
    public async Task DeleteArea_LeavesReferencingTasksUntouched()
    {
        var property = await CreatePropertyAsync();
        var area = new AdhocArea { PropertyId = property.Id, Name = "Lade" };
        await area.Create(BackendConfigurationPnDbContext!);
        BackendConfigurationPnDbContext!.AdhocTasks.Add(new AdhocTaskEntity
        {
            Title = "Task in Lade",
            Description = "d",
            PropertyId = property.Id,
            AreaId = area.Id,
            CreatedByWorkerId = 1,
            WorkflowState = Constants.WorkflowStates.Created,
        });
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        var sut = CreateSut();

        await sut.DeleteArea(0, area.Id, isAdmin: true);

        var task = await BackendConfigurationPnDbContext.AdhocTasks.SingleAsync();
        Assert.That(task.AreaId, Is.EqualTo(area.Id));
        Assert.That(task.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    [Test]
    public async Task DeleteArea_Throws_ForUnknownArea()
    {
        var sut = CreateSut();

        Assert.ThrowsAsync<AdhocAreaNotFoundException>(async () =>
            await sut.DeleteArea(0, 999999, isAdmin: true));
    }

    // --- Access guard (all three mutations share RequirePropertyAccessAsync) ---

    [Test]
    public async Task AreaMutations_AdmitAdminDashboardIdentity()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        var created = await sut.CreateAreas(0, property.Id, ["Lade"], isAdmin: true);
        var renamed = await sut.RenameArea(0, created.Single().Id, "Stald", isAdmin: true);
        await sut.DeleteArea(0, renamed.Id, isAdmin: true);

        Assert.That(await BackendConfigurationPnDbContext!.AdhocAreas
            .CountAsync(a => a.WorkflowState != Constants.WorkflowStates.Removed), Is.EqualTo(0));
    }

    [Test]
    public async Task AreaMutations_DenyNonAdminWorkerZero_MatchingListAreas()
    {
        var property = await CreatePropertyAsync();
        var area = new AdhocArea { PropertyId = property.Id, Name = "Lade" };
        await area.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.CreateAreas(0, property.Id, ["Stald"]));
        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.RenameArea(0, area.Id, "Stald"));
        Assert.ThrowsAsync<AdhocTaskUnauthorizedException>(async () =>
            await sut.DeleteArea(0, area.Id));
    }

    [Test]
    public async Task AreaMutations_AdmitRealWorkerWithPropertyAccess()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 7);
        var sut = CreateSut();

        var created = await sut.CreateAreas(7, property.Id, ["Lade"]);
        var renamed = await sut.RenameArea(7, created.Single().Id, "Stald");
        await sut.DeleteArea(7, renamed.Id);

        Assert.That(await BackendConfigurationPnDbContext!.AdhocAreas
            .CountAsync(a => a.WorkflowState != Constants.WorkflowStates.Removed), Is.EqualTo(0));
    }
}
