using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Services.AreaRulePlanningTagPurgeService;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

[Authorize(Roles = EformRole.Admin)]
[Route("api/backend-configuration-pn/task-list")]
public class TaskListController(
    IBackendConfigurationTaskListService taskListService,
    AreaRulePlanningTagPurgeService tagPurgeService) : Controller
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

    [HttpPost("copy")]
    public async Task<OperationResult> Copy([FromBody] TaskListBatchCopyModel model)
        => await Validated(model) ?? await taskListService.Copy(model);

    [HttpPost("delete")]
    public async Task<OperationResult> Delete([FromBody] TaskListBatchRequestModel model)
        => await Validated(model) ?? await taskListService.Delete(model);

    /// <summary>
    /// Soft-deletes AreaRulePlanningTag rows whose ItemPlanningTagId names a
    /// PlanningTag that has been removed (or never existed). Called by the task-list
    /// page right after the Manage-tags dialog closes, so a tag deleted there stops
    /// being referenced immediately instead of waiting for the next plugin start.
    ///
    /// A dedicated endpoint rather than folding the purge into the task index: the
    /// index is a read path and must not write.
    ///
    /// Takes no body, so <see cref="Validated"/> does not apply — there is no
    /// caller-supplied input to validate. Authorization is the controller-level
    /// [Authorize(Roles = EformRole.Admin)] and nothing more: the call is
    /// parameterless, idempotent, admin-only, and can only remove rows that already
    /// point at a tag the same admin role was able to delete in the first place, so
    /// there is no narrower object to scope a permission to.
    /// </summary>
    [HttpPost("purge-orphan-tags")]
    public async Task<OperationDataResult<int>> PurgeOrphanTags()
        => new OperationDataResult<int>(
            true,
            await tagPurgeService.PurgeOrphanedAreaRulePlanningTagsAsync());

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
