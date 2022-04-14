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
using System.Linq;
using System.Threading.Tasks;
using BackendConfigurationLocalizationService;
using Infrastructure.Data.Seed.Data;
using Infrastructure.Models.PropertyAreas;
using Infrastructure.Models.TaskManagement;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data;

public class BackendConfigurationTaskManagementService: IBackendConfigurationTaskManagementService
{
    private readonly IEFormCoreService _coreHelper;
    private readonly IBackendConfigurationLocalizationService _localizationService;
    private readonly IUserService _userService;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
    private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;

    public BackendConfigurationTaskManagementService(
        IBackendConfigurationLocalizationService localizationService,
        IEFormCoreService coreHelper, IUserService userService,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext)
    {
        _localizationService = localizationService;
        _coreHelper = coreHelper;
        _userService = userService;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
    }

    public async Task<List<WorkorderCaseModel>> GetReport(TaskManagementFiltersModel filtersModel)
    {
        try
        {
            var areaNames = await _backendConfigurationPnDbContext.AreaTranslations
                .Where(x => x.AreaId == filtersModel.AreaId)
                .Select(x => x.Name)
                .ToListAsync();

            var query = _backendConfigurationPnDbContext.WorkorderCases
                .Include(x => x.PropertyWorker)
                .ThenInclude(x => x.Property)
                .Where(x => x.PropertyWorker.PropertyId == filtersModel.PropertyId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => areaNames.Contains(x.SelectedAreaName));

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

            var workorderCaseModel = await query
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
                    LastUpdatedBy = GetUserNameById(x.UpdatedByUserId, _userService),
                })
                .ToListAsync();

            return workorderCaseModel;
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
            var task = await _backendConfigurationPnDbContext.WorkorderCases
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == workOrderCaseId)
                .Include(x => x.PropertyWorker)
                .Select(x => new 
                {
                    AreaId = _backendConfigurationPnDbContext.AreaTranslations
                        .Where(y => y.Name == x.SelectedAreaName)
                        .Select(y => y.AreaId)
                        .First(),
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

            var core =  await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
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
                .Where(x => x.CaseUid == task.CaseId.ToString())
                .Include(x => x.Site)
                .Select(x => x.Site.MicrotingUid)
                .FirstOrDefaultAsync();

            var taskForReturn = new WorkOrderCaseReadModel
            {
                AreaId = task.AreaId,
                AssignedSiteId = (int)assignedSiteId,
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

    private static string GetUserNameById(int id, IUserService userService)
    {
        var task = userService.GetByIdAsync(id);
        task.Wait();
        var user = task.Result;
        return $"{user.FirstName} {user.LastName}";
    }
}