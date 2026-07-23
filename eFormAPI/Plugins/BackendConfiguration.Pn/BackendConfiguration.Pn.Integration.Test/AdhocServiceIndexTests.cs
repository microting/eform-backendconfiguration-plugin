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
/// Drives <see cref="BackendConfigurationAdhocService.IndexTasks"/> (M5/P2) -
/// the dashboard table query that replaces <c>AdhocController</c>'s former
/// in-memory <c>ApplyFiltersAndPaging</c>/<c>ApplySort</c> (B6's own report
/// flagged that approach as untested and a scale risk). Every filter here is
/// pushed into SQL against <see cref="BackendConfigurationPnDbContext"/>
/// directly, so these tests exercise the real translated queries against
/// MariaDB, not an in-memory provider.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class AdhocServiceIndexTests : TestBaseSetup
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

    private static AdhocTaskCreateModel MakeCreateModel(
        int propertyId,
        string? title = null,
        int? areaId = null,
        List<int>? tagIds = null,
        List<int>? assignedWorkerIds = null)
    {
        return new AdhocTaskCreateModel
        {
            Title = title ?? Guid.NewGuid().ToString(),
            Description = "desc",
            PropertyId = propertyId,
            AreaId = areaId,
            TagIds = tagIds ?? [],
            AssignedWorkerIds = assignedWorkerIds ?? [],
        };
    }

    [Test]
    public async Task IndexTasks_IsAdmin_ReturnsCountsAcrossAllStatuses()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        var open = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        var toComplete = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        var toArchive = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        await sut.SetCompleted(1, toComplete.Id, true, isAdmin: true);
        await sut.SetCompleted(1, toArchive.Id, true, isAdmin: true);
        await sut.Archive(1, toArchive.Id, isAdmin: true);

        var result = await sut.IndexTasks(0, isAdmin: true, new AdhocTaskFiltersModel());

        Assert.That(result.OpenCount, Is.EqualTo(1));
        Assert.That(result.CompletedCount, Is.EqualTo(1));
        Assert.That(result.ArchivedCount, Is.EqualTo(1));
        Assert.That(result.Total, Is.EqualTo(3));
        Assert.That(result.Entities.Select(t => t.Id), Is.EquivalentTo(new[] { open.Id, toComplete.Id, toArchive.Id }));
    }

    [Test]
    public async Task IndexTasks_StatusFilter_NarrowsEntities_ButCountsStayAcrossAllStatuses()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        var open = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        var completed = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        await sut.SetCompleted(1, completed.Id, true, isAdmin: true);

        var result = await sut.IndexTasks(0, isAdmin: true, new AdhocTaskFiltersModel
        {
            Status = AdhocTaskStatusFilter.Open,
        });

        Assert.That(result.Entities.Select(t => t.Id), Is.EquivalentTo(new[] { open.Id }));
        Assert.That(result.Total, Is.EqualTo(1));
        // Counts unaffected by the Status filter itself.
        Assert.That(result.OpenCount, Is.EqualTo(1));
        Assert.That(result.CompletedCount, Is.EqualTo(1));
    }

    [Test]
    public async Task IndexTasks_NonAdmin_OnlyReturnsCallersVisibleTasks()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 7);
        var sut = CreateSut();

        var mine = await sut.CreateTask(1, MakeCreateModel(property.Id));
        var notMine = await sut.CreateTask(7, MakeCreateModel(property.Id));

        var result = await sut.IndexTasks(1, isAdmin: false, new AdhocTaskFiltersModel());

        Assert.That(result.Entities.Select(t => t.Id), Is.EquivalentTo(new[] { mine.Id }));
        Assert.That(result.Entities.Select(t => t.Id), Does.Not.Contain(notMine.Id));
    }

    [Test]
    public async Task IndexTasks_PropertyFilter_NarrowsToThatProperty()
    {
        var propertyA = await CreatePropertyAsync();
        var propertyB = await CreatePropertyAsync();
        var sut = CreateSut();

        var taskA = await sut.CreateTask(1, MakeCreateModel(propertyA.Id), isAdmin: true);
        await sut.CreateTask(1, MakeCreateModel(propertyB.Id), isAdmin: true);

        var result = await sut.IndexTasks(0, isAdmin: true, new AdhocTaskFiltersModel { PropertyId = propertyA.Id });

        Assert.That(result.Entities.Select(t => t.Id), Is.EquivalentTo(new[] { taskA.Id }));
    }

    [Test]
    public async Task IndexTasks_AreaFilter_NarrowsToThatArea()
    {
        var property = await CreatePropertyAsync();
        var area = new AdhocArea { PropertyId = property.Id, Name = "Barn" };
        await area.Create(BackendConfigurationPnDbContext!);
        var sut = CreateSut();

        var inArea = await sut.CreateTask(1, MakeCreateModel(property.Id, areaId: area.Id), isAdmin: true);
        await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);

        var result = await sut.IndexTasks(0, isAdmin: true, new AdhocTaskFiltersModel { AreaId = area.Id });

        Assert.That(result.Entities.Select(t => t.Id), Is.EquivalentTo(new[] { inArea.Id }));
    }

    [Test]
    public async Task IndexTasks_SearchText_MatchesTitleOrDescription()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();

        var match = await sut.CreateTask(1, MakeCreateModel(property.Id, title: "Broken window"), isAdmin: true);
        await sut.CreateTask(1, MakeCreateModel(property.Id, title: "Leaking pipe"), isAdmin: true);

        var result = await sut.IndexTasks(0, isAdmin: true, new AdhocTaskFiltersModel { SearchText = "window" });

        Assert.That(result.Entities.Select(t => t.Id), Is.EquivalentTo(new[] { match.Id }));
    }

    [Test]
    public async Task IndexTasks_TagFilter_Any_MatchesTaskWithAtLeastOneTag()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();
        var tagA = await sut.CreateTag(1, "a");
        var tagB = await sut.CreateTag(1, "b");

        var hasA = await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tagA.Id]), isAdmin: true);
        var hasB = await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tagB.Id]), isAdmin: true);
        await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);

        var result = await sut.IndexTasks(0, isAdmin: true, new AdhocTaskFiltersModel
        {
            TagIds = [tagA.Id, tagB.Id],
            TagsMatchAll = false,
        });

        Assert.That(result.Entities.Select(t => t.Id), Is.EquivalentTo(new[] { hasA.Id, hasB.Id }));
    }

    [Test]
    public async Task IndexTasks_TagFilter_All_RequiresEveryTag()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();
        var tagA = await sut.CreateTag(1, "a");
        var tagB = await sut.CreateTag(1, "b");

        var hasBoth = await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tagA.Id, tagB.Id]), isAdmin: true);
        await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tagA.Id]), isAdmin: true);

        var result = await sut.IndexTasks(0, isAdmin: true, new AdhocTaskFiltersModel
        {
            TagIds = [tagA.Id, tagB.Id],
            TagsMatchAll = true,
        });

        Assert.That(result.Entities.Select(t => t.Id), Is.EquivalentTo(new[] { hasBoth.Id }));
    }

    [Test]
    public async Task IndexTasks_Paging_ReturnsRequestedPageAndTotal()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();
        for (var i = 0; i < 5; i++)
        {
            await sut.CreateTask(1, MakeCreateModel(property.Id, title: $"Task {i}"), isAdmin: true);
        }

        var page1 = await sut.IndexTasks(0, isAdmin: true, new AdhocTaskFiltersModel { PageNumber = 1, PageSize = 2 });
        var page2 = await sut.IndexTasks(0, isAdmin: true, new AdhocTaskFiltersModel { PageNumber = 2, PageSize = 2 });

        Assert.That(page1.Total, Is.EqualTo(5));
        Assert.That(page1.Entities, Has.Count.EqualTo(2));
        Assert.That(page2.Entities, Has.Count.EqualTo(2));
        Assert.That(page1.Entities.Select(t => t.Id), Is.Not.EquivalentTo(page2.Entities.Select(t => t.Id)));
    }

    [Test]
    public async Task IndexTasks_SortByTitle_Ascending()
    {
        var property = await CreatePropertyAsync();
        var sut = CreateSut();
        await sut.CreateTask(1, MakeCreateModel(property.Id, title: "Bravo"), isAdmin: true);
        await sut.CreateTask(1, MakeCreateModel(property.Id, title: "Alpha"), isAdmin: true);
        await sut.CreateTask(1, MakeCreateModel(property.Id, title: "Charlie"), isAdmin: true);

        var result = await sut.IndexTasks(0, isAdmin: true, new AdhocTaskFiltersModel
        {
            SortColumn = "title",
            SortAscending = true,
        });

        Assert.That(result.Entities.Select(t => t.Title), Is.EqualTo(new[] { "Alpha", "Bravo", "Charlie" }));
    }
}
