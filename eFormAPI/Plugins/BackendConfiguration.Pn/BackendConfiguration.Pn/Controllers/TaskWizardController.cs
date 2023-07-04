using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;

namespace BackendConfiguration.Pn.Controllers;

[Authorize]
[Route("api/backend-configuration-pn/task-wizard")]
public class TaskWizardController : Controller
{
    private readonly IBackendConfigurationTaskWizardService _backendConfigurationTaskWizardService;

    public TaskWizardController(IBackendConfigurationTaskWizardService backendConfigurationTaskWizardService)
    {
        _backendConfigurationTaskWizardService = backendConfigurationTaskWizardService;
    }

    [HttpPost]
    [Route("index")]
    public async Task<OperationDataResult<object>> Index([FromBody] object requestModel)
    {
        return await _backendConfigurationTaskWizardService.Index(requestModel);
    }

    [HttpGet]
    [Route("{id:int}")]
    public async Task<OperationDataResult<object>> GetTaskById(int id)
    {
        return await _backendConfigurationTaskWizardService.GetTaskById(id);
    }

    [HttpPost]
    public async Task<OperationResult> CreateTask([FromBody] object createModel)
    {
        return await _backendConfigurationTaskWizardService.CreateTask(createModel);
    }

    [HttpPut]
    public async Task<OperationResult> UpdateTask([FromBody] object updateModel)
    {
        return await _backendConfigurationTaskWizardService.UpdateTask(updateModel);
    }

    [HttpDelete]
    [Route("{id:int}")]
    public async Task<OperationResult> DeleteTask(int id)
    {
        return await _backendConfigurationTaskWizardService.DeleteTask(id);
    }
}
