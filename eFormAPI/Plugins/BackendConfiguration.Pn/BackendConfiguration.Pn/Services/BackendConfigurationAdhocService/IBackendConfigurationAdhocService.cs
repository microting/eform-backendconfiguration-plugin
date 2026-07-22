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
using System.Threading.Tasks;
using Infrastructure.Models.Adhoc;

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

    Task<AdhocTaskModel> GetTask(int workerId, int taskId, bool isAdmin = false);

    Task<AdhocTaskModel> CreateTask(int workerId, AdhocTaskCreateModel model);

    Task<AdhocTaskModel> UpdateTask(int workerId, int taskId, AdhocTaskCreateModel model, bool isAdmin = false);

    Task<AdhocTaskModel> SetCompleted(int workerId, int taskId, bool completed, bool isAdmin = false);

    Task<AdhocTaskModel> Archive(int workerId, int taskId, bool isAdmin = false);

    Task<AdhocTaskModel> Reopen(int workerId, int taskId, bool isAdmin = false);

    Task Delete(int workerId, int taskId, bool isAdmin = false);

    Task<AdhocTaskModel> AddComment(int workerId, int taskId, string text, bool isAdmin = false);

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

    /// <summary>Always creates a personal tag owned by <paramref name="workerId"/>.</summary>
    Task<AdhocTagModel> CreateTag(int workerId, string name);

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
}
