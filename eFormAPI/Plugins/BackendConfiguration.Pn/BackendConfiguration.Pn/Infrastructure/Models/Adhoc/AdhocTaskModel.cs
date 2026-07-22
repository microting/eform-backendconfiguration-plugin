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

namespace BackendConfiguration.Pn.Infrastructure.Models.Adhoc;

using System;
using System.Collections.Generic;

/// <summary>
/// Which slice of the caller's visible-task set <see cref="Services.BackendConfigurationAdhocService.IBackendConfigurationAdhocService.ListTasks"/>
/// returns, mirroring the Dart <c>TaskScope</c> enum plus the dashboard-only
/// <see cref="CreatedByMe"/> value (spec's proto <c>TaskScope</c> also has
/// this fifth value). <see cref="All"/> returns the caller's full
/// `TaskVisibility.canSee` set with no further scope narrowing - this is what
/// the mobile client requests, filtering scope locally afterwards. The other
/// values replicate `TaskVisibility.matchesScope` exactly.
/// </summary>
public enum TaskScopeFilter
{
    All = 0,
    Mine = 1,
    Everyone = 2,
    Completed = 3,
    CreatedByMe = 4,
}

/// <summary>
/// Read model for an ad-hoc task, returned by every <c>IBackendConfigurationAdhocService</c>
/// method that hands back a task. Worker ids are plain SDK site ids (int) at
/// this layer - the gRPC/REST façades (B5/B6) render them as decimal strings
/// on the wire per the plan's worker-identity convention.
/// </summary>
public class AdhocTaskModel
{
    public int Id { get; set; }
    public int CreatedByWorkerId { get; set; }
    public DateTime CreatedAt { get; set; }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Urgent { get; set; }

    public int PropertyId { get; set; }
    public int? AreaId { get; set; }

    public List<int> TagIds { get; set; } = [];
    public List<AdhocTaskPhotoModel> Photos { get; set; } = [];

    public DateTime? VisibleFrom { get; set; }
    public DateTime? Deadline { get; set; }

    public bool VisibleReminder { get; set; }
    public bool DeadlineReminder { get; set; }

    // 0 = none, 1 = weekdays (mirrors AdhocTaskEntity.DeadlineReminderRepeat).
    public int DeadlineReminderRepeat { get; set; }

    // Minutes from midnight.
    public int VisibleReminderTimeMinutes { get; set; } = 480;
    public int DeadlineReminderTimeMinutes { get; set; } = 480;

    // 0 = assignedOnly, 1 = everyone (mirrors AdhocTaskEntity.ExecutionRule).
    public int ExecutionRule { get; set; }

    public List<int> AssignedWorkerIds { get; set; } = [];
    public List<AdhocAssignmentLogModel> AssignmentLog { get; set; } = [];

    public bool Completed { get; set; }
    public int? CompletedByWorkerId { get; set; }
    public DateTime? CompletedAt { get; set; }

    public bool Archived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public List<AdhocCommentModel> Comments { get; set; } = [];
}

/// <summary>
/// A photo attached to a task. <see cref="Id"/> is the owning
/// <c>AdhocTaskPhoto</c> row's PK (what <c>GetPhoto</c>/B4 addresses), not
/// the underlying SDK <c>UploadedData</c> id.
/// </summary>
public class AdhocTaskPhotoModel
{
    public int Id { get; set; }
    public string ContentType { get; set; } = "";
}

/// <summary>
/// The fields a client submits on <c>CreateTask</c>/<c>UpdateTask</c> -
/// everything on <see cref="AdhocTaskModel"/> except the server-stamped
/// fields (id, createdBy, createdAt, completedBy, completedAt, archivedAt,
/// assignmentLog, comments), matching the Dart <c>AdhocTaskDraft</c>
/// exclusion list exactly.
/// </summary>
public class AdhocTaskCreateModel
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Urgent { get; set; }

    public int PropertyId { get; set; }
    public int? AreaId { get; set; }

    public List<int> TagIds { get; set; } = [];

    /// <summary>
    /// Ids of already-uploaded <c>AdhocTaskPhoto</c> rows to keep attached.
    /// On <c>CreateTask</c> this is always effectively empty (a photo can
    /// only be uploaded, via <c>SavePhoto</c>, against an existing task id).
    /// On <c>UpdateTask</c> this is the reconciliation set: any existing
    /// photo row for the task whose id is missing from this list is
    /// soft-deleted; ids not belonging to the task are ignored.
    /// </summary>
    public List<int> PhotoIds { get; set; } = [];

    public DateTime? VisibleFrom { get; set; }
    public DateTime? Deadline { get; set; }

    public bool VisibleReminder { get; set; }
    public bool DeadlineReminder { get; set; }
    public int DeadlineReminderRepeat { get; set; }
    public int VisibleReminderTimeMinutes { get; set; } = 480;
    public int DeadlineReminderTimeMinutes { get; set; } = 480;

    public int ExecutionRule { get; set; }
    public List<int> AssignedWorkerIds { get; set; } = [];
}

public class AdhocCommentModel
{
    public int AuthorWorkerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Text { get; set; } = "";
}

public class AdhocAssignmentLogModel
{
    public int ChangedByWorkerId { get; set; }
    public DateTime ChangedAt { get; set; }
    public List<int> FromWorkerIds { get; set; } = [];
    public List<int> ToWorkerIds { get; set; } = [];
}

/// <summary>
/// <see cref="IsUserTag"/> is caller-relative: true when the tag's
/// <c>OwnerWorkerId</c> equals the worker who asked for it (their own
/// personal tag); false for global tags (null owner).
/// </summary>
public class AdhocTagModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsUserTag { get; set; }
}

public class AdhocAreaModel
{
    public int Id { get; set; }
    public int PropertyId { get; set; }
    public string Name { get; set; } = "";
}

public class AdhocPropertyModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class AdhocWorkerModel
{
    public int WorkerId { get; set; }
    public string DisplayName { get; set; } = "";
    public List<int> PropertyIds { get; set; } = [];
}

/// <summary>
/// Scaffolding for the future (M5) dashboard table query - not consumed by
/// any B2 service method yet; B6's <c>AdhocController</c> index endpoint
/// will build one of these from the request and pass it down once the
/// dashboard-side listing/filtering method exists.
/// </summary>
public class AdhocTaskFiltersModel
{
    public int? PropertyId { get; set; }
    public int? AreaId { get; set; }
    public AdhocTaskStatusFilter? Status { get; set; }
    public List<int> TagIds { get; set; } = [];

    /// <summary>true = task must have ALL of <see cref="TagIds"/>; false = ANY.</summary>
    public bool TagsMatchAll { get; set; }

    public string? SearchText { get; set; }

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? SortColumn { get; set; }
    public bool SortAscending { get; set; } = true;
}

public enum AdhocTaskStatusFilter
{
    Open = 0,
    Completed = 1,
    Archived = 2,
}
