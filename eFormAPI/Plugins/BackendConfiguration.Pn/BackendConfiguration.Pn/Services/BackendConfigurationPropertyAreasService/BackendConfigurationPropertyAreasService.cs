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

using BackendConfiguration.Pn.Infrastructure.Helpers;

namespace BackendConfiguration.Pn.Services.BackendConfigurationPropertyAreasService;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfigurationLocalizationService;
using Infrastructure.Data.Seed.Data;
using Infrastructure.Models.PropertyAreas;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Dto;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data;

public class BackendConfigurationPropertyAreasService : IBackendConfigurationPropertyAreasService
{
    private readonly IEFormCoreService _coreHelper;
    private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
    private readonly IUserService _userService;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
    private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;

    public BackendConfigurationPropertyAreasService(
        IEFormCoreService coreHelper,
        IUserService userService,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        IBackendConfigurationLocalizationService backendConfigurationLocalizationService,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext)
    {
        _coreHelper = coreHelper;
        _userService = userService;
        _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
        _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
    }

    public async Task<OperationDataResult<List<PropertyAreaModel>>> Read(int propertyId)
    {
        try
        {
            var property = await _backendConfigurationPnDbContext.Properties.FirstAsync(x => x.Id == propertyId).ConfigureAwait(false);
            var propertyAreas = new List<PropertyAreaModel>();

            var propertyAreasQuery = _backendConfigurationPnDbContext.AreaProperties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyId == propertyId)
                .Include(x => x.Area)
                .ThenInclude(x => x.AreaRules)
                .ThenInclude(x => x.AreaRulesPlannings);

            var areas = _backendConfigurationPnDbContext.Areas
                .Include(x => x.AreaTranslations)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.IsFarm == property.IsFarm)
                .ToList();

            List<PropertyAreaModel> areasForAdd;
            var language = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
            if (propertyAreasQuery.Any())
            {
                propertyAreas = await propertyAreasQuery
                    .Select(x => new PropertyAreaModel
                    {
                        Id = x.Id,
                        Activated = x.Checked,
                        Description = x.Area.AreaTranslations.Where(y => y.LanguageId == language.Id)
                            .Select(y => y.Description).FirstOrDefault(),
                        Name = x.Area.AreaTranslations.Where(y => y.LanguageId == language.Id).Select(y => y.Name)
                            .FirstOrDefault(),
                        Status = x.Area.AreaRules
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(y => y.PropertyId == propertyId)
                            .SelectMany(y => y.AreaRulesPlannings)
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            .Any(y => y.Status),
                        AreaId = x.AreaId,
                        Type = x.Area.Type
                    })
                    .ToListAsync().ConfigureAwait(false);

                areasForAdd = areas.Where(x => !propertyAreasQuery.Any(y => y.AreaId == x.Id))
                    .Select(x => new PropertyAreaModel
                    {
                        Id = null,
                        Activated = false,
                        Description = x.AreaTranslations.Where(y => y.LanguageId == language.Id)
                            .Select(y => y.Description).FirstOrDefault(),
                        Name = x.AreaTranslations.Where(y => y.LanguageId == language.Id).Select(y => y.Name)
                            .FirstOrDefault(),
                        Status = false,
                        AreaId = x.Id,
                        Type = x.Type
                    })
                    .ToList();
            }
            else
            {
                areasForAdd = areas
                    .Select(x => new PropertyAreaModel
                    {
                        Id = null,
                        Activated = false,
                        Description = x.AreaTranslations.Where(y => y.LanguageId == language.Id)
                            .Select(y => y.Description).FirstOrDefault(),
                        Name = x.AreaTranslations.Where(y => y.LanguageId == language.Id).Select(y => y.Name)
                            .FirstOrDefault(),
                        Status = false,
                        AreaId = x.Id,
                        Type = x.Type
                    })
                    .ToList();
            }

            propertyAreas.AddRange(areasForAdd);

            propertyAreas = propertyAreas.OrderBy(x => x.Name).ToList();

