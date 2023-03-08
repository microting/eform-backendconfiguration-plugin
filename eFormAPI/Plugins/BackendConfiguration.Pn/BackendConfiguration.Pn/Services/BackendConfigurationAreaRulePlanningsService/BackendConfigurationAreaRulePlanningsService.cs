using BackendConfiguration.Pn.Infrastructure.Helpers;

namespace BackendConfiguration.Pn.Services.BackendConfigurationAreaRulePlanningsService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using BackendConfigurationLocalizationService;
    using Infrastructure.Models.AreaRules;
    using Microsoft.EntityFrameworkCore;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.ItemsPlanningBase.Infrastructure.Data;

    public class BackendConfigurationAreaRulePlanningsService  : IBackendConfigurationAreaRulePlanningsService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
        private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;

        public BackendConfigurationAreaRulePlanningsService(
            IEFormCoreService coreHelper,
            IUserService userService,
            BackendConfigurationPnDbContext backendConfigurationPnDbContext,
            IBackendConfigurationLocalizationService backendConfigurationLocalizationService,
            ItemsPlanningPnDbContext itemsPlanningPnDbContext)
        {
            _coreHelper = coreHelper;
            _userService = userService;
            _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
            _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
            _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
        }

        public async Task<OperationResult> UpdatePlanning(AreaRulePlanningModel areaRulePlanningModel)
        {
            var core = await _coreHelper.GetCore();

            var result = await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(areaRulePlanningModel, core, _userService.UserId, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext).ConfigureAwait(false);

            return new OperationResult(result.Success, _backendConfigurationLocalizationService.GetString(result.Message));
        }

        public async Task<OperationDataResult<AreaRulePlanningModel>> GetPlanningByRuleId(int ruleId)
        {
            try
            {
                var areaRule = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == ruleId)
                    .Include(x => x.AreaRulesPlannings)
                    .FirstOrDefaultAsync().ConfigureAwait(false);

                if (areaRule == null)
                {
                    return new OperationDataResult<AreaRulePlanningModel>(false,
                        _backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning"));
                }

                if (!areaRule.AreaRulesPlannings.Any())
                {
                    return new OperationDataResult<AreaRulePlanningModel>(true); // it's okay
                }

                var areaRuleId = areaRule.AreaRulesPlannings
                    .OrderByDescending(x => x.ItemPlanningId)
                    .First(y => y.WorkflowState != Constants.WorkflowStates.Removed).Id;
                var areaRulePlanning = await _backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaRuleId)
                    .Include(x => x.PlanningSites)
                    .Select(x => new AreaRulePlanningModel
                    {
                        Id = x.Id,
                        RuleId = ruleId,
                        StartDate = (DateTime)x.StartDate,
                        ServerStatus = x.Status,
                        TypeSpecificFields = new AreaRuleTypePlanningModel
                        {
                            DayOfWeek = x.DayOfWeek,
                            EndDate = x.EndDate,
                            RepeatEvery = x.RepeatType == 1 && x.RepeatEvery == 0 ? 1 : x.RepeatEvery,
                            RepeatType = x.RepeatType,
                            Alarm = x.Alarm,
                            Type = x.Type,
                            HoursAndEnergyEnabled = x.HoursAndEnergyEnabled,
                            DayOfMonth = x.DayOfMonth
                        },
                        SendNotifications = x.SendNotifications,
                        AssignedSites = x.PlanningSites
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            // .Where(y => y.Status > 0)
                            .Select(y => new AreaRuleAssignedSitesModel { SiteId = y.SiteId, Checked = true, Status = y.Status, PlanningSiteId = y.Id})
                            .ToList(),
                        ComplianceEnabled = x.ComplianceEnabled,
                        PropertyId = x.PropertyId
                    })
                    .FirstOrDefaultAsync().ConfigureAwait(false);

                if (areaRulePlanning == null)
                {
                    return new OperationDataResult<AreaRulePlanningModel>(false,
                        _backendConfigurationLocalizationService.GetString("PlanningNotFound"));
                }

                return new OperationDataResult<AreaRulePlanningModel>(true, areaRulePlanning);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRulePlanningModel>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning")}: {e.Message}");
            }
        }

        public async Task<OperationDataResult<Paged<TaskWorkerModel>>> GetPlanningsBySiteId(int siteId, FilterAndSortModel filterAndSortModel)
        {
            try
            {
                var language = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
                var listTaskWorker = new List<TaskWorkerModel>();

                var propertyIds = await _backendConfigurationPnDbContext.PropertyWorkers
                    .Where(x =>
                        x.WorkerId == siteId
                        && x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.PropertyId).ToListAsync().ConfigureAwait(false);

                var sitePlannings = await _backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Status)
                    .Where(x => propertyIds.Contains(x.PropertyId))
                    .Where(x => x.PlanningSites.Where(y => y.WorkflowState != Constants.WorkflowStates.Removed && y.SiteId == siteId).Select(y => y.SiteId).Any())
                    .Include(x => x.AreaRule.AreaRuleTranslations)
                    .Select(x => new
                    {
                        x.Id,
                        x.ItemPlanningId,
                        x.PropertyId,
                        x.AreaRuleId,
                        x.AreaId,
                        x.AreaRule,
                        x.Status
                    })
                    .ToListAsync().ConfigureAwait(false);
                // var total = sitePlannings.Count;
                foreach (var sitePlanning in sitePlannings)
                {
                    var areaName = await _backendConfigurationPnDbContext.AreaTranslations
                        .Where(x => x.AreaId == sitePlanning.AreaId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync().ConfigureAwait(false);

                    var areaRuleName = await _backendConfigurationPnDbContext.AreaRuleTranslations
                        .Where(x => x.AreaRuleId == sitePlanning.AreaRuleId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync().ConfigureAwait(false);

                    var propertyName = await _backendConfigurationPnDbContext.Properties
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == sitePlanning.PropertyId)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync().ConfigureAwait(false);

                    var itemPlanningName = await _itemsPlanningPnDbContext.PlanningNameTranslation
                        .Where(x => x.PlanningId == sitePlanning.ItemPlanningId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync().ConfigureAwait(false);

                    if (itemPlanningName != null)
                    {
                        listTaskWorker.Add(new TaskWorkerModel
                        {
                            Id = sitePlanning.Id,
                            Path = $"{areaName} - {areaRuleName}",
                            PropertyName = propertyName,
                            ItemName = itemPlanningName,
                            PropertyId = sitePlanning.PropertyId,
                            AreaRule = new AreaRuleNameAndTypeSpecificFields
                            {
                                TranslatedName = sitePlanning.AreaRule.AreaRuleTranslations
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .Where(x => x.LanguageId == language.Id)
                                    .Select(x => x.Name)
                                    .FirstOrDefault(),
                                TypeSpecificFields = new TypeSpecificField
                                {
                                    EformId = sitePlanning.AreaRule.EformId,
                                    Type = sitePlanning.AreaRule.Type,
                                    Alarm = sitePlanning.AreaRule.Alarm,
                                    DayOfWeek = sitePlanning.AreaRule.DayOfWeek,
                                    RepeatEvery = sitePlanning.AreaRule.RepeatEvery
                                }
                            }
                        });
                    }
                }
                if (listTaskWorker.Any())
                {
                    listTaskWorker = QueryHelper.AddSortToQuery(listTaskWorker.AsQueryable(),
                        filterAndSortModel.Sort,
                        filterAndSortModel.IsSortDsc).ToList();
                }

                return new OperationDataResult<Paged<TaskWorkerModel>>(true, new Paged<TaskWorkerModel> { Entities = listTaskWorker/*, Total = total*/});
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<Paged<TaskWorkerModel>>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning")}: {e.Message}");
            }
        }

        public async Task<OperationDataResult<AreaRulePlanningModel>> GetPlanningById(int planningId)
        {
            try
            {
                var areaRulePlanning = await _backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == planningId)
                    .Include(x => x.PlanningSites)
                    .Select(x => new AreaRulePlanningModel
                    {
                        Id = x.Id,
                        RuleId = x.AreaRuleId,
                        StartDate = (DateTime)x.StartDate,
                        ServerStatus = x.Status,
                        TypeSpecificFields = new AreaRuleTypePlanningModel
                        {
                            DayOfWeek = x.DayOfWeek,
                            EndDate = x.EndDate,
                            RepeatEvery = x.RepeatEvery,
                            RepeatType = x.RepeatType,
                            Alarm = x.Alarm,
                            Type = x.Type,
                            HoursAndEnergyEnabled = x.HoursAndEnergyEnabled,
                            DayOfMonth = x.DayOfMonth
                        },
                        SendNotifications = x.SendNotifications,
                        AssignedSites = x.PlanningSites
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(y => new AreaRuleAssignedSitesModel { SiteId = y.SiteId, Checked = true })
                            .ToList()
                    })
                    .FirstOrDefaultAsync().ConfigureAwait(false);

                if (areaRulePlanning == null)
                {
                    return new OperationDataResult<AreaRulePlanningModel>(false,
                        _backendConfigurationLocalizationService.GetString("PlanningNotFound"));
                }

                return new OperationDataResult<AreaRulePlanningModel>(true, areaRulePlanning);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRulePlanningModel>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning")}: {e.Message}");
            }
        }

    }
}