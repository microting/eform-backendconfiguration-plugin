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
using BackendConfiguration.Pn.Infrastructure.Models;
using BackendConfiguration.Pn.Services.RebusService;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.eFormCaseTemplateBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.TimePlanningBase.Infrastructure.Data;
using Rebus.Bus;

namespace BackendConfiguration.Pn.Services.BackendConfigurationAssignmentWorkerService;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfigurationLocalizationService;
using Infrastructure.Models.AssignmentWorker;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

public class BackendConfigurationAssignmentWorkerService : IBackendConfigurationAssignmentWorkerService
{
    private readonly IEFormCoreService _coreHelper;
    private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
    private readonly IUserService _userService;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
    private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;
    private readonly TimePlanningPnDbContext _timePlanningDbContext;
    private readonly CaseTemplatePnDbContext _caseTemplatePnDbContext;
    private readonly IBus _bus;

    public BackendConfigurationAssignmentWorkerService(
        IEFormCoreService coreHelper,
        IUserService userService,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        IBackendConfigurationLocalizationService backendConfigurationLocalizationService,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        TimePlanningPnDbContext timePlanningDbContext,
        CaseTemplatePnDbContext caseTemplatePnDbContext,
        IRebusService rebusService)
    {
        _coreHelper = coreHelper;
        _userService = userService;
        _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
        _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
        _timePlanningDbContext = timePlanningDbContext;
        _caseTemplatePnDbContext = caseTemplatePnDbContext;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        _bus = rebusService.GetBus();
    }

