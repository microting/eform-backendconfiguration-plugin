using System.Threading.Tasks;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;

public interface IBackendConfigurationTaskWizardService
{
    Task<OperationDataResult<object>> Index(object requestModel);
    Task<OperationDataResult<object>> GetTaskById(int id);
    Task<OperationDataResult<object>> CreateTask(object updateModel);
    Task<OperationDataResult<object>> UpdateTask(object updateModel);
    Task<OperationDataResult<object>> DeleteTask(int id);
}