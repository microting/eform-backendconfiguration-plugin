using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>
/// The task list's BATCH rail — admin only, as a whole class.
///
/// #1302: the two task-list endpoints a plain `user` may call (inline rename and
/// the Manage-tags orphan purge) live in <see cref="TaskListUserController"/>
/// under the SAME route prefix, not here with a method-level [Authorize]: ASP.NET
/// Core COMBINES authorization attributes (every one must pass), so a
/// method-level attribute can only narrow the class-level admin role, never
/// relax it. Keeping the admin role on the class means an endpoint added here
/// later is admin-only by default. TaskListControllerAuthorizationTests pins both
/// controllers' action sets.
/// </summary>
[Authorize(Roles = EformRole.Admin)]
[Route("api/backend-configuration-pn/task-list")]
public class TaskListController(
    IBackendConfigurationTaskListService taskListService) : Controller
{
    [HttpPost("assign")]
    public async Task<OperationResult> Assign([FromBody] TaskListBatchAssignModel model)
        => await Validated(model) ?? await taskListService.Assign(model);

    [HttpPost("reassign")]
    public async Task<OperationResult> Reassign([FromBody] TaskListBatchReassignModel model)
        => await Validated(model) ?? await taskListService.Reassign(model);

    [HttpPost("add-worker")]
    public async Task<OperationResult> AddWorker([FromBody] TaskListBatchAssignModel model)
        => await Validated(model) ?? await taskListService.AddWorker(model);

    [HttpPost("change-eform")]
    public async Task<OperationResult> ChangeEform([FromBody] TaskListBatchChangeEformModel model)
        => await Validated(model) ?? await taskListService.ChangeEform(model);

    [HttpPost("add-tags")]
    public async Task<OperationResult> AddTags([FromBody] TaskListBatchTagsModel model)
        => await Validated(model) ?? await taskListService.AddTags(model);

    [HttpPost("remove-tags")]
    public async Task<OperationResult> RemoveTags([FromBody] TaskListBatchTagsModel model)
        => await Validated(model) ?? await taskListService.RemoveTags(model);

    [HttpPost("set-compliance")]
    public async Task<OperationResult> SetCompliance([FromBody] TaskListBatchComplianceModel model)
        => await Validated(model) ?? await taskListService.SetCompliance(model);

    [HttpPost("set-status")]
    public async Task<OperationResult> SetStatus([FromBody] TaskListBatchStatusModel model)
        => await Validated(model) ?? await taskListService.SetStatus(model);

    [HttpPost("change-start-date")]
    public async Task<OperationResult> ChangeStartDate([FromBody] TaskListBatchStartDateModel model)
        => await Validated(model) ?? await taskListService.ChangeStartDate(model);

    /// <summary>
    /// #1122 §5 — read-only projection behind the batch modal's preview panel.
    /// Returns data, so it cannot reuse <see cref="Validated"/> (whose null-vs-
    /// OperationResult idiom does not carry a payload); <see cref="ValidatedData{T}"/>
    /// applies the identical empty-TaskIds rule in the data-result shape.
    /// </summary>
    [HttpPost("change-start-date/preview")]
    public async Task<OperationDataResult<TaskListBatchStartDatePreviewModel>> ChangeStartDatePreview(
        [FromBody] TaskListBatchStartDateModel model)
        => ValidatedData<TaskListBatchStartDatePreviewModel>(model)
           ?? await taskListService.ChangeStartDatePreview(model);

    /// <summary>
    /// #1297 — "Flyt til kalender". Board existence is validated pre-loop in the
    /// service, the board/property match per task.
    /// </summary>
    [HttpPost("move-to-board")]
    public async Task<OperationResult> MoveToBoard([FromBody] TaskListBatchMoveBoardModel model)
        => await Validated(model) ?? await taskListService.MoveToBoard(model);

    /// <summary>
    /// #1298 — "Skift rapportoverskrift". Tag existence is validated pre-loop in
    /// the service, so an unknown tag never produces a partial batch.
    /// </summary>
    [HttpPost("change-report-headline")]
    public async Task<OperationResult> ChangeReportHeadline([FromBody] TaskListBatchReportHeadlineModel model)
        => await Validated(model) ?? await taskListService.ChangeReportHeadline(model);

    [HttpPost("copy")]
    public async Task<OperationResult> Copy([FromBody] TaskListBatchCopyModel model)
        => await Validated(model) ?? await taskListService.Copy(model);

    [HttpPost("delete")]
    public async Task<OperationResult> Delete([FromBody] TaskListBatchRequestModel model)
        => await Validated(model) ?? await taskListService.Delete(model);

    private Task<OperationResult> Validated(TaskListBatchRequestModel model)
        => model == null || model.TaskIds == null || model.TaskIds.Count == 0
            ? Task.FromResult<OperationResult>(new OperationResult(false, "TaskIds must not be empty"))
            : Task.FromResult<OperationResult>(null);

    /// <summary>
    /// <see cref="Validated"/> for endpoints that return a payload. Same rule and
    /// same message, kept adjacent so the two can be changed together.
    /// </summary>
    private static OperationDataResult<T> ValidatedData<T>(TaskListBatchRequestModel model)
        => model == null || model.TaskIds == null || model.TaskIds.Count == 0
            ? new OperationDataResult<T>(false, "TaskIds must not be empty")
            : null;
}
