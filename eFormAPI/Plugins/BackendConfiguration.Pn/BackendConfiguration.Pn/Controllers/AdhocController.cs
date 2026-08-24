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

using Sentry;

namespace BackendConfiguration.Pn.Controllers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Infrastructure.Models.Adhoc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;
using Services.BackendConfigurationAdhocService;
using Services.BackendConfigurationLocalizationService;

/// <summary>
/// REST façade for ad-hoc tasks (Task B6, extended M5/P2-P3), consumed by
/// the eFormAPI dashboard. Shares <see cref="IBackendConfigurationAdhocService"/>
/// with <c>AdhocGrpcService</c> (mobile, B5) — every route here is a thin
/// try/catch wrapper that forwards to the same service methods.
///
/// <see cref="Index"/>'s filter/sort/paging/status-counts used to be applied
/// in-memory in this controller over <c>ListTasks</c>' full result set (a
/// scale concern flagged in B6's own report); M5/P2 moved that into
/// <see cref="IBackendConfigurationAdhocService.IndexTasks"/>, which pushes
/// the SQL-translatable filters down to the database and only maps the
/// current page's tasks to the full model shape.
///
/// Caller identity and access: dashboard users authenticate via the eFormAPI
/// web identity; there is no per-request SDK worker/site to resolve (unlike
/// the mobile gRPC path's <c>GrpcSiteResolver</c>). Since 2026-08-24 every
/// authenticated web caller gets the behaviour an admin had: the controller
/// passes the constant <see cref="DashboardHasFullAccess"/> at every call
/// site instead of a role check, so the shared service's property-access /
/// creator / assigned / everyone predicates are bypassed for web calls.
///
/// Reach is bounded HERE, server-side: the class-level
/// <c>[Authorize(Policy = BackendConfigurationClaims.AccessBackendConfigurationPlugin)]</c>
/// requires the <c>backend_configuration_plugin_access</c> claim on every
/// route. That policy is registered by the host from the plugin's own
/// <c>PluginPermissions</c> rows (one policy per claim name, see
/// <c>AuthServiceCollectionExtensions.AddEFormAuth</c>), which is the same
/// mechanism <c>TimePlanningSettingsController</c> and
/// <c>InnerResourcesController</c> use. Do NOT rely on the Angular route
/// guard for this: it only hides the page, it does not stop a direct REST
/// call. "Unrestricted" means unrestricted for users OF THIS PLUGIN — a user
/// with no backend-configuration access is still refused. The standard
/// <c>user</c> role is granted "Access BackendConfiguration Plugin" at seed
/// time (<c>EformBackendConfigurationPlugin.SeedDatabase</c>), so a normal
/// plugin user passes. The <c>adhoc_enable</c> claim is retained but enforced
/// nowhere.
///
/// This is deliberately wider than "sees more tasks", and was confirmed as
/// such: <c>isAdmin</c> is not a pure read filter. Web-created tags are
/// written global (<c>OwnerWorkerId = null</c>) and any user may rename or
/// delete another worker's phone-created tag; <c>ListTags</c> surfaces every
/// worker's personal tags; <c>properties</c>/<c>workers</c> return every
/// property and every worker name for the customer; and <c>DELETE</c> removes
/// a task a worker created on their phone. That delete is the Microting soft
/// delete (<c>WorkflowState = Removed</c>, cascading the same way onto the
/// task's assignments, logs, comments and photos): the rows survive and are
/// recoverable in the database, but the task disappears from the mobile
/// client and from every list. Those effects land in data the mobile clients
/// read — what stays scoped is the mobile *caller*, not the data.
///
/// <see cref="DashboardWorkerId"/> (0) survives as a synthetic identity, not
/// as a grant: it is what web-written rows are stamped with
/// (<c>CreatedByWorkerId</c>, comment <c>AuthorWorkerId</c>, assignment-log
/// <c>ChangedByWorkerId</c>), so web-authored records are owned by nobody and
/// "created by" identifies no one. It no longer decides what a web caller may
/// see — the full-access flag does.
///
/// The mobile gRPC path is deliberately different and must stay that way:
/// <c>AdhocGrpcService</c> resolves a real worker via <c>GrpcSiteResolver</c>
/// (rejecting an unresolvable identity with <c>Unauthenticated</c>) and
/// passes <c>isAdmin: false</c> explicitly, so mobile callers stay scoped to
/// their own site's properties/assignments. The constant below is the whole
/// of the web policy; moving it into <c>BackendConfigurationAdhocService</c>
/// (e.g. by widening <c>CanSee</c>) would extend it to every phone.
/// </summary>
[Authorize(Policy = BackendConfigurationClaims.AccessBackendConfigurationPlugin)]
[Route("api/backend-configuration-pn/adhoc")]
public class AdhocController : Controller
{
    private const int DashboardWorkerId = 0;