            return new OperationDataResult<List<PropertyAreaModel>>(true, propertyAreas);
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationDataResult<List<PropertyAreaModel>>(false,
                $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadPropertyAreas")}: {e.Message}");
        }
    }

    public async Task<OperationResult> Update(PropertyAreasUpdateModel updateModel)
    {
        var core = await _coreHelper.GetCore().ConfigureAwait(false);

        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(updateModel, core, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext, _userService.UserId).ConfigureAwait(false);

        return new OperationResult(result.Success,
            _backendConfigurationLocalizationService.GetString(result.Message));
    }

    public async Task<OperationDataResult<AreaModel>> ReadAreaByPropertyAreaId(int propertyAreaId)
    {
        try
        {
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContex = core.DbContextHelper.GetDbContext();
            var areaProperties = await _backendConfigurationPnDbContext.AreaProperties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == propertyAreaId)
                .Include(x => x.Area)
                //.ThenInclude(x => x.AreaInitialField)
                .Include(x => x.Area.AreaTranslations)
                .Include(x => x.Property)
                .ThenInclude(x => x.SelectedLanguages)
                .Include(x => x.Property.PropertyWorkers)
                .Where(x => x.Area.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Property.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (areaProperties.Property.PropertyWorkers.All(
                    x => x.WorkflowState == Constants.WorkflowStates.Removed))
            {
                return new OperationDataResult<AreaModel>(false,
                    _backendConfigurationLocalizationService.GetString("NotFoundPropertyWorkerAssignments"));
            }

            if (areaProperties.Area == null)
            {
                return new OperationDataResult<AreaModel>(false,
                    _backendConfigurationLocalizationService.GetString("NotFoundArea"));
            }

            var sites = new List<SiteDto>();

            foreach (var worker in areaProperties.Property.PropertyWorkers
                         .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.WorkerId))
            {
                var site = await sdkDbContex.Sites
                    .Where(x => x.Id == worker)
                    .FirstAsync().ConfigureAwait(false);
                sites.Add(new SiteDto
                {
                    SiteId = worker,
                    SiteName = site.Name
                });
            }

            var languages = await sdkDbContex.Languages.Where(x => x.IsActive == true)
                .AsNoTracking().ToListAsync().ConfigureAwait(false);
            var currentUserLanguage = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);

            var areaModel = new AreaModel
            {
                Name = areaProperties.Area.AreaTranslations.Where(x => x.LanguageId == currentUserLanguage.Id)
                    .Select(x => x.Name).FirstOrDefault(),
                Id = areaProperties.AreaId,
                Languages = areaProperties.Property.SelectedLanguages
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => languages.Any(y => y.Id == x.LanguageId))
                    .Select(x => new CommonDictionaryModel
                    {
                        Id = x.LanguageId,
                        Name = languages.First(y => y.Id == x.LanguageId).Name
                    }).ToList(),
                AvailableWorkers = sites,
                Type = areaProperties.Area.Type,
                InitialFields = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField != null
                    ? new AreaInitialFields
                    {
                        Alarm = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.Alarm,
                        DayOfWeek = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.DayOfWeek,
                        EformName = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.EformName,
                        SendNotifications = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.Notifications,
                        RepeatType = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.RepeatType,
                        Type = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.Type,
                        RepeatEvery = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.RepeatEvery,
                        EndDate = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.EndDate,
                        ComplianceEnabled = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.ComplianceEnabled
                    }
                    : null,
                InfoBox = areaProperties.Area.AreaTranslations
                    .Where(x => x.LanguageId == currentUserLanguage.Id)
                    .Select(x => x.InfoBox)
                    .FirstOrDefault(),
                Placeholder = areaProperties.Area.AreaTranslations
                    .Where(x => x.LanguageId == currentUserLanguage.Id)
                    .Select(x => x.Placeholder)
                    .FirstOrDefault(),
                NewItemName = areaProperties.Area.AreaTranslations
                    .Where(x => x.LanguageId == currentUserLanguage.Id)
                    .Select(x => x.NewItemName)
                    .FirstOrDefault(),
                GroupId = areaProperties.GroupMicrotingUuid
            };

            if (areaModel.Type == AreaTypesEnum.Type9)
            {
                areaModel.GroupId = (int)areaProperties.Property.EntitySelectListChemicalAreas!;
            }
            if (areaModel.InitialFields != null && !string.IsNullOrEmpty(areaModel.InitialFields.EformName))
            {
                areaModel.InitialFields.EformId = await sdkDbContex.CheckListTranslations
                    .Where(x => x.Text == areaModel.InitialFields.EformName)
                    .Select(x => x.CheckListId)
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }

            return new OperationDataResult<AreaModel>(true, areaModel);
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationDataResult<AreaModel>(false,
                $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadArea")}: {e.Message}");
        }
    }

    public async Task<OperationDataResult<AreaModel>> ReadAreaByAreaRuleId(int areaRuleId)
    {
        try
        {
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContex = core.DbContextHelper.GetDbContext();

            var languages = await sdkDbContex.Languages.Where(x => x.IsActive == true)
                .Select(x => new { x.Id, x.Name }).ToListAsync().ConfigureAwait(false);
            var language = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);

            var areaRule = await _backendConfigurationPnDbContext.AreaRules
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == areaRuleId)
                .Include(x => x.Area)
                //.ThenInclude(x => x.AreaInitialField)
                .Include(x => x.Area.AreaTranslations)
                .Where(x => x.Area.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.Property)
                .ThenInclude(x => x.SelectedLanguages)
                .Include(x => x.Property.PropertyWorkers)
                .Where(x => x.Property.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (areaRule == null)
            {
                return new OperationDataResult<AreaModel>(false,
                    _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
            }
            var sites = new List<SiteDto>();

            foreach (var worker in areaRule.Property.PropertyWorkers
                         .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.WorkerId))
            {
                var site = await sdkDbContex.Sites
                    .Where(x => x.Id == worker)
                    .FirstAsync().ConfigureAwait(false);
                sites.Add(new SiteDto
                {
                    SiteId = worker,
                    SiteName = site.Name
                });
            }

            var areaModel = new AreaModel
            {
                Name = areaRule.Area.AreaTranslations.Where(x => x.LanguageId == language.Id)
                    .Select(x => x.Name).FirstOrDefault(),
                Id = areaRule.AreaId,
                Type = areaRule.Area.Type,
                Languages = areaRule.Property.SelectedLanguages
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => languages.Any(y => y.Id == x.LanguageId))
                    .Select(x => new CommonDictionaryModel
                    {
                        Id = x.LanguageId,
                        Name = languages.First(y => y.Id == x.LanguageId).Name
                    }).ToList(),
                AvailableWorkers = sites,
                InitialFields = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField != null
                    ? new AreaInitialFields
                    {
                        Alarm = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.Alarm,
                        DayOfWeek = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.DayOfWeek,
                        EformName = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.EformName,
                        SendNotifications = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.Notifications,
                        RepeatType = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.RepeatType,
                        Type = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.Type,
                        RepeatEvery = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.RepeatEvery,
                        EndDate = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.EndDate,
                        ComplianceEnabled = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.ComplianceEnabled
                    }
                    : null
            };
            if (areaModel.InitialFields != null && !string.IsNullOrEmpty(areaModel.InitialFields.EformName))
            {
                areaModel.InitialFields.EformId = await sdkDbContex.CheckListTranslations
                    .Where(x => x.Text == areaModel.InitialFields.EformName)
                    .Select(x => x.CheckListId)
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }

            return new OperationDataResult<AreaModel>(true, areaModel);
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationDataResult<AreaModel>(false,
                $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadArea")}: {e.Message}");
        }
    }

    public async Task<OperationResult> CreateEntityList(List<EntityItem> entityItemsListForCreate, int propertyAreaId)
    {
        try
        {
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var propertyArea = await _backendConfigurationPnDbContext.AreaProperties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == propertyAreaId)
                .Include(x => x.Area)
                .Include(x => x.Property)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (propertyArea == null)
            {
                return new OperationResult(false,
                    _backendConfigurationLocalizationService.GetString("AreaPropertyNotFound"));
            }
            var currentUserLanguage = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
            var groupCreate = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                $"{propertyArea.Property.Name} - {propertyArea.Area.AreaTranslations.Where(x => x.LanguageId == currentUserLanguage.Id).Select(x => x.Name).FirstOrDefault()}", "",
                true, true).ConfigureAwait(false);
            var entityGroup = await core.EntityGroupRead(groupCreate.MicrotingUid).ConfigureAwait(false);
            var nextItemUid = entityGroup.EntityGroupItemLst.Count;
            foreach (var entityItem in entityItemsListForCreate)
            {
                await core.EntitySelectItemCreate(entityGroup.Id, entityItem.Name, entityItem.DisplayIndex,
                    nextItemUid.ToString()).ConfigureAwait(false);
                nextItemUid++;
            }

            propertyArea.GroupMicrotingUuid = Convert.ToInt32(entityGroup.MicrotingUUID);
            await propertyArea.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);

            return new OperationResult(true);
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationResult(false,
                $"{_backendConfigurationLocalizationService.GetString("ErrorWhileCreateEntityList")}: {e.Message}");
        }
    }
}