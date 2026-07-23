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
/// Drives <see cref="BackendConfigurationAdhocService.ListHistory"/> (M5/P2).
/// See <see cref="AdhocTaskHistoryEventModel"/>'s doc comment for the
/// derivation approach (scalar timestamps + assignment log + comments, NOT
/// version-table diffing) and its accepted v1 limitations (no standalone
/// "reopened" event). Worker display-name resolution needs a real SDK Core
/// (same as <c>ListWorkers</c>/<c>AdhocServiceReferenceDataTests</c>), so
/// every test wires <see cref="TestBaseSetup.GetCore"/> through the
/// substituted <see cref="IEFormCoreService"/>.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class AdhocServiceHistoryTests : TestBaseSetup
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
        List<int>? tagIds = null,
        List<int>? assignedWorkerIds = null)
    {
        return new AdhocTaskCreateModel
        {
            Title = title ?? Guid.NewGuid().ToString(),
            Description = "desc",
            PropertyId = propertyId,
            TagIds = tagIds ?? [],
            AssignedWorkerIds = assignedWorkerIds ?? [],
        };
    }

    private async Task SetTaskCreatedAtAsync(int taskId, DateTime createdAt)
    {
        // Direct row edit (bypassing PnBase.Update/versioning) - the only way
        // to exercise ListHistory's date-range filter deterministically,
        // since PnBase.Create always stamps CreatedAt = DateTime.UtcNow.
        var raw = await BackendConfigurationPnDbContext!.AdhocTasks.FirstAsync(t => t.Id == taskId);
        raw.CreatedAt = createdAt;
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    [Test]
    public async Task ListHistory_IsAdmin_EmitsCreatedEvent_ForEveryTask()
    {
        var property = await CreatePropertyAsync("Main Street 1");
        var core = await GetCore();
        var sut = CreateSut(core);

        var task = await sut.CreateTask(1, MakeCreateModel(property.Id, title: "Fix the roof"), isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        var created = result.Entities.Single(e => e.TaskId == task.Id && e.EventType == "created");
        Assert.That(created.TaskTitle, Is.EqualTo("Fix the roof"));
        Assert.That(created.PropertyName, Is.EqualTo("Main Street 1"));
        Assert.That(result.Total, Is.EqualTo(result.Entities.Count));
    }

    [Test]
    public async Task ListHistory_EmitsCompletedEvent_WhenTaskCompleted()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var task = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);

        await sut.SetCompleted(1, task.Id, true, isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        var completedEvent = result.Entities.Single(e => e.TaskId == task.Id && e.EventType == "completed");
        Assert.That(completedEvent.Completed, Is.True);
        Assert.That(completedEvent.Archived, Is.False);
    }

    [Test]
    public async Task ListHistory_EmitsArchivedEvent_WhenTaskArchived()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var task = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        await sut.SetCompleted(1, task.Id, true, isAdmin: true);
        await sut.Archive(1, task.Id, isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        Assert.That(result.Entities.Any(e => e.TaskId == task.Id && e.EventType == "completed"), Is.True);
        var archivedEvent = result.Entities.Single(e => e.TaskId == task.Id && e.EventType == "archived");
        Assert.That(archivedEvent.Archived, Is.True);
    }

    [Test]
    public async Task ListHistory_EmitsAssignedEvent_WhenTaskCreatedWithAssignments()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 7);
        var core = await GetCore();
        var sut = CreateSut(core);

        var task = await sut.CreateTask(1, MakeCreateModel(property.Id, assignedWorkerIds: [7]), isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        Assert.That(result.Entities.Any(e => e.TaskId == task.Id && e.EventType == "assigned"), Is.True);
    }

    [Test]
    public async Task ListHistory_EmitsCommentedEvent_PerComment_AndSurfacesLastComment()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var task = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);

        await sut.AddComment(1, task.Id, "first", isAdmin: true);
        await sut.AddComment(1, task.Id, "second", isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        var commentEvents = result.Entities.Where(e => e.TaskId == task.Id && e.EventType == "commented").ToList();
        Assert.That(commentEvents, Has.Count.EqualTo(2));

        var createdEvent = result.Entities.Single(e => e.TaskId == task.Id && e.EventType == "created");
        Assert.That(createdEvent.LastCommentText, Is.EqualTo("second"));
    }

    [Test]
    public async Task ListHistory_TagNames_ReflectTaskTags()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var tag = await sut.CreateTag(1, "urgent");
        var task = await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tag.Id]), isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        var createdEvent = result.Entities.Single(e => e.TaskId == task.Id && e.EventType == "created");
        Assert.That(createdEvent.TagNames, Is.EquivalentTo(new[] { "urgent" }));
    }

    [Test]
    public async Task ListHistory_ResolvesActorNames_FromSdkSites()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var language = await sdkDbContext.Languages.FirstAsync();
        var site = new Microting.eForm.Infrastructure.Data.Entities.Site
        {
            Name = "Jane Doe",
            LanguageId = language.Id,
            WorkflowState = Constants.WorkflowStates.Created,
        };
        await sdkDbContext.Sites.AddAsync(site);
        await sdkDbContext.SaveChangesAsync();
        await GrantPropertyAccessAsync(property.Id, site.Id);

        var sut = CreateSut(core);
        var task = await sut.CreateTask(site.Id, MakeCreateModel(property.Id));

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        var createdEvent = result.Entities.Single(e => e.TaskId == task.Id && e.EventType == "created");
        Assert.That(createdEvent.ActorName, Is.EqualTo("Jane Doe"));
    }

    [Test]
    public async Task ListHistory_DateRangeFilter_ExcludesEventsOutsideRange()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var oldTask = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        var recentTask = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        await SetTaskCreatedAtAsync(oldTask.Id, DateTime.UtcNow.AddDays(-90));

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel
        {
            DateFrom = DateTime.UtcNow.AddDays(-7),
            DateTo = DateTime.UtcNow.AddDays(1),
        });

        Assert.That(result.Entities.Any(e => e.TaskId == recentTask.Id), Is.True);
        Assert.That(result.Entities.Any(e => e.TaskId == oldTask.Id), Is.False);
    }

    [Test]
    public async Task ListHistory_PropertyFilter_NarrowsToThatProperty()
    {
        var propertyA = await CreatePropertyAsync();
        var propertyB = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var taskA = await sut.CreateTask(1, MakeCreateModel(propertyA.Id), isAdmin: true);
        await sut.CreateTask(1, MakeCreateModel(propertyB.Id), isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel { PropertyId = propertyA.Id });

        Assert.That(result.Entities.All(e => e.TaskId == taskA.Id), Is.True);
        Assert.That(result.Entities, Is.Not.Empty);
    }

    [Test]
    public async Task ListHistory_TagFilter_IsAndOnly_RequiresEveryTag()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var tagA = await sut.CreateTag(1, "a");
        var tagB = await sut.CreateTag(1, "b");
        var hasBoth = await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tagA.Id, tagB.Id]), isAdmin: true);
        var hasOnlyA = await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tagA.Id]), isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel
        {
            TagIds = [tagA.Id, tagB.Id],
        });

        Assert.That(result.Entities.Any(e => e.TaskId == hasBoth.Id), Is.True);
        Assert.That(result.Entities.Any(e => e.TaskId == hasOnlyA.Id), Is.False);
    }

    [Test]
    public async Task ListHistory_NonAdmin_OnlyReturnsCallersVisibleTasks()
    {
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 7);
        var core = await GetCore();
        var sut = CreateSut(core);
        var mine = await sut.CreateTask(1, MakeCreateModel(property.Id));
        var notMine = await sut.CreateTask(7, MakeCreateModel(property.Id));

        var result = await sut.ListHistory(1, isAdmin: false, new AdhocHistoryFiltersModel());

        Assert.That(result.Entities.Any(e => e.TaskId == mine.Id), Is.True);
        Assert.That(result.Entities.Any(e => e.TaskId == notMine.Id), Is.False);
    }

    [Test]
    public async Task ListHistory_SortsEventsDescendingByOccurredAt()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var older = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        var newer = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        await SetTaskCreatedAtAsync(older.Id, DateTime.UtcNow.AddDays(-3));
        await SetTaskCreatedAtAsync(newer.Id, DateTime.UtcNow.AddDays(-1));

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        var occurredAtInOrder = result.Entities.Select(e => e.OccurredAt).ToList();
        var expectedOrder = occurredAtInOrder.OrderByDescending(d => d).ToList();
        Assert.That(occurredAtInOrder, Is.EqualTo(expectedOrder));
    }

    [Test]
    public async Task ListHistory_Paging_ReturnsRequestedPageAndTotal()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        for (var i = 0; i < 5; i++)
        {
            await sut.CreateTask(1, MakeCreateModel(property.Id, title: $"Task {i}"), isAdmin: true);
        }

        var page1 = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel { PageNumber = 1, PageSize = 2 });
        var page2 = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel { PageNumber = 2, PageSize = 2 });

        Assert.That(page1.Total, Is.EqualTo(5)); // one "created" event per task, none completed/archived/commented/assigned
        Assert.That(page1.Entities, Has.Count.EqualTo(2));
        Assert.That(page2.Entities, Has.Count.EqualTo(2));
    }
}
