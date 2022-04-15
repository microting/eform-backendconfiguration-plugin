/*
The MIT License (MIT)

Copyright (c) 2007 - 2022 Microting A/S

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

namespace BackendConfiguration.Pn.Services.BackendConfigurationTaskManagementService;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfigurationLocalizationService;
using Infrastructure.Helpers;
using Infrastructure.Models.TaskManagement;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

public class BackendConfigurationTaskManagementService: IBackendConfigurationTaskManagementService
{
    private readonly IEFormCoreService _coreHelper;
    private readonly IBackendConfigurationLocalizationService _localizationService;
    private readonly IUserService _userService;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
    // private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;
    private readonly WorkOrderHelper _workOrderHelper;

    public BackendConfigurationTaskManagementService(
        IBackendConfigurationLocalizationService localizationService,
        IEFormCoreService coreHelper, IUserService userService,
        // ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext
        )
    {
        _localizationService = localizationService;
        _coreHelper = coreHelper;
        _userService = userService;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        // _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
        _workOrderHelper = new WorkOrderHelper(_coreHelper, _backendConfigurationPnDbContext, _localizationService, _userService);
    }

    public async Task<List<WorkorderCaseModel>> GetReport(TaskManagementFiltersModel filtersModel)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var query = _backendConfigurationPnDbContext.WorkorderCases
                .Include(x => x.PropertyWorker)
                .ThenInclude(x => x.Property)
                .Where(x => x.PropertyWorker.PropertyId == filtersModel.PropertyId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => filtersModel.AreaName == x.SelectedAreaName)
                .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.NewTask);

            if (filtersModel.Status != null)
            {
                query = filtersModel.Status switch
                {
                    -1 => query.Where(x =>
                        x.CaseStatusesEnum == CaseStatusesEnum.Ongoing ||
                        x.CaseStatusesEnum == CaseStatusesEnum.Completed),
                    1 => query.Where(x => x.CaseStatusesEnum == CaseStatusesEnum.Ongoing),
                    2 => query.Where(x => x.CaseStatusesEnum == CaseStatusesEnum.Completed),
                    _ => query
                };
            }
            if (!string.IsNullOrEmpty(filtersModel.CreatedBy))
            {
                query = query.Where(x => x.CreatedByName == filtersModel.CreatedBy);
            }

            if (filtersModel.DateFrom.HasValue && filtersModel.DateTo.HasValue)
            {
                query = query
                    .Where(x => x.CreatedAt >= filtersModel.DateFrom.Value)
                    .Where(x => x.CreatedAt <= filtersModel.DateTo.Value);
            }

            query = QueryHelper.AddFilterAndSortToQuery(query, filtersModel, new List<string>(), new List<string>()
            {
                "Id", "Property", "Area", "CreatedByName", "CreatedByText", "LastAssignedTo", "Description", "Status",
            });

            var workorderCasesFromDb = await query
                .Select(x => new 
                {
                    AreaName = x.SelectedAreaName,
                    x.CreatedByName,
                    x.CreatedByText,
                    CreatedDate = x.CreatedAt,
                    x.Id,
                    Status = x.CaseStatusesEnum.ToString(),
                    x.Description,
                    PropertyName = x.PropertyWorker.Property.Name,
                    LastUpdateDate = x.UpdatedAt,
                    x.CaseId
                })
                .ToListAsync();
            var workorderCases = new List<WorkorderCaseModel>();
            foreach (var workorderCaseModel in workorderCasesFromDb)
            {
                var assignedSiteName = await sdkDbContext.Cases
                    .Where(x => x.MicrotingUid == workorderCaseModel.CaseId || x.Id == workorderCaseModel.CaseId)
                    .Include(x => x.Site)
                    .Select(x => x.Site.Name)
                    .FirstOrDefaultAsync();
                workorderCases.Add(new WorkorderCaseModel
                {
                    AreaName = workorderCaseModel.AreaName,
                    CreatedByName = workorderCaseModel.CreatedByName,
                    CreatedByText = workorderCaseModel.CreatedByText,
                    CreatedDate = workorderCaseModel.CreatedDate,
                    Id = workorderCaseModel.Id,
                    Status = workorderCaseModel.Status,
                    Description = workorderCaseModel.Description,
                    PropertyName = workorderCaseModel.PropertyName,
                    LastUpdateDate = workorderCaseModel.LastUpdateDate,
                    LastAssignedTo = assignedSiteName,
                });
            }
            if (!string.IsNullOrEmpty(filtersModel.LastAssignedTo))
            {
                workorderCases = workorderCases.Where(x => x.LastAssignedTo == filtersModel.LastAssignedTo).ToList();
            }

            return workorderCases;
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            throw;
        }
    }

    public async Task<OperationDataResult<WorkOrderCaseReadModel>> GetTaskById(int workOrderCaseId)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var task = await _backendConfigurationPnDbContext.WorkorderCases
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == workOrderCaseId)
                .Include(x => x.PropertyWorker)
                .Select(x => new 
                {
                    x.SelectedAreaName,
                    x.Description,
                    x.Id,
                    x.CaseId,
                    x.PropertyWorker.PropertyId,
                }).FirstOrDefaultAsync();
            if (task == null)
            {
                return new OperationDataResult<WorkOrderCaseReadModel>(false,
                    _localizationService.GetString("TaskNotFound"));
            }

            var uploadIds = await _backendConfigurationPnDbContext.WorkorderCaseImages
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.WorkorderCaseId == task.Id)
                .Select(x => x.UploadedDataId)
                .ToListAsync();

            var fileNames = new List<string>();
            foreach (var uploadId in uploadIds)
            {
                var fileName = await sdkDbContext.UploadedDatas
                    .Where(x => x.Id == uploadId)
                    .Select(x => x.FileName)
                    .FirstOrDefaultAsync();
                fileNames.Add(fileName);
            }

            var assignedSiteId = await sdkDbContext.Cases
                .Where(x => x.MicrotingUid == task.CaseId || x.Id == task.CaseId)
                .Include(x => x.Site)
                .Select(x => x.Site.Id)
                .FirstOrDefaultAsync();

            var taskForReturn = new WorkOrderCaseReadModel
            {
                AreaName = task.SelectedAreaName,
                AssignedSiteId = assignedSiteId,
                Description = task.Description,
                Id = task.Id,
                PictureNames = fileNames,
                PropertyId = task.PropertyId,
            };
            return new OperationDataResult<WorkOrderCaseReadModel>(true, taskForReturn);
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationDataResult<WorkOrderCaseReadModel>(false,
                $"{_localizationService.GetString("ErrorWhileReadTask")}: {e.Message}");
        }
    }

    public async Task<OperationDataResult<List<string>>> GetItemsEntityListByPropertyId(int propertyId)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var entityListId = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == propertyId)
                .Select(x => x.EntitySelectListAreas)
                .FirstOrDefaultAsync();
            if (entityListId == null)
            {
                return new OperationDataResult<List<string>>(false,
                    _localizationService.GetString("PropertyNotFoundOrEntityListNotCreated"));
            }

            var items = await sdkDbContext.EntityItems
                .Where(x => x.EntityGroupId == entityListId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.Name)
                .ToListAsync();
            return new OperationDataResult<List<string>>(true, items);
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationDataResult<List<string>>(false,
                $"{_localizationService.GetString("ErrorWhileReadEntityList")}: {e.Message}");
        }
    }

    public async Task<OperationResult> DeleteTaskById(int workOrderCaseId)
    {
        try
        {
            var task = await _backendConfigurationPnDbContext.WorkorderCases
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == workOrderCaseId)
                .Include(x => x.PropertyWorker)
                .FirstOrDefaultAsync();

            if (task == null)
            {
                return new OperationDataResult<WorkOrderCaseReadModel>(false,
                    _localizationService.GetString("TaskNotFound"));
            }

            var allChildTasks = await _backendConfigurationPnDbContext.WorkorderCases
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.ParentWorkorderCaseId == task.ParentWorkorderCaseId)
                .ToListAsync();

            var parentTasks = await _backendConfigurationPnDbContext.WorkorderCases
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == task.ParentWorkorderCaseId)
                .FirstAsync();

            var propertyWorkerList = new List<PropertyWorker> { task.PropertyWorker };
            await _workOrderHelper.RetractEform(propertyWorkerList, true);
            await _workOrderHelper.RetractEform(propertyWorkerList, false);
            await _workOrderHelper.RetractEform(propertyWorkerList, false);

            foreach (var childTask in allChildTasks)
            {
                childTask.UpdatedByUserId = _userService.UserId;
                await childTask.Delete(_backendConfigurationPnDbContext);
            }
            parentTasks.UpdatedByUserId = _userService.UserId;
            await parentTasks.Delete(_backendConfigurationPnDbContext);
            return new OperationResult(true, _localizationService.GetString("TaskDeletedSuccessful"));
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationResult(false,
                $"{_localizationService.GetString("ErrorWhileDeleteTask")}: {e.Message}");
        }
    }

    public async Task<OperationResult> CreateTask(WorkOrderCaseCreateModel createModel)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var property = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == createModel.PropertyId)
                .Include(x => x.PropertyWorkers)
                .FirstOrDefaultAsync();
            if (property == null)
            {
                return new OperationDataResult<WorkOrderCaseReadModel>(false,
                    _localizationService.GetString("PropertyNotFound"));
            }

            var propertyWorker = property.PropertyWorkers.First(x => x.WorkerId == createModel.AssignedSiteId);

            var eformId = await sdkDbContext.CheckListTranslations
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Text == "01. New task")
                .Select(x => x.CheckListId)
                .FirstAsync();

            var areasGroupUid = await sdkDbContext.EntityGroups
                .Where(x => x.Id == property.EntitySelectListAreas)
                .Select(x => int.Parse(x.MicrotingUid))
                .FirstAsync();

            var deviceUsersGroup = await sdkDbContext.EntityGroups
                .Where(x => x.Id == property.EntitySelectListDeviceUsers)
                .Select(x => int.Parse(x.MicrotingUid))
                .FirstAsync();

            var defaultValueForArea = await sdkDbContext.EntityItems
                .Where(x => x.EntityGroupId == property.EntitySelectListAreas)
                .Where(x => x.Name == createModel.AreaName)
                .Select(x => int.Parse(x.MicrotingUid))
                .FirstOrDefaultAsync();

            var workOrderCaseId = await DeployEform(propertyWorker, eformId, property.FolderIdForNewTasks,
                $"<strong>{_localizationService.GetString("Location")}:</strong> {property.Name} <br>{createModel.Description}",
                defaultValueForArea, areasGroupUid, deviceUsersGroup);
            if(workOrderCaseId != 0 && createModel.Files.Any())
            {
                var folder = Path.Combine("pictures-for-case");
                Directory.CreateDirectory(folder);
                foreach (var picture in createModel.Files)
                {
                    var filePath = Path.Combine(folder, $"{DateTime.Now.Ticks}.{picture.ContentType}");
                    // ReSharper disable once UseAwaitUsing
                    using (var stream = new FileStream(filePath, FileMode.Create))
                        // if you replace using to await using - stream not start copy until it goes beyond the current block
                    {
                        await picture.CopyToAsync(stream);
                    }

                    await core.PutFileToStorageSystem(filePath, picture.FileName);
                    var hash = await core.PdfUpload(filePath);

                    var uploadData = new Microting.eForm.Infrastructure.Data.Entities.UploadedData
                    {
                        Checksum = hash,
                        FileName = picture.FileName,
                        FileLocation = filePath,
                    };
                    await uploadData.Create(sdkDbContext);

                    await new WorkorderCaseImage
                    {
                        WorkorderCaseId = workOrderCaseId,
                        UploadedDataId = uploadData.Id,
                        CreatedByUserId = _userService.UserId,
                    }.Create(_backendConfigurationPnDbContext);
                }
            }
            return new OperationResult(true, _localizationService.GetString("TaskDeletedSuccessful"));
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationResult(false,
                $"{_localizationService.GetString("ErrorWhileDeleteTask")}: {e.Message}");
        }
    }

    private async Task<int> DeployEform(PropertyWorker propertyWorker, int eformId, int? folderId,
        string description, int defaultValueForArea, int? areasGroupUid, int? deviceUsersGroupId)
    {
        var core = await _coreHelper.GetCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();
        if (_backendConfigurationPnDbContext.WorkorderCases.Any(x =>
                x.PropertyWorkerId == propertyWorker.Id
                && x.CaseStatusesEnum == CaseStatusesEnum.NewTask
                && x.WorkflowState != Constants.WorkflowStates.Removed))
        {
            return 0;
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
            ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[1]).DefaultValue = defaultValueForArea;
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
        var workOrderCase = new WorkorderCase
        {
            CaseId = (int)caseId,
            PropertyWorkerId = propertyWorker.Id,
            CaseStatusesEnum = CaseStatusesEnum.NewTask,
            CreatedByUserId = _userService.UserId,
            UpdatedByUserId = _userService.UserId,
        };
        await workOrderCase.Create(_backendConfigurationPnDbContext);
        return workOrderCase.Id;
    }
}