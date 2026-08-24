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

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Infrastructure.Models.Adhoc;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

/// <summary>
/// Shared task CRUD + visibility service for ad-hoc tasks, consumed by both
/// <c>AdhocGrpcService</c> (mobile, B5) and <c>AdhocController</c> (dashboard,
/// B6). Every method takes the caller's <c>workerId</c> (the SDK site id)
/// explicitly rather than resolving it internally, so both façades - and
/// tests - drive authorization identically.
///
/// <paramref name="isAdmin"/> (default false everywhere) is the full-access
/// bypass: a caller passing true skips the property-access/creator/assigned/
/// everyone visibility predicate entirely and sees/mutates every task for the
/// customer. The name is historical - read it as "has full access", not "is
/// an administrator": since 2026-08-24 <c>AdhocController</c> passes the
/// constant <c>DashboardHasFullAccess = true</c> at EVERY call site (paired
/// with the synthetic <c>workerId = 0</c>) rather than consulting a role
/// service, so every authenticated web caller takes the true branch, not just
/// admins. Web reach is bounded instead by the controller's class-level
/// <c>AccessBackendConfigurationPlugin</c> policy.
///
/// Mobile is deliberately unaffected: <c>AdhocGrpcService</c> resolves a real
/// worker and passes <c>isAdmin: false</c> explicitly at all 19 of its call
/// sites, so the false branch - and every predicate below that keys off it -
/// is what keeps the mobile path scoped to the caller's own site.
/// </summary>
public interface IBackendConfigurationAdhocService
{
    Task<List<AdhocTaskModel>> ListTasks(int workerId, TaskScopeFilter scope, int? propertyId, bool isAdmin = false);

    /// <summary>
    /// The dashboard table query (M5/P2) - unlike <see cref="ListTasks"/>
    /// (which only scopes by property and leaves area/status/tag/search/sort/
    /// paging to the caller), this method pushes every one of
    /// <see cref="AdhocTaskFiltersModel"/>'s filters into SQL and only maps
    /// the current page's tasks to the full <see cref="AdhocTaskModel"/>
    /// shape (tags/photos/assignment log/comments), rather than eagerly
    /// hydrating every visible task before filtering - the scale concern
    /// B6's own report flagged about the controller's former in-memory
    /// approach. <paramref name="filters"/>' three status counts
    /// (<see cref="AdhocTaskIndexResultModel.OpenCount"/> etc.) are computed
    /// against every filter except <see cref="AdhocTaskFiltersModel.Status"/>
    /// itself.
    /// </summary>
    Task<AdhocTaskIndexResultModel> IndexTasks(int workerId, bool isAdmin, AdhocTaskFiltersModel filters);

    Task<AdhocTaskModel> GetTask(int workerId, int taskId, bool isAdmin = false);

    /// <summary>
    /// Creates a task on behalf of <paramref name="workerId"/>, stamping them
    /// as <c>CreatedByWorkerId</c>. <paramref name="isAdmin"/> (B6 dashboard
    /// bypass, default false) skips the "caller must have property access to
    /// <c>model.PropertyId</c>" gate — the dashboard has no real SDK worker
    /// identity to check access for, so a web caller creates tasks on the
    /// customer's behalf directly, on any property.
    /// </summary>
    Task<AdhocTaskModel> CreateTask(int workerId, AdhocTaskCreateModel model, bool isAdmin = false);

    Task<AdhocTaskModel> UpdateTask(int workerId, int taskId, AdhocTaskCreateModel model, bool isAdmin = false);

    /// <summary>
    /// <paramref name="completedByWorkerId"/> (M5/P3, dashboard-only) is the
    /// "Vælg hvem der udfører opgaven" performer select on the "Udfør
    /// opgave" modal: when set (only meaningful together with
    /// <paramref name="completed"/> = true), the worker it names - not
    /// <paramref name="workerId"/> - is stamped as <c>CompletedByWorkerId</c>,
    /// after validating that worker has a <c>PropertyWorker</c> row for the
    /// task's property. Throws <see cref="System.ArgumentException"/> if it
    /// doesn't. Mobile callers (gRPC) never pass this.
    /// </summary>
    Task<AdhocTaskModel> SetCompleted(int workerId, int taskId, bool completed, bool isAdmin = false, int? completedByWorkerId = null);

    Task<AdhocTaskModel> Archive(int workerId, int taskId, bool isAdmin = false);

    Task<AdhocTaskModel> Reopen(int workerId, int taskId, bool isAdmin = false);

    Task Delete(int workerId, int taskId, bool isAdmin = false);

    Task<AdhocTaskModel> AddComment(int workerId, int taskId, string text, bool isAdmin = false);

