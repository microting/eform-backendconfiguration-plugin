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
/// <paramref name="isAdmin"/> (default false everywhere) is the dashboard's
/// bypass: an admin caller (REST, per B6's "workerId = 0 + isAdmin" caller
/// identity) skips the property-access/creator/assigned/everyone visibility
/// predicate entirely and sees/mutates every task for the customer. Mobile
/// callers (gRPC) always pass false.
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
    /// identity to check access for, so an admin caller creates tasks on the
    /// customer's behalf directly.
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
    /// Areas belonging to <paramref name="propertyId"/>. Areas are
    /// admin-managed - there is no <c>CreateArea</c> here, mobile only lists.
    /// Throws <see cref="AdhocTaskUnauthorizedException"/> if the caller has
    /// no access to <paramref name="propertyId"/> and <paramref name="isAdmin"/>
    /// is false.
    /// </summary>
    Task<List<AdhocAreaModel>> ListAreas(int workerId, int propertyId, bool isAdmin = false);

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
    /// own personal tags.
    /// </summary>
    Task<List<AdhocTagModel>> ListTags(int workerId);

    /// <summary>
    /// Creates a personal tag owned by <paramref name="workerId"/>, unless
    /// <paramref name="isAdmin"/> is true (M5/P3 dashboard-only), in which
    /// case it creates a global tag (<c>OwnerWorkerId = null</c>) - an admin
    /// curating shared tags for the customer. Mobile <c>CreateTag</c>
    /// semantics (owner = caller) are unchanged; mobile never passes
    /// <paramref name="isAdmin"/>.
    /// </summary>
    Task<AdhocTagModel> CreateTag(int workerId, string name, bool isAdmin = false);

    /// <summary>
    /// Renames a tag <paramref name="workerId"/> owns. Throws
    /// <see cref="AdhocTaskUnauthorizedException"/> for global tags or tags
    /// owned by another worker.
    /// </summary>
    Task<AdhocTagModel> RenameTag(int workerId, int tagId, string name);

    /// <summary>
    /// Soft-deletes a tag <paramref name="workerId"/> owns, plus every
    /// <c>AdhocTaskTag</c> join referencing it. Throws
    /// <see cref="AdhocTaskUnauthorizedException"/> for global tags or tags
    /// owned by another worker.
    /// </summary>
    Task DeleteTag(int workerId, int tagId);

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
    /// The Historik timeline (M5/P2) - see <see cref="AdhocTaskHistoryEventModel"/>
    /// for the derivation approach and its accepted v1 limitations.
    /// Property/area/tag(AND)/property-access filters are pushed into SQL
    /// against the candidate task set (the dominant scale factor); the
    /// per-task event explosion (created/assigned/completed/archived/
    /// commented) and the date-range filter/sort/paging over the resulting
    /// events happen in memory over that already-bounded candidate set.
    /// </summary>
    Task<Paged<AdhocTaskHistoryEventModel>> ListHistory(int workerId, bool isAdmin, AdhocHistoryFiltersModel filters);
}
