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
using System.Security.Cryptography;
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
            /*var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();*/
            var query = _backendConfigurationPnDbContext.WorkorderCases
                .Include(x => x.PropertyWorker)
                .ThenInclude(x => x.Property)
                .Where(x => x.PropertyWorker.PropertyId == filtersModel.PropertyId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
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

            if (!string.IsNullOrEmpty(filtersModel.AreaName))
            {
                query = query.Where(x => filtersModel.AreaName == x.SelectedAreaName);
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

            var excludeSort = new List<string>
            {
                "PropertyName",
            };
            query = QueryHelper.AddFilterAndSortToQuery(query, filtersModel, new List<string>(), excludeSort);

            var workorderCasesFromDb = await query
                .Select(x => new WorkorderCaseModel
                {
                    AreaName = x.SelectedAreaName,
                    CreatedByName = x.CreatedByName,
                    CreatedByText = x.CreatedByText,
                    CreatedDate = x.CreatedAt,
                    Id = x.Id,
                    Status = x.CaseStatusesEnum.ToString(),
                    Description = x.Description,
                    PropertyName = x.PropertyWorker.Property.Name,
                    LastUpdateDate = x.UpdatedAt,
                    //x.CaseId,
                    LastUpdatedBy = x.LastUpdatedByName,
                    LastAssignedTo = x.LastAssignedToName,
                })
                .ToListAsync();
            /*var workorderCases = new List<WorkorderCaseModel>();
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
                    LastUpdatedBy = workorderCaseModel.LastUpdatedBy,
                });
            }
            if (!string.IsNullOrEmpty(filtersModel.LastAssignedTo))
            {
                workorderCases = workorderCases.Where(x => x.LastAssignedTo == filtersModel.LastAssignedTo).ToList();
            }

            if(excludeSort.Contains(filtersModel.Sort))
            {
                workorderCases = QueryHelper
                    .AddFilterAndSortToQuery(workorderCases.AsQueryable(), filtersModel, new List<string>())
                    .ToList();
            }

            return workorderCases;*/
            if (!string.IsNullOrEmpty(filtersModel.LastAssignedTo))
            {
                workorderCasesFromDb = workorderCasesFromDb.Where(x => x.LastAssignedTo == filtersModel.LastAssignedTo).ToList();
            }

            if (excludeSort.Contains(filtersModel.Sort))
            {
                workorderCasesFromDb = QueryHelper
                    .AddFilterAndSortToQuery(workorderCasesFromDb.AsQueryable(), filtersModel, new List<string>())
                    .ToList();
            }

            return workorderCasesFromDb;
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

            var pictureListUploadedIds = new List<int>();
            if (createModel.Files.Any())
            {
                var folder = Path.Combine(Path.GetTempPath(), "pictures-for-case");
                Directory.CreateDirectory(folder);
                
                foreach (var picture in createModel.Files)
                {
                    var filePath = Path.Combine(folder, $"{DateTime.Now.Ticks}.{picture.ContentType}");

                    var hash = "";
                    using (var md5 = MD5.Create())
                    {
                        await using var stream = new FileStream(filePath, FileMode.Create);
                        await picture.CopyToAsync(stream);
                        var grr = await md5.ComputeHashAsync(stream);
                        hash = BitConverter.ToString(grr).Replace("-", "").ToLower();
                    }

                    await core.PutFileToStorageSystem(filePath, picture.FileName);

                    var uploadData = new Microting.eForm.Infrastructure.Data.Entities.UploadedData
                    {
                        Checksum = hash,
                        FileName = picture.FileName,
                        FileLocation = filePath,
                    };
                    await uploadData.Create(sdkDbContext);
                    pictureListUploadedIds.Add(uploadData.Id);
                }
            }

            var eformIdForOngoingTasks = await sdkDbContext.CheckListTranslations
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Text == "02. Ongoing task")
                .Select(x => x.CheckListId)
                .FirstOrDefaultAsync();

            var deviceUsersGroupMicrotingUid = await sdkDbContext.EntityGroups
                .Where(x => x.Id == property.EntitySelectListDeviceUsers)
                .Select(x => int.Parse(x.MicrotingUid))
                .FirstAsync();

            var site = await sdkDbContext.Sites
                .Where(x => x.Id == createModel.AssignedSiteId)
                .FirstOrDefaultAsync();

            var propertyWorkers = property.PropertyWorkers
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToList();
            
            var label = $"<strong>{_localizationService.GetString("AssignedTo")}:</strong> {site.Name}<br>" +
                        $"<strong>{_localizationService.GetString("Location")}:</strong> {property.Name}<br>" +
                        $"<strong>{_localizationService.GetString("Area")}:</strong> {createModel.AreaName}<br>" +
                        $"<strong>{_localizationService.GetString("Description")}:</strong> {createModel.Description}<br><br>" +
                        $"<strong>{_localizationService.GetString("CreatedBy")}:</strong> {await _userService.GetCurrentUserFullName()}<br>" +
                        $"<strong>{_localizationService.GetString("CreatedDate")}:</strong> {DateTime.UtcNow: dd.MM.yyyy}<br><br>" +
                        $"<strong>{_localizationService.GetString("Status")}:</strong> {_localizationService.GetString("Ongoing")}<br><br>" +
                        $"<center><strong>******************</strong></center>";

            var pushMessageTitle = !string.IsNullOrEmpty(createModel.AreaName) ? $"{property.Name}; {createModel.AreaName}" : $"{property.Name}";
            var pushMessageBody = createModel.Description;

            // deploy eform to ongoing status
            await DeployEform(
                propertyWorkers,
                eformIdForOngoingTasks,
                (int)property.FolderIdForOngoingTasks,
                label,
                CaseStatusesEnum.Ongoing,
                createModel.Description,
                deviceUsersGroupMicrotingUid,
                pushMessageBody,
                pushMessageTitle,
                pictureListUploadedIds,
                createModel.AreaName);

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

    private async Task DeployEform(
        List<PropertyWorker> propertyWorkers,
        int eformId,
        int folderId,
        string description,
        CaseStatusesEnum status,
        string newDescription,
        int? deviceUsersGroupId,
        string pushMessageBody,
        string pushMessageTitle,
        List<int> pictureListUploadedIds,
        string areaName)
    {
        var core = await _coreHelper.GetCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();
        foreach (var propertyWorker in propertyWorkers)
        {
            var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.WorkerId);
            var siteLanguage = await sdkDbContext.Languages.SingleAsync(x => x.Id == site.LanguageId);
            var mainElement = await core.ReadeForm(eformId, siteLanguage);
            mainElement.CheckListFolderName = await sdkDbContext.Folders
                .Where(x => x.Id == folderId)
                .Select(x => x.MicrotingUid.ToString())
                .FirstOrDefaultAsync();
            mainElement.Label = " ";
            mainElement.ElementList[0].QuickSyncEnabled = true;
            mainElement.EnableQuickSync = true;
            mainElement.ElementList[0].Label = " ";
            mainElement.ElementList[0].Description.InderValue = description;
            mainElement.PushMessageTitle = pushMessageTitle;
            mainElement.PushMessageBody = pushMessageBody;
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Description.InderValue = description;
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Label = " ";
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Color = Constants.FieldColors.Yellow;
            if (deviceUsersGroupId != null)
            {
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[4]).Source = (int)deviceUsersGroupId;
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[4]).Mandatory = true;
                ((Comment)((DataElement)mainElement.ElementList[0]).DataItemList[3]).Value = newDescription;
                ((SingleSelect)((DataElement)mainElement.ElementList[0]).DataItemList[5]).Mandatory = true;
                mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                mainElement.Repeated = 1;
            }
            else
            {
                mainElement.EndDate = DateTime.Now.AddDays(30).ToUniversalTime();
                mainElement.ElementList[0].DoneButtonEnabled = false;
                mainElement.Repeated = 1;
            }

            mainElement.StartDate = DateTime.Now.ToUniversalTime();
            var caseId = await core.CaseCreate(mainElement, "", (int)site.MicrotingUid, folderId);
            var workOrderCase = new WorkorderCase
            {
                CaseId = (int)caseId,
                PropertyWorkerId = propertyWorker.Id,
                CaseStatusesEnum = status,
                SelectedAreaName = areaName,
                CreatedByName = site.Name,
                Description = newDescription,
                CaseInitiated = DateTime.UtcNow,
                CreatedByUserId = _userService.UserId,
                LastAssignedToName = site.Name,
            };
            await workOrderCase.Create(_backendConfigurationPnDbContext);

            foreach (var pictureUploadedId in pictureListUploadedIds)
            {
                var workOrderCaseImage = new WorkorderCaseImage
                {
                    WorkorderCaseId = workOrderCase.Id,
                    UploadedDataId = pictureUploadedId
                };
                await workOrderCaseImage.Create(_backendConfigurationPnDbContext);
            }
        }
    }
}