    public async Task<OperationDataResult<List<PropertyAssignWorkersModel>>> GetPropertiesAssignment(List<int> propertyIds)
    {
        try
        {
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var assignWorkersModels = new List<PropertyAssignWorkersModel>();
            var query = _backendConfigurationPnDbContext.PropertyWorkers.AsQueryable();
            query = query
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            if (propertyIds != null && propertyIds.Any())
            {
                query = query.Where(x => propertyIds.Contains(x.PropertyId));
            }

            if (query.Count() > 0)
            {
                var listWorkerId = await query.Select(x => new PropertyWorker
                {
                    WorkerId = x.WorkerId,
                    TaskManagementEnabled = x.TaskManagementEnabled
                }).Distinct().ToListAsync().ConfigureAwait(false);

                foreach (var workerId in listWorkerId)
                {
                    var assignments = await query
                        .Where(x => x.WorkerId == workerId.WorkerId)
                        .Select(x => new PropertyAssignmentWorkerModel
                            { PropertyId = x.PropertyId, IsChecked = true })
                        .ToListAsync().ConfigureAwait(false);

                    foreach (var assignmentWorkerModel in assignments)
                    {
                        var numberOfAssignements = await _backendConfigurationPnDbContext.AreaRulePlannings
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.Status)
                            .Where(x => x.PropertyId == assignmentWorkerModel.PropertyId)
                            .Where(x => x.PlanningSites.Where(y => y.WorkflowState != Constants.WorkflowStates.Removed && y.SiteId == workerId.WorkerId).Select(y => y.SiteId).Any())
                            .CountAsync().ConfigureAwait(false);

                        assignmentWorkerModel.IsLocked = numberOfAssignements > 0;
                        assignmentWorkerModel.NumberOfTasksAssigned = numberOfAssignements;

                        // var siteName = await sdkDbContext.Sites
                        //     .Where(x => x.Id == workerId.WorkerId)
                        //     .Select(x => x.Name)
                        //     .SingleOrDefaultAsync().ConfigureAwait(false);

                        var siteName = await sdkDbContext.Sites
                            .Where(x => x.Id == workerId.WorkerId)
                            .Select(x => x.Name)
                            .SingleOrDefaultAsync().ConfigureAwait(false);


                        var numberOfWorkOrderCases = await _backendConfigurationPnDbContext.WorkorderCases
                            .Join(_backendConfigurationPnDbContext.PropertyWorkers,
                                workorderCase => workorderCase.PropertyWorkerId,
                                propertyWorker => propertyWorker.Id,
                                (workorderCase, propertyWorker) => new
                                {
                                    workorderCase.Id,
                                    workorderCase.LeadingCase,
                                    workorderCase.WorkflowState,
                                    workorderCase.LastAssignedToName,
                                    workorderCase.CaseStatusesEnum,
                                    propertyWorker.PropertyId
                                })
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.Completed)
                            .Where(x => x.LeadingCase == true)
                            .Where(x => x.LastAssignedToName == siteName)
                            .Where(x => x.PropertyId == assignmentWorkerModel.PropertyId)
                            //.Where(x => x.LastAssignedToName == siteName)
                            .ToListAsync();

                        assignmentWorkerModel.IsLocked = assignmentWorkerModel.IsLocked ? assignmentWorkerModel.IsLocked : numberOfWorkOrderCases.Count() > 0;
                        assignmentWorkerModel.NUmberOfWorkOrderCasesAssigned = numberOfWorkOrderCases.Count();

                    }

                    assignWorkersModels.Add(new PropertyAssignWorkersModel
                        { SiteId = workerId.WorkerId, Assignments = assignments, TaskManagementEnabled = workerId.TaskManagementEnabled });
                }

                var properties = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Select(x => new PropertyAssignmentWorkerModel { PropertyId = x.Id, IsChecked = false })
                    .ToListAsync().ConfigureAwait(false);

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
    public async Task<OperationDataResult<List<PropertyAssignWorkersModel>>> GetSimplePropertiesAssignment(List<int> propertyIds)
    {
        try
        {
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var assignWorkersModels = new List<PropertyAssignWorkersModel>();
            var query = _backendConfigurationPnDbContext.PropertyWorkers.AsQueryable();
            query = query
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            if (propertyIds != null && propertyIds.Any())
            {
                query = query.Where(x => propertyIds.Contains(x.PropertyId));
            }

            if (query.Count() > 0)
            {
                var listWorkerId = await query.Select(x => new PropertyWorker
                {
                    WorkerId = x.WorkerId,
                    TaskManagementEnabled = x.TaskManagementEnabled
                }).Distinct().ToListAsync().ConfigureAwait(false);

                foreach (var workerId in listWorkerId)
                {
                    var assignments = await query
                        .Where(x => x.WorkerId == workerId.WorkerId)
                        .Select(x => new PropertyAssignmentWorkerModel
                            { PropertyId = x.PropertyId, IsChecked = true })
                        .ToListAsync().ConfigureAwait(false);

                    assignWorkersModels.Add(new PropertyAssignWorkersModel
                        { SiteId = workerId.WorkerId, Assignments = assignments, TaskManagementEnabled = workerId.TaskManagementEnabled });
                }

                var properties = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Select(x => new PropertyAssignmentWorkerModel { PropertyId = x.Id, IsChecked = false })
                    .ToListAsync().ConfigureAwait(false);

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
        var core = await _coreHelper.GetCore();

        var result = await BackendConfigurationAssignmentWorkerServiceHelper
            .Create(createModel, core, _userService, _backendConfigurationPnDbContext,
                _caseTemplatePnDbContext, _backendConfigurationLocalizationService, _bus)
            .ConfigureAwait(false);

        return new OperationResult(result.Success, _backendConfigurationLocalizationService.GetString(result.Message));
    }

    public async Task<OperationResult> Update(PropertyAssignWorkersModel updateModel)
    {
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var result = await BackendConfigurationAssignmentWorkerServiceHelper
            .Update(updateModel, core, _userService, _backendConfigurationPnDbContext, _caseTemplatePnDbContext,
                _backendConfigurationLocalizationService, _bus, _itemsPlanningPnDbContext)
            .ConfigureAwait(false);

        return new OperationResult(result.Success, _backendConfigurationLocalizationService.GetString(result.Message));
    }

    public async Task<OperationResult> Delete(int deviceUserId)
    {
        try
        {
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var propertyWorkers = await _backendConfigurationPnDbContext.PropertyWorkers
                .Where(x => x.WorkerId == deviceUserId)
                .ToListAsync().ConfigureAwait(false);
            var site = await sdkDbContext.Sites
                .Where(x => x.Id == deviceUserId)
                .SingleOrDefaultAsync().ConfigureAwait(false);

            foreach (var propertyAssignment in propertyWorkers)
            {
                propertyAssignment.UpdatedByUserId = _userService.UserId;
                await propertyAssignment.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                if (propertyAssignment.EntityItemId != null)
                {
                    await core.EntityItemDelete((int)propertyAssignment.EntityItemId).ConfigureAwait(false);
                }

                var property = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.Id == propertyAssignment.PropertyId)
                    .SingleOrDefaultAsync().ConfigureAwait(false);

                var entityItems = await sdkDbContext.EntityItems
                    .Where(x => x.EntityGroupId == property.EntitySelectListDeviceUsers)
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
                await BackendConfigurationAssignmentWorkerServiceHelper
                    .DeleteAllEntriesForPropertyAssignment(propertyAssignment, core, property, sdkDbContext,
                        _caseTemplatePnDbContext, _backendConfigurationPnDbContext, _itemsPlanningPnDbContext)
                    .ConfigureAwait(false);
            }

            await WorkOrderHelper.RetractEform(propertyWorkers, true, core, _userService.UserId, _backendConfigurationPnDbContext).ConfigureAwait(false);
            await WorkOrderHelper.RetractEform(propertyWorkers, false, core, _userService.UserId, _backendConfigurationPnDbContext).ConfigureAwait(false);
            await WorkOrderHelper.RetractEform(propertyWorkers, false, core, _userService.UserId, _backendConfigurationPnDbContext).ConfigureAwait(false);
            foreach (var assignment in propertyWorkers)
            {
                await assignment.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
            }

            if (_timePlanningDbContext.AssignedSites.Any(x => x.SiteId == site.MicrotingUid && x.WorkflowState != Constants.WorkflowStates.Removed))
            {
                var assignmentForDeletes = await _timePlanningDbContext.AssignedSites.Where(x =>
                    x.SiteId == site.MicrotingUid && x.WorkflowState != Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);

                foreach (var assignmentForDelete in assignmentForDeletes)
                {
                    await assignmentForDelete.Delete(_timePlanningDbContext).ConfigureAwait(false);
                    if (assignmentForDelete.CaseMicrotingUid != null)
                    {
                        await core.CaseDelete((int) assignmentForDelete.CaseMicrotingUid).ConfigureAwait(false);
                    }
                }
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

    public async Task<OperationDataResult<List<DeviceUserModel>>> IndexDeviceUser(PropertyWorkersFiltrationModel requestModel)
    {
        try
        {
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            // var deviceUsers = new List<DeviceUser>();

            var sitesQuery = sdkDbContext.Sites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

            try
            {
                sitesQuery = QueryHelper.AddFilterAndSortToQuery(sitesQuery, requestModel, new List<string> { "Name" });
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            var deviceUsers = await sitesQuery
                .Select(x => new DeviceUserModel
                {
                    //UserFirstName = x.SiteWorkers.FirstOrDefault(y => y.WorkflowState != Constants.WorkflowStates.Removed).Worker.FirstName,
                    //UserLastName = x.SiteWorkers.FirstOrDefault(y => y.WorkflowState != Constants.WorkflowStates.Removed).Worker.LastName,
                    LanguageId = x.LanguageId,
                    // OtpCode = x.Units
                    //     .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                    //     .Select(y => y.OtpCode)
                    //     .FirstOrDefault(),
                    SiteId = x.Id,
                    SiteUid = x.MicrotingUid,
                    SiteName = x.Name,
                    //WorkerUid = x.SiteWorkers.FirstOrDefault(y => y.WorkflowState != Constants.WorkflowStates.Removed).Worker.MicrotingUid,
                    Language = sdkDbContext.Languages.Where(y => y.Id == x.LanguageId).Select(y => y.Name).SingleOrDefault() ?? "Danish",
                    LanguageCode = sdkDbContext.Languages.Where(y => y.Id == x.LanguageId).Select(y => y.LanguageCode).SingleOrDefault() ?? "da",
                    IsLocked = x.IsLocked
                })
                .ToListAsync().ConfigureAwait(false);

            // var siteUids = await sitesQuery.Select(x => x.MicrotingUid).ToListAsync().ConfigureAwait(false);
            // var siteIds = await sitesQuery.Select(x => x.Id).ToListAsync().ConfigureAwait(false);

            var timeRegistrationEnabledSites =await
                _timePlanningDbContext.AssignedSites.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.SiteId).ToListAsync().ConfigureAwait(false);

            foreach (var deviceUserModel in deviceUsers)
            {
                var unit = await sdkDbContext.Units
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync(x => x.SiteId == deviceUserModel.SiteId);
                if (unit != null)
                {
                    deviceUserModel.Version = unit.eFormVersion;
                    deviceUserModel.OsVersion = unit.OsVersion;
                    deviceUserModel.Model = unit.Model;
                    deviceUserModel.Manufacturer = unit.Manufacturer;
                    deviceUserModel.CustomerNo = unit.CustomerNo;
                    deviceUserModel.OtpCode = unit.OtpCode;
                    deviceUserModel.UnitId = unit.MicrotingUid;
                }

                var siteWorker = await sdkDbContext.SiteWorkers.FirstOrDefaultAsync(x => x.SiteId == deviceUserModel.SiteId);
                if (siteWorker != null)
                {
                    var worker = await sdkDbContext.Workers.FirstAsync(x => x.Id == siteWorker.WorkerId);
                    deviceUserModel.UserFirstName = worker.FirstName;
                    deviceUserModel.UserLastName = worker.LastName;
                    deviceUserModel.WorkerUid = worker.MicrotingUid;
                    deviceUserModel.TimeRegistrationEnabled =
                        timeRegistrationEnabledSites.Any(x => x == deviceUserModel.SiteUid);
                }
                else
                {
                    Console.WriteLine(deviceUserModel.SiteId);
                }

                deviceUserModel.TaskManagementEnabled = _backendConfigurationPnDbContext.PropertyWorkers.Any(x =>
                    x.WorkflowState != Constants.WorkflowStates.Removed
                    && x.WorkerId == deviceUserModel.SiteId
                    && x.TaskManagementEnabled == true);

                var propertyIds = await _backendConfigurationPnDbContext.PropertyWorkers
                    .Where(x =>
                        x.WorkerId == deviceUserModel.SiteId
                        && x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.PropertyId).ToListAsync().ConfigureAwait(false);

                var properties = _backendConfigurationPnDbContext.Properties
                    .Where(x => propertyIds.Contains(x.Id))
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .OrderBy(x => x.Name)
                    .Select(x => x.Name)
                    .ToList();

                deviceUserModel.PropertyNames = string.Join(", ", properties);
                deviceUserModel.PropertyIds = propertyIds;

                var numberOfAssignements = await _backendConfigurationPnDbContext.AreaRulePlannings
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Status)
                    .Where(x => propertyIds.Contains(x.PropertyId))
                    .Where(x => x.PlanningSites.Where(y => y.WorkflowState != Constants.WorkflowStates.Removed && y.SiteId == deviceUserModel.SiteId).Select(y => y.SiteId).Any())
                    .CountAsync().ConfigureAwait(false);

                deviceUserModel.IsBackendUser = deviceUserModel.IsLocked;

                deviceUserModel.IsLocked = deviceUserModel.IsLocked ? deviceUserModel.IsLocked : numberOfAssignements > 0;

                // var siteName = await sdkDbContext.Sites
                //     .Where(x => x.Id == deviceUserModel.SiteId)
                //     .Select(x => x.Name)
                //     .SingleOrDefaultAsync().ConfigureAwait(false);

                var numberOfWorkOrderCases = await _backendConfigurationPnDbContext.WorkorderCases
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.Completed)
                    .Where(x => x.LeadingCase == true)
                    .Where(x => x.LastAssignedToName == deviceUserModel.SiteName)
                    .CountAsync();

                deviceUserModel.IsLocked = deviceUserModel.IsLocked ? deviceUserModel.IsLocked : numberOfWorkOrderCases > 0;
                deviceUserModel.HasWorkOrdersAssigned = numberOfWorkOrderCases > 0;
            }

            if (requestModel.PropertyIds != null)
            {
                if (requestModel.PropertyIds.Any())
                {
                    deviceUsers = deviceUsers
                        .Where(x => x.PropertyIds.Any(y => requestModel.PropertyIds.Contains(y))).ToList();
                }
            }

            return new OperationDataResult<List<DeviceUserModel>>(true, deviceUsers);
        }
        catch (Exception ex)
        {
            return new OperationDataResult<List<DeviceUserModel>>(false, _backendConfigurationLocalizationService.GetStringWithFormat("ErrorWhileGetDeviceUsers") + " " + ex.Message);
        }
    }

    public async Task<OperationResult> UpdateDeviceUser(DeviceUserModel deviceUserModel)
    {
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.UpdateDeviceUser(deviceUserModel, core,
            _userService.UserId, _backendConfigurationPnDbContext,
            _timePlanningDbContext);

        return new OperationResult(result.Success, _backendConfigurationLocalizationService.GetString(result.Message));
    }

    public async Task<OperationDataResult<int>> CreateDeviceUser(DeviceUserModel deviceUserModel)
    {
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var result = await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core,
            _userService.UserId,
            _timePlanningDbContext);

        return new OperationDataResult<int>(result.Success,
            _backendConfigurationLocalizationService.GetString(result.Message), result.Model);
    }

}