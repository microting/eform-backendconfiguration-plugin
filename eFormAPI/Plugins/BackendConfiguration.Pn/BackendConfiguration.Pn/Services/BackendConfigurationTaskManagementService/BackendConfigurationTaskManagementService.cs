﻿/*
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

using System.Reflection;
using BackendConfiguration.Pn.Messages;
using BackendConfiguration.Pn.Services.RebusService;
using BackendConfiguration.Pn.Services.WordService;
using ImageMagick;
using Microting.eForm.Helpers;
using Rebus.Bus;

namespace BackendConfiguration.Pn.Services.BackendConfigurationTaskManagementService;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BackendConfigurationLocalizationService;
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
using KeyValuePair = Microting.eForm.Dto.KeyValuePair;

public class BackendConfigurationTaskManagementService : IBackendConfigurationTaskManagementService
{
    private readonly IEFormCoreService _coreHelper;
    private readonly IBackendConfigurationLocalizationService _localizationService;
    private readonly IUserService _userService;

    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
    private readonly IBus _bus;

    public BackendConfigurationTaskManagementService(
        IBackendConfigurationLocalizationService localizationService,
        IEFormCoreService coreHelper, IUserService userService,
        // ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext, IRebusService rebusService)
    {
        _localizationService = localizationService;
        _coreHelper = coreHelper;
        _userService = userService;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        _bus = rebusService.GetBus();
    }

    public async Task<List<WorkorderCaseModel>> Index(TaskManagementFiltersModel filtersModel)
    {
        try
        {
            var query = _backendConfigurationPnDbContext.WorkorderCases
                .Include(x => x.PropertyWorker)
                .ThenInclude(x => x.Property)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyWorker.Property.WorkorderEnable)
                .Where(x => x.LeadingCase == true)
                .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.NewTask);

            if (filtersModel.PropertyId != -1)
            {
                query = query
                    .Where(x => x.PropertyWorker.PropertyId == filtersModel.PropertyId);
            }

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

            if (filtersModel.Priority != null)
            {
                query = filtersModel.Priority == 3 ?
                    query.Where(x => x.Priority == null || x.Priority == "3") :
                    query.Where(x => x.Priority == filtersModel.Priority.ToString());
            }

            if (filtersModel.DateFrom.HasValue && filtersModel.DateTo.HasValue)
            {
                query = query
                    .Where(x => x.CaseInitiated >= filtersModel.DateFrom.Value)
                    .Where(x => x.CaseInitiated <= new DateTime(filtersModel.DateTo.Value.Year,
                        filtersModel.DateTo.Value.Month, filtersModel.DateTo.Value.Day, 23, 59, 59));
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
                    CaseInitiated = x.CaseInitiated,
                    Id = x.Id,
                    Status = x.CaseStatusesEnum.ToString(),
                    Description = x.Description.Replace("\n", "<br />"),
                    PropertyName = x.PropertyWorker.Property.Name,
                    LastUpdateDate = x.UpdatedAt,
                    //x.CaseId,
                    LastUpdatedBy = x.LastUpdatedByName,
                    LastAssignedTo = x.LastAssignedToName,
                    ParentWorkorderCaseId = x.ParentWorkorderCaseId,
                    Priority = string.IsNullOrEmpty(x.Priority) ? 3 : int.Parse(x.Priority) == 0 ? 3 : int.Parse(x.Priority),
                })
                .ToListAsync().ConfigureAwait(false);

            if (!string.IsNullOrEmpty(filtersModel.LastAssignedTo))
            {
                workorderCasesFromDb = workorderCasesFromDb.Where(x => x.LastAssignedTo == filtersModel.LastAssignedTo)
                    .ToList();
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
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
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
                    x.ParentWorkorderCaseId,
                    x.PropertyWorker.PropertyId,
                    x.Priority,
                    x.CaseStatusesEnum
                }).FirstOrDefaultAsync().ConfigureAwait(false);
            if (task == null)
            {
                return new OperationDataResult<WorkOrderCaseReadModel>(false,
                    _localizationService.GetString("TaskNotFound"));
            }

            var uploadIds = await _backendConfigurationPnDbContext.WorkorderCaseImages
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.WorkorderCaseId == task.ParentWorkorderCaseId)
                .Select(x => x.UploadedDataId)
                .ToListAsync().ConfigureAwait(false);

            var fileNames = new List<string>();
            foreach (var uploadId in uploadIds)
            {
                var fileName = await sdkDbContext.UploadedDatas
                    .Where(x => x.Id == uploadId)
                    .Select(x => x.FileName)
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                fileNames.Add(fileName);
            }

            var assignedSiteId = await sdkDbContext.Cases
                .Where(x => x.MicrotingUid == task.CaseId || x.Id == task.CaseId)
                .Include(x => x.Site)
                .Select(x => x.Site.Id)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            var taskForReturn = new WorkOrderCaseReadModel
            {
                AreaName = task.SelectedAreaName,
                AssignedSiteId = assignedSiteId,
                Description = task.Description.Replace("<div>", "").Replace("</div>", ""),
                Id = task.Id,
                PictureNames = fileNames,
                PropertyId = task.PropertyId,
                Priority = string.IsNullOrEmpty(task.Priority) ? 3 : int.Parse(task.Priority),
                Status = task.CaseStatusesEnum.ToString(),
                CaseStatusEnum = task.CaseStatusesEnum

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
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var entityListId = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == propertyId)
                .Select(x => x.EntitySelectListAreas)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (entityListId == null)
            {
                return new OperationDataResult<List<string>>(false,
                    _localizationService.GetString("PropertyNotFoundOrEntityListNotCreated"));
            }

            var items = await sdkDbContext.EntityItems
                .Where(x => x.EntityGroupId == entityListId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .OrderBy(x => x.DisplayIndex)
                .Select(x => x.Name)
                .ToListAsync().ConfigureAwait(false);
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
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        try
        {
            var workOrderCase = await _backendConfigurationPnDbContext.WorkorderCases
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == workOrderCaseId)
                .Include(x => x.PropertyWorker)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (workOrderCase == null)
            {
                return new OperationDataResult<WorkOrderCaseReadModel>(false,
                    _localizationService.GetString("TaskNotFound"));
            }

            if (workOrderCase.CaseId != 0)
            {
                await core.CaseDelete(workOrderCase.CaseId).ConfigureAwait(false);
            }

            await workOrderCase.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);

            var allChildTasks = await _backendConfigurationPnDbContext.WorkorderCases
                .Where(x => x.ParentWorkorderCaseId == workOrderCase.Id)
                .ToListAsync().ConfigureAwait(false);

            foreach (var childTask in allChildTasks)
            {
                childTask.UpdatedByUserId = _userService.UserId;

                if (childTask.CaseId != 0)
                {
                    await core.CaseDelete(childTask.CaseId).ConfigureAwait(false);
                }

                await childTask.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
            }

            if (workOrderCase.ParentWorkorderCaseId != null)
            {
                var parentTask = await _backendConfigurationPnDbContext.WorkorderCases
                    .Where(x => x.Id == workOrderCase.ParentWorkorderCaseId)
                    .FirstOrDefaultAsync().ConfigureAwait(false);

                if (parentTask != null)
                {
                    parentTask.UpdatedByUserId = _userService.UserId;
                    await parentTask.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);

                    await core.CaseDelete(parentTask.CaseId).ConfigureAwait(false);

                    allChildTasks = await _backendConfigurationPnDbContext.WorkorderCases
                        .Where(x => x.ParentWorkorderCaseId == parentTask.Id)
                        .ToListAsync().ConfigureAwait(false);

                    foreach (var childTask in allChildTasks)
                    {
                        childTask.UpdatedByUserId = _userService.UserId;

                        if (childTask.CaseId != 0)
                        {
                            await core.CaseDelete(childTask.CaseId).ConfigureAwait(false);
                        }

                        await childTask.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }
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

    public async Task<OperationResult> CreateTask(WorkOrderCaseCreateModel createModel)
    {
        try
        {
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var property = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == createModel.PropertyId)
                .Include(x => x.PropertyWorkers)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (property == null)
            {
                return new OperationDataResult<WorkOrderCaseReadModel>(false,
                    _localizationService.GetString("PropertyNotFound"));
            }

            var propertyWorkers = property.PropertyWorkers
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToList();

            var site = await sdkDbContext.Sites
                .Where(x => x.Id == createModel.AssignedSiteId)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            createModel.Description = string.IsNullOrEmpty(createModel.Description)
                ? ""
                : createModel.Description.Replace("<div>", "").Replace("</div>", "");
            var newWorkOrderCase = new WorkorderCase
            {
                ParentWorkorderCaseId = null,
                CaseId = 0,
                PropertyWorkerId = propertyWorkers.First().Id,
                SelectedAreaName = createModel.AreaName,
                CreatedByName = await _userService.GetCurrentUserFullName().ConfigureAwait(false),
                CreatedByText = "",
                CaseStatusesEnum = createModel.CaseStatusEnum,
                Description = createModel.Description,
                CaseInitiated = DateTime.UtcNow,
                LeadingCase = true,
                LastAssignedToName = site.Name,
                Priority = createModel.Priority.ToString(),
            };
            await newWorkOrderCase.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

            var picturesOfTasks = new List<string>();
            if (createModel.Files.Any())
            {
                foreach (var picture in createModel.Files)
                {
                    MemoryStream baseMemoryStream = new MemoryStream();
                    await picture.CopyToAsync(baseMemoryStream).ConfigureAwait(false);
                    var hash = "";
                    using (var md5 = MD5.Create())
                    {
                        var grr = await md5.ComputeHashAsync(baseMemoryStream).ConfigureAwait(false);
                        hash = BitConverter.ToString(grr).Replace("-", "").ToLower();
                    }

                    var uploadData = new Microting.eForm.Infrastructure.Data.Entities.UploadedData
                    {
                        Checksum = hash,
                        FileName = picture.FileName,
                        FileLocation = "",
                        Extension = picture.ContentType.Split("/")[1]
                    };
                    await uploadData.Create(sdkDbContext).ConfigureAwait(false);

                    var fileName = $"{uploadData.Id}_{hash}.{picture.ContentType.Split("/")[1]}";
                    string smallFilename = $"{uploadData.Id}_300_{hash}.{picture.ContentType.Split("/")[1]}";
                    string bigFilename = $"{uploadData.Id}_700_{hash}.{picture.ContentType.Split("/")[1]}";
                    baseMemoryStream.Seek(0, SeekOrigin.Begin);
                    MemoryStream s3Stream = new MemoryStream();
                    await baseMemoryStream.CopyToAsync(s3Stream).ConfigureAwait(false);
                    s3Stream.Seek(0, SeekOrigin.Begin);
                    await core.PutFileToS3Storage(s3Stream, fileName).ConfigureAwait(false);
                    baseMemoryStream.Seek(0, SeekOrigin.Begin);
                    using (var image = new MagickImage(baseMemoryStream))
                    {
                        decimal currentRation = image.Height / (decimal)image.Width;
                        int newWidth = 300;
                        int newHeight = (int)Math.Round((currentRation * newWidth));

                        image.Resize(newWidth, newHeight);
                        image.Crop(newWidth, newHeight);
                        MemoryStream memoryStream = new MemoryStream();
                        await image.WriteAsync(memoryStream).ConfigureAwait(false);
                        await core.PutFileToS3Storage(memoryStream, smallFilename).ConfigureAwait(false);
                        await memoryStream.DisposeAsync().ConfigureAwait(false);
                        memoryStream.Close();
                        image.Dispose();
                        baseMemoryStream.Seek(0, SeekOrigin.Begin);
                    }

                    using (var image = new MagickImage(baseMemoryStream))
                    {
                        decimal currentRation = image.Height / (decimal)image.Width;
                        int newWidth = 700;
                        int newHeight = (int)Math.Round((currentRation * newWidth));

                        image.Resize(newWidth, newHeight);
                        image.Crop(newWidth, newHeight);
                        MemoryStream memoryStream = new MemoryStream();
                        await image.WriteAsync(memoryStream).ConfigureAwait(false);
                        await core.PutFileToS3Storage(memoryStream, bigFilename).ConfigureAwait(false);
                        await memoryStream.DisposeAsync().ConfigureAwait(false);
                        memoryStream.Close();
                        image.Dispose();
                    }

                    await baseMemoryStream.DisposeAsync().ConfigureAwait(false);
                    baseMemoryStream.Close();

                    var workOrderCaseImage = new WorkorderCaseImage
                    {
                        WorkorderCaseId = newWorkOrderCase.Id,
                        UploadedDataId = uploadData.Id
                    };
                    picturesOfTasks.Add($"{uploadData.Id}_700_{uploadData.Checksum}.{uploadData.Extension}");
                    await workOrderCaseImage.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }
            }

            var eformIdForOngoingTasks = await sdkDbContext.CheckListTranslations
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Text == "02. Ongoing task")
                .Select(x => x.CheckListId)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            var deviceUsersGroupMicrotingUid = await sdkDbContext.EntityGroups
                .Where(x => x.Id == property.EntitySelectListDeviceUsers)
                .Select(x => int.Parse(x.MicrotingUid))
                .FirstAsync().ConfigureAwait(false);

            var description = $"<strong>{_localizationService.GetString("AssignedTo")}:</strong> {site.Name}<br>" +
                              $"<strong>{_localizationService.GetString("Location")}:</strong> {property.Name}<br>" +
                              $"<strong>{_localizationService.GetString("Area")}:</strong> {createModel.AreaName}<br>" +
                              $"<strong>{_localizationService.GetString("Description")}:</strong> {createModel.Description}<br><br>" +
                              $"<strong>{_localizationService.GetString("CreatedBy")}:</strong> {await _userService.GetCurrentUserFullName().ConfigureAwait(false)}<br>" +
                              $"<strong>{_localizationService.GetString("CreatedDate")}:</strong> {DateTime.UtcNow: dd.MM.yyyy}<br><br>" +
                              $"<strong>{_localizationService.GetString("Status")}:</strong> {_localizationService.GetString("Ongoing")}<br><br>";

            var pushMessageTitle = !string.IsNullOrEmpty(createModel.AreaName)
                ? $"{property.Name}; {createModel.AreaName}"
                : $"{property.Name}";
            var pushMessageBody = createModel.Description;

            var propertyWorkerKvpList = new List<KeyValuePair<int, int>>();

            foreach (var propertyWorker in propertyWorkers)
            {
                var kvp = new KeyValuePair<int, int>(propertyWorker.Id, propertyWorker.WorkerId);
                propertyWorkerKvpList.Add(kvp);
            }

            await _bus.SendLocal(new WorkOrderCreated(
                propertyWorkerKvpList,
                eformIdForOngoingTasks,
                (int)property.FolderIdForOngoingTasks!,
                description,
                createModel.CaseStatusEnum,
                newWorkOrderCase.Id,
                createModel.Description,
                deviceUsersGroupMicrotingUid,
                pushMessageBody,
                pushMessageTitle,
                createModel.AreaName,
                _userService.UserId,
                picturesOfTasks,
                site.Name)).ConfigureAwait(false);

            return new OperationResult(true, _localizationService.GetString("TaskCreatedSuccessful"));
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationResult(false,
                $"{_localizationService.GetString("ErrorWhileCreateTask")}: {e.Message}");
        }
    }

    public async Task<OperationResult> UpdateTask(WorkOrderCaseUpdateModel updateModel)
    {
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var workOrderCase = await _backendConfigurationPnDbContext.WorkorderCases
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Include(x => x.PropertyWorker)
            .Include(x => x.PropertyWorker.Property)
            .Where(x => x.Id == updateModel.Id).FirstOrDefaultAsync().ConfigureAwait(false);
        if (workOrderCase == null)
        {
            return new OperationDataResult<WorkOrderCaseReadModel>(false,
                _localizationService.GetString("TaskNotFound"));
        }
        workOrderCase.Priority = updateModel.Priority.ToString();
        workOrderCase.Description = updateModel.Description;
        workOrderCase.CaseStatusesEnum = updateModel.CaseStatusEnum;

        var site = await sdkDbContext.Sites.FirstAsync(x => x.Id == updateModel.AssignedSiteId).ConfigureAwait(false);

        var updatedByName = await _userService.GetCurrentUserFullName().ConfigureAwait(false);

        var picturesOfTasks = new List<string>();

        var parentCaseImages = await _backendConfigurationPnDbContext.WorkorderCaseImages.Where(x => x.WorkorderCaseId == workOrderCase.ParentWorkorderCaseId).ToListAsync();

        foreach (var workorderCaseImage in parentCaseImages)
        {
            var uploadedData = await sdkDbContext.UploadedDatas.FirstAsync(x => x.Id == workorderCaseImage.UploadedDataId);
            picturesOfTasks.Add($"{uploadedData.Id}_700_{uploadedData.Checksum}{uploadedData.Extension}");
            var workOrderCaseImage = new WorkorderCaseImage
            {
                WorkorderCaseId = workOrderCase.Id,
                UploadedDataId = (int)uploadedData.Id
            };
            await workOrderCaseImage.Create(_backendConfigurationPnDbContext);
        }

        var property = workOrderCase.PropertyWorker.Property;
        var hash = await GeneratePdf(picturesOfTasks, site.Id);

        var label = $"<strong>{_localizationService.GetString("AssignedTo")}:</strong> {site.Name}<br>";

        var pushMessageTitle = !string.IsNullOrEmpty(workOrderCase.SelectedAreaName) ? $"{property.Name}; {workOrderCase.SelectedAreaName}" : $"{property.Name}";
        var pushMessageBody = $"{updateModel.Description}";
        var deviceUsersGroupUid = await sdkDbContext.EntityGroups
            .Where(x => x.Id == property.EntitySelectListDeviceUsers)
            .Select(x => x.MicrotingUid)
            .FirstAsync();
            var priorityText = "";

        var propertyWorkers = await _backendConfigurationPnDbContext.PropertyWorkers
            .Where(x => x.PropertyId == property.Id)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        var eformIdForOngoingTasks = await sdkDbContext.CheckLists
            .Where(x => x.OriginalId == "142664new2")
            .Select(x => x.Id)
            .FirstOrDefaultAsync();

        var textStatus = "";
        switch (workOrderCase.CaseStatusesEnum)
        {
            case CaseStatusesEnum.Ongoing:
                textStatus = _localizationService.GetString("Ongoing");
                break;
            case CaseStatusesEnum.Completed:
                textStatus = _localizationService.GetString("Completed");
                break;
            case CaseStatusesEnum.Ordered:
                textStatus = _localizationService.GetString("Ordered");
                break;
            case CaseStatusesEnum.Awaiting:
                textStatus = _localizationService.GetString("Awaiting");
                break;
        }

        switch (updateModel.Priority)
        {
            case 1:
                priorityText = $"<strong>{_localizationService.GetString("Priority")}:</strong> {_localizationService.GetString("Urgent")}<br>";
                break;
            case 2:
                priorityText = $"<strong>{_localizationService.GetString("Priority")}:</strong> {_localizationService.GetString("High")}<br>";
                break;
            case 3:
                priorityText = $"<strong>{_localizationService.GetString("Priority")}:</strong> {_localizationService.GetString("Medium")}<br>";
                break;
            case 4:
                priorityText = $"<strong>{_localizationService.GetString("Priority")}:</strong> {_localizationService.GetString("Low")}<br>";
                break;
        }
        label += $"<strong>{_localizationService.GetString("Location")}:</strong> {property.Name}<br>" +
                 (!string.IsNullOrEmpty(workOrderCase.SelectedAreaName)
                     ? $"<strong>{_localizationService.GetString("Area")}:</strong> {workOrderCase.SelectedAreaName}<br>"
                     : "") +
                 $"<strong>{_localizationService.GetString("Description")}:</strong> {updateModel.Description}<br>" +
                 priorityText +
                 $"<strong>{_localizationService.GetString("CreatedBy")}:</strong> {workOrderCase.CreatedByName}<br>" +
                 (!string.IsNullOrEmpty(workOrderCase.CreatedByText)
                     ? $"<strong>{_localizationService.GetString("CreatedBy")}:</strong> {workOrderCase.CreatedByText}<br>"
                     : "") +
                 $"<strong>{_localizationService.GetString("CreatedDate")}:</strong> {workOrderCase.CaseInitiated: dd.MM.yyyy}<br><br>" +
                 $"<strong>{_localizationService.GetString("LastUpdatedBy")}:</strong> {await _userService.GetCurrentUserFullName().ConfigureAwait(false)}<br>" +
                 $"<strong>{_localizationService.GetString("LastUpdatedDate")}:</strong> {DateTime.UtcNow: dd.MM.yyyy}<br><br>" +
                 $"<strong>{_localizationService.GetString("Status")}:</strong> {textStatus}<br><br>";
        // retract eform
        await RetractEform(workOrderCase);
        // deploy eform to ongoing status

        await DeployWorkOrderEform(propertyWorkers, eformIdForOngoingTasks, property, label,  workOrderCase.CaseStatusesEnum, workOrderCase, updateModel.Description, int.Parse(deviceUsersGroupUid), hash, site.Name, pushMessageBody, pushMessageTitle, updatedByName);

        return new OperationResult(true, _localizationService.GetString("TaskUpdatedSuccessful"));
    }

    private async Task RetractEform(WorkorderCase workOrderCase)
    {
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        if (workOrderCase.ParentWorkorderCaseId != null)
        {
            var workOrdersToRetract = await _backendConfigurationPnDbContext.WorkorderCases
                .Where(x => x.ParentWorkorderCaseId == workOrderCase.ParentWorkorderCaseId).ToListAsync();

            foreach (var theCase in workOrdersToRetract)
            {
                try {
                    await core.CaseDelete(theCase.CaseId);
                } catch (Exception e) {
                    Console.WriteLine(e);
                    Console.WriteLine($"faild to delete case {theCase.CaseId}");
                }
                await theCase.Delete(_backendConfigurationPnDbContext);
            }

            var parentCase = await _backendConfigurationPnDbContext.WorkorderCases
                .FirstAsync(x => x.Id == workOrderCase.ParentWorkorderCaseId);

            if (parentCase.CaseId != 0 && parentCase.ParentWorkorderCaseId != null)
            {
                try
                {
                    await core.CaseDelete(parentCase.CaseId);
                } catch (Exception e) {
                    Console.WriteLine(e);
                    Console.WriteLine($"faild to delete case {parentCase.CaseId}");
                }
            }
            await parentCase.Delete(_backendConfigurationPnDbContext);

            if (workOrderCase.CaseId != 0)
            {
                try
                {
                    await core.CaseDelete(workOrderCase.CaseId);
                } catch (Exception e) {
                    Console.WriteLine(e);
                    Console.WriteLine($"faild to delete case {workOrderCase.CaseId}");
                }
            }

            await workOrderCase.Delete(_backendConfigurationPnDbContext);
        }
    }

    private async Task DeployWorkOrderEform(
        List<PropertyWorker> propertyWorkers,
        int eformId,
        Property property,
        string description,
        CaseStatusesEnum status,
        WorkorderCase workorderCase,
        string newDescription,
        int? deviceUsersGroupId,
        string pdfHash,
        string siteName,
        string pushMessageBody,
        string pushMessageTitle,
        string updatedByName)
    {

        var _sdkCore = await _coreHelper.GetCore().ConfigureAwait(false);
        int? folderId = null;
        await using var sdkDbContext = _sdkCore.DbContextHelper.GetDbContext();
        var i = 0;
        foreach (var propertyWorker in propertyWorkers)
        {
            var priorityText = "";

            var site = await sdkDbContext.Sites.FirstAsync(x => x.Id == propertyWorker.WorkerId);
            switch (workorderCase.Priority)
            {
                case "1":
                    priorityText = $"<strong>{_localizationService.GetString("Priority")}:</strong> {_localizationService.GetString("Urgent")}<br>";
                    break;
                case "2":
                    priorityText = $"<strong>{_localizationService.GetString("Priority")}:</strong> {_localizationService.GetString("High")}<br>";
                    break;
                case "3":
                    priorityText = $"<strong>{_localizationService.GetString("Priority")}:</strong> {_localizationService.GetString("Medium")}<br>";
                    break;
                case "4":
                    priorityText = $"<strong>{_localizationService.GetString("Priority")}:</strong> {_localizationService.GetString("Low")}<br>";
                    break;
            }

            var textStatus = "";

            switch (workorderCase.CaseStatusesEnum)
            {
                case CaseStatusesEnum.Ongoing:
                    textStatus = _localizationService.GetString("Ongoing");
                    break;
                case CaseStatusesEnum.Completed:
                    textStatus = _localizationService.GetString("Completed");
                    break;
                case CaseStatusesEnum.Awaiting:
                    textStatus = _localizationService.GetString("Awaiting");
                    break;
                case CaseStatusesEnum.Ordered:
                    textStatus = _localizationService.GetString("Ordered");
                    break;
            }

            var assignedTo = site.Name == siteName ? "" : $"<strong>{_localizationService.GetString("AssignedTo")}:</strong> {siteName}<br>";

            var areaName = !string.IsNullOrEmpty(workorderCase.SelectedAreaName)
                ? $"<strong>{_localizationService.GetString("Area")}:</strong> {workorderCase.SelectedAreaName}<br>"
                : "";

            var outerDescription = $"<strong>{_localizationService.GetString("Location")}:</strong> {property.Name}<br>" +
                                   areaName +
                                   $"<strong>{_localizationService.GetString("Description")}:</strong> {newDescription}<br>" +
                                   priorityText +
                                   assignedTo +
                                   $"<strong>{_localizationService.GetString("Status")}:</strong> {textStatus}<br><br>";
            var siteLanguage = await sdkDbContext.Languages.FirstAsync(x => x.Id == site.LanguageId);
            var mainElement = await _sdkCore.ReadeForm(eformId, siteLanguage);
            mainElement.Label = " ";
            mainElement.ElementList[0].QuickSyncEnabled = true;
            mainElement.EnableQuickSync = true;
            mainElement.ElementList[0].Label = " ";
            mainElement.ElementList[0].Description.InderValue = outerDescription.Replace("\n", "<br>");
            if (status == CaseStatusesEnum.Completed || site.Name == siteName)
            {
                DateTime startDate = new DateTime(2020, 1, 1);
                mainElement.DisplayOrder = (int)(startDate - DateTime.UtcNow).TotalSeconds;
            }
            if (site.Name == siteName)
            {
                mainElement.CheckListFolderName = sdkDbContext.Folders.First(x => x.Id == (workorderCase.Priority != "1" ? property.FolderIdForOngoingTasks : property.FolderIdForTasks))
                    .MicrotingUid.ToString();
                folderId = property.FolderIdForOngoingTasks;
                mainElement.PushMessageTitle = pushMessageTitle;
                mainElement.PushMessageBody = pushMessageBody;
            }
            else
            {
                folderId = property.FolderIdForCompletedTasks;
                mainElement.CheckListFolderName = sdkDbContext.Folders.First(x => x.Id == property.FolderIdForCompletedTasks)
                    .MicrotingUid.ToString();
            }
            // TODO uncomment when new app has been released.
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Description.InderValue = description.Replace("\n", "<br>");
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Label = " ";
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Color = Constants.FieldColors.Yellow;
            ((ShowPdf) ((DataElement) mainElement.ElementList[0]).DataItemList[1]).Value = pdfHash;

            List<KeyValuePair> kvpList = ((SingleSelect) ((DataElement) mainElement.ElementList[0]).DataItemList[4]).KeyValuePairList;
            var newKvpList = new List<KeyValuePair>();
            foreach (var keyValuePair in kvpList)
            {
                if (keyValuePair.Key == workorderCase.Priority)
                {
                    keyValuePair.Selected = true;
                }
                newKvpList.Add(keyValuePair);
            }
            ((SingleSelect) ((DataElement) mainElement.ElementList[0]).DataItemList[4]).KeyValuePairList = newKvpList;

            if (deviceUsersGroupId != null)
            {
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[5]).Source = (int)deviceUsersGroupId;
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[5]).Mandatory = true;
                ((Comment)((DataElement)mainElement.ElementList[0]).DataItemList[3]).Value = newDescription;
                ((SingleSelect)((DataElement)mainElement.ElementList[0]).DataItemList[6]).Mandatory = true;
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
            var caseId = await _sdkCore.CaseCreate(mainElement, "", (int)site.MicrotingUid, folderId);
            await new WorkorderCase
            {
                CaseId = (int)caseId,
                PropertyWorkerId = propertyWorker.Id,
                CaseStatusesEnum = status,
                ParentWorkorderCaseId = workorderCase.Id,
                SelectedAreaName = workorderCase.SelectedAreaName,
                CreatedByName = workorderCase.CreatedByName,
                CreatedByText = workorderCase.CreatedByText,
                Description = newDescription,
                CaseInitiated = workorderCase.CaseInitiated,
                LastAssignedToName = siteName,
                LastUpdatedByName = updatedByName,
                LeadingCase = i == 0,
                Priority = workorderCase.Priority
            }.Create(_backendConfigurationPnDbContext);
            i++;
        }
    }

    private async Task<string> GeneratePdf(List<string> picturesOfTasks, int sitId)
    {
        var _sdkCore = await _coreHelper.GetCore().ConfigureAwait(false);
        var resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.page.html";
        var assembly = Assembly.GetExecutingAssembly();
        string html;
        await using (var resourceStream = assembly.GetManifestResourceStream(resourceString))
        {
            using var reader = new StreamReader(resourceStream ?? throw new InvalidOperationException($"{nameof(resourceStream)} is null"));
            html = await reader.ReadToEndAsync();
        }

        // Read docx stream
        resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.file.docx";
        var docxFileResourceStream = assembly.GetManifestResourceStream(resourceString);
        if (docxFileResourceStream == null)
        {
            throw new InvalidOperationException($"{nameof(docxFileResourceStream)} is null");
        }

        var docxFileStream = new MemoryStream();
        await docxFileResourceStream.CopyToAsync(docxFileStream);
        await docxFileResourceStream.DisposeAsync();
        string basePicturePath = Path.Combine(Path.GetTempPath(), "pictures", "workorders");
        Directory.CreateDirectory(basePicturePath);
        var word = new WordProcessor(docxFileStream);
        string imagesHtml = "";

        foreach (var imagesName in picturesOfTasks)
        {
            Console.WriteLine($"Trying to insert image into document : {imagesName}");
            imagesHtml = await InsertImageToPdf(imagesName, imagesHtml, 700, 650, basePicturePath);
        }

        html = html.Replace("{%Content%}", imagesHtml);

        word.AddHtml(html);
        word.Dispose();
        docxFileStream.Position = 0;

        // Build docx
        string downloadPath = Path.Combine(Path.GetTempPath(), "reports", "results");
        Directory.CreateDirectory(downloadPath);
        string timeStamp = DateTime.UtcNow.ToString("yyyyMMdd") + "_" + DateTime.UtcNow.ToString("hhmmss");
        string docxFileName = $"{timeStamp}{sitId}_temp.docx";
        string tempPDFFileName = $"{timeStamp}{sitId}_temp.pdf";
        string tempPDFFilePath = Path.Combine(downloadPath, tempPDFFileName);
        await using (var docxFile = new FileStream(Path.Combine(Path.GetTempPath(), "reports", "results", docxFileName), FileMode.Create, FileAccess.Write))
        {
            docxFileStream.WriteTo(docxFile);
        }

        // Convert to PDF
        ReportHelper.ConvertToPdf(Path.Combine(Path.GetTempPath(), "reports", "results", docxFileName), downloadPath);
        File.Delete(docxFileName);

        // Upload PDF
        // string pdfFileName = null;
        string hash = await _sdkCore.PdfUpload(tempPDFFilePath);
        if (hash != null)
        {
            //rename local file
            FileInfo fileInfo = new FileInfo(tempPDFFilePath);
            fileInfo.CopyTo(downloadPath + "/" + hash + ".pdf", true);
            fileInfo.Delete();
            await _sdkCore.PutFileToStorageSystem(Path.Combine(downloadPath, $"{hash}.pdf"), $"{hash}.pdf");

            // TODO Remove from file storage?
        }

        return hash;
    }

    private async Task<string> InsertImageToPdf(string imageName, string itemsHtml, int imageSize, int imageWidth, string basePicturePath)
    {
        var _sdkCore = await _coreHelper.GetCore().ConfigureAwait(false);
        // if (imageName.Contains("GH"))
        // {
        //     var assembly = Assembly.GetExecutingAssembly();
        //     var resourceString = $"ServiceBackendConfigurationPlugin.Resources.GHSHazardPictogram.{imageName}.jpg";
        //     // using var FileStream FileStream = new FileStream()
        //     await using var resourceStream = assembly.GetManifestResourceStream(resourceString);
        //     // using var reader = new StreamReader(resourceStream ?? throw new InvalidOperationException($"{nameof(resourceStream)} is null"));
        //     // html = await reader.ReadToEndAsync();
        //     // MemoryStream memoryStream = new MemoryStream();
        //     // await resourceStream.CopyToAsync(memoryStream);
        //     using var image = new MagickImage(resourceStream);
        //     var profile = image.GetExifProfile();
        //     // Write all values to the console
        //     try
        //     {
        //         foreach (var value in profile.Values)
        //         {
        //             Console.WriteLine("{0}({1}): {2}", value.Tag, value.DataType, value.ToString());
        //         }
        //     } catch (Exception)
        //     {
        //         // Console.WriteLine(e);
        //     }
        //     // image.Rotate(90);
        //     var base64String = image.ToBase64();
        //     itemsHtml +=
        //         $@"<p><img src=""data:image/png;base64,{base64String}"" width=""{imageWidth}px"" alt="""" /></p>";
        //
        //     // await stream.DisposeAsync();
        // }
        // else
        // {
            var storageResult = await _sdkCore.GetFileFromS3Storage(imageName);
            var stream = storageResult.ResponseStream;

            using var image = new MagickImage(stream);
            var profile = image.GetExifProfile();
            // Write all values to the console
            try
            {
                foreach (var value in profile.Values)
                {
                    Console.WriteLine("{0}({1}): {2}", value.Tag, value.DataType, value.ToString());
                }
            } catch (Exception)
            {
                // Console.WriteLine(e);
            }
            image.Rotate(90);
            var base64String = image.ToBase64();
            itemsHtml +=
                $@"<p><img src=""data:image/png;base64,{base64String}"" width=""{imageWidth}px"" alt="""" /></p>";

            await stream.DisposeAsync();
        // }

        return itemsHtml;
    }

}