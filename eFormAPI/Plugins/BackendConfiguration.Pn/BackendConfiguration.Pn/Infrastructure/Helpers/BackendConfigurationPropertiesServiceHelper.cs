#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Properties;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using eFormCore;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Rebus.Bus;
using Sentry;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public static class BackendConfigurationPropertiesServiceHelper
{
    public static async Task<OperationResult> Create(PropertyCreateModel propertyCreateModel, Core core, int userId,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext, int maxChrNumbers, int maxCvrNumbers)
    {
            var currentListOfCvrNumbers = await backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.CVR).ToListAsync().ConfigureAwait(false);
            var currentListOfChrNumbers = await backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.CHR).ToListAsync().ConfigureAwait(false);
            if (backendConfigurationPnDbContext.Properties.Any(x =>
                    x.CHR == propertyCreateModel.Chr && x.WorkflowState != Constants.WorkflowStates.Removed &&
                    x.CVR == propertyCreateModel.Cvr))
            {
                return new OperationResult(false,
                    "PropertyAlreadyExists");
            }

            if (!currentListOfChrNumbers.Contains(propertyCreateModel.Chr) &&
                currentListOfChrNumbers.Count >= maxChrNumbers)
            {
                return new OperationResult(false,
                    "MaxChrNumbersReached");
            }

            if (!currentListOfCvrNumbers.Contains(propertyCreateModel.Cvr) &&
                currentListOfCvrNumbers.Count >= maxCvrNumbers)
            {
                return new OperationResult(false,
                    "MaxCvrNumbersReached");
            }

            try
            {
                var sdkDbContext = core.DbContextHelper.GetDbContext();

                var planningTag = new PlanningTag
                {
                    Name = $"0. {propertyCreateModel.FullName()}",
                    IsLocked = true
                };
                await planningTag.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
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
                    IsFarm = true,
                    MainMailAddress = propertyCreateModel.MainMailAddress
                };
                await newProperty.Create(backendConfigurationPnDbContext).ConfigureAwait(false);

                var property = newProperty;
                var selectedTranslates = propertyCreateModel.LanguagesIds
                    .Select(x => new PropertySelectedLanguage
                    {
                        LanguageId = x,
                        PropertyId = property.Id,
                        CreatedByUserId = userId,
                        UpdatedByUserId = userId
                    });

                foreach (var selectedTranslate in selectedTranslates)
                {
                    await selectedTranslate.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
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

                newProperty = await CreateTaskManagementFolders(newProperty, sdkDbContext, core);

                // create area select list filled manually
                var areasGroup = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                    $"{newProperty.Name} - Areas", "", true, true).ConfigureAwait(false);
                newProperty.EntitySelectListAreas = areasGroup.Id;
                // create device users select list filled automatically by workers bound to property
                var deviceUsersGroup = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                    $"{newProperty.Name} - Device Users", "", true, false).ConfigureAwait(false);
                newProperty.EntitySelectListDeviceUsers = deviceUsersGroup.Id;

                await newProperty.Update(backendConfigurationPnDbContext).ConfigureAwait(false);

                var translations = new List<KeyValuePair<string, string>> {
                    new("da", "00. Overskredne opgaver"),
                    new("en-US", "00. Overdue tasks"),
                    new("de-DE", "00. Überschrittene Aufgaben"),
                    new("uk-UA", "00. Завдання, що прострочені"),
                    new("pl-PL", "00. Zaległe zadania"),
                    new("no-NO", "00. Forfalte oppgaver"),
                    new("sv-SE", "00. Försenade uppgifter"),
                    new("es-ES", "00. Tareas vencidas"),
                    new("fr-FR", "00. Tâches dépassées"),
                    new("it-IT", "00. Compiti superati"),
                    new("nl-NL", "00. Overschreden taken"),
                    new("pt-BR", "00. Tarefas excedidas"),
                    new("pt-PT", "00. Tarefas excedidas"),
                    new("fi-FI", "00. Ylitetyt tehtävät"),
                    new("tr-TR", "00. Aşılan görevler"),
                    new("et-ET", "00. Ületatud ülesanded"),
                    new("lv-LV", "00. Pārsniegtie uzdevumi"),
                    new("lt-LT", "00. Viršyti uždaviniai"),
                    new("ro-RO", "00. Sarcini depășite"),
                    new("bg-BG", "00. Превишени задачи"),
                    new("sk-SK", "00. Prekročené úlohy"),
                    new("sl-SL", "00. Presežene naloge"),
                    new("is-IS", "00. Yfirskredin verkefni"),
                    new("cs-CZ", "00. Překročené úkoly"),
                    new("hr-HR", "00. Prekoračeni zad")
                };

                var sdkLanguages = await sdkDbContext.Languages.ToListAsync();
                // create a List of CommonTranslationsModel for each language
                var commonTranslations = new List<CommonTranslationsModel>();
                foreach (var translation in translations)
                {
                    var language = sdkLanguages.FirstOrDefault(x => x.LanguageCode == translation.Key);
                    if (language != null)
                    {
                        commonTranslations.Add(new CommonTranslationsModel
                        {
                            Name = translation.Value,
                            LanguageId = language.Id,
                            Description = ""
                        });
                    }
                }

                // create a new folder for exceeded tasks
                await core.FolderCreate(commonTranslations, property.FolderId).ConfigureAwait(false);

                return new OperationResult(true,
                    "SuccessfullyCreatingProperties");
            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
                //Log.LogException(e.Message);
                //Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    "ErrorWhileCreatingProperties");
            }
    }


    public static async Task<OperationResult> Update(PropertiesUpdateModel updateModel, Core core, IUserService userService,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        IBackendConfigurationLocalizationService? localizationService, IBus bus)
    {
        try
        {
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var property = await backendConfigurationPnDbContext.Properties
                .Where(x => x.Id == updateModel.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.SelectedLanguages)
                .Include(x => x.PropertyWorkers)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (property == null)
            {
                return new OperationResult(false, "PropertyNotFound");
            }

            if (backendConfigurationPnDbContext.Properties.Any(x =>
                    x.WorkflowState != Constants.WorkflowStates.Removed
                    && x.CHR == updateModel.Chr
                    && x.CVR == updateModel.Cvr
                    && x.Name == updateModel.Name
                    && x.Address == updateModel.Address
                    && x.Id != updateModel.Id))
            {
                return new OperationResult(false, "PropertyAlreadyExists");
            }

            var pooleForm =
                await sdkDbContext.CheckListTranslations.FirstOrDefaultAsync(x =>
                    x.Text == $"01. Aflæsninger - {property.Name}");

            if (pooleForm != null)
            {
                pooleForm.Text = $"01. Aflæsninger - {updateModel.Name}";
                await pooleForm.Update(sdkDbContext);
            }

            var faceseForm =
                await sdkDbContext.CheckListTranslations.FirstOrDefaultAsync(x =>
                    x.Text == $"02. Fækale uheld - {property.Name}");

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

            var planningTag = await itemsPlanningPnDbContext.PlanningTags
                // ReSharper disable once AccessToModifiedClosure
                .Where(x => x.Id == property.ItemPlanningTagId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (planningTag != null)
            {
                planningTag.Name = updateModel.FullName();
                planningTag.UpdatedByUserId = userService.UserId;
                await planningTag.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
            }

            // var planningTags = await itemsPlanningPnDbContext.PlanningTags
            //     // ReSharper disable once AccessToModifiedClosure
            //     .Where(x => x.Name.StartsWith(property.Name)).ToListAsync();
            //
            // foreach (var tag in planningTags)
            // {
            //     tag.Name = tag.Name.Replace(property.Name, updateModel.FullName());
            //     tag.UpdatedByUserId = userId;
            //     await tag.Update(itemsPlanningPnDbContext).ConfigureAwait(false);
            // }

            var translatesForFolder = await sdkDbContext.Languages
                .Where(x => x.IsActive)
                .Select(
                    x => new CommonTranslationsModel
                    {
                        LanguageId = x.Id,
                        Name = updateModel.Name,
                        Description = updateModel.Address ?? ""
                    })
                .ToListAsync().ConfigureAwait(false);

            await core.FolderUpdate((int)property.FolderId!, translatesForFolder, null).ConfigureAwait(false);

            property.Address = updateModel.Address;
            property.CHR = updateModel.Chr;
            property.CVR = updateModel.Cvr;
            property.Name = updateModel.Name;
            property.UpdatedByUserId = userService.UserId;
            property.MainMailAddress = updateModel.MainMailAddress;

            property = await CreateTaskManagementFolders(property,
                sdkDbContext, core);
            await property.Update(backendConfigurationPnDbContext).ConfigureAwait(false);

            if (property.EntitySelectListDeviceUsers == null)
            {
                var group = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                    $"{property.Name} - Device Users", "", true, false).ConfigureAwait(false);
                property.EntitySelectListDeviceUsers = group.Id;
            }

            var deviceUsersEntityGroup = await sdkDbContext.EntityGroups.FirstAsync(x => x.Id == property.EntitySelectListDeviceUsers)
                .ConfigureAwait(false);

            deviceUsersEntityGroup.Name = $"{property.Name} - Device Users";

            await deviceUsersEntityGroup.Update(sdkDbContext).ConfigureAwait(false);

            if (property.EntitySelectListAreas == null)
            {
                var group = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                    $"{property.Name} - Areas", "", true, true).ConfigureAwait(false);
                property.EntitySelectListAreas = group.Id;
            }

            var areasEntityGroup = await sdkDbContext.EntityGroups.FirstAsync(x => x.Id == property.EntitySelectListAreas)
                .ConfigureAwait(false);

            areasEntityGroup.Name = $"{property.Name} - Areas";

            await areasEntityGroup.Update(sdkDbContext).ConfigureAwait(false);

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

                        if (property.EntitySelectListDeviceUsers == null)
                        {
                            var group = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                                $"{property.Name} - Device Users", "", true, false).ConfigureAwait(false);
                            property.EntitySelectListDeviceUsers = group.Id;
                        }

                        if (property.EntitySelectListAreas == null)
                        {
                            var group = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                                $"{property.Name} - Areas", "", true, true).ConfigureAwait(false);
                            property.EntitySelectListAreas = group.Id;
                        }

                        var deviceUsersGroup = await sdkDbContext.EntityGroups.FirstAsync(x => x.Id == property.EntitySelectListDeviceUsers)
                            .ConfigureAwait(false);

                        var areasGroup = await sdkDbContext.EntityGroups.FirstAsync(x => x.Id == property.EntitySelectListAreas)
                            .ConfigureAwait(false);

                        // read and fill list
                        var entityGroup =
                            await core.EntityGroupRead(deviceUsersGroup.MicrotingUid).ConfigureAwait(false);
                        var nextItemUid = entityGroup.EntityGroupItemLst.Count;
                        for (var i = 0; i < propertyWorkers.Count; i++)
                        {
                            var propertyWorker = propertyWorkers[i];
                            var site = await sdkDbContext.Sites.Where(x => x.Id == propertyWorker.WorkerId)
                                .FirstAsync().ConfigureAwait(false);
                            var entityItem = await core
                                .EntitySelectItemCreate(entityGroup.Id, site.Name, i, nextItemUid.ToString())
                                .ConfigureAwait(false);
                            nextItemUid++;
                            propertyWorker.EntityItemId = entityItem.Id;
                            await propertyWorker.Update(backendConfigurationPnDbContext).ConfigureAwait(false);

                            // todo need change language to site language for correct translates and change back after end translate
                            await WorkOrderHelper.DeployEform(propertyWorker, eformId, property,
                                localizationService,
                                int.Parse(areasGroup.MicrotingUid), int.Parse(deviceUsersGroup.MicrotingUid), core,
                                userService, backendConfigurationPnDbContext, bus, false).ConfigureAwait(false);
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

                        var propertyWorkers = property.PropertyWorkers
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .ToList();

                        await WorkOrderHelper
                            .RetractEform(propertyWorkers, true, core, userService.UserId, backendConfigurationPnDbContext)
                            .ConfigureAwait(false);
                        await WorkOrderHelper
                            .RetractEform(propertyWorkers, false, core, userService.UserId, backendConfigurationPnDbContext)
                            .ConfigureAwait(false);
                        // await WorkOrderHelper
                        //     .RetractEform(propertyWorkerIds, false, core, userService.UserId, backendConfigurationPnDbContext)
                        //     .ConfigureAwait(false);
                        break;
                    }
                }

            }
            property.WorkorderEnable = updateModel.WorkorderEnable;
            var languages = await sdkDbContext.Languages.Where(x => x.IsActive == true)
                .AsNoTracking().ToListAsync().ConfigureAwait(false);

            foreach (var propertySelectedLanguage in property.SelectedLanguages)
            {
                if (languages.All(x => x.Id != propertySelectedLanguage.LanguageId))
                {
                    await propertySelectedLanguage.Delete(backendConfigurationPnDbContext);
                }
            }

            await property.Update(backendConfigurationPnDbContext).ConfigureAwait(false);

            var selectedLanguagesForDelete = property.SelectedLanguages
                .Where(x => !updateModel.LanguagesIds.Contains(x.LanguageId))
                .ToList();

            var selectedLanguagesForCreate = updateModel.LanguagesIds
                .Where(x => !property.SelectedLanguages.Exists(y => y.LanguageId == x))
                .Select(x => new PropertySelectedLanguage
                {
                    LanguageId = x,
                    PropertyId = property.Id,
                    CreatedByUserId = userService.UserId,
                    UpdatedByUserId = userService.UserId
                })
                .ToList();

            foreach (var selectedLanguageForDelete in selectedLanguagesForDelete)
            {
                selectedLanguageForDelete.UpdatedByUserId = userService.UserId;
                await selectedLanguageForDelete.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
            }


            foreach (var selectedLanguageForCreate in selectedLanguagesForCreate)
            {
                selectedLanguageForCreate.UpdatedByUserId = userService.UserId;
                selectedLanguageForCreate.CreatedByUserId = userService.UserId;
                await selectedLanguageForCreate.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
            }

            return new OperationResult(true, "SuccessfullyUpdateProperties");
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
            //Log.LogException(e.Message);
            //Log.LogException(e.StackTrace);
            return new OperationResult(false, "ErrorWhileUpdateProperties");
        }
    }

    private static async Task<Property> CreateTaskManagementFolders(Property property,
        MicrotingDbContext sdkDbContext, Core core)
    {
        var parentFolderTranslation =
            await sdkDbContext.Folders
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Include(x => x.FolderTranslations)
                .Where(x => x.ParentId == null)
                .FirstOrDefaultAsync(x =>
                    x.FolderTranslations.Any(y => y.Name == "00.00 Opret ny opgave"));

        int? parentFolderId;
        var danishLanguage = await sdkDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");
        var englishLanguage = await sdkDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
        var germanLanguage = await sdkDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");

        if (parentFolderTranslation == null)
        {
            var translatesFolderForTasks = new List<CommonTranslationsModel>
            {
                new()
                {
                    Name = "00.00 Opret ny opgave",
                    LanguageId = danishLanguage.Id,
                    Description = ""
                },
                new()
                {
                    Name = "00.00 Create a new task",
                    LanguageId = englishLanguage.Id,
                    Description = ""
                },
                new()
                {
                    Name = "00.00 Erstellen Sie eine neue Aufgabe",
                    LanguageId = germanLanguage.Id,
                    Description = ""
                }
            };
            property.FolderIdForNewTasks =
                await core.FolderCreate(translatesFolderForTasks, null).ConfigureAwait(false);
        }
        else
        {
            property.FolderIdForNewTasks = parentFolderTranslation.Id;
        }
        var folderIdForNewTasks = await sdkDbContext.Folders.SingleAsync(x => x.Id == property.FolderIdForNewTasks);
        folderIdForNewTasks.IsLocked = true;
        folderIdForNewTasks.IsEditable = false;
        folderIdForNewTasks.ManagedByPlugin = true;
        await folderIdForNewTasks.Update(sdkDbContext);

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
                    LanguageId = danishLanguage.Id,
                    Description = ""
                },
                new()
                {
                    Name = "00.02 My other tasks",
                    LanguageId = englishLanguage.Id,
                    Description = ""
                },
                new()
                {
                    Name = "00.02 Meine anderen Aufgaben",
                    LanguageId = germanLanguage.Id,
                    Description = ""
                }
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
                LanguageId = danishLanguage.Id,
                Description = ""
            },
            new()
            {
                Name = property.Name,
                LanguageId = englishLanguage.Id,
                Description = ""
            },
            new()
            {
                Name = property.Name,
                LanguageId = germanLanguage.Id,
                Description = ""
            }
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
        var folderIdForOngoingTasks = await sdkDbContext.Folders.SingleAsync(x => x.Id == property.FolderIdForOngoingTasks);
        folderIdForOngoingTasks.IsLocked = true;
        folderIdForOngoingTasks.IsEditable = false;
        folderIdForOngoingTasks.ManagedByPlugin = true;
        await folderIdForOngoingTasks.Update(sdkDbContext);

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
                    LanguageId = danishLanguage.Id,
                    Description = ""
                },
                new()
                {
                    Name = "00.03 Others' tasks",
                    LanguageId = englishLanguage.Id,
                    Description = ""
                },
                new()
                {
                    Name = "00.03 Aufgaben anderer",
                    LanguageId = germanLanguage.Id,
                    Description = ""
                }
            };
            parentFolderId =
                await core.FolderCreate(translatesFolderForTasks, null).ConfigureAwait(false);
            var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == parentFolderId);
            folder.IsLocked = true;
            folder.IsEditable = false;
            folder.ManagedByPlugin = true;
            await folder.Update(sdkDbContext);
        }
        else
        {
            parentFolderId = parentFolderTranslation.Id;
        }

        translateFolderForNewTask =
        [
            new()
            {
                Name = property.Name,
                LanguageId = danishLanguage.Id,
                Description = ""
            },

            new()
            {
                Name = property.Name,
                LanguageId = englishLanguage.Id,
                Description = ""
            },

            new()
            {
                Name = property.Name,
                LanguageId = germanLanguage.Id,
                Description = ""
            }
        ];

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
        var folderIdForCompletedTasks = await sdkDbContext.Folders.SingleAsync(x => x.Id == property.FolderIdForCompletedTasks);
        folderIdForCompletedTasks.IsLocked = true;
        folderIdForCompletedTasks.IsEditable = false;
        folderIdForCompletedTasks.ManagedByPlugin = true;
        await folderIdForCompletedTasks.Update(sdkDbContext);

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
                    LanguageId = danishLanguage.Id,
                    Description = ""
                },
                new()
                {
                    Name = "00.01 My urgent tasks",
                    LanguageId = englishLanguage.Id,
                    Description = ""
                },
                new()
                {
                    Name = "00.01 Meine dringenden Aufgaben",
                    LanguageId = germanLanguage.Id,
                    Description = ""
                }
            };
            parentFolderId =
                await core.FolderCreate(translatesFolderForTasks, null).ConfigureAwait(false);
        }
        else
        {
            parentFolderId = parentFolderTranslation.Id;
        }

        translateFolderForNewTask =
        [
            new()
            {
                Name = property.Name,
                LanguageId = danishLanguage.Id,
                Description = ""
            },

            new()
            {
                Name = property.Name,
                LanguageId = englishLanguage.Id,
                Description = ""
            },

            new()
            {
                Name = property.Name,
                LanguageId = germanLanguage.Id,
                Description = ""
            }
        ];

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