using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public class WorkOrderHelper
{
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
    private readonly IEFormCoreService _coreHelper;
    private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
    public WorkOrderHelper(IEFormCoreService coreHelper, BackendConfigurationPnDbContext backendConfigurationPnDbContext, IBackendConfigurationLocalizationService backendConfigurationLocalizationService)
    {
        _coreHelper = coreHelper;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
    }

    public async Task WorkorderFlowDeployEform(List<PropertyWorker> propertyWorkers)
    {
        var core = await _coreHelper.GetCore();
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        foreach (var propertyWorker in propertyWorkers)
        {
            var property = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkorderEnable)
                .Where(x => x.Id == propertyWorker.PropertyId)
                .Include(x => x.PropertyWorkers)
                .ThenInclude(x => x.WorkorderCases)
                .ThenInclude(x => x.ParentWorkorderCase)
                .FirstOrDefaultAsync();

            if (property == null)
            {
                continue;
            }
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

                await property.Update(_backendConfigurationPnDbContext);

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
                property.FolderIdForOngoingTasks =
                    await core.FolderCreate(translateFolderForOngoingTask, property.FolderIdForTasks);

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
                property.FolderIdForCompletedTasks =
                    await core.FolderCreate(translateFolderForCompletedTask, property.FolderIdForTasks);
            }

            var eformIdForNewTasks = await sdkDbContext.CheckListTranslations
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Text == "01. New task")
                .Select(x => x.CheckListId)
                .FirstAsync();

            var workorderCasesCompleted = property.PropertyWorkers
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .SelectMany(x => x.WorkorderCases)
                .Where(y => y.CaseStatusesEnum == CaseStatusesEnum.Completed)
                .ToList();
            var workorderCasesOngoing = property.PropertyWorkers
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .SelectMany(x => x.WorkorderCases)
                .Where(y => y.CaseStatusesEnum == CaseStatusesEnum.Ongoing)
                .Where(x => workorderCasesCompleted.All(y => y.ParentWorkorderCaseId != x.ParentWorkorderCaseId))
                .ToList();

            if (property.EntitySelectListAreas == null)
            {
                var areasGroup = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                    $"{property.Name} - Områder", "", true, true);
                property.EntitySelectListAreas = areasGroup.Id;
                await property.Update(_backendConfigurationPnDbContext);
            }

            if (property.EntitySelectListDeviceUsers == null) {
                var deviceUsersGp = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                    $"{property.Name} - Device Users", "", true, false);
                property.EntitySelectListDeviceUsers = deviceUsersGp.Id;
                await property.Update(_backendConfigurationPnDbContext);
            }

            var areasGroupUid = await sdkDbContext.EntityGroups
                .Where(x => x.Id == property.EntitySelectListAreas)
                .Select(x => x.MicrotingUid)
                .SingleAsync();

            var deviceUsersGroupUid = await sdkDbContext.EntityGroups
                .Where(x => x.Id == property.EntitySelectListDeviceUsers)
                .Select(x => x.MicrotingUid)
                .SingleAsync();

            var deviceUsersGroup = await core.EntityGroupRead(deviceUsersGroupUid);
            var nextItemUid = deviceUsersGroup.EntityGroupItemLst.Count;

            var site = await sdkDbContext.Sites.Where(x => x.Id == propertyWorker.WorkerId)
                .FirstAsync();
            var entityItem = await core.EntitySelectItemCreate(deviceUsersGroup.Id, site.Name, 0, nextItemUid.ToString());
            propertyWorker.EntityItemId = entityItem.Id;
            await propertyWorker.Update(_backendConfigurationPnDbContext);

            var entityItems = await sdkDbContext.EntityItems
                .Where(x => x.EntityGroupId == deviceUsersGroup.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .OrderBy(x => x.Name)
                .AsNoTracking()
                .ToListAsync();

            int entityItemIncrementer = 0;
            foreach (var entity in entityItems)
            {
                await core.EntityItemUpdate(entity.Id, entity.Name, entity.Description,
                    entity.EntityItemUid, entityItemIncrementer);
                entityItemIncrementer++;
            }

            await DeployEform(propertyWorker, eformIdForNewTasks, property.FolderIdForNewTasks,
                $"<strong>{_backendConfigurationLocalizationService.GetString("Location")}:</strong> {property.Name}", int.Parse(areasGroupUid), int.Parse(deviceUsersGroupUid));
        }
    }

    public async Task DeployEform(PropertyWorker propertyWorker, int eformId, int? folderId,
        string description, int? areasGroupUid, int? deviceUsersGroupId)
    {
        var core = await _coreHelper.GetCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();
            if (_backendConfigurationPnDbContext.WorkorderCases.Any(x =>
                    x.PropertyWorkerId == propertyWorker.Id
                    && x.CaseStatusesEnum == CaseStatusesEnum.NewTask
                    && x.WorkflowState != Constants.WorkflowStates.Removed))
            {
                return;
            }
            var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.WorkerId);
            var language = await sdkDbContext.Languages.SingleAsync(x => x.Id == site.LanguageId);
            var mainElement = await core.ReadeForm(eformId, language);
            mainElement.Repeated = 0;
            mainElement.ElementList[0].QuickSyncEnabled = true;
            mainElement.EnableQuickSync = true;
            if (folderId != null)
            {
                mainElement.CheckListFolderName = await sdkDbContext.Folders
                    .Where(x => x.Id == folderId)
                    .Select(x => x.MicrotingUid.ToString())
                    .FirstOrDefaultAsync();
            }

            if (!string.IsNullOrEmpty(description))
            {
                ((DataElement)mainElement.ElementList[0]).DataItemList[0].Description.InderValue = description;
                ((DataElement)mainElement.ElementList[0]).DataItemList[0].Label = " ";
            }

            if (areasGroupUid != null && deviceUsersGroupId != null)
            {
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[1]).Source = (int)areasGroupUid;
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[5]).Source =
                    (int)deviceUsersGroupId;
            }
            else if (areasGroupUid == null && deviceUsersGroupId != null)
            {
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[4]).Source =
                    (int)deviceUsersGroupId;
            }

            mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
            mainElement.StartDate = DateTime.Now.ToUniversalTime();
            var caseId = await core.CaseCreate(mainElement, "", (int)site.MicrotingUid, folderId);
            await new WorkorderCase
            {
                CaseId = (int)caseId,
                PropertyWorkerId = propertyWorker.Id,
                CaseStatusesEnum = CaseStatusesEnum.NewTask,
            }.Create(_backendConfigurationPnDbContext);
    }

    public async Task RetractEform(List<PropertyWorker> propertyWorkers, bool newWorkOrder)
    {
        var core = await _coreHelper.GetCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();
        foreach (var propertyWorker in propertyWorkers)
        {
            if (newWorkOrder)
            {
                var workorderCase = await _backendConfigurationPnDbContext.WorkorderCases
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PropertyWorkerId == propertyWorker.Id)
                    .FirstOrDefaultAsync();
                if (workorderCase != null)
                {
                    await core.CaseDelete(workorderCase.CaseId);
                    await workorderCase.Delete(_backendConfigurationPnDbContext);
                }

            }
            else
            {
                // var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.WorkerId);
                var pWorkers = await _backendConfigurationPnDbContext.PropertyWorkers.Where(x =>
                    x.WorkerId == propertyWorker.WorkerId
                    && x.PropertyId == propertyWorker.PropertyId).ToListAsync();

                foreach (var pWorker in pWorkers)
                {
                    var workorderCases = await _backendConfigurationPnDbContext.WorkorderCases.Where(x =>
                        x.PropertyWorkerId == pWorker.Id
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
                        .ToListAsync();
                    foreach (var workorderCase in workorderCases)
                    {
                        try
                        {
                            await core.CaseDelete(workorderCase.CaseId);
                        } catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                        await workorderCase.Delete(_backendConfigurationPnDbContext);
                    }
                }
            }

            // await core.CaseDelete(eformId, (int)site.MicrotingUid);

        }
    }
}