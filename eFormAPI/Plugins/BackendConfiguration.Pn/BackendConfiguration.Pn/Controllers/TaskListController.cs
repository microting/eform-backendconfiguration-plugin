using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Controllers;

[Authorize(Roles = EformRole.Admin)]
[Route("api/backend-configuration-pn/task-list")]
public class TaskListController(IBackendConfigurationTaskListService taskListService) : Controller
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
}
