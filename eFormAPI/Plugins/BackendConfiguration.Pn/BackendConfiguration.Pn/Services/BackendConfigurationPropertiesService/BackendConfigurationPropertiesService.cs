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

#nullable enable
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using BackendConfiguration.Pn.Services.RebusService;
using JetBrains.Annotations;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Helpers.PluginDbOptions;
using Rebus.Bus;
using Sentry;

namespace BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfigurationLocalizationService;
using Infrastructure.Models.Properties;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;

public class BackendConfigurationPropertiesService : IBackendConfigurationPropertiesService
{
    private readonly IEFormCoreService _coreHelper;
    private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
    private readonly IUserService _userService;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
    private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;
    private readonly IPluginDbOptions<BackendConfigurationBaseSettings> _options;
    private readonly IBus _bus;

    public BackendConfigurationPropertiesService(
        IEFormCoreService coreHelper,
        IUserService userService,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        IBackendConfigurationLocalizationService backendConfigurationLocalizationService,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        IPluginDbOptions<BackendConfigurationBaseSettings> options,
        IRebusService rebusService)
    {
        _coreHelper = coreHelper;
        _userService = userService;
        _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
        _options = options;
        _bus = rebusService.GetBus();
    }

    public async Task<OperationDataResult<Paged<PropertiesModel>>> Index(PropertiesRequestModel request)
    {
        try
        {
            var query = _backendConfigurationPnDbContext.Properties
                .Include(x => x.SelectedLanguages)
                .Include(x => x.PropertyWorkers)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            var nameFields = new List<string> { "Name", "CHR", "Address", "CVR" };
            query = QueryHelper.AddFilterAndSortToQuery(query, request, nameFields);

            // get total
            var total = await query.Select(x => x.Id).CountAsync().ConfigureAwait(false);

            var properties = new List<PropertiesModel>();

            if (total > 0)
            {
                // pagination
                //propertiesQuery = propertiesQuery
                //    .Skip(request.Offset)
                //    .Take(request.PageSize);

                // add select to query and get from db
                properties = await query
                    .Select(x => new PropertiesModel
                    {
                        Id = x.Id,
                        Address = x.Address,
                        Chr = x.CHR,
                        Name = x.Name,
                        Cvr = x.CVR,
                        Languages = x.SelectedLanguages
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(y => new CommonDictionaryModel { Id = y.LanguageId })
                            .ToList(),
                        IsWorkersAssigned =
                            x.PropertyWorkers.Any(y => y.WorkflowState != Constants.WorkflowStates.Removed),
                        ComplianceStatus = x.ComplianceStatus,
                        ComplianceStatusThirty = x.ComplianceStatusThirty,
                        WorkorderEnable = x.WorkorderEnable,
                        WorkorderEntityListId = x.EntitySelectListAreas,
                        IsFarm = x.IsFarm,
                        IndustryCode = x.IndustryCode,
                        MainMailAddress = x.MainMailAddress
                    }).ToListAsync().ConfigureAwait(false);
            }

            return new OperationDataResult<Paged<PropertiesModel>>(true,
                new Paged<PropertiesModel> { Entities = properties, Total = total });
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationDataResult<Paged<PropertiesModel>>(false,
                $"{_backendConfigurationLocalizationService.GetString("ErrorWhileObtainingProperties")}: {ex.Message}");
        }
    }

    public async Task<OperationResult> Create(PropertyCreateModel propertyCreateModel)
    {
        var maxCvrNumbers = _options.Value.MaxCvrNumbers;
        var maxChrNumbers = _options.Value.MaxChrNumbers;

        var result = await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel,
            await _coreHelper.GetCore(), _userService.UserId, _backendConfigurationPnDbContext,
            _itemsPlanningPnDbContext, maxChrNumbers, maxCvrNumbers).ConfigureAwait(false);

