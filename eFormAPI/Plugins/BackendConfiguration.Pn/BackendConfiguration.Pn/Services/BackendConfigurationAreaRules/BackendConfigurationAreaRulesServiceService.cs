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
    using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
    using Microting.ItemsPlanningBase.Infrastructure.Data;
    using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
    using Microting.ItemsPlanningBase.Infrastructure.Enums;
    using PlanningSite = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.PlanningSite;

    public class BackendConfigurationAreaRulesServiceService : IBackendConfigurationAreaRulesService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
        private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;

        public BackendConfigurationAreaRulesServiceService(
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

        public async Task<OperationDataResult<List<AreaRuleSimpleModel>>> Index(int propertyAreaId)
        {
            try
            {
                var areaId = await _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == propertyAreaId)
                    .Select(x => x.AreaId)
                    .FirstAsync();

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
                            .Select(y => y.Name)
                            .FirstOrDefault(),
                        IsDefault = BackendConfigurationSeedAreas.LastIndexAreaRules >= x.Id,
                        TypeSpecificFields = new { x.EformId, x.Type, x.Alarm, x.ChecklistStable, x.TailBite, x.DayOfWeek, },
                    })
                    .ToListAsync();

                foreach (var areaRule in areaRules)
                {
                    var areaRulePlanning = await _backendConfigurationPnDbContext.AreaRulePlannings
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.AreaRuleId == areaRule.Id)
                        .FirstOrDefaultAsync();
                    areaRule.PlanningStatus = areaRulePlanning != null && areaRulePlanning.ItemPlanningId != 0;
                }

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
                        IsDefault = BackendConfigurationSeedAreas.LastIndexAreaRules >= x.Id,
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
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var areaRule = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == updateModel.Id)
                    .Include(x => x.AreaRuleTranslations)
                    .FirstOrDefaultAsync();

                if (areaRule == null)
                {
                    return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
                }

                if (BackendConfigurationSeedAreas.LastIndexAreaRules >= areaRule.Id)
                {
                    return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("AreaRuleCan'tBeUpdated"));
                }

                if (!string.IsNullOrEmpty(updateModel.EformName))
                {
                    areaRule.EformName = updateModel.EformName;
                    areaRule.EformId = sdkDbContext.CheckListTranslations
                        .Where(x => x.Text == updateModel.EformName)
                        .Select(x => x.CheckListId)
                        .First();
                }
                areaRule.UpdatedByUserId = _userService.UserId;
                if (updateModel.TypeSpecificFields != null)
                {
                    areaRule.Type = updateModel.TypeSpecificFields.Type;
                    areaRule.Alarm = updateModel.TypeSpecificFields.Alarm;
                    areaRule.ChecklistStable = updateModel.TypeSpecificFields.ChecklistStable;
                    areaRule.DayOfWeek = updateModel.TypeSpecificFields.DayOfWeek;
                    areaRule.TailBite = updateModel.TypeSpecificFields.TailBite;
                }
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
                    .Include(x => x.AreaRulesPlannings)
                    .ThenInclude(x => x.PlanningSites)
                    .FirstOrDefaultAsync();

                if (areaRule == null)
                {
                    return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
                }

                if (BackendConfigurationSeedAreas.LastIndexAreaRules >= areaRule.Id)
                {
                    return new OperationDataResult<AreaRuleModel>(false, _backendConfigurationLocalizationService.GetString("AreaRuleCantBeDeleted"));
                }

                foreach (var areaRuleAreaRuleTranslation in areaRule.AreaRuleTranslations)
                {
                    areaRuleAreaRuleTranslation.UpdatedByUserId = _userService.UserId;
                    await areaRuleAreaRuleTranslation.Delete(_backendConfigurationPnDbContext);
                }

                foreach (var areaRulePlanning in areaRule.AreaRulesPlannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    foreach (var planningSite in areaRulePlanning.PlanningSites
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        planningSite.UpdatedByUserId = _userService.UserId;
                        await planningSite.Delete(_backendConfigurationPnDbContext);
                    }
                    if (areaRulePlanning.ItemPlanningId != 0)
                    {
                        var planning = await _itemsPlanningPnDbContext.Plannings
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.Id == areaRulePlanning.ItemPlanningId)
                            .Include(x => x.NameTranslations)
                            .FirstOrDefaultAsync();
                        if (planning != null)
                        {
                            foreach (var translation in planning.NameTranslations
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                            {
                                translation.UpdatedByUserId = _userService.UserId;
                                await translation.Delete(_itemsPlanningPnDbContext);
                            }
                            planning.UpdatedByUserId = _userService.UserId;
                            await planning.Delete(_itemsPlanningPnDbContext);
                        }
                    }

                    areaRulePlanning.UpdatedByUserId = _userService.UserId;
                    await areaRulePlanning.Delete(_backendConfigurationPnDbContext);
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
                var area = await _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == createModel.PropertyAreaId)
                    .Select(x => x.Area)
                    .FirstAsync();

                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();

                foreach (var areaRuleCreateModel in createModel.AreaRules)
                {
                    var eformId = areaRuleCreateModel.TypeSpecificFields.EformId;
                    if (area.Type is AreaTypesEnum.Type2/* or AreaTypesEnum.Type3*/)
                    {
                        var eformName = area.Type switch
                        {
                            AreaTypesEnum.Type2 => "03. Kontrol konstruktion",
                            //AreaTypesEnum.Type3 => "05. Stald_klargøring",
                            _ => ""
                        };
                        eformId = await sdkDbContext.CheckListTranslations
                        .Where(x => x.Text == eformName)
                        .Select(x => x.CheckListId)
                        .FirstAsync();
                    }
                    var areaRule = new AreaRule
                    {
                        AreaId = area.Id,
                        UpdatedByUserId = _userService.UserId,
                        CreatedByUserId = _userService.UserId,
                        Alarm = areaRuleCreateModel.TypeSpecificFields.Alarm,
                        DayOfWeek = areaRuleCreateModel.TypeSpecificFields.DayOfWeek,
                        Type = areaRuleCreateModel.TypeSpecificFields.Type,
                        TailBite = areaRuleCreateModel.TypeSpecificFields.TailBite,
                        ChecklistStable = areaRuleCreateModel.TypeSpecificFields.ChecklistStable,
                        EformId = eformId,
                    };

                    if (eformId != 0)
                    {
                        var language = await _userService.GetCurrentUserLanguage();
                        areaRule.EformName = await sdkDbContext.CheckListTranslations
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.CheckListId == eformId)
                            .Where(x => x.LanguageId == language.Id)
                            .Select(x => x.Text)
                            .FirstOrDefaultAsync();
                        areaRule.FolderId = await _backendConfigurationPnDbContext.ProperyAreaFolders
                            .Include(x => x.AreaProperty)
                            .Where(x => x.AreaProperty.AreaId == areaRule.AreaId)
                            .Select(x => x.FolderId)
                            .FirstOrDefaultAsync();
                        areaRule.FolderName = await sdkDbContext.FolderTranslations
                            .Where(x => x.FolderId == areaRule.FolderId)
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.LanguageId == language.Id)
                            .Select(x => x.Name)
                            .FirstOrDefaultAsync();
                    }

                    await areaRule.Create(_backendConfigurationPnDbContext);

                    var translations = areaRuleCreateModel.TranslatedNames.Select(x => new AreaRuleTranslation
                    {
                        AreaRuleId = areaRule.Id,
                        LanguageId = (int)x.Id,
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
                var core = await _coreHelper.GetCore();
                var sdkDbContex = core.DbContextHelper.GetDbContext();
                areaRulePlanningModel.AssignedSites = areaRulePlanningModel.AssignedSites.Where(x => x.Checked).ToList();

                if (areaRulePlanningModel.Id.HasValue) // update planning
                {
                    var areaRule = await _backendConfigurationPnDbContext.AreaRules
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == areaRulePlanningModel.RuleId)
                        .Include(x => x.AreaRulesPlannings)
                        .ThenInclude(x => x.PlanningSites)
                        .FirstOrDefaultAsync();
                    if (!areaRule.AreaRulesPlannings.Any())
                    {
                        return new OperationDataResult<AreaRulePlanningModel>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning"));
                    }

                    foreach (var rulePlanning in areaRule.AreaRulesPlannings)
                    {
                        rulePlanning.UpdatedByUserId = _userService.UserId;
                        rulePlanning.StartDate = areaRulePlanningModel.StartDate;
                        rulePlanning.Status = areaRulePlanningModel.Status;
                        rulePlanning.SendNotifications = areaRulePlanningModel.SendNotifications;
                        rulePlanning.AreaRuleId = areaRulePlanningModel.RuleId;
                        if (areaRulePlanningModel.TypeSpecificFields != null)
                        {
                            rulePlanning.EndDate = areaRulePlanningModel.TypeSpecificFields?.EndDate;
                            rulePlanning.DayOfWeek = areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                            rulePlanning.RepeatEvery = areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                            rulePlanning.RepeatType = areaRulePlanningModel.TypeSpecificFields.RepeatType;
                        }

                        // update assignments

                        var siteIdsForDelete = rulePlanning.PlanningSites
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(x => x.SiteId)
                            .Where(x => !areaRulePlanningModel.AssignedSites.Select(y => y.SiteId).Contains(x))
                            .ToList();


                        var sitesForCreate = areaRulePlanningModel.AssignedSites
                            .Select(x => x.SiteId)
                            .Where(x => !rulePlanning.PlanningSites.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).Select(y => y.SiteId).Contains(x))
                            .Select(siteId => new PlanningSite
                            {
                                AreaRulePlanningsId = rulePlanning.Id,
                                SiteId = siteId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            })
                            .ToList();

                        foreach (var assignedSite in sitesForCreate)
                        {
                            await assignedSite.Create(_backendConfigurationPnDbContext);
                        }

                        foreach (var siteId in siteIdsForDelete)
                        {
                            await rulePlanning.PlanningSites
                                .First(x => x.SiteId == siteId)
                                .Delete(_backendConfigurationPnDbContext);
                        }

                        await rulePlanning.Update(_backendConfigurationPnDbContext);


                        // update item plannig
                        if (rulePlanning.ItemPlanningId != 0)
                        {
                            var planning = await _itemsPlanningPnDbContext.Plannings
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Where(x => x.Id == rulePlanning.ItemPlanningId)
                                .Include(x => x.PlanningSites)
                                .FirstAsync();
                            planning.Enabled = areaRulePlanningModel.Status;
                            planning.PushMessageOnDeployment = areaRulePlanningModel.SendNotifications;
                            planning.StartDate = areaRulePlanningModel.StartDate;
                            foreach (var planningSite in planning.PlanningSites
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                            {
                                if (siteIdsForDelete.Contains(planningSite.SiteId))
                                {
                                    await planningSite.Delete(_itemsPlanningPnDbContext);
                                }
                            }

                            foreach (var siteId in sitesForCreate.Select(x => x.SiteId))
                            {
                                var planningSite =
                                    new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                                    {
                                        SiteId = siteId,
                                        PlanningId = planning.Id,
                                        CreatedByUserId = _userService.UserId,
                                        UpdatedByUserId = _userService.UserId,
                                    };
                                await planningSite.Create(_itemsPlanningPnDbContext);
                            }
                            await planning.Update(_itemsPlanningPnDbContext);
                        }
                    }
                    return new OperationDataResult<AreaRuleModel>(true, _backendConfigurationLocalizationService.GetString("SuccessfullyUpdatePlanning"));
                }
                else// create planning
                {
                    var areaRule = await _backendConfigurationPnDbContext.AreaRules
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == areaRulePlanningModel.RuleId)
                        .Include(x => x.AreaRuleTranslations)
                        .Include(x => x.Area)
                        .FirstOrDefaultAsync();

                    if (areaRule == null)
                    {
                        return new OperationDataResult<AreaRuleModel>(true, _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
                    }

                    switch (areaRule.Area.Type)
                    {
                        case AreaTypesEnum.Type2:
                            {
                                if (areaRule.Type == AreaRuleT2TypesEnum.Open)
                                {
                                    const string eformName = "03. Kontrol flydelag";
                                    var eformId = await sdkDbContex.CheckListTranslations
                                        .Where(x => x.Text == eformName)
                                        .Select(x => x.CheckListId)
                                        .FirstAsync();
                                    var planningForType2TypeTankOpen = new Planning
                                    {
                                        CreatedByUserId = _userService.UserId,
                                        Enabled = areaRulePlanningModel.Status,
                                        RelatedEFormId = eformId,
                                        RelatedEFormName = eformName,
                                        SdkFolderName = areaRule.FolderName,
                                        SdkFolderId = areaRule.FolderId,
                                        DaysBeforeRedeploymentPushMessageRepeat = false,
                                        DaysBeforeRedeploymentPushMessage = 5,
                                        PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                        StartDate = areaRulePlanningModel.StartDate,
                                        NameTranslations = new List<PlanningNameTranslation>()
                                    {
                                        new()
                                        {
                                            LanguageId = 1, // da
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 1)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + " - Check flydende lag",
                                        },
                                        new()
                                        {
                                            LanguageId = 2, // en
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 2)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + " - Check floating layer",
                                        },
                                        // new PlanningNameTranslation
                                        // {
                                        //     LanguageId = 3, // ge
                                        //     Name = "Schwimmende Ebene prüfen",
                                        // },
                                        // new PlanningNameTranslation
                                        // {
                                        //     LanguageId = 4,// uk-ua
                                        //     Name = "Перевірте плаваючий шар",
                                        // },
                                    },
                                        RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                                        DayOfWeek = DayOfWeek.Monday,
                                        RepeatEvery = 1,
                                        RepeatType = RepeatType.Month,
                                        PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite()
                                        {
                                            SiteId = x.SiteId,
                                        }).ToList(),
                                    };
                                    await planningForType2TypeTankOpen.Create(_itemsPlanningPnDbContext);
                                    var areaRulePlanningForType2TypeTankOpen = new AreaRulePlanning
                                    {
                                        CreatedByUserId = _userService.UserId,
                                        UpdatedByUserId = _userService.UserId,
                                        StartDate = areaRulePlanningModel.StartDate,
                                        Status = areaRulePlanningModel.Status,
                                        SendNotifications = areaRulePlanningModel.SendNotifications,
                                        AreaRuleId = areaRulePlanningModel.RuleId,
                                        Type = AreaRuleT2TypesEnum.Open,
                                        ItemPlanningId = planningForType2TypeTankOpen.Id,
                                    };
                                    await areaRulePlanningForType2TypeTankOpen.Create(_backendConfigurationPnDbContext);

                                    var assignedSitesForType2TypeTankOpen = areaRulePlanningModel.AssignedSites.Select(x =>
                                        new PlanningSite
                                        {
                                            AreaRulePlanningsId = areaRulePlanningForType2TypeTankOpen.Id,
                                            SiteId = x.SiteId,
                                            CreatedByUserId = _userService.UserId,
                                            UpdatedByUserId = _userService.UserId,
                                        }).ToList();

                                    foreach (var assignedSite in assignedSitesForType2TypeTankOpen)
                                    {
                                        await assignedSite.Create(_backendConfigurationPnDbContext);
                                    }
                                }

                                if (areaRule.Type is AreaRuleT2TypesEnum.Open or AreaRuleT2TypesEnum.Closed
                                    && areaRule.Alarm is AreaRuleT2AlarmsEnum.Yes)
                                {
                                    const string eformName = "03. Kontrol alarmanlæg gyllebeholder";
                                    var eformId = await sdkDbContex.CheckListTranslations
                                        .Where(x => x.Text == eformName)
                                        .Select(x => x.CheckListId)
                                        .FirstAsync();
                                    var planningForType2AlarmYes = new Planning
                                    {
                                        CreatedByUserId = _userService.UserId,
                                        Enabled = areaRulePlanningModel.Status,
                                        RelatedEFormId = eformId,
                                        RelatedEFormName = eformName,
                                        SdkFolderName = areaRule.FolderName,
                                        SdkFolderId = areaRule.FolderId,
                                        DaysBeforeRedeploymentPushMessageRepeat = false,
                                        DaysBeforeRedeploymentPushMessage = 5,
                                        PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                        StartDate = areaRulePlanningModel.StartDate,
                                        NameTranslations = new List<PlanningNameTranslation>
                                    {
                                        new()
                                        {
                                            LanguageId = 1, // da
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 1)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + " - Tjek alarm",
                                        },
                                        new()
                                        {
                                            LanguageId = 2, // en
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 2)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + " - Check alarm",
                                        },
                                        // new PlanningNameTranslation
                                        // {
                                        //     LanguageId = 3, // ge
                                        //     Name = "Check alarm",
                                        // },
                                        // new PlanningNameTranslation
                                        // {
                                        //     LanguageId = 4,// uk-ua
                                        //     Name = "Перевірте сигналізацію",
                                        // },
                                    },
                                        RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                                        DayOfWeek = DayOfWeek.Monday,
                                        RepeatEvery = 1,
                                        RepeatType = RepeatType.Month,
                                        PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite()
                                        {
                                            SiteId = x.SiteId,
                                        }).ToList(),
                                    };
                                    await planningForType2AlarmYes.Create(_itemsPlanningPnDbContext);

                                    var areaRulePlanningForType2AlarmYes = new AreaRulePlanning
                                    {
                                        CreatedByUserId = _userService.UserId,
                                        UpdatedByUserId = _userService.UserId,
                                        StartDate = areaRulePlanningModel.StartDate,
                                        Status = areaRulePlanningModel.Status,
                                        SendNotifications = areaRulePlanningModel.SendNotifications,
                                        AreaRuleId = areaRulePlanningModel.RuleId,
                                        Type = (AreaRuleT2TypesEnum)areaRule.Type,
                                        Alarm = AreaRuleT2AlarmsEnum.Yes,
                                        ItemPlanningId = planningForType2AlarmYes.Id,
                                    };
                                    await areaRulePlanningForType2AlarmYes.Create(_backendConfigurationPnDbContext);

                                    var assignedSitesForType2AlarmYes = areaRulePlanningModel.AssignedSites.Select(x =>
                                        new PlanningSite
                                        {
                                            AreaRulePlanningsId = areaRulePlanningForType2AlarmYes.Id,
                                            SiteId = x.SiteId,
                                            CreatedByUserId = _userService.UserId,
                                            UpdatedByUserId = _userService.UserId,
                                        }).ToList();

                                    foreach (var assignedSite in assignedSitesForType2AlarmYes)
                                    {
                                        await assignedSite.Create(_backendConfigurationPnDbContext);
                                    }
                                }

                                var planningForType2 = new Planning
                                {
                                    CreatedByUserId = _userService.UserId,
                                    Enabled = areaRulePlanningModel.Status,
                                    RelatedEFormId = (int)areaRule.EformId,
                                    RelatedEFormName = areaRule.EformName, // must be "03. Kontrol konstruktion"
                                    SdkFolderName = areaRule.FolderName,
                                    SdkFolderId = areaRule.FolderId,
                                    DaysBeforeRedeploymentPushMessageRepeat = false,
                                    DaysBeforeRedeploymentPushMessage = 5,
                                    PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                    StartDate = areaRulePlanningModel.StartDate,
                                    // create translations for planning from translation areaRule
                                    //NameTranslations = areaRule.AreaRuleTranslations.Select(
                                    //    areaRuleAreaRuleTranslation => new PlanningNameTranslation
                                    //    {
                                    //        LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                                    //        Name = areaRuleAreaRuleTranslation.Name + (areaRuleAreaRuleTranslation.LanguageId == 1 ? "Kontrol konstruktion" : "Control construction"),
                                    //    }).ToList(),
                                    NameTranslations = new List<PlanningNameTranslation>()
                                    {
                                        new()
                                        {
                                            LanguageId = 1, // da
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 1)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + " - Kontrol konstruktion",
                                        },
                                        new()
                                        {
                                            LanguageId = 2, // en
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 2)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + " - Check construction",
                                        },
                                        // new PlanningNameTranslation
                                        // {
                                        //     LanguageId = 3, // ge
                                        //     Name = "Konstruktion prüfen",
                                        // },
                                        // new PlanningNameTranslation
                                        // {
                                        //     LanguageId = 4,// uk-ua
                                        //     Name = "Перевірте конструкцію",
                                        // },
                                    },
                                    RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                                    DayOfWeek = DayOfWeek.Monday,
                                    RepeatEvery = 12,
                                    RepeatType = RepeatType.Month,
                                    PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite()
                                    {
                                        SiteId = x.SiteId,
                                    }).ToList(),
                                };

                                await planningForType2.Create(_itemsPlanningPnDbContext);

                                var areaRulePlanningForType2 = new AreaRulePlanning
                                {
                                    CreatedByUserId = _userService.UserId,
                                    UpdatedByUserId = _userService.UserId,
                                    StartDate = areaRulePlanningModel.StartDate,
                                    Status = areaRulePlanningModel.Status,
                                    SendNotifications = areaRulePlanningModel.SendNotifications,
                                    AreaRuleId = areaRulePlanningModel.RuleId,
                                    Type = AreaRuleT2TypesEnum.Open,
                                    ItemPlanningId = planningForType2.Id,
                                };
                                await areaRulePlanningForType2.Create(_backendConfigurationPnDbContext);

                                var assignedSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                                {
                                    AreaRulePlanningsId = areaRulePlanningForType2.Id,
                                    SiteId = x.SiteId,
                                    CreatedByUserId = _userService.UserId,
                                    UpdatedByUserId = _userService.UserId,
                                }).ToList();

                                foreach (var assignedSite in assignedSites)
                                {
                                    await assignedSite.Create(_backendConfigurationPnDbContext);
                                }

                                break;
                            }
                        case AreaTypesEnum.Type3:
                            {
                                if (areaRule.ChecklistStable is true)
                                {
                                    var planningForType3ChecklistStable = new Planning
                                    {
                                        CreatedByUserId = _userService.UserId,
                                        Enabled = areaRulePlanningModel.Status,
                                        RelatedEFormId = (int)areaRule.EformId,
                                        RelatedEFormName = areaRule.EformName,
                                        SdkFolderName = areaRule.FolderName,
                                        SdkFolderId = areaRule.FolderId,
                                        DaysBeforeRedeploymentPushMessageRepeat = false,
                                        DaysBeforeRedeploymentPushMessage = 5,
                                        PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                        StartDate = areaRulePlanningModel.StartDate,
                                        // create translations for planning from translation areaRule
                                        NameTranslations = areaRule.AreaRuleTranslations.Select(
                                            areaRuleAreaRuleTranslation => new PlanningNameTranslation
                                            {
                                                LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                                                Name = areaRuleAreaRuleTranslation.Name,
                                            }).ToList(),
                                        PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite()
                                        {
                                            SiteId = x.SiteId,
                                        }).ToList(),
                                    };
                                    if (areaRulePlanningModel.TypeSpecificFields != null)
                                    {
                                        if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                                        {
                                            planningForType3ChecklistStable.RepeatType =
                                                (RepeatType)areaRulePlanningModel.TypeSpecificFields.RepeatType;
                                        }
                                        if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery is not null)
                                        {
                                            planningForType3ChecklistStable.RepeatEvery =
                                                (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                        }
                                    }
                                    await planningForType3ChecklistStable.Create(_itemsPlanningPnDbContext);
                                    var areaRulePlanningForType3ChecklistStable = new AreaRulePlanning
                                    {
                                        CreatedByUserId = _userService.UserId,
                                        UpdatedByUserId = _userService.UserId,
                                        StartDate = areaRulePlanningModel.StartDate,
                                        Status = areaRulePlanningModel.Status,
                                        SendNotifications = areaRulePlanningModel.SendNotifications,
                                        AreaRuleId = areaRulePlanningModel.RuleId,
                                        ItemPlanningId = planningForType3ChecklistStable.Id,
                                    };

                                    if (areaRulePlanningModel.TypeSpecificFields != null)
                                    {
                                        if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                                        {
                                            areaRulePlanningForType3ChecklistStable.RepeatType = areaRulePlanningModel.TypeSpecificFields.RepeatType;
                                        }
                                        if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery is not null)
                                        {
                                            areaRulePlanningForType3ChecklistStable.RepeatEvery =
                                                (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                        }
                                    }
                                    await areaRulePlanningForType3ChecklistStable.Create(_backendConfigurationPnDbContext);

                                    var assignedSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                                    {
                                        AreaRulePlanningsId = areaRulePlanningForType3ChecklistStable.Id,
                                        SiteId = x.SiteId,
                                        CreatedByUserId = _userService.UserId,
                                        UpdatedByUserId = _userService.UserId,
                                    }).ToList();

                                    foreach (var assignedSite in assignedSites)
                                    {
                                        await assignedSite.Create(_backendConfigurationPnDbContext);
                                    }
                                }

                                if (areaRule.TailBite is true)
                                {
                                    const string eformName = "24. Halebid_NEW";
                                    var eformId = await sdkDbContex.CheckListTranslations
                                        .Where(x => x.Text == eformName)
                                        .Select(x => x.CheckListId)
                                        .FirstAsync();
                                    var planningForType3TailBite = new Planning
                                    {
                                        CreatedByUserId = _userService.UserId,
                                        Enabled = areaRulePlanningModel.Status,
                                        RelatedEFormId = eformId,
                                        RelatedEFormName = eformName,
                                        SdkFolderName = areaRule.FolderName,
                                        SdkFolderId = areaRule.FolderId,
                                        DaysBeforeRedeploymentPushMessageRepeat = false,
                                        DaysBeforeRedeploymentPushMessage = 5,
                                        PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                        StartDate = areaRulePlanningModel.StartDate,
                                        NameTranslations = new List<PlanningNameTranslation>
                                    {
                                        new()
                                        {
                                            LanguageId = 1, // da
                                            Name = "Hale bid",
                                        },
                                        new()
                                        {
                                            LanguageId = 2, // en
                                            Name = "Tail bite",
                                        },
                                        // new PlanningNameTranslation
                                        // {
                                        //     LanguageId = 3, // ge
                                        //     Name = "Schwanz biss",
                                        // },
                                        // new PlanningNameTranslation
                                        // {
                                        //     LanguageId = 4,// uk-ua
                                        //     Name = "Укус за хвіст",
                                        // },
                                    },
                                        PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite()
                                        {
                                            SiteId = x.SiteId,
                                        }).ToList(),
                                    };

                                    if (areaRulePlanningModel.TypeSpecificFields != null)
                                    {
                                        if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                                        {
                                            planningForType3TailBite.RepeatType =
                                                (RepeatType)areaRulePlanningModel.TypeSpecificFields.RepeatType;
                                        }
                                        if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery is not null)
                                        {
                                            planningForType3TailBite.RepeatEvery =
                                                (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                        }
                                    }
                                    await planningForType3TailBite.Create(_itemsPlanningPnDbContext);
                                    var areaRulePlanningForType3TailBite = new AreaRulePlanning
                                    {
                                        CreatedByUserId = _userService.UserId,
                                        UpdatedByUserId = _userService.UserId,
                                        StartDate = areaRulePlanningModel.StartDate,
                                        Status = areaRulePlanningModel.Status,
                                        SendNotifications = areaRulePlanningModel.SendNotifications,
                                        AreaRuleId = areaRulePlanningModel.RuleId,
                                        ItemPlanningId = planningForType3TailBite.Id,
                                    };

                                    if (areaRulePlanningModel.TypeSpecificFields != null)
                                    {
                                        if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                                        {
                                            areaRulePlanningForType3TailBite.RepeatType = areaRulePlanningModel.TypeSpecificFields.RepeatType;
                                        }
                                        if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery is not null)
                                        {
                                            areaRulePlanningForType3TailBite.RepeatEvery =
                                                (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                        }
                                    }
                                    await areaRulePlanningForType3TailBite.Create(_backendConfigurationPnDbContext);

                                    var assignedSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                                    {
                                        AreaRulePlanningsId = areaRulePlanningForType3TailBite.Id,
                                        SiteId = x.SiteId,
                                        CreatedByUserId = _userService.UserId,
                                        UpdatedByUserId = _userService.UserId,
                                    }).ToList();

                                    foreach (var assignedSite in assignedSites)
                                    {
                                        await assignedSite.Create(_backendConfigurationPnDbContext);
                                    }
                                }

                                if (areaRule.TailBite is null or false &&
                                    areaRule.ChecklistStable is null or false)
                                {

                                    var areaRulePlanningForType3 = new AreaRulePlanning
                                    {
                                        CreatedByUserId = _userService.UserId,
                                        UpdatedByUserId = _userService.UserId,
                                        StartDate = areaRulePlanningModel.StartDate,
                                        Status = areaRulePlanningModel.Status,
                                        SendNotifications = areaRulePlanningModel.SendNotifications,
                                        AreaRuleId = areaRulePlanningModel.RuleId,
                                    };
                                    if (areaRulePlanningModel.TypeSpecificFields != null)
                                    {
                                        if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                                        {
                                            areaRulePlanningForType3.RepeatType = areaRulePlanningModel.TypeSpecificFields.RepeatType;
                                        }
                                        if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery is not null)
                                        {
                                            areaRulePlanningForType3.RepeatEvery =
                                                (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                        }
                                    }
                                    await areaRulePlanningForType3.Create(_backendConfigurationPnDbContext);

                                    var assignedSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                                    {
                                        AreaRulePlanningsId = areaRulePlanningForType3.Id,
                                        SiteId = x.SiteId,
                                        CreatedByUserId = _userService.UserId,
                                        UpdatedByUserId = _userService.UserId,
                                    }).ToList();

                                    foreach (var assignedSite in assignedSites)
                                    {
                                        await assignedSite.Create(_backendConfigurationPnDbContext);
                                    }
                                }

                                break;
                            }
                        case AreaTypesEnum.Type5:
                            {
                                var folderIds = await _backendConfigurationPnDbContext.ProperyAreaFolders
                                    .Include(x => x.AreaProperty)
                                    .Where(x => x.AreaProperty.PropertyId == areaRulePlanningModel.PropertyId)
                                    .Where(x => x.AreaProperty.AreaId == areaRule.AreaId)
                                    .Select(x => x.FolderId)
                                    .Skip(1)
                                    .ToListAsync();
                                var planning = new Planning
                                {
                                    CreatedByUserId = _userService.UserId,
                                    Enabled = areaRulePlanningModel.Status,
                                    RelatedEFormId = (int)areaRule.EformId,
                                    RelatedEFormName = areaRule.EformName,
                                    SdkFolderName = areaRule.FolderName,
                                    SdkFolderId = folderIds[areaRule.DayOfWeek],
                                    DaysBeforeRedeploymentPushMessageRepeat = false,
                                    DaysBeforeRedeploymentPushMessage = 5,
                                    PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                    StartDate = areaRulePlanningModel.StartDate,
                                    // create translations for planning from translation areaRule
                                    NameTranslations = areaRule.AreaRuleTranslations.Select(
                                        areaRuleAreaRuleTranslation => new PlanningNameTranslation
                                        {
                                            LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                                            Name = areaRuleAreaRuleTranslation.Name,
                                        }).ToList(),
                                    PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                                    {
                                        SiteId = x.SiteId,
                                    }).ToList(),
                                };
                                if (areaRulePlanningModel.TypeSpecificFields != null)
                                {
                                    planning.RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate;
                                    planning.DayOfWeek = (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                    if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery is not null)
                                    {
                                        planning.RepeatEvery =
                                            (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                    }

                                    if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                                    {
                                        planning.RepeatType =
                                            (RepeatType)areaRulePlanningModel.TypeSpecificFields.RepeatType;
                                    }
                                }

                                await planning.Create(_itemsPlanningPnDbContext);
                                var areaRulePlanning = new AreaRulePlanning
                                {
                                    CreatedByUserId = _userService.UserId,
                                    UpdatedByUserId = _userService.UserId,
                                    StartDate = areaRulePlanningModel.StartDate,
                                    Status = areaRulePlanningModel.Status,
                                    SendNotifications = areaRulePlanningModel.SendNotifications,
                                    AreaRuleId = areaRulePlanningModel.RuleId,
                                    ItemPlanningId = planning.Id,
                                };
                                if (areaRulePlanningModel.TypeSpecificFields != null)
                                {
                                    areaRulePlanning.EndDate = areaRulePlanningModel.TypeSpecificFields?.EndDate;
                                    areaRulePlanning.DayOfWeek = areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                    areaRulePlanning.RepeatEvery = areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                    areaRulePlanning.RepeatType = areaRulePlanningModel.TypeSpecificFields.RepeatType;
                                }

                                await areaRulePlanning.Create(_backendConfigurationPnDbContext);

                                var assignedSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                                {
                                    AreaRulePlanningsId = areaRulePlanning.Id,
                                    SiteId = x.SiteId,
                                    CreatedByUserId = _userService.UserId,
                                    UpdatedByUserId = _userService.UserId,
                                }).ToList();

                                foreach (var assignedSite in assignedSites)
                                {
                                    await assignedSite.Create(_backendConfigurationPnDbContext);
                                }

                                break;
                            }
                        default:
                            {
                                if (areaRule.FolderId == 0)
                                {
                                    var folderId = await _backendConfigurationPnDbContext.ProperyAreaFolders
                                        .Include(x => x.AreaProperty)
                                        .Where(x => x.AreaProperty.PropertyId == areaRulePlanningModel.PropertyId)
                                        .Where(x => x.AreaProperty.AreaId == areaRule.AreaId)
                                        .Select(x => x.FolderId)
                                        .FirstOrDefaultAsync();
                                    if(folderId != 0)
                                    {
                                        areaRule.FolderId = folderId;
                                        areaRule.FolderName = await sdkDbContex.FolderTranslations
                                            .Where(x => x.FolderId == folderId)
                                            .Where(x => x.LanguageId == 1) // danish
                                            .Select(x => x.Name)
                                            .FirstAsync();
                                        await areaRule.Update(_backendConfigurationPnDbContext);
                                    }
                                }
                                var planning = new Planning
                                {
                                    CreatedByUserId = _userService.UserId,
                                    Enabled = areaRulePlanningModel.Status,
                                    RelatedEFormId = (int)areaRule.EformId,
                                    RelatedEFormName = areaRule.EformName,
                                    SdkFolderName = areaRule.FolderName,
                                    SdkFolderId = areaRule.FolderId,
                                    DaysBeforeRedeploymentPushMessageRepeat = false,
                                    DaysBeforeRedeploymentPushMessage = 5,
                                    PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                    StartDate = areaRulePlanningModel.StartDate,
                                    // create translations for planning from translation areaRule
                                    NameTranslations = areaRule.AreaRuleTranslations.Select(
                                        areaRuleAreaRuleTranslation => new PlanningNameTranslation
                                        {
                                            LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                                            Name = areaRuleAreaRuleTranslation.Name,
                                        }).ToList(),
                                    PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                                    {
                                        SiteId = x.SiteId,
                                    }).ToList(),
                                };
                                if (areaRulePlanningModel.TypeSpecificFields != null)
                                {
                                    planning.RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate;
                                    planning.DayOfWeek = (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                    if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery is not null)
                                    {
                                        planning.RepeatEvery =
                                            (int) areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                    }

                                    if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                                    {
                                        planning.RepeatType =
                                            (RepeatType) areaRulePlanningModel.TypeSpecificFields.RepeatType;
                                    }
                                }

                                await planning.Create(_itemsPlanningPnDbContext);
                                var areaRulePlanning = new AreaRulePlanning
                                {
                                    CreatedByUserId = _userService.UserId,
                                    UpdatedByUserId = _userService.UserId,
                                    StartDate = areaRulePlanningModel.StartDate,
                                    Status = areaRulePlanningModel.Status,
                                    SendNotifications = areaRulePlanningModel.SendNotifications,
                                    AreaRuleId = areaRulePlanningModel.RuleId,
                                    ItemPlanningId = planning.Id,
                                };
                                if (areaRulePlanningModel.TypeSpecificFields != null)
                                {
                                    areaRulePlanning.EndDate = areaRulePlanningModel.TypeSpecificFields?.EndDate;
                                    areaRulePlanning.DayOfWeek = areaRulePlanningModel.TypeSpecificFields.DayOfWeek;
                                    areaRulePlanning.RepeatEvery = areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                    areaRulePlanning.RepeatType = areaRulePlanningModel.TypeSpecificFields.RepeatType;
                                }

                                await areaRulePlanning.Create(_backendConfigurationPnDbContext);

                                var assignedSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                                {
                                    AreaRulePlanningsId = areaRulePlanning.Id,
                                    SiteId = x.SiteId,
                                    CreatedByUserId = _userService.UserId,
                                    UpdatedByUserId = _userService.UserId,
                                }).ToList();

                                foreach (var assignedSite in assignedSites)
                                {
                                    await assignedSite.Create(_backendConfigurationPnDbContext);
                                }

                                break;
                            }
                    }

                    return new OperationDataResult<AreaRuleModel>(true,
                        _backendConfigurationLocalizationService.GetString("SuccessfullyCreatedPlanning"));
                }
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
                    .Include(x => x.AreaRulesPlannings)
                    .FirstOrDefaultAsync();

                if (areaRule == null)
                {
                    return new OperationDataResult<AreaRulePlanningModel>(false, _backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning"));
                }

                if (!areaRule.AreaRulesPlannings.Any())
                {
                    return new OperationDataResult<AreaRulePlanningModel>(true); // it's okay
                }

                var areaRulePlanning = await _backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaRule.AreaRulesPlannings.First().Id)
                    .Include(x => x.PlanningSites)
                    .Select(x => new AreaRulePlanningModel
                    {
                        Id = x.Id,
                        RuleId = ruleId,
                        StartDate = (DateTime)x.StartDate,
                        Status = x.Status,
                        TypeSpecificFields = new AreaRuleTypePlanningModel
                        {
                            DayOfWeek = x.DayOfWeek,
                            EndDate = x.EndDate,
                            RepeatEvery = x.RepeatEvery,
                            RepeatType = x.RepeatType,
                            Alarm = x.Alarm,
                            Type = x.Type,
                        },
                        SendNotifications = x.SendNotifications,
                        AssignedSites = x.PlanningSites
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(y => new AreaRuleAssignedSitesModel { SiteId = y.SiteId, Checked = true })
                            .ToList()
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