    /// <summary>
    /// Duplicates <paramref name="taskId"/> into a new task created by
    /// <paramref name="workerId"/> (the caller performing the copy, per REST
    /// "duplicate" semantics - not the original creator): same title/
    /// description/urgent/property/area/reminder fields/execution rule, tags,
    /// and assignments (with a fresh assignment-log row, mirroring
    /// <c>CreateTask</c>'s own "log only when non-empty" rule); starts
    /// uncompleted/unarchived regardless of the original's state. Photos are
    /// duplicated as new <c>AdhocTaskPhoto</c> rows referencing the SAME
    /// <c>UploadedDataId</c> as the original (verified safe: <c>AdhocTaskPhoto</c>
    /// has no FK cascade to <c>UploadedData</c>, and <c>GetPhoto</c> doesn't
    /// assert single-owner exclusivity - deleting one task's photo row never
    /// touches the shared bytes or the sibling task's row). Comments are only
    /// copied when <paramref name="includeComments"/> is true, as new rows
    /// preserving <c>AuthorWorkerId</c> and text verbatim; <c>CreatedAt</c>
    /// itself cannot be preserved through the shared <c>PnBase.Create</c>
    /// helper (it always stamps "now") - an accepted, documented v1
    /// limitation rather than a fabricated "copied" annotation on the text.
    /// </summary>
    Task<AdhocTaskModel> CopyTask(int workerId, bool isAdmin, int taskId, bool includeComments);

    /// <summary>
    /// Properties accessible to <paramref name="workerId"/> (via
    /// <c>PropertyWorker</c>), or every non-removed property when
    /// <paramref name="isAdmin"/> is true (B6 dashboard bypass, matching
    /// <see cref="ListTasks"/>'s convention).
    /// </summary>
    Task<List<AdhocPropertyModel>> ListProperties(int workerId, bool isAdmin = false);

    /// <summary>
    /// Areas belonging to <paramref name="propertyId"/>. Mobile only lists;
    /// dashboard mutations live in <c>CreateAreas</c>/<c>RenameArea</c>/<c>DeleteArea</c>
    /// below (area-management spec 2026-07-30).
    /// Throws <see cref="AdhocTaskUnauthorizedException"/> if the caller has
    /// no access to <paramref name="propertyId"/> and <paramref name="isAdmin"/>
    /// is false.
    /// </summary>
    Task<List<AdhocAreaModel>> ListAreas(int workerId, int propertyId, bool isAdmin = false);

    /// <summary>
    /// Batch-creates areas on <paramref name="propertyId"/>: trims each
    /// name, drops empties, and silently skips case-insensitive duplicates
    /// (both within the batch and against the property's active areas), so
    /// re-submitting a list is idempotent. Returns the refreshed active
    /// list. Access mirrors <see cref="ListAreas"/>
    /// (<c>RequirePropertyAccessAsync</c>): full-access callers pass;
    /// everyone else needs a <c>PropertyWorker</c> row. Throws
    /// <see cref="ArgumentException"/> for an unknown property.
    /// </summary>
    Task<List<AdhocAreaModel>> CreateAreas(int workerId, int propertyId, List<string> names, bool isAdmin = false);

    /// <summary>
    /// Renames an active area. Trims; throws <see cref="ArgumentException"/>
    /// on empty or case-insensitively duplicate names (within the area's
    /// property), <see cref="AdhocAreaNotFoundException"/> for unknown or
    /// removed ids. Access mirrors <see cref="ListAreas"/> on the area's
    /// property.
    /// </summary>
    Task<AdhocAreaModel> RenameArea(int workerId, int areaId, string name, bool isAdmin = false);

    /// <summary>
    /// Soft-deletes an area (<c>WorkflowState = Removed</c>). Referencing
    /// tasks keep their <c>AreaId</c> - history is preserved (spec decision
    /// 2). Throws <see cref="AdhocAreaNotFoundException"/> for unknown or
    /// already-removed ids. Access mirrors <see cref="ListAreas"/> on the
    /// area's property.
    /// </summary>
    Task DeleteArea(int workerId, int areaId, bool isAdmin = false);

    /// <summary>
    /// Workers with access to <paramref name="propertyId"/> (via
    /// <c>PropertyWorker</c>), with display names resolved from the SDK
    /// <c>Site</c> table. Throws <see cref="AdhocTaskUnauthorizedException"/>
    /// if the caller has no access to <paramref name="propertyId"/> and
    /// <paramref name="isAdmin"/> is false.
    /// </summary>
    Task<List<AdhocWorkerModel>> ListWorkers(int workerId, int propertyId, bool isAdmin = false);

    /// <summary>
    /// Global tags (<c>OwnerWorkerId == null</c>) plus <paramref name="workerId"/>'s
    /// own personal tags. When <paramref name="isAdmin"/> is true the owner
    /// filter is skipped entirely - the caller sees every non-removed tag
    /// customer-wide, including every worker's personal tags (mobile-created
    /// tags included), so they show up in the web Etiketter list rather than
    /// being silently hidden. Every web caller passes true since 2026-08-24,
    /// so that customer-wide view is what the dashboard always shows.
    /// </summary>
    Task<List<AdhocTagModel>> ListTags(int workerId, bool isAdmin = false);

