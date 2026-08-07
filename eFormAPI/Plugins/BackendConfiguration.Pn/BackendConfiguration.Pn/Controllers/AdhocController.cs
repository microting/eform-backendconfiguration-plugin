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
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
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
/// Caller identity: dashboard users authenticate via the eFormAPI web
/// identity (there is no per-request SDK worker/site to resolve, unlike the
/// mobile gRPC path's <c>GrpcSiteResolver</c>). Every call therefore uses a
/// synthetic <see cref="DashboardWorkerId"/> of 0 plus an <c>isAdmin</c> flag
/// resolved once per request from <see cref="IUserService.IsAdmin"/>. When
/// <c>isAdmin</c> is true the shared service bypasses its property-access/
/// creator/assigned/everyone visibility predicates entirely (an admin sees
/// and mutates every task for the customer); when false, worker id 0 has no
/// <c>PropertyWorker</c> row, so the property-access/visibility predicates
/// deny every read. Note that dashboard-created tasks ARE stamped
/// <c>CreatedByWorkerId = 0</c>, so the creator gate (and the tag-ownership
/// gate) additionally treat pseudo-identity 0 as owning nothing unless the
/// caller is an admin — see <c>RequireCreator</c> and the tag guards in
/// <c>BackendConfigurationAdhocService</c>.
/// </summary>
[Authorize]
[Route("api/backend-configuration-pn/adhoc")]
public class AdhocController : Controller
{
    private const int DashboardWorkerId = 0;

    private readonly IBackendConfigurationAdhocService _adhocService;
    private readonly IBackendConfigurationLocalizationService _localizationService;
    private readonly IUserService _userService;

    public AdhocController(
        IBackendConfigurationAdhocService adhocService,
        IBackendConfigurationLocalizationService localizationService,
        IUserService userService)
    {
        _adhocService = adhocService;
        _localizationService = localizationService;
        _userService = userService;
    }

    private bool IsAdmin => _userService.IsAdmin();

    // -----------------------------------------------------------------
    // Tasks
    // -----------------------------------------------------------------

    [HttpPost]
    [Route("index")]
    public async Task<OperationDataResult<AdhocTaskIndexResultModel>> Index([FromBody] AdhocTaskFiltersModel filters)
    {
        return await ExecuteAsync(
            () => _adhocService.IndexTasks(DashboardWorkerId, IsAdmin, filters ?? new AdhocTaskFiltersModel()),
            "ErrorWhileGettingAdhocTasks");
    }

    [HttpPost]
    [Route("history/index")]
    public async Task<OperationDataResult<Paged<AdhocTaskHistoryEventModel>>> HistoryIndex([FromBody] AdhocHistoryFiltersModel filters)
    {
        return await ExecuteAsync(
            () => _adhocService.ListHistory(DashboardWorkerId, IsAdmin, filters ?? new AdhocHistoryFiltersModel()),
            "ErrorWhileGettingAdhocHistory");
    }

