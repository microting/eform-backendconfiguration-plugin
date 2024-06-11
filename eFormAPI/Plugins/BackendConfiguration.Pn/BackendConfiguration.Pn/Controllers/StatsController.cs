namespace BackendConfiguration.Pn.Controllers;

using Infrastructure.Models.Stats;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Services.BackendConfigurationStatsService;
using System.Threading.Tasks;

[Authorize]
[Route("api/backend-configuration-pn/stats")]
public class StatsController : Controller
{
    private readonly IBackendConfigurationStatsService _statsService;

    public StatsController(IBackendConfigurationStatsService statsService)
    {
        _statsService = statsService;
    }

    [HttpGet]
    [Route("planned-task-days")]
    public async Task<OperationDataResult<PlannedTaskDays>> GetPlannedTaskDays([FromQuery] int? propertyId)
    {
        return await _statsService.GetPlannedTaskDays(propertyId);
    }

    [HttpGet]
    [Route("ad-hoc-task-priorities")]
    public async Task<OperationDataResult<AdHocTaskPriorities>> GetAdHocTaskPriorities([FromQuery] int? propertyId, [FromQuery] int? priority, [FromQuery] int? status)
    {
        return await _statsService.GetAdHocTaskPriorities(propertyId, priority, status);
    }

    [HttpGet]
    [Route("document-updated-days")]
    public async Task<OperationDataResult<DocumentUpdatedDays>> GetDocumentUpdatedDays([FromQuery] int? propertyId)
    {
        return await _statsService.GetDocumentUpdatedDays(propertyId);
    }

    [HttpGet]
    [Route("planned-task-workers")]
    public async Task<OperationDataResult<PlannedTaskWorkers>> GetPlannedTaskWorkers([FromQuery] int? propertyId, [FromQuery] int? siteId)
    {
        return await _statsService.GetPlannedTaskWorkers(propertyId, siteId);
    }

    [HttpGet]
    [Route("ad-hoc-task-workers")]
    public async Task<OperationDataResult<AdHocTaskWorkers>> GetAdHocTaskWorkers([FromQuery] int? propertyId, [FromQuery] int? siteId)
    {
        return await _statsService.GetAdHocTaskWorkers(propertyId, siteId);
    }
}