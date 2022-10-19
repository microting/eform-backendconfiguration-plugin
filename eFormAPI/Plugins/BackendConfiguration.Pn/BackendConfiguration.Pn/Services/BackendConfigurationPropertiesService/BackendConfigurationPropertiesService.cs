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
            _workOrderHelper = new WorkOrderHelper(_coreHelper, _backendConfigurationPnDbContext, _backendConfigurationLocalizationService, _userService);
        }

        public async Task<OperationDataResult<Paged<PropertiesModel>>> Index(ProperiesRequesModel request)
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
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.CVR).ToListAsync().ConfigureAwait(false);
            var currentListOfChrNumbers = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.CHR).ToListAsync().ConfigureAwait(false);
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
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();

                var planningTag = new PlanningTag
                {
                    Name = propertyCreateModel.FullName(),
                };
                await planningTag.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
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
                    IndustryCode = propertyCreateModel.IndustryCode,
                    IsFarm = propertyCreateModel.IsFarm,
                    MainMailAddress = propertyCreateModel.MainMailAddress
                };
                await newProperty.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

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
                    await selectedTranslate.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }

                var translatesForFolder = await sdkDbContext.Languages
                    .Where(x => x.IsActive == true)
                    .Select(
                        x => new CommonTranslationsModel
                        {
                            LanguageId = x.Id,
                            Name = propertyCreateModel.Name,
                            Description = ""
                        })
                    .ToListAsync().ConfigureAwait(false);
                newProperty.FolderId = await core.FolderCreate(translatesForFolder, null).ConfigureAwait(false);
                await newProperty.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);

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
                        IsFarm = x.IsFarm,
                        IndustryCode = x.IndustryCode,
                        MainMailAddress = x.MainMailAddress,
                        Languages = x.SelectedLanguages
                            .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(y => new CommonDictionaryModel { Id = y.LanguageId })
                            .ToList(),
                        WorkorderEnable = x.WorkorderEnable,
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
                var core = await _coreHelper.GetCore().ConfigureAwait(false);
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var property = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.Id == updateModel.Id)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Include(x => x.SelectedLanguages)
                    .Include(x => x.PropertyWorkers)
                    .FirstOrDefaultAsync().ConfigureAwait(false);

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
                        .FirstAsync().ConfigureAwait(false);
                    var areasGroup = await core.EntityGroupRead(areaGroupUid).ConfigureAwait(false);
                    areasGroup.Name = $"eForm Backend Configurations - {updateModel.Name} - Areas";
                    await core.EntityGroupUpdate(areasGroup).ConfigureAwait(false);

                    var deviceUserGroupUid = await sdkDbContext.EntityGroups
                        .Where(x => x.Id == property.EntitySelectListDeviceUsers)
                        //.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(x => x.MicrotingUid)
                        .FirstAsync().ConfigureAwait(false);
                    var deviceUserGroup = await core.EntityGroupRead(deviceUserGroupUid).ConfigureAwait(false);
                    deviceUserGroup.Name = $"eForm Backend Configurations - {updateModel.Name} - Device Users";
                    await core.EntityGroupUpdate(deviceUserGroup).ConfigureAwait(false);
                }

                var pooleForm = await sdkDbContext.CheckListTranslations.FirstOrDefaultAsync(x => x.Text == $"01. Aflæsninger - {property.Name}");

                if (pooleForm != null)
                {
                    pooleForm.Text = $"01. Aflæsninger - {updateModel.Name}";
                    await pooleForm.Update(sdkDbContext);
                }
                var faceseForm = await sdkDbContext.CheckListTranslations.FirstOrDefaultAsync(x => x.Text == $"02. Fækale uheld - {property.Name}");

                if (faceseForm != null)
                {
                    faceseForm.Text = $"02. Fækale uheld - {updateModel.Name}";
                    await faceseForm.Update(sdkDbContext);
                }

                var eg = await sdkDbContext.EntityGroups.FirstOrDefaultAsync(x =>
                    x.Name == $"00. Aflæsninger, målinger, forbrug og fækale uheld - {property.Name}");

                if (eg != null)
                {
                    eg.Name = $"00. Aflæsninger, målinger, forbrug og fækale uheld - {updateModel.Name}";
                    await eg.Update(sdkDbContext);
                }

                var planningTag = await _itemsPlanningPnDbContext.PlanningTags
                    .Where(x => x.Id == property.ItemPlanningTagId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                if (planningTag != null)
                {
                    planningTag.Name = updateModel.FullName();
                    planningTag.UpdatedByUserId = _userService.UserId;
                    await planningTag.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                var planningTags = await _itemsPlanningPnDbContext.PlanningTags
                    .Where(x => x.Name.StartsWith(property.Name)).ToListAsync();

                foreach (var tag in planningTags)
                {
                    tag.Name = tag.Name.Replace(property.Name, updateModel.FullName());
                    tag.UpdatedByUserId = _userService.UserId;
                    await tag.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                var translatesForFolder = await sdkDbContext.Languages
                    .Select(
                        x => new CommonTranslationsModel
                        {
                            LanguageId = x.Id,
                            Name = property.Name,
                            Description = ""
                        })
                    .ToListAsync().ConfigureAwait(false);

                await core.FolderUpdate((int)property.FolderId, translatesForFolder, null).ConfigureAwait(false);

                if (property.WorkorderEnable != updateModel.WorkorderEnable)
                {
                    switch (updateModel.WorkorderEnable)
                    {
                        case true:
                        {
                            // int? folderIdForNewTasks;
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
                                    await core.FolderCreate(translatesFolderForTasks, property.FolderId).ConfigureAwait(false);

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
                                    property.FolderIdForTasks).ConfigureAwait(false);

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
                                property.FolderIdForOngoingTasks = await core.FolderCreate(translateFolderForOngoingTask, property.FolderIdForTasks).ConfigureAwait(false);

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
                                property.FolderIdForCompletedTasks = await core.FolderCreate(translateFolderForCompletedTask, property.FolderIdForTasks).ConfigureAwait(false);
                            }

                            var eformId = await sdkDbContext.CheckListTranslations
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Where(x => x.Text == "01. New task")
                                .Select(x => x.CheckListId)
                                .FirstAsync().ConfigureAwait(false);

                            var propertyWorkers = property.PropertyWorkers
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .ToList();

                            // create area select list filled manually
                            var areasGroup = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                                $"{property.Name} - Areas", "", true, true).ConfigureAwait(false);
                            property.EntitySelectListAreas = areasGroup.Id;
                            // create device users select list filled automatically by workers bound to property
                            var deviceUsersGroup = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                                $"{property.Name} - Device Users", "", true, false).ConfigureAwait(false);
                            property.EntitySelectListDeviceUsers = deviceUsersGroup.Id;

                            // read and fill list
                            var entityGroup = await core.EntityGroupRead(deviceUsersGroup.MicrotingUid).ConfigureAwait(false);
                            var nextItemUid = entityGroup.EntityGroupItemLst.Count;
                            for (var i = 0; i < propertyWorkers.Count; i++)
                            {
                                var propertyWorker = propertyWorkers[i];
                                var site = await sdkDbContext.Sites.Where(x => x.Id == propertyWorker.WorkerId)
                                    .FirstAsync().ConfigureAwait(false);
                                var entityItem = await core.EntitySelectItemCreate(entityGroup.Id, site.Name, i, nextItemUid.ToString()).ConfigureAwait(false);
                                nextItemUid++;
                                propertyWorker.EntityItemId = entityItem.Id;
                                await propertyWorker.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);

                                // todo need change language to site language for correct translates and change back after end translate
                                await _workOrderHelper.DeployEform(propertyWorker, eformId, property.FolderIdForNewTasks,
                                    $"<strong>{_backendConfigurationLocalizationService.GetString("Location")}:</strong> {property.Name}",
                                    int.Parse(areasGroup.MicrotingUid), int.Parse(deviceUsersGroup.MicrotingUid)).ConfigureAwait(false);
                            }

                            var entityItems = await sdkDbContext.EntityItems
                                .Where(x => x.EntityGroupId == deviceUsersGroup.Id)
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .OrderBy(x => x.Name)
                                .AsNoTracking()
                                .ToListAsync().ConfigureAwait(false);

                            int entityItemIncrementer = 0;
                            foreach (var entityItem in entityItems)
                            {
                                await core.EntityItemUpdate(entityItem.Id, entityItem.Name, entityItem.Description,
                                    entityItem.EntityItemUid, entityItemIncrementer).ConfigureAwait(false);
                                entityItemIncrementer++;
                            }
                            break;
                        }
                        case false:
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

                            var propertyWorkerIds = property.PropertyWorkers
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .ToList();

                            await _workOrderHelper.RetractEform(propertyWorkerIds, true).ConfigureAwait(false);
                            await _workOrderHelper.RetractEform(propertyWorkerIds, false).ConfigureAwait(false);
                            await _workOrderHelper.RetractEform(propertyWorkerIds, false).ConfigureAwait(false);


                            var areaGroupUid = await sdkDbContext.EntityGroups
                                .Where(x => x.Id == property.EntitySelectListAreas)
                                //.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Select(x => x.MicrotingUid)
                                .FirstOrDefaultAsync().ConfigureAwait(false);
                            if (areaGroupUid != null)
                            {
                                await core.EntityGroupDelete(areaGroupUid).ConfigureAwait(false);
                            }

                            var deviceUserGroupUid = await sdkDbContext.EntityGroups
                                .Where(x => x.Id == property.EntitySelectListDeviceUsers)
                                //.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Select(x => x.MicrotingUid)
                                .FirstOrDefaultAsync().ConfigureAwait(false);
                            if (deviceUserGroupUid != null)
                            {
                                await core.EntityGroupDelete(deviceUserGroupUid).ConfigureAwait(false);
                            }
                            property.EntitySelectListAreas = null;
                            property.EntitySelectListDeviceUsers = null;
                            break;
                        }
                    }
                }

                property.Address = updateModel.Address;
                property.CHR = updateModel.Chr;
                property.CVR = updateModel.Cvr;
                property.Name = updateModel.Name;
                property.UpdatedByUserId = _userService.UserId;
                property.MainMailAddress = updateModel.MainMailAddress;
                property.WorkorderEnable = updateModel.WorkorderEnable;
                await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);

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
                    await selectedLanguageForDelete.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }


                foreach (var selectedLanguageForCreate in selectedLanguagesForCreate)
                {
                    selectedLanguageForCreate.UpdatedByUserId = _userService.UserId;
                    selectedLanguageForCreate.CreatedByUserId = _userService.UserId;
                    await selectedLanguageForCreate.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
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

                    await _workOrderHelper.RetractEform(property.PropertyWorkers, true).ConfigureAwait(false);
                    await _workOrderHelper.RetractEform(property.PropertyWorkers, false).ConfigureAwait(false);
                    await _workOrderHelper.RetractEform(property.PropertyWorkers, false).ConfigureAwait(false);
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
                            await areaRuleAreaRuleTranslation.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
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
                                            if (result is {MicrotingUid: { }})
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
                        await core.EntityGroupDelete(property.EntitySearchListPoolWorkers.ToString()).ConfigureAwait(false);
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
                    await propertyFolder.Delete(sdkDbContext).ConfigureAwait(false);
                }

                // delete property folder for tasks
                var propertyFolderForTasks = await sdkDbContext.Folders
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == property.FolderIdForTasks)
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                if (propertyFolderForTasks != null) // if folder is not created
                {
                    await propertyFolderForTasks.Delete(sdkDbContext).ConfigureAwait(false);
                }

                // delete property
                property.UpdatedByUserId = _userService.UserId;
                await property.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);

                if (property.EntitySelectListAreas != null)
                {
                    var eg = await sdkDbContext.EntityGroups.SingleAsync(x => x.Id == property.EntitySelectListAreas).ConfigureAwait(false);
                    await core.EntityGroupDelete(eg.MicrotingUid).ConfigureAwait(false);
                }

                if (property.EntitySelectListDeviceUsers != null) {
                    var eg = await sdkDbContext.EntityGroups.SingleAsync(x => x.Id == property.EntitySelectListDeviceUsers).ConfigureAwait(false);
                    await core.EntityGroupDelete(eg.MicrotingUid).ConfigureAwait(false);
                }

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
                    }).ToListAsync().ConfigureAwait(false);
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

        public async Task<OperationDataResult<Result>> GetCompanyType(int cvrNumber)
        {
            var cvrHelper = new CvrHelper();
            var cvr = await cvrHelper.GetCompanyInfo(cvrNumber).ConfigureAwait(false);

            return new OperationDataResult<Result>(true, cvr);
        }

        public async Task<OperationDataResult<ChrResult>> GetChrInformation(int chrNumber)
        {
            var chrHelper = new ChrHelper();
            var chr = await chrHelper.GetCompanyInfo(chrNumber).ConfigureAwait(false);

            return new OperationDataResult<ChrResult>(true, chr);
        }
    }
}