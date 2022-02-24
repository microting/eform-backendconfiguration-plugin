/*
The MIT License (MIT)

Copyright (c) 2007 - 2021 Microting A/S

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

using BackendConfiguration.Pn.Infrastructure.Helpers;

namespace BackendConfiguration.Pn.Services.BackendConfigurationAssignmentWorkerService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using BackendConfigurationLocalizationService;
    using Infrastructure.Models.AssignmentWorker;
    using Microsoft.EntityFrameworkCore;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eForm.Infrastructure.Data.Entities;
    using Microting.eForm.Infrastructure.Models;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Consts;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
    using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

    public class BackendConfigurationAssignmentWorkerService : IBackendConfigurationAssignmentWorkerService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
        private readonly WorkOrderHelper _workOrderHelper;

        public BackendConfigurationAssignmentWorkerService(
            IEFormCoreService coreHelper,
            IUserService userService,
            BackendConfigurationPnDbContext backendConfigurationPnDbContext,
            IBackendConfigurationLocalizationService backendConfigurationLocalizationService)
        {
            _coreHelper = coreHelper;
            _userService = userService;
            _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
            _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
            _workOrderHelper = new WorkOrderHelper(_coreHelper, _backendConfigurationPnDbContext, _backendConfigurationLocalizationService);
        }

        public async Task<OperationDataResult<List<PropertyAssignWorkersModel>>> GetPropertiesAssignment()
        {
            try
            {
                var assignWorkersModels = new List<PropertyAssignWorkersModel>();
                var query = _backendConfigurationPnDbContext.PropertyWorkers.AsQueryable();
                query = query
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

                if (query.Any())
                {
                    var listWorkerId = await query.Select(x => x.WorkerId).Distinct().ToListAsync();

                    foreach (var workerId in listWorkerId)
                    {
                        var assignments = await query
                            .Where(x => x.WorkerId == workerId)
                            .Select(x => new PropertyAssignmentWorkerModel
                                { PropertyId = x.PropertyId, IsChecked = true })
                            .ToListAsync();
                        assignWorkersModels.Add(new PropertyAssignWorkersModel
                            { SiteId = workerId, Assignments = assignments });
                    }

                    var properties = await _backendConfigurationPnDbContext.Properties
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(x => new PropertyAssignmentWorkerModel { PropertyId = x.Id, IsChecked = false })
                        .ToListAsync();

                    foreach (var propertyAssignWorkersModel in assignWorkersModels)
                    {
                        var missingProperties = properties
                            .Where(x => !propertyAssignWorkersModel.Assignments.Select(y => y.PropertyId)
                                .Contains(x.PropertyId))
                            .ToList();
                        propertyAssignWorkersModel.Assignments.AddRange(missingProperties);
                    }
                }

                return new OperationDataResult<List<PropertyAssignWorkersModel>>(true, assignWorkersModels);
            }
            catch (Exception ex)
            {
                Log.LogException(ex.Message);
                Log.LogException(ex.StackTrace);
                return new OperationDataResult<List<PropertyAssignWorkersModel>>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileObtainingAssignmentsProperties")}: {ex.Message}");
            }
        }

        public async Task<OperationResult> Create(PropertyAssignWorkersModel createModel)
        {
            try
            {
                // var propertyIds = createModel.Assignments
                //     .Select(x => x.PropertyId)
                //     .Distinct()
                //     .ToList();
                List<PropertyWorker> propertyWorkers = new List<PropertyWorker>();
                foreach (var propertyAssignment in createModel.Assignments
                             .Select(propertyAssignmentWorkerModel => new PropertyWorker
                             {
                                 WorkerId = createModel.SiteId,
                                 PropertyId = propertyAssignmentWorkerModel.PropertyId,
                                 CreatedByUserId = _userService.UserId,
                                 UpdatedByUserId = _userService.UserId
                             }))
                {
                    await propertyAssignment.Create(_backendConfigurationPnDbContext);
                    propertyWorkers.Add(propertyAssignment);
                }

                await _workOrderHelper.WorkorderFlowDeployEform(propertyWorkers);

                return new OperationResult(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyAssignmentsCreatingProperties"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileAssignmentsCreatingProperties")}: {e.Message}");
            }
        }

        public async Task<OperationResult> Update(PropertyAssignWorkersModel updateModel)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                // var propertyIds = updateModel.Assignments
                //     .Select(x => x.PropertyId)
                //     .Distinct()
                //     .ToList();

                updateModel.Assignments = updateModel.Assignments.Where(x => x.IsChecked).ToList();

                var assignments = await _backendConfigurationPnDbContext.PropertyWorkers
                    .Where(x => x.WorkerId == updateModel.SiteId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToListAsync();

                var assignmentsForCreate = updateModel.Assignments
                    .Select(x => x.PropertyId)
                    .Where(x => !assignments.Select(y => y.PropertyId).Contains(x))
                    .ToList();
                List<PropertyWorker> propertyWorkers = new List<PropertyWorker>();

                foreach (var propertyAssignment in assignmentsForCreate
                             .Select(propertyAssignmentWorkerModel => new PropertyWorker
                             {
                                 WorkerId = updateModel.SiteId,
                                 PropertyId = propertyAssignmentWorkerModel,
                                 CreatedByUserId = _userService.UserId,
                                 UpdatedByUserId = _userService.UserId
                             }))
                {
                    await propertyAssignment.Create(_backendConfigurationPnDbContext);
                    propertyWorkers.Add(propertyAssignment);
                }

                var assignmentsForDelete = assignments
                    .Where(x => !updateModel.Assignments.Select(y => y.PropertyId).Contains(x.PropertyId))
                    .ToList();

                var eformIdForNewTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "01. New task")
                    .Select(x => x.CheckListId)
                    .FirstAsync();

                var eformIdForOngoingTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "02. Ongoing task")
                    .Select(x => x.CheckListId)
                    .FirstAsync();

                var eformIdForCompletedTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "03. Completed task")
                    .Select(x => x.CheckListId)
                    .FirstAsync();

                foreach (var propertyAssignment in assignmentsForDelete)
                {
                    propertyAssignment.UpdatedByUserId = _userService.UserId;
                    await propertyAssignment.Delete(_backendConfigurationPnDbContext);
                    if (propertyAssignment.EntityItemId != null)
                    {
                        await core.EntityItemDelete((int)propertyAssignment.EntityItemId);
                    }
                }

                if(assignmentsForDelete.Any())
                {
                    await _workOrderHelper.RetractEform(assignmentsForDelete, true);
                    await _workOrderHelper.RetractEform(assignmentsForDelete, false);
                    await _workOrderHelper.RetractEform(assignmentsForDelete, false);
                }

                await _workOrderHelper.WorkorderFlowDeployEform(propertyWorkers);
                return new OperationResult(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyUpdateAssignmentsProperties"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileUpdateAssignmentsProperties")}: {e.Message}");
            }
        }

        public async Task<OperationResult> Delete(int deviceUserId)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContext = core.DbContextHelper.GetDbContext();
                var propertyWorkers = await _backendConfigurationPnDbContext.PropertyWorkers
                    // .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.WorkerId == deviceUserId)
                    .ToListAsync();

                var eformIdForNewTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "01. New task")
                    .Select(x => x.CheckListId)
                    .FirstAsync();

                var eformIdForOngoingTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "02. Ongoing task")
                    .Select(x => x.CheckListId)
                    .FirstAsync();

                var eformIdForCompletedTasks = await sdkDbContext.CheckListTranslations
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Text == "03. Completed task")
                    .Select(x => x.CheckListId)
                    .FirstAsync();


                foreach (var propertyAssignment in propertyWorkers)
                {
                    propertyAssignment.UpdatedByUserId = _userService.UserId;
                    await propertyAssignment.Delete(_backendConfigurationPnDbContext);
                    if (propertyAssignment.EntityItemId != null)
                    {
                        await core.EntityItemDelete((int)propertyAssignment.EntityItemId);
                    }
                }

                await _workOrderHelper.RetractEform(propertyWorkers, true);
                await _workOrderHelper.RetractEform(propertyWorkers, false);
                await _workOrderHelper.RetractEform(propertyWorkers, false);
                foreach (var assignment in propertyWorkers)
                {
                    await assignment.Delete(_backendConfigurationPnDbContext);
                }

                return new OperationResult(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyDeleteAssignmentsProperties"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhilDeleteAssignmentsProperties")}: {e.Message}");
            }
        }



    }
}