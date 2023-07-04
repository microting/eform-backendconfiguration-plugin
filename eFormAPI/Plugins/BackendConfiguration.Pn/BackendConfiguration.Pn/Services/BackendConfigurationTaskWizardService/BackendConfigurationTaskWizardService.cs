using System.Threading.Tasks;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;

public class BackendConfigurationTaskWizardService : IBackendConfigurationTaskWizardService
{
    /// <inheritdoc />
    public async Task<OperationDataResult<object>> Index(object requestModel)
    {
        throw new System.NotImplementedException();
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<object>> GetTaskById(int id)
    {
        throw new System.NotImplementedException();
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<object>> CreateTask(object updateModel)
    {
        throw new System.NotImplementedException();
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<object>> UpdateTask(object updateModel)
    {
        throw new System.NotImplementedException();
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<object>> DeleteTask(int id)
    {
        throw new System.NotImplementedException();
    }
}