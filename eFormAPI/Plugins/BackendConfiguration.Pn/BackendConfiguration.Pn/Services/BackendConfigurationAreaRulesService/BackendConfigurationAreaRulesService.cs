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

using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackendConfiguration.Pn.Infrastructure.Models.Pools;
using BackendConfiguration.Pn.Infrastructure.Models.PropertyAreas;
using BackendConfiguration.Pn.Messages;
using BackendConfiguration.Pn.Services.RebusService;
using ChemicalsBase.Infrastructure;
using ChemicalsBase.Infrastructure.Data.Entities;
using Rebus.Bus;

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
        private readonly ChemicalsDbContext _chemicalsDbContext;
        private readonly IBus _bus;

        public BackendConfigurationAreaRulesService(
            IEFormCoreService coreHelper,
            IUserService userService,
            BackendConfigurationPnDbContext backendConfigurationPnDbContext,
            IBackendConfigurationLocalizationService backendConfigurationLocalizationService,
            ItemsPlanningPnDbContext itemsPlanningPnDbContext, ChemicalsDbContext chemicalsDbContext, IRebusService rebusService)
        {
            _coreHelper = coreHelper;
            _userService = userService;
            _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
            _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
            _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
            _chemicalsDbContext = chemicalsDbContext;
            _bus = rebusService.GetBus();
        }

        public async Task<OperationDataResult<List<AreaRuleSimpleModel>>> Index(int propertyAreaId)
        {
            try
            {
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var areaProperty = await _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == propertyAreaId)
                    .Select(x => new {x.AreaId, x.PropertyId, x.Area.Type, x.GroupMicrotingUuid})
                    .FirstAsync().ConfigureAwait(false);

                var currentUserLanguage = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);

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
                        SecondaryeFormId = x.SecondaryeFormId,
                        SecondaryeFormName = x.SecondaryeFormName,
                        TypeSpecificFields = new TypeSpecificField
                            {
                                EformId = x.EformId,
                                Type = x.Type ?? AreaRuleT2TypesEnum.Open,
                                Alarm = x.Alarm ?? AreaRuleT2AlarmsEnum.No,
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

                if (areaProperty.Type == AreaTypesEnum.Type9)
                {
                    await _bus.SendLocal(new ChemicalAreaCreated(areaProperty.PropertyId)).ConfigureAwait(false);
                }

                var areaRules = await queryWithSelect
                    .ToListAsync().ConfigureAwait(false);

                if (areaProperty.Type == AreaTypesEnum.Type10)
                {
                    if (!areaRules.Any(x => x.SecondaryeFormName == "Morgenrundtur"))
                    {
                        var areaRule = new AreaRule
                        {
                            AreaId = areaProperty.AreaId,
                            UpdatedByUserId = _userService.UserId,
                            CreatedByUserId = _userService.UserId,
                            EformId = null,
                            PropertyId = areaProperty.PropertyId,
                            SecondaryeFormId = 0,
                            SecondaryeFormName = "Morgenrundtur",
                            ComplianceEnabled = false,
                            ComplianceModifiable = false,
                            Notifications = false,
                            NotificationsModifiable = false
                        };

                        var property = await _backendConfigurationPnDbContext.Properties
                            .Where(x => x.Id == areaProperty.PropertyId)
                            .FirstAsync().ConfigureAwait(false);

                        areaRule.FolderId = await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = 1, // da
                                Name = "00. Morgenrundtur",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = 2, // en
                                Name = "00. Morning tour",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = 3, // ge
                                Name = "00. Morgentour",
                                Description = "",
                            },
                        }, property.FolderId).ConfigureAwait(false);
                        areaRule.FolderName = "00. Morgenrundtur";

                        await areaRule.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        var areaRuleTranslation = new AreaRuleTranslation
                        {
                            AreaRuleId = areaRule.Id,
                            LanguageId = 1,
                            Name = "Morgenrundtur",
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                        };
                        await areaRuleTranslation.Create(_backendConfigurationPnDbContext);
                        areaRuleTranslation = new AreaRuleTranslation
                        {
                            AreaRuleId = areaRule.Id,
                            LanguageId = 2,
                            Name = "Morning tour",
                            CreatedByUserId = _userService.UserId,
                            UpdatedByUserId = _userService.UserId,
                        };
                        await areaRuleTranslation.Create(_backendConfigurationPnDbContext);
                    }
                }

                foreach (var areaRule in areaRules)
                {
                    // add translate eform name
                    areaRule.EformName = await sdkDbContext.CheckListTranslations
                        .Where(x => x.CheckListId == areaRule.TypeSpecificFields.EformId)
                        .Where(x => x.LanguageId == currentUserLanguage.Id)
                        .Select(x => x.Text)
                        .FirstOrDefaultAsync().ConfigureAwait(false);
                    // add has plannings and initial fields
                    var areaRulePlannings = await _backendConfigurationPnDbContext.AreaRulePlannings
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.AreaRuleId == areaRule.Id)
                        .ToListAsync().ConfigureAwait(false);
                    areaRule.PlanningStatus = areaRulePlannings.Any(x => x.ItemPlanningId != 0);

                    if (areaRule.InitialFields != null && !string.IsNullOrEmpty(areaRule.InitialFields.EformName))
                    {
                        areaRule.InitialFields.EformId = await sdkDbContext.CheckListTranslations
                            .Where(x => x.Text == areaRule.InitialFields.EformName)
                            .Select(x => x.CheckListId)
                            .FirstOrDefaultAsync().ConfigureAwait(false);
                    }
                }

                if (areaProperty.AreaId == 1)
                {
                    var folderId = _backendConfigurationPnDbContext.AreaRules.First(x => x.Id == areaRules.First().Id).FolderId;
                    foreach (var areaRule in BackendConfigurationSeedAreas.AreaRules.Where(x => x.AreaId == 1))
                    {
                        var eformId = await sdkDbContext.CheckListTranslations
                            .Where(x => x.Text == areaRule.EformName)
                            .Select(x => x.CheckListId)
                            .FirstAsync().ConfigureAwait(false);
                        var dbAreaRule = await _backendConfigurationPnDbContext.AreaRules
                            .Where(x => x.PropertyId == areaProperty.PropertyId)
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.AreaId == areaRule.AreaId)
                            .FirstOrDefaultAsync(x => x.EformId == eformId);

                        if (dbAreaRule == null)
                        {
                            areaRule.PropertyId = areaProperty.PropertyId;
                            areaRule.FolderId = folderId;
                            areaRule.CreatedByUserId = _userService.UserId;
                            areaRule.UpdatedByUserId = _userService.UserId;
                            areaRule.ComplianceModifiable = true;
                            areaRule.NotificationsModifiable = true;
                            areaRule.EformId = eformId;
                            await areaRule.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        }
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
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var languages = await sdkDbContext.Languages.Where(x => x.IsActive == true).AsNoTracking().ToListAsync().ConfigureAwait(false);

                var areaId = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == ruleId)
                    .Select(x => new {x.AreaId})
                    .FirstAsync().ConfigureAwait(false);

                var area = await _backendConfigurationPnDbContext.Areas
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaId.AreaId).FirstAsync().ConfigureAwait(false);

                var areaRule = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == ruleId)
                    .Include(x => x.AreaRuleTranslations)
                    .Select(x => new AreaRuleModel
                    {
                        Id = x.Id,
                        EformName = x.EformName,
                        SecondaryeFormId = x.SecondaryeFormId,
                        SecondaryeFormName = x.SecondaryeFormName,
                        TranslatedNames = x.AreaRuleTranslations
                            .Select(y => new CommonDictionaryModel
                            {
                                Id = y.LanguageId,
                                Name = y.Name,
                                //Description = languages.First(z => z.Id == y.LanguageId).Name,
                            }).ToList(),
                        IsDefault = x.IsDefault,
                        TypeSpecificFields = new TypeSpecificFields()
                        {
                            Alarm = x.Alarm ?? AreaRuleT2AlarmsEnum.No,
                            DayOfWeek = x.DayOfWeek,
                            RepeatEvery = x.RepeatEvery,
                            Type = x.Type ?? AreaRuleT2TypesEnum.Open,
                            EformId = x.EformId
                        },
                        // {x.Type, x.Alarm, x.DayOfWeek, x.RepeatEvery, x.RepeatType},
                        EformId = x.EformId,
                    })
                    .FirstOrDefaultAsync().ConfigureAwait(false);


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

                if (area.Type == AreaTypesEnum.Type10)
                {
                    areaRule.TypeSpecificFields.PoolHoursModel = new PoolHoursModel
                    {
                        Parrings = await _backendConfigurationPnDbContext.PoolHours.Where(x => x.AreaRuleId == areaRule.Id).Select(y => new PoolHourModel()
                        {
                            IsActive = y.IsActive,
                            AreaRuleId = y.AreaRuleId,
                            DayOfWeek = (int)y.DayOfWeek - 1,
                            Index = y.Index

                        }).ToListAsync().ConfigureAwait(false)
                    };
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
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var areaId = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == updateModel.Id)
                    .Select(x => new {x.AreaId})
                    .FirstAsync().ConfigureAwait(false);

                var area = await _backendConfigurationPnDbContext.Areas
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaId.AreaId).FirstAsync().ConfigureAwait(false);

                var areaRule = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == updateModel.Id)
                    .Include(x => x.AreaRuleTranslations)
                    .FirstOrDefaultAsync().ConfigureAwait(false);

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
                    areaRule.EformId = updateModel.EformId;
                    // areaRule.EformId = sdkDbContext.CheckListTranslations
                    //     .Where(x => x.Text == updateModel.EformName)
                    //     .Select(x => x.CheckListId)
                    //     .First();
                    if (areaRule.SecondaryeFormName == "Morgenrundtur")
                    {
                        areaRule.SecondaryeFormId = (int)areaRule.EformId;
                    }
                }

                areaRule.UpdatedByUserId = _userService.UserId;
                if (updateModel.TypeSpecificFields != null)
                {
                    areaRule.Type = updateModel.TypeSpecificFields.Type;
                    areaRule.Alarm = updateModel.TypeSpecificFields.Alarm;
                    areaRule.DayOfWeek = (int)updateModel.TypeSpecificFields.DayOfWeek;
                    areaRule.RepeatEvery = (int)updateModel.TypeSpecificFields.RepeatEvery;
                }

                if (areaRule.GroupItemId != 0)
                {
                    await core.EntityItemDelete(areaRule.GroupItemId).ConfigureAwait(false);
                    areaRule.GroupItemId = 0;
                }

                await areaRule.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);

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
                        await newTranslate.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }
                    else
                    {
                        if (translateForUpdate.Name != updateModelTranslatedName.Name)
                        {
                            translateForUpdate.Name = updateModelTranslatedName.Name;
                            await translateForUpdate.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        }
                    }
                }
                if (area.Type is AreaTypesEnum.Type10)
                {
                    foreach (var poolHourModel in updateModel.TypeSpecificFields!.PoolHoursModel.Parrings)
                    {
                        var poolHour = await _backendConfigurationPnDbContext.PoolHours
                            .Where(x => x.AreaRuleId == updateModel.Id)
                            .Where(x => x.Index == poolHourModel.Index)
                            .Where(x => x.DayOfWeek == (DayOfWeekEnum)(poolHourModel.DayOfWeek + 1))
                            .SingleAsync().ConfigureAwait(false);
                        poolHour.IsActive = poolHourModel.IsActive;

                        // var poolHour = new PoolHour
                        // {
                        //     AreaRuleId = areaRule.Id,
                        //     DayOfWeek = (DayOfWeekEnum)poolHourModel.DayOfWeek,
                        //     Index = poolHourModel.Index,
                        //     IsActive = poolHourModel.IsActive,
                        //     CreatedByUserId = _userService.UserId,
                        //     UpdatedByUserId = _userService.UserId,
                        // };
                        await poolHour.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
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
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                await using var _ = sdkDbContext.ConfigureAwait(false);
                var areaRule = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaId)
                    .Include(x => x.Area)
                    .Include(x => x.AreaRuleTranslations)
                    .Include(x => x.AreaRulesPlannings)
                    .ThenInclude(x => x.PlanningSites)
                    .FirstOrDefaultAsync().ConfigureAwait(false);

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
                    await areaRuleAreaRuleTranslation.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }

                foreach (var areaRulePlanning in areaRule.AreaRulesPlannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    foreach (var planningSite in areaRulePlanning.PlanningSites
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        planningSite.UpdatedByUserId = _userService.UserId;
                        await planningSite.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }

                    if (areaRulePlanning.ItemPlanningId != 0)
                    {
                        var planning = await _itemsPlanningPnDbContext.Plannings
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.Id == areaRulePlanning.ItemPlanningId)
                            .Include(x => x.NameTranslations)
                            .Include(x => x.PlanningCases)
                            .Include(x => x.PlanningSites)
                            .FirstOrDefaultAsync().ConfigureAwait(false);
                        if (planning != null)
                        {
                            foreach (var translation in planning.NameTranslations
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                            {
                                translation.UpdatedByUserId = _userService.UserId;
                                await translation.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                            }

                            foreach (var planningSite in planning.PlanningSites
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                            {
                                planningSite.UpdatedByUserId = _userService.UserId;
                                await planningSite.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                            }

                            var planningCases = planning.PlanningCases
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .ToList();

                            foreach (var planningCase in planningCases)
                            {
                                var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                                    .Where(x => x.PlanningCaseId == planningCase.Id)
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .ToListAsync().ConfigureAwait(false);
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
                                        await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == planningCaseSite.MicrotingSdkCaseId).ConfigureAwait(false);
                                    if (result is {MicrotingUid: { }})
                                    {
                                        await core.CaseDelete((int)result.MicrotingUid).ConfigureAwait(false);
                                    }
                                    else
                                    {
                                        var clSites = await sdkDbContext.CheckListSites.SingleOrDefaultAsync(x =>
                                            x.Id == planningCaseSite.MicrotingCheckListSitId).ConfigureAwait(false);

                                        if (clSites != null)
                                        {
                                            await core.CaseDelete(clSites.MicrotingUid).ConfigureAwait(false);
                                        }
                                    }
                                }

                                // Delete planning case
                                await planningCase.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                            }

                            planning.UpdatedByUserId = _userService.UserId;
                            await planning.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                            var complianceList = await _backendConfigurationPnDbContext.Compliances.Where(x => x.PlanningId == planning.Id
                                && x.WorkflowState != Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);
                            foreach (var compliance in complianceList)
                            {
                                if (compliance != null)
                                {
                                    await compliance.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                }
                                var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId).ConfigureAwait(false);
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

                                await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                            }
                        }
                    }

                    areaRulePlanning.UpdatedByUserId = _userService.UserId;
                    await areaRulePlanning.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }

                areaRule.UpdatedByUserId = _userService.UserId;
                await areaRule.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);

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
                    .Select(x => new {x.Id, x.Area, x.GroupMicrotingUuid, x.PropertyId, x.ProperyAreaFolders})
                    .FirstAsync().ConfigureAwait(false);

                var property = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.Id == areaProperty.PropertyId)
                    .SingleAsync().ConfigureAwait(false);

                var core = await _coreHelper.GetCore().ConfigureAwait(false);
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
                    if (areaProperty.Area.Type is AreaTypesEnum.Type2 or AreaTypesEnum.Type6 or AreaTypesEnum.Type7 or AreaTypesEnum.Type8 or AreaTypesEnum.Type10)
                    {
                        var eformName = areaProperty.Area.Type switch
                        {
                            AreaTypesEnum.Type2 => "03. Kontrol konstruktion",
                            AreaTypesEnum.Type6 => "10. Varmepumpe serviceaftale",
                            AreaTypesEnum.Type7 => areaRuleType7.EformName,
                            AreaTypesEnum.Type8 => areaRuleType8.EformName,
                            AreaTypesEnum.Type10 => "01. Aflæsninger",
                            _ => ""
                        };
                        eformId = await sdkDbContext.CheckListTranslations
                            .Where(x => x.Text == eformName)
                            .Select(x => x.CheckListId)
                            .FirstAsync().ConfigureAwait(false);
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
                            .FirstAsync().ConfigureAwait(false);
                        areaRule.FolderId = parentFolderId;
                    }

                    if (areaRuleCreateModel.TypeSpecificFields != null)
                    {
                        areaRule.Type = areaRuleCreateModel.TypeSpecificFields.Type;
                        areaRule.DayOfWeek = areaRuleCreateModel.TypeSpecificFields.DayOfWeek ?? 0;
                        areaRule.Alarm = areaRuleCreateModel.TypeSpecificFields.Alarm;
                        areaRule.RepeatEvery = areaRuleCreateModel.TypeSpecificFields.RepeatEvery ?? 0;
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
                            .FirstAsync().ConfigureAwait(false);
                        areaRule.FolderId = folderId;
                        if (areaRuleType8.AreaRuleInitialField.RepeatEvery != null)
                        {
                            areaRule.RepeatEvery = (int) areaRuleType8.AreaRuleInitialField.RepeatEvery;
                        }
                        // areaRule.DayOfWeek = (int) areaRuleType8.AreaRuleInitialField.DayOfWeek;
                        areaRule.RepeatType = areaRuleType8.AreaRuleInitialField.RepeatType;
                        areaRule.ComplianceEnabled = areaRuleType8.AreaRuleInitialField.ComplianceEnabled;
                        areaRule.ComplianceModifiable = areaRuleType8.AreaRuleInitialField.ComplianceEnabled;
                        areaRule.Notifications = areaRuleType8.AreaRuleInitialField.Notifications;
                        areaRule.NotificationsModifiable = areaRuleType8.AreaRuleInitialField.Notifications;
                    }


                    var language = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
                    if (eformId != 0)
                    {
                        areaRule.EformName = await sdkDbContext.CheckListTranslations
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.CheckListId == eformId)
                            .Where(x => x.LanguageId == language.Id)
                            .Select(x => x.Text)
                            .FirstOrDefaultAsync().ConfigureAwait(false);
                    }

                    if (areaProperty.Area.Type != AreaTypesEnum.Type7 && areaProperty.Area.Type != AreaTypesEnum.Type8)
                    {
                        areaRule.FolderId = await _backendConfigurationPnDbContext.ProperyAreaFolders
                            .Include(x => x.AreaProperty)
                            .Where(x => x.AreaProperty.Id == createModel.PropertyAreaId)
                            .Select(x => x.FolderId)
                            .FirstOrDefaultAsync().ConfigureAwait(false);

                    }

                    areaRule.FolderName = await sdkDbContext.FolderTranslations
                        .Where(x => x.FolderId == areaRule.FolderId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync().ConfigureAwait(false);

                    await areaRule.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

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

                    if (areaProperty.Area.Type is AreaTypesEnum.Type10)
                    {
                        foreach (var poolHourModel in areaRuleCreateModel.TypeSpecificFields.PoolHoursModel.Parrings)
                        {
                            var poolHour = new PoolHour
                            {
                                AreaRuleId = areaRule.Id,
                                DayOfWeek = (DayOfWeekEnum)(poolHourModel.DayOfWeek + 1),
                                Index = poolHourModel.Index,
                                IsActive = poolHourModel.IsActive,
                                CreatedByUserId = _userService.UserId,
                                UpdatedByUserId = _userService.UserId,
                                Name = poolHourModel.Name
                            };
                            await poolHour.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        }

                        var folderId = await core.FolderCreate(
                            areaRuleCreateModel.TranslatedNames.Select(x =>
                            {
                                var model = new Microting.eForm.Infrastructure.Models.CommonTranslationsModel
                                {
                                    Name = x.Name,
                                    LanguageId = (int)x.Id,
                                    Description = ""
                                };
                                return model;
                            }).ToList(),
                            areaRule.FolderId).ConfigureAwait(false);

                        var folderIds = new List<int>
                        {
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "7. Søn",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "7. Sun",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "7. Son",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "1. Man",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "1. Mon",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "1. Mon",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "2. Tir",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "2. Tue",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "2. Die",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "3. Ons",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "3. Wed",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "3. Mit",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "4. Tor",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "4. Thu",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "4. Don",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "5. Fre",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "5. Fri",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "5. Fre",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "6. Lør",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "6. Sat",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "6. Sam",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                        };


                        foreach (var assignmentWithFolder in folderIds.Select(folderIdLocal => new ProperyAreaFolder
                        {
                            FolderId = folderIdLocal,
                            ProperyAreaAsignmentId = areaProperty.Id,
                        }))
                        {
                            await assignmentWithFolder.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        }
                    }

                    foreach (var translation in translations)
                    {
                        await translation.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
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
                var curentLanguage = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
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
                        .LastOrDefaultAsync().ConfigureAwait(false);
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
                var curentLanguage = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
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
                        .LastOrDefaultAsync().ConfigureAwait(false);
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
                    var core = await _coreHelper.GetCore().ConfigureAwait(false);
                    var sdkDbContext = core.DbContextHelper.GetDbContext();
                    await using var _ = sdkDbContext.ConfigureAwait(false);
                    var areaRule = await _backendConfigurationPnDbContext.AreaRules
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == areaRuleId)
                        .Include(x => x.Area)
                        .Include(x => x.AreaRuleTranslations)
                        .Include(x => x.AreaRulesPlannings)
                        .ThenInclude(x => x.PlanningSites)
                        .FirstOrDefaultAsync().ConfigureAwait(false);

                    if (areaRule == null)
                    {
                        continue; // todo maybe need throw
                    }

                    if (areaRule.Area.Type is AreaTypesEnum.Type3 && areaRule.GroupItemId != 0)
                    {
                        await core.EntityItemDelete(areaRule.GroupItemId).ConfigureAwait(false);
                    }

                    foreach (var areaRuleAreaRuleTranslation in areaRule.AreaRuleTranslations)
                    {
                        areaRuleAreaRuleTranslation.UpdatedByUserId = _userService.UserId;
                        await areaRuleAreaRuleTranslation.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }

                    foreach (var areaRulePlanning in areaRule.AreaRulesPlannings
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        foreach (var planningSite in areaRulePlanning.PlanningSites
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            planningSite.UpdatedByUserId = _userService.UserId;
                            await planningSite.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        }

                        if (areaRulePlanning.ItemPlanningId != 0)
                        {
                            var planning = await _itemsPlanningPnDbContext.Plannings
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Where(x => x.Id == areaRulePlanning.ItemPlanningId)
                                .Include(x => x.NameTranslations)
                                .Include(x => x.PlanningCases)
                                .Include(x => x.PlanningSites)
                                .FirstOrDefaultAsync().ConfigureAwait(false);
                            if (planning != null)
                            {
                                foreach (var translation in planning.NameTranslations
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                {
                                    translation.UpdatedByUserId = _userService.UserId;
                                    await translation.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                }

                                foreach (var planningSite in planning.PlanningSites
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                {
                                    planningSite.UpdatedByUserId = _userService.UserId;
                                    await planningSite.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                }

                                var planningCases = planning.PlanningCases
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .ToList();

                                foreach (var planningCase in planningCases)
                                {
                                    var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                                        .Where(x => x.PlanningCaseId == planningCase.Id)
                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                        .ToListAsync().ConfigureAwait(false);
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
                                            await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == planningCaseSite.MicrotingSdkCaseId).ConfigureAwait(false);
                                        if (result is {MicrotingUid: { }})
                                        {
                                            await core.CaseDelete((int)result.MicrotingUid).ConfigureAwait(false);
                                        }
                                        else
                                        {
                                            var clSites = await sdkDbContext.CheckListSites.SingleAsync(x =>
                                                x.Id == planningCaseSite.MicrotingCheckListSitId).ConfigureAwait(false);

                                            await core.CaseDelete(clSites.MicrotingUid).ConfigureAwait(false);
                                        }
                                    }

                                    // Delete planning case
                                    await planningCase.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                }

                                planning.UpdatedByUserId = _userService.UserId;
                                await planning.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                // if (!_itemsPlanningPnDbContext.PlanningSites.AsNoTracking().Any(x => x.PlanningId == planning.Id && x.WorkflowState != Constants.WorkflowStates.Removed))
                                // {
                                var complianceList = await _backendConfigurationPnDbContext.Compliances
                                    .Where(x => x.PlanningId == planning.Id).AsNoTracking().ToListAsync().ConfigureAwait(false);
                                foreach (var compliance in complianceList)
                                {
                                    var dbCompliacne =
                                        await _backendConfigurationPnDbContext.Compliances.SingleAsync(x => x.Id == compliance.Id).ConfigureAwait(false);
                                    await dbCompliacne.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                    var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId).ConfigureAwait(false);
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

                                    await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                                }
                                // }
                            }
                        }

                        areaRulePlanning.UpdatedByUserId = _userService.UserId;
                        await areaRulePlanning.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }

                    areaRule.UpdatedByUserId = _userService.UserId;
                    await areaRule.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
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