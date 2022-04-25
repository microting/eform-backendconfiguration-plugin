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

namespace BackendConfiguration.Pn.Services.BackendConfigurationAreaRulesService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Infrastructure.Data.Seed.Data;
    using BackendConfigurationLocalizationService;
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

    public class BackendConfigurationAreaRulesService : IBackendConfigurationAreaRulesService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
        private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;

        public BackendConfigurationAreaRulesService(
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
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var areaProperty = await _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == propertyAreaId)
                    .Select(x => new {x.AreaId, x.PropertyId, x.Area.Type})
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

                var queryWithSelect = query
                    .Select(x => new AreaRuleSimpleModel
                    {
                        Id = x.Id,
                        // EformName = x.EformName,
                        TranslatedName = x.AreaRuleTranslations
                            .Where(y => y.LanguageId == currentUserLanguage.Id)
                            .Select(y => y.Name)
                            .FirstOrDefault(),
                        IsDefault = x.IsDefault,
                        TypeSpecificFields = new TypeSpecificField
                            {
                                EformId = x.EformId,
                                Type = x.Type,
                                Alarm = x.Alarm,
                                DayOfWeek = x.DayOfWeek,
                                RepeatEvery = x.RepeatEvery,
                                RepeatType = x.RepeatType,
                                ComplianceEnabled = x.ComplianceEnabled,
                                ComplianceModifiable = x.ComplianceModifiable,
                                Notifications = x.Notifications,
                                NotificationsModifiable = x.NotificationsModifiable,
                            },
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
                                ComplianceEnabled = x.AreaRuleInitialField.ComplianceEnabled,
                            }
                            : null,
                    });

                if (areaProperty.Type == AreaTypesEnum.Type7 || areaProperty.Type == AreaTypesEnum.Type8)
                {
                    queryWithSelect = queryWithSelect.OrderBy(x => x.TranslatedName);
                }

                var areaRules = await queryWithSelect
                    .ToListAsync();

                foreach (var areaRule in areaRules)
                {
                    // add translate eform name
                    areaRule.EformName = await sdkDbContext.CheckListTranslations
                        .Where(x => x.CheckListId == areaRule.TypeSpecificFields.EformId)
                        .Where(x => x.LanguageId == currentUserLanguage.Id)
                        .Select(x => x.Text)
                        .FirstOrDefaultAsync();
                    // add has plannings and initial fields
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
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileObtainingAreaRules")}: {ex.Message}");
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
                            {x.Type, x.Alarm, x.DayOfWeek, x.RepeatEvery, x.RepeatType},
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
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadAreaRule")}: {e.Message}");
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
                    areaRule.DayOfWeek = updateModel.TypeSpecificFields.DayOfWeek;
                    areaRule.RepeatEvery = updateModel.TypeSpecificFields.RepeatEvery;
                }

                if (areaRule.GroupItemId != 0)
                {
                    await core.EntityItemDelete(areaRule.GroupItemId);
                    areaRule.GroupItemId = 0;
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
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileUpdateAreaRule")}: {e.Message}");
            }
        }

        public async Task<OperationResult> Delete(int areaId)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                await using var sdkDbContext = core.DbContextHelper.GetDbContext();
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


                //if (areaRule.Area.Type is AreaTypesEnum.Type3 && areaRule.GroupItemId != 0)
                //{
                //    await core.EntityGroupDelete(areaRule.GroupItemId.ToString());
                //}

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
                            .Include(x => x.PlanningCases)
                            .Include(x => x.PlanningSites)
                            .FirstOrDefaultAsync();
                        if (planning != null)
                        {
                            foreach (var translation in planning.NameTranslations
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                            {
                                translation.UpdatedByUserId = _userService.UserId;
                                await translation.Delete(_itemsPlanningPnDbContext);
                            }

                            foreach (var planningSite in planning.PlanningSites
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                            {
                                planningSite.UpdatedByUserId = _userService.UserId;
                                await planningSite.Delete(_itemsPlanningPnDbContext);
                            }

                            var planningCases = planning.PlanningCases
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .ToList();

                            foreach (var planningCase in planningCases)
                            {
                                var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                                    .Where(x => x.PlanningCaseId == planningCase.Id)
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .ToListAsync();
                                // foreach (var planningCaseSite in planningCaseSites
                                //     .Where(planningCaseSite => planningCaseSite.MicrotingSdkCaseId != 0))
                                // {
                                //     var result = await sdkDbContext.Cases.SingleAsync(x =>
                                //         x.Id == planningCaseSite.MicrotingSdkCaseId);
                                //     if (result.MicrotingUid != null)
                                //     {
                                //         await core.CaseDelete((int) result.MicrotingUid);
                                //     }
                                // }
                                foreach (var planningCaseSite in planningCaseSites)
                                {
                                    var result =
                                        await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == planningCaseSite.MicrotingSdkCaseId);
                                    if (result is {MicrotingUid: { }})
                                    {
                                        await core.CaseDelete((int)result.MicrotingUid);
                                    }
                                    else
                                    {
                                        var clSites = await sdkDbContext.CheckListSites.SingleOrDefaultAsync(x =>
                                            x.Id == planningCaseSite.MicrotingCheckListSitId);

                                        if (clSites != null)
                                        {
                                            await core.CaseDelete(clSites.MicrotingUid);
                                        }
                                    }
                                }

                                // Delete planning case
                                await planningCase.Delete(_itemsPlanningPnDbContext);
                            }

                            planning.UpdatedByUserId = _userService.UserId;
                            await planning.Delete(_itemsPlanningPnDbContext);
                            var complianceList = await _backendConfigurationPnDbContext.Compliances.Where(x => x.PlanningId == planning.Id
                                && x.WorkflowState != Constants.WorkflowStates.Removed).ToListAsync();
                            foreach (var compliance in complianceList)
                            {
                                if (compliance != null)
                                {
                                    await compliance.Delete(_backendConfigurationPnDbContext);
                                }
                                var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId);
                                if (_backendConfigurationPnDbContext.Compliances.Any(x => x.PropertyId == property.Id && x.Deadline < DateTime.UtcNow && x.WorkflowState != Constants.WorkflowStates.Removed))
                                {
                                    property.ComplianceStatusThirty = 2;
                                    property.ComplianceStatus = 2;
                                }
                                else
                                {
                                    if (!_backendConfigurationPnDbContext.Compliances.Any(x =>
                                            x.PropertyId == property.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                                    {
                                        property.ComplianceStatusThirty = 0;
                                        property.ComplianceStatus = 0;
                                    }
                                }

                                await property.Update(_backendConfigurationPnDbContext);
                            }
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
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileDeleteAreaRule")}: {e.Message}");
            }
        }

        public async Task<OperationResult> Create(AreaRulesCreateModel createModel)
        {
            try
            {
                var areaProperty = await _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == createModel.PropertyAreaId)
                    .Select(x => new {x.Area, x.GroupMicrotingUuid, x.PropertyId, x.ProperyAreaFolders})
                    .FirstAsync();

                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();

                foreach (var areaRuleCreateModel in createModel.AreaRules)
                {
                    var areaRuleType7 = new AreaRule();
                    if (areaProperty.Area.Type is AreaTypesEnum.Type7)
                    {
                        areaRuleType7 = BackendConfigurationSeedAreas.AreaRulesForType7
                            .First(x => x.AreaRuleTranslations
                                .Select(y => y.Name)
                                .Contains(areaRuleCreateModel.TranslatedNames[0].Name));
                    }
                    var areaRuleType8 = new AreaRule();
                    if (areaProperty.Area.Type is AreaTypesEnum.Type8)
                    {
                        areaRuleType8 = BackendConfigurationSeedAreas.AreaRulesForType8
                            .First(x => x.AreaRuleTranslations
                                .Select(y => y.Name)
                                .Contains(areaRuleCreateModel.TranslatedNames[0].Name));
                    }
                    var eformId = areaRuleCreateModel.TypeSpecificFields.EformId;
                    if (areaProperty.Area.Type is AreaTypesEnum.Type2 or AreaTypesEnum.Type6 or AreaTypesEnum.Type7 or AreaTypesEnum.Type8)
                    {
                        var eformName = areaProperty.Area.Type switch
                        {
                            AreaTypesEnum.Type2 => "03. Kontrol konstruktion",
                            AreaTypesEnum.Type6 => "10. Varmepumpe serviceaftale",
                            AreaTypesEnum.Type7 => areaRuleType7.EformName,
                            AreaTypesEnum.Type8 => areaRuleType8.EformName,
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
                        EformId = eformId,
                        PropertyId = areaProperty.PropertyId,
                    };

                    if (areaProperty.Area.Type is AreaTypesEnum.Type7)
                    {
                        areaRule.IsDefault = areaRuleType7.IsDefault;
                        // create folder
                        var pairedFolderToPropertyArea = areaProperty.ProperyAreaFolders.Select(x => x.FolderId).ToList();
                        var parentFolderId = await sdkDbContext.FolderTranslations
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.LanguageId == 2) // en
                            .Where(x => pairedFolderToPropertyArea.Contains(x.FolderId))
                            .Where(x => x.Name == areaRuleType7.FolderName)
                            .Select(x => x.FolderId)
                            .FirstAsync();
                        areaRule.FolderId = parentFolderId;
                    }

                    if (areaRuleCreateModel.TypeSpecificFields != null)
                    {
                        areaRule.Type = areaRuleCreateModel.TypeSpecificFields.Type;
                        areaRule.DayOfWeek = areaRuleCreateModel.TypeSpecificFields.DayOfWeek;
                        areaRule.Alarm = areaRuleCreateModel.TypeSpecificFields.Alarm;
                        areaRule.RepeatEvery = areaRuleCreateModel.TypeSpecificFields.RepeatEvery;
                    }
                    areaRule.ComplianceEnabled = true;
                    areaRule.ComplianceModifiable = true;
                    areaRule.Notifications = true;
                    areaRule.NotificationsModifiable = true;

                    if (areaProperty.Area.Type is AreaTypesEnum.Type8)
                    {
                        areaRule.IsDefault = areaRuleType8.IsDefault;
                        // create folder
                        var pairedFolderToPropertyArea = areaProperty.ProperyAreaFolders.Select(x => x.FolderId).ToList();
                        var folderId = await sdkDbContext.FolderTranslations
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            // .Where(x => x.LanguageId == 2) // en
                            .Where(x => pairedFolderToPropertyArea.Contains(x.FolderId))
                            .Where(x => x.Name == areaRuleType8.FolderName)
                            .Select(x => x.FolderId)
                            .FirstAsync();
                        areaRule.FolderId = folderId;
                        areaRule.RepeatEvery = (int) areaRuleType8.AreaRuleInitialField.RepeatEvery;
                        // areaRule.DayOfWeek = (int) areaRuleType8.AreaRuleInitialField.DayOfWeek;
                        areaRule.RepeatType = areaRuleType8.AreaRuleInitialField.RepeatType;
                        areaRule.ComplianceEnabled = areaRuleType8.AreaRuleInitialField.ComplianceEnabled;
                        areaRule.ComplianceModifiable = false;
                        areaRule.Notifications = areaRuleType8.AreaRuleInitialField.Notifications;
                        areaRule.NotificationsModifiable = false;
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

                    if (areaProperty.Area.Type != AreaTypesEnum.Type7 && areaProperty.Area.Type != AreaTypesEnum.Type8)
                    {
                        areaRule.FolderId = await _backendConfigurationPnDbContext.ProperyAreaFolders
                            .Include(x => x.AreaProperty)
                            .Where(x => x.AreaProperty.Id == createModel.PropertyAreaId)
                            .Select(x => x.FolderId)
                            .FirstOrDefaultAsync();

                    }

                    areaRule.FolderName = await sdkDbContext.FolderTranslations
                        .Where(x => x.FolderId == areaRule.FolderId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync();

                    await areaRule.Create(_backendConfigurationPnDbContext);

                    var translations = new List<AreaRuleTranslation>();

                    if (areaProperty.Area.Type != AreaTypesEnum.Type7 && areaProperty.Area.Type != AreaTypesEnum.Type8)
                    {
                        translations = areaRuleCreateModel.TranslatedNames
                            .Select(x => new AreaRuleTranslation
                            {
                                AreaRuleId = areaRule.Id,
                                LanguageId = (int)x.Id,
                                Name = x.Name,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList();
                    }

                    if (areaProperty.Area.Type is AreaTypesEnum.Type7)
                    {
                        translations = areaRuleType7.AreaRuleTranslations
                            .Select(x => new AreaRuleTranslation
                            {
                                AreaRuleId = areaRule.Id,
                                LanguageId = x.LanguageId,
                                Name = x.Name,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList();
                    }

                    if (areaProperty.Area.Type is AreaTypesEnum.Type8)
                    {
                        translations = areaRuleType8.AreaRuleTranslations
                            .Select(x => new AreaRuleTranslation
                            {
                                AreaRuleId = areaRule.Id,
                                LanguageId = x.LanguageId,
                                Name = x.Name,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                            }).ToList();
                    }

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
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileCreateAreaRule")}: {e.Message}");
            }
        }

        public async Task<OperationDataResult<List<AreaRulesForType7>>> GetAreaRulesForType7()
        {
            try
            {
                var curentLanguage = await _userService.GetCurrentUserLanguage();
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var model = BackendConfigurationSeedAreas.AreaRulesForType7
                    .GroupBy(x => x.FolderName)
                    .Select(x => new AreaRulesForType7
                    {
                        FolderName = x.Key,
                        AreaRuleNames = x.Select(y => y)
                            .Where(y => y.FolderName == x.Key)
                            .SelectMany(y => y.AreaRuleTranslations
                                .Where(z => z.LanguageId == curentLanguage.Id)
                                .Select(z => z.Name))
                            .ToList(),
                    })
                    .ToList();

                foreach (var areaRulesForType7 in model)
                {
                    areaRulesForType7.FolderName = await sdkDbContext.FolderTranslations
                        .OrderBy(x => x.Id)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Name == areaRulesForType7.FolderName)
                        .SelectMany(x => x.Folder.FolderTranslations)
                        .Where(x => x.LanguageId == curentLanguage.Id)
                        .Select(x => x.Name)
                        .LastOrDefaultAsync();
                }

                return new OperationDataResult<List<AreaRulesForType7>>(true, model);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<List<AreaRulesForType7>>(false);
            }
        }
        public async Task<OperationDataResult<List<AreaRulesForType8>>> GetAreaRulesForType8()
        {
            try
            {
                var curentLanguage = await _userService.GetCurrentUserLanguage();
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var model = BackendConfigurationSeedAreas.AreaRulesForType8
                    .GroupBy(x => x.FolderName)
                    .Select(x => new AreaRulesForType8
                    {
                        FolderName = x.Key,
                        AreaRuleNames = x.Select(y => y)
                            .Where(y => y.FolderName == x.Key)
                            .SelectMany(y => y.AreaRuleTranslations
                                .Where(z => z.LanguageId == curentLanguage.Id)
                                .Select(z => z.Name))
                            .ToList(),
                    })
                    .ToList();

                foreach (var areaRulesForType8 in model)
                {
                    areaRulesForType8.FolderName = await sdkDbContext.FolderTranslations
                        .OrderBy(x => x.Id)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Name == areaRulesForType8.FolderName)
                        .SelectMany(x => x.Folder.FolderTranslations)
                        .Where(x => x.LanguageId == curentLanguage.Id)
                        .Select(x => x.Name)
                        .LastOrDefaultAsync();
                }

                return new OperationDataResult<List<AreaRulesForType8>>(true, model);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<List<AreaRulesForType8>>(false);
            }
        }

        public async Task<OperationResult> Delete(List<int> areaRuleIds)
        {
            try
            {
                foreach (var areaRuleId in areaRuleIds)
                {
                    var core = await _coreHelper.GetCore();
                    await using var sdkDbContext = core.DbContextHelper.GetDbContext();
                    var areaRule = await _backendConfigurationPnDbContext.AreaRules
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == areaRuleId)
                        .Include(x => x.Area)
                        .Include(x => x.AreaRuleTranslations)
                        .Include(x => x.AreaRulesPlannings)
                        .ThenInclude(x => x.PlanningSites)
                        .FirstOrDefaultAsync();

                    if (areaRule == null)
                    {
                        continue; // todo maybe need throw
                    }

                    if (areaRule.Area.Type is AreaTypesEnum.Type3 && areaRule.GroupItemId != 0)
                    {
                        await core.EntityItemDelete(areaRule.GroupItemId);
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
                                .Include(x => x.PlanningCases)
                                .Include(x => x.PlanningSites)
                                .FirstOrDefaultAsync();
                            if (planning != null)
                            {
                                foreach (var translation in planning.NameTranslations
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                {
                                    translation.UpdatedByUserId = _userService.UserId;
                                    await translation.Delete(_itemsPlanningPnDbContext);
                                }

                                foreach (var planningSite in planning.PlanningSites
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                {
                                    planningSite.UpdatedByUserId = _userService.UserId;
                                    await planningSite.Delete(_itemsPlanningPnDbContext);
                                }

                                var planningCases = planning.PlanningCases
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .ToList();

                                foreach (var planningCase in planningCases)
                                {
                                    var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                                        .Where(x => x.PlanningCaseId == planningCase.Id)
                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                        .ToListAsync();
                                    // foreach (var planningCaseSite in planningCaseSites
                                    //     .Where(planningCaseSite => planningCaseSite.MicrotingSdkCaseId != 0))
                                    // {
                                    //     var result = await sdkDbContext.Cases.SingleAsync(x =>
                                    //         x.Id == planningCaseSite.MicrotingSdkCaseId);
                                    //     if (result.MicrotingUid != null)
                                    //     {
                                    //         await core.CaseDelete((int)result.MicrotingUid);
                                    //     }
                                    // }

                                    foreach (var planningCaseSite in planningCaseSites)
                                    {
                                        var result =
                                            await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == planningCaseSite.MicrotingSdkCaseId);
                                        if (result is {MicrotingUid: { }})
                                        {
                                            await core.CaseDelete((int)result.MicrotingUid);
                                        }
                                        else
                                        {
                                            var clSites = await sdkDbContext.CheckListSites.SingleAsync(x =>
                                                x.Id == planningCaseSite.MicrotingCheckListSitId);

                                            await core.CaseDelete(clSites.MicrotingUid);
                                        }
                                    }

                                    // Delete planning case
                                    await planningCase.Delete(_itemsPlanningPnDbContext);
                                }

                                planning.UpdatedByUserId = _userService.UserId;
                                await planning.Delete(_itemsPlanningPnDbContext);
                                // if (!_itemsPlanningPnDbContext.PlanningSites.AsNoTracking().Any(x => x.PlanningId == planning.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                                // {
                                var complianceList = await _backendConfigurationPnDbContext.Compliances
                                    .Where(x => x.PlanningId == planning.Id).AsNoTracking().ToListAsync();
                                foreach (var compliance in complianceList)
                                {
                                    var dbCompliacne =
                                        await _backendConfigurationPnDbContext.Compliances.SingleAsync(x => x.Id == compliance.Id);
                                    await dbCompliacne.Delete(_backendConfigurationPnDbContext);
                                    var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId);
                                    if (_backendConfigurationPnDbContext.Compliances.Any(x => x.PropertyId == property.Id && x.Deadline < DateTime.UtcNow && x.WorkflowState != Constants.WorkflowStates.Removed))
                                    {
                                        property.ComplianceStatusThirty = 2;
                                        property.ComplianceStatus = 2;
                                    }
                                    else
                                    {
                                        if (!_backendConfigurationPnDbContext.Compliances.Any(x =>
                                                x.PropertyId == property.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                                        {
                                            property.ComplianceStatusThirty = 0;
                                            property.ComplianceStatus = 0;
                                        }
                                    }

                                    await property.Update(_backendConfigurationPnDbContext);
                                }
                                // }
                            }
                        }

                        areaRulePlanning.UpdatedByUserId = _userService.UserId;
                        await areaRulePlanning.Delete(_backendConfigurationPnDbContext);
                    }

                    areaRule.UpdatedByUserId = _userService.UserId;
                    await areaRule.Delete(_backendConfigurationPnDbContext);
                }
                return new OperationDataResult<AreaRuleModel>(true,
                _backendConfigurationLocalizationService.GetString("SuccessfullyDeletedAreaRules"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRuleModel>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileDeleteAreaRules")}: {e.Message}");
            }
        }
    }
}