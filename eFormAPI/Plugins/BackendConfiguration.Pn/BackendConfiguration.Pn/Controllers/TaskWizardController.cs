using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationTaskWizardService;
using BackendConfiguration.Pn.Infrastructure.Models.TaskWizard;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

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
    public async Task<OperationDataResult<List<TaskWizardModel>>> Index([FromBody] TaskWizardRequestModel requestModel)
    {
        return await _backendConfigurationTaskWizardService.Index(requestModel);
    }

    [HttpGet]
    [Route("properties")]
    public Task<OperationDataResult<List<CommonDictionaryModel>>> GetCommonDictionary([FromQuery] bool fullNames)
    {
        return _backendConfigurationTaskWizardService.GetProperties(fullNames);
    }

    [HttpGet]
    [Route("{id:int}")]
    public async Task<OperationDataResult<TaskWizardTaskModel>> GetTaskById(int id)
    {
        return await _backendConfigurationTaskWizardService.GetTaskById(id);
    }

    [HttpPost]
    public async Task<OperationResult> CreateTask([FromBody] TaskWizardCreateModel createModel)
    {
        return await _backendConfigurationTaskWizardService.CreateTask(createModel);
    }

    [HttpPut]
    [Route("deactivate")]
    public async Task<OperationResult> DeactivateList([FromBody] List<int> ids)
    {
        return await _backendConfigurationTaskWizardService.DeactivateList(ids);
    }

    [HttpPut]
    public async Task<OperationResult> UpdateTask([FromBody] TaskWizardCreateModel updateModel)
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
