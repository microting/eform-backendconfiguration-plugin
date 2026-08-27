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
    Task<OperationResult> Copy(TaskListBatchCopyModel model);
    Task<OperationResult> Delete(TaskListBatchRequestModel model);
}
