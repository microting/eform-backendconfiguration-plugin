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
    using Microting.eForm.Infrastructure.Models;
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
    using CommonTranslationsModel = Microting.eForm.Infrastructure.Models.CommonTranslationsModel;
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
                var areaProperty = await _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == propertyAreaId)
                    .Select(x => new { x.AreaId, x.PropertyId })
                    .FirstAsync();

                var currentUserLanguage = await _userService.GetCurrentUserLanguage();

                var query = _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.AreaId == areaProperty.AreaId)
                    .Where(x => x.PropertyId == areaProperty.PropertyId)
                    .Where(x => x.Area.AreaProperties.Select(y => y.Id).Contains(propertyAreaId))
                    .Include(x => x.AreaRuleTranslations)
                    .Include(x => x.AreaRuleInitialField)
                    .AsQueryable();

                var areaRules = await query
                    .Select(x => new AreaRuleSimpleModel
                    {
                        Id = x.Id,
                        EformName = x.EformName,
                        TranslatedName = x.AreaRuleTranslations
                            .Where(y => y.LanguageId == currentUserLanguage.Id)
                            .Select(y => y.Name)
                            .FirstOrDefault(),
                        IsDefault = x.IsDefault,
                        TypeSpecificFields = new
                            {x.EformId, x.Type, x.Alarm, x.ChecklistStable, x.TailBite, x.DayOfWeek,},
                        InitialFields = x.AreaRuleInitialField != null
                            ? new AreaRuleInitialFields
                            {
                                RepeatType = x.AreaRuleInitialField.RepeatType,
                                RepeatEvery = x.AreaRuleInitialField.RepeatEvery,
                                Type = x.AreaRuleInitialField.Type,
                                Alarm = x.AreaRuleInitialField.Alarm,
                                DayOfWeek = x.AreaRuleInitialField.DayOfWeek,
                                EformName = x.AreaRuleInitialField.EformName,
                                EndDate = x.AreaRuleInitialField.EndDate,
                                SendNotifications = x.AreaRuleInitialField.Notifications,
                            }
                            : null,
                    })
                    .ToListAsync();

                foreach (var areaRule in areaRules)
                {
                    var core = await _coreHelper.GetCore();
                    var sdkDbContext = core.DbContextHelper.GetDbContext();
                    var areaRulePlannings = await _backendConfigurationPnDbContext.AreaRulePlannings
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.AreaRuleId == areaRule.Id)
                        .ToListAsync();
                    areaRule.PlanningStatus = areaRulePlannings.Any(x => x.ItemPlanningId != 0);

                    if (areaRule.InitialFields != null && !string.IsNullOrEmpty(areaRule.InitialFields.EformName))
                    {
                        areaRule.InitialFields.EformId = await sdkDbContext.CheckListTranslations
                            .Where(x => x.Text == areaRule.InitialFields.EformName)
                            .Select(x => x.CheckListId)
                            .FirstOrDefaultAsync();
                    }
                }

                return new OperationDataResult<List<AreaRuleSimpleModel>>(true, areaRules);
            }
            catch (Exception ex)
            {
                Log.LogException(ex.Message);
                Log.LogException(ex.StackTrace);
                return new OperationDataResult<List<AreaRuleSimpleModel>>(false,
                    _backendConfigurationLocalizationService.GetString("ErrorWhileObtainingAreaRules"));
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
                        IsDefault = x.IsDefault,
                        TypeSpecificFields = new
                            {x.Type, x.Alarm, x.ChecklistStable, x.TailBite, x.DayOfWeek,},
                        EformId = x.EformId,
                    })
                    .FirstOrDefaultAsync();

                if (areaRule == null)
                {
                    return new OperationDataResult<AreaRuleModel>(false,
                        _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
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
                return new OperationDataResult<AreaRuleModel>(false,
                    _backendConfigurationLocalizationService.GetString("ErrorWhileReadAreaRule"));
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
                    return new OperationDataResult<AreaRuleModel>(false,
                        _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
                }

                if (areaRule.IsDefault)
                {
                    return new OperationDataResult<AreaRuleModel>(false,
                        _backendConfigurationLocalizationService.GetString("AreaRuleCan'tBeUpdated"));
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
                    var translateForUpdate = areaRule.AreaRuleTranslations
                        .Where(x => x.LanguageId == updateModelTranslatedName.Id)
                        .FirstOrDefault(x => x.WorkflowState != Constants.WorkflowStates.Removed);
                    if (translateForUpdate is null)
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
                        if (translateForUpdate.Name != updateModelTranslatedName.Name)
                        {
                            translateForUpdate.Name = updateModelTranslatedName.Name;
                            await translateForUpdate.Update(_backendConfigurationPnDbContext);
                        }
                    }
                }

                return new OperationDataResult<AreaRuleModel>(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyUpdateAreaRule"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRuleModel>(false,
                    _backendConfigurationLocalizationService.GetString("ErrorWhileUpdateAreaRule"));
            }
        }

        public async Task<OperationResult> Delete(int areaId)
        {
            try
            {
                var areaRule = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaId)
                    .Include(x => x.Area)
                    .Include(x => x.AreaRuleTranslations)
                    .Include(x => x.AreaRulesPlannings)
                    .ThenInclude(x => x.PlanningSites)
                    .FirstOrDefaultAsync();

                if (areaRule == null)
                {
                    return new OperationDataResult<AreaRuleModel>(false,
                        _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
                }

                if (areaRule.IsDefault)
                {
                    return new OperationDataResult<AreaRuleModel>(false,
                        _backendConfigurationLocalizationService.GetString("AreaRuleCantBeDeleted"));
                }


                if (areaRule.Area.Type is AreaTypesEnum.Type3 && areaRule.GroupItemId != 0)
                {
                    var core = await _coreHelper.GetCore();
                    var sdkDbContext = core.DbContextHelper.GetDbContext();
                    var entityGroupItem = await sdkDbContext.EntityItems.Where(x => x.Id == areaRule.GroupItemId).FirstOrDefaultAsync();
                    if (entityGroupItem != null)
                    {
                        await entityGroupItem.Delete(sdkDbContext);
                    }
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

                return new OperationDataResult<AreaRuleModel>(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyDeletedAreaRule"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRuleModel>(false,
                    _backendConfigurationLocalizationService.GetString("ErrorWhileDeleteAreaRule"));
            }
        }

        public async Task<OperationResult> Create(AreaRulesCreateModel createModel)
        {
            try
            {
                var areaProperty = await _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == createModel.PropertyAreaId)
                    .Select(x => new {x.Area, x.GroupMicrotingUuid, x.PropertyId})
                    .FirstAsync();

                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();

                foreach (var areaRuleCreateModel in createModel.AreaRules)
                {
                    var eformId = areaRuleCreateModel.TypeSpecificFields.EformId;
                    if (areaProperty.Area.Type is AreaTypesEnum.Type2 or AreaTypesEnum.Type6)
                    {
                        var eformName = areaProperty.Area.Type switch
                        {
                            AreaTypesEnum.Type2 => "03. Kontrol konstruktion",
                            AreaTypesEnum.Type6 => "10. Varmepumpe serviceaftale",
                            _ => ""
                        };
                        eformId = await sdkDbContext.CheckListTranslations
                            .Where(x => x.Text == eformName)
                            .Select(x => x.CheckListId)
                            .FirstAsync();
                    }

                    var areaRule = new AreaRule
                    {
                        AreaId = areaProperty.Area.Id,
                        UpdatedByUserId = _userService.UserId,
                        CreatedByUserId = _userService.UserId,
                        Alarm = areaRuleCreateModel.TypeSpecificFields?.Alarm,
                        DayOfWeek = (int) areaRuleCreateModel.TypeSpecificFields?.DayOfWeek,
                        Type = areaRuleCreateModel.TypeSpecificFields?.Type,
                        TailBite = areaRuleCreateModel.TypeSpecificFields?.TailBite,
                        ChecklistStable = areaRuleCreateModel.TypeSpecificFields?.ChecklistStable,
                        EformId = eformId,
                        PropertyId = areaProperty.PropertyId,
                    };

                    if (areaProperty.Area.Type is AreaTypesEnum.Type3)
                    {
                        var entityGroup = await core.EntityGroupRead(areaProperty.GroupMicrotingUuid.ToString());
                        var nextItemUid = entityGroup.EntityGroupItemLst.Count;
                        var entityItem = await core.EntitySelectItemCreate(entityGroup.Id, areaRuleCreateModel.TranslatedNames.First().Name, entityGroup.EntityGroupItemLst.Count,
                                nextItemUid.ToString());
                        areaRule.GroupItemId = entityItem.Id;
                    }

                    var language = await _userService.GetCurrentUserLanguage();
                    if (eformId != 0)
                    {
                        areaRule.EformName = await sdkDbContext.CheckListTranslations
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.CheckListId == eformId)
                            .Where(x => x.LanguageId == language.Id)
                            .Select(x => x.Text)
                            .FirstOrDefaultAsync();
                    }
                    areaRule.FolderId = await _backendConfigurationPnDbContext.ProperyAreaFolders
                        .Include(x => x.AreaProperty)
                        .Where(x => x.AreaProperty.Id == createModel.PropertyAreaId)
                        .Select(x => x.FolderId)
                        .FirstOrDefaultAsync();
                    areaRule.FolderName = await sdkDbContext.FolderTranslations
                        .Where(x => x.FolderId == areaRule.FolderId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync();

                    await areaRule.Create(_backendConfigurationPnDbContext);

                    var translations = areaRuleCreateModel.TranslatedNames
                        .Select(x => new AreaRuleTranslation
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

                return new OperationDataResult<AreaRuleModel>(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyCreateAreaRule"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRuleModel>(false,
                    _backendConfigurationLocalizationService.GetString("ErrorWhileCreateAreaRule"));
            }
        }

        public async Task<OperationResult> UpdatePlanning(AreaRulePlanningModel areaRulePlanningModel)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                areaRulePlanningModel.AssignedSites =
                    areaRulePlanningModel.AssignedSites.Where(x => x.Checked).ToList();

                if (areaRulePlanningModel.Id.HasValue) // update planning
                {
                    var areaRule = await _backendConfigurationPnDbContext.AreaRules
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == areaRulePlanningModel.RuleId)
                        .Include(x => x.AreaRulesPlannings)
                        .ThenInclude(x => x.PlanningSites)
                        .Include(x => x.Area)
                        .Include(x => x.AreaRuleTranslations)
                        .FirstOrDefaultAsync();
                    if (!areaRule.AreaRulesPlannings.Any())
                    {
                        return new OperationDataResult<AreaRulePlanningModel>(false,
                            _backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning"));
                    }

                    for (var i = 0; i < areaRule.AreaRulesPlannings.Count; i++)
                    {
                        var rulePlanning = areaRule.AreaRulesPlannings[i];
                        var oldStatus = rulePlanning.Status;
                        rulePlanning.UpdatedByUserId = _userService.UserId;
                        rulePlanning.StartDate = areaRulePlanningModel.StartDate;
                        rulePlanning.Status = areaRulePlanningModel.Status;
                        rulePlanning.SendNotifications = areaRulePlanningModel.SendNotifications;
                        rulePlanning.AreaRuleId = areaRulePlanningModel.RuleId;
                        if (areaRulePlanningModel.TypeSpecificFields != null)
                        {
                            rulePlanning.HoursAndEnergyEnabled =
                                areaRulePlanningModel.TypeSpecificFields.HoursAndEnergyEnabled;
                            rulePlanning.DayOfMonth = areaRulePlanningModel.TypeSpecificFields.DayOfMonth;
                            rulePlanning.EndDate = areaRulePlanningModel.TypeSpecificFields.EndDate;
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
                            .Where(x => !rulePlanning.PlanningSites
                                .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed).Select(y => y.SiteId)
                                .Contains(x))
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

                        switch (oldStatus)
                        {
                            // create item planning
                            case false when areaRulePlanningModel.Status:
                                switch (areaRule.Area.Type)
                                {
                                    case AreaTypesEnum.Type2: // tanks
                                    {
                                        if (areaRule.Type == AreaRuleT2TypesEnum.Open)
                                        {
                                            const string eformName = "03. Kontrol flydelag";
                                            var eformId = await sdkDbContext.CheckListTranslations
                                                .Where(x => x.Text == eformName)
                                                .Select(x => x.CheckListId)
                                                .FirstAsync();

                                            var planningForType2TypeTankOpen = new Planning
                                            {
                                                CreatedByUserId = _userService.UserId,
                                                Enabled = areaRulePlanningModel.Status,
                                                RelatedEFormId = eformId,
                                                RelatedEFormName = eformName,
                                                SdkFolderName = areaRule.AreaRuleTranslations
                                                    .Where(x => x.LanguageId == 1)
                                                    .Select(x => x.Name).FirstOrDefault(),
                                                SdkFolderId = areaRule.AreaRulesPlannings[0].FolderId,
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
                                                    new()
                                                    {
                                                        LanguageId = 3, // ge
                                                        Name = areaRule.AreaRuleTranslations
                                                            .Where(x => x.LanguageId == 2)
                                                            .Select(x => x.Name)
                                                            .FirstOrDefault() + " - Schwimmende Ebene prüfen",
                                                    },
                                                    // new PlanningNameTranslation
                                                    // {
                                                    //     LanguageId = 4,// uk-ua
                                                    //     Name = "Перевірте плаваючий шар",
                                                    // },
                                                },
                                                RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                                                DayOfWeek = (DayOfWeek)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek,
                                                DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                                RepeatEvery = 1,
                                                RepeatType = RepeatType.Month,
                                                PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                                    new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                                        PlanningSite()
                                                        {
                                                            SiteId = x.SiteId,
                                                        }).ToList(),
                                            };
                                            await planningForType2TypeTankOpen.Create(_itemsPlanningPnDbContext);
                                            areaRule.AreaRulesPlannings[0].DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth);
                                                areaRule.AreaRulesPlannings[0].ItemPlanningId =
                                                planningForType2TypeTankOpen.Id;
                                            await areaRule.AreaRulesPlannings[0]
                                                .Update(_backendConfigurationPnDbContext);
                                        }

                                        if (areaRule.Type is AreaRuleT2TypesEnum.Open or AreaRuleT2TypesEnum.Closed
                                            && areaRule.Alarm is AreaRuleT2AlarmsEnum.Yes)
                                        {
                                            const string eformName = "03. Kontrol alarmanlæg gyllebeholder";
                                            var eformId = await sdkDbContext.CheckListTranslations
                                                .Where(x => x.Text == eformName)
                                                .Select(x => x.CheckListId)
                                                .FirstAsync();

                                            var planningForType2AlarmYes = new Planning
                                            {
                                                CreatedByUserId = _userService.UserId,
                                                Enabled = areaRulePlanningModel.Status,
                                                RelatedEFormId = eformId,
                                                RelatedEFormName = eformName,
                                                SdkFolderName = areaRule.AreaRuleTranslations
                                                    .Where(x => x.LanguageId == 1)
                                                    .Select(x => x.Name).FirstOrDefault(),
                                                SdkFolderId = areaRule.AreaRulesPlannings[1].FolderId,
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
                                                    new()
                                                    {
                                                        LanguageId = 3, // ge
                                                        Name = areaRule.AreaRuleTranslations
                                                            .Where(x => x.LanguageId == 3)
                                                            .Select(x => x.Name)
                                                            .FirstOrDefault() + " - Check alarm",
                                                    },
                                                    // new ()
                                                    // {
                                                    //     LanguageId = 4,// uk-ua
                                                    //     Name = areaRule.AreaRuleTranslations
                                                    //.Where(x => x.LanguageId == 4)
                                                    //.Select(x => x.Name)
                                                    //.FirstOrDefault() + "Перевірте сигналізацію",
                                                    // },
                                                },
                                                RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                                                DayOfWeek = (DayOfWeek)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek,
                                                DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                                RepeatEvery = 1,
                                                RepeatType = RepeatType.Month,
                                                PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                                    new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                                        PlanningSite()
                                                        {
                                                            SiteId = x.SiteId,
                                                        }).ToList(),
                                            };
                                            await planningForType2AlarmYes.Create(_itemsPlanningPnDbContext);
                                            areaRule.AreaRulesPlannings[1].DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth);
                                                areaRule.AreaRulesPlannings[1].ItemPlanningId = planningForType2AlarmYes.Id;
                                            await areaRule.AreaRulesPlannings[1]
                                                .Update(_backendConfigurationPnDbContext);
                                        }

                                        var planningForType2 = new Planning
                                        {
                                            CreatedByUserId = _userService.UserId,
                                            Enabled = areaRulePlanningModel.Status,
                                            RelatedEFormId = (int) areaRule.EformId,
                                            RelatedEFormName = areaRule.EformName, // must be "03. Kontrol konstruktion"
                                            SdkFolderName = areaRule.AreaRuleTranslations.Where(x => x.LanguageId == 1)
                                                .Select(x => x.Name).FirstOrDefault(), // name tank
                                            SdkFolderId = areaRule.AreaRulesPlannings[2].FolderId,
                                            DaysBeforeRedeploymentPushMessageRepeat = false,
                                            DaysBeforeRedeploymentPushMessage = 5,
                                            PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                            StartDate = areaRulePlanningModel.StartDate,
                                            // create translations for planning from translation areaRule
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
                                                new()
                                                {
                                                    LanguageId = 3, // ge
                                                    Name = areaRule.AreaRuleTranslations
                                                        .Where(x => x.LanguageId == 3)
                                                        .Select(x => x.Name)
                                                        .FirstOrDefault() + " - Konstruktion prüfen",
                                                },
                                                // new PlanningNameTranslation
                                                // {
                                                //     LanguageId = 4,// uk-ua
                                                //     Name = "Перевірте конструкцію",
                                                // },
                                            },
                                            RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                                            DayOfWeek = (DayOfWeek)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek,
                                            RepeatEvery = 12,
                                            RepeatType = RepeatType.Month,
                                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                                new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                                    PlanningSite()
                                                    {
                                                        SiteId = x.SiteId,
                                                    }).ToList(),
                                        };
                                        await planningForType2.Create(_itemsPlanningPnDbContext);
                                        areaRule.AreaRulesPlannings[2].ItemPlanningId = planningForType2.Id;
                                        areaRule.AreaRulesPlannings[2].DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth);
                                            await areaRule.AreaRulesPlannings[2].Update(_backendConfigurationPnDbContext);
                                        i = areaRule.AreaRulesPlannings.Count;
                                        break;
                                    }
                                    case AreaTypesEnum.Type3: // stables and tail bite
                                    {
                                        if (areaRule.ChecklistStable is true)
                                        {
                                            var planningForType3ChecklistStable = new Planning
                                            {
                                                CreatedByUserId = _userService.UserId,
                                                Enabled = areaRulePlanningModel.Status,
                                                RelatedEFormId = (int) areaRule.EformId,
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
                                                PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                                    new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                                        PlanningSite
                                                        {
                                                            SiteId = x.SiteId,
                                                        }).ToList(),
                                                DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                                DayOfWeek = (DayOfWeek)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                                            };
                                            if (areaRulePlanningModel.TypeSpecificFields?.RepeatType != null)
                                            {
                                                planningForType3ChecklistStable.RepeatType =
                                                    (RepeatType) areaRulePlanningModel.TypeSpecificFields
                                                        .RepeatType;
                                            }

                                            if (areaRulePlanningModel.TypeSpecificFields?.RepeatEvery != null)
                                            {
                                                planningForType3ChecklistStable.RepeatEvery =
                                                    (int) areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                            }

                                            await planningForType3ChecklistStable.Create(_itemsPlanningPnDbContext);
                                            areaRule.AreaRulesPlannings[0].ItemPlanningId =
                                                planningForType3ChecklistStable.Id;
                                            areaRule.AreaRulesPlannings[0].DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth);
                                            areaRule.AreaRulesPlannings[0].DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek);
                                                await areaRule.AreaRulesPlannings[0]
                                                .Update(_backendConfigurationPnDbContext);
                                        }

                                        if (areaRule.TailBite is true)
                                        {
                                            const string eformName = "24. Halebid_NEW";
                                            var eformId = await sdkDbContext.CheckListTranslations
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
                                                    new()
                                                    {
                                                        LanguageId = 3, // ge
                                                        Name = "Schwanzbiss",
                                                    },
                                                    // new PlanningNameTranslation
                                                    // {
                                                    //     LanguageId = 4,// uk-ua
                                                    //     Name = "Укус за хвіст",
                                                    // },
                                                },
                                                PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                                    new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                                        PlanningSite
                                                        {
                                                            SiteId = x.SiteId,
                                                        }).ToList(),
                                                DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                                DayOfWeek = (DayOfWeek)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                                            };

                                            if (areaRulePlanningModel.TypeSpecificFields?.RepeatType != null)
                                            {
                                                planningForType3TailBite.RepeatType =
                                                    (RepeatType) areaRulePlanningModel.TypeSpecificFields
                                                        .RepeatType;
                                            }

                                            if (areaRulePlanningModel.TypeSpecificFields?.RepeatEvery != null)
                                            {
                                                planningForType3TailBite.RepeatEvery =
                                                    (int) areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                            }

                                            await planningForType3TailBite.Create(_itemsPlanningPnDbContext);
                                            areaRule.AreaRulesPlannings[1].DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth);
                                            areaRule.AreaRulesPlannings[1].DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek);
                                                areaRule.AreaRulesPlannings[1].ItemPlanningId = planningForType3TailBite.Id;
                                            await areaRule.AreaRulesPlannings[1]
                                                .Update(_backendConfigurationPnDbContext);
                                        }

                                        i = areaRule.AreaRulesPlannings.Count;
                                        break;
                                    }
                                    case AreaTypesEnum.Type6: // head pumps
                                    {
                                        if (areaRulePlanningModel.TypeSpecificFields?.HoursAndEnergyEnabled is true)
                                        {
                                            areaRule.AreaRulesPlannings[0].HoursAndEnergyEnabled = true;
                                            areaRule.AreaRulesPlannings[1].HoursAndEnergyEnabled = true;
                                            areaRule.AreaRulesPlannings[2].HoursAndEnergyEnabled = true;
                                            areaRule.AreaRulesPlannings[0].DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth);
                                            areaRule.AreaRulesPlannings[0].DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek);
                                            areaRule.AreaRulesPlannings[1].DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth);
                                            areaRule.AreaRulesPlannings[1].DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek);
                                            areaRule.AreaRulesPlannings[2].DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth);
                                            areaRule.AreaRulesPlannings[2].DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek);
                                                await areaRule.Update(_backendConfigurationPnDbContext);
                                            const string eformName = "10. Varmepumpe timer og energi";
                                            var eformId = await sdkDbContext.CheckListTranslations
                                                .Where(x => x.Text == eformName)
                                                .Select(x => x.CheckListId)
                                                .FirstAsync();
                                            var planningForType6HoursAndEnergyEnabled = new Planning
                                            {
                                                CreatedByUserId = _userService.UserId,
                                                Enabled = areaRulePlanningModel.Status,
                                                RelatedEFormId = eformId, //(int) areaRule.EformId,
                                                RelatedEFormName = eformName,
                                                SdkFolderName = areaRule.AreaRuleTranslations
                                                    .Where(x => x.LanguageId == 1).Select(x => x.Name).FirstOrDefault(),
                                                SdkFolderId = areaRule.AreaRulesPlannings[0].FolderId,
                                                DaysBeforeRedeploymentPushMessageRepeat = false,
                                                DaysBeforeRedeploymentPushMessage = 5,
                                                PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                                StartDate = areaRulePlanningModel.StartDate,
                                                // create translations for planning from translation areaRule
                                                NameTranslations = new List<PlanningNameTranslation>()
                                                {
                                                    new()
                                                    {
                                                        LanguageId = 1, // da
                                                        Name = areaRule.AreaRuleTranslations
                                                            .Where(x => x.LanguageId == 1)
                                                            .Select(x => x.Name)
                                                            .FirstOrDefault() + ": Timer og energi",
                                                    },
                                                    new()
                                                    {
                                                        LanguageId = 2, // en
                                                        Name = areaRule.AreaRuleTranslations
                                                            .Where(x => x.LanguageId == 2)
                                                            .Select(x => x.Name)
                                                            .FirstOrDefault() + ": Hours and energy",
                                                    },
                                                    new()
                                                    {
                                                        LanguageId = 3, // ge
                                                        Name = areaRule.AreaRuleTranslations
                                                            .Where(x => x.LanguageId == 3)
                                                            .Select(x => x.Name)
                                                            .FirstOrDefault() + ": Stunden und Energie",
                                                    },
                                                },
                                                PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                                    new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                                        PlanningSite()
                                                        {
                                                            SiteId = x.SiteId,
                                                        }).ToList(),
                                                DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                                DayOfWeek = (DayOfWeek)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                                            };
                                            await planningForType6HoursAndEnergyEnabled.Create(
                                                _itemsPlanningPnDbContext);
                                            areaRule.AreaRulesPlannings[0].ItemPlanningId =
                                                planningForType6HoursAndEnergyEnabled.Id;
                                            await areaRule.AreaRulesPlannings[0]
                                                .Update(_backendConfigurationPnDbContext);
                                        }

                                        const string eformNameOne = "10. Varmepumpe serviceaftale";
                                        var eformIdOne = await sdkDbContext.CheckListTranslations
                                            .Where(x => x.Text == eformNameOne)
                                            .Select(x => x.CheckListId)
                                            .FirstAsync();
                                        const string eformNameTwo = "10. Varmepumpe logbog";
                                        var eformIdTwo = await sdkDbContext.CheckListTranslations
                                            .Where(x => x.Text == eformNameTwo)
                                            .Select(x => x.CheckListId)
                                            .FirstAsync();
                                        var planningForType6One = new Planning
                                        {
                                            CreatedByUserId = _userService.UserId,
                                            Enabled = areaRulePlanningModel.Status,
                                            RelatedEFormId = eformIdOne,
                                            RelatedEFormName = eformNameOne,
                                            SdkFolderName = areaRule.AreaRuleTranslations.Where(x => x.LanguageId == 1)
                                                .Select(x => x.Name).FirstOrDefault(), // name head pump
                                            SdkFolderId = areaRule.AreaRulesPlannings[1].FolderId,
                                            DaysBeforeRedeploymentPushMessageRepeat = false,
                                            DaysBeforeRedeploymentPushMessage = 5,
                                            PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                            StartDate = areaRulePlanningModel.StartDate,
                                            // create translations for planning from translation areaRule
                                            NameTranslations = new List<PlanningNameTranslation>()
                                            {
                                                new()
                                                {
                                                    LanguageId = 1, // da
                                                    Name = areaRule.AreaRuleTranslations
                                                        .Where(x => x.LanguageId == 1)
                                                        .Select(x => x.Name)
                                                        .FirstOrDefault() + ": Service",
                                                },
                                                new()
                                                {
                                                    LanguageId = 2, // en
                                                    Name = areaRule.AreaRuleTranslations
                                                        .Where(x => x.LanguageId == 2)
                                                        .Select(x => x.Name)
                                                        .FirstOrDefault() + ": Service",
                                                },
                                                new()
                                                {
                                                    LanguageId = 3, // ge
                                                    Name = areaRule.AreaRuleTranslations
                                                        .Where(x => x.LanguageId == 3)
                                                        .Select(x => x.Name)
                                                        .FirstOrDefault() + ": Service",
                                                },
                                            },
                                            RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                                            RepeatEvery = 12,
                                            RepeatType = RepeatType.Month,
                                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                                new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                                    PlanningSite()
                                                    {
                                                        SiteId = x.SiteId,
                                                    }).ToList(),
                                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                            DayOfWeek = (DayOfWeek)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                                        };
                                        var planningForType6Two = new Planning
                                        {
                                            CreatedByUserId = _userService.UserId,
                                            Enabled = areaRulePlanningModel.Status,
                                            RelatedEFormId = eformIdTwo,
                                            RelatedEFormName = eformNameTwo,
                                            SdkFolderName = areaRule.AreaRuleTranslations.Where(x => x.LanguageId == 1)
                                                .Select(x => x.Name).FirstOrDefault(), // name head pump
                                            SdkFolderId = areaRule.AreaRulesPlannings[2].FolderId,
                                            DaysBeforeRedeploymentPushMessageRepeat = false,
                                            DaysBeforeRedeploymentPushMessage = 5,
                                            PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                            StartDate = areaRulePlanningModel.StartDate,
                                            // create translations for planning from translation areaRule
                                            NameTranslations = new List<PlanningNameTranslation>()
                                            {
                                                new()
                                                {
                                                    LanguageId = 1, // da
                                                    Name = areaRule.AreaRuleTranslations
                                                        .Where(x => x.LanguageId == 1)
                                                        .Select(x => x.Name)
                                                        .FirstOrDefault() + ": Logbog",
                                                },
                                                new()
                                                {
                                                    LanguageId = 2, // en
                                                    Name = areaRule.AreaRuleTranslations
                                                        .Where(x => x.LanguageId == 2)
                                                        .Select(x => x.Name)
                                                        .FirstOrDefault() + ": Logbook",
                                                },
                                                new()
                                                {
                                                    LanguageId = 3, // ge
                                                    Name = areaRule.AreaRuleTranslations
                                                        .Where(x => x.LanguageId == 3)
                                                        .Select(x => x.Name)
                                                        .FirstOrDefault() + ": Logbook",
                                                },
                                            },
                                            RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                                            RepeatEvery = 12,
                                            RepeatType = RepeatType.Month,
                                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                                new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                                    PlanningSite()
                                                    {
                                                        SiteId = x.SiteId,
                                                    }).ToList(),
                                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                        };
                                        await planningForType6One.Create(_itemsPlanningPnDbContext);
                                        areaRule.AreaRulesPlannings[1].ItemPlanningId = planningForType6One.Id;
                                        await planningForType6Two.Create(_itemsPlanningPnDbContext);
                                        areaRule.AreaRulesPlannings[2].ItemPlanningId = planningForType6Two.Id;
                                        await areaRule.AreaRulesPlannings[1].Update(_backendConfigurationPnDbContext);
                                        await areaRule.AreaRulesPlannings[2].Update(_backendConfigurationPnDbContext);
                                        i = areaRule.AreaRulesPlannings.Count;
                                        break;
                                    }
                                    default:
                                    {
                                        if (areaRule.FolderId == 0)
                                        {
                                            var folderId = await _backendConfigurationPnDbContext.ProperyAreaFolders
                                                .Include(x => x.AreaProperty)
                                                .Where(x => x.AreaProperty.PropertyId ==
                                                            areaRulePlanningModel.PropertyId)
                                                .Where(x => x.AreaProperty.AreaId == areaRule.AreaId)
                                                .Select(x => x.FolderId)
                                                .FirstOrDefaultAsync();
                                            if (folderId != 0)
                                            {
                                                areaRule.FolderId = folderId;
                                                areaRule.FolderName = await sdkDbContext.FolderTranslations
                                                    .Where(x => x.FolderId == folderId)
                                                    .Where(x => x.LanguageId == 1) // danish
                                                    .Select(x => x.Name)
                                                    .FirstAsync();
                                                await areaRule.Update(_backendConfigurationPnDbContext);
                                            }
                                        }

                                        if (areaRule.Area.Type == AreaTypesEnum.Type5) // recuring tasks(mon-sun)
                                        {
                                            var folderIds = await _backendConfigurationPnDbContext.ProperyAreaFolders
                                                .Include(x => x.AreaProperty)
                                                .Where(x => x.AreaProperty.PropertyId ==
                                                            areaRulePlanningModel.PropertyId)
                                                .Where(x => x.AreaProperty.AreaId == areaRule.AreaId)
                                                .Select(x => x.FolderId)
                                                .Skip(1)
                                                .ToListAsync();
                                            areaRule.FolderId = folderIds[areaRule.DayOfWeek];
                                            areaRule.FolderName = await sdkDbContext.FolderTranslations
                                                .Where(x => x.FolderId == areaRule.FolderId)
                                                .Where(x => x.LanguageId == 1) // danish
                                                .Select(x => x.Name)
                                                .FirstAsync();
                                            await areaRule.Update(_backendConfigurationPnDbContext);
                                        }

                                        var planning = new Planning
                                        {
                                            CreatedByUserId = _userService.UserId,
                                            Enabled = areaRulePlanningModel.Status,
                                            RelatedEFormId = (int) areaRule.EformId,
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
                                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                                new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                                    PlanningSite
                                                    {
                                                        SiteId = x.SiteId,
                                                    }).ToList(),
                                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                        };
                                        if (areaRulePlanningModel.TypeSpecificFields != null)
                                        {
                                            planning.RepeatUntil =
                                                areaRulePlanningModel.TypeSpecificFields?.EndDate;
                                            planning.DayOfWeek =
                                                (DayOfWeek?) areaRulePlanningModel.TypeSpecificFields?.DayOfWeek;
                                            if (areaRulePlanningModel.TypeSpecificFields.RepeatEvery is not null)
                                            {
                                                planning.RepeatEvery =
                                                    (int) areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                            }

                                            if (areaRulePlanningModel.TypeSpecificFields.RepeatType is not null)
                                            {
                                                planning.RepeatType =
                                                    (RepeatType) areaRulePlanningModel.TypeSpecificFields
                                                        .RepeatType;
                                            }
                                        }

                                        await planning.Create(_itemsPlanningPnDbContext);
                                        rulePlanning.ItemPlanningId = planning.Id;
                                        await rulePlanning.Update(_backendConfigurationPnDbContext);
                                        break;
                                    }
                                }

                                break;
                            // delete item planning
                            case true when !areaRulePlanningModel.Status:
                                if (rulePlanning.ItemPlanningId != 0)
                                {
                                    var planning = await _itemsPlanningPnDbContext.Plannings
                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                        .Where(x => x.Id == rulePlanning.ItemPlanningId)
                                        .Include(x => x.PlanningSites)
                                        .FirstAsync();
                                    planning.UpdatedByUserId = _userService.UserId;
                                    foreach (var planningSite in planning.PlanningSites
                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                    {
                                        planningSite.UpdatedByUserId = _userService.UserId;
                                        await planningSite.Delete(_itemsPlanningPnDbContext);
                                    }

                                    rulePlanning.ItemPlanningId = 0;
                                    await rulePlanning.Update(_backendConfigurationPnDbContext);
                                    await planning.Delete(_itemsPlanningPnDbContext);
                                }

                                break;
                            // update item planning
                            case true when areaRulePlanningModel.Status:
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
                                    planning.DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth);
                                    planning.DayOfWeek =
                                        (DayOfWeek)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek);
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

                                break;
                            // nothing to do
                            case false when !areaRulePlanningModel.Status:
                                break;
                        }
                    }

                    return new OperationDataResult<AreaRuleModel>(true,
                        _backendConfigurationLocalizationService.GetString("SuccessfullyUpdatePlanning"));
                }

                return await CreatePlanning(areaRulePlanningModel); // create planning
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRuleModel>(false,
                    _backendConfigurationLocalizationService.GetString("ErrorWhileUpdatePlanning"));
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
                    return new OperationDataResult<AreaRulePlanningModel>(false,
                        _backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning"));
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
                        StartDate = (DateTime) x.StartDate,
                        Status = x.Status,
                        TypeSpecificFields = new AreaRuleTypePlanningModel
                        {
                            DayOfWeek = x.DayOfWeek,
                            EndDate = x.EndDate,
                            RepeatEvery = x.RepeatEvery,
                            RepeatType = x.RepeatType,
                            Alarm = x.Alarm,
                            Type = x.Type,
                            HoursAndEnergyEnabled = x.HoursAndEnergyEnabled,
                            DayOfMonth = x.DayOfMonth,
                        },
                        SendNotifications = x.SendNotifications,
                        AssignedSites = x.PlanningSites
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(y => new AreaRuleAssignedSitesModel {SiteId = y.SiteId, Checked = true})
                            .ToList()
                    }).FirstOrDefaultAsync();


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
                    _backendConfigurationLocalizationService.GetString("ErrorWhileReadPlanning"));
            }
        }

        private async Task<OperationDataResult<AreaRuleModel>> CreatePlanning(AreaRulePlanningModel areaRulePlanningModel)
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var areaRule = await _backendConfigurationPnDbContext.AreaRules
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == areaRulePlanningModel.RuleId)
                .Include(x => x.AreaRuleTranslations)
                .Include(x => x.Area)
                .FirstOrDefaultAsync();

            if (areaRule == null)
            {
                return new OperationDataResult<AreaRuleModel>(true,
                    _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
            }

            switch (areaRule.Area.Type)
            {
                case AreaTypesEnum.Type2: // tanks
                {
                    var translatesForFolder = areaRule.AreaRuleTranslations
                        .Select(x => new CommonTranslationsModel
                        {
                            LanguageId = x.LanguageId,
                            Description = "",
                            Name = x.Name,
                        }).ToList();
                        // create folder with name tank
                        var folderId = await core.FolderCreate(translatesForFolder, areaRule.FolderId);
                        var planningForType2TypeTankOpenId = 0;
                        if (areaRule.Type == AreaRuleT2TypesEnum.Open)
                        {
                            const string eformName = "03. Kontrol flydelag";
                            var eformId = await sdkDbContext.CheckListTranslations
                                .Where(x => x.Text == eformName)
                                .Select(x => x.CheckListId)
                                .FirstAsync();

                            if (areaRulePlanningModel.Status)
                            {
                                var planningForType2TypeTankOpen = new Planning
                                {
                                    CreatedByUserId = _userService.UserId,
                                    Enabled = areaRulePlanningModel.Status,
                                    RelatedEFormId = eformId,
                                    RelatedEFormName = eformName,
                                    SdkFolderName = areaRule.AreaRuleTranslations.Where(x => x.LanguageId == 1)
                                        .Select(x => x.Name).FirstOrDefault(),
                                    SdkFolderId = folderId,
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
                                            new()
                                            {
                                                LanguageId = 3, // ge
                                                Name = areaRule.AreaRuleTranslations
                                                    .Where(x => x.LanguageId == 3)
                                                    .Select(x => x.Name)
                                                    .FirstOrDefault() + " - Schwimmende Ebene prüfen",
                                            },
                                            // new PlanningNameTranslation
                                            // {
                                            //     LanguageId = 4,// uk-ua
                                            //     Name = "Перевірте плаваючий шар",
                                            // },
                                        },
                                    RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                                    DayOfMonth = (areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                    DayOfWeek = (DayOfWeek?)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek),
                                    RepeatEvery = 1,
                                    RepeatType = RepeatType.Month,
                                    PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                        new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                            PlanningSite()
                                        {
                                            SiteId = x.SiteId,
                                        }).ToList(),
                                };
                                await planningForType2TypeTankOpen.Create(_itemsPlanningPnDbContext);
                                planningForType2TypeTankOpenId = planningForType2TypeTankOpen.Id;
                            }
                        }

                        var areaRulePlanningForType2TypeTankOpen = new AreaRulePlanning
                        {
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                            StartDate = areaRulePlanningModel.StartDate,
                            Status = areaRulePlanningModel.Status,
                            SendNotifications = areaRulePlanningModel.SendNotifications,
                            AreaRuleId = areaRulePlanningModel.RuleId,
                            Type = AreaRuleT2TypesEnum.Open,
                            ItemPlanningId = planningForType2TypeTankOpenId,
                            FolderId = folderId,
                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                            {
                                SiteId = x.SiteId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList(),
                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                            DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                        };
                        await areaRulePlanningForType2TypeTankOpen.Create(_backendConfigurationPnDbContext);

                        var planningForType2AlarmYesId = 0;
                        if (areaRule.Type is AreaRuleT2TypesEnum.Open or AreaRuleT2TypesEnum.Closed
                            && areaRule.Alarm is AreaRuleT2AlarmsEnum.Yes)
                        {
                            const string eformName = "03. Kontrol alarmanlæg gyllebeholder";
                            var eformId = await sdkDbContext.CheckListTranslations
                                .Where(x => x.Text == eformName)
                                .Select(x => x.CheckListId)
                                .FirstAsync();

                            if (areaRulePlanningModel.Status)
                            {
                                var planningForType2AlarmYes = new Planning
                                {
                                    CreatedByUserId = _userService.UserId,
                                    Enabled = areaRulePlanningModel.Status,
                                    RelatedEFormId = eformId,
                                    RelatedEFormName = eformName,
                                    SdkFolderName = areaRule.AreaRuleTranslations.Where(x => x.LanguageId == 1)
                                        .Select(x => x.Name).FirstOrDefault(),
                                    SdkFolderId = folderId,
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
                                            new()
                                            {
                                                LanguageId = 3, // ge
                                                Name = areaRule.AreaRuleTranslations
                                                    .Where(x => x.LanguageId == 3)
                                                    .Select(x => x.Name)
                                                    .FirstOrDefault() + " - Check alarm",
                                            },
                                            // new ()
                                            // {
                                            //     LanguageId = 4,// uk-ua
                                            //     Name = areaRule.AreaRuleTranslations
                                            //.Where(x => x.LanguageId == 4)
                                            //.Select(x => x.Name)
                                            //.FirstOrDefault() + "Перевірте сигналізацію",
                                            // },
                                        },
                                    RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                                    DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                    RepeatEvery = 1,
                                    RepeatType = RepeatType.Month,
                                    PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                        new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                            PlanningSite()
                                        {
                                            SiteId = x.SiteId,
                                        }).ToList(),
                                };
                                await planningForType2AlarmYes.Create(_itemsPlanningPnDbContext);
                                planningForType2AlarmYesId = planningForType2AlarmYes.Id;
                            }
                        }

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
                            ItemPlanningId = planningForType2AlarmYesId,
                            FolderId = folderId,
                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                            {
                                SiteId = x.SiteId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList(),
                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                            DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                        };
                        await areaRulePlanningForType2AlarmYes.Create(_backendConfigurationPnDbContext);

                        var planningForType2Id = 0;
                        if (areaRulePlanningModel.Status)
                        {
                            var planningForType2 = new Planning
                            {
                                CreatedByUserId = _userService.UserId,
                                Enabled = areaRulePlanningModel.Status,
                                RelatedEFormId = (int)areaRule.EformId,
                                RelatedEFormName = areaRule.EformName, // must be "03. Kontrol konstruktion"
                                SdkFolderName = areaRule.AreaRuleTranslations.Where(x => x.LanguageId == 1)
                                    .Select(x => x.Name).FirstOrDefault(), // name tank
                                SdkFolderId = folderId,
                                DaysBeforeRedeploymentPushMessageRepeat = false,
                                DaysBeforeRedeploymentPushMessage = 5,
                                PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                StartDate = areaRulePlanningModel.StartDate,
                                // create translations for planning from translation areaRule
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
                                        new()
                                        {
                                            LanguageId = 3, // ge
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 3)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + " - Konstruktion prüfen",
                                        },
                                        // new PlanningNameTranslation
                                        // {
                                        //     LanguageId = 4,// uk-ua
                                        //     Name = "Перевірте конструкцію",
                                        // },
                                    },
                                RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                                DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                RepeatEvery = 12,
                                RepeatType = RepeatType.Month,
                                PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                    new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite()
                                    {
                                        SiteId = x.SiteId,
                                    }).ToList(),
                            };
                            await planningForType2.Create(_itemsPlanningPnDbContext);
                            planningForType2Id = planningForType2.Id;
                        }

                        var areaRulePlanningForType2 = new AreaRulePlanning
                        {
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                            StartDate = areaRulePlanningModel.StartDate,
                            Status = areaRulePlanningModel.Status,
                            SendNotifications = areaRulePlanningModel.SendNotifications,
                            AreaRuleId = areaRulePlanningModel.RuleId,
                            Type = (AreaRuleT2TypesEnum)areaRule.Type,
                            Alarm = (AreaRuleT2AlarmsEnum)areaRule.Alarm,
                            ItemPlanningId = planningForType2Id,
                            FolderId = folderId,
                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                            {
                                SiteId = x.SiteId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList(),
                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                            DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                        };
                        await areaRulePlanningForType2.Create(_backendConfigurationPnDbContext);

                        break;
                    }
                case AreaTypesEnum.Type3: // stables and tail bite
                    {
                        var planningForType3ChecklistStableId = 0;
                        if (areaRule.ChecklistStable is true)
                        {
                            if (areaRulePlanningModel.Status)
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
                                    PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                        new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                            PlanningSite()
                                        {
                                            SiteId = x.SiteId,
                                        }).ToList(),
                                    DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                    DayOfWeek = (DayOfWeek)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                                };
                                if (areaRulePlanningModel.TypeSpecificFields?.RepeatType != null)
                                {
                                    planningForType3ChecklistStable.RepeatType =
                                        (RepeatType)areaRulePlanningModel.TypeSpecificFields.RepeatType;
                                }

                                if (areaRulePlanningModel.TypeSpecificFields?.RepeatEvery != null)
                                {
                                    planningForType3ChecklistStable.RepeatEvery =
                                        (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                }

                                await planningForType3ChecklistStable.Create(_itemsPlanningPnDbContext);
                                planningForType3ChecklistStableId = planningForType3ChecklistStable.Id;
                            }
                        }

                        var areaRulePlanningForType3ChecklistStable = new AreaRulePlanning
                        {
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                            StartDate = areaRulePlanningModel.StartDate,
                            Status = areaRulePlanningModel.Status,
                            SendNotifications = areaRulePlanningModel.SendNotifications,
                            AreaRuleId = areaRulePlanningModel.RuleId,
                            ItemPlanningId = planningForType3ChecklistStableId,
                            FolderId = areaRule.FolderId,
                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                            {
                                SiteId = x.SiteId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList(),
                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                            DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                        };

                        if (areaRulePlanningModel.TypeSpecificFields?.RepeatType != null)
                        {
                            areaRulePlanningForType3ChecklistStable.RepeatType =
                                areaRulePlanningModel.TypeSpecificFields.RepeatType;
                        }

                        if (areaRulePlanningModel.TypeSpecificFields?.RepeatEvery != null)
                        {
                            areaRulePlanningForType3ChecklistStable.RepeatEvery =
                                (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                        }

                        await areaRulePlanningForType3ChecklistStable.Create(_backendConfigurationPnDbContext);

                        var planningForType3TailBiteId = 0;
                        if (areaRule.TailBite is true)
                        {
                            const string eformName = "24. Halebid_NEW";
                            var eformId = await sdkDbContext.CheckListTranslations
                                .Where(x => x.Text == eformName)
                                .Select(x => x.CheckListId)
                                .FirstAsync();
                            if (areaRulePlanningModel.Status)
                            {
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
                                            new()
                                            {
                                                LanguageId = 3, // ge
                                                Name = "Schwanzbiss",
                                            },
                                            // new PlanningNameTranslation
                                            // {
                                            //     LanguageId = 4,// uk-ua
                                            //     Name = "Укус за хвіст",
                                            // },
                                        },
                                    PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                        new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                            PlanningSite
                                        {
                                            SiteId = x.SiteId,
                                        }).ToList(),
                                    DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                    DayOfWeek = (DayOfWeek)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                                };

                                if (areaRulePlanningModel.TypeSpecificFields?.RepeatType != null)
                                {
                                    planningForType3TailBite.RepeatType =
                                        (RepeatType)areaRulePlanningModel.TypeSpecificFields.RepeatType;
                                }

                                if (areaRulePlanningModel.TypeSpecificFields?.RepeatEvery != null)
                                {
                                    planningForType3TailBite.RepeatEvery =
                                        (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                                }

                                await planningForType3TailBite.Create(_itemsPlanningPnDbContext);
                                planningForType3TailBiteId = planningForType3TailBite.Id;
                            }
                        }

                        var areaRulePlanningForType3TailBite = new AreaRulePlanning
                        {
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                            StartDate = areaRulePlanningModel.StartDate,
                            Status = areaRulePlanningModel.Status,
                            SendNotifications = areaRulePlanningModel.SendNotifications,
                            AreaRuleId = areaRulePlanningModel.RuleId,
                            ItemPlanningId = planningForType3TailBiteId,
                            FolderId = areaRule.FolderId,
                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                            {
                                SiteId = x.SiteId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList(),
                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                            DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                        };

                        if (areaRulePlanningModel.TypeSpecificFields?.RepeatType != null)
                        {
                            areaRulePlanningForType3TailBite.RepeatType =
                                areaRulePlanningModel.TypeSpecificFields.RepeatType;
                        }

                        if (areaRulePlanningModel.TypeSpecificFields?.RepeatEvery != null)
                        {
                            areaRulePlanningForType3TailBite.RepeatEvery =
                                (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                        }

                        await areaRulePlanningForType3TailBite.Create(_backendConfigurationPnDbContext);

                        //if (areaRule.TailBite is null or false &&
                        //    areaRule.ChecklistStable is null or false)
                        //{
                        var areaRulePlanningForType3 = new AreaRulePlanning
                        {
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                            StartDate = areaRulePlanningModel.StartDate,
                            Status = areaRulePlanningModel.Status,
                            SendNotifications = areaRulePlanningModel.SendNotifications,
                            AreaRuleId = areaRulePlanningModel.RuleId,
                            FolderId = areaRule.FolderId,
                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                            {
                                SiteId = x.SiteId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList(),
                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                            DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                        };
                        if (areaRulePlanningModel.TypeSpecificFields?.RepeatType != null)
                        {
                            areaRulePlanningForType3.RepeatType =
                                areaRulePlanningModel.TypeSpecificFields.RepeatType;
                        }

                        if (areaRulePlanningModel.TypeSpecificFields?.RepeatEvery != null)
                        {
                            areaRulePlanningForType3.RepeatEvery =
                                (int)areaRulePlanningModel.TypeSpecificFields.RepeatEvery;
                        }

                        await areaRulePlanningForType3.Create(_backendConfigurationPnDbContext);
                        //}

                        break;
                    }
                case AreaTypesEnum.Type5: // recuring tasks(mon-sun)
                    {
                        var folderIds = await _backendConfigurationPnDbContext.ProperyAreaFolders
                            .Include(x => x.AreaProperty)
                            .Where(x => x.AreaProperty.PropertyId == areaRulePlanningModel.PropertyId)
                            .Where(x => x.AreaProperty.AreaId == areaRule.AreaId)
                            .Select(x => x.FolderId)
                            .Skip(1)
                            .ToListAsync();
                        var folderId = folderIds[areaRule.DayOfWeek];
                        var planningId = 0;
                        if (areaRulePlanningModel.Status)
                        {
                            var planning = new Planning
                            {
                                CreatedByUserId = _userService.UserId,
                                Enabled = areaRulePlanningModel.Status,
                                RelatedEFormId = (int)areaRule.EformId,
                                RelatedEFormName = areaRule.EformName,
                                SdkFolderName = areaRule.FolderName,
                                SdkFolderId = folderId,
                                DaysBeforeRedeploymentPushMessageRepeat = false,
                                DaysBeforeRedeploymentPushMessage = 5,
                                PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                StartDate = areaRulePlanningModel.StartDate,
                                DayOfWeek = (DayOfWeek?)areaRule.DayOfWeek,
                                // create translations for planning from translation areaRule
                                NameTranslations = areaRule.AreaRuleTranslations.Select(
                                    areaRuleAreaRuleTranslation => new PlanningNameTranslation
                                    {
                                        LanguageId = areaRuleAreaRuleTranslation.LanguageId,
                                        Name = areaRuleAreaRuleTranslation.Name,
                                    }).ToList(),
                                PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                    new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                                    {
                                        SiteId = x.SiteId,
                                    }).ToList(),
                                DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                            };
                            if (areaRulePlanningModel.TypeSpecificFields != null)
                            {
                                planning.RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate;
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
                            planningId = planning.Id;
                        }

                        var areaRulePlanning = new AreaRulePlanning
                        {
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                            StartDate = areaRulePlanningModel.StartDate,
                            Status = areaRulePlanningModel.Status,
                            SendNotifications = areaRulePlanningModel.SendNotifications,
                            AreaRuleId = areaRulePlanningModel.RuleId,
                            ItemPlanningId = planningId,
                            FolderId = folderId,
                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                            {
                                SiteId = x.SiteId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList(),
                            EndDate = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                            RepeatEvery = areaRulePlanningModel.TypeSpecificFields?.RepeatEvery,
                            RepeatType = areaRulePlanningModel.TypeSpecificFields?.RepeatType,
                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                            DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                        };

                        await areaRulePlanning.Create(_backendConfigurationPnDbContext);

                        break;
                    }
                case AreaTypesEnum.Type6: // head pumps
                    {
                        // create folder with name head pump
                        var translatesForFolder = areaRule.AreaRuleTranslations
                            .Select(x => new CommonTranslationsModel
                            {
                                LanguageId = x.LanguageId,
                                Description = "",
                                Name = x.Name,
                            }).ToList();
                        var folderId = await core.FolderCreate(translatesForFolder, areaRule.FolderId);

                        var planningForType6HoursAndEnergyEnabledId = 0;
                        var planningForType6IdOne = 0;
                        var planningForType6IdTwo = 0;
                        if (areaRulePlanningModel.TypeSpecificFields?.HoursAndEnergyEnabled is true &&
                            areaRulePlanningModel.Status)
                        {
                            await areaRule.Update(_backendConfigurationPnDbContext);
                            const string eformName = "10. Varmepumpe timer og energi";
                            var eformId = await sdkDbContext.CheckListTranslations
                                .Where(x => x.Text == eformName)
                                .Select(x => x.CheckListId)
                                .FirstAsync();
                            var planningForType6HoursAndEnergyEnabled = new Planning
                            {
                                CreatedByUserId = _userService.UserId,
                                Enabled = areaRulePlanningModel.Status,
                                RelatedEFormId = eformId,
                                RelatedEFormName = eformName,
                                SdkFolderName = areaRule.AreaRuleTranslations.Where(x => x.LanguageId == 1)
                                    .Select(x => x.Name).FirstOrDefault(),
                                SdkFolderId = folderId,
                                DaysBeforeRedeploymentPushMessageRepeat = false,
                                DaysBeforeRedeploymentPushMessage = 5,
                                PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                StartDate = areaRulePlanningModel.StartDate,
                                RepeatEvery = 12,
                                RepeatType = RepeatType.Month,
                                // create translations for planning from translation areaRule
                                NameTranslations = new List<PlanningNameTranslation>()
                                    {
                                        new()
                                        {
                                            LanguageId = 1, // da
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 1)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + ": Timer og energi",
                                        },
                                        new()
                                        {
                                            LanguageId = 2, // en
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 2)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + ": Hours and energy",
                                        },
                                        new()
                                        {
                                            LanguageId = 3, // ge
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 3)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + ": Stunden und Energie",
                                        },
                                    },
                                PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                    new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.
                                        PlanningSite
                                    {
                                        SiteId = x.SiteId,
                                    }).ToList(),
                                DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                DayOfWeek = (DayOfWeek)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                            };
                            await planningForType6HoursAndEnergyEnabled.Create(_itemsPlanningPnDbContext);
                            planningForType6HoursAndEnergyEnabledId = planningForType6HoursAndEnergyEnabled.Id;
                        }

                        if (areaRulePlanningModel.Status)
                        {
                            const string eformNameOne = "10. Varmepumpe serviceaftale";
                            var eformIdOne = await sdkDbContext.CheckListTranslations
                                .Where(x => x.Text == eformNameOne)
                                .Select(x => x.CheckListId)
                                .FirstAsync();
                            const string eformNameTwo = "10. Varmepumpe driftsstop";
                            var eformIdTwo = await sdkDbContext.CheckListTranslations
                                .Where(x => x.Text == eformNameTwo)
                                .Select(x => x.CheckListId)
                                .FirstAsync();
                            var planningForType6One = new Planning
                            {
                                CreatedByUserId = _userService.UserId,
                                Enabled = areaRulePlanningModel.Status,
                                RelatedEFormId = eformIdOne,
                                RelatedEFormName = eformNameOne,
                                SdkFolderName = areaRule.AreaRuleTranslations.Where(x => x.LanguageId == 1)
                                    .Select(x => x.Name).FirstOrDefault(), // name head pump
                                SdkFolderId = folderId,
                                DaysBeforeRedeploymentPushMessageRepeat = false,
                                DaysBeforeRedeploymentPushMessage = 5,
                                PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                StartDate = areaRulePlanningModel.StartDate,
                                // create translations for planning from translation areaRule
                                NameTranslations = new List<PlanningNameTranslation>()
                                    {
                                        new()
                                        {
                                            LanguageId = 1, // da
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 1)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + ": Service",
                                        },
                                        new()
                                        {
                                            LanguageId = 2, // en
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 2)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + ": Service",
                                        },
                                        new()
                                        {
                                            LanguageId = 3, // ge
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 3)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + ": Service",
                                        },
                                    },
                                RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                                RepeatEvery = 12,
                                RepeatType = RepeatType.Month,
                                PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                    new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite()
                                    {
                                        SiteId = x.SiteId,
                                    }).ToList(),
                                DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                DayOfWeek = (DayOfWeek)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                            };
                            var planningForType6Two = new Planning
                            {
                                CreatedByUserId = _userService.UserId,
                                Enabled = areaRulePlanningModel.Status,
                                RelatedEFormId = eformIdTwo,
                                RelatedEFormName = eformNameTwo,
                                SdkFolderName = areaRule.AreaRuleTranslations.Where(x => x.LanguageId == 1)
                                    .Select(x => x.Name).FirstOrDefault(), // name head pump
                                SdkFolderId = folderId,
                                DaysBeforeRedeploymentPushMessageRepeat = false,
                                DaysBeforeRedeploymentPushMessage = 5,
                                PushMessageOnDeployment = areaRulePlanningModel.SendNotifications,
                                StartDate = areaRulePlanningModel.StartDate,
                                // create translations for planning from translation areaRule
                                NameTranslations = new List<PlanningNameTranslation>()
                                    {
                                        new()
                                        {
                                            LanguageId = 1, // da
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 1)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + ": Logbog",
                                        },
                                        new()
                                        {
                                            LanguageId = 2, // en
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 2)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + ": Logbook",
                                        },
                                        new()
                                        {
                                            LanguageId = 3, // ge
                                            Name = areaRule.AreaRuleTranslations
                                                .Where(x => x.LanguageId == 3)
                                                .Select(x => x.Name)
                                                .FirstOrDefault() + ": Logbook",
                                        },
                                    },
                                RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                                RepeatEvery = 1,
                                RepeatType = RepeatType.Day,
                                PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                    new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite()
                                    {
                                        SiteId = x.SiteId,
                                    }).ToList(),
                                DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                                DayOfWeek = (DayOfWeek)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                            };
                            await planningForType6One.Create(_itemsPlanningPnDbContext);
                            planningForType6IdOne = planningForType6One.Id;
                            await planningForType6Two.Create(_itemsPlanningPnDbContext);
                            planningForType6IdTwo = planningForType6Two.Id;
                        }

                        var areaRulePlanningForType6HoursAndEnergyEnabled = new AreaRulePlanning
                        {
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                            StartDate = areaRulePlanningModel.StartDate,
                            Status = areaRulePlanningModel.Status,
                            SendNotifications = areaRulePlanningModel.SendNotifications,
                            AreaRuleId = areaRulePlanningModel.RuleId,
                            ItemPlanningId = planningForType6HoursAndEnergyEnabledId,
                            HoursAndEnergyEnabled = (bool)areaRulePlanningModel.TypeSpecificFields?.HoursAndEnergyEnabled,
                            FolderId = folderId,
                            EndDate = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                            RepeatEvery = areaRulePlanningModel.TypeSpecificFields?.RepeatEvery,
                            RepeatType = areaRulePlanningModel.TypeSpecificFields?.RepeatType,
                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                            {
                                SiteId = x.SiteId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList(),
                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                            DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                        };
                        var areaRulePlanningForType6One = new AreaRulePlanning
                        {
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                            StartDate = areaRulePlanningModel.StartDate,
                            Status = areaRulePlanningModel.Status,
                            SendNotifications = areaRulePlanningModel.SendNotifications,
                            HoursAndEnergyEnabled = (bool)areaRulePlanningModel.TypeSpecificFields?.HoursAndEnergyEnabled,
                            AreaRuleId = areaRulePlanningModel.RuleId,
                            Type = (AreaRuleT2TypesEnum)areaRule.Type,
                            Alarm = (AreaRuleT2AlarmsEnum)areaRule.Alarm,
                            ItemPlanningId = planningForType6IdOne,
                            FolderId = folderId,
                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                            {
                                SiteId = x.SiteId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList(),
                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                            DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                        };
                        var areaRulePlanningForType6Two = new AreaRulePlanning
                        {
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                            StartDate = areaRulePlanningModel.StartDate,
                            Status = areaRulePlanningModel.Status,
                            SendNotifications = areaRulePlanningModel.SendNotifications,
                            AreaRuleId = areaRulePlanningModel.RuleId,
                            Type = (AreaRuleT2TypesEnum)areaRule.Type,
                            Alarm = (AreaRuleT2AlarmsEnum)areaRule.Alarm,
                            ItemPlanningId = planningForType6IdTwo,
                            FolderId = folderId,
                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                            {
                                SiteId = x.SiteId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList(),
                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                            DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                        };
                        await areaRulePlanningForType6HoursAndEnergyEnabled.Create(
                            _backendConfigurationPnDbContext);
                        await areaRulePlanningForType6One.Create(_backendConfigurationPnDbContext);
                        await areaRulePlanningForType6Two.Create(_backendConfigurationPnDbContext);
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
                            if (folderId != 0)
                            {
                                areaRule.FolderId = folderId;
                                areaRule.FolderName = await sdkDbContext.FolderTranslations
                                    .Where(x => x.FolderId == folderId)
                                    .Where(x => x.LanguageId == 1) // danish
                                    .Select(x => x.Name)
                                    .FirstAsync();
                                await areaRule.Update(_backendConfigurationPnDbContext);
                            }
                        }

                        var planningId = 0;
                        if (areaRulePlanningModel.Status)
                        {
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
                                PlanningSites = areaRulePlanningModel.AssignedSites.Select(x =>
                                    new Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite
                                    {
                                        SiteId = x.SiteId,
                                    }).ToList(),
                            };
                            if (areaRulePlanningModel.TypeSpecificFields != null)
                            {
                                planning.DayOfMonth = (int)areaRulePlanningModel.TypeSpecificFields?.DayOfMonth;
                                planning.RepeatUntil = areaRulePlanningModel.TypeSpecificFields?.EndDate;
                                planning.DayOfWeek =
                                    (DayOfWeek?)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek;
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
                            planningId = planning.Id;
                        }

                        var areaRulePlanning = new AreaRulePlanning
                        {
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                            StartDate = areaRulePlanningModel.StartDate,
                            Status = areaRulePlanningModel.Status,
                            SendNotifications = areaRulePlanningModel.SendNotifications,
                            AreaRuleId = areaRulePlanningModel.RuleId,
                            ItemPlanningId = planningId,
                            FolderId = areaRule.FolderId,
                            EndDate = areaRulePlanningModel.TypeSpecificFields?.EndDate,
                            RepeatEvery = areaRulePlanningModel.TypeSpecificFields?.RepeatEvery,
                            RepeatType = areaRulePlanningModel.TypeSpecificFields?.RepeatType,
                            PlanningSites = areaRulePlanningModel.AssignedSites.Select(x => new PlanningSite
                            {
                                SiteId = x.SiteId,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList(),
                            DayOfMonth = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfMonth),
                            DayOfWeek = (int)(areaRulePlanningModel.TypeSpecificFields?.DayOfWeek)
                        };
                        if (areaRulePlanningModel.TypeSpecificFields?.DayOfWeek is not null)
                            areaRulePlanning.DayOfWeek =
                                (int)areaRulePlanningModel.TypeSpecificFields?.DayOfWeek;

                        await areaRulePlanning.Create(_backendConfigurationPnDbContext);
                        break;
                    }
            }
            return new OperationDataResult<AreaRuleModel>(true,
                _backendConfigurationLocalizationService.GetString("SuccessfullyCreatedPlanning"));
        }
    }
}