    /// <summary>
    /// Creates a personal tag owned by <paramref name="workerId"/>, unless
    /// <paramref name="isAdmin"/> is true, in which case it creates a global
    /// tag (<c>OwnerWorkerId = null</c>) owned by nobody and visible to
    /// everyone. Since 2026-08-24 every web caller takes that branch, so all
    /// tags created from the dashboard are shared, customer-wide tags. Mobile
    /// <c>CreateTag</c> semantics (owner = caller) are unchanged - mobile
    /// passes <c>isAdmin: false</c> explicitly
    /// (<c>AdhocGrpcService.CreateTag</c>), as it does at every call site.
    /// Throws <see cref="AdhocTaskUnauthorizedException"/> for the
    /// combination (workerId 0, isAdmin false) - identity 0 owns nothing. No
    /// current caller produces that combination; the guard is what stops the
    /// gRPC path from ever writing an unowned tag.
    /// </summary>
    Task<AdhocTagModel> CreateTag(int workerId, string name, bool isAdmin = false);

    /// <summary>
    /// Renames a tag <paramref name="workerId"/> owns, or - when
    /// <paramref name="isAdmin"/> is true - any tag at all: the global ones
    /// (<c>OwnerWorkerId == null</c>) that no worker owns, and equally
    /// another worker's personal, phone-created tag. Since 2026-08-24 every
    /// web caller has that reach; it is an accepted consequence of the "web
    /// is unrestricted" decision, not an oversight. Throws
    /// <see cref="AdhocTaskUnauthorizedException"/> when
    /// <paramref name="isAdmin"/> is false and the tag is global or owned by
    /// another worker, and for the combination (workerId 0, isAdmin false),
    /// which no current caller produces.
    /// </summary>
    Task<AdhocTagModel> RenameTag(int workerId, int tagId, string name, bool isAdmin = false);

    /// <summary>
    /// Soft-deletes a tag <paramref name="workerId"/> owns - any tag when
    /// <paramref name="isAdmin"/> is true, global ones and another worker's
    /// personal tag alike - plus every <c>AdhocTaskTag</c> join referencing
    /// it, so the tag also disappears from other people's tasks. Since
    /// 2026-08-24 every web caller has that reach (accepted consequence of
    /// the "web is unrestricted" decision). Throws
    /// <see cref="AdhocTaskUnauthorizedException"/> when
    /// <paramref name="isAdmin"/> is false and the tag is global or owned by
    /// another worker, and for the combination (workerId 0, isAdmin false),
    /// which no current caller produces.
    /// </summary>
    Task DeleteTag(int workerId, int tagId, bool isAdmin = false);

    /// <summary>
    /// Persists a photo via <see cref="IAdhocPhotoStorage"/> (the same
    /// <c>core.PutFileToS3Storage</c> pipeline as
    /// <c>EventsGrpcService.UploadPhoto</c>/<c>BackendConfigurationTaskManagementService.CreateTask</c>),
    /// creates the SDK <c>UploadedData</c> row and an <c>AdhocTaskPhoto</c>
    /// row, and returns the new photo's id. Authorization mirrors
    /// <see cref="GetTask"/>'s <c>canSee</c> gate; <paramref name="isAdmin"/>
    /// (B6 dashboard bypass, default false) skips it entirely.
    /// </summary>
    Task<int> SavePhoto(int workerId, int taskId, byte[] bytes, string contentType, bool isAdmin = false);

    /// <summary>
    /// Streams a photo's bytes back via <see cref="IAdhocPhotoStorage"/> (the
    /// same <c>core.GetFileFromS3Storage</c> pipeline). Authorization mirrors
    /// <see cref="GetTask"/>'s <c>canSee</c> gate, applied to the photo's
    /// owning task; <paramref name="isAdmin"/> (B6 dashboard bypass, default
    /// false) skips it entirely.
    /// </summary>
    Task<(Stream Content, string ContentType)> GetPhoto(int workerId, int photoId, bool isAdmin = false);

    /// <summary>
    /// The Historik table (#1095, mockup parity) - one
    /// <see cref="AdhocTaskHistoryRowModel"/> per task currently Completed
    /// ("Løst") or Archived, sorted by CompletedAt descending. Open tasks
    /// never appear. Property/area/tag(AND)/property-access/status filters
    /// are pushed into SQL against the candidate task set (the dominant
    /// scale factor); the per-task row build and the date-truncated,
    /// both-ends-inclusive CompletedAt range filter/sort/paging happen in
    /// memory over that already-bounded candidate set. Assignment logs are
    /// no longer read - assignment has no representation in Historik rows.
    /// </summary>
    Task<Paged<AdhocTaskHistoryRowModel>> ListHistory(int workerId, bool isAdmin, AdhocHistoryFiltersModel filters);
}