        return new OperationResult(result.Success,
            _backendConfigurationLocalizationService.GetString(result.Message));
    }

    public async Task<OperationDataResult<PropertiesModel>> Read(int id)
    {
        try
        {
            var property = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == id)
                .Include(x => x.SelectedLanguages)
                .Select(x => new PropertiesModel
                {
                    Id = x.Id,
                    Address = x.Address,
                    Chr = x.CHR,
                    Cvr = x.CVR,
                    Name = x.Name,
                    IsFarm = x.IsFarm,
                    IndustryCode = x.IndustryCode,
                    MainMailAddress = x.MainMailAddress,
                    Languages = x.SelectedLanguages
                        .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(y => new CommonDictionaryModel { Id = y.LanguageId })
                        .ToList(),
                    WorkorderEnable = x.WorkorderEnable
                })
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (property == null)
            {
                return new OperationDataResult<PropertiesModel>(false,
                    _backendConfigurationLocalizationService.GetString("PropertyNotFound"));
            }

            return new OperationDataResult<PropertiesModel>(true, property);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationDataResult<PropertiesModel>(false,
                $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadProperty")}: {e.Message}");
        }
    }

    public async Task<OperationResult> Update(PropertiesUpdateModel updateModel)
    {
        var result = await BackendConfigurationPropertiesServiceHelper.Update(updateModel,
            await _coreHelper.GetCore(), _userService, _backendConfigurationPnDbContext,
            _itemsPlanningPnDbContext, _backendConfigurationLocalizationService, _bus).ConfigureAwait(false);

        return new OperationResult(result.Success,
            _backendConfigurationLocalizationService.GetString(result.Message));
    }

    public async Task<OperationResult> Delete(int id)
    {
        try
        {
            // find property and all links
            var property = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.Id == id)
                .Include(x => x.SelectedLanguages)
                .Include(x => x.SelectedLanguages)
                .Include(x => x.PropertyWorkers)
                .Include(x => x.AreaProperties)
                .ThenInclude(x => x.ProperyAreaFolders)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (property == null)
            {
                return new OperationResult(false,
                    _backendConfigurationLocalizationService.GetString("PropertyNotFound"));
            }

            // delete item planning tag
            var planningTag = await _itemsPlanningPnDbContext.PlanningTags
                .Where(x => x.Id == property.ItemPlanningTagId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (planningTag != null)
            {
                planningTag.UpdatedByUserId = _userService.UserId;
                await planningTag.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            // retract eforms
            if (property.WorkorderEnable)
            {
                /*var eformIdForNewTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "01. New task")
                    .Select(x => x.CheckListId)
                    .FirstAsync();

                var eformIdForOngoingTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "02. Ongoing task")
                    .Select(x => x.CheckListId)
                    .FirstAsync();

                var eformIdForCompletedTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "03. Completed task")
                    .Select(x => x.CheckListId)
                    .FirstAsync();*/

                await WorkOrderHelper.RetractEform(property.PropertyWorkers, true, core, _userService.UserId,
                    _backendConfigurationPnDbContext).ConfigureAwait(false);
                await WorkOrderHelper.RetractEform(property.PropertyWorkers, false, core, _userService.UserId,
                    _backendConfigurationPnDbContext).ConfigureAwait(false);
                await WorkOrderHelper.RetractEform(property.PropertyWorkers, false, core, _userService.UserId,
                    _backendConfigurationPnDbContext).ConfigureAwait(false);
            }

            // delete property workers
            foreach (var propertyWorker in property.PropertyWorkers)
            {
                propertyWorker.UpdatedByUserId = _userService.UserId;
                await propertyWorker.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
            }

            // delete area properties
            foreach (var areaProperty in property.AreaProperties)
            {
                // delete area property folders
                foreach (var properyAreaFolder in areaProperty.ProperyAreaFolders)
                {
                    var folder = await sdkDbContext.Folders
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == properyAreaFolder.FolderId)
                        .FirstOrDefaultAsync().ConfigureAwait(false);
                    if (folder != null) // if folder is not deleted
                    {
                        await folder.Delete(sdkDbContext).ConfigureAwait(false);
                    }
                }

                if (areaProperty.GroupMicrotingUuid != 0)
                {
                    await core.EntityGroupDelete(areaProperty.GroupMicrotingUuid.ToString()).ConfigureAwait(false);
                }

                // get areaRules and select all linked entity for delete
                var areaRules = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.PropertyId == areaProperty.PropertyId)
                    .Where(x => x.AreaId == areaProperty.AreaId)
                    .Include(x => x.Area)
                    .Include(x => x.AreaRuleTranslations)
                    .Include(x => x.AreaRulesPlannings)
                    .ThenInclude(x => x.PlanningSites)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync().ConfigureAwait(false);

                foreach (var areaRule in areaRules)
                {
                    if (areaRule.Area.Type is AreaTypesEnum.Type3 && areaRule.GroupItemId != 0)
                    {
                        // delete item from selectable list
                        var entityGroupItem = await sdkDbContext.EntityItems
                            .Where(x => x.Id == areaRule.GroupItemId).FirstOrDefaultAsync().ConfigureAwait(false);
                        if (entityGroupItem != null)
                        {
                            await entityGroupItem.Delete(sdkDbContext).ConfigureAwait(false);
                        }
                    }

                    string eformName = $"05. Halebid og risikovurdering - {property.Name}";
                    var eForm = await sdkDbContext.CheckListTranslations
                        .Where(x => x.Text == eformName)
                        .FirstOrDefaultAsync().ConfigureAwait(false);
                    if (eForm != null)
                    {
                        foreach (CheckListSite checkListSite in sdkDbContext.CheckListSites.Where(x =>
                                     x.CheckListId == eForm.CheckListId))
                        {
                            await core.CaseDelete(checkListSite.MicrotingUid).ConfigureAwait(false);
                        }
                    }

                    // delete translations for are rules
                    foreach (var areaRuleAreaRuleTranslation in areaRule.AreaRuleTranslations.Where(x =>
                                 x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        areaRuleAreaRuleTranslation.UpdatedByUserId = _userService.UserId;
                        await areaRuleAreaRuleTranslation.Delete(_backendConfigurationPnDbContext)
                            .ConfigureAwait(false);
                    }

                    // delete plannings area rules and items planning
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
                                .FirstOrDefaultAsync().ConfigureAwait(false);
                            if (planning != null)
                            {
                                foreach (var translation in planning.NameTranslations
                                             .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                {
                                    translation.UpdatedByUserId = _userService.UserId;
                                    await translation.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                }

                                planning.UpdatedByUserId = _userService.UserId;
                                await planning.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);

                                var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                                    .Where(x => x.PlanningId == planning.Id)
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .ToListAsync().ConfigureAwait(false);
                                foreach (PlanningCaseSite planningCaseSite in planningCaseSites)
                                {
                                    planningCaseSite.UpdatedByUserId = _userService.UserId;
                                    await planningCaseSite.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                    if (planningCaseSite.MicrotingSdkCaseId != 0)
                                    {
                                        var result =
                                            await sdkDbContext.Cases.SingleOrDefaultAsync(x =>
                                                x.Id == planningCaseSite.MicrotingSdkCaseId).ConfigureAwait(false);
                                        if (result is { MicrotingUid: { } })
                                        {
                                            await core.CaseDelete((int)result.MicrotingUid).ConfigureAwait(false);
                                        }
                                    }
                                    else
                                    {
                                        var result = await sdkDbContext.CheckListSites.SingleOrDefaultAsync(x =>
                                            x.Id == planningCaseSite.MicrotingCheckListSitId).ConfigureAwait(false);
                                        if (result != null)
                                        {
                                            await core.CaseDelete(result.MicrotingUid).ConfigureAwait(false);
                                        }
                                    }
                                }
                            }
                        }

                        areaRulePlanning.UpdatedByUserId = _userService.UserId;
                        await areaRulePlanning.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }

                    // delete area rule
                    areaRule.UpdatedByUserId = _userService.UserId;
                    await areaRule.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }

                // delete entity select group. only for type 3(tail bite and stables)
                if (areaProperty.GroupMicrotingUuid != 0)
                {
                    await core.EntityGroupDelete(areaProperty.GroupMicrotingUuid.ToString()).ConfigureAwait(false);
                }

                // delete entity search group. only for type 10 Pool inspections
                if (property.EntitySearchListPoolWorkers != null)
                {
                    await core.EntityGroupDelete(property.EntitySearchListPoolWorkers.ToString())
                        .ConfigureAwait(false);
                }

                areaProperty.UpdatedByUserId = _userService.UserId;
                await areaProperty.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
            }

            // delete selected languages
            foreach (var selectedLanguage in property.SelectedLanguages)
            {
                selectedLanguage.UpdatedByUserId = _userService.UserId;
                await selectedLanguage.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
            }

            // delete property folder
            var propertyFolder = await sdkDbContext.Folders
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == property.FolderId)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (propertyFolder != null) // if folder is not deleted
            {
                await core.FolderDelete(propertyFolder.Id);
            }

            // delete property folder for tasks
            var propertyFolderForTasks = await sdkDbContext.Folders
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == property.FolderIdForTasks)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (propertyFolderForTasks != null) // if folder is not created
            {
                await core.FolderDelete(propertyFolderForTasks.Id);
            }

            // delete linked files
            var propertyFiles = await _backendConfigurationPnDbContext.PropertyFiles
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyId == property.Id)
                .Include(x => x.File)
                .ThenInclude(x => x.FileTags)
                .ToListAsync();
            foreach (var propertyFile in propertyFiles)
            {
                // delete tags linked to file
                foreach (var fileFileTag in propertyFile.File.FileTags)
                {
                    fileFileTag.UpdatedByUserId = _userService.UserId;
                    await fileFileTag.Delete(_backendConfigurationPnDbContext);
                }

                propertyFile.File.UpdatedByUserId = _userService.UserId;
                await propertyFile.File.Delete(_backendConfigurationPnDbContext);

                propertyFile.UpdatedByUserId = _userService.UserId;
                await propertyFile.Delete(_backendConfigurationPnDbContext);
            }

            // delete property
            property.UpdatedByUserId = _userService.UserId;
            await property.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);

            if (property.EntitySelectListAreas != null)
            {
                var eg = await sdkDbContext.EntityGroups.SingleAsync(x => x.Id == property.EntitySelectListAreas)
                    .ConfigureAwait(false);
                await core.EntityGroupDelete(eg.MicrotingUid).ConfigureAwait(false);
            }

            if (property.EntitySelectListDeviceUsers != null)
            {
                var eg = await sdkDbContext.EntityGroups
                    .SingleAsync(x => x.Id == property.EntitySelectListDeviceUsers).ConfigureAwait(false);
                await core.EntityGroupDelete(eg.MicrotingUid).ConfigureAwait(false);
            }

            return new OperationResult(true,
                _backendConfigurationLocalizationService.GetString("SuccessfullyDeleteProperties"));
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationResult(false,
                $"{_backendConfigurationLocalizationService.GetString("ErrorWhileDeleteProperties")}: {e.Message}");
        }
    }

    public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetCommonDictionary(bool fullNames)
    {
        try
        {
            var properties = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => new CommonDictionaryModel
                {
                    Id = x.Id,
                    Name = fullNames ? $"{x.CVR} - {x.CHR} - {x.Name}" : x.Name,
                    Description = ""
                }).OrderBy(x => x.Name).ToListAsync().ConfigureAwait(false);
            return new OperationDataResult<List<CommonDictionaryModel>>(true, properties);
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationDataResult<List<CommonDictionaryModel>>(false,
                $"{_backendConfigurationLocalizationService.GetString("ErrorWhileObtainingProperties")}: {ex.Message}");
        }
    }

    public async Task<OperationDataResult<Result>> GetCompanyType(int cvrNumber)
    {
        var cvrHelper = new CvrHelper();
        var cvr = await cvrHelper.GetCompanyInfo(cvrNumber).ConfigureAwait(false);

        return new OperationDataResult<Result>(true, cvr);
    }

    public async Task<OperationDataResult<ChrResult>> GetChrInformation(int chrNumber)
    {
        var chrHelper = new ChrHelper();
        try
        {
            var chr = await chrHelper.GetCompanyInfo(chrNumber).ConfigureAwait(false);

            return new OperationDataResult<ChrResult>(true, chr);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            return new OperationDataResult<ChrResult>(false, e.Message);
        }
    }

    public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetLinkedFoldersList(int propertyId)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var userLanguage = await _userService.GetCurrentUserLanguage();

            var folderIds = await _backendConfigurationPnDbContext.ProperyAreaFolders
                .Where(x => x.AreaProperty.PropertyId == propertyId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.FolderId)
                .ToListAsync();
            folderIds = folderIds.Distinct().ToList();


            var folders = await sdkDbContext.Folders
                .Include(x => x.FolderTranslations)
                .ToListAsync();

            var folderList = folders
                .Where(f => f.ParentId == null)
                .Select(f => MapFolder(f, folders, userLanguage))
                .Where(x => HaveFolderWithId(folderIds, x))
                .SelectMany(x => MapFolder(x))
                .ToList();

            return new OperationDataResult<List<CommonDictionaryModel>>(true, folderList);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            return new OperationDataResult<List<CommonDictionaryModel>>(false, e.Message);
        }
    }

    public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetLinkedFoldersList(List<int> propertyIds)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var userLanguage = await _userService.GetCurrentUserLanguage();

            var folderIds = await _backendConfigurationPnDbContext.ProperyAreaFolders
                .Where(x => propertyIds.Contains(x.AreaProperty.PropertyId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.FolderId)
                .ToListAsync();
            folderIds = folderIds.Distinct().ToList();

            var folders = await sdkDbContext.Folders
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.FolderTranslations)
                .ToListAsync();

            var folderList = folders
                .Where(f => f.ParentId == null)
                .Select(f => MapFolder(f, folders, userLanguage))
                .Where(x => HaveFolderWithId(folderIds, x))
                .SelectMany(x => MapFolder(x))
                .ToList();

            return new OperationDataResult<List<CommonDictionaryModel>>(true, folderList);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            return new OperationDataResult<List<CommonDictionaryModel>>(false, e.Message);
        }
    }

    public async Task<OperationDataResult<List<PropertyFolderModel>>> GetLinkedFolderDtos(int propertyId)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var userLanguage = await _userService.GetCurrentUserLanguage();

            var folderIds = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.Id == propertyId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.FolderId.HasValue)
                .Select(x => x.FolderId.Value)
                .ToListAsync();

            folderIds.AddRange(await _backendConfigurationPnDbContext.ProperyAreaFolders
                .Where(x => x.AreaProperty.PropertyId == propertyId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.FolderId)
                .ToListAsync());
            folderIds = folderIds.Distinct().ToList();

            var folders = await sdkDbContext.Folders
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.FolderTranslations)
                .ToListAsync();

            var folderModels = folders
                .Where(f => f.ParentId == null)
                .Select(f => MapFolder(f, folders, userLanguage))
                .Where(x => HaveFolderWithId(folderIds, x))
                .ToList();

            return new OperationDataResult<List<PropertyFolderModel>>(true, folderModels);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            return new OperationDataResult<List<PropertyFolderModel>>(false, e.Message);
        }
    }

    public async Task<OperationDataResult<List<PropertyFolderModel>>> GetLinkedFolderDtos(List<int> propertyIds)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var userLanguage = await _userService.GetCurrentUserLanguage();

            var folderIds = await _backendConfigurationPnDbContext.Properties
                .Where(x => propertyIds.Contains(x.Id))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.FolderId.HasValue)
                .Select(x => x.FolderId.Value)
                .ToListAsync();

            folderIds.AddRange(await _backendConfigurationPnDbContext.ProperyAreaFolders
                .Where(x => propertyIds.Contains(x.AreaProperty.PropertyId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.FolderId)
                .ToListAsync());
            folderIds = folderIds.Distinct().ToList();

            var folders = await sdkDbContext.Folders
                .Include(x => x.FolderTranslations)
                .ToListAsync();

            var folderModels = folders
                .Where(f => f.ParentId == null)
                .Select(f => MapFolder(f, folders, userLanguage))
                .Where(x => HaveFolderWithId(folderIds, x))
                .ToList();

            return new OperationDataResult<List<PropertyFolderModel>>(true, folderModels);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            return new OperationDataResult<List<PropertyFolderModel>>(false, e.Message);
        }
    }

    public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetLinkedSites(int? propertyId, bool compliance)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var currentUser = await _userService.GetCurrentUserAsync();

            var site = await sdkDbContext.Sites.SingleOrDefaultAsync(x =>
                x.Name == currentUser.FirstName + " " + currentUser.LastName
                && x.WorkflowState != Constants.WorkflowStates.Removed);

            var query = _backendConfigurationPnDbContext.PropertyWorkers
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            if (propertyId != null)
            {
                query = query.Where(x => x.PropertyId == propertyId);
            }

            var siteIds = await query
                .Select(x => x.WorkerId)
                .ToListAsync();

            if (site != null && !siteIds.Contains(site.Id) && compliance)
            {
                siteIds.Add(site.Id);
            }

            var sites = await sdkDbContext.Sites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => siteIds.Contains(x.Id))
                .Select(x => new CommonDictionaryModel
                {
                    Name = x.Name,
                    Id = x.Id
                })
                .OrderBy(x => x.Name)
                .ToListAsync();

            return new OperationDataResult<List<CommonDictionaryModel>>(true, sites);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            return new OperationDataResult<List<CommonDictionaryModel>>(false, e.Message);
        }
    }

    public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetLinkedSites(List<int>? propertyIds)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();

            var query =  _backendConfigurationPnDbContext.PropertyWorkers
                //.Where(x => propertyIds.Contains(x.PropertyId))
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            if (propertyIds.Any())
            {
                query = query.Where(x => propertyIds.Contains(x.PropertyId));
            }

            var siteIds = await query.Select(x => x.WorkerId)
                .ToListAsync();

            var sites = await sdkDbContext.Sites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => siteIds.Contains(x.Id))
                .Select(x => new CommonDictionaryModel
                {
                    Name = x.Name,
                    Id = x.Id
                })
                .OrderBy(x => x.Name)
                .ToListAsync();

            return new OperationDataResult<List<CommonDictionaryModel>>(true, sites);
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            return new OperationDataResult<List<CommonDictionaryModel>>(false, e.Message);
        }
    }

    private static PropertyFolderModel MapFolder(Folder folder, List<Folder> allFolders, Language userLanguage)
    {
        var propertyFolderModel = new PropertyFolderModel
        {
            Id = folder.Id,
            Name = folder.FolderTranslations
                .Where(x => x.LanguageId == userLanguage.Id)
                .Select(x => x.Name)
                .FirstOrDefault(),
            Description = folder.FolderTranslations
                .Where(x => x.LanguageId == userLanguage.Id)
                .Select(x => x.Description)
                .FirstOrDefault(),
            MicrotingUId = folder.MicrotingUid,
            ParentId = folder.ParentId,
            Children = []
        };

        foreach (var childFolder in allFolders.Where(f => f.ParentId == folder.Id))
        {
            propertyFolderModel.Children.Add(MapFolder(childFolder, allFolders, userLanguage));
        }

        propertyFolderModel.Children = propertyFolderModel.Children.OrderBy(x => x.Name).ToList();

        return propertyFolderModel;
    }
    private static List<CommonDictionaryModel> MapFolder(PropertyFolderModel folder, string rootFolderName = "")
    {
        var result = new List<CommonDictionaryModel>();
        var fullName = string.IsNullOrEmpty(rootFolderName) ? folder.Name : $"{rootFolderName} - {folder.Name}";
        if (!folder.Children.Any())
        {
            result.Add(new CommonDictionaryModel
            {
                Id = folder.Id,
                Description = folder.Description,
                Name = fullName
            });
        }
        else
        {
            foreach (var childFolder in folder.Children)
            {
                result.AddRange(MapFolder(childFolder, fullName));
            }
        }

        return result;
    }

    private static bool HaveFolderWithId(int folderId, PropertyFolderModel folder)
    {
        return folder.Id == folderId || folder.Children.Any(f => HaveFolderWithId(folderId, f));
    }

    private static bool HaveFolderWithId(List<int> folderIds, PropertyFolderModel folder)
    {
        return folderIds.Contains(folder.Id) || folder.Children.Any(f => HaveFolderWithId(folderIds, f));
    }
}