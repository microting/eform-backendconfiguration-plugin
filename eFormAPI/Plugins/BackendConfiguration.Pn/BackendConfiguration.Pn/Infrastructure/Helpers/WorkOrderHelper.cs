using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Dto;
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
    private readonly IUserService _userService;
    public WorkOrderHelper(
        IEFormCoreService coreHelper,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        IBackendConfigurationLocalizationService backendConfigurationLocalizationService,
        IUserService userService
        )
    {
        _coreHelper = coreHelper;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
        _userService = userService;
    }

    public async Task WorkorderFlowDeployEform(List<PropertyWorker> propertyWorkers)
    {
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        foreach (var propertyWorker in propertyWorkers)
        {
            var property = await _backendConfigurationPnDbContext.Properties
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
                await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
            }

            if (property.EntitySelectListDeviceUsers == null)
            {
                var deviceUsersGp = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                    $"{property.Name} - Device Users", "", true, false).ConfigureAwait(false);
                property.EntitySelectListDeviceUsers = deviceUsersGp.Id;
                await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
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
            var entityItem = await core.EntitySelectItemCreate(deviceUsersGroup.Id, site.Name, 0, nextItemUid.ToString()).ConfigureAwait(false);
            propertyWorker.EntityItemId = entityItem.Id;
            await propertyWorker.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);

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
                await DeployEform(propertyWorker, eformIdForNewTasks, property,
                    $"<strong>{_backendConfigurationLocalizationService.GetString("Location")}:</strong> {property.Name}",
                    int.Parse(areasGroupUid), int.Parse(deviceUsersGroupUid)).ConfigureAwait(false);
            }
        }
    }

    public async Task DeployEform(PropertyWorker propertyWorker, int eformId, Property property,
        string description, int? areasGroupUid, int? deviceUsersGroupId)
    {
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        await using var _ = sdkDbContext.ConfigureAwait(false);
        if (_backendConfigurationPnDbContext.WorkorderCases.Any(x =>
                x.PropertyWorkerId == propertyWorker.Id
                && x.CaseStatusesEnum == CaseStatusesEnum.NewTask
                && x.WorkflowState != Constants.WorkflowStates.Removed))
        {
            return;
        }
        var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.WorkerId).ConfigureAwait(false);
        var language = await sdkDbContext.Languages.SingleAsync(x => x.Id == site.LanguageId).ConfigureAwait(false);
        var mainElement = await core.ReadeForm(eformId, language).ConfigureAwait(false);
        mainElement.Repeated = 0;
        mainElement.ElementList[0].QuickSyncEnabled = true;
        mainElement.ElementList[0].Description.InderValue = description;
        mainElement.ElementList[0].Label = "Ny opgave";
        mainElement.Label = "Ny opgave";
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
            CreatedByUserId = _userService.UserId,
            UpdatedByUserId = _userService.UserId,
        }.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
    }

    public async Task RetractEform(List<PropertyWorker> propertyWorkers, bool newWorkOrder)
    {
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        await using var _ = sdkDbContext.ConfigureAwait(false);
        foreach (var propertyWorker in propertyWorkers)
        {
            if (newWorkOrder)
            {
                var workorderCase = await _backendConfigurationPnDbContext.WorkorderCases
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PropertyWorkerId == propertyWorker.Id)
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                if (workorderCase != null)
                {
                    try
                    {
                        await core.CaseDelete(workorderCase.CaseId).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        // throw;
                    }
                    // await core.CaseDelete(workorderCase.CaseId);
                    workorderCase.UpdatedByUserId = _userService.UserId;
                    await workorderCase.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }

            }
            else
            {
                // var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.WorkerId);
                var pWorkers = await _backendConfigurationPnDbContext.PropertyWorkers.Where(x =>
                    x.WorkerId == propertyWorker.WorkerId
                    && x.PropertyId == propertyWorker.PropertyId).ToListAsync().ConfigureAwait(false);

                foreach (var pWorker in pWorkers)
                {
                    var workorderCases = await _backendConfigurationPnDbContext.WorkorderCases.Where(x =>
                            x.PropertyWorkerId == pWorker.Id
                            && x.WorkflowState != Constants.WorkflowStates.Removed)
                        .ToListAsync().ConfigureAwait(false);
                    foreach (var workorderCase in workorderCases)
                    {
                        try
                        {
                            await core.CaseDelete(workorderCase.CaseId).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                        workorderCase.UpdatedByUserId = _userService.UserId;
                        await workorderCase.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }
                }
            }

            // await core.CaseDelete(eformId, (int)site.MicrotingUid);

        }
    }
}