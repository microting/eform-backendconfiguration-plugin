using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.TaskList;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.BackendConfigurationTaskListService;

public interface IBackendConfigurationTaskListService
{
    Task<OperationResult> Assign(TaskListBatchAssignModel model);
    Task<OperationResult> Reassign(TaskListBatchReassignModel model);
    Task<OperationResult> AddWorker(TaskListBatchAssignModel model);
    Task<OperationResult> ChangeEform(TaskListBatchChangeEformModel model);
    Task<OperationResult> AddTags(TaskListBatchTagsModel model);
    Task<OperationResult> RemoveTags(TaskListBatchTagsModel model);
    Task<OperationResult> SetCompliance(TaskListBatchComplianceModel model);

    /// <summary>#1122 — re-anchors every selected series to the given date, past or future.</summary>
    Task<OperationResult> ChangeStartDate(TaskListBatchStartDateModel model);

    /// <summary>
    /// #1122 §5 — what <see cref="ChangeStartDate"/> would do, counted without
    /// writing anything.
    /// </summary>
    Task<OperationDataResult<TaskListBatchStartDatePreviewModel>> ChangeStartDatePreview(
        TaskListBatchStartDateModel model);
    Task<OperationResult> Copy(TaskListBatchCopyModel model);
    Task<OperationResult> Delete(TaskListBatchRequestModel model);
}