    // Ad-hoc is unrestricted for dashboard callers (2026-08-24): every
    // authenticated web user has the behaviour an admin has. Passed instead of
    // IUserService.IsAdmin() so the shared service's property-access / creator /
    // assigned predicates are bypassed for web callers.
    //
    // This constant is the whole of that policy. The mobile gRPC path is NOT
    // affected: AdhocGrpcService resolves a real worker via GrpcSiteResolver and
    // passes isAdmin: false. Widening the service's CanSee predicate instead of
    // this flag WOULD hand every phone in the customer every task in that
    // customer's database — the platform is database-per-customer, so the
    // blast radius stops at the customer boundary, but inside it it is total:
    // every property, every worker's tasks, on every phone. Do not move this
    // decision into the service.
    private const bool DashboardHasFullAccess = true;

    private readonly IBackendConfigurationAdhocService _adhocService;
    private readonly IBackendConfigurationLocalizationService _localizationService;

    public AdhocController(
        IBackendConfigurationAdhocService adhocService,
        IBackendConfigurationLocalizationService localizationService)
    {
        _adhocService = adhocService;
        _localizationService = localizationService;
    }

    // -----------------------------------------------------------------
    // Tasks
    // -----------------------------------------------------------------

    [HttpPost]
    [Route("index")]
    public async Task<OperationDataResult<AdhocTaskIndexResultModel>> Index([FromBody] AdhocTaskFiltersModel filters)
    {
        return await ExecuteAsync(
            () => _adhocService.IndexTasks(DashboardWorkerId, DashboardHasFullAccess, filters ?? new AdhocTaskFiltersModel()),
            "ErrorWhileGettingAdhocTasks");
    }

    [HttpPost]
    [Route("history/index")]
    public async Task<OperationDataResult<Paged<AdhocTaskHistoryRowModel>>> HistoryIndex([FromBody] AdhocHistoryFiltersModel filters)
    {
        return await ExecuteAsync(
            () => _adhocService.ListHistory(DashboardWorkerId, DashboardHasFullAccess, filters ?? new AdhocHistoryFiltersModel()),
            "ErrorWhileGettingAdhocHistory");
    }