    [HttpPost]
    [Route("{id:int}/copy")]
    public async Task<OperationDataResult<AdhocTaskModel>> CopyTask(int id, [FromBody] AdhocCopyTaskModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.CopyTask(DashboardWorkerId, IsAdmin, id, model?.IncludeComments ?? false),
            "ErrorWhileCopyingAdhocTask");
    }

    [HttpGet]
    [Route("{id:int}")]
    public async Task<OperationDataResult<AdhocTaskModel>> GetTask(int id)
    {
        return await ExecuteAsync(
            () => _adhocService.GetTask(DashboardWorkerId, id, IsAdmin),
            "ErrorWhileGettingAdhocTask");
    }

    [HttpPost]
    public async Task<OperationDataResult<AdhocTaskModel>> CreateTask([FromBody] AdhocTaskCreateModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.CreateTask(DashboardWorkerId, model, IsAdmin),
            "ErrorWhileCreatingAdhocTask");
    }

    [HttpPut]
    [Route("{id:int}")]
    public async Task<OperationDataResult<AdhocTaskModel>> UpdateTask(int id, [FromBody] AdhocTaskCreateModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.UpdateTask(DashboardWorkerId, id, model, IsAdmin),
            "ErrorWhileUpdatingAdhocTask");
    }

    [HttpPost]
    [Route("{id:int}/completed")]
    public async Task<OperationDataResult<AdhocTaskModel>> SetCompleted(int id, [FromBody] AdhocSetCompletedModel model)
    {
        var completed = model?.Completed ?? true;
        return await ExecuteAsync(
            () => _adhocService.SetCompleted(DashboardWorkerId, id, completed, IsAdmin, model?.CompletedByWorkerId),
            "ErrorWhileUpdatingAdhocTask");
    }

    [HttpPost]
    [Route("{id:int}/archive")]
    public async Task<OperationDataResult<AdhocTaskModel>> Archive(int id)
    {
        return await ExecuteAsync(
            () => _adhocService.Archive(DashboardWorkerId, id, IsAdmin),
            "ErrorWhileUpdatingAdhocTask");
    }

    [HttpPost]
    [Route("{id:int}/reopen")]
    public async Task<OperationDataResult<AdhocTaskModel>> Reopen(int id)
    {
        return await ExecuteAsync(
            () => _adhocService.Reopen(DashboardWorkerId, id, IsAdmin),
            "ErrorWhileUpdatingAdhocTask");
    }

    [HttpDelete]
    [Route("{id:int}")]
    public async Task<OperationResult> DeleteTask(int id)
    {
        return await ExecuteAsync(
            () => _adhocService.Delete(DashboardWorkerId, id, IsAdmin),
            "ErrorWhileDeletingAdhocTask");
    }

    [HttpPost]
    [Route("{id:int}/comments")]
    public async Task<OperationDataResult<AdhocTaskModel>> AddComment(int id, [FromBody] AdhocCommentCreateModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.AddComment(DashboardWorkerId, id, model?.Text ?? string.Empty, IsAdmin),
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
            () => _adhocService.ListProperties(DashboardWorkerId, IsAdmin),
            "ErrorWhileGettingAdhocProperties");
    }

    [HttpGet]
    [Route("areas")]
    public async Task<OperationDataResult<List<AdhocAreaModel>>> ListAreas(int propertyId)
    {
        return await ExecuteAsync(
            () => _adhocService.ListAreas(DashboardWorkerId, propertyId, IsAdmin),
            "ErrorWhileGettingAdhocAreas");
    }

    [HttpPost]
    [Route("areas")]
    public async Task<OperationDataResult<List<AdhocAreaModel>>> CreateAreas([FromBody] AdhocAreaCreateModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.CreateAreas(DashboardWorkerId, model?.PropertyId ?? 0, model?.Names ?? [], IsAdmin),
            "ErrorWhileCreatingAdhocAreas");
    }

    [HttpPut]
    [Route("areas/{id:int}")]
    public async Task<OperationDataResult<AdhocAreaModel>> RenameArea(int id, [FromBody] AdhocAreaRenameModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.RenameArea(DashboardWorkerId, id, model?.Name ?? string.Empty, IsAdmin),
            "ErrorWhileUpdatingAdhocArea");
    }

    [HttpDelete]
    [Route("areas/{id:int}")]
    public async Task<OperationResult> DeleteArea(int id)
    {
        return await ExecuteAsync(
            () => _adhocService.DeleteArea(DashboardWorkerId, id, IsAdmin),
            "ErrorWhileDeletingAdhocArea");
    }

    [HttpGet]
    [Route("workers")]
    public async Task<OperationDataResult<List<AdhocWorkerModel>>> ListWorkers(int propertyId)
    {
        return await ExecuteAsync(
            () => _adhocService.ListWorkers(DashboardWorkerId, propertyId, IsAdmin),
            "ErrorWhileGettingAdhocWorkers");
    }

    // -----------------------------------------------------------------
    // Tags
    // -----------------------------------------------------------------

    [HttpGet]
    [Route("tags")]
    public async Task<OperationDataResult<List<AdhocTagModel>>> ListTags()
    {
        // Admins see every non-removed tag customer-wide (global + every
        // worker's personal tags, including mobile-created ones) so they
        // show up in the web Etiketter list; non-admins keep the
        // global-only view the synthetic DashboardWorkerId already implied
        // (worker 0 owns nothing).
        return await ExecuteAsync(
            () => _adhocService.ListTags(DashboardWorkerId, IsAdmin),
            "ErrorWhileGettingAdhocTags");
    }

    [HttpPost]
    [Route("tags")]
    public async Task<OperationDataResult<AdhocTagModel>> CreateTag([FromBody] AdhocTagCreateModel model)
    {
        // A dashboard admin curates shared tags for the customer - REST
        // CreateTag creates a global tag (OwnerWorkerId = null) when the
        // caller is an admin; mobile's CreateTag (gRPC, always isAdmin=false)
        // keeps its existing "owner = caller" semantics unchanged.
        return await ExecuteAsync(
            () => _adhocService.CreateTag(DashboardWorkerId, model?.Name ?? string.Empty, IsAdmin),
            "ErrorWhileCreatingAdhocTag");
    }

    [HttpPut]
    [Route("tags/{id:int}")]
    public async Task<OperationDataResult<AdhocTagModel>> RenameTag(int id, [FromBody] AdhocTagCreateModel model)
    {
        return await ExecuteAsync(
            () => _adhocService.RenameTag(DashboardWorkerId, id, model?.Name ?? string.Empty, IsAdmin),
            "ErrorWhileUpdatingAdhocTag");
    }

    [HttpDelete]
    [Route("tags/{id:int}")]
    public async Task<OperationResult> DeleteTag(int id)
    {
        return await ExecuteAsync(
            () => _adhocService.DeleteTag(DashboardWorkerId, id, IsAdmin),
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
                .SavePhoto(DashboardWorkerId, id, stream.ToArray(), contentType, IsAdmin)
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
                .GetPhoto(DashboardWorkerId, photoId, IsAdmin)
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
