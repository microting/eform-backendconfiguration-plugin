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
/// Drives <see cref="BackendConfigurationAdhocService.ListHistory"/> (#1095,
/// mockup parity): one <see cref="AdhocTaskHistoryRowModel"/> per task
/// currently Completed ("Løst") or Archived - open tasks never appear -
/// keyed and sorted on the completion date (never the archive date), with a
/// date-truncated, both-ends-inclusive CompletedAt range filter. Worker
/// display-name resolution needs a real SDK Core (same as
/// <c>ListWorkers</c>/<c>AdhocServiceReferenceDataTests</c>), so every test
/// wires <see cref="TestBaseSetup.GetCore"/> through the substituted
/// <see cref="IEFormCoreService"/>.
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

    private async Task SetTaskCompletedAtAsync(int taskId, DateTime completedAt)
    {
        // Direct row edit (bypassing PnBase.Update/versioning) - the only way
        // to exercise ListHistory's CompletedAt-keyed date-range filter and
        // sort deterministically, since SetCompleted always stamps
        // CompletedAt = DateTime.UtcNow.
        var raw = await BackendConfigurationPnDbContext!.AdhocTasks.FirstAsync(t => t.Id == taskId);
        raw.CompletedAt = completedAt;
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    [Test]
    public async Task ListHistory_ExcludesOpenTasks()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);

        // Created but never completed - the core #1095 regression guard:
        // open tasks (whose created/assigned/commented events used to leak
        // into the old per-event Historik) never appear at all.
        await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        Assert.That(result.Entities, Is.Empty);
        Assert.That(result.Total, Is.EqualTo(0));
    }

    [Test]
    public async Task ListHistory_CompletedTask_YieldsOneRowWithCompletedStatus()
    {
        var property = await CreatePropertyAsync("Main Street 1");
        var core = await GetCore();
        var sut = CreateSut(core);
        var task = await sut.CreateTask(1, MakeCreateModel(property.Id, title: "Fix the roof"), isAdmin: true);

        await sut.SetCompleted(1, task.Id, true, isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        var row = result.Entities.Single(r => r.TaskId == task.Id);
        Assert.That(row.TaskTitle, Is.EqualTo("Fix the roof"));
        Assert.That(row.PropertyName, Is.EqualTo("Main Street 1"));
        Assert.That(row.Status, Is.EqualTo(AdhocTaskStatusFilter.Completed));
        Assert.That(row.CompletedAt, Is.Not.EqualTo(default(DateTime)));
        Assert.That(result.Total, Is.EqualTo(1));
    }

    [Test]
    public async Task ListHistory_ArchivedTask_YieldsOneRowWithArchivedStatus()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var task = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        await sut.SetCompleted(1, task.Id, true, isAdmin: true);
        var completedAtStamp = (await BackendConfigurationPnDbContext!.AdhocTasks
            .FirstAsync(t => t.Id == task.Id)).CompletedAt!.Value;
        await sut.Archive(1, task.Id, isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        // Exactly ONE row per task - archiving must not add a second row.
        var row = result.Entities.Single(r => r.TaskId == task.Id);
        Assert.That(row.Status, Is.EqualTo(AdhocTaskStatusFilter.Archived));
        // "Udført" stays the completion date - never the archive date.
        Assert.That(row.CompletedAt, Is.EqualTo(completedAtStamp));
        Assert.That(result.Total, Is.EqualTo(1));
    }

    [Test]
    public async Task ListHistory_ArchivedTask_KeepsCompletedAtAsCompletionDate_NotArchiveDate()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var task = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        await sut.SetCompleted(1, task.Id, true, isAdmin: true);
        // Completion at T1, archive at T2 (now) with T2 > T1 - the row must
        // surface T1.
        var completionDate = new DateTime(2026, 3, 10, 11, 30, 0, DateTimeKind.Utc);
        await SetTaskCompletedAtAsync(task.Id, completionDate);
        await sut.Archive(1, task.Id, isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        var row = result.Entities.Single(r => r.TaskId == task.Id);
        Assert.That(row.CompletedAt, Is.EqualTo(completionDate));
        var archivedAt = (await BackendConfigurationPnDbContext!.AdhocTasks
            .FirstAsync(t => t.Id == task.Id)).ArchivedAt!.Value;
        Assert.That(row.CompletedAt, Is.Not.EqualTo(archivedAt));
    }

    [Test]
    public async Task ListHistory_SurfacesLastComment_OnTheSingleRow()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var task = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);

        await sut.AddComment(1, task.Id, "first", isAdmin: true);
        await sut.AddComment(1, task.Id, "second", isAdmin: true);

        // Not yet completed -> commented tasks yield NO Historik rows.
        var beforeCompletion = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());
        Assert.That(beforeCompletion.Entities, Is.Empty);

        await sut.SetCompleted(1, task.Id, true, isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        var row = result.Entities.Single(r => r.TaskId == task.Id);
        Assert.That(row.LastCommentText, Is.EqualTo("second"));
        Assert.That(row.LastCommentAt, Is.Not.Null);
    }

    [Test]
    public async Task ListHistory_TagNames_ReflectTaskTags()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var tag = await sut.CreateTag(1, "urgent");
        var task = await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tag.Id]), isAdmin: true);
        await sut.SetCompleted(1, task.Id, true, isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        var row = result.Entities.Single(r => r.TaskId == task.Id);
        Assert.That(row.TagNames, Is.EquivalentTo(new[] { "urgent" }));
    }

    [Test]
    public async Task ListHistory_ResolvesCompletedByName_FromSdkSites()
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
        await sut.SetCompleted(0, task.Id, true, isAdmin: true, completedByWorkerId: site.Id);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        var row = result.Entities.Single(r => r.TaskId == task.Id);
        Assert.That(row.CompletedByName, Is.EqualTo("Jane Doe"));
    }

    [Test]
    public async Task ListHistory_CompletedByName_IsEmptyString_WhenCompletedByWorkerIdIsNull()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var task = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);

        // Dashboard-style completion (synthetic workerId 0, no performer
        // selected) stamps a NULL CompletedByWorkerId - the row must carry
        // an empty string (the client renders its own "—" fallback), not
        // null and not a thrown lookup.
        await sut.SetCompleted(0, task.Id, true, isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        var row = result.Entities.Single(r => r.TaskId == task.Id);
        Assert.That(row.CompletedByName, Is.EqualTo(""));
    }

    [Test]
    public async Task ListHistory_DateRangeFilter_ExcludesRowsOutsideRange()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var oldTask = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        var recentTask = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        await sut.SetCompleted(1, oldTask.Id, true, isAdmin: true);
        await sut.SetCompleted(1, recentTask.Id, true, isAdmin: true);
        await SetTaskCompletedAtAsync(oldTask.Id, DateTime.UtcNow.AddDays(-90));

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel
        {
            DateFrom = DateTime.UtcNow.AddDays(-7),
            DateTo = DateTime.UtcNow.AddDays(1),
        });

        Assert.That(result.Entities.Any(r => r.TaskId == recentTask.Id), Is.True);
        Assert.That(result.Entities.Any(r => r.TaskId == oldTask.Id), Is.False);
    }

    [Test]
    public async Task ListHistory_CompletedAtRangeFilter_IsInclusiveOfBothBoundaryDates()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var onFromDay = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        var onToDay = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        await sut.SetCompleted(1, onFromDay.Id, true, isAdmin: true);
        await sut.SetCompleted(1, onToDay.Id, true, isAdmin: true);

        var dateFrom = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var dateTo = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);
        // Late on the DateFrom day and early on the DateTo day - the raw
        // (untruncated) comparison the old code used would wrongly drop the
        // DateTo-day task (00:01 > midnight DateTo). "inkl. begge dage"
        // requires whole-day inclusion at both ends.
        await SetTaskCompletedAtAsync(onFromDay.Id, dateFrom.AddHours(23).AddMinutes(59));
        await SetTaskCompletedAtAsync(onToDay.Id, dateTo.AddMinutes(1));

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
        });

        Assert.That(result.Entities.Any(r => r.TaskId == onFromDay.Id), Is.True);
        Assert.That(result.Entities.Any(r => r.TaskId == onToDay.Id), Is.True);
        Assert.That(result.Total, Is.EqualTo(2));
    }

    [Test]
    public async Task ListHistory_PropertyFilter_NarrowsToThatProperty()
    {
        var propertyA = await CreatePropertyAsync();
        var propertyB = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var taskA = await sut.CreateTask(1, MakeCreateModel(propertyA.Id), isAdmin: true);
        var taskB = await sut.CreateTask(1, MakeCreateModel(propertyB.Id), isAdmin: true);
        await sut.SetCompleted(1, taskA.Id, true, isAdmin: true);
        await sut.SetCompleted(1, taskB.Id, true, isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel { PropertyId = propertyA.Id });

        Assert.That(result.Entities.All(r => r.TaskId == taskA.Id), Is.True);
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
        await sut.SetCompleted(1, hasBoth.Id, true, isAdmin: true);
        await sut.SetCompleted(1, hasOnlyA.Id, true, isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel
        {
            TagIds = [tagA.Id, tagB.Id],
        });

        Assert.That(result.Entities.Any(r => r.TaskId == hasBoth.Id), Is.True);
        Assert.That(result.Entities.Any(r => r.TaskId == hasOnlyA.Id), Is.False);
    }

    [Test]
    public async Task ListHistory_TagFilter_And_RequiresAllTags_AcrossThreeTags()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var tagA = await sut.CreateTag(1, "a");
        var tagB = await sut.CreateTag(1, "b");
        var tagC = await sut.CreateTag(1, "c");
        var hasAll = await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tagA.Id, tagB.Id, tagC.Id]), isAdmin: true);
        var hasTwo = await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tagA.Id, tagB.Id]), isAdmin: true);
        var hasOne = await sut.CreateTask(1, MakeCreateModel(property.Id, tagIds: [tagC.Id]), isAdmin: true);
        await sut.SetCompleted(1, hasAll.Id, true, isAdmin: true);
        await sut.SetCompleted(1, hasTwo.Id, true, isAdmin: true);
        await sut.SetCompleted(1, hasOne.Id, true, isAdmin: true);

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel
        {
            TagIds = [tagA.Id, tagB.Id, tagC.Id],
        });

        Assert.That(result.Entities.Any(r => r.TaskId == hasAll.Id), Is.True);
        Assert.That(result.Entities.Any(r => r.TaskId == hasTwo.Id), Is.False);
        Assert.That(result.Entities.Any(r => r.TaskId == hasOne.Id), Is.False);
        Assert.That(result.Total, Is.EqualTo(1));
    }

    [Test]
    public async Task ListHistory_NonAdmin_OnlyReturnsCallersVisibleTasks()
    {
        // Same standing as IndexTasks_NonAdmin_OnlyReturnsCallersVisibleTasks:
        // ListHistory has NO gRPC call site - AdhocGrpcService never calls it -
        // so the web (Historik) is its only caller and it always passes
        // isAdmin: true since 2026-08-24. This therefore exercises a parameter
        // combination no current caller produces, and is NOT what keeps the
        // mobile path scoped. It is kept as the contract for the
        // non-full-access branch of the visibility predicate, should a caller
        // ever pass false again.
        var property = await CreatePropertyAsync();
        await GrantPropertyAccessAsync(property.Id, 1);
        await GrantPropertyAccessAsync(property.Id, 7);
        var core = await GetCore();
        var sut = CreateSut(core);
        var mine = await sut.CreateTask(1, MakeCreateModel(property.Id));
        var notMine = await sut.CreateTask(7, MakeCreateModel(property.Id));
        await sut.SetCompleted(1, mine.Id, true);
        await sut.SetCompleted(7, notMine.Id, true);

        var result = await sut.ListHistory(1, isAdmin: false, new AdhocHistoryFiltersModel());

        Assert.That(result.Entities.Any(r => r.TaskId == mine.Id), Is.True);
        Assert.That(result.Entities.Any(r => r.TaskId == notMine.Id), Is.False);
    }

    [Test]
    public async Task ListHistory_SortsRowsDescendingByCompletedAt()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        var older = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        var newer = await sut.CreateTask(1, MakeCreateModel(property.Id), isAdmin: true);
        await sut.SetCompleted(1, older.Id, true, isAdmin: true);
        await sut.SetCompleted(1, newer.Id, true, isAdmin: true);
        await SetTaskCompletedAtAsync(older.Id, DateTime.UtcNow.AddDays(-3));
        await SetTaskCompletedAtAsync(newer.Id, DateTime.UtcNow.AddDays(-1));

        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        Assert.That(result.Entities.Select(r => r.TaskId), Is.EqualTo(new[] { newer.Id, older.Id }));
        var completedAtInOrder = result.Entities.Select(r => r.CompletedAt).ToList();
        Assert.That(completedAtInOrder, Is.EqualTo(completedAtInOrder.OrderByDescending(d => d).ToList()));
    }

    [Test]
    public async Task ListHistory_Paging_ReturnsRequestedPageAndTotal()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        for (var i = 0; i < 5; i++)
        {
            var task = await sut.CreateTask(1, MakeCreateModel(property.Id, title: $"Task {i}"), isAdmin: true);
            await sut.SetCompleted(1, task.Id, true, isAdmin: true);
            // Distinct completion dates so the CompletedAt-desc ordering (and
            // therefore the page split) is deterministic.
            await SetTaskCompletedAtAsync(task.Id, DateTime.UtcNow.AddDays(-i));
        }

        var page1 = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel { PageNumber = 1, PageSize = 2 });
        var page2 = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel { PageNumber = 2, PageSize = 2 });

        Assert.That(page1.Total, Is.EqualTo(5)); // one row per completed task
        Assert.That(page1.Entities, Has.Count.EqualTo(2));
        Assert.That(page2.Entities, Has.Count.EqualTo(2));
        Assert.That(page1.Entities.Select(r => r.TaskId), Is.Not.EquivalentTo(page2.Entities.Select(r => r.TaskId)));
    }

    [Test]
    public async Task ListHistory_Paging_PageSizeDefaultsTo25_WhenUnspecified()
    {
        var property = await CreatePropertyAsync();
        var core = await GetCore();
        var sut = CreateSut(core);
        for (var i = 0; i < 26; i++)
        {
            var task = await sut.CreateTask(1, MakeCreateModel(property.Id, title: $"Task {i}"), isAdmin: true);
            await sut.SetCompleted(1, task.Id, true, isAdmin: true);
        }

        // No PageSize set - ClampPageSize's default (25) caps the page.
        var result = await sut.ListHistory(0, isAdmin: true, new AdhocHistoryFiltersModel());

        Assert.That(result.Total, Is.EqualTo(26));
        Assert.That(result.Entities, Has.Count.EqualTo(25));
    }
}
