using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public static class BackendConfigurationPropertiesServiceHelper
{
    public static async Task<OperationResult> Create(PropertyCreateModel propertyCreateModel, Core core, int userId,
        BackendConfigurationPnDbContext _backendConfigurationPnDbContext,
        IBackendConfigurationLocalizationService _backendConfigurationLocalizationService,
        ItemsPlanningPnDbContext _itemsPlanningPnDbContext, int maxChrNumbers, int maxCvrNumbers)
    {
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
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId,
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
                        CreatedByUserId = userId,
                        UpdatedByUserId = userId
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

                newProperty = await BackendConfigurationPropertiesServiceHelper.CreateTaskManagementFolders(newProperty, sdkDbContext, core);

                // create area select list filled manually
                var areasGroup = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                    $"{newProperty.Name} - Areas", "", true, true).ConfigureAwait(false);
                newProperty.EntitySelectListAreas = areasGroup.Id;
                // create device users select list filled automatically by workers bound to property
                var deviceUsersGroup = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                    $"{newProperty.Name} - Device Users", "", true, false).ConfigureAwait(false);
                newProperty.EntitySelectListDeviceUsers = deviceUsersGroup.Id;

                await newProperty.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);

                return new OperationResult(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyCreatingProperties"));
            }
            catch (Exception e)
            {
                //Log.LogException(e.Message);
                //Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileCreatingProperties")}: {e.Message}");
            }
    }


    public static async Task<OperationResult> Update(PropertiesUpdateModel updateModel, Core core, int userId,
        BackendConfigurationPnDbContext _backendConfigurationPnDbContext,
        IBackendConfigurationLocalizationService _backendConfigurationLocalizationService,
        ItemsPlanningPnDbContext _itemsPlanningPnDbContext)
    {
        try
            {
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
                    planningTag.UpdatedByUserId = userId;
                    await planningTag.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                var planningTags = await _itemsPlanningPnDbContext.PlanningTags
                    .Where(x => x.Name.StartsWith(property.Name)).ToListAsync();

                foreach (var tag in planningTags)
                {
                    tag.Name = tag.Name.Replace(property.Name, updateModel.FullName());
                    tag.UpdatedByUserId = userId;
                    await tag.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                var translatesForFolder = await sdkDbContext.Languages
                    .Select(
                        x => new CommonTranslationsModel
                        {
                            LanguageId = x.Id,
                            Name = updateModel.Name,
                            Description = updateModel.Address ?? ""
                        })
                    .ToListAsync().ConfigureAwait(false);

                await core.FolderUpdate((int)property.FolderId, translatesForFolder, null).ConfigureAwait(false);

                property = await BackendConfigurationPropertiesServiceHelper.CreateTaskManagementFolders(property, sdkDbContext, core);
                await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);

                if (property.WorkorderEnable != updateModel.WorkorderEnable)
                {
                    switch (updateModel.WorkorderEnable)
                    {
                        case true:
                        {
                            var eformId = await sdkDbContext.CheckLists
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Where(x => x.OriginalId == "142663new2")
                                .Select(x => x.Id)
                                .FirstAsync().ConfigureAwait(false);

                            var propertyWorkers = property.PropertyWorkers
                                .Where(x => x.TaskManagementEnabled == true)
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
                                await WorkOrderHelper.DeployEform(propertyWorker, eformId, property,
                                    $"<strong>{_backendConfigurationLocalizationService.GetString("Location")}:</strong> {property.Name}",
                                    int.Parse(areasGroup.MicrotingUid), int.Parse(deviceUsersGroup.MicrotingUid), core, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
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
                            await core.FolderDelete((int) property.FolderIdForCompletedTasks).ConfigureAwait(false);
                            await core.FolderDelete((int) property.FolderIdForOngoingTasks).ConfigureAwait(false);
                            await core.FolderDelete((int) property.FolderIdForNewTasks).ConfigureAwait(false);
                            property.FolderIdForCompletedTasks = null;
                            property.FolderIdForOngoingTasks = null;
                            property.FolderIdForNewTasks = null;
                            property.FolderIdForTasks = null;
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

                            await WorkOrderHelper.RetractEform(propertyWorkerIds, true, core, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
                            await WorkOrderHelper.RetractEform(propertyWorkerIds, false, core, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);
                            await WorkOrderHelper.RetractEform(propertyWorkerIds, false, core, userId, _backendConfigurationPnDbContext).ConfigureAwait(false);


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
                property.UpdatedByUserId = userId;
                property.MainMailAddress = updateModel.MainMailAddress;
                property.WorkorderEnable = updateModel.WorkorderEnable;

                property.SelectedLanguages = property.SelectedLanguages
                    .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToList();
                await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);

                var selectedLanguagesForDelete = property.SelectedLanguages
                    .Where(x => !updateModel.LanguagesIds.Contains(x.LanguageId))
                    .ToList();

                var selectedLanguagesForCreate = updateModel.LanguagesIds
                    .Where(x => !property.SelectedLanguages.Exists(y => y.LanguageId == x))
                    .Select(x => new PropertySelectedLanguage
                    {
                        LanguageId = x,
                        PropertyId = property.Id,
                        CreatedByUserId = userId,
                        UpdatedByUserId = userId
                    })
                    .ToList();

                foreach (var selectedLanguageForDelete in selectedLanguagesForDelete)
                {
                    selectedLanguageForDelete.UpdatedByUserId = userId;
                    await selectedLanguageForDelete.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }


                foreach (var selectedLanguageForCreate in selectedLanguagesForCreate)
                {
                    selectedLanguageForCreate.UpdatedByUserId = userId;
                    selectedLanguageForCreate.CreatedByUserId = userId;
                    await selectedLanguageForCreate.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }

                return new OperationResult(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyUpdateProperties"));
            }
            catch (Exception e)
            {
                //Log.LogException(e.Message);
                //Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileUpdateProperties")}: {e.Message}");
            }
    }

    // public static async Task<OperationResult> Delete()
    // {
    //
    // }

    public static async Task<Property> CreateTaskManagementFolders(Property property,
        MicrotingDbContext sdkDbContext, Core core)
    {
        var parentFolderTranslation =
            await sdkDbContext.Folders
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.FolderTranslations)
                .Where(x => x.ParentId == null)
                .FirstOrDefaultAsync(x =>
                    x.FolderTranslations.Any(y => y.Name == "00.00 Opret ny opgave"));

        int? parentFolderId = null;

        if (parentFolderTranslation == null)
        {
            var translatesFolderForTasks = new List<CommonTranslationsModel>
            {
                new()
                {
                    Name = "00.00 Opret ny opgave",
                    LanguageId = 1, // da
                    Description = "",
                },
                new()
                {
                    Name = "00.00 Create a new task",
                    LanguageId = 2, // en
                    Description = "",
                },
                new()
                {
                    Name = "00.00 Erstellen Sie eine neue Aufgabe",
                    LanguageId = 3, // de
                    Description = "",
                },
            };
            property.FolderIdForNewTasks =
                await core.FolderCreate(translatesFolderForTasks, null).ConfigureAwait(false);
        }
        else
        {
            property.FolderIdForNewTasks = parentFolderTranslation.Id;
        }

        parentFolderTranslation =
            await sdkDbContext.Folders
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.FolderTranslations)
                .Where(x => x.ParentId == null)
                .FirstOrDefaultAsync(x =>
                    x.FolderTranslations.Any(y => y.Name == "00.02 Mine øvrige opgaver"));

        if (parentFolderTranslation == null)
        {
            var translatesFolderForTasks = new List<CommonTranslationsModel>
            {
                new()
                {
                    Name = "00.02 Mine øvrige opgaver",
                    LanguageId = 1, // da
                    Description = "",
                },
                new()
                {
                    Name = "00.02 My other tasks",
                    LanguageId = 2, // en
                    Description = "",
                },
                new()
                {
                    Name = "00.02 Meine anderen Aufgaben",
                    LanguageId = 3, // de
                    Description = "",
                },
            };
            parentFolderId =
                await core.FolderCreate(translatesFolderForTasks, null).ConfigureAwait(false);
        }
        else
        {
            parentFolderId = parentFolderTranslation.Id;
        }

        var translateFolderForNewTask = new List<CommonTranslationsModel>
        {
            new()
            {
                Name = property.Name,
                LanguageId = 1, // da
                Description = "",
            },
            new()
            {
                Name = property.Name,
                LanguageId = 2, // en
                Description = "",
            },
            new()
            {
                Name = property.Name,
                LanguageId = 3, // de
                Description = "",
            },
        };

        if (!sdkDbContext.Folders
                .Where(x => x.ParentId == parentFolderId)
                .Any(x => x.Id == property.FolderIdForOngoingTasks))
        {
            property.FolderIdForOngoingTasks = await core.FolderCreate(translateFolderForNewTask,
                parentFolderId).ConfigureAwait(false);
        }
        else
        {
            var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == property.FolderIdForOngoingTasks);
            await core.FolderUpdate(folder.Id, translateFolderForNewTask, folder.ParentId);
        }

        parentFolderTranslation =
            await sdkDbContext.Folders
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.FolderTranslations)
                .Where(x => x.ParentId == null)
                .FirstOrDefaultAsync(x =>
                    x.FolderTranslations.Any(y => y.Name == "00.03 Andres opgaver"));

        if (parentFolderTranslation == null)
        {
            var translatesFolderForTasks = new List<CommonTranslationsModel>
            {
                new()
                {
                    Name = "00.03 Andres opgaver",
                    LanguageId = 1, // da
                    Description = "",
                },
                new()
                {
                    Name = "00.03 Others' tasks",
                    LanguageId = 2, // en
                    Description = "",
                },
                new()
                {
                    Name = "00.03 Aufgaben anderer",
                    LanguageId = 3, // de
                    Description = "",
                },
            };
            parentFolderId =
                await core.FolderCreate(translatesFolderForTasks, null).ConfigureAwait(false);
        }
        else
        {
            parentFolderId = parentFolderTranslation.Id;
        }

        translateFolderForNewTask = new List<CommonTranslationsModel>
        {
            new()
            {
                Name = property.Name,
                LanguageId = 1, // da
                Description = "",
            },
            new()
            {
                Name = property.Name,
                LanguageId = 2, // en
                Description = "",
            },
            new()
            {
                Name = property.Name,
                LanguageId = 3, // de
                Description = "",
            },
        };

        if (!sdkDbContext.Folders
                .Where(x => x.ParentId == parentFolderId)
                .Any(x => x.Id == property.FolderIdForCompletedTasks))
        {
            property.FolderIdForCompletedTasks = await core.FolderCreate(translateFolderForNewTask,
                parentFolderId).ConfigureAwait(false);
        }
        else
        {
            var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == property.FolderIdForCompletedTasks);
            await core.FolderUpdate(folder.Id, translateFolderForNewTask, folder.ParentId);
        }

        parentFolderTranslation =
            await sdkDbContext.Folders
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.FolderTranslations)
                .Where(x => x.ParentId == null)
                .FirstOrDefaultAsync(x =>
                    x.FolderTranslations.Any(y => y.Name == "00.01 Mine hasteopgaver"));

        if (parentFolderTranslation == null)
        {
            var translatesFolderForTasks = new List<CommonTranslationsModel>
            {
                new()
                {
                    Name = "00.01 Mine hasteopgaver",
                    LanguageId = 1, // da
                    Description = "",
                },
                new()
                {
                    Name = "00.01 My urgent tasks",
                    LanguageId = 2, // en
                    Description = "",
                },
                new()
                {
                    Name = "00.01 Meine dringenden Aufgaben",
                    LanguageId = 3, // de
                    Description = "",
                },
            };
            parentFolderId =
                await core.FolderCreate(translatesFolderForTasks, null).ConfigureAwait(false);
        }
        else
        {
            parentFolderId = parentFolderTranslation.Id;
        }

        translateFolderForNewTask = new List<CommonTranslationsModel>
        {
            new()
            {
                Name = property.Name,
                LanguageId = 1, // da
                Description = "",
            },
            new()
            {
                Name = property.Name,
                LanguageId = 2, // en
                Description = "",
            },
            new()
            {
                Name = property.Name,
                LanguageId = 3, // de
                Description = "",
            },
        };

        if (!sdkDbContext.Folders
                .Where(x => x.ParentId == parentFolderId)
                .Any(x => x.Id == property.FolderIdForTasks))
        {
            property.FolderIdForTasks = await core.FolderCreate(translateFolderForNewTask,
                parentFolderId).ConfigureAwait(false);
        }
        else
        {
            var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == property.FolderIdForTasks);
            await core.FolderUpdate(folder.Id, translateFolderForNewTask, folder.ParentId);
        }

        return property;
    }
}