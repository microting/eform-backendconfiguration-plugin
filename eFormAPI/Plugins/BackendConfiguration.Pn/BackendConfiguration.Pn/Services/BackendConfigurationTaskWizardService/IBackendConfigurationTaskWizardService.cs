using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

namespace BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;

public interface IBackendConfigurationTaskWizardService
{
    Task<OperationDataResult<List<TaskWizardModel>>> Index(TaskWizardRequestModel requestModel);
    Task<OperationDataResult<List<CommonDictionaryModel>>> GetProperties(bool fullNames);
    Task<OperationDataResult<TaskWizardTaskModel>> GetTaskById(int id, bool compliance);
    Task<OperationResult> CreateTask(TaskWizardCreateModel createModel);
    Task<OperationResult> DeactivateList(List<int> ids);
    Task<OperationResult> UpdateTask(TaskWizardCreateModel updateModel);
    Task<OperationResult> DeleteTask(int id);

    /// <summary>
    /// Same DB soft-deletes as <see cref="DeleteTask"/>, but retracts the SDK
    /// cases fire-and-forget so the call returns immediately even when
    /// core.CaseDelete blocks (dev has no eform-core consumer). Used by the
    /// task-list batch delete.
    /// </summary>
    Task<OperationResult> DeleteTaskDeferredRetraction(int id);
}