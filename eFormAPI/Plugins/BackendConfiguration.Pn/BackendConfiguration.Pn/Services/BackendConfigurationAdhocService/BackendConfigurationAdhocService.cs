/*
The MIT License (MIT)

Copyright (c) 2007 - 2022 Microting A/S

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

#nullable enable

namespace BackendConfiguration.Pn.Services.BackendConfigurationAdhocService;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Infrastructure.Models.Adhoc;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using UserPropertyAccess;
using SdkUploadedData = Microting.eForm.Infrastructure.Data.Entities.UploadedData;

/// <summary>
/// Shared task CRUD + visibility implementation. Every read/authorization
/// check replicates, field for field, the predicates in the Dart
/// <c>TaskVisibility</c> class (<c>canSee</c>/<c>matchesScope</c>) so the
/// server is the authoritative mirror of the client's own filtering logic -
/// see the plan's Architecture section ("Authorization lives server-side
/// with the same predicates as the client").
/// </summary>
public class BackendConfigurationAdhocService(
    BackendConfigurationPnDbContext dbContext,
    IBackendConfigurationUserPropertyAccess propertyAccess,
    IEFormCoreService coreHelper,
    IAdhocPhotoStorage photoStorage)
    : IBackendConfigurationAdhocService
{
    public async Task<List<AdhocTaskModel>> ListTasks(int workerId, TaskScopeFilter scope, int? propertyId, bool isAdmin = false)
    {
        var query = dbContext.AdhocTasks
            .Where(t => t.WorkflowState != Constants.WorkflowStates.Removed);

        HashSet<int>? accessiblePropertyIds = null;
        if (!isAdmin)
        {
            accessiblePropertyIds = (await propertyAccess.GetAccessiblePropertyIdsAsync(workerId)).ToHashSet();
            query = query.Where(t => accessiblePropertyIds.Contains(t.PropertyId));
        }

        if (propertyId.HasValue)
        {
            query = query.Where(t => t.PropertyId == propertyId.Value);
        }

        var candidates = await query.ToListAsync();
        if (candidates.Count == 0)
        {
            return [];
        }

        var taskIds = candidates.Select(t => t.Id).ToList();
        var assignmentsByTask = await LoadAssignedWorkerIdsAsync(taskIds);

        var visible = candidates
            .Where(t => CanSee(t, workerId, isAdmin, isAdmin || accessiblePropertyIds!.Contains(t.PropertyId), assignmentsByTask.GetValueOrDefault(t.Id, [])))
            .Where(t => MatchesScope(t, scope, workerId, assignmentsByTask.GetValueOrDefault(t.Id, [])))
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        var result = new List<AdhocTaskModel>(visible.Count);
        foreach (var task in visible)
        {
            result.Add(await MapToModelAsync(task, assignmentsByTask.GetValueOrDefault(task.Id, [])));
        }

        return result;
    }

    public async Task<AdhocTaskIndexResultModel> IndexTasks(int workerId, bool isAdmin, AdhocTaskFiltersModel filters)
    {
        filters ??= new AdhocTaskFiltersModel();

        var query = BuildIndexCandidateQuery(workerId, isAdmin, filters);

        // Counts reflect every filter above EXCEPT Status, so the mockup's
        // status-tab counts stay meaningful as the caller switches tabs.
        var openCount = await query.CountAsync(t => !t.Completed && !t.Archived);
        var completedCount = await query.CountAsync(t => t.Completed && !t.Archived);
        var archivedCount = await query.CountAsync(t => t.Archived);

        if (filters.Status.HasValue)
        {
            query = filters.Status.Value switch
            {
                AdhocTaskStatusFilter.Open => query.Where(t => !t.Completed && !t.Archived),
                AdhocTaskStatusFilter.Completed => query.Where(t => t.Completed && !t.Archived),
                AdhocTaskStatusFilter.Archived => query.Where(t => t.Archived),
                _ => query
            };
        }

        var total = await query.CountAsync();

        query = ApplyIndexSort(query, filters.SortColumn, filters.SortAscending);

        var pageNumber = filters.PageNumber < 1 ? 1 : filters.PageNumber;
        var pageSize = filters.PageSize < 1 ? 25 : filters.PageSize;

        var pageEntities = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var taskIds = pageEntities.Select(t => t.Id).ToList();
        var assignmentsByTask = await LoadAssignedWorkerIdsAsync(taskIds);

        var mapped = new List<AdhocTaskModel>(pageEntities.Count);
        foreach (var task in pageEntities)
        {
            mapped.Add(await MapToModelAsync(task, assignmentsByTask.GetValueOrDefault(task.Id, [])));
        }

        return new AdhocTaskIndexResultModel
        {
            Total = total,
            Entities = mapped,
            OpenCount = openCount,
            CompletedCount = completedCount,
            ArchivedCount = archivedCount,
        };
    }

    /// <summary>
    /// Property-access/visibility/area/tag/search filters, all pushed into
    /// SQL - the part of the old controller-side <c>ApplyFiltersAndPaging</c>
    /// that scaled worst (it ran over every visible task, fully hydrated,
    /// before any of these were applied). Status is deliberately excluded
    /// here so <see cref="IndexTasks"/> can compute all three status counts
    /// against the same base query before narrowing to one status.
    /// </summary>
    private IQueryable<AdhocTaskEntity> BuildIndexCandidateQuery(int workerId, bool isAdmin, AdhocTaskFiltersModel filters)
    {
        var query = dbContext.AdhocTasks
            .Where(t => t.WorkflowState != Constants.WorkflowStates.Removed);

        if (!isAdmin)
        {
            var everyoneRule = (int)AdhocExecutionRuleValue.Everyone;
            query = query.Where(t =>
                dbContext.PropertyWorkers.Any(pw =>
                    pw.PropertyId == t.PropertyId && pw.WorkerId == workerId && pw.WorkflowState != Constants.WorkflowStates.Removed)
                && (t.CreatedByWorkerId == workerId
                    || t.ExecutionRule == everyoneRule
                    || dbContext.AdhocTaskAssignments.Any(a =>
                        a.AdhocTaskId == t.Id && a.WorkerId == workerId && a.WorkflowState != Constants.WorkflowStates.Removed)));
        }

        if (filters.PropertyId.HasValue)
        {
            query = query.Where(t => t.PropertyId == filters.PropertyId.Value);
        }

        if (filters.AreaId.HasValue)
        {
            query = query.Where(t => t.AreaId == filters.AreaId.Value);
        }

        if (filters.TagIds is { Count: > 0 })
        {
            if (filters.TagsMatchAll)
            {
                // AND: apply one correlated-EXISTS Where per tag, so a task
                // must satisfy every one of them.
                foreach (var tagId in filters.TagIds.Distinct())
                {
                    query = query.Where(t => dbContext.AdhocTaskTags.Any(tt =>
                        tt.AdhocTaskId == t.Id && tt.AdhocTagId == tagId && tt.WorkflowState != Constants.WorkflowStates.Removed));
                }
            }
            else
            {
                var tagIds = filters.TagIds.Distinct().ToList();
                query = query.Where(t => dbContext.AdhocTaskTags.Any(tt =>
                    tt.AdhocTaskId == t.Id && tagIds.Contains(tt.AdhocTagId) && tt.WorkflowState != Constants.WorkflowStates.Removed));
            }
        }

        if (!string.IsNullOrWhiteSpace(filters.SearchText))
        {
            var needle = filters.SearchText.Trim();
            query = query.Where(t => t.Title.Contains(needle) || t.Description.Contains(needle));
        }

        return query;
    }

    private static IQueryable<AdhocTaskEntity> ApplyIndexSort(IQueryable<AdhocTaskEntity> query, string? sortColumn, bool ascending)
    {
        return (sortColumn ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "title" => ascending ? query.OrderBy(t => t.Title) : query.OrderByDescending(t => t.Title),
            "deadline" => ascending ? query.OrderBy(t => t.Deadline) : query.OrderByDescending(t => t.Deadline),
            "urgent" => ascending ? query.OrderBy(t => t.Urgent) : query.OrderByDescending(t => t.Urgent),
            "completed" => ascending ? query.OrderBy(t => t.Completed) : query.OrderByDescending(t => t.Completed),
            _ => ascending ? query.OrderBy(t => t.CreatedAt) : query.OrderByDescending(t => t.CreatedAt),
        };
    }

    public async Task<AdhocTaskModel> GetTask(int workerId, int taskId, bool isAdmin = false)
    {
        var task = await LoadTaskOrThrowAsync(taskId);
        var assignedWorkerIds = await LoadAssignedWorkerIdsAsync(taskId);
        var hasPropertyAccess = isAdmin || await propertyAccess.HasAccessAsync(workerId, task.PropertyId);

        if (!CanSee(task, workerId, isAdmin, hasPropertyAccess, assignedWorkerIds))
        {
            throw new AdhocTaskUnauthorizedException(
                $"Worker {workerId} may not view adhoc task {taskId}.");
        }

        return await MapToModelAsync(task, assignedWorkerIds);
    }

    public async Task<AdhocTaskModel> CreateTask(int workerId, AdhocTaskCreateModel model, bool isAdmin = false)
    {
        await ValidateAreaBelongsToPropertyAsync(model.AreaId, model.PropertyId);

        var hasAccess = isAdmin || await propertyAccess.HasAccessAsync(workerId, model.PropertyId);
        if (!hasAccess)
        {
            throw new AdhocTaskUnauthorizedException(
                $"Worker {workerId} has no access to property {model.PropertyId}.");
        }

        var entity = new AdhocTaskEntity
        {
            Title = model.Title,
            Description = model.Description,
            Urgent = model.Urgent,
            PropertyId = model.PropertyId,
            AreaId = model.AreaId,
            VisibleFrom = model.VisibleFrom,
            Deadline = model.Deadline,
            VisibleReminder = model.VisibleReminder,
            DeadlineReminder = model.DeadlineReminder,
            DeadlineReminderRepeat = model.DeadlineReminderRepeat,
            VisibleReminderTimeMinutes = model.VisibleReminderTimeMinutes,
            DeadlineReminderTimeMinutes = model.DeadlineReminderTimeMinutes,
            ExecutionRule = model.ExecutionRule,
            CreatedByWorkerId = workerId,
            Completed = false,
            Archived = false,
        };
        await entity.Create(dbContext);

        foreach (var tagId in model.TagIds.Distinct())
        {
            var tagJoin = new AdhocTaskTag { AdhocTaskId = entity.Id, AdhocTagId = tagId };
            await tagJoin.Create(dbContext);
        }

        var assignedWorkerIds = model.AssignedWorkerIds.Distinct().ToList();
        foreach (var assignedWorkerId in assignedWorkerIds)
        {
            var assignment = new AdhocTaskAssignment { AdhocTaskId = entity.Id, WorkerId = assignedWorkerId };
            await assignment.Create(dbContext);
        }

        if (assignedWorkerIds.Count > 0)
        {
            await AppendAssignmentLogAsync(entity.Id, workerId, [], assignedWorkerIds);
        }

        return await MapToModelAsync(entity, assignedWorkerIds);
    }

    public async Task<AdhocTaskModel> UpdateTask(int workerId, int taskId, AdhocTaskCreateModel model, bool isAdmin = false)
    {
        var task = await LoadTaskOrThrowAsync(taskId);
        RequireCreator(task, workerId, isAdmin, "update");

        await ValidateAreaBelongsToPropertyAsync(model.AreaId, model.PropertyId);

        var hasAccess = await propertyAccess.HasAccessAsync(workerId, model.PropertyId);
        if (!hasAccess && !isAdmin)
        {
            throw new AdhocTaskUnauthorizedException(
                $"Worker {workerId} has no access to property {model.PropertyId}.");
        }

        task.Title = model.Title;
        task.Description = model.Description;
        task.Urgent = model.Urgent;
        task.PropertyId = model.PropertyId;
        task.AreaId = model.AreaId;
        task.VisibleFrom = model.VisibleFrom;
        task.Deadline = model.Deadline;
        task.VisibleReminder = model.VisibleReminder;
        task.DeadlineReminder = model.DeadlineReminder;
        task.DeadlineReminderRepeat = model.DeadlineReminderRepeat;
        task.VisibleReminderTimeMinutes = model.VisibleReminderTimeMinutes;
        task.DeadlineReminderTimeMinutes = model.DeadlineReminderTimeMinutes;
        task.ExecutionRule = model.ExecutionRule;
        await task.Update(dbContext);

        var assignedWorkerIds = await ReconcileAssignmentsAsync(taskId, workerId, model.AssignedWorkerIds);
        await ReconcileTagsAsync(taskId, model.TagIds);
        await ReconcilePhotosAsync(taskId, model.PhotoIds);

        return await MapToModelAsync(task, assignedWorkerIds);
    }

    public async Task<AdhocTaskModel> SetCompleted(int workerId, int taskId, bool completed, bool isAdmin = false, int? completedByWorkerId = null)
    {
        var task = await LoadTaskOrThrowAsync(taskId);
        var assignedWorkerIds = await LoadAssignedWorkerIdsAsync(taskId);
        var hasPropertyAccess = isAdmin || await propertyAccess.HasAccessAsync(workerId, task.PropertyId);

        if (!CanSee(task, workerId, isAdmin, hasPropertyAccess, assignedWorkerIds))
        {
            throw new AdhocTaskUnauthorizedException(
                $"Worker {workerId} may not complete adhoc task {taskId}.");
        }

        if (completed)
        {
            var stampedWorkerId = workerId;
            if (completedByWorkerId.HasValue)
            {
                var performerHasAccess = await propertyAccess.HasAccessAsync(completedByWorkerId.Value, task.PropertyId);
                if (!performerHasAccess)
                {
                    throw new ArgumentException(
                        $"Worker {completedByWorkerId.Value} has no access to property {task.PropertyId} and cannot be recorded as the performer.",
                        nameof(completedByWorkerId));
                }

                stampedWorkerId = completedByWorkerId.Value;
            }

            task.Completed = true;
            task.CompletedByWorkerId = stampedWorkerId;
            task.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            task.Completed = false;
            task.CompletedByWorkerId = null;
            task.CompletedAt = null;
        }

        await task.Update(dbContext);

        return await MapToModelAsync(task, assignedWorkerIds);
    }

    public async Task<AdhocTaskModel> Archive(int workerId, int taskId, bool isAdmin = false)
    {
        var task = await LoadTaskOrThrowAsync(taskId);
        RequireCreator(task, workerId, isAdmin, "archive");

        task.Archived = true;
        task.ArchivedAt = DateTime.UtcNow;
        await task.Update(dbContext);

        var assignedWorkerIds = await LoadAssignedWorkerIdsAsync(taskId);
        return await MapToModelAsync(task, assignedWorkerIds);
    }

    public async Task<AdhocTaskModel> Reopen(int workerId, int taskId, bool isAdmin = false)
    {
        var task = await LoadTaskOrThrowAsync(taskId);
        RequireCreator(task, workerId, isAdmin, "reopen");

        task.Archived = false;
        task.ArchivedAt = null;
        task.Completed = false;
        task.CompletedByWorkerId = null;
        task.CompletedAt = null;
        await task.Update(dbContext);

        var assignedWorkerIds = await LoadAssignedWorkerIdsAsync(taskId);
        return await MapToModelAsync(task, assignedWorkerIds);
    }

    public async Task Delete(int workerId, int taskId, bool isAdmin = false)
    {
        var task = await LoadTaskOrThrowAsync(taskId);
        RequireCreator(task, workerId, isAdmin, "delete");

        var assignments = await dbContext.AdhocTaskAssignments
            .Where(a => a.AdhocTaskId == taskId && a.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        foreach (var assignment in assignments)
        {
            await assignment.Delete(dbContext);
        }

        var logs = await dbContext.AdhocTaskAssignmentLogs
            .Where(l => l.AdhocTaskId == taskId && l.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        foreach (var log in logs)
        {
            await log.Delete(dbContext);
        }

        var comments = await dbContext.AdhocTaskComments
            .Where(c => c.AdhocTaskId == taskId && c.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        foreach (var comment in comments)
        {
            await comment.Delete(dbContext);
        }

        var photos = await dbContext.AdhocTaskPhotos
            .Where(p => p.AdhocTaskId == taskId && p.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        foreach (var photo in photos)
        {
            await photo.Delete(dbContext);
        }

        var tagJoins = await dbContext.AdhocTaskTags
            .Where(tt => tt.AdhocTaskId == taskId && tt.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        foreach (var tagJoin in tagJoins)
        {
            await tagJoin.Delete(dbContext);
        }

        await task.Delete(dbContext);
    }

    public async Task<AdhocTaskModel> AddComment(int workerId, int taskId, string text, bool isAdmin = false)
    {
        var task = await LoadTaskOrThrowAsync(taskId);
        var assignedWorkerIds = await LoadAssignedWorkerIdsAsync(taskId);
        var hasPropertyAccess = isAdmin || await propertyAccess.HasAccessAsync(workerId, task.PropertyId);

        if (!CanSee(task, workerId, isAdmin, hasPropertyAccess, assignedWorkerIds))
        {
            throw new AdhocTaskUnauthorizedException(
                $"Worker {workerId} may not comment on adhoc task {taskId}.");
        }

        var comment = new AdhocTaskComment
        {
            AdhocTaskId = taskId,
            AuthorWorkerId = workerId,
            Text = text,
        };
        await comment.Create(dbContext);

        return await MapToModelAsync(task, assignedWorkerIds);
    }

    public async Task<AdhocTaskModel> CopyTask(int workerId, bool isAdmin, int taskId, bool includeComments)
    {
        var source = await LoadTaskOrThrowAsync(taskId);
        var sourceAssignedWorkerIds = await LoadAssignedWorkerIdsAsync(taskId);
        var hasPropertyAccess = isAdmin || await propertyAccess.HasAccessAsync(workerId, source.PropertyId);

        if (!CanSee(source, workerId, isAdmin, hasPropertyAccess, sourceAssignedWorkerIds))
        {
            throw new AdhocTaskUnauthorizedException(
                $"Worker {workerId} may not copy adhoc task {taskId}.");
        }

        // New task, created by the copier (not the original's creator) -
        // REST "duplicate" semantics; starts uncompleted/unarchived
        // regardless of the source's current state.
        var copy = new AdhocTaskEntity
        {
            Title = source.Title,
            Description = source.Description,
            Urgent = source.Urgent,
            PropertyId = source.PropertyId,
            AreaId = source.AreaId,
            VisibleFrom = source.VisibleFrom,
            Deadline = source.Deadline,
            VisibleReminder = source.VisibleReminder,
            DeadlineReminder = source.DeadlineReminder,
            DeadlineReminderRepeat = source.DeadlineReminderRepeat,
            VisibleReminderTimeMinutes = source.VisibleReminderTimeMinutes,
            DeadlineReminderTimeMinutes = source.DeadlineReminderTimeMinutes,
            ExecutionRule = source.ExecutionRule,
            CreatedByWorkerId = workerId,
            Completed = false,
            Archived = false,
        };
        await copy.Create(dbContext);

        var sourceTagIds = await dbContext.AdhocTaskTags
            .Where(tt => tt.AdhocTaskId == taskId && tt.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(tt => tt.AdhocTagId)
            .ToListAsync();
        foreach (var tagId in sourceTagIds)
        {
            var tagJoin = new AdhocTaskTag { AdhocTaskId = copy.Id, AdhocTagId = tagId };
            await tagJoin.Create(dbContext);
        }

        foreach (var assignedWorkerId in sourceAssignedWorkerIds)
        {
            var assignment = new AdhocTaskAssignment { AdhocTaskId = copy.Id, WorkerId = assignedWorkerId };
            await assignment.Create(dbContext);
        }

        if (sourceAssignedWorkerIds.Count > 0)
        {
            await AppendAssignmentLogAsync(copy.Id, workerId, [], sourceAssignedWorkerIds);
        }

        // Photos: new AdhocTaskPhoto rows sharing the SAME UploadedDataId as
        // the source (verified safe - see IBackendConfigurationAdhocService.
        // CopyTask's doc comment).
        var sourcePhotos = await dbContext.AdhocTaskPhotos
            .Where(p => p.AdhocTaskId == taskId && p.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        foreach (var photo in sourcePhotos)
        {
            var photoCopy = new AdhocTaskPhoto
            {
                AdhocTaskId = copy.Id,
                UploadedDataId = photo.UploadedDataId,
                ContentType = photo.ContentType,
            };
            await photoCopy.Create(dbContext);
        }

        if (includeComments)
        {
            var sourceComments = await dbContext.AdhocTaskComments
                .Where(c => c.AdhocTaskId == taskId && c.WorkflowState != Constants.WorkflowStates.Removed)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync();
            foreach (var comment in sourceComments)
            {
                // AuthorWorkerId/Text preserved verbatim; CreatedAt cannot be
                // preserved through the shared PnBase.Create helper (always
                // stamps "now") - see the interface doc comment.
                var commentCopy = new AdhocTaskComment
                {
                    AdhocTaskId = copy.Id,
                    AuthorWorkerId = comment.AuthorWorkerId,
                    Text = comment.Text,
                };
                await commentCopy.Create(dbContext);
            }
        }

        return await MapToModelAsync(copy, sourceAssignedWorkerIds);
    }

    // --- Reference data: properties / areas / workers / tags ---

    public async Task<List<AdhocPropertyModel>> ListProperties(int workerId, bool isAdmin = false)
    {
        var query = dbContext.Properties
            .Where(p => p.WorkflowState != Constants.WorkflowStates.Removed);

        if (!isAdmin)
        {
            var accessiblePropertyIds = (await propertyAccess.GetAccessiblePropertyIdsAsync(workerId)).ToHashSet();
            query = query.Where(p => accessiblePropertyIds.Contains(p.Id));
        }

        return await query
            .OrderBy(p => p.Name)
            .Select(p => new AdhocPropertyModel { Id = p.Id, Name = p.Name })
            .ToListAsync();
    }

    public async Task<List<AdhocAreaModel>> ListAreas(int workerId, int propertyId, bool isAdmin = false)
    {
        await RequirePropertyAccessAsync(workerId, propertyId, isAdmin);

        return await dbContext.AdhocAreas
            .Where(a => a.PropertyId == propertyId && a.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(a => a.Name)
            .Select(a => new AdhocAreaModel { Id = a.Id, PropertyId = a.PropertyId, Name = a.Name })
            .ToListAsync();
    }

    public async Task<List<AdhocWorkerModel>> ListWorkers(int workerId, int propertyId, bool isAdmin = false)
    {
        await RequirePropertyAccessAsync(workerId, propertyId, isAdmin);

        var siteIds = await dbContext.PropertyWorkers
            .Where(pw => pw.PropertyId == propertyId && pw.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(pw => pw.WorkerId)
            .Distinct()
            .ToListAsync();
        if (siteIds.Count == 0)
        {
            return [];
        }

        // Second, narrower query: every property each of those workers has
        // access to (across ALL properties, not just propertyId), so
        // AdhocWorkerModel.PropertyIds reflects the worker's full access set.
        var otherAssignments = await dbContext.PropertyWorkers
            .Where(pw => siteIds.Contains(pw.WorkerId) && pw.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(pw => new { pw.WorkerId, pw.PropertyId })
            .ToListAsync();
        var propertyIdsByWorker = otherAssignments
            .GroupBy(pw => pw.WorkerId)
            .ToDictionary(g => g.Key, g => g.Select(pw => pw.PropertyId).Distinct().ToList());

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var siteNames = await sdkDbContext.Sites
            .Where(s => siteIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        return siteIds
            .Select(siteId => new AdhocWorkerModel
            {
                WorkerId = siteId,
                DisplayName = siteNames.GetValueOrDefault(siteId, ""),
                PropertyIds = propertyIdsByWorker.GetValueOrDefault(siteId, []),
            })
            .OrderBy(w => w.DisplayName)
            .ToList();
    }

    public async Task<List<AdhocTagModel>> ListTags(int workerId)
    {
        return await dbContext.AdhocTags
            .Where(t => t.WorkflowState != Constants.WorkflowStates.Removed
                        && (t.OwnerWorkerId == null || t.OwnerWorkerId == workerId))
            .OrderBy(t => t.Name)
            .Select(t => new AdhocTagModel { Id = t.Id, Name = t.Name, IsUserTag = t.OwnerWorkerId != null })
            .ToListAsync();
    }

    public async Task<AdhocTagModel> CreateTag(int workerId, string name, bool isAdmin = false)
    {
        RequireRealIdentityOrAdmin(workerId, isAdmin, "create");
        var trimmedName = RequireNonEmptyTagName(name);

        var tag = new AdhocTag { Name = trimmedName, OwnerWorkerId = isAdmin ? null : workerId };
        await tag.Create(dbContext);

        return new AdhocTagModel { Id = tag.Id, Name = tag.Name, IsUserTag = !isAdmin };
    }

    public async Task<AdhocTagModel> RenameTag(int workerId, int tagId, string name, bool isAdmin = false)
    {
        RequireRealIdentityOrAdmin(workerId, isAdmin, "rename");
        var trimmedName = RequireNonEmptyTagName(name);

        var tag = await LoadManageableTagOrThrowAsync(workerId, tagId, isAdmin, "rename");

        tag.Name = trimmedName;
        await tag.Update(dbContext);

        return new AdhocTagModel { Id = tag.Id, Name = tag.Name, IsUserTag = tag.OwnerWorkerId != null };
    }

    public async Task DeleteTag(int workerId, int tagId, bool isAdmin = false)
    {
        RequireRealIdentityOrAdmin(workerId, isAdmin, "delete");
        var tag = await LoadManageableTagOrThrowAsync(workerId, tagId, isAdmin, "delete");

        var joins = await dbContext.AdhocTaskTags
            .Where(tt => tt.AdhocTagId == tagId && tt.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        foreach (var join in joins)
        {
            await join.Delete(dbContext);
        }

        await tag.Delete(dbContext);
    }

    private async Task RequirePropertyAccessAsync(int workerId, int propertyId, bool isAdmin)
    {
        if (isAdmin)
        {
            return;
        }

        if (!await propertyAccess.HasAccessAsync(workerId, propertyId))
        {
            throw new AdhocTaskUnauthorizedException(
                $"Worker {workerId} has no access to property {propertyId}.");
        }
    }

    private static string RequireNonEmptyTagName(string name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Tag name must not be empty.", nameof(name));
        }

        return trimmed;
    }

    /// <summary>
    /// Tag mutations require a real identity: worker id 0 is the REST
    /// dashboard's synthetic caller (AdhocController.DashboardWorkerId) and
    /// owns nothing — a non-admin web user must never create/rename/delete
    /// tags through the shared pseudo-identity. Admins pass (they curate the
    /// customer's global tags); gRPC callers always have real, positive SDK
    /// site ids and never hit this guard.
    /// </summary>
    private static void RequireRealIdentityOrAdmin(int workerId, bool isAdmin, string action)
    {
        if (workerId == 0 && !isAdmin)
        {
            throw new AdhocTaskUnauthorizedException(
                $"Worker {workerId} may not {action} tags: dashboard callers must be admins.");
        }
    }

    private async Task<AdhocTag> LoadManageableTagOrThrowAsync(int workerId, int tagId, bool isAdmin, string action)
    {
        var tag = await dbContext.AdhocTags
            .FirstOrDefaultAsync(t => t.Id == tagId && t.WorkflowState != Constants.WorkflowStates.Removed);
        if (tag == null)
        {
            throw new AdhocTagNotFoundException(tagId);
        }

        // Admins manage every tag — in particular the global ones
        // (OwnerWorkerId == null), which no worker owns and which were
        // otherwise unmanageable by anyone. Non-admins only their own.
        if (!isAdmin && tag.OwnerWorkerId != workerId)
        {
            throw new AdhocTaskUnauthorizedException(
                $"Worker {workerId} may not {action} tag {tagId}: not the owner.");
        }

        return tag;
    }

    // --- Photos (S3 + SDK UploadedData) ---

    public async Task<int> SavePhoto(int workerId, int taskId, byte[] bytes, string contentType, bool isAdmin = false)
    {
        if (bytes == null || bytes.Length == 0)
        {
            throw new ArgumentException("Photo bytes must not be empty.", nameof(bytes));
        }

        var extension = NormalizePhotoExtension(contentType);

        var task = await LoadTaskOrThrowAsync(taskId);
        var assignedWorkerIds = await LoadAssignedWorkerIdsAsync(taskId);
        var hasPropertyAccess = isAdmin || await propertyAccess.HasAccessAsync(workerId, task.PropertyId);

        if (!CanSee(task, workerId, isAdmin, hasPropertyAccess, assignedWorkerIds))
        {
            throw new AdhocTaskUnauthorizedException(
                $"Worker {workerId} may not attach a photo to adhoc task {taskId}.");
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        string checksum;
        using (var md5 = MD5.Create())
        {
            checksum = BitConverter.ToString(md5.ComputeHash(bytes))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        // Two-phase FileName write mirrors EventsGrpcService.UploadPhoto /
        // BackendConfigurationTaskManagementService.CreateTask: create first
        // to get the row's Id, then fold it into the final S3 key so rows
        // sharing a checksum (re-uploads) don't collide in the shared bucket.
        // FileLocation is left blank - mobile photos are S3/IAdhocPhotoStorage
        // only, no local file is materialised (same contract EventsGrpcService.
        // UploadPhoto documents for its own UploadedData rows).
        var uploadedData = new SdkUploadedData
        {
            Checksum = checksum,
            FileName = $"{checksum}.{extension}",
            FileLocation = "",
            Extension = $".{extension}",
        };
        await uploadedData.Create(sdkDbContext).ConfigureAwait(false);

        var fileName = $"{uploadedData.Id}_{uploadedData.FileName}";
        uploadedData.FileName = fileName;
        await uploadedData.Update(sdkDbContext).ConfigureAwait(false);

        using (var stream = new MemoryStream(bytes))
        {
            await photoStorage.PutAsync(fileName, stream).ConfigureAwait(false);
        }

        var photo = new AdhocTaskPhoto
        {
            AdhocTaskId = taskId,
            UploadedDataId = uploadedData.Id,
            ContentType = contentType,
        };
        await photo.Create(dbContext).ConfigureAwait(false);

        return photo.Id;
    }

    public async Task<(Stream Content, string ContentType)> GetPhoto(int workerId, int photoId, bool isAdmin = false)
    {
        var photo = await dbContext.AdhocTaskPhotos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.WorkflowState != Constants.WorkflowStates.Removed);
        if (photo == null)
        {
            throw new AdhocTaskPhotoNotFoundException(photoId);
        }

        var task = await LoadTaskOrThrowAsync(photo.AdhocTaskId);
        var assignedWorkerIds = await LoadAssignedWorkerIdsAsync(task.Id);
        var hasPropertyAccess = isAdmin || await propertyAccess.HasAccessAsync(workerId, task.PropertyId);

        if (!CanSee(task, workerId, isAdmin, hasPropertyAccess, assignedWorkerIds))
        {
            throw new AdhocTaskUnauthorizedException(
                $"Worker {workerId} may not view photo {photoId} on adhoc task {task.Id}.");
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var uploadedData = await sdkDbContext.UploadedDatas
            .FirstOrDefaultAsync(u => u.Id == photo.UploadedDataId).ConfigureAwait(false);
        if (uploadedData == null)
        {
            throw new AdhocTaskPhotoNotFoundException(photoId);
        }

        var content = await photoStorage.GetAsync(uploadedData.FileName).ConfigureAwait(false);
        return (content, photo.ContentType);
    }

    private static string NormalizePhotoExtension(string contentType)
    {
        return contentType?.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => "jpg",
            "image/png" => "png",
            _ => throw new ArgumentException(
                $"contentType must be image/jpeg, image/jpg, or image/png (was '{contentType}').",
                nameof(contentType)),
        };
    }

    // --- History timeline (M5/P2) ---

    public async Task<Paged<AdhocTaskHistoryEventModel>> ListHistory(int workerId, bool isAdmin, AdhocHistoryFiltersModel filters)
    {
        filters ??= new AdhocHistoryFiltersModel();

        // Property/area/tag(AND)/access filters pushed into SQL against the
        // candidate task set - the dominant scale factor for a customer's
        // total adhoc-task volume. The per-task event explosion and the
        // date-range filter/sort/paging over the resulting events happen in
        // memory over that already-bounded candidate set (see the interface
        // doc comment).
        var candidateQuery = dbContext.AdhocTasks
            .Where(t => t.WorkflowState != Constants.WorkflowStates.Removed);

        if (!isAdmin)
        {
            var everyoneRule = (int)AdhocExecutionRuleValue.Everyone;
            candidateQuery = candidateQuery.Where(t =>
                dbContext.PropertyWorkers.Any(pw =>
                    pw.PropertyId == t.PropertyId && pw.WorkerId == workerId && pw.WorkflowState != Constants.WorkflowStates.Removed)
                && (t.CreatedByWorkerId == workerId
                    || t.ExecutionRule == everyoneRule
                    || dbContext.AdhocTaskAssignments.Any(a =>
                        a.AdhocTaskId == t.Id && a.WorkerId == workerId && a.WorkflowState != Constants.WorkflowStates.Removed)));
        }

        if (filters.PropertyId.HasValue)
        {
            candidateQuery = candidateQuery.Where(t => t.PropertyId == filters.PropertyId.Value);
        }

        if (filters.AreaId.HasValue)
        {
            candidateQuery = candidateQuery.Where(t => t.AreaId == filters.AreaId.Value);
        }

        if (filters.TagIds is { Count: > 0 })
        {
            foreach (var tagId in filters.TagIds.Distinct())
            {
                candidateQuery = candidateQuery.Where(t => dbContext.AdhocTaskTags.Any(tt =>
                    tt.AdhocTaskId == t.Id && tt.AdhocTagId == tagId && tt.WorkflowState != Constants.WorkflowStates.Removed));
            }
        }

        var candidates = await candidateQuery.ToListAsync();
        if (candidates.Count == 0)
        {
            return new Paged<AdhocTaskHistoryEventModel> { Total = 0, Entities = [] };
        }

        var taskIds = candidates.Select(t => t.Id).ToList();

        var assignmentLogs = await dbContext.AdhocTaskAssignmentLogs
            .Where(l => taskIds.Contains(l.AdhocTaskId) && l.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        var comments = await dbContext.AdhocTaskComments
            .Where(c => taskIds.Contains(c.AdhocTaskId) && c.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
        var commentsByTask = comments.GroupBy(c => c.AdhocTaskId).ToDictionary(g => g.Key, g => g.ToList());

        var tagJoins = await dbContext.AdhocTaskTags
            .Where(tt => taskIds.Contains(tt.AdhocTaskId) && tt.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        var involvedTagIds = tagJoins.Select(tt => tt.AdhocTagId).Distinct().ToList();
        var tagNamesById = involvedTagIds.Count == 0
            ? new Dictionary<int, string>()
            : await dbContext.AdhocTags
                .Where(t => involvedTagIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name);
        var tagNamesByTask = tagJoins
            .GroupBy(tt => tt.AdhocTaskId)
            .ToDictionary(g => g.Key, g => g.Select(tt => tagNamesById.GetValueOrDefault(tt.AdhocTagId, "")).Where(n => n.Length > 0).ToList());

        var propertyIds = candidates.Select(t => t.PropertyId).Distinct().ToList();
        var propertyNamesById = await dbContext.Properties
            .Where(p => propertyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name);

        var areaIds = candidates.Where(t => t.AreaId.HasValue).Select(t => t.AreaId!.Value).Distinct().ToList();
        var areaNamesById = areaIds.Count == 0
            ? new Dictionary<int, string>()
            : await dbContext.AdhocAreas.Where(a => areaIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name);

        // Actor/comment-author display names, resolved from the SDK Site
        // table in one batch - same pattern as ListWorkers.
        var actorWorkerIds = new HashSet<int>();
        foreach (var task in candidates)
        {
            actorWorkerIds.Add(task.CreatedByWorkerId);
            if (task.CompletedByWorkerId.HasValue)
            {
                actorWorkerIds.Add(task.CompletedByWorkerId.Value);
            }
        }

        foreach (var log in assignmentLogs)
        {
            actorWorkerIds.Add(log.ChangedByWorkerId);
        }

        foreach (var comment in comments)
        {
            actorWorkerIds.Add(comment.AuthorWorkerId);
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var displayNamesById = await sdkDbContext.Sites
            .Where(s => actorWorkerIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        string ActorName(int id) => displayNamesById.GetValueOrDefault(id, "");

        var events = new List<AdhocTaskHistoryEventModel>();

        foreach (var task in candidates)
        {
            var taskComments = commentsByTask.GetValueOrDefault(task.Id, []);
            var lastComment = taskComments.Count > 0 ? taskComments[^1] : null;
            var tagNames = tagNamesByTask.GetValueOrDefault(task.Id, []);
            var propertyName = propertyNamesById.GetValueOrDefault(task.PropertyId, "");
            var areaName = task.AreaId.HasValue ? areaNamesById.GetValueOrDefault(task.AreaId.Value) : null;

            AdhocTaskHistoryEventModel NewEvent(string eventType, DateTime occurredAt, string actorName, string? detail) => new()
            {
                TaskId = task.Id,
                TaskTitle = task.Title,
                EventType = eventType,
                OccurredAt = occurredAt,
                ActorName = actorName,
                Detail = detail,
                PropertyName = propertyName,
                AreaName = areaName,
                TagNames = tagNames,
                LastCommentText = lastComment?.Text,
                LastCommentAuthor = lastComment != null ? ActorName(lastComment.AuthorWorkerId) : null,
                LastCommentAt = lastComment?.CreatedAt,
                Completed = task.Completed,
                Archived = task.Archived,
            };

            events.Add(NewEvent("created", task.CreatedAt, ActorName(task.CreatedByWorkerId), null));

            foreach (var log in assignmentLogs.Where(l => l.AdhocTaskId == task.Id))
            {
                events.Add(NewEvent("assigned", log.CreatedAt, ActorName(log.ChangedByWorkerId), null));
            }

            // "reopened" is not derivable from scalar state alone (Reopen
            // clears CompletedAt/ArchivedAt) and is intentionally not emitted
            // here - see the interface/model doc comments for why this is an
            // accepted v1 limitation, not an oversight.
            if (task.Completed && task.CompletedAt.HasValue)
            {
                var completedByName = task.CompletedByWorkerId.HasValue ? ActorName(task.CompletedByWorkerId.Value) : "";
                events.Add(NewEvent("completed", task.CompletedAt.Value, completedByName, null));
            }

            if (task.Archived && task.ArchivedAt.HasValue)
            {
                // Archive is creator-only (RequireCreator) - the archiver is
                // always the creator, there is no separate ArchivedByWorkerId.
                events.Add(NewEvent("archived", task.ArchivedAt.Value, ActorName(task.CreatedByWorkerId), null));
            }

            foreach (var comment in taskComments)
            {
                events.Add(NewEvent("commented", comment.CreatedAt, ActorName(comment.AuthorWorkerId), comment.Text));
            }
        }

        IEnumerable<AdhocTaskHistoryEventModel> filtered = events;
        if (filters.DateFrom.HasValue)
        {
            filtered = filtered.Where(e => e.OccurredAt >= filters.DateFrom.Value);
        }

        if (filters.DateTo.HasValue)
        {
            filtered = filtered.Where(e => e.OccurredAt <= filters.DateTo.Value);
        }

        var sorted = filtered.OrderByDescending(e => e.OccurredAt).ToList();

        var pageNumber = filters.PageNumber < 1 ? 1 : filters.PageNumber;
        var pageSize = filters.PageSize < 1 ? 25 : filters.PageSize;
        var page = sorted.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

        return new Paged<AdhocTaskHistoryEventModel> { Total = sorted.Count, Entities = page };
    }

    // --- Visibility predicates (mirror TaskVisibility.canSee/matchesScope exactly) ---

    /// <summary>
    /// Mirrors <c>TaskVisibility.canSee</c> exactly: property access is an
    /// unconditional gate checked BEFORE the creator/everyone/assigned
    /// checks - even the creator is excluded if they no longer have
    /// property access. <paramref name="hasPropertyAccess"/> is resolved by
    /// the caller (bulk via <see cref="ListTasks"/>'s accessible-property
    /// set, or a single <see cref="IBackendConfigurationUserPropertyAccess.HasAccessAsync"/>
    /// check for the single-task paths) rather than looked up in here, so
    /// this predicate stays synchronous and directly testable.
    /// </summary>
    private static bool CanSee(AdhocTaskEntity task, int workerId, bool isAdmin, bool hasPropertyAccess, List<int> assignedWorkerIds)
    {
        if (isAdmin)
        {
            return true;
        }

        if (!hasPropertyAccess)
        {
            return false;
        }

        if (task.CreatedByWorkerId == workerId)
        {
            return true;
        }

        if (task.ExecutionRule == (int)AdhocExecutionRuleValue.Everyone)
        {
            return true;
        }

        return assignedWorkerIds.Contains(workerId);
    }

    private static bool MatchesScope(AdhocTaskEntity task, TaskScopeFilter scope, int workerId, List<int> assignedWorkerIds)
    {
        // All = the caller's full canSee set with no scope narrowing at all
        // (regardless of completed/archived state) - this is what the
        // mobile client requests, filtering scope locally afterwards.
        if (scope == TaskScopeFilter.All)
        {
            return true;
        }

        // CreatedByMe (dashboard-only "Oprettet af mig" tab) is likewise a
        // creator filter independent of completed/archived state.
        if (scope == TaskScopeFilter.CreatedByMe)
        {
            return task.CreatedByWorkerId == workerId;
        }

        if (task.Completed || task.Archived)
        {
            return scope == TaskScopeFilter.Completed;
        }

        var assignedToMe = assignedWorkerIds.Contains(workerId);
        return scope switch
        {
            TaskScopeFilter.Mine => assignedToMe,
            TaskScopeFilter.Everyone => task.ExecutionRule == (int)AdhocExecutionRuleValue.Everyone && !assignedToMe,
            TaskScopeFilter.Completed => false,
            _ => false,
        };
    }

    // --- Authorization helpers ---

    private static void RequireCreator(AdhocTaskEntity task, int workerId, bool isAdmin, string action)
    {
        if (isAdmin)
        {
            return;
        }

        // Worker id 0 is the REST dashboard's synthetic caller identity
        // (AdhocController.DashboardWorkerId), and dashboard-created tasks
        // are themselves stamped CreatedByWorkerId = 0 - so a plain equality
        // check would let EVERY authenticated non-admin web user pass the
        // creator gate on every dashboard-created task. Pseudo-identity 0
        // owns nothing: only an admin (already returned above) may act on
        // worker-0-created tasks. gRPC callers always resolve to real,
        // positive SDK site ids and are unaffected by this guard.
        if (workerId == 0 || task.CreatedByWorkerId != workerId)
        {
            throw new AdhocTaskUnauthorizedException(
                $"Worker {workerId} may not {action} adhoc task {task.Id}: not the creator.");
        }
    }

    private async Task<AdhocTaskEntity> LoadTaskOrThrowAsync(int taskId)
    {
        var task = await dbContext.AdhocTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.WorkflowState != Constants.WorkflowStates.Removed);
        if (task == null)
        {
            throw new AdhocTaskNotFoundException(taskId);
        }

        return task;
    }

    private async Task ValidateAreaBelongsToPropertyAsync(int? areaId, int propertyId)
    {
        if (areaId == null)
        {
            return;
        }

        var belongs = await dbContext.AdhocAreas.AnyAsync(a =>
            a.Id == areaId.Value
            && a.PropertyId == propertyId
            && a.WorkflowState != Constants.WorkflowStates.Removed);

        if (!belongs)
        {
            throw new ArgumentException(
                $"Area {areaId.Value} does not belong to property {propertyId}.");
        }
    }

    // --- Assignment delta + log ---

    private async Task<List<int>> ReconcileAssignmentsAsync(int taskId, int changedByWorkerId, List<int> newWorkerIds)
    {
        var newSet = newWorkerIds.Distinct().ToList();

        var existingAssignments = await dbContext.AdhocTaskAssignments
            .Where(a => a.AdhocTaskId == taskId && a.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        var currentSet = existingAssignments.Select(a => a.WorkerId).ToList();

        var toRemove = existingAssignments.Where(a => !newSet.Contains(a.WorkerId)).ToList();
        var toAdd = newSet.Where(id => !currentSet.Contains(id)).ToList();

        foreach (var assignment in toRemove)
        {
            await assignment.Delete(dbContext);
        }

        foreach (var workerId in toAdd)
        {
            var assignment = new AdhocTaskAssignment { AdhocTaskId = taskId, WorkerId = workerId };
            await assignment.Create(dbContext);
        }

        if (toRemove.Count > 0 || toAdd.Count > 0)
        {
            await AppendAssignmentLogAsync(taskId, changedByWorkerId, currentSet, newSet);
        }

        return newSet;
    }

    private async Task AppendAssignmentLogAsync(int taskId, int changedByWorkerId, List<int> fromWorkerIds, List<int> toWorkerIds)
    {
        var log = new AdhocTaskAssignmentLog
        {
            AdhocTaskId = taskId,
            ChangedByWorkerId = changedByWorkerId,
            FromWorkerIdsJson = JsonSerializer.Serialize(fromWorkerIds.OrderBy(id => id)),
            ToWorkerIdsJson = JsonSerializer.Serialize(toWorkerIds.OrderBy(id => id)),
        };
        await log.Create(dbContext);
    }

    private async Task ReconcileTagsAsync(int taskId, List<int> newTagIds)
    {
        var newSet = newTagIds.Distinct().ToList();

        var existingJoins = await dbContext.AdhocTaskTags
            .Where(tt => tt.AdhocTaskId == taskId && tt.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        var currentSet = existingJoins.Select(tt => tt.AdhocTagId).ToList();

        var toRemove = existingJoins.Where(tt => !newSet.Contains(tt.AdhocTagId)).ToList();
        var toAdd = newSet.Where(id => !currentSet.Contains(id)).ToList();

        foreach (var join in toRemove)
        {
            await join.Delete(dbContext);
        }

        foreach (var tagId in toAdd)
        {
            var join = new AdhocTaskTag { AdhocTaskId = taskId, AdhocTagId = tagId };
            await join.Create(dbContext);
        }
    }

    private async Task ReconcilePhotosAsync(int taskId, List<int> keepPhotoIds)
    {
        var keepSet = keepPhotoIds.Distinct().ToList();

        var existingPhotos = await dbContext.AdhocTaskPhotos
            .Where(p => p.AdhocTaskId == taskId && p.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        var toRemove = existingPhotos.Where(p => !keepSet.Contains(p.Id)).ToList();
        foreach (var photo in toRemove)
        {
            await photo.Delete(dbContext);
        }
    }

    // --- Loading / mapping ---

    private async Task<List<int>> LoadAssignedWorkerIdsAsync(int taskId)
    {
        return await dbContext.AdhocTaskAssignments
            .Where(a => a.AdhocTaskId == taskId && a.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(a => a.WorkerId)
            .ToListAsync();
    }

    private async Task<Dictionary<int, List<int>>> LoadAssignedWorkerIdsAsync(List<int> taskIds)
    {
        var rows = await dbContext.AdhocTaskAssignments
            .Where(a => taskIds.Contains(a.AdhocTaskId) && a.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(a => new { a.AdhocTaskId, a.WorkerId })
            .ToListAsync();

        return rows
            .GroupBy(r => r.AdhocTaskId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.WorkerId).ToList());
    }

    private async Task<AdhocTaskModel> MapToModelAsync(AdhocTaskEntity task, List<int> assignedWorkerIds)
    {
        var tagIds = await dbContext.AdhocTaskTags
            .Where(tt => tt.AdhocTaskId == task.Id && tt.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(tt => tt.AdhocTagId)
            .ToListAsync();

        var photos = await dbContext.AdhocTaskPhotos
            .Where(p => p.AdhocTaskId == task.Id && p.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(p => new AdhocTaskPhotoModel { Id = p.Id, ContentType = p.ContentType })
            .ToListAsync();

        var assignmentLog = await dbContext.AdhocTaskAssignmentLogs
            .Where(l => l.AdhocTaskId == task.Id && l.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(l => l.CreatedAt)
            .Select(l => new AdhocAssignmentLogModel
            {
                ChangedByWorkerId = l.ChangedByWorkerId,
                ChangedAt = l.CreatedAt,
                FromWorkerIds = DeserializeIds(l.FromWorkerIdsJson),
                ToWorkerIds = DeserializeIds(l.ToWorkerIdsJson),
            })
            .ToListAsync();

        var comments = await dbContext.AdhocTaskComments
            .Where(c => c.AdhocTaskId == task.Id && c.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new AdhocCommentModel
            {
                AuthorWorkerId = c.AuthorWorkerId,
                CreatedAt = c.CreatedAt,
                Text = c.Text,
            })
            .ToListAsync();

        return new AdhocTaskModel
        {
            Id = task.Id,
            CreatedByWorkerId = task.CreatedByWorkerId,
            CreatedAt = task.CreatedAt,
            Title = task.Title,
            Description = task.Description,
            Urgent = task.Urgent,
            PropertyId = task.PropertyId,
            AreaId = task.AreaId,
            TagIds = tagIds,
            Photos = photos,
            VisibleFrom = task.VisibleFrom,
            Deadline = task.Deadline,
            VisibleReminder = task.VisibleReminder,
            DeadlineReminder = task.DeadlineReminder,
            DeadlineReminderRepeat = task.DeadlineReminderRepeat,
            VisibleReminderTimeMinutes = task.VisibleReminderTimeMinutes,
            DeadlineReminderTimeMinutes = task.DeadlineReminderTimeMinutes,
            ExecutionRule = task.ExecutionRule,
            AssignedWorkerIds = assignedWorkerIds,
            AssignmentLog = assignmentLog,
            Completed = task.Completed,
            CompletedByWorkerId = task.CompletedByWorkerId,
            CompletedAt = task.CompletedAt,
            Archived = task.Archived,
            ArchivedAt = task.ArchivedAt,
            Comments = comments,
        };
    }

    private static List<int> DeserializeIds(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<int>>(json) ?? [];
    }

    private enum AdhocExecutionRuleValue
    {
        AssignedOnly = 0,
        Everyone = 1,
    }
}
