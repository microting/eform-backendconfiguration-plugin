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

public class BackendConfigurationStatsService: IBackendConfigurationStatsService
{
    private readonly IBackendConfigurationLocalizationService _localizationService;
    private readonly ILogger<BackendConfigurationStatsService> _logger;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
    private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;
    private readonly CaseTemplatePnDbContext _caseTemplatePnDbContext;
    private readonly IEFormCoreService _coreHelper;

    public BackendConfigurationStatsService(
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ILogger<BackendConfigurationStatsService> logger,
        IBackendConfigurationLocalizationService localizationService,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        CaseTemplatePnDbContext caseTemplatePnDbContext,
        IEFormCoreService coreHelper)
    {
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        _logger = logger;
        _localizationService = localizationService;
        _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
        _caseTemplatePnDbContext = caseTemplatePnDbContext;
        _coreHelper = coreHelper;
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<PlannedTaskDays>> GetPlannedTaskDays(int? propertyId)
    {
        try
        {
            var currentDateTime = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 0, 0, 0);
            var currentEndDateTime = currentDateTime.AddDays(1);
            var query = _backendConfigurationPnDbContext.Compliances
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PlanningId != 0)
                .Where(x => x.StartDate <= currentDateTime)
                .OrderBy(x => x.Deadline)
                //.Where(x => x.Deadline > currentEndDateTime)
                .AsQueryable();
            var result = new PlannedTaskDays();

            if (propertyId.HasValue)
            {
                query = query
                    .Where(x => x.PropertyId == propertyId.Value)
                    .AsQueryable();
            }

            // get exceeded tasks
            result.Exceeded = await query
                .Where(x => x.Deadline < currentEndDateTime)
                .Select(x => x.Id)
                .CountAsync();

            // get today tasks
            result.Today = await query
                .Where(x => x.Deadline > currentDateTime)
                .Where(x => x.Deadline <= currentEndDateTime)
                .Select(x => x.Id)
                .CountAsync();

            // get 1-7
            result.FromFirstToSeventhDays = await query
                .Where(x => x.Deadline > currentDateTime.AddDays(1))
                .Where(x => x.Deadline < currentEndDateTime.AddDays(7))
                .Select(x => x.Id)
                .CountAsync();

            // get 8-30
            result.FromEighthToThirtiethDays = await query
                .Where(x => x.Deadline > currentDateTime.AddDays(8))
                .Where(x => x.Deadline < currentEndDateTime.AddDays(30))
                .Select(x => x.Id)
                .CountAsync();

            // get over 30
            result.OverThirtiethDays = await query
                .Where(x => x.Deadline >= currentEndDateTime.AddDays(30))
                .Select(x => x.Id)
                .CountAsync();

            return new OperationDataResult<PlannedTaskDays>(true, result);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            _logger.LogError(e.Message);
            return new OperationDataResult<PlannedTaskDays>(false,
                _localizationService.GetString("ErrorWhileGetPlannedTaskDaysStat"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<AdHocTaskPriorities>> GetAdHocTaskPriorities(int? propertyId)
    {
        try
        {
            var result = new AdHocTaskPriorities();

            var query = _backendConfigurationPnDbContext.WorkorderCases
                .Include(x => x.PropertyWorker)
                .ThenInclude(x => x.Property)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyWorker.Property.WorkorderEnable)
                .Where(x => x.LeadingCase)
                .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.Completed)
                .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.NewTask);

            if (propertyId.HasValue)
            {
                query = query
                    .Where(x => x.PropertyWorker.PropertyId == propertyId);
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
            Console.WriteLine(e);
            _logger.LogError(e.Message);
            return new OperationDataResult<AdHocTaskPriorities>(false,
                _localizationService.GetString("ErrorWhileGetAdHocTaskPrioritiesStat"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<DocumentUpdatedDays>> GetDocumentUpdatedDays(int? propertyId)
    {
        try
        {
            var currentDateTime = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 0, 0, 0);
            var result = new DocumentUpdatedDays();

            var query = _caseTemplatePnDbContext.Documents
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
            Console.WriteLine(e);
            _logger.LogError(e.Message);
            return new OperationDataResult<DocumentUpdatedDays>(false,
                _localizationService.GetString("ErrorWhileGetDocumentUpdatedDaysStat"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<PlannedTaskWorkers>> GetPlannedTaskWorkers(int? propertyId)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var result = new PlannedTaskWorkers();
            var query = _backendConfigurationPnDbContext.PlanningSites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.AreaRulePlanning)
                // .ThenInclude(x => x.AreaRule)
                // .ThenInclude(x => x.Area)
                // .ThenInclude(x => x.AreaProperties)
                .Where(x => x.AreaRulePlanning.RepeatEvery > 0)
                .Where(x => x.AreaRulePlanning.AreaRule.CreatedInGuide)
                .Where(x => x.AreaRulePlanning.Status && x.AreaRulePlanning.ItemPlanningId != 0)
                .AsNoTracking();

            if (propertyId.HasValue)
            {
                query = query
                    .Where(x => x.AreaRulePlanning.PropertyId == propertyId.Value
                                // ||
                                // x.AreaRulePlanning.AreaRule.PropertyId == propertyId.Value ||
                                // x.AreaRulePlanning.AreaRule.Area.AreaProperties.Select(y => y.PropertyId)
                                    // .Contains(propertyId.Value)
                                );
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
            Console.WriteLine(e);
            _logger.LogError(e.Message);
            return new OperationDataResult<PlannedTaskWorkers>(false,
                _localizationService.GetString("ErrorWhileGetPlannedTaskWorkersStat"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationDataResult<AdHocTaskWorkers>> GetAdHocTaskWorkers(int? propertyId)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var result = new AdHocTaskWorkers();
            var query = _backendConfigurationPnDbContext.WorkorderCases
                .Include(x => x.PropertyWorker)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.LeadingCase == true)
                .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.Completed)
                .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.NewTask);
            // var query = _backendConfigurationPnDbContext.PropertyWorkers
            //     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            //     .Include(x => x.WorkorderCases)
            //     .Include(x => x.Property)
            //     .Where(x => x.Property.WorkorderEnable);
            //
            if (propertyId.HasValue)
            {
                query = query
                    .Where(x => x.PropertyWorker.PropertyId == propertyId.Value);
            }

            var groupedData = await query.GroupBy(x => x.LastAssignedToName)
                .Select(x => new
                {
                    // SiteId = x.Where(y => y.LastAssignedToName == x.Key)
                    //     .Select(y => y.PropertyWorker.WorkerId)
                    //     .FirstOrDefault(),
                    SiteName = x.Key,
                    Count = x.Count(y => y.LastAssignedToName == x.Key)
                }).ToListAsync();

            //
            // var groupedData = await query
            //     .GroupBy(x => x.WorkerId)
            //     .Select(x => new
            //     {
            //         SiteId = x.Key,
            //         Count = x
            //             .SelectMany(y => y.WorkorderCases)
            //             .Count(z => z.PropertyWorker.Property.WorkorderEnable &&
            //                         z.WorkflowState != Constants.WorkflowStates.Removed &&
            //                         z.LeadingCase && z.CaseStatusesEnum != CaseStatusesEnum.Completed)
            //     })
            //     .ToListAsync();
            //
            var names = query
                .Select(x => x.LastAssignedToName)
                .ToList();
            // //
            var siteNames = await sdkDbContext.Sites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => names.Contains(x.Name))
                .ToDictionaryAsync(x => x.Name, x => x.Id);
            //
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

            return new OperationDataResult<AdHocTaskWorkers>(true, result);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            _logger.LogError(e.Message);
            return new OperationDataResult<AdHocTaskWorkers>(false,
                _localizationService.GetString("ErrorWhileGetAdHocTaskWorkersStat"));
        }
    }
}