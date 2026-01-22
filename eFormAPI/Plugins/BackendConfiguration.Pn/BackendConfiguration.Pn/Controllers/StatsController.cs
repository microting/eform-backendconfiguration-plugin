using System;
using System.Collections.Generic;
using System.Linq;

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
    
    private static List<int> ResolveIds(int? singleId, List<int>? multipleIds)
    {
        if (multipleIds != null && multipleIds.Any())
        {
            return multipleIds;
        }

        if (singleId.HasValue)
        {
            return new List<int> { singleId.Value };
        }

        return new List<int>();
    }


    [HttpGet]
    [Route("planned-task-days")]
    public async Task<OperationDataResult<PlannedTaskDays>> GetPlannedTaskDays(
        [FromQuery] int? propertyId, 
        [FromQuery] List<int>? propertyIds,
        [FromQuery] List<int>? tagIds,
        [FromQuery] List<int>? workerIds)
    {
        var resolvedPropertyIds = ResolveIds(propertyId, propertyIds);

        return await _statsService.GetPlannedTaskDays(
            resolvedPropertyIds,
            tagIds ?? new List<int>(),
            workerIds ?? new List<int>()
        );
    }

    [HttpGet]
    [Route("ad-hoc-task-priorities")]
    public async Task<OperationDataResult<AdHocTaskPriorities>> GetAdHocTaskPriorities(
        [FromQuery] int? propertyId, 
        [FromQuery] int? priority, 
        [FromQuery] int? status,
        [FromQuery] List<int>? propertyIds,
        [FromQuery] List<int>? statuses,
        [FromQuery] int? lastAssignedTo,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo
        )
    {
        return await _statsService.GetAdHocTaskPriorities(
            propertyId, 
            priority, 
            status,
            propertyIds ?? new List<int>(),
            statuses ?? new List<int>(),
            lastAssignedTo,
            dateFrom,
            dateTo
            );
    }

    [HttpGet]
    [Route("document-updated-days")]
    public async Task<OperationDataResult<DocumentUpdatedDays>> GetDocumentUpdatedDays([FromQuery] int? propertyId)
    {
        return await _statsService.GetDocumentUpdatedDays(propertyId);
    }

    [HttpGet]
    [Route("planned-task-workers")]
    public async Task<OperationDataResult<PlannedTaskWorkers>> GetPlannedTaskWorkers(
        [FromQuery] int? propertyId, 
        [FromQuery] int? siteId,
        [FromQuery] List<int>? propertyIds,
        [FromQuery] List<int>? status,
        [FromQuery] List<int>? tagIds,
        [FromQuery] List<int>? folderIds,
        [FromQuery] List<int>? assignToIds
        )
    {
        return await _statsService.GetPlannedTaskWorkers(
            propertyId, 
            siteId, 
            propertyIds ?? [],
            status ?? [],
            tagIds ?? [],
            folderIds ?? [],
            assignToIds ?? []
        );
    }

    [HttpGet]
    [Route("ad-hoc-task-workers")]
    public async Task<OperationDataResult<AdHocTaskWorkers>> GetAdHocTaskWorkers(
        [FromQuery] int? siteId,
        [FromQuery] List<int>? propertyId,
        [FromQuery] List<int>? areaName,
        [FromQuery] List<int>? createdBy,
        [FromQuery] List<int>? lastAssignedTo,
        [FromQuery] List<int>? statuses,
        [FromQuery] List<int>? priority,
        [FromQuery] List<DateTime>? dateFrom,
        [FromQuery] List<DateTime>? dateTo
        
        )
    {
       //return await _statsService.GetAdHocTaskWorkers(propertyId, siteId);
        return await _statsService.GetAdHocTaskWorkersByFilters(
            siteId,
            propertyId, 
            areaName ?? [],
            createdBy ?? [],
            lastAssignedTo ?? [],
            statuses ?? [],
            priority ?? [],
            dateFrom?.FirstOrDefault(),
            dateTo?.FirstOrDefault()
        );
    }
}