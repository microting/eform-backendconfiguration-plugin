using System.Collections.Generic;
using Sentry;

namespace BackendConfiguration.Pn.Services.BackendConfigurationStatsService;

using BackendConfigurationLocalizationService;
using Microsoft.Extensions.Logging;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Infrastructure.Models.Stats;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormCaseTemplateBase.Infrastructure.Data;
using Microting.eFormApi.BasePn.Abstractions;

public class BackendConfigurationStatsService(
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    ILogger<BackendConfigurationStatsService> logger,
    IBackendConfigurationLocalizationService localizationService,
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    CaseTemplatePnDbContext caseTemplatePnDbContext,
    IEFormCoreService coreHelper)
    : IBackendConfigurationStatsService
{
    /// <inheritdoc />

public async Task<OperationDataResult<PlannedTaskDays>> GetPlannedTaskDays(
    List<int> propertyIds,
    List<int> tagIds,
    List<int> workerIds)
{
    try
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        
        var compliancesQuery = backendConfigurationPnDbContext.Compliances
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.PlanningId != 0);

        if (propertyIds.Any())
        {
            compliancesQuery =
                compliancesQuery.Where(x => propertyIds.Contains(x.PropertyId));
        }

        var compliances = await compliancesQuery
            .Select(x => new
            {
                x.Id,
                x.Deadline,
                x.PlanningId
            })
            .ToListAsync();

        if (!compliances.Any())
        {
            return new OperationDataResult<PlannedTaskDays>(true, new PlannedTaskDays());
        }
        
        var planningIds = compliances
            .Select(x => x.PlanningId)
            .Distinct()
            .ToList();

        var plannings = await itemsPlanningPnDbContext.Plannings
            .Where(x => planningIds.Contains(x.Id))
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Include(x => x.PlanningsTags)
            .ToListAsync();
        
        var planningSites = await itemsPlanningPnDbContext.PlanningSites
            .Where(x => planningIds.Contains(x.PlanningId))
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();
        
        var filteredCompliances = compliances.Where(c =>
        {
            var planning = plannings.FirstOrDefault(p => p.Id == c.PlanningId);
            if (planning == null)
                return false;

            if (tagIds.Any() &&
                !planning.PlanningsTags.Any(t => tagIds.Contains(t.PlanningTagId)))
            {
                return false;
            }
            
            if (workerIds.Any())
            {
                var siteIdsForPlanning = planningSites
                    .Where(s => s.PlanningId == c.PlanningId)
                    .Select(s => s.SiteId)
                    .ToList();

                if (!siteIdsForPlanning.Any(siteId => workerIds.Contains(siteId)))
                {
                    return false;
                }
            }

            return true;
        }).ToList();
        
        var result = new PlannedTaskDays
        {
            Exceeded = filteredCompliances.Count(x =>
                x.Deadline.AddDays(-1) < today),

            Today = filteredCompliances.Count(x =>
                x.Deadline.AddDays(-1) >= today &&
                x.Deadline.AddDays(-1) < tomorrow),

            FromFirstToSeventhDays = filteredCompliances.Count(x =>
                x.Deadline.AddDays(-1) >= tomorrow &&
                x.Deadline.AddDays(-1) < tomorrow.AddDays(7)),

            FromEighthToThirtiethDays = filteredCompliances.Count(x =>
                x.Deadline.AddDays(-1) >= tomorrow.AddDays(7) &&
                x.Deadline.AddDays(-1) < tomorrow.AddDays(30)),

            OverThirtiethDays = filteredCompliances.Count(x =>
                x.Deadline.AddDays(-1) >= tomorrow.AddDays(30))
        };


        return new OperationDataResult<PlannedTaskDays>(true, result);
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        logger.LogError(e, e.Message);
        return new OperationDataResult<PlannedTaskDays>(
            false,
            localizationService.GetString("ErrorWhileGetPlannedTaskDaysStat"));
    }
}



    /// <inheritdoc />
    public async Task<OperationDataResult<AdHocTaskPriorities>> GetAdHocTaskPriorities(int? propertyId, int? priority, int? status)
    {
        try
        {
            var result = new AdHocTaskPriorities();

            var query = backendConfigurationPnDbContext.WorkorderCases
                .Include(x => x.PropertyWorker)
                .ThenInclude(x => x.Property)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyWorker.Property.WorkorderEnable)
                .Where(x => x.LeadingCase);

            if (propertyId.HasValue && propertyId != -1)
            {
                query = query
                    .Where(x => x.PropertyWorker.PropertyId == propertyId);
            }

            if (priority.HasValue && priority != -1)
            {
                query = query
                    .Where(x => x.Priority == priority.ToString());
            }

            if (status.HasValue && status != -1)
            {
                query = query
                    .Where(x => x.CaseStatusesEnum == (CaseStatusesEnum)status);
            }
            else
            {
                if (status == -1)
                {
                    query = query
                        .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.NewTask);
                }
                else
                {
                    query = query
                        .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.Completed)
                        .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.NewTask);
                }
            }

            result.Urgent = await query
                .Where(x => x.Priority == 1.ToString())
                .Select(x => x.Id)
                .CountAsync();

            result.High = await query
                .Where(x => x.Priority == 2.ToString())
                .Select(x => x.Id)
                .CountAsync();

            result.Middle = await query
                .Where(x => x.Priority == 3.ToString())
                .Select(x => x.Id)
                .CountAsync();

            result.Low = await query
                .Where(x => x.Priority == 4.ToString())
                .Select(x => x.Id)
                .CountAsync();

            return new OperationDataResult<AdHocTaskPriorities>(true, result);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e.Message);
            logger.LogTrace(e.StackTrace);
            return new OperationDataResult<AdHocTaskPriorities>(false,
                localizationService.GetString("ErrorWhileGetAdHocTaskPrioritiesStat"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<DocumentUpdatedDays>> GetDocumentUpdatedDays(int? propertyId)
    {
        try
        {
            var currentDateTime = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 0, 0, 0);
            var result = new DocumentUpdatedDays();

            var query = caseTemplatePnDbContext.Documents
                .Include(x => x.DocumentProperties)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            if (propertyId.HasValue)
            {
                query = query
                    .Where(x => x.DocumentProperties.Select(y => y.PropertyId).Contains(propertyId.Value));
            }

            result.ExceededOrToday = await query
                .Where(x => x.EndAt <= currentDateTime)
                .Select(x => x.Id)
                .CountAsync();

            result.UnderThirtiethDays = await query
                .Where(x => x.EndAt <= currentDateTime.AddDays(30) && x.EndAt > currentDateTime)
                .Select(x => x.Id)
                .CountAsync();

            result.OverThirtiethDays = await query
                .Where(x => x.EndAt > currentDateTime.AddDays(30))
                .Select(x => x.Id)
                .CountAsync();

            return new OperationDataResult<DocumentUpdatedDays>(true, result);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e.Message);
            logger.LogTrace(e.StackTrace);
            return new OperationDataResult<DocumentUpdatedDays>(false,
                localizationService.GetString("ErrorWhileGetDocumentUpdatedDaysStat"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<PlannedTaskWorkers>> GetPlannedTaskWorkers(int? propertyId, int? siteId)
    {
        try
        {
            var core = await coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var result = new PlannedTaskWorkers();
            var query = backendConfigurationPnDbContext.PlanningSites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.AreaRulePlanning)
                .Where(x => x.AreaRulePlanning.RepeatEvery > 0)
                .Where(x => x.AreaRulePlanning.AreaRule.CreatedInGuide)
                .Where(x => x.AreaRulePlanning.Status && x.AreaRulePlanning.ItemPlanningId != 0)
                .AsNoTracking();

            if (propertyId.HasValue)
            {
                query = query
                    .Where(x => x.AreaRulePlanning.PropertyId == propertyId.Value
                                );
            }

            if (siteId.HasValue)
            {
                query = query
                    .Where(x => x.SiteId == siteId.Value);
            }

            var groupedData = await query
                .GroupBy(x => x.SiteId)
                .Select(x => new
                {
                    SiteId = x.Key,
                    Count = x.Count()
                })
                .ToListAsync();

            var siteIds = groupedData
                .Select(x => x.SiteId)
                .ToList();

            var siteNames = await sdkDbContext.Sites
                .Where(x => siteIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Name, x => x.Id);

            result.TaskWorkers = groupedData
                .Select(x => new PlannedTaskWorker
                {
                    StatValue = x.Count,
                    WorkerName = siteNames
                        .Where(y => y.Value == x.SiteId)
                        .Select(y => y.Key)
                        .FirstOrDefault(),
                    WorkerId = x.SiteId
                })
                .ToList();

            return new OperationDataResult<PlannedTaskWorkers>(true, result);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e.Message);
            logger.LogTrace(e.StackTrace);
            return new OperationDataResult<PlannedTaskWorkers>(false,
                localizationService.GetString("ErrorWhileGetPlannedTaskWorkersStat"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<AdHocTaskWorkers>> GetAdHocTaskWorkers(int? propertyId, int? siteId)
    {
        try
        {
            var core = await coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var result = new AdHocTaskWorkers();
            var query = backendConfigurationPnDbContext.WorkorderCases
                .Include(x => x.PropertyWorker)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.LeadingCase == true)
                .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.Completed)
                .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.NewTask);
            if (propertyId.HasValue && propertyId != -1)
            {
                query = query
                    .Where(x => x.PropertyWorker.PropertyId == propertyId.Value);
            }

            var groupedData = await query.GroupBy(x => x.LastAssignedToName)
                .Select(x => new
                {
                    SiteName = x.Key,
                    Count = x.Count(y => y.LastAssignedToName == x.Key)
                }).ToListAsync();

            var names = query
                .Select(x => x.LastAssignedToName)
                .ToList();
            var siteNames = await sdkDbContext.Sites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => names.Contains(x.Name))
                .ToDictionaryAsync(x => x.Name, x => x.Id);
            result.TaskWorkers = groupedData
                .Select(x => new AdHocTaskWorker()
                {
                    StatValue = x.Count,
                    WorkerName = x.SiteName,
                    WorkerId = siteNames
                        .Where(y => y.Key == x.SiteName)
                        .Select(y => y.Value)
                        .FirstOrDefault()
                })
                .ToList();

            if (siteId.HasValue)
            {
                result.TaskWorkers = result.TaskWorkers
                    .Where(x => x.WorkerId == siteId.Value)
                    .ToList();
            }

            return new OperationDataResult<AdHocTaskWorkers>(true, result);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            logger.LogError(e.Message);
            logger.LogTrace(e.StackTrace);
            return new OperationDataResult<AdHocTaskWorkers>(false,
                localizationService.GetString("ErrorWhileGetAdHocTaskWorkersStat"));
        }
    }
}