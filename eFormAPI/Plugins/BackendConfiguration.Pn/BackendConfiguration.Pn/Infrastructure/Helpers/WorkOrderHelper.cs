using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.TaskManagement;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using eFormCore;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Rebus.Bus;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public static class WorkOrderHelper
{
    public static async Task WorkorderFlowDeployEform(List<PropertyWorker> propertyWorkers, Core core, IUserService userService,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext, [CanBeNull] IBackendConfigurationLocalizationService localizationService, IBus bus)
    {
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        foreach (var propertyWorker in propertyWorkers.Where(x => x.TaskManagementEnabled == true))
        {
            var property = await backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkorderEnable)
                .Where(x => x.Id == propertyWorker.PropertyId)
                .Include(x => x.PropertyWorkers)
                .ThenInclude(x => x.WorkorderCases)
                .ThenInclude(x => x.ParentWorkorderCase)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (property == null)
            {
                continue;
            }

            var eformIdForNewTasks = await sdkDbContext.CheckLists
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.OriginalId == "142663new2")
                .Select(x => x.Id)
                .FirstAsync().ConfigureAwait(false);

            // var workorderCasesCompleted = property.PropertyWorkers
            //     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            //     .SelectMany(x => x.WorkorderCases)
            //     .Where(y => y.CaseStatusesEnum == CaseStatusesEnum.Completed)
            //     .ToList();
            // var workorderCasesOngoing = property.PropertyWorkers
            //     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            //     .SelectMany(x => x.WorkorderCases)
            //     .Where(y => y.CaseStatusesEnum == CaseStatusesEnum.Ongoing)
            //     .Where(x => workorderCasesCompleted.All(y => y.ParentWorkorderCaseId != x.ParentWorkorderCaseId))
            //     .ToList();

            if (property.EntitySelectListAreas == null)
            {
                var areasGroup = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                    $"{property.Name} - Områder", "", true, true).ConfigureAwait(false);
                property.EntitySelectListAreas = areasGroup.Id;
                await property.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
            }

            if (property.EntitySelectListDeviceUsers == null)
            {
                var deviceUsersGp = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                    $"{property.Name} - Device Users", "", true, false).ConfigureAwait(false);
                property.EntitySelectListDeviceUsers = deviceUsersGp.Id;
                await property.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
            }

            var areasGroupUid = await sdkDbContext.EntityGroups
                .Where(x => x.Id == property.EntitySelectListAreas)
                .Select(x => x.MicrotingUid)
                .SingleAsync().ConfigureAwait(false);

            var deviceUsersGroupUid = await sdkDbContext.EntityGroups
                .Where(x => x.Id == property.EntitySelectListDeviceUsers)
                .Select(x => x.MicrotingUid)
                .SingleAsync().ConfigureAwait(false);

            var deviceUsersGroup = await core.EntityGroupRead(deviceUsersGroupUid).ConfigureAwait(false);
            var nextItemUid = deviceUsersGroup.EntityGroupItemLst.Count;

            var site = await sdkDbContext.Sites.Where(x => x.Id == propertyWorker.WorkerId)
                .FirstAsync().ConfigureAwait(false);

            if (propertyWorker.EntityItemId != null) {
                var entityItem = await sdkDbContext.EntityItems
                    .Where(x => x.Id == propertyWorker.EntityItemId)
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                await core.EntityItemUpdate(entityItem.Id, site.Name, entityItem.Description,
                    entityItem.EntityItemUid, nextItemUid).ConfigureAwait(false);
            } else
            {
                if (propertyWorker.WorkflowState != Constants.WorkflowStates.Removed)
                {
                    var entityItem = await core
                        .EntitySelectItemCreate(deviceUsersGroup.Id, site.Name, 0, nextItemUid.ToString())
                        .ConfigureAwait(false);
                    propertyWorker.EntityItemId = entityItem.Id;
                    await propertyWorker.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                }
            }

            var entityItems = await sdkDbContext.EntityItems
                .Where(x => x.EntityGroupId == deviceUsersGroup.Id)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .OrderBy(x => x.Name)
                .AsNoTracking()
                .ToListAsync().ConfigureAwait(false);

            int entityItemIncrementer = 0;
            foreach (var entity in entityItems)
            {
                await core.EntityItemUpdate(entity.Id, entity.Name, entity.Description,
                    entity.EntityItemUid, entityItemIncrementer).ConfigureAwait(false);
                entityItemIncrementer++;
            }

            if (propertyWorker.TaskManagementEnabled == true || propertyWorker.TaskManagementEnabled == null)
            {
                if (propertyWorker.WorkflowState != Constants.WorkflowStates.Removed)
                {
                    await DeployEform(propertyWorker, eformIdForNewTasks, property, localizationService,
                        int.Parse(areasGroupUid), int.Parse(deviceUsersGroupUid), core, userService, backendConfigurationPnDbContext, bus).ConfigureAwait(false);
                }
            }
        }
    }

    public static  async Task DeployEform(PropertyWorker propertyWorker, int eformId, Property property,
        [CanBeNull] IBackendConfigurationLocalizationService localizationService, int? areasGroupUid, int? deviceUsersGroupId, Core core, IUserService userService, BackendConfigurationPnDbContext backendConfigurationPnDbContext, IBus bus)
    {
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        if (backendConfigurationPnDbContext.WorkorderCases.Any(x =>
                x.PropertyWorkerId == propertyWorker.Id
                && x.CaseStatusesEnum == CaseStatusesEnum.NewTask
                && x.WorkflowState != Constants.WorkflowStates.Removed))
        {
            // find all cases that are not removed and assignedTo is equal to site.name and update them with the new name
            var workorderCases = await backendConfigurationPnDbContext.WorkorderCases
                .Where(x => x.PropertyWorkerId == propertyWorker.Id)
                .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.NewTask)
                .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.Completed)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToListAsync().ConfigureAwait(false);
            foreach (var workorderCase in workorderCases)
            {
                await BackendConfigurationTaskManagementHelper.UpdateTask(new WorkOrderCaseUpdateModel() {Id = workorderCase.Id, AssignedSiteId = propertyWorker.WorkerId, Description = workorderCase.Description, Priority = int.Parse(workorderCase.Priority)},
                    localizationService, core, userService, backendConfigurationPnDbContext, bus).ConfigureAwait(false);
                // var caseDto = await core.CaseLookupMUId(workorderCase.CaseId).ConfigureAwait(false);
                // if (caseDto.SiteName != property.Name)
                // {
                //     await core.CaseUpdateMUId(workorderCase.CaseId, property.Name).ConfigureAwait(false);
                // }
            }


            return;
        }
        var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.WorkerId).ConfigureAwait(false);
        var language = await sdkDbContext.Languages.SingleAsync(x => x.Id == site.LanguageId).ConfigureAwait(false);
        var mainElement = await core.ReadeForm(eformId, language).ConfigureAwait(false);

        string description = $"<strong>Location:</strong> {property.Name}";
        string newTask = "New task";

        if (localizationService != null)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(language.LanguageCode);
            description = "<strong>"+localizationService.GetString("Location") + "</strong>: " + property.Name;
            newTask = localizationService.GetString("NewTask");
        }

        mainElement.Repeated = 0;
        mainElement.ElementList[0].QuickSyncEnabled = true;
        mainElement.ElementList[0].Description.InderValue = description;
        mainElement.ElementList[0].Label = newTask;
        mainElement.Label = newTask;
        mainElement.EnableQuickSync = true;
        if (property.FolderIdForNewTasks != null)
        {
            mainElement.CheckListFolderName = await sdkDbContext.Folders
                .Where(x => x.Id == property.FolderIdForNewTasks)
                .Select(x => x.MicrotingUid.ToString())
                .FirstOrDefaultAsync().ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(description))
        {
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Description.InderValue = description;
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Label = " ";
        }

        if (areasGroupUid != null && deviceUsersGroupId != null)
        {
            ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[2]).Source = (int)areasGroupUid;
            ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[6]).Source =
                (int)deviceUsersGroupId;
        }
        // else if (areasGroupUid == null && deviceUsersGroupId != null)
        // {
        //     ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[4]).Source =
        //         (int)deviceUsersGroupId;
        // }

        mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
        mainElement.StartDate = DateTime.Now.ToUniversalTime();
        var caseId = await core.CaseCreate(mainElement, "", (int)site.MicrotingUid, property.FolderIdForNewTasks).ConfigureAwait(false);
        await new WorkorderCase
        {
            CaseId = (int)caseId,
            PropertyWorkerId = propertyWorker.Id,
            CaseStatusesEnum = CaseStatusesEnum.NewTask,
            CreatedByUserId = userService.UserId,
            UpdatedByUserId = userService.UserId,
        }.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
    }

    public static async Task RetractEform(List<PropertyWorker> propertyWorkers, bool newWorkOrder, Core core, int userId, BackendConfigurationPnDbContext backendConfigurationPnDbContext)
    {
        foreach (var propertyWorker in propertyWorkers)
        {
            if (newWorkOrder)
            {
                var workOrderCase = await backendConfigurationPnDbContext.WorkorderCases
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PropertyWorkerId == propertyWorker.Id)
                    .Where(x => x.CaseStatusesEnum == CaseStatusesEnum.NewTask)
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                if (workOrderCase != null)
                {
                    try
                    {
                        await core.CaseDelete(workOrderCase.CaseId).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        // throw;
                    }
                    // await core.CaseDelete(workorderCase.CaseId);
                    workOrderCase.UpdatedByUserId = userId;
                    await workOrderCase.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
                }

            }
            else
            {
                // var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.WorkerId);
                // var pWorkers = await _backendConfigurationPnDbContext.PropertyWorkers.Where(x =>
                //     x.WorkerId == propertyWorker.WorkerId
                //     && x.PropertyId == propertyWorker.PropertyId).ToListAsync().ConfigureAwait(false);
                //
                // foreach (var pWorker in pWorkers)
                // {
                    var workOrderCases = await backendConfigurationPnDbContext.WorkorderCases.Where(x =>
                            x.PropertyWorkerId == propertyWorker.Id
                            && x.WorkflowState != Constants.WorkflowStates.Removed)
                        .ToListAsync().ConfigureAwait(false);
                    foreach (var workOrderCase in workOrderCases)
                    {
                        try
                        {
                            await core.CaseDelete(workOrderCase.CaseId).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                        workOrderCase.UpdatedByUserId = userId;
                        await workOrderCase.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
                    }
                //}
            }
        }
    }
}