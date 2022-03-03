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
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Helpers.PluginDbOptions;

namespace BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using BackendConfigurationLocalizationService;
    using Infrastructure;
    using Infrastructure.Models.Properties;
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
    using CommonTranslationsModel = Microting.eForm.Infrastructure.Models.CommonTranslationsModel;

    public class BackendConfigurationPropertiesService : IBackendConfigurationPropertiesService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
        private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;
        private readonly IPluginDbOptions<BackendConfigurationBaseSettings> _options;
        private readonly WorkOrderHelper _workOrderHelper;

        public BackendConfigurationPropertiesService(
            IEFormCoreService coreHelper,
            IUserService userService,
            BackendConfigurationPnDbContext backendConfigurationPnDbContext,
            IBackendConfigurationLocalizationService backendConfigurationLocalizationService,
            ItemsPlanningPnDbContext itemsPlanningPnDbContext,
            IPluginDbOptions<BackendConfigurationBaseSettings> options)
        {
            _coreHelper = coreHelper;
            _userService = userService;
            _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
            _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
            _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
            _options = options;
            _workOrderHelper = new WorkOrderHelper(_coreHelper, _backendConfigurationPnDbContext, _backendConfigurationLocalizationService);
        }

        public async Task<OperationDataResult<Paged<PropertiesModel>>> Index(ProperiesRequesModel request)
        {
            try
            {
                var propertiesQuery = _backendConfigurationPnDbContext.Properties
                    .Include(x => x.SelectedLanguages)
                    .Include(x => x.PropertyWorkers)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

                var nameFields = new List<string> { "Name", "CHR", "Address", "CVR" };
                propertiesQuery = QueryHelper.AddFilterAndSortToQuery(propertiesQuery, request, nameFields);

                // get total
                var total = await propertiesQuery.Select(x => x.Id).CountAsync();

                var properties = new List<PropertiesModel>();

                if (total > 0)
                {
                    // pagination
                    //propertiesQuery = propertiesQuery
                    //    .Skip(request.Offset)
                    //    .Take(request.PageSize);

                    // add select to query and get from db
                    properties = await propertiesQuery
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
                        }).ToListAsync();
                }

                return new OperationDataResult<Paged<PropertiesModel>>(true,
                    new Paged<PropertiesModel> { Entities = properties, Total = total });
            }
            catch (Exception ex)
            {
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
            var currentListOfCvrNumbers = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.CVR).ToListAsync();
            var currentListOfChrNumbers = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.CHR).ToListAsync();
            if (_backendConfigurationPnDbContext.Properties.Any(x =>
                    x.CHR == propertyCreateModel.Chr && x.WorkflowState != Constants.WorkflowStates.Removed &&
                    x.CVR == propertyCreateModel.Cvr))
            {
                return new OperationResult(false,
                    _backendConfigurationLocalizationService.GetString("PropertyAlreadyExists"));
            }

            if (!currentListOfChrNumbers.Contains(propertyCreateModel.Chr) &&
                currentListOfChrNumbers.Count >= maxChrNumbers)
            {
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("MaxChrNumbersReached")}");
            }

            if (!currentListOfCvrNumbers.Contains(propertyCreateModel.Cvr) &&
                currentListOfCvrNumbers.Count >= maxCvrNumbers)
            {
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("MaxCvrNumbersReached")}");
            }

            try
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();

                var planningTag = new PlanningTag
                {
                    Name = propertyCreateModel.FullName(),
                };
                await planningTag.Create(_itemsPlanningPnDbContext);
                var newProperty = new Property
                {
                    Address = propertyCreateModel.Address,
                    CHR = propertyCreateModel.Chr,
                    CVR = propertyCreateModel.Cvr,
                    Name = propertyCreateModel.Name,
                    CreatedByUserId = _userService.UserId,
                    UpdatedByUserId = _userService.UserId,
                    ItemPlanningTagId = planningTag.Id,
                    WorkorderEnable = propertyCreateModel.WorkorderEnable,
                };
                await newProperty.Create(_backendConfigurationPnDbContext);

                var selectedTranslates = propertyCreateModel.LanguagesIds
                    .Select(x => new PropertySelectedLanguage
                    {
                        LanguageId = x,
                        PropertyId = newProperty.Id,
                        CreatedByUserId = _userService.UserId,
                        UpdatedByUserId = _userService.UserId,
                    });

                foreach (var selectedTranslate in selectedTranslates)
                {
                    await selectedTranslate.Create(_backendConfigurationPnDbContext);
                }

                var translatesForFolder = await sdkDbContext.Languages
                    .Select(
                        x => new CommonTranslationsModel
                        {
                            LanguageId = x.Id,
                            Name = propertyCreateModel.Name,
                            Description = ""
                        })
                    .ToListAsync();
                newProperty.FolderId = await core.FolderCreate(translatesForFolder, null);
                await newProperty.Update(_backendConfigurationPnDbContext);

                return new OperationResult(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyCreatingProperties"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileCreatingProperties")}: {e.Message}");
            }
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
                        Languages = x.SelectedLanguages
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(y => new CommonDictionaryModel { Id = y.LanguageId })
                            .ToList(),
                        WorkorderEnable = x.WorkorderEnable,
                    })
                    .FirstOrDefaultAsync();

                if (property == null)
                {
                    return new OperationDataResult<PropertiesModel>(false,
                        _backendConfigurationLocalizationService.GetString("PropertyNotFound"));
                }

                return new OperationDataResult<PropertiesModel>(true, property);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<PropertiesModel>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadProperty")}: {e.Message}");
            }
        }

        public async Task<OperationResult> Update(PropertiesUpdateModel updateModel)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var property = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.Id == updateModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Include(x => x.SelectedLanguages)
                    .Include(x => x.PropertyWorkers)
                    .FirstOrDefaultAsync();

                if (property == null)
                {
                    return new OperationResult(false,
                        _backendConfigurationLocalizationService.GetString("PropertyNotFound"));
                }

                if (_backendConfigurationPnDbContext.Properties.Any(x =>
                        x.WorkflowState != Constants.WorkflowStates.Removed
                        && x.CHR == updateModel.Chr
                        && x.CVR == updateModel.Cvr
                        && x.Name == updateModel.Name
                        && x.Address == updateModel.Address
                        && x.Id != updateModel.Id))
                {
                    return new OperationResult(false,
                        _backendConfigurationLocalizationService.GetString("PropertyAlreadyExists"));
                }


                if (property.Name != updateModel.Name && property.WorkorderEnable)
                {
                    var areaGroupUid = await sdkDbContext.EntityGroups
                        .Where(x => x.Id == property.EntitySelectListAreas)
                        //.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(x => x.MicrotingUid)
                        .FirstAsync();
                    var areasGroup = await core.EntityGroupRead(areaGroupUid);
                    areasGroup.Name = $"eForm Backend Configurations - {updateModel.Name} - Areas";
                    await core.EntityGroupUpdate(areasGroup);

                    var deviceUserGroupUid = await sdkDbContext.EntityGroups
                        .Where(x => x.Id == property.EntitySelectListDeviceUsers)
                        //.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(x => x.MicrotingUid)
                        .FirstAsync();
                    var deviceUserGroup = await core.EntityGroupRead(deviceUserGroupUid);
                    deviceUserGroup.Name = $"eForm Backend Configurations - {updateModel.Name} - Device Users";
                    await core.EntityGroupUpdate(deviceUserGroup);
                }
                property.Address = updateModel.Address;
                property.CHR = updateModel.Chr;
                property.CVR = updateModel.Cvr;
                property.Name = updateModel.Name;
                property.UpdatedByUserId = _userService.UserId;

                var planningTag = await _itemsPlanningPnDbContext.PlanningTags
                    .Where(x => x.Id == property.ItemPlanningTagId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();
                if (planningTag != null)
                {
                    planningTag.Name = updateModel.FullName();
                    planningTag.UpdatedByUserId = _userService.UserId;
                    await planningTag.Update(_itemsPlanningPnDbContext);
                }

                var translatesForFolder = await sdkDbContext.Languages
                    .Select(
                        x => new CommonTranslationsModel
                        {
                            LanguageId = x.Id,
                            Name = property.Name,
                            Description = ""
                        })
                    .ToListAsync();

                await core.FolderUpdate((int)property.FolderId, translatesForFolder, null);

                if (property.WorkorderEnable != updateModel.WorkorderEnable)
                {
                    switch (updateModel.WorkorderEnable)
                    {
                        case true:
                        {
                            int? folderIdForNewTasks;
                            if (property.FolderIdForTasks == null)
                            {
                                var translatesFolderForTasks = new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        Name = "00. Opgavestyring",
                                        LanguageId = 1, // da
                                        Description = "",
                                    },
                                    new()
                                    {
                                        Name = "00. Tasks",
                                        LanguageId = 2, // en
                                        Description = "",
                                    },
                                    //new ()
                                    //{
                                    //    Name = "00. Tasks",
                                    //    LanguageId = 3, // de
                                    //    Description = "",
                                    //},
                                };
                                property.FolderIdForTasks =
                                    await core.FolderCreate(translatesFolderForTasks, property.FolderId);

                                var translateFolderForNewTask = new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        Name = "01. Ny opgave",
                                        LanguageId = 1, // da
                                        Description = "",
                                    },
                                    new()
                                    {
                                        Name = "01. New tasks",
                                        LanguageId = 2, // en
                                        Description = "",
                                    },
                                    //new ()
                                    //{
                                    //    Name = "01. New task",
                                    //    LanguageId = 3, // de
                                    //    Description = "",
                                    //},
                                };
                                property.FolderIdForNewTasks = await core.FolderCreate(translateFolderForNewTask,
                                    property.FolderIdForTasks);

                                var translateFolderForOngoingTask = new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        Name = "02. Igangværende opgaver",
                                        LanguageId = 1, // da
                                        Description = "",
                                    },
                                    new()
                                    {
                                        Name = "02. Ongoing tasks",
                                        LanguageId = 2, // en
                                        Description = "",
                                    },
                                    //new ()
                                    //{
                                    //    Name = "02. Ongoing tasks",
                                    //    LanguageId = 3, // de
                                    //    Description = "",
                                    //},
                                };
                                property.FolderIdForOngoingTasks = await core.FolderCreate(translateFolderForOngoingTask, property.FolderIdForTasks);

                                var translateFolderForCompletedTask = new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        Name = "03. Afsluttede opgaver",
                                        LanguageId = 1, // da
                                        Description = "",
                                    },
                                    new()
                                    {
                                        Name = "03. Completed tasks",
                                        LanguageId = 2, // en
                                        Description = "",
                                    },
                                    //new ()
                                    //{
                                    //    Name = "03. Completed tasks",
                                    //    LanguageId = 3, // de
                                    //    Description = "",
                                    //},
                                };
                                property.FolderIdForCompletedTasks = await core.FolderCreate(translateFolderForCompletedTask, property.FolderIdForTasks);
                            }

                            var eformId = await sdkDbContext.CheckListTranslations
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Where(x => x.Text == "01. New task")
                                .Select(x => x.CheckListId)
                                .FirstAsync();

                            var propertyWorkers = property.PropertyWorkers
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .ToList();

                            // create area select list filled manually
                            var areasGroup = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                                $"{property.Name} - Areas", "", true, true);
                            property.EntitySelectListAreas = areasGroup.Id;
                            // create device users select list filled automatically by workers bound to property
                            var deviceUsersGroup = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                                $"{property.Name} - Device Users", "", true, false);
                            property.EntitySelectListDeviceUsers = deviceUsersGroup.Id;

                            // read and fill list
                            var entityGroup = await core.EntityGroupRead(deviceUsersGroup.MicrotingUid);
                            var nextItemUid = entityGroup.EntityGroupItemLst.Count;
                            for (var i = 0; i < propertyWorkers.Count; i++)
                            {
                                var propertyWorker = propertyWorkers[i];
                                var site = await sdkDbContext.Sites.Where(x => x.Id == propertyWorker.WorkerId)
                                    .FirstAsync();
                                var entityItem = await core.EntitySelectItemCreate(entityGroup.Id, site.Name, i, nextItemUid.ToString());
                                nextItemUid++;
                                propertyWorker.EntityItemId = entityItem.Id;
                                await propertyWorker.Update(_backendConfigurationPnDbContext);
                            }

                            var entityItems = await sdkDbContext.EntityItems
                                .Where(x => x.EntityGroupId == deviceUsersGroup.Id)
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .OrderBy(x => x.Name)
                                .AsNoTracking()
                                .ToListAsync();

                            int entityItemIncrementer = 0;
                            foreach (var entityItem in entityItems)
                            {
                                await core.EntityItemUpdate(entityItem.Id, entityItem.Name, entityItem.Description,
                                    entityItem.EntityItemUid, entityItemIncrementer);
                                entityItemIncrementer++;
                            }

                            // todo need change language to site language for correct translates and change back after end translate
                            await _workOrderHelper.DeployEform(propertyWorkers, eformId, property.FolderIdForNewTasks,
                                $"<strong>{_backendConfigurationLocalizationService.GetString("Location")}:</strong> {property.Name}",
                                int.Parse(areasGroup.MicrotingUid), int.Parse(deviceUsersGroup.MicrotingUid));
                            break;
                        }
                        case false:
                        {
                            var eformIdForNewTasks = await sdkDbContext.CheckListTranslations
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
                                .FirstAsync();

                            var propertyWorkerIds = property.PropertyWorkers
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .ToList();

                            await _workOrderHelper.RetractEform(propertyWorkerIds, true);
                            await _workOrderHelper.RetractEform(propertyWorkerIds, false);
                            await _workOrderHelper.RetractEform(propertyWorkerIds, false);


                            var areaGroupUid = await sdkDbContext.EntityGroups
                                .Where(x => x.Id == property.EntitySelectListAreas)
                                //.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Select(x => x.MicrotingUid)
                                .FirstOrDefaultAsync();
                            if (areaGroupUid != null)
                            {
                                await core.EntityGroupDelete(areaGroupUid);
                            }

                            var deviceUserGroupUid = await sdkDbContext.EntityGroups
                                .Where(x => x.Id == property.EntitySelectListDeviceUsers)
                                //.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Select(x => x.MicrotingUid)
                                .FirstOrDefaultAsync();
                            if (deviceUserGroupUid != null)
                            {
                                await core.EntityGroupDelete(deviceUserGroupUid);
                            }
                            property.EntitySelectListAreas = null;
                            property.EntitySelectListDeviceUsers = null;
                            break;
                        }
                    }
                }

                property.WorkorderEnable = updateModel.WorkorderEnable;
                await property.Update(_backendConfigurationPnDbContext);

                property.SelectedLanguages = property.SelectedLanguages
                    .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToList();

                var selectedLanguagesForDelete = property.SelectedLanguages
                    .Where(x => !updateModel.LanguagesIds.Contains(x.LanguageId))
                    .ToList();

                var selectedLanguagesForCreate = updateModel.LanguagesIds
                    .Where(x => !property.SelectedLanguages.Exists(y => y.LanguageId == x))
                    .Select(x => new PropertySelectedLanguage
                    {
                        LanguageId = x,
                        PropertyId = property.Id,
                        CreatedByUserId = _userService.UserId,
                        UpdatedByUserId = _userService.UserId,
                    })
                    .ToList();

                foreach (var selectedLanguageForDelete in selectedLanguagesForDelete)
                {
                    selectedLanguageForDelete.UpdatedByUserId = _userService.UserId;
                    await selectedLanguageForDelete.Delete(_backendConfigurationPnDbContext);
                }


                foreach (var selectedLanguageForCreate in selectedLanguagesForCreate)
                {
                    selectedLanguageForCreate.UpdatedByUserId = _userService.UserId;
                    selectedLanguageForCreate.CreatedByUserId = _userService.UserId;
                    await selectedLanguageForCreate.Create(_backendConfigurationPnDbContext);
                }

                return new OperationResult(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyUpdateProperties"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileUpdateProperties")}: {e.Message}");
            }
        }

        public async Task<OperationResult> Delete(int id)
        {
            try
            {
                // find property and all links
                var property = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.Id == id)
                    .Include(x => x.SelectedLanguages)
                    .Include(x => x.PropertyWorkers)
                    .Include(x => x.AreaProperties)
                    .ThenInclude(x => x.ProperyAreaFolders)
                    .FirstOrDefaultAsync();

                if (property == null)
                {
                    return new OperationResult(false,
                        _backendConfigurationLocalizationService.GetString("PropertyNotFound"));
                }

                // delete item planning tag
                var planningTag = await _itemsPlanningPnDbContext.PlanningTags
                    .Where(x => x.Id == property.ItemPlanningTagId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();
                if (planningTag != null)
                {
                    planningTag.UpdatedByUserId = _userService.UserId;
                    await planningTag.Delete(_itemsPlanningPnDbContext);
                }

                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                // retract eforms
                if (property.WorkorderEnable)
                {
                    var eformIdForNewTasks = await sdkDbContext.CheckListTranslations
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
                        .FirstAsync();

                    await _workOrderHelper.RetractEform(property.PropertyWorkers, true);
                    await _workOrderHelper.RetractEform(property.PropertyWorkers, false);
                    await _workOrderHelper.RetractEform(property.PropertyWorkers, false);
                }

                // delete property workers
                foreach (var propertyWorker in property.PropertyWorkers)
                {
                    propertyWorker.UpdatedByUserId = _userService.UserId;
                    await propertyWorker.Delete(_backendConfigurationPnDbContext);
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
                            .FirstOrDefaultAsync();
                        if (folder != null) // if folder is not deleted
                        {
                            await folder.Delete(sdkDbContext);
                        }
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
                        .ToListAsync();

                    foreach (var areaRule in areaRules)
                    {
                        if (areaRule.Area.Type is AreaTypesEnum.Type3 && areaRule.GroupItemId != 0)
                        {
                            // delete item from selectable list
                            var entityGroupItem = await sdkDbContext.EntityItems
                                .Where(x => x.Id == areaRule.GroupItemId).FirstOrDefaultAsync();
                            if (entityGroupItem != null)
                            {
                                await entityGroupItem.Delete(sdkDbContext);
                            }
                        }

                        string eformName = $"05. Halebid - {property.Name}";
                        var eForm = await sdkDbContext.CheckListTranslations
                            .Where(x => x.Text == eformName)
                            .FirstOrDefaultAsync();
                        if (eForm != null)
                        {
                            foreach (CheckListSite checkListSite in sdkDbContext.CheckListSites.Where(x =>
                                         x.CheckListId == eForm.CheckListId))
                            {
                                await core.CaseDelete(checkListSite.MicrotingUid);
                            }
                        }

                        // delete translations for are rules
                        foreach (var areaRuleAreaRuleTranslation in areaRule.AreaRuleTranslations.Where(x =>
                                     x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            areaRuleAreaRuleTranslation.UpdatedByUserId = _userService.UserId;
                            await areaRuleAreaRuleTranslation.Delete(_backendConfigurationPnDbContext);
                        }

                        // delete plannings area rules and items planning
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

                                    var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                                        .Where(x => x.PlanningId == planning.Id)
                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                        .ToListAsync();
                                    foreach (PlanningCaseSite planningCaseSite in planningCaseSites)
                                    {
                                        planningCaseSite.UpdatedByUserId = _userService.UserId;
                                        await planningCaseSite.Delete(_itemsPlanningPnDbContext);
                                        if (planningCaseSite.MicrotingSdkCaseId != 0)
                                        {
                                            var result =
                                                await sdkDbContext.Cases.SingleOrDefaultAsync(x =>
                                                    x.Id == planningCaseSite.MicrotingSdkCaseId);
                                            if (result.MicrotingUid != null)
                                            {
                                                await core.CaseDelete((int)result.MicrotingUid);
                                            }
                                        }
                                        else
                                        {
                                            var result = await sdkDbContext.CheckListSites.SingleOrDefaultAsync(x =>
                                                x.Id == planningCaseSite.MicrotingCheckListSitId);
                                            if (result != null)
                                            {
                                                await core.CaseDelete(result.MicrotingUid);
                                            }
                                        }
                                    }
                                }
                            }

                            areaRulePlanning.UpdatedByUserId = _userService.UserId;
                            await areaRulePlanning.Delete(_backendConfigurationPnDbContext);
                        }

                        // delete area rule
                        areaRule.UpdatedByUserId = _userService.UserId;
                        await areaRule.Delete(_backendConfigurationPnDbContext);
                    }

                    // delete entity select group. only for type 3(tail bite and stables)
                    if (areaProperty.GroupMicrotingUuid != 0)
                    {
                        await core.EntityGroupDelete(areaProperty.GroupMicrotingUuid.ToString());
                    }

                    areaProperty.UpdatedByUserId = _userService.UserId;
                    await areaProperty.Delete(_backendConfigurationPnDbContext);
                }

                // delete selected languages
                foreach (var selectedLanguage in property.SelectedLanguages)
                {
                    selectedLanguage.UpdatedByUserId = _userService.UserId;
                    await selectedLanguage.Delete(_backendConfigurationPnDbContext);
                }

                // delete property folder
                var propertyFolder = await sdkDbContext.Folders
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == property.FolderId)
                    .FirstOrDefaultAsync();
                if (propertyFolder != null) // if folder is not deleted
                {
                    await propertyFolder.Delete(sdkDbContext);
                }

                // delete property folder for tasks
                var propertyFolderForTasks = await sdkDbContext.Folders
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == property.FolderIdForTasks)
                    .FirstOrDefaultAsync();
                if (propertyFolderForTasks != null) // if folder is not created
                {
                    await propertyFolderForTasks.Delete(sdkDbContext);
                }

                // delete property
                property.UpdatedByUserId = _userService.UserId;
                await property.Delete(_backendConfigurationPnDbContext);

                return new OperationResult(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyDeleteProperties"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileDeleteProperties")}: {e.Message}");
            }
        }

        public async Task<OperationDataResult<List<CommonDictionaryModel>>> GetCommonDictionary()
        {
            try
            {
                var properties = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Select(x => new CommonDictionaryModel
                    {
                        Id = x.Id,
                        Name = $"{x.CVR} - {x.CHR} - {x.Name}",
                        Description = "",
                    }).ToListAsync();
                return new OperationDataResult<List<CommonDictionaryModel>>(true, properties);
            }
            catch (Exception ex)
            {
                Log.LogException(ex.Message);
                Log.LogException(ex.StackTrace);
                return new OperationDataResult<List<CommonDictionaryModel>>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileObtainingProperties")}: {ex.Message}");
            }
        }
    }
}