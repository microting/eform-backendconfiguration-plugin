using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Stats;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.BackendConfigurationStatsService;

public interface IBackendConfigurationStatsService
{
    Task<OperationDataResult<PlannedTaskDays>> GetPlannedTaskDays(int? propertyId);

    Task<OperationDataResult<AdHocTaskPriorities>> GetAdHocTaskPriorities(int? propertyId, int? priority, int? status);

    Task<OperationDataResult<DocumentUpdatedDays>> GetDocumentUpdatedDays(int? propertyId);

    Task<OperationDataResult<PlannedTaskWorkers>> GetPlannedTaskWorkers(int? propertyId, int? siteId);

    Task<OperationDataResult<AdHocTaskWorkers>> GetAdHocTaskWorkers(int? propertyId, int? siteId);
}