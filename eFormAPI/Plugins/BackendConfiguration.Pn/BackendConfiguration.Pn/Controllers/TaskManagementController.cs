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

namespace BackendConfiguration.Pn.Controllers;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Infrastructure.Models.TaskManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Services.BackendConfigurationLocalizationService;
using Services.BackendConfigurationTaskManagementService;

[Authorize]
[Route("api/backend-configuration-pn/task-management")]
public class TaskManagementController : Controller
{
    private readonly IBackendConfigurationTaskManagementService _backendConfigurationTaskManagementService;
    private readonly IBackendConfigurationLocalizationService _localizationService;

    public TaskManagementController(
        IBackendConfigurationTaskManagementService backendConfigurationTaskManagementService,
        IBackendConfigurationLocalizationService localizationService
        )
    {
        _backendConfigurationTaskManagementService = backendConfigurationTaskManagementService;
        _localizationService = localizationService;
    }

    [HttpGet]
    public async Task<OperationDataResult<List<WorkorderCaseModel>>> GetReport(TaskManagementFiltersModel filtersModel)
    {
        try
        {
            var report = await _backendConfigurationTaskManagementService.GetReport(filtersModel);
            return new OperationDataResult<List<WorkorderCaseModel>>(true, report);
        }
        catch (Exception e)
        {

            return new OperationDataResult<List<WorkorderCaseModel>>(false,
                $"{_localizationService.GetString("ErrorWhileGetReport")}: {e.Message}");
        }
    }

    [HttpGet]
    [Route("{workOrderCaseId:int}")]
    public async Task<OperationDataResult<WorkOrderCaseReadModel>> GetTaskById(int workOrderCaseId)
    {
        return await _backendConfigurationTaskManagementService.GetTaskById(workOrderCaseId);
    }

    [HttpGet]
    [Route("entity-items")]
    public async Task<OperationDataResult<List<string>>> GetItemsEntityListByPropertyId(int propertyId)
    {
        return await _backendConfigurationTaskManagementService.GetItemsEntityListByPropertyId(propertyId);
    }

    [HttpDelete]
    public async Task<OperationResult> DeleteTaskById(int workOrderCaseId)
    {
        return await _backendConfigurationTaskManagementService.DeleteTaskById(workOrderCaseId);
    }

    [HttpPost]
    public async Task<OperationResult> CreateTask([FromForm]WorkOrderCaseCreateModel createModel)
    {
        createModel.Files = HttpContext.Request.Form.Files;
        return await _backendConfigurationTaskManagementService.CreateTask(createModel);
    }
}