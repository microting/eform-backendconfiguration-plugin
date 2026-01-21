using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Stats;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.BackendConfigurationStatsService;

public interface IBackendConfigurationStatsService
{
    Task<OperationDataResult<PlannedTaskDays>> GetPlannedTaskDays(
        List<int> propertyIds,
        List<int> tagIds,
        List<int> workerIds
    );

    Task<OperationDataResult<AdHocTaskPriorities>> GetAdHocTaskPriorities(int? propertyId, int? priority, int? status);

    Task<OperationDataResult<DocumentUpdatedDays>> GetDocumentUpdatedDays(int? propertyId);

    Task<OperationDataResult<PlannedTaskWorkers>> GetPlannedTaskWorkers(
        int? propertyId, 
        int? siteId,
        List<int> propertyIds,
        List<int> status,
        List<int> tagIds,
        List<int> folderIds,
        List<int> assignToIds
        
        );

    Task<OperationDataResult<AdHocTaskWorkers>> GetAdHocTaskWorkers(int? propertyId, int? siteId);
    
    Task<OperationDataResult<AdHocTaskWorkers>> GetAdHocTaskWorkersByFilters(
        int? siteId,
        List<int> propertyId,
        List<int> areaIds,
        List<int> createdByIds,
        List<int> assignedToIds,
        List<int> statuses,
        List<int> priorities,
        DateTime? dateFrom,
        DateTime? dateTo
    );
}