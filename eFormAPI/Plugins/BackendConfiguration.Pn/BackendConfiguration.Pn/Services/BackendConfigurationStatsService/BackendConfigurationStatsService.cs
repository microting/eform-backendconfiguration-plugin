using System.Collections.Generic;
using BackendConfiguration.Pn.Infrastructure.Enums;
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

#pragma warning disable CS9113 // Parameter is unread
public class BackendConfigurationStatsService(
    BackendConfigurationPnDbContext backendConfigurationPnDbContext,
    ILogger<BackendConfigurationStatsService> logger,
    IBackendConfigurationLocalizationService localizationService,
   // ItemsPlanningPnDbContext _,  // Unused parameter kept for backwards compatibility
    ItemsPlanningPnDbContext itemsPlanningPnDbContext,
    CaseTemplatePnDbContext caseTemplatePnDbContext,
    IEFormCoreService coreHelper)
    : IBackendConfigurationStatsService
#pragma warning restore CS9113 // Parameter is unread
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
    public async Task<OperationDataResult<PlannedTaskWorkers>> GetPlannedTaskWorkers(
    int? propertyId,
    int? siteId,
    List<int> propertyIds,
    List<int> status,
    List<int> tagIds,
    List<int> folderIds,
    List<int> assignToIds)
{
    try
    {
        var core = await coreHelper.GetCore();
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        var query = backendConfigurationPnDbContext.AreaRulePlannings
            .Include(x => x.PlanningSites)
            .Include(x => x.AreaRulePlanningTags)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.AreaRule.CreatedInGuide)
            .Where(x => x.ItemPlanningId != 0)
            .AsNoTracking();
        
        if (propertyId.HasValue)
            query = query.Where(x => x.PropertyId == propertyId.Value);

        if (propertyIds.Any())
            query = query.Where(x => propertyIds.Contains(x.PropertyId));

        if (status.Any())
        {
            var includeActive = status.Contains((int)TaskWizardStatuses.Active);
            var includeNotActive = status.Contains((int)TaskWizardStatuses.NotActive);

            query = query.Where(x =>
                (includeActive && x.Status) ||
                (includeNotActive && !x.Status));
        }

        if (folderIds.Any())
            query = query.Where(x => folderIds.Contains(x.FolderId));

        if (assignToIds.Any())
        {
            query = query.Where(x =>
                x.PlanningSites.Any(ps =>
                    ps.WorkflowState != Constants.WorkflowStates.Removed &&
                    assignToIds.Contains(ps.SiteId)));
        }

        if (tagIds.Any())
        {
            query = query.Where(x =>
                x.AreaRulePlanningTags.Any(t =>
                    t.WorkflowState != Constants.WorkflowStates.Removed &&
                    tagIds.Contains(t.ItemPlanningTagId))
                ||
                (x.ItemPlanningTagId.HasValue &&
                 tagIds.Contains(x.ItemPlanningTagId.Value)));
        }

        var grouped = await query
            .SelectMany(x => x.PlanningSites
                .Where(ps => ps.WorkflowState != Constants.WorkflowStates.Removed))
            .GroupBy(ps => ps.SiteId)
            .Select(g => new
            {
                SiteId = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        var siteIds = grouped.Select(x => x.SiteId).ToList();

        var siteNames = await sdkDbContext.Sites
            .Where(x => siteIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        return new OperationDataResult<PlannedTaskWorkers>(true,
            new PlannedTaskWorkers
            {
                TaskWorkers = grouped.Select(x => new PlannedTaskWorker
                {
                    WorkerId = x.SiteId,
                    WorkerName = siteNames.GetValueOrDefault(x.SiteId),
                    StatValue = x.Count
                }).ToList()
            });
    }
    catch (Exception e)
    {
        SentrySdk.CaptureException(e);
        logger.LogError(e, e.Message);
        return new OperationDataResult<PlannedTaskWorkers>(
            false,
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
    
    public async Task<OperationDataResult<AdHocTaskWorkers>> GetAdHocTaskWorkersByFilters(
        int? siteId,
        List<int> propertyId,
        List<int> areaIds,
        List<int> createdByIds,
        List<int> assignedToIds,
        List<int> statuses,
        List<int> priorities,
        DateTime? dateFrom,
        DateTime? dateTo)
{
    try
    {
        var core = await coreHelper.GetCore();
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        var query = backendConfigurationPnDbContext.WorkorderCases
            .Include(x => x.PropertyWorker)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.LeadingCase)
            .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.Completed)
            .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.NewTask);

        if (propertyId.Any())
            query = query.Where(x => propertyId.Contains(x.PropertyWorker.PropertyId));

        if (statuses.Any())
            query = query.Where(x => statuses.Contains((int)x.CaseStatusesEnum));

        if (priorities.Any())
            query = query.Where(x => priorities.Contains(int.Parse(x.Priority)));

        if (assignedToIds.Any())
        {
            var siteNames = await sdkDbContext.Sites
                .Where(x => assignedToIds.Contains(x.Id))
                .Select(x => x.Name)
                .ToListAsync();

            query = query.Where(x => siteNames.Contains(x.LastAssignedToName));
        }
        
        if (dateFrom.HasValue)
            query = query.Where(x => x.CreatedAt >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(x => x.CreatedAt <= dateTo.Value);

        var grouped = await query
            .GroupBy(x => x.LastAssignedToName)
            .Select(x => new
            {
                WorkerName = x.Key,
                Count = x.Count()
            })
            .ToListAsync();

        var siteIds = await sdkDbContext.Sites
            .Where(x => grouped.Select(g => g.WorkerName).Contains(x.Name))
            .ToDictionaryAsync(x => x.Name, x => x.Id);

        return new OperationDataResult<AdHocTaskWorkers>(true, new AdHocTaskWorkers
        {
            TaskWorkers = grouped.Select(x => new AdHocTaskWorker
            {
                WorkerName = x.WorkerName,
                WorkerId = siteIds.GetValueOrDefault(x.WorkerName),
                StatValue = x.Count
            }).ToList()
        });
    }
    catch (Exception e)
    {
        logger.LogError(e, e.Message);
        SentrySdk.CaptureException(e);
        return new OperationDataResult<AdHocTaskWorkers>(
            false,
            localizationService.GetString("ErrorWhileGetAdHocTaskWorkersStat"));
    }
}

}