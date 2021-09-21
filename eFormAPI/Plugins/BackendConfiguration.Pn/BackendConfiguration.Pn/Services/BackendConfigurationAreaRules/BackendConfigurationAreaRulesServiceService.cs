/*
The MIT License (MIT)

Copyright (c) 2007 - 2021 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Services.BackendConfigurationAreaRules
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using BackendConfigurationLocalizationService;
    using Infrastructure.Data.Seed.Data;
    using Infrastructure.Models.AreaRules;
    using Microsoft.EntityFrameworkCore;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

    public class BackendConfigurationAreaRulesServiceService : IBackendConfigurationAreaRulesService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;

        public BackendConfigurationAreaRulesServiceService(
            IEFormCoreService coreHelper,
            IUserService userService,
            BackendConfigurationPnDbContext backendConfigurationPnDbContext,
            IBackendConfigurationLocalizationService backendConfigurationLocalizationService)
        {
            _coreHelper = coreHelper;
            _userService = userService;
            _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
            _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        }

        public async Task<OperationDataResult<List<AreaRuleSimpleModel>>> Index(int areaId)
        {
            try
            {
                var currentUserLanguage = await _userService.GetCurrentUserLanguage();
                var areaRules = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.AreaId == areaId)
                    .Include(x => x.AreaRuleTranslations)
                    .Select(x => new AreaRuleSimpleModel
                    {
                        Id = x.Id,
                        EformName = x.EformName,
                        TranslatedName = x.AreaRuleTranslations
                            .Where(y => y.LanguageId == currentUserLanguage.Id)
                            .Select(y =>y.Name)
                            .FirstOrDefault(),
                        IsDefault = BackendConfigurationSeedAreaRules.AreaRulesSeed.Last().Id >= x.Id,
                        TypeSpecificFields = new { x.EformId, x.Type, x.Alarm, x.ChecklistStable, x.TailBite, x.DayOfWeek, },
                    })
                    .ToListAsync();
                return new OperationDataResult<List<AreaRuleSimpleModel>>(true, areaRules);
            }
            catch (Exception ex)
            {
                Log.LogException(ex.Message);
                Log.LogException(ex.StackTrace);
                return new OperationDataResult<List<AreaRuleSimpleModel>>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileObtainingAreaRules"));
            }
        }
        
        public async Task<OperationDataResult<AreaRuleModel>> Read(int ruleId)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var languages = await sdkDbContext.Languages.AsNoTracking().ToListAsync();

                var areaRule = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == ruleId)
                    .Include(x => x.AreaRuleTranslations)
                    .Select(x => new AreaRuleModel
                    {
                        Id = x.Id,
                        EformName = x.EformName,
                        TranslatedNames = x.AreaRuleTranslations
                            .Select(y => new CommonDictionaryModel
                            {
                                Id = y.LanguageId,
                                Name = y.Name,
                                //Description = languages.First(z => z.Id == y.LanguageId).Name,
                            }).ToList(),
                        IsDefault = BackendConfigurationSeedAreaRules.AreaRulesSeed.Last().Id >= x.Id,
                        EformId = (int)x.EformId,
                        TypeSpecificFields = new { x.EformId, x.Type, x.Alarm, x.ChecklistStable, x.TailBite, x.DayOfWeek, },
                    })
                    .FirstOrDefaultAsync();

                if (areaRule == null)
                {
                    return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
                }

                foreach (var areaRuleTranslatedName in areaRule.TranslatedNames)
                {
                    areaRuleTranslatedName.Description =
                        languages.First(z => z.Id == areaRuleTranslatedName.Id).Name;
                }


                return new OperationDataResult<AreaRuleModel>(true, areaRule);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileReadAreaRule"));
            }
        }

        public async Task<OperationResult> Update(AreaRuleUpdateModel updateModel)
        {
            try
            {
                var areaRule = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == updateModel.Id)
                    .Include(x => x.AreaRuleTranslations)
                    .FirstOrDefaultAsync();

                if (areaRule == null)
                {
                    return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
                }

                if (BackendConfigurationSeedAreaRules.AreaRulesSeed.Last().Id >= areaRule.Id)
                {
                    return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("AreaRuleCan'tBeUpdated"));
                }

                areaRule.EformName = updateModel.EformName;
                areaRule.EformId = updateModel.EformId;
                areaRule.UpdatedByUserId = _userService.UserId;
                await areaRule.Update(_backendConfigurationPnDbContext);

                foreach (var updateModelTranslatedName in updateModel.TranslatedNames)
                {
                    if (updateModelTranslatedName.Id is null or 0)
                    {
                        var newTranslate = new AreaRuleTranslation
                        {
                            Name = updateModelTranslatedName.Name,
                            LanguageId = updateModelTranslatedName.LanguageId,
                            AreaRuleId = areaRule.Id,
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                        };
                        await newTranslate.Create(_backendConfigurationPnDbContext);
                    }
                    else
                    {
                        var translateForUpdate = areaRule.AreaRuleTranslations
                            .Where(x => x.Id == updateModelTranslatedName.Id)
                            .Where(x => x.LanguageId == updateModelTranslatedName.LanguageId)
                            .FirstOrDefault(x => x.Name != updateModelTranslatedName.Name);
                        if (translateForUpdate != null)
                        {
                            translateForUpdate.Name = updateModelTranslatedName.Name;
                            await translateForUpdate.Update(_backendConfigurationPnDbContext);
                        }
                    }
                }

                return new OperationDataResult<AreaRuleModel>(true, _backendConfigurationLocalizationService.GetString("SuccessfullyUpdateAreaRule"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileUpdateAreaRule"));
            }
        }

        public async Task<OperationResult> Delete(int areaId)
        {
            try
            {
                var areaRule = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaId)
                    .Include(x => x.AreaRuleTranslations)
                    .FirstOrDefaultAsync();

                if (areaRule == null)
                {
                    return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
                }

                if (BackendConfigurationSeedAreaRules.AreaRulesSeed.Last().Id >= areaRule.Id)
                {
                    return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("AreaRuleCantBeDeleted"));
                }

                foreach (var areaRuleAreaRuleTranslation in areaRule.AreaRuleTranslations)
                {
                    areaRuleAreaRuleTranslation.UpdatedByUserId = _userService.UserId;
                    await areaRuleAreaRuleTranslation.Delete(_backendConfigurationPnDbContext);
                }

                areaRule.UpdatedByUserId = _userService.UserId;
                await areaRule.Delete(_backendConfigurationPnDbContext);
                
                return new OperationDataResult<AreaRuleModel>(true, _backendConfigurationLocalizationService.GetString("SuccessfullyDeletedAreaRule"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileDeleteAreaRule"));
            }
        }

        public async Task<OperationResult> Create(AreaRulesCreateModel createModel)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();

                foreach (var areaRuleCreateModel in createModel.AreaRules)
                {
                    var areaRule = new AreaRules
                    {
                        AreaId = createModel.AreaId,
                        UpdatedByUserId = _userService.UserId,
                        CreatedByUserId = _userService.UserId,
                        Alarm = areaRuleCreateModel.TypeSpecificFields.Alarm,
                        DayOfWeek = areaRuleCreateModel.TypeSpecificFields.DayOfWeek,
                        Type = areaRuleCreateModel.TypeSpecificFields.Type,
                        TailBite = areaRuleCreateModel.TypeSpecificFields.TailBite,
                        ChecklistStable = areaRuleCreateModel.TypeSpecificFields.ChecklistStable,
                        EformId = areaRuleCreateModel.TypeSpecificFields.EformId,
                    };

                    if (areaRuleCreateModel.TypeSpecificFields.EformId.HasValue)
                    {
                        var language = await _userService.GetCurrentUserLanguage();
                        areaRule.EformName = await sdkDbContext.CheckListTranslations
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.CheckListId == areaRuleCreateModel.TypeSpecificFields.EformId)
                            .Where(x => x.LanguageId == language.Id)
                            .Select(x => x.Text)
                            .FirstOrDefaultAsync();
                    }
                    
                    await areaRule.Create(_backendConfigurationPnDbContext);

                    var translations = areaRuleCreateModel.TranslatedNames.Select(x => new AreaRuleTranslation
                    {
                        AreaRuleId = areaRule.Id,
                        LanguageId = (int) x.Id,
                        Name = x.Name,
                        CreatedByUserId = _userService.UserId,
                        UpdatedByUserId = _userService.UserId,
                    }).ToList();

                    foreach (var translation in translations)
                    {
                        await translation.Create(_backendConfigurationPnDbContext);
                    }
                }
                return new OperationDataResult<AreaRuleModel>(true, _backendConfigurationLocalizationService.GetString("SuccessfullyCreateAreaRule"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileCreateAreaRule"));
            }
        }

        public async Task<OperationResult> UpdatePlanning(AreaRulePlanningModel areaRulePlanningModel)
        {
            try
            {
                var areaRulePlanning = new AreaRulesPlanning
                {
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId,
                    EndDate = areaRulePlanningModel.TypeSpecificFields.EndDate,
                    DayOfWeek = areaRulePlanningModel.TypeSpecificFields.DayOfWeek,
                    RepeatEvery = areaRulePlanningModel.TypeSpecificFields.RepeatEvery,
                    RepeatType = areaRulePlanningModel.TypeSpecificFields.RepeatType,
                    StartDate = areaRulePlanningModel.StartDate,
                    Status = areaRulePlanningModel.Status,
                    SendNotifications = areaRulePlanningModel.SendNotifications,
                    Alarm = areaRulePlanningModel.TypeSpecificFields.Alarm,
                    Type = areaRulePlanningModel.TypeSpecificFields.Type,
                };

                await areaRulePlanning.Create(_backendConfigurationPnDbContext);

                var assignedSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSites
                {
                    PlanningId = areaRulePlanning.Id,
                    SiteId = x.SiteId,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId,
                }).ToList();

                foreach (var assignedSite in assignedSites)
                {
                    await assignedSite.Create(_backendConfigurationPnDbContext);
                }

                var areaRule = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaRulePlanningModel.RuleId)
                    .FirstOrDefaultAsync();

                areaRule.UpdatedByUserId = _userService.UserId;
                areaRule.PlanningId = areaRulePlanning.Id;
                await areaRule.Update(_backendConfigurationPnDbContext);

                return new OperationDataResult<AreaRuleModel>(true, _backendConfigurationLocalizationService.GetString("SuccessfullyUpdatePlanning"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileUpdatePlanning"));
            }
        }

        public async Task<OperationDataResult<AreaRulePlanningModel>> GetPlanning(int ruleId)
        {
            try
            {
                var areaRule = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == ruleId)
                    .FirstOrDefaultAsync();

                if (areaRule?.PlanningId == null)
                {
                    return new OperationDataResult<AreaRulePlanningModel>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning"));
                }

                var assignedSites = _backendConfigurationPnDbContext.PlanningSites
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PlanningId == areaRule.PlanningId)
                    .Select(x => new AreaRuleAssignedSitesModel {SiteId = x.SiteId})
                    .ToList();

                var areaRulePlanning = await _backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaRule.PlanningId)
                    .Select(x => new AreaRulePlanningModel
                    {
                        RuleId = ruleId,
                        StartDate = x.StartDate,
                        Status = x.Status,
                        TypeSpecificFields = new AreaRuleTypePlanningModel
                        {
                            DayOfWeek = x.DayOfWeek,
                            EndDate = x.EndDate,
                            RepeatEvery = (int) x.RepeatEvery,
                            RepeatType = (int) x.RepeatType,
                        },
                        SendNotifications = x.SendNotifications,
                        AssignedSites = assignedSites,
                    }).FirstOrDefaultAsync();


                if (areaRulePlanning == null)
                {
                    return new OperationDataResult<AreaRulePlanningModel>(false, _backendConfigurationLocalizationService.GetString("PlanningNotFound"));
                }

                return new OperationDataResult<AreaRulePlanningModel>(true, areaRulePlanning);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRulePlanningModel>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning"));
            }
        }
    }
}