    [HttpPost]
    [Route("{id:int}/copy")]
    public async Task<OperationDataResult<AdhocTaskModel>> CopyTask(int id, [FromBody] AdhocCopyTaskModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.CopyTask(DashboardWorkerId, DashboardHasFullAccess, id, model?.IncludeComments ?? false),
            "ErrorWhileCopyingAdhocTask");
    }

    [HttpGet]
    [Route("{id:int}")]
    public async Task<OperationDataResult<AdhocTaskModel>> GetTask(int id)
    {
        return await ExecuteAsync(
            () => _adhocService.GetTask(DashboardWorkerId, id, DashboardHasFullAccess),
            "ErrorWhileGettingAdhocTask");
    }

    [HttpPost]
    public async Task<OperationDataResult<AdhocTaskModel>> CreateTask([FromBody] AdhocTaskCreateModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.CreateTask(DashboardWorkerId, model, DashboardHasFullAccess),
            "ErrorWhileCreatingAdhocTask");
    }

    [HttpPut]
    [Route("{id:int}")]
    public async Task<OperationDataResult<AdhocTaskModel>> UpdateTask(int id, [FromBody] AdhocTaskCreateModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.UpdateTask(DashboardWorkerId, id, model, DashboardHasFullAccess),
            "ErrorWhileUpdatingAdhocTask");
    }

    [HttpPost]
    [Route("{id:int}/completed")]
    public async Task<OperationDataResult<AdhocTaskModel>> SetCompleted(int id, [FromBody] AdhocSetCompletedModel model)
    {
        var completed = model?.Completed ?? true;
        return await ExecuteAsync(
            () => _adhocService.SetCompleted(DashboardWorkerId, id, completed, DashboardHasFullAccess, model?.CompletedByWorkerId),
            "ErrorWhileUpdatingAdhocTask");
    }

    [HttpPost]
    [Route("{id:int}/archive")]
    public async Task<OperationDataResult<AdhocTaskModel>> Archive(int id)
    {
        return await ExecuteAsync(
            () => _adhocService.Archive(DashboardWorkerId, id, DashboardHasFullAccess),
            "ErrorWhileUpdatingAdhocTask");
    }

    [HttpPost]
    [Route("{id:int}/reopen")]
    public async Task<OperationDataResult<AdhocTaskModel>> Reopen(int id)
    {
        return await ExecuteAsync(
            () => _adhocService.Reopen(DashboardWorkerId, id, DashboardHasFullAccess),
            "ErrorWhileUpdatingAdhocTask");
    }

    [HttpDelete]
    [Route("{id:int}")]
    public async Task<OperationResult> DeleteTask(int id)
    {
        return await ExecuteAsync(
            () => _adhocService.Delete(DashboardWorkerId, id, DashboardHasFullAccess),
            "ErrorWhileDeletingAdhocTask");
    }

    [HttpPost]
    [Route("{id:int}/comments")]
    public async Task<OperationDataResult<AdhocTaskModel>> AddComment(int id, [FromBody] AdhocCommentCreateModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.AddComment(DashboardWorkerId, id, model?.Text ?? string.Empty, DashboardHasFullAccess),
            "ErrorWhileUpdatingAdhocTask");
    }

    // -----------------------------------------------------------------
    // Reference data
    // -----------------------------------------------------------------

    [HttpGet]
    [Route("properties")]
    public async Task<OperationDataResult<List<AdhocPropertyModel>>> ListProperties()
    {
        return await ExecuteAsync(
            () => _adhocService.ListProperties(DashboardWorkerId, DashboardHasFullAccess),
            "ErrorWhileGettingAdhocProperties");
    }

    [HttpGet]
    [Route("areas")]
    public async Task<OperationDataResult<List<AdhocAreaModel>>> ListAreas(int propertyId)
    {
        return await ExecuteAsync(
            () => _adhocService.ListAreas(DashboardWorkerId, propertyId, DashboardHasFullAccess),
            "ErrorWhileGettingAdhocAreas");
    }

    [HttpPost]
    [Route("areas")]
    public async Task<OperationDataResult<List<AdhocAreaModel>>> CreateAreas([FromBody] AdhocAreaCreateModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.CreateAreas(DashboardWorkerId, model?.PropertyId ?? 0, model?.Names ?? [], DashboardHasFullAccess),
            "ErrorWhileCreatingAdhocAreas");
    }

    [HttpPut]
    [Route("areas/{id:int}")]
    public async Task<OperationDataResult<AdhocAreaModel>> RenameArea(int id, [FromBody] AdhocAreaRenameModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.RenameArea(DashboardWorkerId, id, model?.Name ?? string.Empty, DashboardHasFullAccess),
            "ErrorWhileUpdatingAdhocArea");
    }

    [HttpDelete]
    [Route("areas/{id:int}")]
    public async Task<OperationResult> DeleteArea(int id)
    {
        return await ExecuteAsync(
            () => _adhocService.DeleteArea(DashboardWorkerId, id, DashboardHasFullAccess),
            "ErrorWhileDeletingAdhocArea");
    }

    [HttpGet]
    [Route("workers")]
    public async Task<OperationDataResult<List<AdhocWorkerModel>>> ListWorkers(int propertyId)
    {
        return await ExecuteAsync(
            () => _adhocService.ListWorkers(DashboardWorkerId, propertyId, DashboardHasFullAccess),
            "ErrorWhileGettingAdhocWorkers");
    }

    // -----------------------------------------------------------------
    // Tags
    // -----------------------------------------------------------------

    [HttpGet]
    [Route("tags")]
    public async Task<OperationDataResult<List<AdhocTagModel>>> ListTags()
    {
        // Full access means every non-removed tag customer-wide (global +
        // every worker's personal tags, including mobile-created ones) shows
        // up in the web Etiketter list. Since 2026-08-24 that holds for every
        // web caller, not just admins.
        return await ExecuteAsync(
            () => _adhocService.ListTags(DashboardWorkerId, DashboardHasFullAccess),
            "ErrorWhileGettingAdhocTags");
    }

    [HttpPost]
    [Route("tags")]
    public async Task<OperationDataResult<AdhocTagModel>> CreateTag([FromBody] AdhocTagCreateModel model)
    {
        // The dashboard curates shared tags for the customer - REST CreateTag
        // creates a global tag (OwnerWorkerId = null) because it passes full
        // access; mobile's CreateTag (gRPC, always isAdmin=false) keeps its
        // existing "owner = caller" semantics unchanged.
        return await ExecuteAsync(
            () => _adhocService.CreateTag(DashboardWorkerId, model?.Name ?? string.Empty, DashboardHasFullAccess),
            "ErrorWhileCreatingAdhocTag");
    }

    [HttpPut]
    [Route("tags/{id:int}")]
    public async Task<OperationDataResult<AdhocTagModel>> RenameTag(int id, [FromBody] AdhocTagCreateModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.RenameTag(DashboardWorkerId, id, model?.Name ?? string.Empty, DashboardHasFullAccess),
            "ErrorWhileUpdatingAdhocTag");
    }

    [HttpDelete]
    [Route("tags/{id:int}")]
    public async Task<OperationResult> DeleteTag(int id)
    {
        return await ExecuteAsync(
            () => _adhocService.DeleteTag(DashboardWorkerId, id, DashboardHasFullAccess),
            "ErrorWhileDeletingAdhocTag");
    }

    // -----------------------------------------------------------------
    // Photos
    // -----------------------------------------------------------------

    [HttpPost]
    [Route("{id:int}/photos")]
    public async Task<OperationDataResult<AdhocTaskPhotoModel>> UploadPhoto(int id, IFormFile file)
    {
        return await ExecuteAsync(async () =>
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("A non-empty file must be uploaded.", nameof(file));
            }

            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream).ConfigureAwait(false);

            var contentType = file.ContentType ?? string.Empty;
            var photoId = await _adhocService
                .SavePhoto(DashboardWorkerId, id, stream.ToArray(), contentType, DashboardHasFullAccess)
                .ConfigureAwait(false);

            return new AdhocTaskPhotoModel { Id = photoId, ContentType = contentType };
        }, "ErrorWhileUploadingAdhocPhoto");
    }

    [HttpGet]
    [Route("photos/{photoId:int}")]
    public async Task<IActionResult> GetPhoto(int photoId)
    {
        try
        {
            var (content, contentType) = await _adhocService
                .GetPhoto(DashboardWorkerId, photoId, DashboardHasFullAccess)
                .ConfigureAwait(false);
            return File(content, string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType);
        }
        catch (AdhocTaskPhotoNotFoundException)
        {
            return NotFound();
        }
        catch (AdhocTaskUnauthorizedException)
        {
            return Forbid();
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            return StatusCode(500, $"{_localizationService.GetString("ErrorWhileGettingAdhocPhoto")}: {e.Message}");
        }
    }

    // -----------------------------------------------------------------
    // try/catch -> OperationDataResult/OperationResult, mirroring
    // TaskManagementController's convention.
    // -----------------------------------------------------------------

    private async Task<OperationDataResult<T>> ExecuteAsync<T>(Func<Task<T>> action, string errorKey)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            return new OperationDataResult<T>(true, result);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            return new OperationDataResult<T>(false, $"{_localizationService.GetString(errorKey)}: {e.Message}");
        }
    }

    private async Task<OperationResult> ExecuteAsync(Func<Task> action, string errorKey)
    {
        try
        {
            await action().ConfigureAwait(false);
            return new OperationResult(true);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            return new OperationResult(false, $"{_localizationService.GetString(errorKey)}: {e.Message}");
        }
    }
}
