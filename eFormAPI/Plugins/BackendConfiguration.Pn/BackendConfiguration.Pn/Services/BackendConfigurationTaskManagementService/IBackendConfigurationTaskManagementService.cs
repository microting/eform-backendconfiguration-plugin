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

using System.Collections.Generic;
using System.Threading.Tasks;
using Infrastructure.Models.TaskManagement;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

public interface IBackendConfigurationTaskManagementService
{
    Task<List<WorkorderCaseModel>> Index(TaskManagementRequestModel filtersModel);

    Task<OperationDataResult<WorkOrderCaseReadModel>> GetTaskById(int workOrderCaseId);

    Task<OperationDataResult<List<string>>> GetItemsEntityListByPropertyId(int propertyId);

    Task<OperationResult> DeleteTaskById(int workOrderCaseId);

    Task<OperationResult> CreateTask(WorkOrderCaseCreateModel createModel);

    Task<OperationResult> UpdateTask(WorkOrderCaseUpdateModel createModel);
}